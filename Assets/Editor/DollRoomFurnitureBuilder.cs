using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 인형 마니아 방(DollManiaHouse)에 Blender에서 만든 가구·소품·장식과 자연 배치(v2)를 적용하는 에디터 도구.
/// 메뉴: Tools > 인형방 > 배치 적용 (가구·소품·장식) / 가구 모델 지우기(자리표시 다시 켜기)
/// - 데이터: Assets/3DModel&Asset/DollRoom/dollroom_assets.json (Blender build_dollroom_layout2.py + build_dollroom_export.py 결과)
/// - 가구: 자리표시 큐브(Map/Furniture)를 새 위치로 옮기고 끈다(침대는 팀원 모델 자리라 켜 둔 채 옮기기만).
///   모델은 Map/FurnitureModels 아래에 매번 새로 만든다 → FurnitureModels 아래는 손으로 고치지 않기.
/// - 가구에 붙어 있던 것(인형·소품·웨이포인트)은 가구와 함께 옮긴다. json의 remove 항목(주인공 인형 줄이기)은 지우지 않고 비활성화한다. 대상은 '이름 + 원래 x,z'로 찾고, 이미 옮겨졌으면 건너뛴다.
/// - NavMesh 링크(Map/NavLinks/Link_*)는 출발점·도착점을 새로 지정한다.
/// - 소품(공·블록·쟁반·주전자·찻잔)은 오브젝트(태그 Prop, 레이어, NavMeshModifier)는 그대로 두고 메시·재질·콜라이더만 교체.
/// - 씬은 저장하지 않는다. 확인 후 저장(Ctrl+S)하고 NavMesh를 다시 굽는다(Navigation 창 또는 NavMeshSurface의 Bake).
/// - 미끼 인형(json의 dolls)은 Map/Props/AddedDolls, 새 인형·장난감(json의 items)은 Map/Props/AddedProps 아래에 매번 새로 만든다.
///   둘 다 잡기 대상(태그·레이어는 doll.prefab과 같게). 기존 인형 색 섞기는 별도 메뉴.
/// - 방 마감·침대(dollroom_room.json, Blender build_dollroom_room.py): 벽지·징두리 벽판·몰딩(Map/RoomShell), 벽 큐브·바닥 재질, 팀원 침대 bed4.fbx 배치. Apply 끝에서 같이 적용.
/// - 되돌리기: Ctrl+Z(한 번에) 또는 git.
/// </summary>
public static class DollRoomFurnitureBuilder
{
    const string Root = "Assets/3DModel&Asset/DollRoom";
    const string ModelDir = Root + "/Models";
    const string ColDir = ModelDir + "/Collision";
    const string MatDir = Root + "/Materials";
    const string DataPath = Root + "/dollroom_assets.json";
    const string ScenePath = "Assets/Scenes/DollManiaHouse.unity";
    const string ModelRootName = "FurnitureModels";
    const float MatchTol = 0.03f;

    [Serializable] class MatInfo { public string name; public float[] color; public float roughness; public float metallic; public float[] emission; public float emissionStrength; public string texture;
                                   public string normal; public float normalScale; public float[] tiling; }
    [Serializable] class ModelInfo { public string name; public string[] mats; }
    [Serializable] class FurnInfo { public string name; public string model; public float[] pos; public float yaw; public float[] blockPos; public float[] blockScale; public bool keepBlockout; }
    [Serializable] class ExtraInfo { public string name; public string model; public float[] pos; public float yaw; public string kind; }
    [Serializable] class MoveInfo { public string parent; public string name; public float[] old; public float[] pos; public float dyaw; public bool setY; public bool remove; }
    [Serializable] class LinkInfo { public string name; public string parent; public float[] start; public float[] end; }
    [Serializable] class PropInfo { public string name; public string model; }
    [Serializable] class DollInfo { public string name; public float scale; public float[] pos; public float face; public string mat; }
    [Serializable] class ItemInfo { public string name; public string model; public float[] pos; public float face; public float scale; }
    [Serializable] class Data { public MatInfo[] materials; public ModelInfo[] models; public FurnInfo[] furniture; public ExtraInfo[] extras; public MoveInfo[] moves; public LinkInfo[] links; public PropInfo[] props;
                                public DollInfo[] dolls; public string[] alienColors; public string[] allColors; public ItemInfo[] items; }

    // ---------- 방 마감(벽지·바닥) + 침대 (dollroom_room.json, Blender build_dollroom_room.py 결과) ----------
    const string RoomDataPath = Root + "/dollroom_room.json";
    const string ShellName = "RoomShell";
    const float Keep = -999f;     // fixes의 pos 성분 중 그대로 둘 것
    [Serializable] class ShellInfo { public string model; public string[] mats; }
    [Serializable] class FloorInfo { public string path; public string mat; }
    [Serializable] class NodeMats { public string node; public string[] mats; }
    [Serializable] class BedInfo { public string asset; public string name; public float[] pos; public float yaw; public float scale; public string placeholder; public NodeMats[] mats; }
    [Serializable] class FixInfo { public string parent; public string name; public float x; public float z; public float[] pos; }
    [Serializable] class ExtraMove { public string name; public float[] pos; }
    [Serializable] class RoomData { public MatInfo[] materials; public ShellInfo shell; public string[] wallCubes; public string wallMat; public string[] hideRenderers; public string[] deleteObjects; public FloorInfo floor; public BedInfo bed;
                                    public FixInfo[] fixes; public LinkInfo[] links; public ExtraMove[] extraMoves; }

    const string DollPrefabPath = "Assets/3DModel&Asset/doll/doll.prefab";
    const string DollMatDir = "Assets/3DModel&Asset/doll/dollcharacter_fbx/DollCharacter_FBX";
    const string AddedDollsName = "AddedDolls";
    const string AddedItemsName = "AddedProps";     // 새 인형·장난감 (잡기 대상)
    const float MidScale = 0.8843127f;     // 빈대(doll_move)와 같은 크기

    static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);

    static bool s_batch;      // CLI(배치 모드) 실행 중이면 대화상자를 띄우지 않음

    /// <summary>
    /// CLI 실행용: 배치 적용 → 기존 미끼 색 섞기 → NavMesh 다시 굽기 → 씬 저장 → 종료.
    /// Unity.exe -batchmode -projectPath "프로젝트" -executeMethod DollRoomFurnitureBuilder.ApplyBatch -logFile "로그" (에디터가 꺼져 있어야 함)
    /// </summary>
    public static void ApplyBatch()
    {
        try
        {
            s_batch = true;
            Apply();
            RecolorExisting();
            var scene = EditorSceneManager.GetActiveScene();
            BakeNavMesh(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("씬 저장 실패");
            AssetDatabase.SaveAssets();
            Debug.Log("[인형방] CLI 적용 완료");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError($"[인형방] CLI 실패: {e}");
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// CLI 확인용: 씬을 열어 출입문 시점·위에서 본 화면을 PNG로 찍고, 웨이포인트가 NavMesh 위에 있는지 검사(씬은 저장하지 않음).
    /// 출력 폴더는 환경 변수 DOLLROOM_CAPTURE_DIR (없으면 프로젝트 Temp)
    /// </summary>
    public static void CaptureBatch()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            string outDir = Environment.GetEnvironmentVariable("DOLLROOM_CAPTURE_DIR");
            if (string.IsNullOrEmpty(outDir)) outDir = "Temp";
            Directory.CreateDirectory(outDir);
            if (Environment.GetEnvironmentVariable("DOLLROOM_NO_SHOTS") != "1")
            {
                Capture(new Vector3(0f, 1.35f, -6.38f), Quaternion.Euler(8f, 0f, 0f), 62f, Path.Combine(outDir, "dollroom_door.png"));
                Capture(new Vector3(0f, 13f, -12.5f), Quaternion.Euler(52f, 0f, 0f), 52f, Path.Combine(outDir, "dollroom_top.png"));
                Capture(new Vector3(-6.5f, 1.35f, -6.5f), Quaternion.Euler(10f, 40f, 0f), 62f, Path.Combine(outDir, "dollroom_corner.png"));
                Capture(new Vector3(-3.2f, 2.3f, 3.6f), Quaternion.Euler(22f, -50f, 0f), 60f, Path.Combine(outDir, "dollroom_bed.png"));
                Capture(new Vector3(1.5f, 1.6f, 2.5f), Quaternion.Euler(4f, 165f, 0f), 66f, Path.Combine(outDir, "dollroom_doorwall.png"));
                Capture(new Vector3(8.51f, 15.2f, 9.71f), new Quaternion(-0.16215743f, 0.8366262f, -0.37547052f, -0.36439484f), 60f, Path.Combine(outDir, "dollroom_maincam.png"));
            }
            int ok = 0, total = 0; var miss = new List<string>();
            var wg = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "WayPointGroup");
            if (wg != null)
                foreach (Transform t in wg.transform)
                {
                    total++;
                    if (UnityEngine.AI.NavMesh.SamplePosition(t.position, out var hit, 0.35f, UnityEngine.AI.NavMesh.AllAreas)) ok++;
                    else
                    {
                        string near = UnityEngine.AI.NavMesh.SamplePosition(t.position, out var h2, 3f, UnityEngine.AI.NavMesh.AllAreas)
                            ? $"가장 가까운 NavMesh {Vector3.Distance(t.position, h2.position):0.00}m {h2.position}" : "3m 안에 NavMesh 없음";
                        var cols = Physics.OverlapSphere(t.position + Vector3.up * 0.3f, 0.45f).Select(c => c.transform.parent ? c.transform.parent.name + "/" + c.name : c.name);
                        miss.Add($"{t.name} {t.position} ({near}; 주변: {string.Join(", ", cols)})");
                    }
                }
            Debug.Log($"[인형방] 웨이포인트 NavMesh 확인 {ok}/{total} 누락: {string.Join(",", miss)}");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError($"[인형방] 캡처 실패: {e}");
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// CLI 확인용: NavMesh 링크 양끝이 NavMesh에 닿는지, 각 빈대 시작 자리에서 모든 웨이포인트까지 경로가 끝까지 이어지는지 검사 (씬 저장 안 함)
    /// </summary>
    public static void LinkCheckBatch()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var linkType = Type.GetType("Unity.AI.Navigation.NavMeshLink, Unity.AI.Navigation");
            var lines = new List<string>();
            int linkOk = 0, linkTotal = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren(linkType, true))
                {
                    var so = new SerializedObject(c);
                    var t = ((Component)c).transform;
                    Vector3 a = t.TransformPoint(so.FindProperty("m_StartPoint").vector3Value);
                    Vector3 b = t.TransformPoint(so.FindProperty("m_EndPoint").vector3Value);
                    bool sa = UnityEngine.AI.NavMesh.SamplePosition(a, out var ha, 0.4f, UnityEngine.AI.NavMesh.AllAreas);
                    bool sb = UnityEngine.AI.NavMesh.SamplePosition(b, out var hb, 0.4f, UnityEngine.AI.NavMesh.AllAreas);
                    linkTotal++;
                    if (sa && sb) linkOk++;
                    else lines.Add($"링크 {t.name}: 출발 {(sa ? "OK" : "NavMesh 없음")} {a}, 도착 {(sb ? "OK" : "NavMesh 없음")} {b}");
                }
            var wg = scene.GetRootGameObjects().First(g => g.name == "WayPointGroup").transform;
            var dg = scene.GetRootGameObjects().First(g => g.name == "DollGroup").transform;
            var path = new UnityEngine.AI.NavMeshPath();
            foreach (Transform doll in dg)
            {
                if (!UnityEngine.AI.NavMesh.SamplePosition(doll.position, out var hs, 0.6f, UnityEngine.AI.NavMesh.AllAreas)) { lines.Add($"{doll.name} 시작 자리에 NavMesh 없음"); continue; }
                int ok = 0, total = 0; var bad = new List<string>();
                foreach (Transform w in wg)
                {
                    total++;
                    if (!UnityEngine.AI.NavMesh.SamplePosition(w.position, out var hw, 0.35f, UnityEngine.AI.NavMesh.AllAreas)) { bad.Add(w.name + "(NavMesh 없음)"); continue; }
                    UnityEngine.AI.NavMesh.CalculatePath(hs.position, hw.position, UnityEngine.AI.NavMesh.AllAreas, path);
                    if (path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete) ok++;
                    else bad.Add($"{w.name}({path.status})");
                }
                lines.Add($"{doll.name} → 웨이포인트 경로 {ok}/{total} {(bad.Count > 0 ? "실패: " + string.Join(", ", bad) : "")}");
            }
            Debug.Log($"[인형방] 링크 양끝 {linkOk}/{linkTotal}\n" + string.Join("\n", lines.Select(l => "[인형방] " + l)));
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogError($"[인형방] 링크 검사 실패: {e}"); EditorApplication.Exit(1); }
    }

    static void Capture(Vector3 pos, Quaternion rot, float fov, string path)
    {
        const int w = 1600, h = 900;
        var go = new GameObject("CaptureCam");
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.16f, 0.18f);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
        finally { Object.DestroyImmediate(go); }
    }

    // NavMeshSurface를 다시 굽고, 기존 NavMesh 에셋(GUID 유지)에 덮어씀
    static void BakeNavMesh(UnityEngine.SceneManagement.Scene scene)
    {
        var t = Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation");
        if (t == null) { Debug.LogWarning("[인형방] NavMeshSurface 타입을 찾지 못해 굽기를 건너뜀"); return; }
        var dataProp = t.GetProperty("navMeshData");
        var build = t.GetMethod("BuildNavMesh", Type.EmptyTypes);
        // 빈대(NavMeshAgent) 몸은 굽기에서 빼야 함: 설정의 '에이전트 무시'는 에이전트가 붙은 오브젝트만 빼고 자식 메시는 장애물로 넣어 시작 자리에 구멍이 남
        var agents = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))
                          .Select(a => a.gameObject).Where(go => go.activeSelf).ToList();
        foreach (var go in agents) go.SetActive(false);
        try
        {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var c in root.GetComponentsInChildren(t, true))
            {
                var oldData = dataProp.GetValue(c) as UnityEngine.AI.NavMeshData;
                string path = oldData != null ? AssetDatabase.GetAssetPath(oldData) : null;
                build.Invoke(c, null);
                var newData = dataProp.GetValue(c) as UnityEngine.AI.NavMeshData;
                if (newData == null) { Debug.LogWarning("[인형방] NavMesh 굽기 결과가 비었음"); continue; }
                if (!string.IsNullOrEmpty(path) && newData != oldData)
                {
                    EditorUtility.CopySerialized(newData, oldData);
                    EditorUtility.SetDirty(oldData);
                    dataProp.SetValue(c, oldData);
                }
                else if (string.IsNullOrEmpty(path))
                {
                    string dir = $"{Path.GetDirectoryName(scene.path)}/{scene.name}".Replace('\\', '/');
                    if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Path.GetDirectoryName(scene.path).Replace('\\', '/'), scene.name);
                    path = $"{dir}/NavMesh-{((Component)c).gameObject.name}.asset";
                    AssetDatabase.CreateAsset(newData, path);
                }
                EditorUtility.SetDirty((Component)c);
                Debug.Log($"[인형방] NavMesh 다시 구움 → {path} (빈대 {agents.Count}마리 빼고)");
            }
        }
        }
        finally { foreach (var go in agents) go.SetActive(true); }
    }

    [MenuItem("Tools/인형방/배치 적용 (가구·소품·장식)")]
    static void Apply()
    {
        if (!s_batch && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        AssetDatabase.Refresh();
        ConfigureImporters(ModelDir);
        var data = JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));
        var mats = BuildMaterials(data.materials);
        var slots = new Dictionary<string, Material[]>();
        foreach (var m in data.models) slots[m.name] = m.mats.Select(n => mats[n]).ToArray();
        float flip = DetectFrontIsPlusZ() ? 0f : 180f;

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var map = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Map");
        if (map == null) { Debug.LogError("[인형방] Map 오브젝트를 찾지 못함"); return; }
        Undo.SetCurrentGroupName("인형방 배치 적용");
        int group = Undo.GetCurrentGroup();

        // ---------- 가구 모델 + 자리표시 이동 ----------
        var oldRoot = map.transform.Find(ModelRootName);
        if (oldRoot) Undo.DestroyObjectImmediate(oldRoot.gameObject);
        var root = new GameObject(ModelRootName);
        Undo.RegisterCreatedObjectUndo(root, "create");
        root.transform.SetParent(map.transform, false);
        var furnRoot = map.transform.Find("Furniture");
        int nf = 0;
        foreach (var fi in data.furniture)
        {
            var block = furnRoot ? furnRoot.Find(fi.name) : null;
            if (block)
            {
                Undo.RecordObject(block, "move");
                Undo.RecordObject(block.gameObject, "move");
                block.position = V(fi.blockPos);
                block.localScale = V(fi.blockScale);
                block.gameObject.SetActive(fi.keepBlockout);
            }
            if (string.IsNullOrEmpty(fi.model)) continue;
            if (Place(scene, root.transform, fi.name, fi.model, V(fi.pos), fi.yaw + flip, slots, true) != null) nf++;
        }
        var deco = new GameObject("Decor");
        Undo.RegisterCreatedObjectUndo(deco, "create");
        deco.transform.SetParent(root.transform, false);
        int ne = 0;
        foreach (var ex in data.extras)
        {
            bool wall = ex.kind == "wall";
            var go = Place(scene, wall ? deco.transform : root.transform, ex.name, ex.model, V(ex.pos), ex.yaw + flip, slots, !wall);
            if (go == null) continue;
            if (wall) IgnoreFromNavMesh(go);
            ne++;
        }

        // ---------- 부착물 이동 (인형·소품·웨이포인트·빈대 시작 자리) ----------
        // 1차: 원래 자리(old) 또는 새 자리(pos)에 정확히 있는 것. 2차: 남은 항목은 새 자리 근처의 같은 이름(이전 적용으로 옮겨진 것)
        int nm = 0, nskip = 0, nmiss = 0, nrem = 0, nfix = 0;
        var used = new HashSet<Transform>();
        var pending = new List<MoveInfo>();
        void DoMove(Transform hit, MoveInfo mv, bool rotate)
        {
            if (mv.remove)      // 주인공 인형 줄이기: 지우지 않고 끔 (다시 켜면 원래대로)
            {
                if (hit.gameObject.activeSelf) { Undo.RecordObject(hit.gameObject, "hide"); hit.gameObject.SetActive(false); nrem++; }
                return;
            }
            Undo.RecordObject(hit, "move");
            // 높이: 인형·높이를 바꾼 가구(벽선반 윗칸) 위의 것은 새 높이, 나머지(소품 등 피벗이 바뀌는 것)는 그대로
            hit.position = new Vector3(mv.pos[0], mv.setY ? mv.pos[1] : hit.position.y, mv.pos[2]);
            if (rotate && Mathf.Abs(mv.dyaw) > 0.01f) hit.rotation = Quaternion.Euler(0f, mv.dyaw, 0f) * hit.rotation;   // 원래 자리에서 옮길 때만 회전
            nm++;
        }
        foreach (var mv in data.moves)
        {
            var parent = FindPath(scene, mv.parent);
            if (parent == null) { nmiss++; continue; }
            Transform hit = null; bool done = false, fromOld = false;
            foreach (Transform c in parent)
            {
                if (c.name != mv.name || used.Contains(c)) continue;
                var p = c.position;
                bool atOld = Mathf.Abs(p.x - mv.old[0]) < MatchTol && Mathf.Abs(p.z - mv.old[2]) < MatchTol;
                bool atNew = Mathf.Abs(p.x - mv.pos[0]) < MatchTol && Mathf.Abs(p.z - mv.pos[2]) < MatchTol;
                if (!atOld && !atNew) continue;
                if (atNew && !mv.remove && (!mv.setY || Mathf.Abs(p.y - mv.pos[1]) < MatchTol)) { done = true; used.Add(c); break; }
                hit = c; fromOld = atOld && !atNew; break;
            }
            if (done) { nskip++; continue; }
            if (hit == null) { pending.Add(mv); continue; }
            DoMove(hit, mv, fromOld); used.Add(hit);
        }
        foreach (var mv in pending)
        {
            var parent = FindPath(scene, mv.parent);
            Transform best = null; float bd = 1.2f;
            if (parent != null && !mv.remove)
                foreach (Transform c in parent)
                {
                    if (c.name != mv.name || used.Contains(c)) continue;
                    var p = c.position;
                    float d = new Vector2(p.x - mv.pos[0], p.z - mv.pos[2]).magnitude;
                    if (d < bd && Mathf.Abs(p.y - mv.pos[1]) < 0.6f) { bd = d; best = c; }
                }
            if (best == null) { nmiss++; Debug.LogWarning($"[인형방] 옮길 대상 없음: {mv.parent}/{mv.name} ({mv.old[0]:0.##}, {mv.old[2]:0.##})"); continue; }
            DoMove(best, mv, false); used.Add(best); nfix++;
        }

        // ---------- NavMesh 링크 ----------
        int nl = 0;
        foreach (var li in data.links) if (SetLink(scene, li)) nl++;

        // ---------- 소품 메시 교체 ----------
        int np = SwapProps(map, data, slots);

        // ---------- 미끼 인형 (Map/Props/AddedDolls, 매번 새로 만듦) ----------
        int nd = SpawnDolls(scene, map, data);

        // ---------- 새 인형·장난감 (Map/Props/AddedProps, 매번 새로 만듦) ----------
        int ni = SpawnItems(scene, map, data, slots, flip);

        // ---------- 방 마감 + 침대 (가구·소품이 다 놓인 뒤) ----------
        ApplyRoom(scene, map, flip);

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[인형방] 가구 {nf}, 추가 가구·장식 {ne}, 부착물 이동 {nm}(이미 옮겨짐 {nskip}, 근처에서 찾음 {nfix}, 못 찾음 {nmiss}), 기존 인형 끔 {nrem}, 링크 {nl}, 소품 교체 {np}, 미끼 인형 {nd}, 새 인형·장난감 {ni}. " +
                  "확인 후 씬 저장(Ctrl+S)하고 NavMesh를 다시 구우세요.");
    }

    [MenuItem("Tools/인형방/가구 모델 지우기 (자리표시 다시 켜기)")]
    static void Revert()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var map = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Map");
        if (map == null) return;
        var r = map.transform.Find(ModelRootName);
        if (r) Undo.DestroyObjectImmediate(r.gameObject);
        var furn = map.transform.Find("Furniture");
        if (furn) foreach (Transform t in furn) { Undo.RecordObject(t.gameObject, "show"); t.gameObject.SetActive(true); }
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static bool SetLink(UnityEngine.SceneManagement.Scene scene, LinkInfo li)
    {
        var t = FindPath(scene, li.parent + "/" + li.name);
        if (t == null) { Debug.LogWarning($"[인형방] 링크 없음: {li.name}"); return false; }
        var link = t.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "NavMeshLink");
        if (link == null) return false;
        Undo.RecordObject(t, "link");
        t.position = V(li.start);
        t.rotation = Quaternion.identity;
        var so = new SerializedObject(link);
        so.FindProperty("m_StartPoint").vector3Value = Vector3.zero;
        so.FindProperty("m_EndPoint").vector3Value = V(li.end) - V(li.start);
        so.ApplyModifiedProperties();
        return true;
    }

    /// <summary>
    /// 방 마감 + 침대 (dollroom_room.json). Apply 끝에서 호출.
    /// - 벽: 기존 벽 큐브(Map/Wall/*)는 충돌·NavMesh용으로 그대로 두고 재질만 흰 페인트로. 안쪽 면 4mm 앞에 벽지·징두리 벽판·걸레받이·몰딩·문틀(Map/RoomShell, NavMesh 제외)
    /// - 바닥: Map/Plane 재질을 원목 마루로 (타일 반복은 재질에)
    /// - 침대: 팀원 모델 bed4.fbx를 Map/FurnitureModels/침대 로 놓고 재질·MeshCollider, 자리표시 큐브는 끔. 침대 위 물건 높이·링크·협탁 위치 보정(fixes)
    /// </summary>
    static void ApplyRoom(UnityEngine.SceneManagement.Scene scene, GameObject map, float flip)
    {
        if (!File.Exists(RoomDataPath)) { Debug.Log("[인형방] dollroom_room.json 없음 → 방 마감 건너뜀"); return; }
        var room = JsonUtility.FromJson<RoomData>(File.ReadAllText(RoomDataPath));
        ConfigureImporters(ModelDir);
        var mats = BuildMaterials(room.materials);
        Material M(string n) { if (n != null && mats.TryGetValue(n, out var m)) return m; Debug.LogWarning($"[인형방] 재질 없음: {n}"); return null; }

        // 벽 안쪽 마감
        var old = map.transform.Find(ShellName);
        if (old) Undo.DestroyObjectImmediate(old.gameObject);
        if (room.shell != null && !string.IsNullOrEmpty(room.shell.model))
        {
            var slots = new Dictionary<string, Material[]> { [room.shell.model] = room.shell.mats.Select(M).ToArray() };
            var go = Place(scene, map.transform, ShellName, room.shell.model, Vector3.zero, flip, slots, false);
            if (go != null) IgnoreFromNavMesh(go);
        }
        int nw = 0;
        if (room.wallCubes != null)
            foreach (var p in room.wallCubes)
            {
                var t = FindPath(scene, p);
                var r = t ? t.GetComponent<MeshRenderer>() : null;
                if (r == null) { Debug.LogWarning($"[인형방] 벽 없음: {p}"); continue; }
                Undo.RecordObject(r, "wall"); r.sharedMaterial = M(room.wallMat); nw++;
            }
        if (room.floor != null)
        {
            var t = FindPath(scene, room.floor.path);
            var r = t ? t.GetComponent<MeshRenderer>() : null;
            if (r != null) { Undo.RecordObject(r, "floor"); r.sharedMaterial = M(room.floor.mat); }
            else Debug.LogWarning($"[인형방] 바닥 없음: {room.floor.path}");
        }
        if (room.hideRenderers != null)      // 바닥과 같은 높이에 겹친 면(깜빡임) → 렌더러만 끄고 충돌은 유지
            foreach (var p in room.hideRenderers)
            {
                var t = FindPath(scene, p);
                var r = t ? t.GetComponent<MeshRenderer>() : null;
                if (r != null && r.enabled) { Undo.RecordObject(r, "hide"); r.enabled = false; }
            }
        int ndel = 0;
        if (room.deleteObjects != null)      // 쓰지 않는 블록아웃 오브젝트 삭제 (없으면 이미 지운 것)
            foreach (var p in room.deleteObjects)
            {
                var t = FindPath(scene, p);
                if (t != null) { Undo.DestroyObjectImmediate(t.gameObject); ndel++; Debug.Log($"[인형방] 삭제: {p}"); }
            }

        // 침대
        bool bedOk = false;
        if (room.bed != null && !string.IsNullOrEmpty(room.bed.asset))
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(room.bed.asset);
            var parent = map.transform.Find(ModelRootName) ?? map.transform;
            if (asset == null) Debug.LogWarning($"[인형방] 침대 모델 없음: {room.bed.asset}");
            else
            {
                var prev = parent.Find(room.bed.name);
                if (prev) Undo.DestroyObjectImmediate(prev.gameObject);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                Undo.RegisterCreatedObjectUndo(go, "create");
                go.name = room.bed.name;
                go.transform.SetParent(parent, true);
                go.transform.position = V(room.bed.pos);
                go.transform.rotation = Quaternion.Euler(0f, room.bed.yaw, 0f);    // 마야 모델: 우리 FBX와 앞뒤 규칙이 달라 flip 안 씀
                go.transform.localScale = Vector3.one * room.bed.scale;
                var byNode = (room.bed.mats ?? new NodeMats[0]).ToDictionary(n => n.node, n => n.mats);
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr != null && byNode.TryGetValue(mf.gameObject.name, out var ms))
                    {
                        var arr = ms.Select(M).ToArray();
                        if (arr.Length < mf.sharedMesh.subMeshCount) arr = Enumerable.Range(0, mf.sharedMesh.subMeshCount).Select(i => arr[Mathf.Min(i, arr.Length - 1)]).ToArray();
                        mr.sharedMaterials = arr;
                    }
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                }
                var ph = FindPath(scene, room.bed.placeholder);
                if (ph) { Undo.RecordObject(ph.gameObject, "hide"); ph.gameObject.SetActive(false); }
                bedOk = true;
            }
        }

        // 침대 위 물건 높이 등: 현재 자리(x,z) 또는 이미 옮긴 자리에서 찾음
        int nfx = 0, nfxMiss = 0;
        if (room.fixes != null)
            foreach (var f in room.fixes)
            {
                var parent = FindPath(scene, f.parent);
                if (parent == null) { nfxMiss++; continue; }
                Vector3 Target(Vector3 p) => new Vector3(f.pos[0] == Keep ? p.x : f.pos[0], f.pos[1] == Keep ? p.y : f.pos[1], f.pos[2] == Keep ? p.z : f.pos[2]);
                Transform hit = null;
                foreach (Transform c in parent)
                {
                    if (c.name != f.name) continue;
                    var p = c.position;
                    var tgt = Target(p);
                    bool atCur = Mathf.Abs(p.x - f.x) < MatchTol && Mathf.Abs(p.z - f.z) < MatchTol;
                    bool atNew = Mathf.Abs(p.x - tgt.x) < MatchTol && Mathf.Abs(p.z - tgt.z) < MatchTol && Mathf.Abs(p.y - tgt.y) < MatchTol;
                    if (atCur || atNew) { hit = c; break; }
                }
                if (hit == null) { nfxMiss++; Debug.LogWarning($"[인형방] 보정 대상 없음: {f.parent}/{f.name} ({f.x:0.##}, {f.z:0.##})"); continue; }
                Undo.RecordObject(hit, "fix");
                hit.position = Target(hit.position);
                nfx++;
            }
        int nl = 0;
        if (room.links != null) foreach (var li in room.links) if (SetLink(scene, li)) nl++;
        int nx = 0;
        if (room.extraMoves != null)
        {
            var mroot = map.transform.Find(ModelRootName);
            foreach (var em in room.extraMoves)
            {
                var t = mroot ? mroot.Find(em.name) : null;
                if (t == null) { Debug.LogWarning($"[인형방] 옮길 가구 없음: {em.name}"); continue; }
                Undo.RecordObject(t, "move"); t.position = V(em.pos); nx++;
            }
        }
        Debug.Log($"[인형방] 방 마감: 벽 {nw}, 바닥 {(room.floor != null ? 1 : 0)}, 침대 {(bedOk ? "OK" : "없음")}, 보정 {nfx}(못 찾음 {nfxMiss}), 링크 {nl}, 가구 이동 {nx}");
    }

    static GameObject Place(UnityEngine.SceneManagement.Scene scene, Transform parent, string name, string model, Vector3 pos, float yaw,
                            Dictionary<string, Material[]> slots, bool collider)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{model}.fbx");
        if (asset == null) { Debug.LogWarning($"[인형방] 모델 없음: {model}"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
        Undo.RegisterCreatedObjectUndo(go, "create");
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = Vector3.one;
        var mf = go.GetComponentInChildren<MeshFilter>();
        if (slots.TryGetValue(model, out var ms)) mf.GetComponent<MeshRenderer>().sharedMaterials = ms;
        if (collider) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        return go;
    }

    static void IgnoreFromNavMesh(GameObject go)    // 벽 장식(창턱·액자 윗면)에 NavMesh가 생기지 않게
    {
        var type = Type.GetType("Unity.AI.Navigation.NavMeshModifier, Unity.AI.Navigation");
        if (type == null) return;
        var c = go.AddComponent(type);
        var so = new SerializedObject(c);
        var p = so.FindProperty("m_IgnoreFromBuild");
        if (p != null) { p.boolValue = true; so.ApplyModifiedProperties(); }
    }

    static Transform FindPath(UnityEngine.SceneManagement.Scene scene, string path)
    {
        var parts = path.Split('/');
        var r = scene.GetRootGameObjects().FirstOrDefault(g => g.name == parts[0]);
        if (r == null) return null;
        var t = r.transform;
        for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
        return t;
    }

    static int SwapProps(GameObject map, Data data, Dictionary<string, Material[]> slots)
    {
        int np = 0;
        var props = map.transform.Find("Props");
        if (!props) return 0;
        var pinfo = data.props.ToDictionary(p => p.name);
        foreach (Transform t in props.Cast<Transform>().ToList())
        {
            if (!pinfo.TryGetValue(t.name, out var pi)) continue;
            var mf = t.GetComponent<MeshFilter>();
            var mr = t.GetComponent<MeshRenderer>();
            if (mf == null || mr == null || mf.sharedMesh == null) continue;
            if (mf.sharedMesh.name.StartsWith("DR_Prop")) continue;     // 이미 교체됨
            var b = mf.sharedMesh.bounds;
            var c = t.TransformPoint(b.center);
            float bottom = c.y - b.extents.y * Mathf.Abs(t.lossyScale.y);
            string model = pi.model;
            if (model.Contains("|"))    // 블록: 큰 것(0.2 이상) / 작은 것
            {
                var two = model.Split('|');
                model = Mathf.Max(t.lossyScale.x, t.lossyScale.z) * b.size.x >= 0.2f ? two[0] : two[1];
            }
            var mesh = LoadMesh($"{ModelDir}/{model}.fbx");
            var col = LoadMesh($"{ColDir}/{model}_Col.fbx");
            if (mesh == null || col == null) { Debug.LogWarning($"[인형방] 소품 모델 없음: {model}"); continue; }
            Undo.RecordObject(t, "prop"); Undo.RecordObject(mf, "prop"); Undo.RecordObject(mr, "prop");
            mf.sharedMesh = mesh;
            mr.sharedMaterials = slots[model];
            var ps = t.parent ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(1f / ps.x, 1f / ps.y, 1f / ps.z);
            t.position = new Vector3(c.x, bottom, c.z);
            foreach (var cc in t.GetComponents<Collider>())
                if (!(cc is MeshCollider)) Undo.DestroyObjectImmediate(cc);
            var mc = t.GetComponent<MeshCollider>();
            if (mc == null) mc = Undo.AddComponent<MeshCollider>(t.gameObject);
            mc.sharedMesh = col;
            mc.convex = true;
            np++;
        }
        return np;
    }

    // ---------- 미끼 인형 ----------
    static int SpawnDolls(UnityEngine.SceneManagement.Scene scene, GameObject map, Data data)
    {
        if (data.dolls == null || data.dolls.Length == 0) return 0;
        var props = map.transform.Find("Props");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DollPrefabPath);
        if (props == null || prefab == null) { Debug.LogWarning("[인형방] Map/Props 또는 doll.prefab 없음"); return 0; }
        var old = props.Find(AddedDollsName);
        if (old) Undo.DestroyObjectImmediate(old.gameObject);
        var root = new GameObject(AddedDollsName);
        Undo.RegisterCreatedObjectUndo(root, "create");
        root.transform.SetParent(props, false);
        float front = DollFrontAngle(prefab);
        int n = 0;
        foreach (var d in data.dolls)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(go, "create");
            go.name = d.name;
            go.transform.SetParent(root.transform, false);
            go.transform.position = V(d.pos);
            go.transform.rotation = Quaternion.Euler(0f, d.face - front, 0f);
            go.transform.localScale = Vector3.one * d.scale;
            SetDollMat(go, d.mat);
            IgnoreFromNavMesh(go);     // 기존 인형처럼 NavMesh에서 제외 (가구 윗면이 깎이지 않게)
            n++;
        }
        return n;
    }

    // 새 인형·장난감: 기존 잡기 대상(인형 프리팹)과 같은 태그·레이어, Convex 충돌 메시, NavMesh 제외
    static int SpawnItems(UnityEngine.SceneManagement.Scene scene, GameObject map, Data data, Dictionary<string, Material[]> slots, float flip)
    {
        if (data.items == null || data.items.Length == 0) return 0;
        var props = map.transform.Find("Props");
        var dollPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DollPrefabPath);
        if (props == null) return 0;
        string tag = dollPrefab ? dollPrefab.tag : "Prop";
        int layer = dollPrefab ? dollPrefab.layer : 6;
        var old = props.Find(AddedItemsName);
        if (old) Undo.DestroyObjectImmediate(old.gameObject);
        var root = new GameObject(AddedItemsName);
        Undo.RegisterCreatedObjectUndo(root, "create");
        root.transform.SetParent(props, false);
        int n = 0;
        foreach (var it in data.items)
        {
            var go = Place(scene, root.transform, it.name, it.model, V(it.pos), it.face + flip, slots, false);
            if (go == null) continue;
            if (it.scale > 0f) go.transform.localScale = Vector3.one * it.scale;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) { t.gameObject.tag = tag; t.gameObject.layer = layer; }
            var mf = go.GetComponentInChildren<MeshFilter>();
            var col = LoadMesh($"{ColDir}/{it.model}_Col.fbx");
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = col != null ? col : mf.sharedMesh;
            mc.convex = true;
            IgnoreFromNavMesh(go);
            n++;
        }
        return n;
    }

    // 인형 모델의 정면 방향(루트 기준 yaw). 얼굴 쪽에 정점이 몰려 있어 '정점 평균 - 상자 중심' 방향이 정면
    static float DollFrontAngle(GameObject prefab)
    {
        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            var mf = tmp.GetComponentInChildren<MeshFilter>();
            var vs = mf.sharedMesh.vertices;
            Vector3 sum = Vector3.zero, mn = Vector3.one * 1e9f, mx = -Vector3.one * 1e9f;
            foreach (var v in vs)
            {
                var p = tmp.transform.InverseTransformPoint(mf.transform.TransformPoint(v));
                sum += p; mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
            }
            var dir = sum / vs.Length - (mn + mx) * 0.5f;
            return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        }
        catch { return 0f; }
        finally { Object.DestroyImmediate(tmp); }
    }

    static void SetDollMat(GameObject go, string mat)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>($"{DollMatDir}/{mat}.mat");
        var r = go.GetComponentInChildren<Renderer>();
        if (m != null && r != null) { Undo.RecordObject(r, "color"); r.sharedMaterial = m; }
    }

    // 기존 인형(미끼)의 색을 빈대 색과 같은 분포로 섞음: 빈대와 같은 크기 = 빈대 색만, 나머지 = 6색 전부
    [MenuItem("Tools/인형방/기존 미끼 인형 색 섞기 (빈대 색과 맞추기)")]
    static void RecolorExisting()
    {
        var data = JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));
        var scene = EditorSceneManager.GetActiveScene();
        var map = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Map");
        var props = map ? map.transform.Find("Props") : null;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DollPrefabPath);
        if (props == null || prefab == null) { Debug.LogWarning("[인형방] DollManiaHouse 씬을 열고 실행하세요"); return; }
        Undo.SetCurrentGroupName("인형방 미끼 색 섞기");
        int group = Undo.GetCurrentGroup();
        var rng = new System.Random(20261007);
        int n = 0;
        foreach (Transform t in props)
        {
            if (t.name == AddedDollsName) continue;
            if (PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) != prefab) continue;
            bool mid = Mathf.Abs(t.localScale.x - MidScale) < 0.02f;
            var pool = mid ? data.alienColors : data.allColors;
            SetDollMat(t.gameObject, pool[rng.Next(pool.Length)]);
            n++;
        }
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[인형방] 기존 미끼 인형 {n}개 색 섞음 (빈대 크기는 {string.Join("/", data.alienColors)}만). 되돌리기는 Ctrl+Z");
    }

    static Mesh LoadMesh(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault();

    // Blender 정면(-Y)이 Unity +Z로 들어왔는지 벽 선반으로 확인 (받침 고정대는 뒤쪽·판 아래에 있음)
    static bool DetectFrontIsPlusZ()
    {
        var m = LoadMesh($"{ModelDir}/DR_WallShelf.fbx");
        if (m == null) return true;
        float sum = 0f; int n = 0;
        try { foreach (var v in m.vertices) if (v.y < -0.08f) { sum += v.z; n++; } }
        catch { return true; }
        return n == 0 || sum / n < 0f;    // 뒤쪽이 -Z면 정면은 +Z
    }

    static void ConfigureImporters(string dir)
    {
        if (!AssetDatabase.IsValidFolder(dir)) return;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { dir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) continue;
            bool dirty = false;
            if (imp.materialImportMode != ModelImporterMaterialImportMode.None) { imp.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
            if (imp.importCameras) { imp.importCameras = false; dirty = true; }
            if (imp.importLights) { imp.importLights = false; dirty = true; }
            if (imp.importAnimation) { imp.importAnimation = false; dirty = true; }
            if (imp.animationType != ModelImporterAnimationType.None) { imp.animationType = ModelImporterAnimationType.None; dirty = true; }
            if (!imp.generateSecondaryUV && !path.StartsWith(ColDir)) { imp.generateSecondaryUV = true; dirty = true; }
            if (dirty) imp.SaveAndReimport();
        }
    }

    // 텍스처 가져오기 설정: 반복(Repeat), 비스듬히 봐도 덜 뭉개지게 이방성 필터, 노멀맵이면 NormalMap 형식
    static void ConfigureTexture(string path, bool normal)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) return;
        bool dirty = false;
        var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (imp.textureType != type) { imp.textureType = type; dirty = true; }
        if (imp.wrapMode != TextureWrapMode.Repeat) { imp.wrapMode = TextureWrapMode.Repeat; dirty = true; }
        if (imp.anisoLevel < 8) { imp.anisoLevel = 8; dirty = true; }
        if (imp.maxTextureSize < 2048) { imp.maxTextureSize = 2048; dirty = true; }
        if (dirty) imp.SaveAndReimport();
    }

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
            m.SetColor("_BaseColor", new Color(mi.color[0], mi.color[1], mi.color[2], 1f).gamma);   // Blender 값은 선형
            // 텍스처(체크 천 등): Root 기준 경로. 없으면 비움
            if (!string.IsNullOrEmpty(mi.texture)) ConfigureTexture($"{Root}/{mi.texture}", false);
            Texture2D baseTex = string.IsNullOrEmpty(mi.texture) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/{mi.texture}");
            if (!string.IsNullOrEmpty(mi.texture) && baseTex == null) Debug.LogWarning($"[인형방] 텍스처 없음: {Root}/{mi.texture}");
            m.SetTexture("_BaseMap", baseTex);
            // 노멀맵 (벽판 홈·마루 이음매)
            Texture2D nrm = null;
            if (!string.IsNullOrEmpty(mi.normal))
            {
                ConfigureTexture($"{Root}/{mi.normal}", true);
                nrm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/{mi.normal}");
                if (nrm == null) Debug.LogWarning($"[인형방] 노멀맵 없음: {Root}/{mi.normal}");
            }
            m.SetTexture("_BumpMap", nrm);
            m.SetFloat("_BumpScale", nrm != null ? (mi.normalScale > 0f ? mi.normalScale : 1f) : 1f);
            if (nrm != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
            // 타일 반복 (바닥 Plane처럼 UV가 0~1인 메시용). URP Lit은 _BaseMap 반복을 노멀맵에도 씀
            var tiling = mi.tiling != null && mi.tiling.Length >= 2 ? new Vector2(mi.tiling[0], mi.tiling[1]) : Vector2.one;
            m.SetTextureScale("_BaseMap", tiling);
            m.SetFloat("_Metallic", mi.metallic);
            m.SetFloat("_Smoothness", 1f - mi.roughness);
            if (mi.emissionStrength > 0f && mi.emission != null && mi.emission.Length >= 3)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", new Color(mi.emission[0], mi.emission[1], mi.emission[2]) * (mi.emissionStrength * 0.5f));
            }
            else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
            EditorUtility.SetDirty(m);
            dict[mi.name] = m;
        }
        AssetDatabase.SaveAssets();
        return dict;
    }
}
