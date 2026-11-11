using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// 튜토리얼 씬(흰 훈련장)에 게임 요소를 배치하는 에디터 도구 (2026-10-18).
/// 메뉴: Tools > 튜토리얼 > 게임 요소 배치 / CLI: -executeMethod TutorialGameplayBuilder.ApplyBatch (Unity 에디터를 꺼야 함)
/// 하는 일 (순서대로)
///  1. 플레이어 리그: 씬에 Player가 없으면 DollManiaHouse 씬에서 복사한다 (Player, Main Camera, 플레이어 시네머신 카메라, Canvas·HUD,
///     일시정지·승패(GameProgressManager), EventSystem, 설정, 얼룩 신선도 매니저). 인트로 카메라·리플레이·디버그는 복사하지 않는다.
///     이미 있으면 손대지 않고 위치·방향만 PlayerStart에 맞춘다. HUD의 얼룩·인형 개수 표시는 튜토리얼에선 꺼 둔다 (게이지 첫 등장 7-2는 따로).
///  2. TutorialMap 손질: 빈대가 될 소품 5개와 외계인 임시 모델을 끄고, 나머지 소품을 잡기 대상(태그 Prop, 레이어 Interactable)으로 만든다.
///  3. NavMesh 굽기: 루트 "NavMesh"의 NavMeshSurface (천장은 빼고 바닥에서 3m까지). 빈대·플레이어는 잠시 끄고 굽는다.
///  4. TutorialGameplay 아래를 매번 새로 만든다: 빈대 6마리 + 웨이포인트, 문 앞 미리 배치 얼룩(나이 고정), 얼룩 문 조건,
///     연습 구역 입장 전 빈대 대기, [임시] 진행 문 자동 열림. → TutorialGameplay 아래는 손으로 고치지 않기. 값은 아래 표에서 고치고 다시 실행.
///  5. 빌드 설정에 Tutorial.unity 추가, 검사(웨이포인트 NavMesh·경로, 웃음소리 거리) 후 저장.
/// TutorialSceneBuilder(맵 재생성)가 끝에서 ApplyToScene을 자동 호출 → 맵을 다시 만들어도 게임 요소가 다시 붙는다.
/// 좌표는 「3d 모델링」 문서의 평면 배치 좌표(Blender 평면, x 동쪽·y 북쪽) 그대로 적고, Unity 변환 축은 맵에서 확인한다.
/// </summary>
public static class TutorialGameplayBuilder
{
    const string ScenePath = "Assets/Scenes/Tutorial.unity";
    const string RigSourcePath = "Assets/Scenes/DollManiaHouse.unity";
    const string StainPrefabPath = "Assets/Prefabs/Stain.prefab";
    const string DollPrefabPath = "Assets/Prefabs/doll_move.prefab";
    const string FreshnessSettingsPath = "Assets/Settings/StainFreshness_Default.asset";
    const string MapRootName = "TutorialMap";
    const string GameplayRootName = "TutorialGameplay";
    const string NavRootName = "NavMesh";

    // DollManiaHouse에서 복사할 루트 (서로 참조하므로 한 번에 복사해야 참조가 유지된다)
    static readonly string[] RigRoots = { "Player", "Main Camera", "CinemachineGroup", "Canvas", "GameHUD", "GameProgressManager", "EventSystem", "SettingsManager", "StainFreshnessManager" };
    // 복사 후 지울 것: 인트로 달리·리플레이·문 열림 카메라 (CameraManagement를 안 가져오므로 우선순위 경쟁이 없다)
    static readonly string[] RigDrop = { "Dolly Spline", "PanoramaCinemachineCamera", "ReplayCinemachineCamera", "DoorOpenCinemachineCamera" };

    // ===================== 배치 표 =====================

    class Bindae
    {
        public string name;          // TutorialGameplay/Bindae 아래 이름
        public string source;        // TutorialMap 안의 원본 (같은 메시·재질·충돌 메시로 만들고 원본은 끈다)
        public string zone;          // 웨이포인트 묶음 이름
        public Vector2[] waypoints;  // Blender 평면 좌표. 이동 끔이면 원본 자리 하나
        public bool catchable = true, laugh, stains, move, countsTowardGoal = true;
        public float escapeDistance = 5f, tokenInterval = 5f, grace = 10f, speed = 1.8f, nearMoveChance = 0.2f;
        public int startTokens, maxTokens = 3;
        public bool startAtFirst;    // 0번 웨이포인트에서 시작 (자리 섞기와 함께, 2026-11-02). 끄면 플레이어에게서 가장 먼 곳
    }

    // 4 방 A 곁눈질 찾기 (2026-11-02, 「코드 설계 - 튜토리얼 방 A 곁눈질 찾기·방 B 정답 위치」)
    // 같은 상자 3개를 북쪽 벽에 한 줄로. 빈대는 실행할 때 3자리 중 무작위(TutorialSlotShuffle), 등 돌린 사이 한 번만 줄 앞으로 벗어난다
    static readonly Vector2[] RoomASlots = { new Vector2(23.6f, 21.75f), new Vector2(24.75f, 21.75f), new Vector2(25.9f, 21.75f) };
    static readonly Vector2[] RoomAOffSpots = { new Vector2(24.1f, 20.45f), new Vector2(25.4f, 20.45f) };   // 줄 앞 1.3m, 통로(문 폭 y 18.83~20.18) 밖
    // 5 방 B: 빈대와 섞을 미끼 상자 (의자는 섞지 않음). 세 자리 모두 서쪽 벽에서 3.5m 이상 (웃음소리 거리 규칙)
    static readonly string[] RoomBShuffleDecoys = { "TK_Prop_Box_L.001", "TK_Prop_Box_L.003" };
    static readonly Vector2[] RoomBSlotsForCheck = { new Vector2(35.3f, 17.7f), new Vector2(38.0f, 18.1f), new Vector2(35.5f, 21.4f) };   // tutorial_layout.json의 L.001·L.002·L.003 (검사용)

    // 연습 구역 웨이포인트: 입구(51, 25.5)에서 3.5m 이상, 남은 소품에서 1.4m 이상 (원래 쓰레기통·스툴 자리 2곳 포함)
    static readonly Vector2[] PracticeWaypoints =
    {
        new Vector2(49.0f, 29.2f), new Vector2(55.8f, 31.8f), new Vector2(53.5f, 33.2f),
        new Vector2(49.3f, 32.9f), new Vector2(54.0f, 28.6f), new Vector2(51.2f, 33.0f),
    };

    static readonly Bindae[] Bindaes =
    {
        // 3 격리실 본모습: 보면 굳음 시연용. 잡기 대상 아님(유리 너머 잡기 방지), 승리 집계에서 빠짐, 얼룩·웃음 없음.
        // 플레이어가 관찰실(5m 안)에 있어도 움직여야 하므로 escapeDistance 0. 컵 시연(3-5~3-8)은 진행 관리에서 SetFrozen·목표 지정으로 따로
        new Bindae { name = "Bindae_Isolation", source = "CH_Alien_Temp", zone = "Isolation", catchable = false, countsTowardGoal = false, move = true,
            escapeDistance = 0f, nearMoveChance = 0f, tokenInterval = 2.5f, grace = 0f, startTokens = 1, speed = 1.2f,
            waypoints = new[] { new Vector2(24.3f, 14.0f), new Vector2(27.0f, 13.6f), new Vector2(27.2f, 16.6f), new Vector2(24.2f, 16.8f), new Vector2(25.6f, 15.2f) } },
        // 4 방 A: 상자 3개 한 줄 중 하나 → 곁눈질 찾기 (2026-11-02, 원래는 상자 하나뿐인 무조건 성공 포획).
        // 이동권 1개·충전 없음 = 한 번만 벗어남. 도망 거리 0 = 가까이 있어도 안 볼 때 움직임. 방에 들어와 대사가 끝날 때까지 얼려 둠 (진행 관리 startFrozen)
        // 웨이포인트 0번 = 시작 칸 (자리 섞기가 옮김), 1·2번 = 벗어날 자리
        new Bindae { name = "Bindae_RoomA", source = "TK_Prop_Box", zone = "RoomA", move = true, startAtFirst = true,
            escapeDistance = 0f, nearMoveChance = 0f, tokenInterval = 100000f, grace = 0f, startTokens = 1, maxTokens = 1, speed = 1.4f,
            waypoints = new[] { RoomASlots[2], RoomAOffSpots[0], RoomAOffSpots[1] } },
        // 5 방 B: 상자 셋 + 의자 중 하나, 웃음소리로 찾기. 얼룩 끔.
        // 2026-11-02: 이동 끔 — 켜 두면 벽 너머에서 빈 바닥 웨이포인트로 걸어가 "가운데 혼자 선 상자"가 됨 (사용자 제보 "가운데 것을 먼저 눌러 봄").
        // 시작 자리는 상자 3자리 중 무작위 (자리 섞기, 미끼 L.001·L.003과)
        new Bindae { name = "Bindae_RoomB", source = "TK_Prop_Box_L.002", zone = "RoomB", laugh = true, startAtFirst = true,
            waypoints = new[] { new Vector2(38.0f, 18.1f) } },
        // 6 문 앞: 얼룩 줄 끝의 의자. 이동·얼룩·웃음 끔 (얼룩 줄이 유일한 단서)
        new Bindae { name = "Bindae_StainChair", source = "TK_Prop_Chair_L.001", zone = "DoorFront", waypoints = new[] { new Vector2(47.25f, 21.75f) } },
        // 7 연습 구역 두 마리: 같은 모양 미끼가 있는 쓰레기통·스툴. 입장 전엔 얼어 있음 (TutorialZoneActivator)
        new Bindae { name = "Bindae_Practice_1", source = "TK_Prop_TrashBin_L.001", zone = "Practice", laugh = true, stains = true, move = true, escapeDistance = 4f, waypoints = PracticeWaypoints },
        new Bindae { name = "Bindae_Practice_2", source = "TK_Prop_Stool_L.001", zone = "Practice", laugh = true, stains = true, move = true, escapeDistance = 4f, waypoints = PracticeWaypoints },
    };

    // 6 문 앞 얼룩 줄: 입구 → 의자, 오래된 것(갈색·잠든 얼굴)에서 갓 생긴 것(노랑·웃는 얼굴)으로. 나이 고정
    static readonly Vector2 StainLineStart = new Vector2(44.25f, 19.35f);
    static readonly Vector2 StainLineControl = new Vector2(45.7f, 19.55f);
    static readonly Vector2 StainLineEnd = new Vector2(46.6f, 21.15f);
    const int StainCount = 6;
    const float StainOldestAge = 34f, StainNewestAge = 1f;   // 단계 0~6초 E23 / 6~20초 E9 / 20초~ E35 기준 → E35 3개, E9 2개, E23 1개

    // [임시] 진행 문: (문 이름, 지나기 전에 잡아야 할 빈대)
    static readonly (string door, string[] required)[] ProgressDoors =
    {
        ("Door_1_Wake", new string[0]),
        ("Door_4_RoomA_In", new string[0]),
        ("Door_4_RoomA_Out", new[] { "Bindae_RoomA" }),
        ("Door_5_RoomB_In", new string[0]),
        ("Door_5_RoomB_Out", new[] { "Bindae_RoomB" }),
        ("Door_6_Stain_In", new string[0]),
        ("Door_7_Practice_In", new string[0]),
    };
    const string StainDoorName = "Door_6_StainDoor";
    const string PracticeDoorName = "Door_7_Practice_In";

    // 구역 (Blender 평면 사각형 x0, y0, x1, y1)
    static readonly Vector4 PracticeRect = new Vector4(46.5f, 26.2f, 57f, 36f);   // 입구 문턱(25.5)에서 0.7m 들어와야 입장
    static readonly Vector2 PracticeEntrance = new Vector2(51f, 25.5f);
    static readonly Vector4 ExitRect = new Vector4(48.3f, 18.2f, 52.3f, 20.8f);   // ㄱ자 복도 1구간 (연습 구역 입구에서 되돌아 약 5m)
    const string NextSceneName = "DollManiaHouse";

    // ===== CCTV (2026-10-21) =====
    const string CctvRtPath = "Assets/Scenes/Tutorial/RT_CCTV.renderTexture";
    const string CctvMatPath = "Assets/Scenes/Tutorial/M_CCTVScreen.mat";
    static readonly Vector2 CctvLookAt = new Vector2(25.5f, 15.0f);   // 격리실 가운데 (Blender 평면)
    const float CctvLookHeight = 0.3f, CctvFov = 72f;
    // 모니터 화면이 거울처럼 뒤집혀 보이면 여기만 바꾸고 다시 실행
    const bool CctvFlipX = false, CctvFlipY = false;

    // ===== 진행 관리 (2026-10-19) =====
    const string ScriptAssetPath = "Assets/Data/Tutorial/tutorial_script.json";
    const string SubtitleFontPath = "Assets/TextMesh Pro/Fonts/Maplestory OTF Light SDF.asset";
    // 대사 데이터의 enterZone target (Blender 평면 사각형 x0, y0, x1, y1)
    static readonly (string name, Vector4 rect)[] Zones =
    {
        ("AnteRoom", new Vector4(19.6f, 18.2f, 22.4f, 21f)),      // 4-1 전실
        ("RoomA", new Vector4(22.9f, 18.3f, 26.7f, 22.2f)),       // 4-2 방 A 안 (문턱에서 0.4m, 2026-11-02)
        ("ABCorridor", new Vector4(27.3f, 18.1f, 31.2f, 20.9f)),  // 5-1 A-B 복도 (방 B 입구 앞)
        ("DoorFront", new Vector4(43.8f, 18.1f, 47.9f, 22.4f)),   // 6-1 문 앞
        ("LCorridor2", new Vector4(49.6f, 21.2f, 52.4f, 25.2f)),  // 7-4 연습 판 뒤 되돌아가는 ㄱ자 복도 2구간
    };
    // 대사 데이터의 walk target (복도 중심선, Blender 평면)
    static readonly (string name, Vector2[] pts)[] Paths =
    {
        ("Corridor", new[] { new Vector2(3f, 6.2f), new Vector2(3f, 15f), new Vector2(18f, 15f) }),          // 2 복도 (대사 0·7·14·21m)
        ("LCorridor", new[] { new Vector2(48.2f, 19.5f), new Vector2(51f, 19.5f), new Vector2(51f, 25.5f) }), // 6-3·7-1 ㄱ자 복도
    };
    const float RoomBWestWall = 31.5f;
    const float LaughDistance = 3.5f, RearmDistance = 4.5f;

    // ===================== 진입점 =====================

    [MenuItem("Tools/튜토리얼/게임 요소 배치")]
    static void ApplyMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ApplyToScene(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        VerifySavedStains();
    }

    /// <summary>CLI 전용: 적용 → 저장 → 종료</summary>
    public static void ApplyBatch()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ApplyToScene(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("씬 저장 실패");
            AssetDatabase.SaveAssets();
            VerifySavedStains();
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError($"[튜토리얼 배치] 실패: {e}");
            EditorApplication.Exit(1);
        }
    }

    // 저장한 씬을 다시 열어 문 앞 얼룩이 전부 문 앞 방 안에 있는지 본다 — 메모리에선 맞는데 저장 파일만 틀어지는 문제를 잡으려고
    static void VerifySavedStains()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var map = Root(scene, MapRootName).transform;
        var ax = DetectAxes(map);
        var bad = new List<string>();
        int n = 0;
        foreach (var s in Object.FindObjectsByType<StainFreshness>(FindObjectsSortMode.None))
        {
            if (!s.name.StartsWith("Stain_DoorFront_")) continue;
            n++;
            Vector3 w = s.transform.position;
            var bp = new Vector2(w.x / ax.sx, w.z / ax.sz);
            if (!(bp.x > 43.5f && bp.x < 48f && bp.y > 18f && bp.y < 22.5f)) bad.Add($"{s.name} ({bp.x:0.00}, {bp.y:0.00})");
        }
        if (n != StainCount || bad.Count > 0) Debug.LogError($"[튜토리얼 배치] 저장된 문 앞 얼룩 이상: {n}개, 방 밖 {string.Join(", ", bad)}");
        else Log($"저장 확인: 문 앞 얼룩 {n}개 모두 제자리");
    }

    /// <summary>열려 있는 튜토리얼 씬에 적용 (저장은 호출한 쪽에서). TutorialSceneBuilder가 맵 재생성 끝에 부른다</summary>
    public static void ApplyToScene(Scene scene)
    {
        SceneManager.SetActiveScene(scene);
        var map = Root(scene, MapRootName)?.transform ?? throw new Exception("TutorialMap이 없음 — 먼저 Tools > 튜토리얼 씬 생성");
        var ax = DetectAxes(map);
        var start = Root(scene, "PlayerStart")?.transform ?? throw new Exception("PlayerStart가 없음");

        // 임시 미리보기 카메라는 플레이어 리그가 들어오면 지운다
        var temp = start.Find("TempPreviewCamera");
        if (temp != null) Object.DestroyImmediate(temp.gameObject);

        EnsureRig(scene);
        PlaceRig(scene, start);
        EnsureFreshnessManager(scene);

        var old = Root(scene, GameplayRootName);
        if (old != null) Object.DestroyImmediate(old);

        PrepareMap(map);
        BakeNavMesh(scene, ax);

        var dolls = BuildGameplay(scene, map, ax, out var stains);
        WireProgressManager(scene, map);
        AddToBuildSettings();

        var report = Validate(dolls, ax);
        EditorSceneManager.MarkSceneDirty(scene);
        Log($"완료 — 빈대 {dolls.Count}마리, 문 앞 얼룩 {stains.Count}개\n{report}");
    }

    // ===================== 1. 플레이어 리그 =====================

    static void EnsureRig(Scene scene)
    {
        if (Root(scene, "Player") != null) return;

        var src = EditorSceneManager.OpenScene(RigSourcePath, OpenSceneMode.Additive);
        var copied = new List<GameObject>();
        try
        {
            // 원본 루트들을 임시 부모 하나로 묶어 통째로 복제 → 루트끼리의 참조(카메라·HUD·버튼)가 복제본끼리로 이어진다
            var bundle = new GameObject("__RigBundle");
            SceneManager.MoveGameObjectToScene(bundle, src);
            foreach (var n in RigRoots)
            {
                var g = Root(src, n);
                if (g == null) { Warn($"리그 원본에 '{n}'이 없음"); continue; }
                g.transform.SetParent(bundle.transform, true);
            }

            SceneManager.SetActiveScene(scene);
            var clone = Object.Instantiate(bundle);
            SceneManager.MoveGameObjectToScene(clone, scene);
            foreach (var t in clone.transform.Cast<Transform>().ToList())
            {
                t.SetParent(null, true);
                copied.Add(t.gameObject);
            }
            Object.DestroyImmediate(clone);

            foreach (var g in copied)
                foreach (var n in RigDrop)
                {
                    var t = g.transform.Find(n);
                    if (t != null) Object.DestroyImmediate(t.gameObject);
                }

            int cleared = ClearForeignRefs(copied, scene);
            if (cleared > 0) Log($"리그 복사: 다른 씬을 가리키던 참조 {cleared}개를 비움");
        }
        finally
        {
            EditorSceneManager.CloseScene(src, true);   // 원본 씬은 저장하지 않고 닫는다 (묶느라 바꾼 부모가 버려짐)
        }

        // 튜토리얼에선 얼룩·인형 개수와 경고 문구를 숨긴다 (게이지 첫 등장 7-2는 진행 관리에서 따로)
        var hud = Object.FindFirstObjectByType<GameHUD>();
        if (hud != null)
        {
            var so = new SerializedObject(hud);
            foreach (var p in new[] { "stainCountText", "dollCountText", "warningMessage" })
            {
                var prop = so.FindProperty(p);
                if (prop == null || prop.objectReferenceValue == null) continue;
                var target = prop.objectReferenceValue is Component c ? c.gameObject : prop.objectReferenceValue as GameObject;
                if (target != null) target.SetActive(false);
                prop.objectReferenceValue = null;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        Log($"플레이어 리그 복사: {string.Join(", ", copied.Select(g => g.name))}");
    }

    // 복사본이 원본 씬 오브젝트를 가리키면(예: 문 열림 대상 문) 저장 때 깨지므로 비운다
    static int ClearForeignRefs(List<GameObject> roots, Scene scene)
    {
        int n = 0;
        foreach (var r in roots)
            foreach (var comp in r.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                var so = new SerializedObject(comp);
                var it = so.GetIterator();
                bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    GameObject go = it.objectReferenceValue as GameObject ?? (it.objectReferenceValue as Component)?.gameObject;
                    if (go == null || !go.scene.IsValid() || go.scene == scene) continue;
                    Log($"  참조 비움: {comp.GetType().Name}.{it.propertyPath} → {go.name} ({go.scene.name})");
                    it.objectReferenceValue = null;
                    changed = true;
                    n++;
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
        return n;
    }

    static void PlaceRig(Scene scene, Transform start)
    {
        var player = Root(scene, "Player");
        if (player == null) return;
        float yaw = start.eulerAngles.y;
        var p = player.transform;
        p.SetPositionAndRotation(new Vector3(start.position.x, p.position.y, start.position.z), Quaternion.Euler(0f, yaw, 0f));

        // 몸통 방향은 PlayerMove가 시네머신 Pan 값에서 매 프레임 가져온다 → Pan 축도 같은 방향으로
        foreach (var pt in Object.FindObjectsByType<CinemachinePanTilt>(FindObjectsSortMode.None))
        {
            var pan = pt.PanAxis; pan.Value = Mathf.DeltaAngle(0f, yaw); pt.PanAxis = pan;
            var tilt = pt.TiltAxis; tilt.Value = 0f; pt.TiltAxis = tilt;
            pt.transform.SetPositionAndRotation(start.position + Vector3.up * 1.3485f, Quaternion.Euler(0f, yaw, 0f));
            EditorUtility.SetDirty(pt);
        }
        var main = Root(scene, "Main Camera");
        if (main != null) main.transform.SetPositionAndRotation(start.position + Vector3.up * 1.3485f, Quaternion.Euler(0f, yaw, 0f));
    }

    static void EnsureFreshnessManager(Scene scene)
    {
        if (Object.FindFirstObjectByType<StainFreshnessManager>() != null) return;
        var go = new GameObject("StainFreshnessManager");
        SceneManager.MoveGameObjectToScene(go, scene);
        var m = go.AddComponent<StainFreshnessManager>();
        var so = new SerializedObject(m);
        so.FindProperty("settings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<StainFreshnessSettings>(FreshnessSettingsPath);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ===================== 2. 맵 손질 =====================

    static void PrepareMap(Transform map)
    {
        foreach (var b in Bindaes)
        {
            var src = FindDeep(map, b.source);
            if (src == null) Warn($"원본 없음: {b.source}");
            else src.gameObject.SetActive(false);
        }

        // 나머지 소품 = 미끼. 잡기 시도(오답)가 되려면 태그 Prop + 레이어 Interactable (머그컵은 격리실 시연 전용이라 제외)
        int layer = LayerMask.NameToLayer("Interactable");
        var props = map.Find("Props");
        int decoys = 0;
        if (props != null)
            foreach (Transform t in props)
            {
                if (!t.gameObject.activeSelf || t.name.StartsWith("TK_Prop_Mug")) continue;
                t.gameObject.tag = "Prop";
                t.gameObject.layer = layer;
                foreach (var col in t.GetComponentsInChildren<Collider>(true))
                {
                    col.gameObject.tag = "Prop";
                    col.gameObject.layer = layer;
                }
                decoys++;
            }
        Log($"미끼 소품 {decoys}개를 잡기 대상으로 설정");
    }

    // ===================== 3. NavMesh =====================

    static void BakeNavMesh(Scene scene, Axes ax)
    {
        var surfType = Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation");
        if (surfType == null) { Warn("NavMeshSurface 타입을 찾지 못해 굽기를 건너뜀"); return; }

        var navRoot = Root(scene, NavRootName);
        if (navRoot == null) { navRoot = new GameObject(NavRootName); SceneManager.MoveGameObjectToScene(navRoot, scene); }
        var surf = navRoot.GetComponent(surfType);
        if (surf == null) surf = navRoot.AddComponent(surfType);

        // 훈련장 전체(약 57×36)를 덮고 높이는 바닥~3m: 천장(4.5) 윗면에 NavMesh가 생기지 않게
        Bounds area = RectBounds(ax, new Vector4(-1f, -1f, 58f, 37f), 1.4f, 3.2f);
        navRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var so = new SerializedObject(surf);
        so.FindProperty("m_AgentTypeID").intValue = 0;
        so.FindProperty("m_CollectObjects").intValue = 1;   // Volume
        so.FindProperty("m_Size").vector3Value = area.size;
        so.FindProperty("m_Center").vector3Value = area.center;
        so.FindProperty("m_UseGeometry").intValue = 0;      // 렌더 메시 (인형방과 같음)
        so.FindProperty("m_IgnoreNavMeshAgent").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 플레이어 몸·에이전트는 굽기에서 뺀다 (자식 메시가 장애물로 구워져 구멍이 생기는 것 방지 — 인형방에서 확인한 문제)
        var hide = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NavMeshAgent>(true)).Select(a => a.gameObject).ToList();
        var player = Root(scene, "Player");
        if (player != null) hide.Add(player);
        // 방 B 미끼 상자: 실행할 때 빈대와 자리를 바꾸므로 그 자리에도 NavMesh가 있어야 빈대가 순간이동할 수 있다 (2026-11-02)
        var mapRoot = Root(scene, MapRootName);
        if (mapRoot != null)
            foreach (var n in RoomBShuffleDecoys)
            {
                var t = FindDeep(mapRoot.transform, n);
                if (t != null) hide.Add(t.gameObject);
            }
        hide = hide.Where(g => g.activeSelf).Distinct().ToList();
        foreach (var g in hide) g.SetActive(false);
        try
        {
            var dataProp = surfType.GetProperty("navMeshData");
            var oldData = dataProp.GetValue(surf) as NavMeshData;
            string path = oldData != null ? AssetDatabase.GetAssetPath(oldData) : null;
            surfType.GetMethod("BuildNavMesh", Type.EmptyTypes).Invoke(surf, null);
            var newData = dataProp.GetValue(surf) as NavMeshData;
            if (newData == null) { Warn("NavMesh 굽기 결과가 비었음"); return; }
            if (!string.IsNullOrEmpty(path) && newData != oldData)
            {
                EditorUtility.CopySerialized(newData, oldData);   // GUID 유지하며 덮어쓰기
                EditorUtility.SetDirty(oldData);
                dataProp.SetValue(surf, oldData);
            }
            else if (string.IsNullOrEmpty(path))
            {
                string dir = "Assets/Scenes/Tutorial";
                if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Scenes", "Tutorial");
                path = $"{dir}/NavMesh-Tutorial.asset";
                AssetDatabase.CreateAsset(newData, path);
            }
            EditorUtility.SetDirty(surf);
            Log($"NavMesh 구움 → {path}");
        }
        finally { foreach (var g in hide) if (g != null) g.SetActive(true); }
    }

    // ===================== 4. 게임 요소 =====================

    static List<(Bindae spec, DollMove doll)> BuildGameplay(Scene scene, Transform map, Axes ax, out List<GameObject> stains)
    {
        var root = new GameObject(GameplayRootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        var wpRoot = Child(root.transform, "Waypoints");
        var dollRoot = Child(root.transform, "Bindae");

        var dollPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DollPrefabPath);
        var stainPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StainPrefabPath);
        if (stainPrefab == null) throw new Exception($"얼룩 프리팹 없음: {StainPrefabPath}");

        // 웨이포인트 (같은 구역 빈대는 같은 웨이포인트를 공유 — 자리 예약은 WaypointRegistry)
        var zoneWps = new Dictionary<string, Transform[]>();
        foreach (var b in Bindaes)
        {
            if (zoneWps.ContainsKey(b.zone)) continue;
            var zr = Child(wpRoot, b.zone);
            zoneWps[b.zone] = b.waypoints.Select((w, i) =>
            {
                var t = new GameObject($"WP_{b.zone}_{i:00}").transform;
                t.SetParent(zr, false);
                t.position = ax.U(w);
                return t;
            }).ToArray();
        }

        var dolls = new List<(Bindae, DollMove)>();
        foreach (var b in Bindaes)
        {
            var d = BuildBindae(scene, dollRoot, map, b, zoneWps[b.zone], dollPrefab, stainPrefab);
            if (d != null) dolls.Add((b, d));
        }
        DollMove D(string n) => dolls.FirstOrDefault(x => x.Item1.name == n).Item2;

        // 4 방 A 곁눈질 찾기 / 5 방 B 정답 자리 무작위 (2026-11-02)
        BuildRoomA(scene, root.transform, map, ax, D("Bindae_RoomA"), zoneWps["RoomA"][0]);
        BuildRoomBShuffle(root.transform, map, D("Bindae_RoomB"), zoneWps["RoomB"][0]);

        // 6 문 앞 미리 배치 얼룩
        stains = BuildStainLine(Child(root.transform, "DoorFrontStains"), ax, stainPrefab);

        // 얼룩 문: 얼룩 전부 + 의자 빈대 → 열림
        var stainDoor = FindDeep(map, StainDoorName)?.GetComponent<DoorController>();
        if (stainDoor == null) Warn($"얼룩 문 DoorController 없음: {StainDoorName}");
        var goalGo = new GameObject("StainDoorGoal");
        goalGo.transform.SetParent(root.transform, false);
        var goal = goalGo.AddComponent<TutorialZoneGoal>();
        var gso = new SerializedObject(goal);
        gso.FindProperty("door").objectReferenceValue = stainDoor;
        SetArray(gso.FindProperty("dolls"), new Object[] { D("Bindae_StainChair") });
        SetArray(gso.FindProperty("stains"), stains.Cast<Object>().ToArray());
        gso.ApplyModifiedPropertiesWithoutUndo();

        // 7 연습 구역: 플레이어가 들어올 때까지 빈대 대기
        var actGo = new GameObject("PracticeZoneActivator");
        actGo.transform.SetParent(root.transform, false);
        var act = actGo.AddComponent<TutorialZoneActivator>();
        var aso = new SerializedObject(act);
        SetArray(aso.FindProperty("dolls"), new Object[] { D("Bindae_Practice_1"), D("Bindae_Practice_2") });
        aso.FindProperty("zone").boundsValue = RectBounds(ax, PracticeRect, 1.5f, 4f);
        aso.ApplyModifiedPropertiesWithoutUndo();

        // 7 연습 한 판 흐름: 입장하면 입구 문 닫힘 + 개수 표시, 승리 후 ㄱ자 복도로 나가면 인형 마니아 방으로
        var flowGo = new GameObject("PracticeFlow");
        flowGo.transform.SetParent(root.transform, false);
        var flow = flowGo.AddComponent<TutorialPracticeFlow>();
        var fso2 = new SerializedObject(flow);
        fso2.FindProperty("activator").objectReferenceValue = act;
        fso2.FindProperty("entranceDoor").objectReferenceValue = FindDeep(map, PracticeDoorName)?.GetComponent<DoorController>();
        SetArray(fso2.FindProperty("revealOnEnter"), HudCountObjects(scene).Cast<Object>().ToArray());
        fso2.FindProperty("exitZone").boundsValue = RectBounds(ax, ExitRect, 1.5f, 4f);
        fso2.FindProperty("nextScene").stringValue = NextSceneName;
        var canvas = Root(scene, "Canvas");
        var fadeT = canvas != null ? canvas.transform.Find("ScreenFader") : null;
        fso2.FindProperty("fader").objectReferenceValue = fadeT != null ? fadeT.GetComponent<ScreenFader>() : null;
        fso2.FindProperty("directorDriven").boolValue = true;   // 7-2 개수 표시·7-4 퇴장은 진행 관리가 부른다
        fso2.ApplyModifiedPropertiesWithoutUndo();

        // 3 격리실 CCTV → 관찰실 모니터 (2026-10-21)
        BuildCctv(root.transform, map, ax);

        // 한 주임 NPC (2026-10-21): 앞장서 걷기·진행 문 열기
        var guide = BuildGuide(scene, root.transform, map, ax);

        // 3 컵 시연 + CCTV 응시 (2026-10-21)
        var cupDemo = BuildCupDemo(scene, root.transform, map, ax, guide, D("Bindae_Isolation"));

        // 1 기상 연출 (2026-10-21)
        var wakeUp = BuildWakeUp(scene, root.transform, guide);

        // 7-3 실패 → 연습 구역 입구 문 앞에서 재시작 (2026-10-21)
        BuildRetry(root.transform, map, ax, guide, flow, stainDoor,
            new[] { D("Bindae_RoomA"), D("Bindae_RoomB"), D("Bindae_StainChair") }.Where(bd => bd != null).Select(bd => bd.gameObject)
                .Append(root.transform.Find("DoorFrontStains")?.gameObject).Where(bg => bg != null).ToArray());

        // 진행 관리 + 한 주임 자막 (2026-10-19, 자막 박스는 2026-10-21부터 공용 UI)
        BuildDirector(scene, root.transform, ax, flow, act, guide, cupDemo, wakeUp);

        // [임시] 진행 문 자동 열림 — 2026-10-21부터 한 주임이 연다. 한 주임이 막혀 진행이 안 되면 UseTempDoorOpeners를 켜고 다시 실행
        var openers = Child(root.transform, "TempDoorOpeners");
        foreach (var (doorName, required) in UseTempDoorOpeners ? ProgressDoors : new (string, string[])[0])
        {
            var dc = FindDeep(map, doorName)?.GetComponent<DoorController>();
            if (dc == null) { Warn($"진행 문 DoorController 없음: {doorName}"); continue; }
            var go = new GameObject($"Opener_{doorName}");
            go.transform.SetParent(openers, false);
            go.transform.position = dc.transform.position;
            var op = go.AddComponent<TutorialDoorOpener>();
            var oso = new SerializedObject(op);
            oso.FindProperty("door").objectReferenceValue = dc;
            SetArray(oso.FindProperty("requiredCaught"), required.Select(n => (Object)D(n)).ToArray());
            oso.ApplyModifiedPropertiesWithoutUndo();
        }
        return dolls;
    }

    // 방 A: 빈대를 3번 칸에 두고, 원본 상자와 같은 모양의 미끼 2개를 1·2번 칸에. 실행할 때 셋의 자리를 섞는다
    static void BuildRoomA(Scene scene, Transform root, Transform map, Axes ax, DollMove doll, Transform wp0)
    {
        var src = FindDeep(map, "TK_Prop_Box");
        if (doll == null || src == null || wp0 == null) { Warn("방 A: 빈대·원본 상자·웨이포인트 중 없는 것이 있어 곁눈질 찾기를 만들지 못함"); return; }
        doll.transform.SetPositionAndRotation(ax.U(RoomASlots[2]), src.rotation);

        var parent = Child(root, "RoomADecoys");
        int layer = LayerMask.NameToLayer("Interactable");
        var srcCol = src.GetComponentInChildren<MeshCollider>(true);
        var items = new List<Object> { doll.transform };
        var wps = new List<Object> { wp0 };
        for (int i = 0; i < 2; i++)
        {
            var go = CopyModel(scene, src, parent);
            go.name = $"RoomA_Decoy_{i + 1}";
            go.transform.SetPositionAndRotation(ax.U(RoomASlots[i]), src.rotation);
            // 맵 미끼와 같게: 태그 Prop + 레이어 Interactable + 원본의 볼록 충돌 메시 (콜라이더 모양이 다르면 조준 범위로 진짜를 가려낼 수 있다)
            var r = go.GetComponentInChildren<MeshRenderer>(true);
            if (r != null)
            {
                var mc = r.GetComponent<MeshCollider>();
                if (mc == null) mc = r.gameObject.AddComponent<MeshCollider>();
                if (srcCol != null) mc.sharedMesh = srcCol.sharedMesh;
                mc.convex = true;
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) { t.gameObject.tag = "Prop"; t.gameObject.layer = layer; }
            items.Add(go.transform);
            wps.Add(null);
        }

        var shuffle = new GameObject("RoomAShuffle").AddComponent<TutorialSlotShuffle>();
        shuffle.transform.SetParent(parent, false);
        var so = new SerializedObject(shuffle);
        SetArray(so.FindProperty("items"), items.ToArray());
        SetArray(so.FindProperty("waypointOf"), wps.ToArray());
        so.ApplyModifiedPropertiesWithoutUndo();
        Log($"방 A 곁눈질 찾기: 상자 3개 한 줄 (빈대 + 미끼 2), 벗어날 자리 {RoomAOffSpots.Length}곳");
    }

    // 방 B: 빈대(L.002 자리)와 미끼 상자 L.001·L.003의 자리를 실행할 때 섞는다. 의자는 그대로
    static void BuildRoomBShuffle(Transform root, Transform map, DollMove doll, Transform wp0)
    {
        if (doll == null || wp0 == null) { Warn("방 B: 빈대 또는 웨이포인트가 없어 자리 섞기를 만들지 못함"); return; }
        var items = new List<Object> { doll.transform };
        var wps = new List<Object> { wp0 };
        foreach (var n in RoomBShuffleDecoys)
        {
            var t = FindDeep(map, n);
            if (t == null) { Warn($"방 B 미끼 없음: {n}"); continue; }
            items.Add(t);
            wps.Add(null);
        }
        var shuffle = new GameObject("RoomBShuffle").AddComponent<TutorialSlotShuffle>();
        shuffle.transform.SetParent(root, false);
        var so = new SerializedObject(shuffle);
        SetArray(so.FindProperty("items"), items.ToArray());
        SetArray(so.FindProperty("waypointOf"), wps.ToArray());
        so.ApplyModifiedPropertiesWithoutUndo();
        Log($"방 B 정답 자리 무작위: 빈대 + 미끼 상자 {items.Count - 1}개");
    }

    static DollMove BuildBindae(Scene scene, Transform parent, Transform map, Bindae b, Transform[] wps, GameObject dollPrefab, GameObject stainPrefab)
    {
        var src = FindDeep(map, b.source);
        if (src == null) { Warn($"빈대 원본 없음: {b.source}"); return null; }

        var root = new GameObject(b.name);
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(new Vector3(src.position.x, 0f, src.position.z), src.rotation);

        // 모습: 원본과 같은 FBX를 새로 놓고 재질·충돌 메시를 그대로 → 미끼와 구분되는 단서가 생기지 않게
        var model = PrefabUtility.GetCorrespondingObjectFromSource(src.gameObject);
        var vis = model != null ? (GameObject)PrefabUtility.InstantiatePrefab(model, scene) : Object.Instantiate(src.gameObject);
        vis.name = "Visual";
        vis.SetActive(true);
        vis.transform.SetParent(root.transform, false);
        vis.transform.localPosition = Vector3.zero;
        vis.transform.localRotation = Quaternion.identity;
        vis.transform.localScale = src.localScale;
        var srcR = src.GetComponentInChildren<MeshRenderer>(true);
        var dstR = vis.GetComponentInChildren<MeshRenderer>(true);
        if (srcR != null && dstR != null) dstR.sharedMaterials = srcR.sharedMaterials;
        RecordInstance(vis.transform, dstR);

        int interactable = LayerMask.NameToLayer("Interactable");
        if (b.catchable && dstR != null)
        {
            var srcCol = src.GetComponentInChildren<MeshCollider>(true);
            var mc = dstR.GetComponent<MeshCollider>();
            if (mc == null) mc = dstR.gameObject.AddComponent<MeshCollider>();   // ??는 Unity의 가짜 null을 못 걸러서 쓰지 않음
            if (srcCol != null) mc.sharedMesh = srcCol.sharedMesh;
            mc.convex = true;
            foreach (var t in new[] { root, vis, dstR.gameObject }) { t.tag = "Prop"; t.layer = interactable; }
        }
        else
        {
            foreach (var c in vis.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        }

        // 몸: doll_move 프리팹과 같은 구성 (DollMove가 NavMeshAgent·DollVision·DollStainSpawner를 같이 붙인다)
        var dm = root.AddComponent<DollMove>();
        var agent = root.GetComponent<NavMeshAgent>();
        agent.agentTypeID = 0;
        agent.radius = 0.3f; agent.height = 0.75f; agent.baseOffset = 0f;   // 인형방 빈대와 같은 값 (NavMesh 굽기 설정과 일치)
        agent.speed = b.speed; agent.stoppingDistance = 0.15f; agent.angularSpeed = 240f;

        dm.wayPoints = wps;
        dm.escapeDistance = b.escapeDistance;
        dm.nearMoveChance = b.move ? b.nearMoveChance : 0f;
        dm.tokenChargeInterval = b.move ? b.tokenInterval : 100000f;   // 이동 끔 = 이동권이 사실상 충전되지 않음
        dm.maxMoveTokens = b.maxTokens;
        dm.startingMoveTokens = b.move ? b.startTokens : 0;
        dm.startGraceTime = b.grace;
        var dso = new SerializedObject(dm);
        dso.FindProperty("countsTowardGoal").boolValue = b.countsTowardGoal;
        dso.FindProperty("startAtFirstWaypoint").boolValue = b.startAtFirst;
        // 튜토리얼 빈대는 원본 소품과 같은 모양으로 만들어 미끼와 맞춰 둔다 → 무작위 변장(DollDisguise) 끔 (2026-11-02, 연습 판 "같은 모양 미끼"가 깨지던 문제)
        dso.FindProperty("keepOwnLook").boolValue = true;
        dso.ApplyModifiedPropertiesWithoutUndo();

        var vision = root.GetComponent<DollVision>();
        // 시야각·거리는 공용 설정 에셋(DollVisionSettings, 인형방 기준)을 쓴다 (2026-11-01)
        vision.obstructionMask = ~(1 << LayerMask.NameToLayer("Ignore Raycast"));   // 격리실 유리(Ignore Raycast)는 시야를 가리지 않음
        vision.eyeHeightOffset = 0.5f;

        var sso = new SerializedObject(root.GetComponent<DollStainSpawner>());
        sso.FindProperty("stainPrefab").objectReferenceValue = b.stains ? stainPrefab : null;   // 비우면 얼룩을 남기지 않는다
        sso.ApplyModifiedPropertiesWithoutUndo();

        if (b.laugh)
        {
            var laugh = root.AddComponent<DollLaugh>();
            var audio = root.GetComponent<AudioSource>();
            var pAudio = dollPrefab != null ? dollPrefab.GetComponent<AudioSource>() : null;
            var pLaugh = dollPrefab != null ? dollPrefab.GetComponent<DollLaugh>() : null;
            if (pAudio != null) EditorUtility.CopySerialized(pAudio, audio);
            audio.playOnAwake = false; audio.spatialBlend = 1f; audio.minDistance = 1f; audio.priority = 32;
            var lso = new SerializedObject(laugh);
            if (pLaugh != null) lso.FindProperty("laughClip").objectReferenceValue = new SerializedObject(pLaugh).FindProperty("laughClip").objectReferenceValue;
            lso.FindProperty("laughDistance").floatValue = LaughDistance;
            lso.FindProperty("rearmDistance").floatValue = RearmDistance;
            lso.FindProperty("repeatable").boolValue = true;
            lso.ApplyModifiedPropertiesWithoutUndo();
        }
        return dm;
    }

    static List<GameObject> BuildStainLine(Transform parent, Axes ax, GameObject prefab)
    {
        var settings = AssetDatabase.LoadAssetAtPath<StainFreshnessSettings>(FreshnessSettingsPath);
        var list = new List<GameObject>();
        var wanted = new List<Vector3>();
        for (int i = 0; i < StainCount; i++)
        {
            float t = StainCount == 1 ? 1f : i / (float)(StainCount - 1);
            Vector2 p = (1 - t) * (1 - t) * StainLineStart + 2 * (1 - t) * t * StainLineControl + t * t * StainLineEnd;   // 2차 베지어
            float age = Mathf.Lerp(StainOldestAge, StainNewestAge, t);

            // 프리팹 인스턴스를 쓰지 않고 일반 Transform 오브젝트에 프리팹 부품만 복사한다 (2026-10-18).
            // Stain 프리팹 루트가 RectTransform이라, 일반 오브젝트 아래에 두면 저장할 때 얼룩 하나씩 좌표가 틀어졌다 (05 → 00 차례로).
            // 런타임에 빈대가 남기는 얼룩은 저장되지 않으므로 프리팹 그대로 써도 된다
            var go = new GameObject($"Stain_DoorFront_{i:00}");
            go.transform.SetParent(parent, false);
            go.tag = prefab.tag;
            go.layer = prefab.layer;
            go.transform.localScale = prefab.transform.localScale;
            go.transform.SetPositionAndRotation(ax.U(p, 0.02f), Quaternion.Euler(90f, 0f, 0f));
            foreach (var type in new[] { typeof(SpriteRenderer), typeof(SphereCollider), typeof(StainFreshness) })
            {
                var src = prefab.GetComponent(type);
                if (src == null) { Warn($"얼룩 프리팹에 {type.Name} 없음"); continue; }
                var dst = go.GetComponent(type);
                if (dst == null) dst = go.AddComponent(type);   // ??는 Unity의 가짜 null을 못 걸러서 쓰지 않음
                EditorUtility.CopySerialized(src, dst);
            }

            var fresh = go.GetComponent<StainFreshness>();
            var fso = new SerializedObject(fresh);
            fso.FindProperty("startAge").floatValue = Mathf.Round(age * 10f) / 10f;
            fso.FindProperty("freezeAge").boolValue = true;   // 기다려도 변하지 않게 (튜토리얼 문 앞)
            fso.ApplyModifiedPropertiesWithoutUndo();

            // 에디터에서도 실제 모습이 보이게 표정·색을 미리 맞춰 둔다 (게임에선 매니저가 다시 맞춤)
            if (settings != null)
            {
                var sr = go.GetComponent<SpriteRenderer>();
                var sp = settings.GetSprite(settings.GetStageIndex(age));
                if (sp != null) sr.sprite = sp;
                sr.color = settings.GetTint(age);
            }
            list.Add(go);
            wanted.Add(ax.U(p, 0.02f));
        }
        // 모두 만든 뒤 위치를 한 번 더 박고 확인한다 — 어긋나면 경고 (저장된 파일은 CaptureBatch에서 다시 확인)
        for (int i = 0; i < list.Count; i++)
        {
            var tr = list[i].transform;
            tr.SetPositionAndRotation(wanted[i], Quaternion.Euler(90f, 0f, 0f));
            if (Vector3.Distance(tr.position, wanted[i]) > 0.01f)
                Warn($"얼룩 위치 어긋남: {list[i].name} {tr.position} (원래 {wanted[i]})");
        }
        return list;
    }

    // 격리실 CCTV 카메라 모델 자리에 실제 카메라를 두고, Render Texture로 관찰실 모니터 화면에 띄운다
    // 장면 3의 핵심: 창으로 보면 굳어 있고 모니터로 보면 움직인다 (모니터는 관측이 아님 — 이 카메라는 MainCamera가 아니다)
    static void BuildCctv(Transform root, Transform map, Axes ax)
    {
        var cctvModel = FindDeep(map, "TK_Iso_CCTV");
        var screen = FindDeep(map, "TK_Iso_MonitorScreen");
        if (cctvModel == null || screen == null) { Warn("CCTV 카메라 또는 모니터 화면 모델이 없어 CCTV를 건너뜀"); return; }

        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(CctvRtPath);
        if (rt == null)
        {
            rt = new RenderTexture(480, 360, 16) { name = "RT_CCTV" };
            AssetDatabase.CreateAsset(rt, CctvRtPath);
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(CctvMatPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "M_CCTVScreen" };
            AssetDatabase.CreateAsset(mat, CctvMatPath);
        }
        mat.SetTexture("_BaseMap", rt);
        mat.SetColor("_BaseColor", new Color(0.92f, 1f, 0.95f));   // 살짝 초록빛 = CCTV 느낌
        mat.SetTextureScale("_BaseMap", new Vector2(CctvFlipX ? -1f : 1f, CctvFlipY ? -1f : 1f));
        mat.SetTextureOffset("_BaseMap", new Vector2(CctvFlipX ? 1f : 0f, CctvFlipY ? 1f : 0f));
        EditorUtility.SetDirty(mat);

        var sr = screen.GetComponentInChildren<MeshRenderer>();
        if (sr != null) sr.sharedMaterial = mat;

        // 카메라: 모델 자리에서 방 안쪽으로 조금 나와 격리실 가운데를 내려다본다
        var go = new GameObject("CctvCamera");
        go.transform.SetParent(root, false);
        Vector3 target = ax.U(CctvLookAt, CctvLookHeight);
        Vector3 basePos = cctvModel.position;
        Vector3 inward = target - basePos; inward.y = 0f;
        go.transform.position = basePos + inward.normalized * 0.25f + Vector3.down * 0.1f;
        go.transform.LookAt(target);
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = CctvFov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 25f;
        cam.targetTexture = rt;
        cam.cullingMask = ~(1 << LayerMask.NameToLayer("UI"));
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        go.tag = "Untagged";

        var feed = go.AddComponent<CctvFeed>();
        var so = new SerializedObject(feed);
        so.FindProperty("monitor").objectReferenceValue = screen;
        so.ApplyModifiedPropertiesWithoutUndo();
        Log($"CCTV: 카메라 {go.transform.position} → 격리실 가운데, 모니터 화면 재질 {CctvMatPath}");
    }

    // ===== 한 주임 (2026-10-21) =====
    const bool UseTempDoorOpeners = false;
    static readonly Vector2 SlotStand = new Vector2(21.5f, 12.75f);    // 투입구(22.5, 12.75) 앞에 서는 자리. 던지며 숙인 머리(+0.56m)가 벽·올라간 덮개에 닿지 않게 (21.75 → 21.5, 2026-10-25)
    static readonly Vector3 ClipHold = new Vector3(0f, 0.98f, 0.42f);  // 클립보드 중심 (한 주임 기준: 높이, 앞). 맵 배치는 (0, 0.95, 0.34)
    const float ClipTilt = 30f;                                        // 세운 자세에서 윗부분을 앞으로 눕히는 각도

    // ===== 컵 시연 (2026-10-21) =====
    static readonly Vector2 CupLand = new Vector2(24.4f, 13.8f);       // 컵이 떨어질 곳: CCTV 화면 안 (처음 (23.35, 12.75)은 화면 밖이라 옮김, 2026-10-21)
    const float SlotHoleCenterY = 1.125f;                              // 투입구 구멍 바닥 0.9 + 높이 0.45의 가운데
    const string AlienMouthMatPath = "Assets/Scenes/Tutorial/M_AlienMouth.mat";
    const string DemoCupMatPath = "Assets/Scenes/Tutorial/M_DemoCup.mat";
    static readonly Color DemoCupColor = Color.white;                  // 모니터(초록빛 CCTV)에서 잘 보이게 시연 컵만 흰색 (2026-10-21)
    const bool FlapSlideUp = true;                                     // 투입구 덮개: true = 벽면을 따라 위로 밀어 올리는 셔터 (2026-10-25 사용자 결정), false = 경첩으로 회전
    const float FlapSlideClear = 0.02f;                                // 셔터: 덮개 아래 끝이 구멍 위보다 이만큼 더 올라감 (올리는 거리 = 덮개 높이 + 이 값)
    const bool FlapOpensTowardIso = false;                             // (회전 방식일 때) false = 관찰실(한 주임) 쪽, true = 격리실 쪽 — 격리실 쪽은 구멍 때문에 75~78°가 한계
    const float FlapOpenAngle = 75f;                                   // (회전 방식일 때) 여는 각도
    const float HanStride = 1.1f;                                      // 한 주임 보폭: 걷기 속도를 1.6 → 3.2로 올리며 0.55 → 1.1 (걸음 박자 유지 = "깐족" 방지, 「코드 설계 - 튜토리얼 템포」)
    const float HanThrowLean = 16f;                                    // 던지기 몸짓의 최대 앞 기울기 (PegDollAnimator.ThrowPitch와 같게) — 덮개 여유 검사용
    const float HanHeadRadius = 0.18f;                                 // 한 주임 머리 반지름 (CH_HanJuim 1.3~1.4m)
    const float DemoCupGlow = 0.35f;                                   // 약한 자체 발광: 격리실이 어두워도 형태가 보이게
    // 구간: 이름, 길(Blender 평면), 문(길의 각 점에 도착하면 열 문, 없으면 null), 플레이어가 뒤처지면 기다릴지
    // 문 앞 0.8~1m에서 멈춰 열고 지나간다. 서 있는 자리는 플레이어 길목을 피해 벽 쪽으로
    static readonly (string name, Vector2[] pts, string[] doors, bool lead)[] GuideLegs =
    {
        // 1-3 → 2 → 3: 기상 방 문을 열고 복도를 앞장서 관찰실 창 뒤쪽으로
        ("Wake", new[] { new Vector2(3f, 5.2f), new Vector2(3f, 7.0f), new Vector2(3f, 15f), new Vector2(17f, 15f), new Vector2(19.4f, 16.4f) },
                 new[] { "Door_1_Wake", null, null, null, null }, true),
        // 3-5 컵 시연: 관찰실 투입구 앞으로 (TutorialCupDemo가 요청)
        ("ToSlot", new[] { new Vector2(20.8f, 13.6f), SlotStand },
                   new[] { (string)null, null }, false),
        // 3-9 뒤 / 4-1: 전실로 가서 방 A 문을 열고 밖에 남는다 ("한 주임은 밖에 남음")
        ("ToRoomA", new[] { new Vector2(20.6f, 18.4f), new Vector2(21.6f, 19.5f), new Vector2(20.4f, 20.4f) },
                    new[] { null, "Door_4_RoomA_In", null }, false),
        // 4-3 포획 뒤: 방 A를 지나 출구를 열고, A-B 복도에서 방 B 문을 열어 준다
        ("ToRoomB", new[] { new Vector2(21.8f, 19.5f), new Vector2(26.2f, 19.5f), new Vector2(28.2f, 19.5f), new Vector2(30.6f, 19.5f), new Vector2(29.9f, 20.4f) },
                    new[] { null, "Door_4_RoomA_Out", null, "Door_5_RoomB_In", null }, false),
        // 5-3 포획 뒤: 방 B를 지나 출구를 열고, 문 앞 입구를 열어 준 뒤 복도에서 기다린다
        ("ToDoorFront", new[] { new Vector2(31f, 19.5f), new Vector2(38.2f, 19.5f), new Vector2(40.3f, 19.5f), new Vector2(42.6f, 19.5f), new Vector2(41.6f, 20.4f) },
                        new[] { null, "Door_5_RoomB_Out", null, "Door_6_Stain_In", null }, false),
        // 6-2 얼룩 문 열림 뒤: 문 앞을 지나 ㄱ자 복도를 앞장서, 연습 구역 입구를 열어 주고 옆으로 비켜선다
        ("ToPractice", new[] { new Vector2(43f, 19.5f), new Vector2(48.9f, 19.5f), new Vector2(51f, 19.8f), new Vector2(51f, 24.4f), new Vector2(50.2f, 23.6f) },
                       new[] { null, null, null, "Door_7_Practice_In", null }, true),
        // 7-4: 연습 판 승리 뒤 함께 ㄱ자 복도를 되돌아간다
        ("Exit", new[] { new Vector2(50.8f, 21.2f), new Vector2(50.2f, 19.6f) },
                 new[] { (string)null, null }, false),
    };

    static TutorialGuide BuildGuide(Scene scene, Transform root, Transform map, Axes ax)
    {
        var han = FindDeep(map, "CH_HanJuim");
        if (han == null) { Warn("한 주임 모델(CH_HanJuim)이 맵에 없음"); return null; }
        var clip = FindDeep(map, "TK_Clipboard");

        // 루트(이동·충돌) → Visual(애니메이션) → 모델·클립보드. 맵의 원본은 끈다 (빈대와 같은 방식)
        var go = new GameObject("HanJuim");
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(new Vector3(han.position.x, 0f, han.position.z), han.rotation);
        var visual = new GameObject("Visual").transform;
        visual.SetParent(go.transform, false);

        var model = CopyModel(scene, han, visual);
        foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        if (clip != null)
        {
            var cm = CopyModel(scene, clip, visual);
            foreach (var c in cm.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            // 드는 자세 (2026-10-21 피드백): 종이 면(모델 -Y)이 한 주임 쪽으로 오게 180° 돌리고, 윗부분을 앞으로 눕혀 내려다보는 자세
            cm.transform.localPosition = ClipHold;
            cm.transform.localRotation = Quaternion.Euler(ClipTilt, 0f, 0f) * Quaternion.Euler(0f, 180f, 0f);
            clip.gameObject.SetActive(false);
        }
        han.gameObject.SetActive(false);

        var cap = go.AddComponent<CapsuleCollider>();
        cap.radius = 0.28f; cap.height = 1.55f; cap.center = new Vector3(0f, 0.775f, 0f);
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        var anim = go.AddComponent<PegDollAnimator>();
        var aso = new SerializedObject(anim);
        aso.FindProperty("visual").objectReferenceValue = visual;
        aso.FindProperty("strideLength").floatValue = HanStride;   // 빨라진 걸음(3.2m/s)에도 박자는 그대로 (2026-11-02)
        aso.ApplyModifiedPropertiesWithoutUndo();

        var guide = go.AddComponent<TutorialGuide>();
        var gso = new SerializedObject(guide);
        gso.FindProperty("anim").objectReferenceValue = anim;
        var legsProp = gso.FindProperty("legs");
        legsProp.arraySize = GuideLegs.Length;
        for (int i = 0; i < GuideLegs.Length; i++)
        {
            var (lname, pts, doors, lead) = GuideLegs[i];
            var lp = legsProp.GetArrayElementAtIndex(i);
            lp.FindPropertyRelative("name").stringValue = lname;
            lp.FindPropertyRelative("lead").boolValue = lead;
            var pp = lp.FindPropertyRelative("points");
            pp.arraySize = pts.Length;
            for (int j = 0; j < pts.Length; j++) pp.GetArrayElementAtIndex(j).vector3Value = ax.U(pts[j]);
            var dp = lp.FindPropertyRelative("doors");
            dp.arraySize = pts.Length;
            for (int j = 0; j < pts.Length; j++)
            {
                string dn = j < doors.Length ? doors[j] : null;
                var dc = string.IsNullOrEmpty(dn) ? null : FindDeep(map, dn)?.GetComponent<DoorController>();
                if (!string.IsNullOrEmpty(dn) && dc == null) Warn($"한 주임 구간 {lname}: 문 {dn} 없음");
                dp.GetArrayElementAtIndex(j).objectReferenceValue = dc;
            }
        }
        gso.ApplyModifiedPropertiesWithoutUndo();
        CheckGuideRoute(go.transform, map, ax);
        return guide;
    }

    // 원본(맵의 FBX 인스턴스)과 같은 모델·재질을 parent 아래에 같은 상대 자세로 놓는다
    static GameObject CopyModel(Scene scene, Transform src, Transform parent)
    {
        var prefab = PrefabUtility.GetCorrespondingObjectFromSource(src.gameObject);
        var go = prefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene) : Object.Instantiate(src.gameObject);
        go.name = src.name;
        go.SetActive(true);
        go.transform.SetParent(parent, false);
        go.transform.position = src.position;
        go.transform.rotation = src.rotation;
        go.transform.localScale = src.localScale;
        var sr = src.GetComponentsInChildren<MeshRenderer>(true);
        var dr = go.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < Mathf.Min(sr.Length, dr.Length); i++) dr[i].sharedMaterials = sr[i].sharedMaterials;
        RecordInstance(go.transform);
        foreach (var r in dr) RecordInstance(r);
        return go;
    }

    // 한 주임 길이 벽을 뚫지 않는지 확인 — 구간마다 허리 높이로 광선을 쏴서 문·빈대·소품이 아닌 것에 맞으면 경고
    static void CheckGuideRoute(Transform hanRoot, Transform map, Axes ax)
    {
        Physics.SyncTransforms();
        var doorCols = new HashSet<Collider>(map.GetComponentsInChildren<DoorController>(true).SelectMany(d => d.GetComponentsInChildren<Collider>(true)));
        int bad = 0, segs = 0;
        Vector2 prev = new Vector2(hanRoot.position.x / ax.sx, hanRoot.position.z / ax.sz);
        foreach (var (lname, pts, _, _) in GuideLegs)
            foreach (var q in pts)
            {
                Vector3 a = ax.U(prev, 1.0f), c = ax.U(q, 1.0f);
                Vector3 d = c - a; float len = d.magnitude;
                prev = q;
                if (len < 0.01f) continue;
                segs++;
                foreach (var hit in Physics.RaycastAll(a, d / len, len, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (doorCols.Contains(hit.collider) || hit.collider.transform.IsChildOf(hanRoot)) continue;
                    if (hit.collider.GetComponentInParent<DollMove>() != null || hit.collider.CompareTag("Prop") || hit.collider.CompareTag("Player")) continue;
                    Warn($"한 주임 길 {lname}: {hit.collider.name}에 막힘 ({hit.point})");
                    bad++;
                    break;
                }
            }
        Log($"한 주임 길 확인: 구간 {segs}개 중 막힘 {bad}개");
    }

    // 열린 덮개가 던지며 앞으로 숙인 한 주임 머리와 겹치는지 검사 (2026-10-25, 바깥으로 열리게 바꾼 뒤 덮개가 머리를 통과했음)
    // 덮개: 메시 상자 꼭짓점을 열린 모양으로 옮김(회전 또는 위로 이동) / 한 주임: 꼭대기 높이 × sin(최대 기울기) + 머리 반지름 (보수적)
    static void CheckFlapClearance(Transform flap, Func<Vector3, Vector3> toOpen, TutorialGuide guide, Vector3 stand, Vector3 facing)
    {
        float top = 1.55f;
        var hr = guide.GetComponentsInChildren<Renderer>();
        if (hr.Length > 0) top = hr.Max(r => r.bounds.max.y) - guide.transform.position.y;
        facing = Vector3.ProjectOnPlane(facing, Vector3.up).normalized;
        float nearest = float.PositiveInfinity;
        foreach (var mf in flap.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 p = toOpen(mf.transform.TransformPoint(c));
                if (p.y < stand.y || p.y > stand.y + top) continue;   // 한 주임 키 밖은 안 부딪힘
                nearest = Mathf.Min(nearest, Vector3.Dot(p - stand, facing));
            }
        }
        if (float.IsInfinity(nearest)) { Log("덮개 여유: 열린 덮개가 한 주임 키 높이 밖"); return; }
        float reach = top * Mathf.Sin(HanThrowLean * Mathf.Deg2Rad) + HanHeadRadius;
        float margin = nearest - reach;
        if (margin < 0.03f) Warn($"덮개가 던지는 한 주임과 겹침: 덮개까지 {nearest:0.00}m, 숙인 머리 {reach:0.00}m (여유 {margin:0.00}) — SlotStand를 뒤로 또는 FlapOpenAngle 조절");
        else Log($"덮개 여유: 덮개까지 {nearest:0.00}m, 숙인 머리 {reach:0.00}m → {margin:0.00}m");
    }

    // 컵 시연: 시연용 컵·변신 머그컵 모습·외계인 입·투입구 덮개 여는 방향·던지기 포물선을 준비한다
    static TutorialCupDemo BuildCupDemo(Scene scene, Transform root, Transform map, Axes ax, TutorialGuide guide, DollMove alien)
    {
        var mug = FindDeep(map, "TK_Prop_Mug");
        var flap = FindDeep(map, "TK_Iso_SlotFlap");
        var slot = FindDeep(map, "TK_Iso_SlotFrame");
        var monitor = FindDeep(map, "TK_Iso_MonitorScreen");
        if (mug == null || alien == null || guide == null) { Warn("컵 시연: 머그컵·격리실 외계인·한 주임 중 없는 것이 있어 건너뜀"); return null; }

        var demoRoot = Child(root, "CupDemo");
        var cup = CopyModel(scene, mug, demoRoot); cup.name = "Cup";
        var look = CopyModel(scene, mug, demoRoot); look.name = "AlienAsMug";
        foreach (var c in cup.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var c in look.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        // 시연 컵과 변신 모습만 흰색 (외계인은 "먹은 그 컵"이 되므로 둘이 같아야 함). 맵 원래 머그컵·FBX 재질은 그대로
        var cupMat = AssetDatabase.LoadAssetAtPath<Material>(DemoCupMatPath);
        if (cupMat == null)
        {
            cupMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_DemoCup" };
            AssetDatabase.CreateAsset(cupMat, DemoCupMatPath);
        }
        cupMat.SetColor("_BaseColor", DemoCupColor);
        cupMat.SetFloat("_Smoothness", 0.4f);
        cupMat.EnableKeyword("_EMISSION");
        cupMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        cupMat.SetColor("_EmissionColor", DemoCupColor * DemoCupGlow);
        EditorUtility.SetDirty(cupMat);
        foreach (var r in cup.GetComponentsInChildren<MeshRenderer>(true).Concat(look.GetComponentsInChildren<MeshRenderer>(true)))
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = cupMat;
            r.sharedMaterials = mats;
            RecordInstance(r);
        }
        mug.gameObject.SetActive(false);   // 시작 땐 격리실에 컵이 없다 (한 주임이 넣는다)

        // ---- 외계인 입 (이 장면에서만 보임, 2026-10-21 결정) ----
        var aroot = alien.transform;
        var vis = aroot.Find("Visual");
        Transform mouthTf = null;
        var mouthOpen = Vector3.zero;
        var ar = vis != null ? vis.GetComponentInChildren<MeshRenderer>() : null;
        if (ar != null)
        {
            // 몸 크기를 외계인 루트 기준으로 잰다 (앞 = +Z)
            var wb = ar.bounds;
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? wb.min.x : wb.max.x, (i & 2) == 0 ? wb.min.y : wb.max.y, (i & 4) == 0 ? wb.min.z : wb.max.z);
                var l = aroot.InverseTransformPoint(corner);
                mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
            }
            float h = mx.y, width = mx.x - mn.x, front = mx.z;
            var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(m.GetComponent<Collider>());
            m.name = "Mouth";
            m.transform.SetPositionAndRotation(aroot.TransformPoint(new Vector3(0f, h * 0.3f, front - 0.02f)), aroot.rotation);
            m.transform.SetParent(vis, true);
            var mmat = AssetDatabase.LoadAssetAtPath<Material>(AlienMouthMatPath);
            if (mmat == null)
            {
                mmat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "M_AlienMouth" };
                AssetDatabase.CreateAsset(mmat, AlienMouthMatPath);
            }
            mmat.SetColor("_BaseColor", new Color(0.12f, 0.03f, 0.1f));
            EditorUtility.SetDirty(mmat);
            m.GetComponent<MeshRenderer>().sharedMaterial = mmat;
            Vector3 ls = vis.lossyScale;
            mouthOpen = new Vector3(width * 0.42f / ls.x, h * 0.26f / ls.y, 0.06f / ls.z);
            m.transform.localScale = Vector3.zero;
            mouthTf = m.transform;
        }
        else Warn("컵 시연: 외계인 몸 렌더러가 없어 입을 만들지 못함");

        var fx = alien.gameObject.AddComponent<AlienMimicFx>();
        var fso = new SerializedObject(fx);
        fso.FindProperty("visual").objectReferenceValue = vis;
        fso.FindProperty("mouth").objectReferenceValue = mouthTf;
        fso.FindProperty("mouthOpenScale").vector3Value = mouthOpen;
        fso.ApplyModifiedPropertiesWithoutUndo();

        // ---- 한 주임 손 (클립보드 자리) ----
        var hanVis = guide.transform.Find("Visual");
        var clip = hanVis != null ? hanVis.Find("TK_Clipboard") : null;
        var hand = new GameObject("Hand").transform;
        hand.SetParent(hanVis != null ? hanVis : guide.transform, false);
        hand.position = clip != null ? clip.position : guide.transform.position + guide.transform.forward * 0.3f + Vector3.up * 0.95f;
        hand.rotation = guide.transform.rotation;

        // ---- 투입구 덮개: 두 방향으로 직접 돌려 보고 FlapOpensTowardIso가 정한 방(관찰실/격리실) 쪽으로 열리는 방향을 고른다 ----
        Quaternion closed = Quaternion.identity, open = Quaternion.identity, flapTurn = Quaternion.identity;
        Vector3 closedPos = Vector3.zero, openPos = Vector3.zero, slideWorld = Vector3.zero;
        if (flap != null && FlapSlideUp)
        {
            // 셔터: 회전 없이 벽면을 따라 위로. 올리는 거리 = 덮개 높이(렌더러 상자) + 여유 → 아래 끝이 구멍 위로
            closed = open = flap.localRotation;
            float h = 0f;
            foreach (var r in flap.GetComponentsInChildren<Renderer>()) h = Mathf.Max(h, r.bounds.size.y);
            if (h <= 0f) h = 0.55f;
            slideWorld = Vector3.up * (h + FlapSlideClear);
            closedPos = flap.localPosition;
            openPos = closedPos + (flap.parent != null ? flap.parent.InverseTransformVector(slideWorld) : slideWorld);
            Log($"투입구 덮개: 위로 {h + FlapSlideClear:0.00}m 밀어 올림 (덮개 높이 {h:0.00})");
        }
        else if (flap != null)
        {
            closed = flap.localRotation;
            Vector3 alongWall = (ax.U(new Vector2(0f, 1f)) - ax.U(Vector2.zero)).normalized;
            Vector3 toIso = (ax.U(new Vector2(1f, 0f)) - ax.U(Vector2.zero)).normalized;
            var fr = flap.GetComponentInChildren<Renderer>();
            Vector3 c0 = fr != null ? fr.bounds.center : flap.position + Vector3.down * 0.2f;
            float best = float.NegativeInfinity;
            foreach (float sign in new[] { 1f, -1f })
            {
                var q = Quaternion.AngleAxis(sign * FlapOpenAngle, alongWall);
                Vector3 c1 = flap.position + q * (c0 - flap.position);
                float d = Vector3.Dot(c1 - c0, toIso) * (FlapOpensTowardIso ? 1f : -1f);
                if (d > best) { best = d; flapTurn = q; open = Quaternion.Inverse(flap.parent != null ? flap.parent.rotation : Quaternion.identity) * (q * flap.rotation); }
            }
        }
        else Warn("컵 시연: 투입구 덮개(TK_Iso_SlotFlap)가 없음");

        // ---- 던지기 포물선: 손 → 투입구 구멍 가운데 → 착지점 ----
        Vector3 land = ax.U(CupLand, 0f);
        Vector3 stand = ax.U(SlotStand, 0f);
        Vector3 slotPos = slot != null ? slot.position : ax.U(new Vector2(22.5f, 12.75f), 0f);
        var faceSlot = Quaternion.LookRotation(Vector3.ProjectOnPlane(slotPos - stand, Vector3.up).normalized, Vector3.up);
        Vector3 handLocal = guide.transform.InverseTransformPoint(hand.position);
        Vector3 handAtSlot = stand + faceSlot * handLocal;
        Vector3 hole = new Vector3(slotPos.x, SlotHoleCenterY, slotPos.z);
        Vector2 a2 = new Vector2(handAtSlot.x, handAtSlot.z), b2 = new Vector2(land.x, land.z), h2 = new Vector2(hole.x, hole.z);
        // 컵은 수평으로 손 → 구멍 가운데 → 착지점 꺾은선을 난다 (TutorialCupDemo.throughHole) → 구멍 지점 비율 = 거리 비율 (2026-10-25)
        float d1 = Vector2.Distance(a2, h2), d2 = Vector2.Distance(h2, b2);
        float f = Mathf.Clamp(d1 / Mathf.Max(0.01f, d1 + d2), 0.05f, 0.95f);
        float straight = Mathf.Lerp(handAtSlot.y, land.y, f);
        float arc = Mathf.Clamp((hole.y - straight) / (4f * f * (1f - f)), 0.15f, 1.2f);

        var demo = demoRoot.gameObject.AddComponent<TutorialCupDemo>();
        var so = new SerializedObject(demo);
        so.FindProperty("alien").objectReferenceValue = fx;
        so.FindProperty("cctv").objectReferenceValue = root.Find("CctvCamera");
        so.FindProperty("mugLook").objectReferenceValue = look;
        so.FindProperty("guide").objectReferenceValue = guide;
        so.FindProperty("hanAnim").objectReferenceValue = guide.GetComponent<PegDollAnimator>();
        so.FindProperty("slotFacing").objectReferenceValue = slot;
        so.FindProperty("watchTarget").objectReferenceValue = monitor;
        so.FindProperty("clipboard").objectReferenceValue = clip != null ? clip.gameObject : null;
        so.FindProperty("hand").objectReferenceValue = hand;
        so.FindProperty("cup").objectReferenceValue = cup.transform;
        so.FindProperty("landPoint").vector3Value = land;
        so.FindProperty("arcHeight").floatValue = arc;
        so.FindProperty("throughHole").boolValue = true;
        so.FindProperty("holePoint").vector3Value = hole;
        so.FindProperty("flap").objectReferenceValue = flap;
        so.FindProperty("flapClosed").quaternionValue = closed;
        so.FindProperty("flapOpen").quaternionValue = open;
        so.FindProperty("flapSlides").boolValue = flap != null && FlapSlideUp;
        so.FindProperty("flapClosedPos").vector3Value = closedPos;
        so.FindProperty("flapOpenPos").vector3Value = openPos;
        so.ApplyModifiedPropertiesWithoutUndo();
        cup.SetActive(false);
        look.SetActive(false);
        Log($"컵 시연: 손 높이 {handAtSlot.y:0.00}m, 투입구 지점 비율 {f:0.00}, 포물선 높이 {arc:0.00}m, 입 {(mouthTf != null ? "만듦" : "없음")}, 덮개 {(flap != null ? "있음" : "없음")}");
        if (flap != null)
        {
            Vector3 pivot = flap.position;
            Func<Vector3, Vector3> toOpen;
            if (FlapSlideUp) toOpen = pt => pt + slideWorld;
            else toOpen = pt => pivot + flapTurn * (pt - pivot);
            CheckFlapClearance(flap, toOpen, guide, stand, faceSlot * Vector3.forward);
        }
        // 착지점이 CCTV 화면에 보이는지 (모니터로 봐야 시연이 진행되므로 화면 밖이면 안 됨)
        var cctvCam = root.Find("CctvCamera")?.GetComponent<Camera>();
        if (cctvCam != null)
        {
            Vector3 vp = cctvCam.WorldToViewportPoint(land + Vector3.up * 0.05f);
            bool inView = vp.z > 0f && vp.x > 0.08f && vp.x < 0.92f && vp.y > 0.08f && vp.y < 0.92f;
            if (inView) Log($"컵 착지점 CCTV 화면 안: ({vp.x:0.00}, {vp.y:0.00})");
            else Warn($"컵 착지점이 CCTV 화면 밖: ({vp.x:0.00}, {vp.y:0.00}, 깊이 {vp.z:0.00}) — CupLand를 격리실 가운데 쪽으로");
        }
        return demo;
    }

    // ===== 1 기상 연출 (2026-10-21) =====
    const string WakeVolumeProfilePath = "Assets/Scenes/Tutorial/VP_Wake.asset";

    // 전용 시네머신 카메라 + Canvas 눈꺼풀(자막 박스보다 아래 층) + 흐림 볼륨(피사계 심도·비네트) + 메인 카메라 후처리 켜기
    static TutorialWakeUp BuildWakeUp(Scene scene, Transform root, TutorialGuide guide)
    {
        var pm = Object.FindFirstObjectByType<PlayerMove>();
        var playerCam = pm != null ? pm.cinemachineCamera : null;
        if (playerCam == null) { Warn("기상 연출: 플레이어 시네머신 카메라를 못 찾아 건너뜀"); return null; }

        var camGo = new GameObject("WakeCam");
        camGo.transform.SetParent(root, false);
        var wakeCam = camGo.AddComponent<CinemachineCamera>();
        wakeCam.Lens = playerCam.Lens;
        wakeCam.Priority = 100;

        // 흐림 볼륨
        var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(WakeVolumeProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            AssetDatabase.CreateAsset(profile, WakeVolumeProfilePath);
        }
        if (!profile.TryGet(out UnityEngine.Rendering.Universal.DepthOfField dof))
        {
            dof = profile.Add<UnityEngine.Rendering.Universal.DepthOfField>(true);
            AssetDatabase.AddObjectToAsset(dof, profile);
        }
        dof.mode.Override(UnityEngine.Rendering.Universal.DepthOfFieldMode.Bokeh);
        dof.focusDistance.Override(0.1f);
        dof.aperture.Override(1f);
        dof.focalLength.Override(300f);
        if (!profile.TryGet(out UnityEngine.Rendering.Universal.Vignette vig))
        {
            vig = profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);
            AssetDatabase.AddObjectToAsset(vig, profile);
        }
        vig.intensity.Override(0.45f);
        vig.smoothness.Override(0.6f);
        vig.color.Override(Color.black);
        EditorUtility.SetDirty(dof); EditorUtility.SetDirty(vig); EditorUtility.SetDirty(profile);

        var volGo = new GameObject("WakeBlurVolume");
        volGo.transform.SetParent(root, false);
        var vol = volGo.AddComponent<UnityEngine.Rendering.Volume>();
        vol.isGlobal = true;
        vol.priority = 10f;
        vol.weight = 0f;
        vol.sharedProfile = profile;
        vol.enabled = false;

        // 메인 카메라 후처리 (튜토리얼 씬만)
        var mainCam = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).FirstOrDefault(c => c.CompareTag("MainCamera"));
        if (mainCam != null)
        {
            var data = mainCam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (data == null) data = mainCam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            EditorUtility.SetDirty(data);
        }
        else Warn("기상 연출: MainCamera가 없어 후처리를 켜지 못함 (흐림 없이 눈꺼풀만)");

        // 눈꺼풀 (Canvas는 TutorialGameplay 밖이라 이전 것을 이름으로 지우고 다시 만든다). 자막 박스는 이 뒤에 PausePage 앞으로 들어가므로 눈꺼풀보다 위
        EyelidOverlay lids = null;
        var canvas = Root(scene, "Canvas");
        if (canvas != null)
        {
            var old = canvas.transform.Find("WakeEyelids");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var pause = canvas.transform.Find("PausePage");
            var lidGo = new GameObject("WakeEyelids", typeof(RectTransform));
            lidGo.transform.SetParent(canvas.transform, false);
            lidGo.transform.SetSiblingIndex(pause != null ? pause.GetSiblingIndex() : canvas.transform.childCount - 1);
            var lrt = (RectTransform)lidGo.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            lids = lidGo.AddComponent<EyelidOverlay>();
            var lso = new SerializedObject(lids);
            lso.FindProperty("blurVolume").objectReferenceValue = vol;
            lso.ApplyModifiedPropertiesWithoutUndo();
        }
        else Warn("기상 연출: Canvas가 없어 눈꺼풀을 만들지 못함");

        var go = new GameObject("WakeUp");
        go.transform.SetParent(root, false);
        var wake = go.AddComponent<TutorialWakeUp>();
        var so = new SerializedObject(wake);
        so.FindProperty("eyelids").objectReferenceValue = lids;
        so.FindProperty("wakeCam").objectReferenceValue = wakeCam;
        so.FindProperty("playerCam").objectReferenceValue = playerCam;
        so.FindProperty("lookAtPerson").objectReferenceValue = guide != null ? guide.transform : null;
        so.ApplyModifiedPropertiesWithoutUndo();
        Log($"기상 연출: WakeCam·눈꺼풀·흐림 볼륨 준비, 메인 카메라 후처리 {(mainCam != null ? "켬" : "없음")}");
        return wake;
    }

    // ===== 7-3 실패 → 재시작 (2026-10-21) =====
    static readonly Vector2 RetryStand = new Vector2(51.3f, 22.3f);     // 연습 구역 입구 문(51, 25.5) 앞, 한 주임(50.2, 23.6) 옆
    static readonly Vector2 PracticeDoorPoint = new Vector2(51f, 25.5f);

    static void BuildRetry(Transform root, Transform map, Axes ax, TutorialGuide guide, TutorialPracticeFlow flow, DoorController stainDoor, GameObject[] disable)
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        var pm = Object.FindFirstObjectByType<PlayerMove>();
        float py = player != null ? player.transform.position.y : 0.725f;
        Vector3 pos = ax.U(RetryStand, py);
        Vector3 dir = ax.U(PracticeDoorPoint, py) - pos;
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        var go = new GameObject("PracticeRetry");
        go.transform.SetParent(root, false);
        var r = go.AddComponent<TutorialRetry>();
        var so = new SerializedObject(r);
        SetArray(so.FindProperty("disableOnRetry"), disable.Cast<Object>().ToArray());
        SetArray(so.FindProperty("openOnRetry"), stainDoor != null ? new Object[] { stainDoor } : new Object[0]);
        so.FindProperty("playerPosition").vector3Value = pos;
        so.FindProperty("playerYaw").floatValue = yaw;
        so.FindProperty("playerCam").objectReferenceValue = pm != null ? pm.cinemachineCamera : null;
        so.FindProperty("guide").objectReferenceValue = guide;
        so.FindProperty("guideLeg").stringValue = "ToPractice";
        so.FindProperty("practice").objectReferenceValue = flow;
        so.FindProperty("fader").objectReferenceValue = new SerializedObject(flow).FindProperty("fader").objectReferenceValue;
        so.ApplyModifiedPropertiesWithoutUndo();
        Log($"연습 판 재시작: 끌 것 {disable.Length}개 ({string.Join(", ", disable.Select(g => g.name))}), 플레이어 자리 {pos}, 방향 {yaw:0}°");
    }

    static void BuildDirector(Scene scene, Transform root, Axes ax, TutorialPracticeFlow flow, TutorialZoneActivator act, TutorialGuide guide, TutorialCupDemo cupDemo, TutorialWakeUp wakeUp)
    {
        var markers = Child(root, "Markers");
        foreach (var (name, rect) in Zones)
        {
            var z = new GameObject(name).AddComponent<TutorialZone>();
            z.transform.SetParent(markers, false);
            z.bounds = RectBounds(ax, rect, 1.5f, 4f);
        }
        foreach (var (name, pts) in Paths)
        {
            var pa = new GameObject(name).AddComponent<TutorialPath>();
            pa.transform.SetParent(markers, false);
            pa.points = pts.Select(v => ax.U(v)).ToArray();
        }

        var (subtitle, moveKeys) = BuildSubtitleUI(scene);

        var go = new GameObject("TutorialDirector");
        go.transform.SetParent(root, false);
        var dir = go.AddComponent<TutorialDirector>();
        var so = new SerializedObject(dir);
        var script = AssetDatabase.LoadAssetAtPath<TextAsset>(ScriptAssetPath);
        if (script == null) Warn($"대사 데이터 없음: {ScriptAssetPath}");
        so.FindProperty("scriptAsset").objectReferenceValue = script;
        so.FindProperty("subtitle").objectReferenceValue = subtitle;
        so.FindProperty("markersRoot").objectReferenceValue = markers;
        so.FindProperty("practice").objectReferenceValue = flow;
        so.FindProperty("practiceActivator").objectReferenceValue = act;
        so.FindProperty("moveKeys").objectReferenceValue = moveKeys;
        so.FindProperty("guide").objectReferenceValue = guide;
        so.FindProperty("cupDemo").objectReferenceValue = cupDemo;
        so.FindProperty("wakeUp").objectReferenceValue = wakeUp;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // 이동 키 ㅗ 배치 (1080 기준 px). 칸 좌표 × (키 + 간격) = 묶음 아래 가운데 기준 자리
    const float MoveKeySize = 72f, MoveKeyGap = 8f, MoveKeysY = 250f, MoveKeyFontSize = 40f;
    static readonly Color MoveKeyColor = new Color(0f, 0f, 0f, 0.6f);
    static readonly (string, Vector2)[] MoveKeyCells =
    {
        ("W", new Vector2(0f, 1f)),
        ("A", new Vector2(-1f, 0f)), ("S", new Vector2(0f, 0f)), ("D", new Vector2(1f, 0f)),
    };

    // Canvas 아래 공용 자막 박스(프리팹) + 튜토리얼 전용 이동 키 표시. Canvas는 TutorialGameplay 밖이라 이전 것을 이름으로 찾아 지우고 다시 넣는다
    static (SubtitleBox, GameObject) BuildSubtitleUI(Scene scene)
    {
        var canvas = Root(scene, "Canvas");
        if (canvas == null) { Warn("Canvas가 없어 자막을 만들지 못함"); return (null, null); }
        foreach (var n in new[] { "SubtitleBox", "TutorialSubtitle", "TutorialMoveKeys" })
        {
            var old = canvas.transform.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        // 일시정지 화면·설정창보다 아래에 그려지도록 PausePage 앞에 끼운다
        var pause = canvas.transform.Find("PausePage");
        int index = pause != null ? pause.GetSiblingIndex() : canvas.transform.childCount;

        var prefab = SubtitleBoxFactory.EnsurePrefab();
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        inst.name = "SubtitleBox";
        inst.transform.SetParent(canvas.transform, false);
        inst.transform.SetSiblingIndex(index);
        var sub = inst.GetComponent<SubtitleBox>();

        // 1-3 이동 키 표시: 키보드처럼 ㅗ 모양 (W 위, A S D 아래). 2026-10-24 글자 한 줄 → 키 상자 4개
        // 묶음 오브젝트(TutorialMoveKeys)를 Director가 켜고 끈다. 진짜 키 아이콘이 생기면 상자 스프라이트만 바꾼다
        var keysGo = new GameObject("TutorialMoveKeys", typeof(RectTransform));
        keysGo.transform.SetParent(canvas.transform, false);
        keysGo.transform.SetSiblingIndex(index + 1);
        var krt = (RectTransform)keysGo.transform;
        krt.anchorMin = krt.anchorMax = krt.pivot = new Vector2(0.5f, 0f);
        krt.anchoredPosition = new Vector2(0f, MoveKeysY);
        krt.sizeDelta = new Vector2(MoveKeySize * 3f + MoveKeyGap * 2f, MoveKeySize * 2f + MoveKeyGap);
        var keySprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        var font = SubtitleBoxFactory.LoadFont();
        foreach (var (letter, cell) in MoveKeyCells)
        {
            var key = new GameObject("Key_" + letter, typeof(RectTransform));
            key.transform.SetParent(krt, false);
            var rt = (RectTransform)key.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = cell * (MoveKeySize + MoveKeyGap);
            rt.sizeDelta = new Vector2(MoveKeySize, MoveKeySize);
            var img = key.AddComponent<UnityEngine.UI.Image>();
            img.sprite = keySprite;
            img.type = UnityEngine.UI.Image.Type.Sliced;
            img.color = MoveKeyColor;
            img.raycastTarget = false;
            var label = SubtitleBoxFactory.MakeText(key.transform, "Label", font, MoveKeyFontSize, Color.white);
            var lrt = (RectTransform)label.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            label.alignment = TMPro.TextAlignmentOptions.Center;
            label.text = letter;
        }
        keysGo.SetActive(false);

        return (sub, keysGo);
    }

    // HUD 얼룩·빈대 개수 (Canvas/StainCount, DollCount). 처음엔 꺼 두고 연습 구역 입장 때 PracticeFlow가 켠다
    // 리그를 복사할 때 GameHUD 연결을 비웠으므로 여기서 다시 잇는다. 경고 문구는 계속 비워 둔다 (문 앞 얼룩 6개만으로 경고 기준 5를 넘음)
    static List<GameObject> HudCountObjects(Scene scene)
    {
        var list = new List<GameObject>();
        var canvas = Root(scene, "Canvas");
        var hud = Object.FindFirstObjectByType<GameHUD>();
        if (canvas == null || hud == null) { Warn("Canvas 또는 GameHUD가 없어 개수 표시를 연결하지 못함"); return list; }
        var so = new SerializedObject(hud);
        foreach (var (field, child) in new[] { ("stainCountText", "StainCount"), ("dollCountText", "DollCount") })
        {
            var t = canvas.transform.Find(child);
            if (t == null) { Warn($"Canvas/{child} 없음"); continue; }
            so.FindProperty(field).objectReferenceValue = t.GetComponent<TMPro.TMP_Text>();
            t.gameObject.SetActive(false);
            list.Add(t.gameObject);
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return list;
    }

    static void WireProgressManager(Scene scene, Transform map)
    {
        // 전체 승패(GameProgressManager): 모든 빈대·얼룩이 끝나는 순간 = 연습 판 승리 → 들어온 입구 문을 연다 (2026-10-18 결정)
        var gpm = Object.FindFirstObjectByType<GameProgressManager>();
        var door = FindDeep(map, PracticeDoorName)?.GetComponent<DoorController>();
        if (gpm == null || door == null) { Warn("GameProgressManager 또는 연습 구역 입구 문을 찾지 못해 승리 문 연결을 건너뜀"); return; }
        var so = new SerializedObject(gpm);
        so.FindProperty("door").objectReferenceValue = door;
        so.FindProperty("externalDefeat").boolValue = true;   // 패배 → 결과 화면 대신 연습 판 재시작 (2026-10-21)
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void AddToBuildSettings()
    {
        var list = EditorBuildSettings.scenes.ToList();
        int idx = list.FindIndex(s => s.path == ScenePath);
        if (idx >= 0)
        {
            if (!list[idx].enabled) { list[idx].enabled = true; EditorBuildSettings.scenes = list.ToArray(); }
            return;
        }
        int main = list.FindIndex(s => s.path.EndsWith("/MainScene.unity"));
        list.Insert(main + 1, new EditorBuildSettingsScene(ScenePath, true));   // 메인 메뉴 바로 다음 (씬 전환은 이름으로 하므로 순서는 표시용)
        EditorBuildSettings.scenes = list.ToArray();
        Log("빌드 설정에 Tutorial.unity 추가");
    }

    // ===================== 5. 검사 =====================

    static string Validate(List<(Bindae spec, DollMove doll)> dolls, Axes ax)
    {
        var lines = new List<string>();
        var path = new NavMeshPath();
        int wpOk = 0, wpAll = 0, pathOk = 0, pathAll = 0;
        foreach (var (b, d) in dolls)
        {
            var wps = d.wayPoints;
            bool has0 = NavMesh.SamplePosition(wps[0].position, out var h0, 0.35f, NavMesh.AllAreas);
            foreach (var w in wps)
            {
                wpAll++;
                if (NavMesh.SamplePosition(w.position, out var h, 0.35f, NavMesh.AllAreas)) wpOk++;
                else
                {
                    string near = NavMesh.SamplePosition(w.position, out var h2, 3f, NavMesh.AllAreas) ? $"가장 가까운 NavMesh {Vector3.Distance(w.position, h2.position):0.00}m" : "3m 안에 없음";
                    lines.Add($"  웨이포인트 NavMesh 없음: {b.name}/{w.name} ({near})");
                    continue;
                }
                if (!has0 || w == wps[0]) continue;
                pathAll++;
                if (NavMesh.CalculatePath(h0.position, h.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) pathOk++;
                else lines.Add($"  경로 끊김: {b.name} {wps[0].name} → {w.name} ({path.status})");
            }
        }
        lines.Insert(0, $"웨이포인트 NavMesh {wpOk}/{wpAll}, 경로 {pathOk}/{pathAll}");

        // 웃음소리 거리 규칙 (3d 모델링 문서): 방 B는 서쪽 벽에서, 연습 구역은 입구에서 3.5m 이상
        foreach (var b in Bindaes.Where(x => x.laugh))
            foreach (var w in b.waypoints)
            {
                float dist = b.zone == "RoomB" ? w.x - RoomBWestWall : b.zone == "Practice" ? Vector2.Distance(w, PracticeEntrance) : 99f;
                if (dist < LaughDistance) lines.Add($"  웃음소리 거리 미달: {b.name} {w} → {dist:0.00}m");
            }
        // 방 B는 자리 섞기로 상자 3자리 어디서든 시작하므로 그 자리들도 검사 (2026-11-02)
        foreach (var w in RoomBSlotsForCheck)
            if (w.x - RoomBWestWall < LaughDistance) lines.Add($"  웃음소리 거리 미달: 방 B 자리 {w} → {w.x - RoomBWestWall:0.00}m");
        float minB = Bindaes.Where(x => x.zone == "RoomB").SelectMany(x => x.waypoints).Concat(RoomBSlotsForCheck).Min(w => w.x - RoomBWestWall);
        float minP = PracticeWaypoints.Min(w => Vector2.Distance(w, PracticeEntrance));
        lines.Add($"웃음소리 거리: 방 B 서쪽 벽 최소 {minB:0.00}m, 연습 구역 입구 최소 {minP:0.00}m (기준 {LaughDistance}m)");
        return string.Join("\n", lines);
    }

    // ===================== 확인용 캡처 (CLI, 저장 안 함) =====================

    /// <summary>CLI 확인용: 시작 시점·문 앞·연습 구역·격리실·방 B를 PNG로 찍고 리그 상태를 로그로 (출력 폴더 = 환경 변수 TUTORIAL_CAPTURE_DIR)</summary>
    public static void CaptureBatch()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var map = Root(scene, MapRootName).transform;
            var ax = DetectAxes(map);
            string dir = Environment.GetEnvironmentVariable("TUTORIAL_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = "Temp";
            Directory.CreateDirectory(dir);

            var start = Root(scene, "PlayerStart").transform;
            Shot(Path.Combine(dir, "tut_start_eye.png"), start.position + Vector3.up * 1.3485f, Quaternion.Euler(0f, start.eulerAngles.y, 0f), 50f, false, 0f);
            Top(dir, "tut_top_doorfront.png", ax, new Vector2(45.75f, 20.25f), 2.6f);
            Top(dir, "tut_top_practice.png", ax, new Vector2(51.75f, 30.75f), 5.6f);
            Top(dir, "tut_top_isolation.png", ax, new Vector2(23.5f, 15.0f), 5.0f);
            Top(dir, "tut_top_rooms_ab.png", ax, new Vector2(31.0f, 19.5f), 5.0f);
            // 문 앞 입구에서 플레이어 눈높이로 얼룩 줄 보기
            var eye = ax.U(new Vector2(43.9f, 19.4f), 1.3485f);
            var look = ax.U(new Vector2(46.6f, 21.2f), 0.2f);
            Shot(Path.Combine(dir, "tut_doorfront_eye.png"), eye, Quaternion.LookRotation(look - eye), 50f, false, 0f);

            var lines = new List<string>();
            lines.Add($"Camera.main = {(Camera.main != null ? Camera.main.name : "없음")}, AudioListener {Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length}개");
            var player = GameObject.FindGameObjectWithTag("Player");
            lines.Add($"Player = {(player != null ? player.name + " " + player.transform.position + " yaw " + player.transform.eulerAngles.y : "없음")}");
            var gpm = Object.FindFirstObjectByType<GameProgressManager>();
            if (gpm != null) lines.Add($"GameProgressManager.door = {new SerializedObject(gpm).FindProperty("door").objectReferenceValue}");
            var sfm = Object.FindFirstObjectByType<StainFreshnessManager>();
            if (sfm != null) lines.Add($"StainFreshnessManager.settings = {new SerializedObject(sfm).FindProperty("settings").objectReferenceValue}");
            foreach (var d in Object.FindObjectsByType<DollMove>(FindObjectsSortMode.None).OrderBy(x => x.name))
            {
                var col = d.GetComponentInChildren<Collider>();
                lines.Add($"{d.name}: tag {d.tag}, 충돌체 {(col != null ? col.name + "/" + col.tag + "/" + LayerMask.LayerToName(col.gameObject.layer) : "없음")}, 웨이포인트 {d.wayPoints.Length}, 집계 {d.CountsTowardGoal}, 웃음 {d.GetComponent<DollLaugh>() != null}");
            }
            var cams = Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
            lines.Add($"시네머신 카메라 {cams.Length}개: {string.Join(", ", cams.Select(c => c.name))}");
            // 저장된 씬을 다시 연 상태에서 얼룩 위치 확인 — 문 앞 방(x 43.5~48, z 18~22.5) 밖이면 저장 문제
            foreach (var s in Object.FindObjectsByType<StainFreshness>(FindObjectsSortMode.None).OrderBy(x => x.name))
            {
                Vector3 w = s.transform.position;
                var bp = new Vector2(w.x / ax.sx, w.z / ax.sz);
                bool inRoom = bp.x > 43.5f && bp.x < 48f && bp.y > 18f && bp.y < 22.5f;
                lines.Add($"{s.name}: 평면 ({bp.x:0.00}, {bp.y:0.00}) {(inRoom ? "OK" : "문 앞 방 밖!")}");
            }
            Log("캡처 확인\n" + string.Join("\n", lines));
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError($"[튜토리얼 배치] 캡처 실패: {e}");
            EditorApplication.Exit(1);
        }
    }

    static void Top(string dir, string file, Axes ax, Vector2 center, float half)
    {
        // 천장(4.5) 아래에서 내려다본다
        Shot(Path.Combine(dir, file), ax.U(center, 4.3f), Quaternion.Euler(90f, 0f, 0f), 0f, true, half);
    }

    static void Shot(string path, Vector3 pos, Quaternion rot, float fov, bool ortho, float orthoSize)
    {
        var go = new GameObject("__CaptureCam");
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.orthographic = ortho;
            if (ortho) cam.orthographicSize = orthoSize; else cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
            int w = 1280, h = 720;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = prev;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
        finally { Object.DestroyImmediate(go); }
    }

    // ===================== 도우미 =====================

    struct Axes
    {
        public float sx, sz;
        public Vector3 U(Vector2 b, float y = 0f) => new Vector3(sx * b.x, y, sz * b.y);
    }

    // 맵의 방 A 상자(Blender 24.75, 20.25)로 Blender → Unity 축 부호를 확인 (TutorialSceneBuilder와 같은 변환)
    static Axes DetectAxes(Transform map)
    {
        var box = FindDeep(map, "TK_Prop_Box");
        if (box == null) throw new Exception("축 확인용 TK_Prop_Box가 맵에 없음");
        return new Axes { sx = Mathf.Sign(box.position.x / 24.75f), sz = Mathf.Sign(box.position.z / 20.25f) };
    }

    static Bounds RectBounds(Axes ax, Vector4 r, float centerY, float height)
    {
        Vector3 a = ax.U(new Vector2(r.x, r.y)), c = ax.U(new Vector2(r.z, r.w));
        var b = new Bounds((a + c) * 0.5f, new Vector3(Mathf.Abs(c.x - a.x), height, Mathf.Abs(c.z - a.z)));
        b.center = new Vector3(b.center.x, centerY, b.center.z);
        return b;
    }

    static GameObject Root(Scene scene, string name) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            var r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }

    static Transform Child(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t;
        t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    // 프리팹 인스턴스의 Transform·렌더러를 코드로 바꾼 뒤 반드시 호출 — 안 하면 마지막으로 만든 인스턴스의 변경이 씬 저장에서 빠질 수 있다
    static void RecordInstance(params Object[] targets)
    {
        foreach (var t in targets)
            if (t != null && PrefabUtility.IsPartOfPrefabInstance(t)) PrefabUtility.RecordPrefabInstancePropertyModifications(t);
    }

    static void SetArray(SerializedProperty p, Object[] items)
    {
        p.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }

    static void Log(string m) => Debug.Log($"[튜토리얼 배치] {m}");
    static void Warn(string m) => Debug.LogWarning($"[튜토리얼 배치] {m}");
}
