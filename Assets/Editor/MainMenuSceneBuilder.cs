using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 메인화면을 "격리실 CCTV 화면"으로 만드는 에디터 도구 (2026-10-22). 설계: 프로젝트 문서 「코드 설계 - 메인화면」
/// 메뉴: Tools > 메인화면 > 격리실 CCTV 배치 / CLI: -executeMethod MainMenuSceneBuilder.ApplyBatch (Unity 에디터를 꺼야 함)
/// 하는 일 (순서대로)
///  1. 에셋: 주사선·▶·점 텍스처, RT_MenuCctv, VP_MenuCctv(채도·비네트·그레인), HUD 글자 그림자 재질 — 없을 때만
///  2. 튜토리얼 씬을 잠깐 열어 TutorialMap에서 격리실(벽·바닥·천장·조명·장치)을 복사하고, 외계인 본모습으로 MenuAlien 프리팹을 다시 만든다 (튜토리얼 씬은 저장 안 함)
///  3. 기념품 프리팹(리본·호박 꼭지)과 빈대 목록(MenuAlienRoster) — 없을 때만 → 손으로 바꾼 모델·목록이 유지된다
///  4. MainScene: MenuIsolationRoom을 매번 새로 만든다 (방·CCTV 카메라·볼륨·빈대 생성기) → 그 아래는 손으로 고치지 않기
///     환경광을 튜토리얼과 같게, Directional Light는 끄고(삭제 아님), Main Camera는 아무것도 그리지 않게
///  5. UI: Canvas에 CctvMenu·CreditsPanel이 없을 때만 만든다 → 손으로 다듬은 UI가 유지된다. 처음 모양으로 되돌리려면 지우고 다시 실행
///     있든 없든 참조(RT·HUD·버튼 연결·MainMenuManager.creditsPanel)는 다시 잇고, 기존 MainMenuPanel은 끈다(삭제 아님)
///     전시회용 초기화 버튼(CctvMenu/Btn_Reset)도 없을 때만 만든다 (2026-10-24, 「코드 설계 - 전시회 초기화 버튼」). 버튼만 넣으려면 Tools > 메인화면 > 초기화 버튼 넣기
/// </summary>
public static class MainMenuSceneBuilder
{
    // ===== 경로 =====
    const string MainScenePath = "Assets/Scenes/MainScene.unity";
    const string TutorialScenePath = "Assets/Scenes/Tutorial.unity";
    const string TutorialMapName = "TutorialMap";
    const string RoomRootName = "MenuIsolationRoom";
    const string AssetDir = "Assets/Scenes/MainScene";
    const string PrefabDir = "Assets/Prefabs/MainMenu";
    const string DataDir = "Assets/Data/MainMenu";
    const string RtPath = AssetDir + "/RT_MenuCctv.renderTexture";
    const string VolumePath = AssetDir + "/VP_MenuCctv.asset";
    const string ScanlinePath = AssetDir + "/T_Scanline.png";
    const string MarkerPath = AssetDir + "/T_Marker.png";
    const string DotPath = AssetDir + "/T_Dot.png";
    const string HudShadowMatPath = AssetDir + "/M_HudText_Shadow.mat";
    const string PinkMatPath = AssetDir + "/M_Souvenir_Pink.mat";
    const string PinkDarkMatPath = AssetDir + "/M_Souvenir_PinkDark.mat";
    const string OrangeMatPath = AssetDir + "/M_Souvenir_Orange.mat";
    const string StemMatPath = AssetDir + "/M_Souvenir_Stem.mat";
    const string AlienPrefabPath = PrefabDir + "/MenuAlien.prefab";
    const string RibbonPrefabPath = PrefabDir + "/Souvenir_Ribbon.prefab";
    const string PumpkinPrefabPath = PrefabDir + "/Souvenir_Pumpkin.prefab";
    const string RosterPath = DataDir + "/MenuAlienRoster.asset";
    const string TitleFontPath = "Assets/TextMesh Pro/Fonts/Cafe24PROSlimMax SDF.asset";
    const string BodyFontPath = "Assets/TextMesh Pro/Fonts/Maplestory OTF Light SDF.asset";

    // ===== 격리실 (Blender 평면 좌표, 「3d 모델링」 평면 배치) =====
    static readonly Vector4 IsoRect = new Vector4(22.5f, 12f, 28.5f, 18f);     // x0, z0, x1, z1 (벽 중심선)
    const float CopyMargin = 0.31f;       // 경계선 위 벽(두께 0.3)은 포함, 옆 방 벽(피벗이 0.75 이상 바깥)은 제외
    static readonly string[] CopyGroups = { "Structure", "IsolationGlass", "IsolationDevices", "Lights" };
    const float WallHalf = 0.15f, AreaInset = 0.5f;                            // 벽 면 + 몸이 박히지 않을 여백
    static readonly Vector2 CctvLookAt = new Vector2(25.5f, 15.0f);            // 튜토리얼 CCTV와 같음: 격리실 가운데
    const float CctvLookHeight = 0.3f;
    const float CctvFov = 60f;            // 튜토리얼 72°(4:3 모니터). 16:9 전체 화면이라 세로만 줄여 가로 약 92°
    const float CctvFps = 15f;
    const int RtWidth = 960, RtHeight = 540;
    const float KeepOutRadius = 1.6f;     // CCTV 바로 아래는 빈대가 너무 크게 잘려 보임
    static readonly Color AmbientColor = new Color(0.55f, 0.55f, 0.58f);   // TutorialSceneBuilder와 같음
    static readonly Color FeedTint = new Color(0.90f, 1f, 0.92f);           // 튜토리얼 모니터 M_CCTVScreen 계열 초록빛

    // ===== UI 색 (시안 기준, 기준 해상도 1920×1080) =====
    static readonly Color PanelBg = new Color(0.03f, 0.05f, 0.04f, 0.74f);
    static readonly Color TextMain = new Color(0.95f, 0.98f, 0.95f);
    static readonly Color TextSub = new Color(0.75f, 0.88f, 0.79f);
    static readonly Color Accent = new Color(0.71f, 1f, 0.80f);
    static readonly Color ButtonTint = new Color(0.55f, 1f, 0.70f, 0.16f);
    static readonly Color RecRed = new Color(1f, 0.23f, 0.23f);

    [MenuItem("Tools/메인화면/격리실 CCTV 배치")]
    public static void ApplyMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Apply();
    }

    /// <summary>CLI 전용</summary>
    public static void ApplyBatch()
    {
        try { Apply(); }
        catch (Exception e) { Debug.LogError($"[메인화면] 실패: {e}"); EditorApplication.Exit(1); }
    }

    [MenuItem("Tools/메인화면/클리어 기록 지우기")]
    public static void ClearRecords()
    {
        foreach (var s in RosterScenes()) StageClearRecord.Clear(s);
        Log($"클리어 기록 지움: {string.Join(", ", RosterScenes())}");
    }

    [MenuItem("Tools/메인화면/클리어 기록 모두 채우기 (확인용)")]
    public static void FillRecords()
    {
        foreach (var s in RosterScenes()) StageClearRecord.MarkCleared(s);
        Log($"클리어 기록 채움: {string.Join(", ", RosterScenes())}");
    }

    // 전체 배치(격리실 다시 만들기·튜토리얼 씬 열기)를 다시 돌리지 않고 초기화 버튼만 넣는다 (2026-10-24)
    [MenuItem("Tools/메인화면/초기화 버튼 넣기")]
    public static void AddResetButtonMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var main = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        var canvas = Root(main, "Canvas");
        var cctv = canvas != null ? canvas.transform.Find("CctvMenu") : null;
        if (cctv == null) { Warn("Canvas/CctvMenu가 없음 — 먼저 Tools > 메인화면 > 격리실 CCTV 배치를 실행하세요"); return; }

        var bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath);
        if (bodyFont == null) bodyFont = TMP_Settings.defaultFontAsset;
        EnsureResetButton(cctv, bodyFont, EnsureHudShadow(bodyFont));

        EditorSceneManager.MarkSceneDirty(main);
        EditorSceneManager.SaveScene(main);
        Log("MainScene 저장");
    }

    static IEnumerable<string> RosterScenes()
    {
        var roster = AssetDatabase.LoadAssetAtPath<MenuAlienRoster>(RosterPath);
        if (roster != null && roster.entries != null)
            return roster.entries.Where(e => e != null && !string.IsNullOrEmpty(e.sceneName)).Select(e => e.sceneName).ToArray();
        return new[] { "DollManiaHouse", "HalloweenNight" };
    }

    // ===================== 전체 =====================

    static void Apply()
    {
        EnsureFolder(AssetDir); EnsureFolder(PrefabDir); EnsureFolder(DataDir);
        var rt = EnsureRenderTexture();
        var volume = EnsureVolumeProfile();
        EnsureTexture(ScanlinePath, 1, 3, (x, y) => y == 0 ? new Color(0f, 0f, 0f, 0.16f) : Color.clear, sprite: false, repeat: true, point: true);
        EnsureTexture(MarkerPath, 32, 32, Triangle, sprite: true, repeat: false, point: false);
        EnsureTexture(DotPath, 32, 32, Dot, sprite: true, repeat: false, point: false);

        var main = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(main);
        var old = Root(main, RoomRootName);
        if (old != null) Object.DestroyImmediate(old);

        var root = new GameObject(RoomRootName);
        var roomGroup = Child(root.transform, "Room");
        var aliens = Child(root.transform, "Aliens");

        // --- 튜토리얼 격리실 복사 + 외계인 프리팹 ---
        Axes ax;
        var tut = EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Additive);
        try
        {
            var mapGo = Root(tut, TutorialMapName);
            if (mapGo == null) throw new Exception($"{TutorialScenePath}에 {TutorialMapName}이 없음 (Tools > 튜토리얼 씬 생성을 먼저)");
            var map = mapGo.transform;
            ax = DetectAxes(map);
            SceneManager.SetActiveScene(main);
            CopyIsolationRoom(map, roomGroup, ax);
            BuildAlienPrefab(map);
        }
        finally
        {
            EditorSceneManager.CloseScene(tut, true);   // 원본은 저장하지 않고 닫는다
            SceneManager.SetActiveScene(main);
        }

        var ribbon = EnsureRibbonPrefab();
        var pumpkin = EnsurePumpkinPrefab();
        var roster = EnsureRoster(ribbon, pumpkin);

        // --- 카메라·볼륨·생성기 ---
        var cam = BuildCctvCamera(root.transform, roomGroup, ax, rt);
        var volGo = new GameObject("CctvVolume");
        volGo.transform.SetParent(root.transform, false);
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 1f;
        vol.sharedProfile = volume;

        var spawner = root.AddComponent<MenuIsolationRoom>();
        float x0 = IsoRect.x + WallHalf + AreaInset, x1 = IsoRect.z - WallHalf - AreaInset;
        float z0 = IsoRect.y + WallHalf + AreaInset, z1 = IsoRect.w - WallHalf - AreaInset;
        Vector3 areaCenter = ax.U(new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f));
        Vector3 keepOut = cam.transform.position; keepOut.y = 0f;
        var sso = new SerializedObject(spawner);
        sso.FindProperty("roster").objectReferenceValue = roster;
        sso.FindProperty("alienParent").objectReferenceValue = aliens;
        sso.FindProperty("areaCenter").vector3Value = areaCenter;
        sso.FindProperty("areaSize").vector2Value = new Vector2(x1 - x0, z1 - z0);
        sso.FindProperty("keepOutCenter").vector3Value = keepOut;
        sso.FindProperty("keepOutRadius").floatValue = KeepOutRadius;
        sso.ApplyModifiedPropertiesWithoutUndo();

        // --- 씬 조명·기존 카메라 ---
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = AmbientColor;
        var sun = Root(main, "Directional Light");
        if (sun != null && sun.activeSelf) { sun.SetActive(false); Log("Directional Light 끔 (격리실은 형광등 조명만, 삭제 아님)"); }
        QuietMainCamera(main);

        // --- UI ---
        var hud = BuildOrLinkUi(main, rt);
        if (hud != null)
        {
            sso.Update();
            sso.FindProperty("hud").objectReferenceValue = hud;
            sso.ApplyModifiedPropertiesWithoutUndo();
        }

        Log($"빈대 다니는 곳: 가운데 {areaCenter}, 크기 {x1 - x0:0.##}×{z1 - z0:0.##}, CCTV 아래 금지 반지름 {KeepOutRadius}");
        LogRecords(roster);

        EditorSceneManager.MarkSceneDirty(main);
        if (!EditorSceneManager.SaveScene(main)) throw new Exception("MainScene 저장 실패");
        AssetDatabase.SaveAssets();
        Log("완료");
    }

    // ===================== 2. 격리실 복사 =====================

    static void CopyIsolationRoom(Transform map, Transform dst, Axes ax)
    {
        int total = 0;
        var counts = new List<string>();
        foreach (var gname in CopyGroups)
        {
            var g = map.Find(gname);
            if (g == null) { Warn($"TutorialMap에 {gname} 그룹이 없음"); continue; }
            var group = Child(dst, gname);
            int n = 0;
            foreach (Transform item in g)
            {
                Vector2 p = ax.Plan(item.position);
                if (p.x < IsoRect.x - CopyMargin || p.x > IsoRect.z + CopyMargin) continue;
                if (p.y < IsoRect.y - CopyMargin || p.y > IsoRect.w + CopyMargin) continue;
                // 프리팹 연결 없는 복사본: 씬 생성 도구가 붙인 형광등 PointLight·충돌체·재질까지 그대로 온다
                var copy = Object.Instantiate(item.gameObject);
                copy.name = item.name;
                copy.transform.SetParent(group, false);
                copy.transform.SetPositionAndRotation(item.position, item.rotation);
                copy.transform.localScale = item.lossyScale;
                n++;
            }
            counts.Add($"{gname} {n}");
            total += n;
        }
        if (total == 0) throw new Exception("격리실 오브젝트를 하나도 못 찾음 (좌표 축 확인 필요)");
        Log($"튜토리얼 격리실 복사 {total}개 ({string.Join(", ", counts)})");
    }

    // ===================== 외계인 프리팹 =====================

    static void BuildAlienPrefab(Transform map)
    {
        var src = FindDeep(map, "CH_Alien_Temp");
        if (src == null) throw new Exception("TutorialMap에 외계인 본모습 모델 CH_Alien_Temp가 없음");

        var root = new GameObject("MenuAlien");
        try
        {
            var visual = Child(root.transform, "Visual");
            var model = Object.Instantiate(src.gameObject);
            model.name = "Model";
            model.SetActive(true);
            model.transform.SetParent(visual, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;   // 모델 정면 = +Z (캐릭터 규칙)
            model.transform.localScale = src.lossyScale;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

            var rends = model.GetComponentsInChildren<Renderer>(true);
            Bounds b = rends.Length > 0 ? rends[0].bounds : new Bounds(new Vector3(0f, 0.31f, 0f), new Vector3(0.7f, 0.62f, 0.7f));
            foreach (var r in rends) b.Encapsulate(r.bounds);

            // 기념품 자리: 머리 꼭대기 가운데 (Visual 아래라 몸과 함께 출렁임)
            var anchor = Child(visual, "SouvenirAnchor");
            anchor.localPosition = new Vector3(b.center.x, b.max.y - 0.03f, b.center.z);

            root.AddComponent<MenuAlienWander>();
            var anim = root.AddComponent<PegDollAnimator>();
            var so = new SerializedObject(anim);
            so.FindProperty("visual").objectReferenceValue = visual;
            // 젤리답게: 페그돌(한 주임)보다 말랑하고 덜 기울게
            so.FindProperty("fullSpeed").floatValue = 0.7f;
            so.FindProperty("strideLength").floatValue = 0.3f;
            so.FindProperty("hopHeight").floatValue = 0.012f;
            so.FindProperty("leanDegrees").floatValue = 3f;
            so.FindProperty("swayDegrees").floatValue = 2f;
            so.FindProperty("squash").floatValue = 0.08f;
            so.FindProperty("breatheAmount").floatValue = 0.025f;
            so.FindProperty("breathePeriod").floatValue = 2.4f;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, AlienPrefabPath);
            Log($"빈대 프리팹 {AlienPrefabPath}: 키 {b.size.y:0.00}, 폭 {Mathf.Max(b.size.x, b.size.z):0.00}");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ===================== 3. 기념품·목록 (없을 때만) =====================

    // 인형 마니아 방: 분홍 리본. 고리 둘 + 매듭, 앞(+Z)을 보게 세움. 기본 도형 임시 모델 → 나중에 Blender 모델로 바꿔 끼워도 됨
    static GameObject EnsureRibbonPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(RibbonPrefabPath);
        if (existing != null) return existing;
        var pink = EnsureLit(PinkMatPath, new Color(1f, 0.49f, 0.71f));
        var dark = EnsureLit(PinkDarkMatPath, new Color(0.90f, 0.38f, 0.60f));
        var root = new GameObject("Souvenir_Ribbon");
        try
        {
            Part(root.transform, PrimitiveType.Sphere, "LoopL", new Vector3(-0.058f, 0.035f, 0f), new Vector3(0f, 0f, -22f), new Vector3(0.11f, 0.07f, 0.04f), pink);
            Part(root.transform, PrimitiveType.Sphere, "LoopR", new Vector3(0.058f, 0.035f, 0f), new Vector3(0f, 0f, 22f), new Vector3(0.11f, 0.07f, 0.04f), pink);
            Part(root.transform, PrimitiveType.Sphere, "Knot", new Vector3(0f, 0.035f, 0.004f), Vector3.zero, new Vector3(0.045f, 0.045f, 0.045f), dark);
            var p = PrefabUtility.SaveAsPrefabAsset(root, RibbonPrefabPath);
            Log($"기념품 프리팹 새로 만듦: {RibbonPrefabPath}");
            return p;
        }
        finally { Object.DestroyImmediate(root); }
    }

    // 할로윈 방: 호박 꼭지 모자. 납작한 주황 호박 + 기울어진 초록 꼭지
    static GameObject EnsurePumpkinPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PumpkinPrefabPath);
        if (existing != null) return existing;
        var orange = EnsureLit(OrangeMatPath, new Color(1f, 0.60f, 0.18f));
        var stem = EnsureLit(StemMatPath, new Color(0.25f, 0.48f, 0.17f));
        var root = new GameObject("Souvenir_Pumpkin");
        try
        {
            Part(root.transform, PrimitiveType.Sphere, "Pumpkin", new Vector3(0f, 0.04f, 0f), Vector3.zero, new Vector3(0.2f, 0.1f, 0.2f), orange);
            Part(root.transform, PrimitiveType.Cylinder, "Stem", new Vector3(0.008f, 0.105f, 0f), new Vector3(0f, 0f, -15f), new Vector3(0.035f, 0.035f, 0.035f), stem);
            var p = PrefabUtility.SaveAsPrefabAsset(root, PumpkinPrefabPath);
            Log($"기념품 프리팹 새로 만듦: {PumpkinPrefabPath}");
            return p;
        }
        finally { Object.DestroyImmediate(root); }
    }

    static void Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localEulerAngles = euler;
        go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    static Material EnsureLit(string path, Color c)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = Path.GetFileNameWithoutExtension(path) };
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.3f);   // 무광에 가깝게 (아트 방향)
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static MenuAlienRoster EnsureRoster(GameObject ribbon, GameObject pumpkin)
    {
        var alien = AssetDatabase.LoadAssetAtPath<GameObject>(AlienPrefabPath);
        var roster = AssetDatabase.LoadAssetAtPath<MenuAlienRoster>(RosterPath);
        if (roster == null)
        {
            roster = ScriptableObject.CreateInstance<MenuAlienRoster>();
            roster.entries = new[]
            {
                new MenuAlienRoster.Entry { sceneName = "DollManiaHouse", label = "인형 마니아 방", souvenirPrefab = ribbon },
                new MenuAlienRoster.Entry { sceneName = "HalloweenNight", label = "할로윈 방", souvenirPrefab = pumpkin },
            };
            roster.alienPrefab = alien;
            AssetDatabase.CreateAsset(roster, RosterPath);
            Log($"빈대 목록 새로 만듦: {RosterPath}");
        }
        else if (roster.alienPrefab == null)
        {
            roster.alienPrefab = alien;
            EditorUtility.SetDirty(roster);
        }
        return roster;
    }

    static void LogRecords(MenuAlienRoster roster)
    {
        if (roster == null || roster.entries == null) return;
        var parts = roster.entries.Where(e => e != null).Select(e => $"{e.label}({e.sceneName}) {(StageClearRecord.IsCleared(e.sceneName) ? "클리어" : "-")}");
        Log($"빈대 목록: 처음 1마리 + {string.Join(" / ", parts)}");
    }

    // ===================== 4. 카메라 =====================

    static Camera BuildCctvCamera(Transform root, Transform room, Axes ax, RenderTexture rt)
    {
        var go = new GameObject("CctvCamera");
        go.transform.SetParent(root, false);
        Vector3 target = ax.U(CctvLookAt, CctvLookHeight);
        var model = FindDeep(room, "TK_Iso_CCTV");
        Vector3 basePos;
        if (model != null) basePos = model.position;
        else
        {
            Warn("격리실 복사본에 TK_Iso_CCTV가 없어 남서쪽 위 모서리에 카메라를 둠");
            basePos = ax.U(new Vector2(IsoRect.x + 0.3f, IsoRect.y + 0.3f), 3.6f);
        }
        // 튜토리얼 CCTV와 같은 계산: 모델 자리에서 방 안쪽 0.25m·아래 0.1m, 격리실 가운데를 본다
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
        go.tag = "Untagged";   // MainCamera 태그 금지 (CctvFeed 주석 참고)
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.None;

        var feed = go.AddComponent<CctvFeed>();
        var so = new SerializedObject(feed);
        so.FindProperty("fps").floatValue = CctvFps;
        so.ApplyModifiedPropertiesWithoutUndo();
        Log($"CCTV 카메라 {go.transform.position} → 격리실 가운데, 세로 시야각 {CctvFov}°, 초당 {CctvFps}번");
        return cam;
    }

    // 메인화면은 UI(Screen Space Overlay)와 CCTV 화면만 보이므로 Main Camera는 검은 바탕만 그린다 (AudioListener 때문에 남김)
    static void QuietMainCamera(Scene main)
    {
        foreach (var g in main.GetRootGameObjects())
            foreach (var c in g.GetComponentsInChildren<Camera>(true))
            {
                if (!c.CompareTag("MainCamera")) continue;
                c.cullingMask = 0;
                c.clearFlags = CameraClearFlags.SolidColor;
                c.backgroundColor = Color.black;
                var d = c.GetUniversalAdditionalCameraData();
                if (d != null) d.renderPostProcessing = false;
                Log($"{c.name}: 아무것도 그리지 않게 (검은 바탕)");
            }
    }

    // ===================== 5. UI =====================

    static MenuCctvHud BuildOrLinkUi(Scene main, RenderTexture rt)
    {
        var canvasGo = Root(main, "Canvas");
        if (canvasGo == null) { Warn("MainScene에 Canvas가 없어 UI를 건너뜀"); return null; }
        var canvas = canvasGo.transform;
        var mgr = main.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MainMenuManager>(true)).FirstOrDefault();
        if (mgr == null) Warn("MainMenuManager가 없어 버튼을 연결하지 못함");

        var titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);
        var bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath);
        if (bodyFont == null) bodyFont = TMP_Settings.defaultFontAsset;
        if (titleFont == null) titleFont = bodyFont;
        var shadow = EnsureHudShadow(bodyFont);

        var cctv = canvas.Find("CctvMenu");
        if (cctv == null) { cctv = BuildCctvMenu(canvas, titleFont, bodyFont, shadow).transform; Log("CctvMenu 새로 만듦"); }
        else Log("CctvMenu가 이미 있어 모양은 그대로 두고 연결만 다시 함");
        cctv.SetSiblingIndex(0);   // 맨 뒤: 스테이지 선택·설정·크레딧 팝업이 위에 뜬다

        var feed = cctv.Find("Feed")?.GetComponent<RawImage>();
        if (feed != null) feed.texture = rt; else Warn("CctvMenu/Feed가 없음");
        EnsureResetButton(cctv, bodyFont, shadow);

        var credits = canvas.Find("CreditsPanel");
        if (credits == null) { credits = BuildCreditsPanel(canvas, titleFont, bodyFont).transform; Log("CreditsPanel 새로 만듦"); }
        credits.SetAsLastSibling();
        credits.gameObject.SetActive(false);

        if (mgr != null)
        {
            Wire(cctv, "Btn_Sortie", mgr.ToggleStageSelectPanel);
            Wire(cctv, "Btn_Settings", mgr.ToggleSettingsPanel);
            Wire(cctv, "Btn_Credits", mgr.ToggleCreditsPanel);
            Wire(cctv, "Btn_Quit", mgr.QuitGame);
            Wire(credits, "Btn_Close", mgr.CloseCreditsPanel);
            var mso = new SerializedObject(mgr);
            mso.FindProperty("creditsPanel").objectReferenceValue = credits.gameObject;
            mso.ApplyModifiedPropertiesWithoutUndo();
        }

        var oldMenu = canvas.Find("MainMenuPanel");
        if (oldMenu != null && oldMenu.gameObject.activeSelf) { oldMenu.gameObject.SetActive(false); Log("기존 MainMenuPanel 끔 (삭제 아님)"); }

        return cctv.GetComponent<MenuCctvHud>();
    }

    // 전시회용 초기화 버튼 (2026-10-24). 없을 때만 만든다 → 손으로 옮기거나 다듬어도 유지. 참조는 있든 없든 다시 잇는다
    // 자리: 오른쪽 아래 채널 표시(아래 33~69) 바로 위, 메뉴 패널과 반대쪽 → 관람객이 게임 메뉴로 착각하지 않게
    const string ResetButtonName = "Btn_Reset";
    static void EnsureResetButton(Transform cctv, TMP_FontAsset font, Material shadow)
    {
        var t = FindDeep(cctv, ResetButtonName);
        if (t == null)
        {
            var rt = UiNode(ResetButtonName, cctv);
            Anchor(rt, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-48f, 84f), new Vector2(300f, 54f));
            var img = rt.gameObject.AddComponent<Image>(); img.color = new Color(0.03f, 0.05f, 0.04f, 0.55f);
            var o = rt.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.22f); o.effectDistance = new Vector2(1.5f, -1.5f);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;   // ColorTint는 Image 색에 곱해짐: 평소 살짝 어둡게, 마우스를 올리면 원래 밝기
            cb.normalColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cb.highlightedColor = Color.white;
            cb.selectedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cb.pressedColor = new Color(0.65f, 0.65f, 0.65f, 1f);
            cb.fadeDuration = 0.06f;
            btn.colors = cb;
            var label = Text(rt, "Label", "게임 초기화", font, 22f, TextSub, TextAlignmentOptions.Center, shadow, bold: true);
            Stretch(label.rectTransform);
            rt.gameObject.AddComponent<MenuResetButton>();
            rt.SetAsLastSibling();
            t = rt;
            Log("Btn_Reset 새로 만듦 (오른쪽 아래)");
        }
        else Log("Btn_Reset이 이미 있어 모양은 그대로 두고 연결만 다시 함");

        var comp = t.GetComponent<MenuResetButton>();
        if (comp == null) comp = t.gameObject.AddComponent<MenuResetButton>();
        var so = new SerializedObject(comp);
        so.FindProperty("button").objectReferenceValue = t.GetComponent<Button>();
        so.FindProperty("label").objectReferenceValue = t.GetComponentInChildren<TMP_Text>(true);
        so.FindProperty("background").objectReferenceValue = t.GetComponent<Image>();
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Wire(Transform root, string buttonName, UnityAction action)
    {
        var t = FindDeep(root, buttonName);
        var btn = t != null ? t.GetComponent<Button>() : null;
        if (btn == null) { Warn($"{root.name}에 버튼 {buttonName}이 없어 연결하지 못함"); return; }
        while (btn.onClick.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(btn.onClick, 0);
        UnityEventTools.AddPersistentListener(btn.onClick, action);
    }

    static GameObject BuildCctvMenu(Transform canvas, TMP_FontAsset titleFont, TMP_FontAsset bodyFont, Material shadow)
    {
        var root = UiNode("CctvMenu", canvas); Stretch(root);
        var hud = root.gameObject.AddComponent<MenuCctvHud>();

        // 화면 층: CCTV 화면 → 주사선 → 깜빡임
        var feed = UiNode("Feed", root); Stretch(feed);
        var feedImg = feed.gameObject.AddComponent<RawImage>(); feedImg.color = FeedTint; feedImg.raycastTarget = false;
        var scan = UiNode("Scanlines", root); Stretch(scan);
        var scanImg = scan.gameObject.AddComponent<RawImage>();
        scanImg.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ScanlinePath); scanImg.raycastTarget = false;
        var flick = UiNode("Flicker", root); Stretch(flick);
        var flickImg = flick.gameObject.AddComponent<Image>(); flickImg.color = new Color(1f, 1f, 1f, 0f); flickImg.raycastTarget = false;

        // 녹화기 표시 (위: REC·카메라 이름 / 시각, 아래: 수용 개체 / 녹화기 채널)
        var top = UiNode("RecLabel", root);
        Anchor(top, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -36f), new Vector2(800f, 40f));
        var hl = top.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 18f; hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
        var dot = UiNode("RecDot", top);
        var dotImg = dot.gameObject.AddComponent<Image>();
        dotImg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(DotPath); dotImg.color = RecRed; dotImg.raycastTarget = false;
        var dle = dot.gameObject.AddComponent<LayoutElement>(); dle.preferredWidth = 21f; dle.preferredHeight = 21f;
        Text(top, "Rec", "REC", bodyFont, 27f, Color.white, TextAlignmentOptions.Left, shadow, bold: true);
        Text(top, "Cam", "CAM-01 · 격리실 A-1", bodyFont, 27f, new Color(0.89f, 0.93f, 0.89f), TextAlignmentOptions.Left, shadow);

        var clock = Text(root, "Clock", "2026-10-22  00:00:00", bodyFont, 27f, Color.white, TextAlignmentOptions.Right, shadow, bold: true);
        Anchor(clock.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-48f, -36f), new Vector2(600f, 40f));
        var count = Text(root, "Count", "수용 개체 01 · 상태 이상 없음", bodyFont, 24f, Color.white, TextAlignmentOptions.Left, shadow, bold: true);
        Anchor(count.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(48f, 33f), new Vector2(900f, 36f));
        var dvr = Text(root, "Channel", "DVR-02 · CH 01/04", bodyFont, 24f, Color.white, TextAlignmentOptions.Right, shadow, bold: true);
        Anchor(dvr.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-48f, 33f), new Vector2(600f, 36f));

        // 메뉴 패널: 제목 + 버튼 4개 (빈대가 다니는 바닥을 가리지 않게 왼쪽 위)
        var panel = UiNode("MenuPanel", root);
        Anchor(panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -120f), new Vector2(570f, 0f));
        var panelImg = panel.gameObject.AddComponent<Image>(); panelImg.color = PanelBg;
        var outline = panel.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.28f); outline.effectDistance = new Vector2(1.5f, -1.5f);
        var vl = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(36, 36, 36, 30); vl.spacing = 9f;
        vl.childControlWidth = true; vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        var fit = panel.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var title = Text(panel, "Title", "외계인 숨바꼭질", titleFont, 81f, TextMain, TextAlignmentOptions.Left, null);
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 100f;
        UiNode("Gap", panel).gameObject.AddComponent<LayoutElement>().preferredHeight = 12f;

        MenuButton(panel, "Btn_Sortie", "출동", "게임 시작", bodyFont, primary: true);
        MenuButton(panel, "Btn_Settings", "설정", null, bodyFont, primary: false);
        MenuButton(panel, "Btn_Credits", "크레딧", null, bodyFont, primary: false);
        MenuButton(panel, "Btn_Quit", "종료", null, bodyFont, primary: false);

        var hso = new SerializedObject(hud);
        hso.FindProperty("clockText").objectReferenceValue = clock;
        hso.FindProperty("countText").objectReferenceValue = count;
        hso.FindProperty("recDot").objectReferenceValue = dotImg;
        hso.FindProperty("scanlines").objectReferenceValue = scanImg;
        hso.FindProperty("flicker").objectReferenceValue = flickImg;
        hso.ApplyModifiedPropertiesWithoutUndo();
        return root.gameObject;
    }

    static void MenuButton(Transform parent, string name, string label, string sub, TMP_FontAsset font, bool primary)
    {
        var t = UiNode(name, parent);
        t.gameObject.AddComponent<LayoutElement>().preferredHeight = 75f;
        var img = t.gameObject.AddComponent<Image>(); img.color = ButtonTint;
        var btn = t.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        // 배경 칠은 강조(출동)와 마우스·선택일 때만 보인다. ColorTint는 Image 색에 곱해지므로 알파로 껐다 켠다
        cb.normalColor = primary ? Color.white : new Color(1f, 1f, 1f, 0f);
        cb.highlightedColor = Color.white;
        cb.selectedColor = Color.white;
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        cb.fadeDuration = 0.06f;
        btn.colors = cb;
        if (primary)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.6f); o.effectDistance = new Vector2(1.5f, -1.5f);
        }

        var mark = UiNode("Marker", t);
        Anchor(mark, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(21f, 0f), new Vector2(21f, 24f), new Vector2(0f, 0.5f));
        var mi = mark.gameObject.AddComponent<Image>();
        mi.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(MarkerPath); mi.color = Accent; mi.raycastTarget = false;

        var txt = Text(t, "Label", label, font, 32f, TextMain, TextAlignmentOptions.Left, null, bold: true);
        Anchor(txt.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(57f, 0f), new Vector2(-57f, 0f), new Vector2(0f, 0.5f));
        if (!string.IsNullOrEmpty(sub))
        {
            var s = Text(t, "Sub", sub, font, 20f, TextSub, TextAlignmentOptions.Right, null);
            Anchor(s.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(-21f, 0f), new Vector2(-42f, 0f), new Vector2(1f, 0.5f));
        }

        var mk = t.gameObject.AddComponent<MenuButtonMarker>();
        var so = new SerializedObject(mk);
        so.FindProperty("marker").objectReferenceValue = mark.gameObject;
        so.FindProperty("alwaysOn").boolValue = primary;
        so.ApplyModifiedPropertiesWithoutUndo();
        mark.gameObject.SetActive(primary);
    }

    // 크레딧 (조작 방법 대신, 2026-10-22). 내용은 자리표시 — 에셋 출처 확인(DollCharacter 등) 뒤 채운다
    static GameObject BuildCreditsPanel(Transform canvas, TMP_FontAsset titleFont, TMP_FontAsset bodyFont)
    {
        var root = UiNode("CreditsPanel", canvas); Stretch(root);
        var dim = root.gameObject.AddComponent<Image>(); dim.color = new Color(0f, 0f, 0f, 0.6f);   // 뒤 버튼 클릭 막기

        var box = UiNode("Box", root);
        Anchor(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960f, 760f), new Vector2(0.5f, 0.5f));
        box.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.05f, 0.04f, 0.95f);
        var o = box.gameObject.AddComponent<Outline>(); o.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f); o.effectDistance = new Vector2(1.5f, -1.5f);

        var head = Text(box, "Title", "크레딧", titleFont, 60f, TextMain, TextAlignmentOptions.Left, null);
        Anchor(head.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(60f, -48f), new Vector2(-180f, 80f), new Vector2(0f, 1f));

        const string body =
            "제작\n" +
            "    [팀원 이름]  ·  [팀원 이름]\n\n" +
            "사용 에셋\n" +
            "    Poly Halloween — polyperfect (Unity Asset Store)\n" +
            "    [그 밖의 에셋은 출처 확인 뒤 추가]\n\n" +
            "글꼴\n" +
            "    Cafe24 PRO Slim Max — 카페24\n" +
            "    메이플스토리 서체 — 넥슨";
        var bt = Text(box, "Body", body, bodyFont, 28f, TextSub, TextAlignmentOptions.TopLeft, null);
        bt.textWrappingMode = TextWrappingModes.Normal;
        bt.lineSpacing = 12f;
        Anchor(bt.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(60f, 48f), new Vector2(-120f, -200f), new Vector2(0f, 0f));

        var close = UiNode("Btn_Close", box);
        Anchor(close, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36f, -36f), new Vector2(120f, 60f), new Vector2(1f, 1f));
        var ci = close.gameObject.AddComponent<Image>(); ci.color = ButtonTint;
        var cbtn = close.gameObject.AddComponent<Button>(); cbtn.targetGraphic = ci;
        var cl = Text(close, "Label", "닫기", bodyFont, 28f, TextMain, TextAlignmentOptions.Center, null, bold: true);
        Stretch(cl.rectTransform);
        return root.gameObject;
    }

    // ===================== UI 도우미 =====================

    static RectTransform UiNode(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;   // UI
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pos, Vector2 size, Vector2? pivot = null)
    {
        rt.anchorMin = min; rt.anchorMax = max;
        rt.pivot = pivot ?? min;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static TextMeshProUGUI Text(Transform parent, string name, string text, TMP_FontAsset font, float size, Color color,
                                TextAlignmentOptions align, Material mat, bool bold = false)
    {
        var t = UiNode(name, parent);
        var tmp = t.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        if (mat != null) tmp.fontSharedMaterial = mat;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.raycastTarget = false;
        return tmp;
    }

    // HUD 글자는 밝은 벽·천장 위에 얹히므로 그림자(밑깔림)를 준다. 글꼴 기본 재질을 복사해 밑깔림만 켬
    static Material EnsureHudShadow(TMP_FontAsset font)
    {
        if (font == null || font.material == null) return null;
        var m = AssetDatabase.LoadAssetAtPath<Material>(HudShadowMatPath);
        if (m != null) return m;
        m = new Material(font.material) { name = "M_HudText_Shadow" };
        m.EnableKeyword("UNDERLAY_ON");
        m.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.85f));
        m.SetFloat("_UnderlayOffsetX", 0.5f);
        m.SetFloat("_UnderlayOffsetY", -0.5f);
        m.SetFloat("_UnderlaySoftness", 0.35f);
        AssetDatabase.CreateAsset(m, HudShadowMatPath);
        return m;
    }

    // ===================== 1. 에셋 =====================

    static RenderTexture EnsureRenderTexture()
    {
        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(RtPath);
        if (rt != null) return rt;
        rt = new RenderTexture(RtWidth, RtHeight, 24) { name = "RT_MenuCctv", filterMode = FilterMode.Bilinear, antiAliasing = 1 };
        AssetDatabase.CreateAsset(rt, RtPath);
        return rt;
    }

    // CCTV 화면 안 효과 (RT에 함께 구워짐): 채도↓·대비↑·비네트·필름 그레인
    static VolumeProfile EnsureVolumeProfile()
    {
        var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
        if (p == null)
        {
            p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, VolumePath);
        }
        if (!p.TryGet(out ColorAdjustments ca))
        {
            ca = p.Add<ColorAdjustments>(true);
            AssetDatabase.AddObjectToAsset(ca, p);
            ca.saturation.Override(-35f);
            ca.contrast.Override(8f);
        }
        if (!p.TryGet(out Vignette vg))
        {
            vg = p.Add<Vignette>(true);
            AssetDatabase.AddObjectToAsset(vg, p);
            vg.intensity.Override(0.3f);
            vg.smoothness.Override(0.45f);
        }
        if (!p.TryGet(out FilmGrain fg))
        {
            fg = p.Add<FilmGrain>(true);
            AssetDatabase.AddObjectToAsset(fg, p);
            fg.type.Override(FilmGrainLookup.Medium1);
            fg.intensity.Override(0.35f);
            fg.response.Override(0.6f);
        }
        EditorUtility.SetDirty(p);
        return p;
    }

    static void EnsureTexture(string path, int w, int h, Func<int, int, Color> pixel, bool sprite, bool repeat, bool point)
    {
        bool created = false;
        if (!File.Exists(path))
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, y, pixel(x, y));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            created = true;
        }
        if (!created) return;
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
        if (sprite) ti.spriteImportMode = SpriteImportMode.Single;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        ti.filterMode = point ? FilterMode.Point : FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
    }

    // ▶ (4배 표본으로 가장자리 부드럽게)
    static Color Triangle(int x, int y)
    {
        int hit = 0;
        for (int i = 0; i < 4; i++)
        {
            float px = x + 0.25f + 0.5f * (i % 2), py = y + 0.25f + 0.5f * (i / 2);
            // 꼭짓점 (3,2) (29,16) (3,30)
            bool inside = px >= 3f && (py - 2f) >= (px - 3f) * 14f / 26f && (30f - py) >= (px - 3f) * 14f / 26f;
            if (inside) hit++;
        }
        return new Color(1f, 1f, 1f, hit / 4f);
    }

    static Color Dot(int x, int y)
    {
        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16f, 16f));
        return new Color(1f, 1f, 1f, Mathf.Clamp01(15f - d));
    }

    // ===================== 공용 =====================

    struct Axes
    {
        public float sx, sz;
        public Vector3 U(Vector2 b, float y = 0f) => new Vector3(sx * b.x, y, sz * b.y);
        public Vector2 Plan(Vector3 u) => new Vector2(u.x / sx, u.z / sz);
    }

    // 방 A 상자(Blender 24.75, 20.25)로 Blender → Unity 축 부호 확인 (TutorialGameplayBuilder와 같은 방법)
    static Axes DetectAxes(Transform map)
    {
        var box = FindDeep(map, "TK_Prop_Box");
        if (box == null) throw new Exception("축 확인용 TK_Prop_Box가 TutorialMap에 없음");
        return new Axes { sx = Mathf.Sign(box.position.x / 24.75f), sz = Mathf.Sign(box.position.z / 20.25f) };
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static GameObject Root(Scene scene, string name) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);

    static Transform Child(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t;
        t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

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

    static void Log(string m) => Debug.Log($"[메인화면] {m}");
    static void Warn(string m) => Debug.LogWarning($"[메인화면] {m}");
}
