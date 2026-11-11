using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 흰 훈련장(튜토리얼 맵)을 Blender에서 내보낸 FBX + 배치 데이터로 씬에 조립하는 에디터 도구.
/// 메뉴: Tools > 튜토리얼 씬 생성 / CLI: -executeMethod TutorialSceneBuilder.BuildBatch
/// - 배치 데이터: Assets/3DModel&Asset/TrainingRoom/tutorial_layout.json (Blender의 build_training_layout.py 결과)
/// - 씬이 이미 있으면 "TutorialMap" 오브젝트만 지우고 다시 만든다. 그 밖에 씬에 추가한 오브젝트(플레이어, 게임 로직 등)는 유지된다.
///   → TutorialMap 아래를 손으로 고치면 재생성 때 사라지므로, 맵 수정은 Blender 쪽에서 하고 다시 내보낸다.
/// - 좌표: Blender (x, y, z) → Unity (sx*x, z, sz*y), 회전 Y = -(sx*sz)*rz. sx, sz는 문짝·모니터 모델로 자동 확인.
/// </summary>
public static class TutorialSceneBuilder
{
    const string Root = "Assets/3DModel&Asset/TrainingRoom";
    const string ModelDir = Root + "/Models";
    const string TexDir = Root + "/Textures";
    const string MatDir = Root + "/Materials";
    const string LayoutPath = Root + "/tutorial_layout.json";
    const string ScenePath = "Assets/Scenes/Tutorial.unity";
    const string MapRootName = "TutorialMap";

    [System.Serializable] class MatInfo { public string name; public float[] color; public float metallic; public float roughness; public float[] emission; public float emissionStrength; public string texture; public float alpha; }
    [System.Serializable] class Inst { public string name; public string mesh; public string kind; public float[] pos; public float rz; public string[] mats; }
    [System.Serializable] class Layout { public MatInfo[] materials; public Inst[] instances; public float[] playerStart; public float playerStartRz; }

    [MenuItem("Tools/튜토리얼 씬 생성")]
    public static void BuildMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }

    /// <summary>CLI 전용</summary>
    public static void BuildBatch()
    {
        try { Build(); }
        catch (System.Exception e) { Debug.LogError($"[튜토리얼씬] 실패: {e}"); EditorApplication.Exit(1); }
    }

    static void Build()
    {
        AssetDatabase.Refresh();
        ConfigureImporters();
        var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
        var mats = BuildMaterials(layout.materials);
        DetectAxes(out float sx, out float sz);

        bool existed = File.Exists(ScenePath);
        var scene = existed
            ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == MapRootName) Object.DestroyImmediate(go);

        var root = new GameObject(MapRootName);
        var groups = new Dictionary<string, Transform>();
        int count = 0, warn = 0;
        foreach (var it in layout.instances)
        {
            if (it.kind == "doorset")
            {
                warn += BuildDoorSet(it, Group(root.transform, groups, "doorset"), mats, sx, sz, scene);
                count++;
                continue;
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{it.mesh}.fbx");
            if (model == null) { Warn($"모델 없음: {it.mesh}"); warn++; continue; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            go.name = it.name;
            go.transform.SetParent(Group(root.transform, groups, it.kind), false);
            go.transform.localPosition = ToUnity(it.pos, sx, sz);
            go.transform.localRotation = Quaternion.Euler(0f, -(sx * sz) * it.rz, 0f);
            warn += AssignMaterials(go, it, mats);
            SetupKind(go, it.kind, it.mesh);
            count++;
        }

        if (!existed)
        {
            // 플레이어 시작 위치 표시 + 임시 미리보기 카메라 (플레이어 리그를 넣으면 카메라는 지운다)
            var start = new GameObject("PlayerStart");
            start.transform.position = ToUnity(layout.playerStart, sx, sz);
            start.transform.rotation = Quaternion.Euler(0f, -(sx * sz) * layout.playerStartRz, 0f);
            var camGo = new GameObject("TempPreviewCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(start.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.3485f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.58f);
        }

        // 맵을 다시 만들면 TutorialMap을 가리키던 게임 요소(빈대 원본 끄기, 문 연결)가 끊기므로 게임 요소 배치를 이어서 다시 한다 (2026-10-18)
        if (FindRoot(scene, "PlayerStart") != null) TutorialGameplayBuilder.ApplyToScene(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Log($"완료: {ScenePath} — 배치 {count}개, 경고 {warn}개 (축 sx={sx}, sz={sz})");
    }

    // ---------- 모델 임포트 설정 ----------
    static void ConfigureImporters()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) continue;
            bool dirty = false;
            if (imp.materialImportMode != ModelImporterMaterialImportMode.None) { imp.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
            if (imp.importCameras) { imp.importCameras = false; dirty = true; }
            if (imp.importLights) { imp.importLights = false; dirty = true; }
            if (imp.importAnimation) { imp.importAnimation = false; dirty = true; }
            if (imp.animationType != ModelImporterAnimationType.None) { imp.animationType = ModelImporterAnimationType.None; dirty = true; }
            if (!imp.generateSecondaryUV) { imp.generateSecondaryUV = true; dirty = true; }
            if (dirty) imp.SaveAndReimport();
        }
    }

    // ---------- 재질 (Blender 값 → URP Lit) ----------
    static Dictionary<string, Material> BuildMaterials(MatInfo[] infos)
    {
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Root, "Materials");
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var dict = new Dictionary<string, Material>();
        foreach (var mi in infos)
        {
            string path = $"{MatDir}/{mi.name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != shader) m.shader = shader;

            Color c = new Color(mi.color[0], mi.color[1], mi.color[2], 1f).gamma;   // Blender 값은 선형
            Texture2D tex = string.IsNullOrEmpty(mi.texture) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{mi.texture}");
            if (tex != null) c = Color.white;
            c.a = mi.alpha;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", mi.metallic);
            m.SetFloat("_Smoothness", 1f - mi.roughness);
            if (mi.emissionStrength > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", new Color(mi.emission[0], mi.emission[1], mi.emission[2]) * (mi.emissionStrength * 0.5f));
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }
            if (mi.alpha < 0.999f) MakeTransparent(m); else MakeOpaque(m);
            EditorUtility.SetDirty(m);
            dict[mi.name] = m;
        }
        AssetDatabase.SaveAssets();
        return dict;
    }

    static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetShaderPassEnabled("DepthOnly", false);
        m.SetShaderPassEnabled("ShadowCaster", false);
    }

    static void MakeOpaque(Material m)
    {
        m.SetFloat("_Surface", 0f);
        m.SetOverrideTag("RenderType", "Opaque");
        m.SetFloat("_SrcBlend", (float)BlendMode.One); m.SetFloat("_DstBlend", (float)BlendMode.Zero);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
        m.SetFloat("_ZWrite", 1f);
        m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = -1;
        m.SetShaderPassEnabled("DepthOnly", true);
        m.SetShaderPassEnabled("ShadowCaster", true);
    }

    // ---------- 축 확인 ----------
    // 문짝: Blender에서 경첩(원점)부터 +X로 1.33 뻗음, 중심 높이 1.5
    // 모니터: Blender에서 벽(원점)부터 -Y로 0.13 나옴
    static void DetectAxes(out float sx, out float sz)
    {
        sx = -1f; sz = -1f;
        Vector3 door = ModelCenter("TK_Door_Leaf", out Vector3 doorSize);
        Vector3 mon = ModelCenter("TK_Iso_Monitor", out _);
        if (Mathf.Abs(door.x) > 0.3f) sx = Mathf.Sign(door.x);
        if (Mathf.Abs(mon.z) > 0.01f) sz = -Mathf.Sign(mon.z);
        if (Mathf.Abs(door.y - 1.5f) > 0.05f || Mathf.Abs(doorSize.y - 2.98f) > 0.05f)
            Warn($"문짝 크기·높이가 예상과 다름 (중심 {door}, 크기 {doorSize}) — FBX 축/단위 설정 확인 필요");
    }

    static Vector3 ModelCenter(string mesh, out Vector3 size)
    {
        size = Vector3.zero;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{mesh}.fbx");
        if (model == null) return Vector3.zero;
        var mf = model.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return Vector3.zero;
        var b = mf.sharedMesh.bounds;
        var t = mf.transform;
        Vector3 a = model.transform.InverseTransformPoint(t.TransformPoint(b.min));
        Vector3 c = model.transform.InverseTransformPoint(t.TransformPoint(b.max));
        size = new Vector3(Mathf.Abs(c.x - a.x), Mathf.Abs(c.y - a.y), Mathf.Abs(c.z - a.z));
        return (a + c) * 0.5f;
    }

    static Vector3 ToUnity(float[] p, float sx, float sz) => new Vector3(sx * p[0], p[2], sz * p[1]);

    static Transform Group(Transform root, Dictionary<string, Transform> groups, string kind)
    {
        if (groups.TryGetValue(kind, out var t)) return t;
        string label = kind switch
        {
            "static" => "Structure", "door" => "Doors", "glass" => "IsolationGlass", "prop" => "Props",
            "device" => "IsolationDevices", "light" => "Lights", "npc" => "NPC", "alien" => "Alien_Temp", "doorset" => "Doors", _ => "Decor"
        };
        var go = new GameObject(label);
        go.transform.SetParent(root, false);
        groups[kind] = go.transform;
        return go.transform;
    }

    // ---------- 캡슐형 좌우 분할 슬라이드 문 (2026-10-12) ----------
    // 문 루트(BoxCollider + DoorController) 아래에 문짝 2개 + 테두리 빛. 문벽·틀은 Structure에 따로 배치됨
    // 테두리 빛: 잠김 빨강 → DoorController.Open() 순간 초록. 진행 문은 한 주임이, 얼룩 문은 승리 조건이 Open()을 부른다
    static int BuildDoorSet(Inst it, Transform parent, Dictionary<string, Material> mats, float sx, float sz, UnityEngine.SceneManagement.Scene scene)
    {
        int warn = 0;
        var rootGo = new GameObject(it.name);
        rootGo.transform.SetParent(parent, false);
        rootGo.transform.localPosition = ToUnity(it.pos, sx, sz);
        rootGo.transform.localRotation = Quaternion.Euler(0f, -(sx * sz) * it.rz, 0f);
        var box = rootGo.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 1.5f, 0f);
        box.size = new Vector3(1.35f, 3.0f, 0.3f);

        var panels = new List<Transform>();
        var lights = new List<Renderer>();
        var parts = new (string mesh, string mat, bool light)[]
        {
            ("TK_DoorCapsule_PanelA", "TK_Wall", false),
            ("TK_DoorCapsule_PanelB", "TK_Wall", false),
            ("TK_DoorCapsule_Light", "TK_DoorLight", true),
        };
        foreach (var part in parts)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{part.mesh}.fbx");
            if (model == null) { Warn($"모델 없음: {part.mesh}"); warn++; continue; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            go.transform.SetParent(rootGo.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            var r = go.GetComponentInChildren<MeshRenderer>();
            if (r != null && mats.TryGetValue(part.mat, out var m)) r.sharedMaterial = m;
            else { Warn($"재질 연결 실패: {part.mesh}"); warn++; }
            if (part.light) { if (r != null) lights.Add(r); }
            else panels.Add(go.transform);
        }

        var dc = rootGo.AddComponent<DoorController>();
        var so = new SerializedObject(dc);
        so.FindProperty("openOffset").vector3Value = Vector3.zero;
        so.FindProperty("openDuration").floatValue = 1.0f;
        so.FindProperty("openClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SoundEffect/문열림sci-fi_door_2_1s_speedup.wav");
        var sp = so.FindProperty("splitPanels");
        sp.arraySize = panels.Count;
        for (int i = 0; i < panels.Count; i++) sp.GetArrayElementAtIndex(i).objectReferenceValue = panels[i];
        var lp = so.FindProperty("statusLights");
        lp.arraySize = lights.Count;
        for (int i = 0; i < lights.Count; i++) lp.GetArrayElementAtIndex(i).objectReferenceValue = lights[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        dc.SetLight(false);
        return warn;
    }

    static int AssignMaterials(GameObject go, Inst it, Dictionary<string, Material> mats)
    {
        var r = go.GetComponentInChildren<MeshRenderer>();
        if (r == null) { Warn($"렌더러 없음: {it.name}"); return 1; }
        int warn = 0;
        var arr = new Material[it.mats.Length];
        for (int i = 0; i < arr.Length; i++)
            if (!mats.TryGetValue(it.mats[i], out arr[i])) { Warn($"재질 없음: {it.mats[i]} ({it.name})"); warn++; }
        var mf = r.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null && mf.sharedMesh.subMeshCount != arr.Length)
        {
            Warn($"서브메시 {mf.sharedMesh.subMeshCount}개 ≠ 재질 {arr.Length}개: {it.name}");
            warn++;
            if (arr.Length > mf.sharedMesh.subMeshCount) System.Array.Resize(ref arr, mf.sharedMesh.subMeshCount);   // 남는 재질은 마지막 서브메시를 덧그리므로 잘라냄
        }
        r.sharedMaterials = arr;
        return warn;
    }

    static void SetupKind(GameObject go, string kind, string mesh)
    {
        var r = go.GetComponentInChildren<MeshRenderer>();
        var target = r != null ? r.gameObject : go;
        switch (kind)
        {
            case "static":
                target.AddComponent<MeshCollider>();
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                break;
            case "door":      // DoorController는 Collider가 필요
                target.AddComponent<BoxCollider>();
                break;
            case "glass":     // 플레이어는 막고 시야(DollVision 레이캐스트)는 통과: obstructionMask에서 Ignore Raycast 제외 필요
                target.AddComponent<BoxCollider>();
                go.layer = target.layer = LayerMask.NameToLayer("Ignore Raycast");
                break;
            case "prop":
            case "device":
                // Convex는 삼각형 255개 한도 → 보이는 모델 대신 Collision/{mesh}_Col.fbx 의 단순 껍데기를 쓴다
                var mc = target.AddComponent<MeshCollider>();
                var colModel = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/Collision/{mesh}_Col.fbx");
                var colMf = colModel != null ? colModel.GetComponentInChildren<MeshFilter>() : null;
                if (colMf != null) mc.sharedMesh = colMf.sharedMesh;
                else Warn($"충돌 메시 없음, 보이는 메시 사용: {mesh}");
                mc.convex = true;
                break;
            case "npc":
                var cap = target.AddComponent<CapsuleCollider>();
                cap.radius = 0.28f; cap.height = 1.55f; cap.center = new Vector3(0f, 0.775f, 0f);
                break;
            case "light":
                var lg = new GameObject("PointLight");
                lg.transform.SetParent(go.transform, false);
                lg.transform.localPosition = new Vector3(0f, -0.3f, 0f);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point; l.range = 9f; l.intensity = 1.6f;
                l.color = new Color(1f, 0.97f, 0.92f); l.shadows = LightShadows.None;
                break;
        }
    }

    static GameObject FindRoot(UnityEngine.SceneManagement.Scene scene, string name)
    {
        foreach (var g in scene.GetRootGameObjects()) if (g.name == name) return g;
        return null;
    }

    static void Log(string m) => Debug.Log($"[튜토리얼씬] {m}");
    static void Warn(string m) => Debug.LogWarning($"[튜토리얼씬] {m}");
}
