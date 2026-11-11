using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 맵 인트로 배치 도구 (2026-10-23 인형방, 2026-10-24 할로윈 방 추가 → 맵 표로 일반화).
/// 메뉴 Tools > 인트로 > (맵) 인트로 배치 / CLI: -executeMethod MapIntroBuilder.ApplyBatch(인형방) · ApplyBatchHalloween · ApplyBatchAll (Unity 에디터를 끈 상태)
/// - 맵마다 다른 것은 Maps 표 한 줄 (씬, 데이터 에셋, 카메라 길, 데이터 처음 값). 런타임 코드는 맵을 모른다
/// - 루트 MapIntro(인트로 카메라·초상 촬영장·MapIntro)는 매번 새로 만든다 → 그 아래는 손으로 고치지 않기
/// - 카메라 길(IntroPath)은 이미 있으면 그대로 둔다 → 씬에서 손본 점이 유지됨. 처음 값으로: Tools > 인트로 > 인형방 카메라 길 다시 만들기
/// - UI 프리팹·데이터 에셋은 없을 때만 만든다
/// - 끝에서 길을 검사해 콘솔에 남긴다 (천장 높이, 가구와 겹침)
/// 설계 기록: 프로젝트 문서 「코드 설계 - 맵 인트로·무전」 3.8
/// </summary>
public static class MapIntroBuilder
{
    const string DataFolder = "Assets/Data/Intro";
    const string RtPath = DataFolder + "/RT_RadioPortrait.renderTexture";
    const string ModelDir = "Assets/3DModel&Asset/TrainingRoom/";
    const string HanModel = ModelDir + "Models/CH_HanJuim.fbx";
    const string HanGear = ModelDir + "Models/CH_HanJuim_FieldGear.fbx";
    const string MatDir = ModelDir + "Materials/";

    const float CeilingY = 4.5f;          // 사용자 확인 2026-10-23 (벽 높이와 같음)
    const float CeilingMargin = 0.3f;
    const float ClearRadius = 0.3f;

    // ───────────────────────── 맵 표 ─────────────────────────
    // 카메라 길 (Unity x, y, z / 도착 시각 / 보는 곳). 마지막 점은 플레이어 눈 (실행 시 계산)
    // setup: 데이터 에셋을 처음 만들 때 넣을 문구 (이미 있으면 손대지 않음 → 손으로 고친 문구 보존)
    class MapEntry
    {
        public string label, scenePath, dataPath;
        public (Vector3 pos, float time, Vector3 look)[] points;
        public float landTime;
        public Action<MapIntroScript> setup;
        public string wrongGuessLine, stainWarningLine;   // 게임 도중 무전 (데이터 칸이 비어 있을 때만 넣음)
    }

    // 인형방 (2026-10-23): 높이 3.7m — 가장 높은 윗면 벽선반 3.2m. 문구는 MapIntroScript 기본값
    static readonly MapEntry DollRoom = new MapEntry
    {
        label = "인형방",
        scenePath = "Assets/Scenes/DollManiaHouse.unity",
        dataPath = DataFolder + "/DollManiaHouse_Intro.asset",
        landTime = 17.5f,
        points = new[]
        {
            (new Vector3( 6.3f, 3.9f,  -7.3f), 0.0f,  new Vector3(-3f, 0.3f, 4f)),
            (new Vector3( 6.3f, 3.75f, -4.0f), 3.5f,  new Vector3(-2f, 0.5f, 2f)),
            (new Vector3( 6.3f, 3.7f,   1.0f), 5.6f,  new Vector3( 0f, 0.5f, 0.5f)),
            (new Vector3( 5.3f, 3.7f,   6.8f), 7.6f,  new Vector3( 0f, 0.5f, 0.5f)),
            (new Vector3( 0.0f, 3.7f,   7.0f), 9.3f,  new Vector3( 0f, 0.5f, 0.5f)),
            (new Vector3(-5.8f, 3.7f,   6.5f), 11.0f, new Vector3( 0f, 0.5f, 0.5f)),
            (new Vector3(-6.2f, 3.7f,   0.5f), 12.8f, new Vector3( 0f, 0.5f, 0.5f)),
            (new Vector3(-5.5f, 3.6f,  -6.8f), 14.8f, new Vector3( 0f, 0.5f, 0.5f)),
        },
        setup = null,
        wrongGuessLine = "축하한다. 멀쩡한 물건 하나 잡았네.",
        stainWarningLine = "얼룩 꼴 봐라. 집주인 깨기 전에 닦아.",
    };

    // 할로윈 방 (2026-10-24): 높이 4.0m — 방 안 나무 꼭대기 최대 3.45m, 납골당 지붕 2.6m. 문구·대사는 제안 단계
    static readonly MapEntry Halloween = new MapEntry
    {
        label = "할로윈 방",
        scenePath = "Assets/Scenes/HalloweenNight.unity",
        dataPath = DataFolder + "/HalloweenNight_Intro.asset",
        landTime = 17.5f,
        points = new[]
        {
            (new Vector3( 6.3f, 4.1f, -7.3f), 0.0f,  new Vector3(-3f, 0.3f, 4f)),
            (new Vector3( 6.3f, 4.0f, -4.0f), 3.5f,  new Vector3(-2f, 0.5f, 2f)),
            (new Vector3( 6.3f, 4.0f,  1.0f), 5.6f,  new Vector3( 0f, 0.6f, 1.5f)),
            (new Vector3( 5.6f, 4.0f,  7.0f), 7.6f,  new Vector3( 0f, 0.6f, 1.0f)),
            (new Vector3( 0.0f, 4.0f,  7.2f), 9.3f,  new Vector3( 0f, 0.6f, 1.0f)),
            (new Vector3(-5.8f, 4.0f,  6.6f), 11.0f, new Vector3( 0f, 0.6f, 1.0f)),
            (new Vector3(-6.3f, 4.0f,  0.5f), 12.8f, new Vector3( 0f, 0.6f, 1.0f)),
            (new Vector3(-5.5f, 3.8f, -6.8f), 14.8f, new Vector3( 0f, 0.6f, 1.0f)),
        },
        setup = d =>
        {
            d.areaCode = "OPERATION AREA 02";
            d.bandAreaCode = "AREA 02";
            d.areaName = "할로윈 묘지 세트장";
            d.areaDetail = "파티 대여업체 창고 · 야간 소등";
            d.complaints = new[] { "민원 1 · 호박이 하나씩 사라짐", "민원 2 · 아무도 없는데 웃음소리" };
            d.lines = new[]
            {
                new RadioLine { time = 3.5f,  text = "두 번째 현장. 파티 소품 창고다." },
                new RadioLine { time = 6.6f,  text = "호박이 하나씩 없어진댄다. 먹힌 거지." },
                new RadioLine { time = 10.5f, text = "어두워서 잘 안 보일 거다. 위도 봐 둬." },
                new RadioLine { time = 13.9f, text = "웃음소리 들리면 그쪽이다. 귀 열어." },
                new RadioLine { time = 17.5f, text = "...가자." },
            };
        },
        wrongGuessLine = "그녀석은 무죄야.",
        stainWarningLine = "바닥이 웃음 천지다. 빨리 닦아.",
    };

    // 초상 촬영장
    static readonly Vector3 StudioPos = new Vector3(0f, -500f, 0f);
    const float PortraitYaw = 0f;          // 모델 정면 = +Z (「3d 모델링」). 뒤통수가 찍히면 180
    const float PortraitAimY = 1.2f;       // 바라볼 높이 (모델 총 1.55)
    const float PortraitDistance = 2.0f;
    const float PortraitFov = 30f;
    const int RtW = 272, RtH = 208;        // 무전기 화면 136×104 × 2

    // CH_HanJuim_FieldGear 재질 (Blender tk_gear.gear_mats 슬롯 순서)
    static readonly string[] GearMats = { "TK_Gear_Rubber", "TK_Gear_Navy", "TK_Gear_White", "TK_Metal", "TK_Gear_Hose", "TK_Gear_Blue", "TK_Gear_Lens", "TK_Gear_MaskGray", "TK_Gear_Filter" };

    static readonly string[] HudNames = { "DollCount", "StainCount" };

    [MenuItem("Tools/인트로/인형방 인트로 배치")]
    public static void ApplyMenu() => ApplyFromMenu(DollRoom, false);

    [MenuItem("Tools/인트로/인형방 카메라 길 다시 만들기")]
    public static void RebuildPathMenu() => ApplyFromMenu(DollRoom, true);

    [MenuItem("Tools/인트로/할로윈 방 인트로 배치")]
    public static void ApplyHalloweenMenu() => ApplyFromMenu(Halloween, false);

    [MenuItem("Tools/인트로/할로윈 방 카메라 길 다시 만들기")]
    public static void RebuildHalloweenPathMenu() => ApplyFromMenu(Halloween, true);

    static void ApplyFromMenu(MapEntry m, bool rebuildPath)
    {
        if (rebuildPath && !EditorUtility.DisplayDialog("인트로 카메라 길", $"{m.label} 카메라 길 점을 처음 값으로 다시 만듭니다. 씬에서 옮긴 점은 사라집니다.", "다시 만들기", "취소")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Apply(m, rebuildPath);
    }

    public static void ApplyBatch() => Batch(DollRoom);
    public static void ApplyBatchHalloween() => Batch(Halloween);
    public static void ApplyBatchAll() { Batch(DollRoom); Batch(Halloween); }

    static void Batch(MapEntry m)
    {
        try { Apply(m, false); }
        catch (Exception e) { Debug.LogError($"[인트로] {m.label} 실패: {e}"); EditorApplication.Exit(1); }
    }

    static void Apply(MapEntry m, bool rebuildPath)
    {
        if (EditorApplication.isPlaying) throw new Exception("플레이 모드에서는 실행할 수 없음");
        var scene = EditorSceneManager.OpenScene(m.scenePath, OpenSceneMode.Single);

        var cm = UnityEngine.Object.FindObjectsByType<CameraManagement>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (cm == null) throw new Exception("CameraManagement가 씬에 없음");
        var cmSo = new SerializedObject(cm);
        var playerCam = cmSo.FindProperty("playerCam").objectReferenceValue as CinemachineCamera;
        if (playerCam == null) throw new Exception("CameraManagement.playerCam이 비어 있음");

        var data = EnsureData(m);
        var radioPrefab = MapIntroFactory.EnsureRadio();
        var overlayPrefab = MapIntroFactory.EnsureOverlay();

        // ── 루트 새로 만들기 ──
        foreach (var old in scene.GetRootGameObjects().Where(g => g.name == "MapIntro").ToArray())
            UnityEngine.Object.DestroyImmediate(old);
        var root = new GameObject("MapIntro");
        SceneManager.MoveGameObjectToScene(root, scene);

        // ── 인트로 카메라 (렌즈는 플레이어 카메라와 같게 → 착지 컷이 보이지 않음) ──
        var camGo = new GameObject("IntroCinemachineCamera");
        camGo.transform.SetParent(root.transform, false);
        var introCam = camGo.AddComponent<CinemachineCamera>();
        introCam.Lens = playerCam.Lens;
        introCam.Priority = 100;
        introCam.enabled = false;

        // ── 카메라 길 ──
        GetPlayerEye(playerCam, out var eyePos, out var eyeRot);
        var path = BuildOrKeepPath(m, scene, rebuildPath, eyePos, eyeRot);

        // ── 초상 촬영장 ──
        var portrait = BuildStudio(root.transform);

        // ── UI ──
        var canvas = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Canvas");
        if (canvas == null) throw new Exception("Canvas가 씬에 없음");
        RemoveChildren(canvas.transform, "RadioBox", "MapIntroOverlay");
        int insertAt = UiInsertIndex(canvas.transform);
        var overlayGo = (GameObject)PrefabUtility.InstantiatePrefab(overlayPrefab, canvas.transform);
        overlayGo.transform.SetSiblingIndex(insertAt);
        var radioGo = (GameObject)PrefabUtility.InstantiatePrefab(radioPrefab, canvas.transform);
        radioGo.transform.SetSiblingIndex(insertAt + 1);
        var radio = radioGo.GetComponent<RadioBox>();
        var rso = new SerializedObject(radio);
        rso.FindProperty("portrait").objectReferenceValue = portrait;
        rso.ApplyModifiedPropertiesWithoutUndo();

        var hud = new List<GameObject>();
        foreach (var n in HudNames)
        {
            var t = canvas.transform.Find(n);
            if (t != null) hud.Add(t.gameObject); else Warn($"HUD '{n}'가 Canvas 아래에 없음 (숨기지 않음)");
        }

        // ── MapIntro 연결 ──
        var intro = root.AddComponent<MapIntro>();
        var so = new SerializedObject(intro);
        so.FindProperty("script").objectReferenceValue = data;
        so.FindProperty("path").objectReferenceValue = path;
        so.FindProperty("introCam").objectReferenceValue = introCam;
        so.FindProperty("overlay").objectReferenceValue = overlayGo.GetComponent<MapIntroOverlay>();
        so.FindProperty("radio").objectReferenceValue = radio;
        var hudProp = so.FindProperty("hudToHide");
        hudProp.arraySize = hud.Count;
        for (int i = 0; i < hud.Count; i++) hudProp.GetArrayElementAtIndex(i).objectReferenceValue = hud[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        // ── 게임 도중 무전 (첫 오답·얼룩 경고) ──
        var events = root.AddComponent<MapEventRadio>();
        var eso = new SerializedObject(events);
        eso.FindProperty("radio").objectReferenceValue = radio;
        eso.FindProperty("script").objectReferenceValue = data;
        eso.FindProperty("intro").objectReferenceValue = intro;
        eso.ApplyModifiedPropertiesWithoutUndo();
        Log($"{m.label} 이벤트 무전 연결: 첫 오답 \"{data.wrongGuessLine}\" / 얼룩 경고 \"{data.stainWarningLine}\"");

        cmSo.FindProperty("mapIntro").objectReferenceValue = intro;
        cmSo.ApplyModifiedPropertiesWithoutUndo();

        Log($"{m.label} 배치: 카메라 길 점 {path.GetComponentsInChildren<IntroPathPoint>().Length}개 / 무전 박스·인트로 화면 Canvas {insertAt}번째 / HUD 숨김 {hud.Count}개 / 데이터 {m.dataPath}");

        CheckPath(path, eyePos, eyeRot, root.transform);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("씬 저장 실패");
        Log($"{m.label} 저장 완료");
    }

    // ───────────────────────── 카메라 길 ─────────────────────────
    static IntroPath BuildOrKeepPath(MapEntry m, Scene scene, bool rebuild, Vector3 eyePos, Quaternion eyeRot)
    {
        var existing = scene.GetRootGameObjects().Select(g => g.GetComponent<IntroPath>()).FirstOrDefault(p => p != null);
        if (existing != null && !rebuild)
        {
            // 플레이어 눈 점만 지금 플레이어 자리에 맞춰 둔다 (미리 보기용, 실행 시 다시 계산)
            foreach (var pt in existing.GetComponentsInChildren<IntroPathPoint>())
                if (pt.endsAtPlayerEye) pt.transform.SetPositionAndRotation(eyePos, eyeRot);
            Log("카메라 길: 씬에 있던 길을 그대로 씀");
            return existing;
        }
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

        var go = new GameObject("IntroPath");
        SceneManager.MoveGameObjectToScene(go, scene);
        var path = go.AddComponent<IntroPath>();
        path.ceilingY = CeilingY;
        path.ceilingMargin = CeilingMargin;
        for (int i = 0; i < m.points.Length; i++)
        {
            var (pos, time, look) = m.points[i];
            var p = new GameObject($"P{i}");
            p.transform.SetParent(go.transform, false);
            p.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            p.AddComponent<IntroPathPoint>().time = time;
        }
        var eye = new GameObject($"P{m.points.Length}_PlayerEye");
        eye.transform.SetParent(go.transform, false);
        eye.transform.SetPositionAndRotation(eyePos, eyeRot);
        var ep = eye.AddComponent<IntroPathPoint>();
        ep.time = m.landTime; ep.endsAtPlayerEye = true;
        Log("카메라 길: 처음 값으로 만듦");
        return path;
    }

    static void GetPlayerEye(CinemachineCamera playerCam, out Vector3 pos, out Quaternion rot)
    {
        Transform target = playerCam.Follow;
        if (target == null) { var p = GameObject.FindGameObjectWithTag("Player"); target = p != null ? p.transform : null; }
        var follow = playerCam.GetComponent<CinemachineFollow>();
        Vector3 offset = follow != null ? follow.FollowOffset : new Vector3(0f, 0.6235f, 0f);
        pos = target != null ? target.position + target.rotation * offset : playerCam.transform.position;
        var pt = playerCam.GetComponent<CinemachinePanTilt>();
        rot = pt != null ? Quaternion.Euler(pt.TiltAxis.Value, pt.PanAxis.Value, 0f) : playerCam.transform.rotation;
    }

    // 길을 0.05초마다 찍어 천장·가구와 겹치는 곳을 찾는다 (Claude가 플레이 모드를 볼 수 없어 도구가 먼저 잡음)
    static void CheckPath(IntroPath path, Vector3 eyePos, Quaternion eyeRot, Transform introRoot)
    {
        Physics.SyncTransforms();
        path.SetPlayerEye(eyePos, eyeRot);
        float dur = path.Duration;
        var ceilingHits = new List<string>();
        var blockHits = new List<string>();
        var nearHits = new List<string>();
        // 콜라이더 없는 장식(할로윈 나무 등)도 잡으려고 렌더러 경계 상자도 본다. 상자라서 실제보다 넓게 잡힘 → 경고만
        var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
            .Where(r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer))
            .Where(r => !r.transform.IsChildOf(introRoot) && !r.transform.IsChildOf(path.transform))
            .Where(r => r.GetComponentInParent<DollMove>() == null)
            .Where(r => { var t = r.transform; while (t.parent != null) t = t.parent; return !t.CompareTag("Player") && t.name != "Player"; })
            .ToArray();
        float minY = float.MaxValue, maxY = float.MinValue;
        // 착지 직전(플레이어 몸 근처)은 플레이어 콜라이더와 당연히 겹치므로 플레이어·빈대·트리거는 뺀다
        for (float t = 0f; t <= dur + 0.001f; t += 0.05f)
        {
            path.Evaluate(t, out var p, out _);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            if (p.y > CeilingY - CeilingMargin) ceilingHits.Add($"{t:F2}s(y {p.y:F2})");
            foreach (var c in Physics.OverlapSphere(p, ClearRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.GetComponentInParent<DollMove>() != null) continue;
                var tr = c.transform;
                while (tr.parent != null) tr = tr.parent;
                if (tr.CompareTag("Player") || tr.name == "Player") continue;
                blockHits.Add($"{t:F2}s {c.name}");
                break;
            }
            foreach (var r in renderers)
            {
                if (r.bounds.SqrDistance(p) > ClearRadius * ClearRadius) continue;
                nearHits.Add(r.name);
                break;
            }
        }
        Log($"길 확인: {dur:F1}초, 높이 {minY:F2}~{maxY:F2}m (천장 {CeilingY}m)");
        if (ceilingHits.Count > 0) Warn($"길 확인: 천장 {CeilingY - CeilingMargin:F1}m보다 높은 곳 {ceilingHits.Count}곳 — {string.Join(", ", ceilingHits.Take(8))}");
        if (blockHits.Count > 0) Warn($"길 확인: 막힘 {blockHits.Count}곳 — {string.Join(", ", blockHits.Distinct().Take(10))}");
        if (ceilingHits.Count == 0 && blockHits.Count == 0) Log("길 확인: 막힘 0곳");
        if (nearHits.Count > 0)
        {
            var groups = nearHits.GroupBy(n => n).Select(g => $"{g.Key}({g.Count()})");
            Warn($"길 확인: 가까움(장식 경계 상자, 콜라이더 없음 포함) {nearHits.Count}곳 — {string.Join(", ", groups.Take(10))}. 상자라 실제보다 넓게 잡힘 → Scene 뷰 기즈모로 확인");
        }
    }

    // ───────────────────────── 초상 촬영장 ─────────────────────────
    static RadioPortrait BuildStudio(Transform parent)
    {
        var studio = new GameObject("RadioPortraitStudio");
        studio.transform.SetParent(parent, false);
        studio.transform.position = StudioPos;

        var visual = new GameObject("Visual").transform;
        visual.SetParent(studio.transform, false);
        visual.localRotation = Quaternion.Euler(0f, PortraitYaw, 0f);

        Renderer face = null;
        var hanAsset = AssetDatabase.LoadAssetAtPath<GameObject>(HanModel);
        if (hanAsset == null) Warn($"한 주임 모델 없음: {HanModel}");
        else
        {
            var han = (GameObject)PrefabUtility.InstantiatePrefab(hanAsset, visual);
            han.name = "CH_HanJuim";
            face = han.GetComponentInChildren<Renderer>();
            SetMats(face, new[] { "TK_Han_Field", "TK_Face_Han" }, "한 주임 몸");
        }
        var gearAsset = AssetDatabase.LoadAssetAtPath<GameObject>(HanGear);
        if (gearAsset == null) Warn($"현장 장비 모델 없음: {HanGear}");
        else
        {
            var gear = (GameObject)PrefabUtility.InstantiatePrefab(gearAsset, visual);
            gear.name = "CH_HanJuim_FieldGear";
            SetMats(gear.GetComponentInChildren<Renderer>(), GearMats, "현장 장비");
        }
        foreach (var r in studio.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // 카메라: 정면에서 가슴~머리
        var camGo = new GameObject("PortraitCamera");
        camGo.transform.SetParent(studio.transform, false);
        Vector3 fwd = Quaternion.Euler(0f, PortraitYaw, 0f) * Vector3.forward;
        camGo.transform.position = StudioPos + Vector3.up * PortraitAimY + fwd * PortraitDistance;
        camGo.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = PortraitFov;
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 6f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        ColorUtility.TryParseHtmlString("#1F3A3A", out var bg);
        cam.backgroundColor = bg;
        cam.depth = -10f;
        cam.targetTexture = EnsureRt();
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null) { data.renderPostProcessing = false; data.renderShadows = false; }
        cam.enabled = false;

        // 조명: 앞 위에서 하나
        var lightGo = new GameObject("PortraitLight");
        lightGo.transform.SetParent(studio.transform, false);
        lightGo.transform.position = StudioPos + new Vector3(0.6f, 1.8f, 0f) + fwd * 1.3f;
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point; light.range = 5f; light.intensity = 2.2f; light.color = new Color(1f, 0.97f, 0.92f);
        light.shadows = LightShadows.None;

        var portrait = studio.AddComponent<RadioPortrait>();
        var so = new SerializedObject(portrait);
        so.FindProperty("cam").objectReferenceValue = cam;
        so.FindProperty("visual").objectReferenceValue = visual;
        so.FindProperty("faceRenderer").objectReferenceValue = face;
        so.FindProperty("faceMaterialIndex").intValue = 1;
        so.ApplyModifiedPropertiesWithoutUndo();
        return portrait;
    }

    static void SetMats(Renderer r, string[] names, string label)
    {
        if (r == null) { Warn($"{label}: 렌더러 없음"); return; }
        var mats = names.Select(n => AssetDatabase.LoadAssetAtPath<Material>(MatDir + n + ".mat")).ToArray();
        for (int i = 0; i < names.Length; i++) if (mats[i] == null) Warn($"{label}: 재질 없음 {names[i]}");
        Mesh mesh = null;
        if (r is SkinnedMeshRenderer sk) mesh = sk.sharedMesh;
        else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
        int sub = mesh != null ? mesh.subMeshCount : 0;
        if (sub != names.Length) Warn($"{label}: 서브메시 {sub}개, 재질 {names.Length}개 — 색이 어긋나면 재질 표 순서를 확인");
        else Log($"{label}: 서브메시 {sub}개 = 재질 {names.Length}개");
        r.sharedMaterials = mats;
    }

    static RenderTexture EnsureRt()
    {
        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(RtPath);
        if (rt != null) return rt;
        EnsureFolder();
        rt = new RenderTexture(RtW, RtH, 24) { name = "RT_RadioPortrait", filterMode = FilterMode.Bilinear, antiAliasing = 1 };
        AssetDatabase.CreateAsset(rt, RtPath);
        return rt;
    }

    // ───────────────────────── 데이터 · UI 자리 ─────────────────────────
    static MapIntroScript EnsureData(MapEntry m)
    {
        var d = AssetDatabase.LoadAssetAtPath<MapIntroScript>(m.dataPath);
        if (d != null)
        {
            // 나중에 추가한 칸(게임 도중 무전)만, 비어 있을 때 채운다 — 손으로 고친 문구는 그대로
            if (FillEventLines(d, m)) { EditorUtility.SetDirty(d); AssetDatabase.SaveAssets(); Log($"{m.label} 데이터에 게임 도중 무전 문구 채움"); }
            return d;
        }
        EnsureFolder();
        d = ScriptableObject.CreateInstance<MapIntroScript>();   // 기본값 = 인형방 시안 내용
        m.setup?.Invoke(d);                                     // 맵별 처음 문구
        FillEventLines(d, m);
        AssetDatabase.CreateAsset(d, m.dataPath);
        AssetDatabase.SaveAssets();
        Log($"데이터 만듦: {m.dataPath}");
        return d;
    }

    static bool FillEventLines(MapIntroScript d, MapEntry m)
    {
        bool changed = false;
        if (string.IsNullOrEmpty(d.wrongGuessLine) && !string.IsNullOrEmpty(m.wrongGuessLine)) { d.wrongGuessLine = m.wrongGuessLine; changed = true; }
        if (string.IsNullOrEmpty(d.stainWarningLine) && !string.IsNullOrEmpty(m.stainWarningLine)) { d.stainWarningLine = m.stainWarningLine; changed = true; }
        return changed;
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Data")) AssetDatabase.CreateFolder("Assets", "Data");
        if (!AssetDatabase.IsValidFolder(DataFolder)) AssetDatabase.CreateFolder("Assets/Data", "Intro");
    }

    static void RemoveChildren(Transform parent, params string[] names)
    {
        foreach (Transform c in parent.Cast<Transform>().ToArray())
            if (names.Contains(c.name)) UnityEngine.Object.DestroyImmediate(c.gameObject);
    }

    // 일시정지·설정·페이드 화면보다 아래, HUD보다 위
    static int UiInsertIndex(Transform canvas)
    {
        int idx = canvas.childCount;
        foreach (var n in new[] { "PausePage", "SettingPanel", "ScreenFader", "whiteOut" })
        {
            var t = canvas.Find(n);
            if (t != null) idx = Mathf.Min(idx, t.GetSiblingIndex());
        }
        return idx;
    }

    static void Log(string m) => Debug.Log($"[인트로] {m}");
    static void Warn(string m) => Debug.LogWarning($"[인트로] {m}");
}
