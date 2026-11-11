using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 소품(Prop) 콜라이더를 보이는 모양 그대로(비볼록 MeshCollider)로 맞추는 에디터 메뉴 (2026-11-02)
// 왜: 조준은 "조준선에서 가장 가까운 대상"을 고르는데, 얼룩은 그림 면으로 정확히 재고 소품은 콜라이더로 잰다.
//     할로윈 소품은 루트 BoxCollider 하나라 둥근 호박의 상자 모서리(눈에 안 보임)가 얼룩보다 먼저 맞아
//     얼룩을 보고 있어도 Catch가 떴다. 규칙이 아니라 측정이 틀린 것 → 콜라이더를 실제 모양으로.
// - 거친 콜라이더(Box·Sphere·Capsule)는 지우지 않고 끈다. DollDisguise는 꺼진 콜라이더를 복사하지 않음
// - 변장한 외계인은 DollDisguise가 원본 콜라이더를 같은 자리에 복사하므로 미끼와 같은 판정 범위가 유지된다
// - 실행 중 복사되는 MeshCollider가 빌드에서 비지 않도록 쓰인 메시의 FBX Read/Write를 켠다
// 맵·소품 이름을 코드에 넣지 않았다 — 새 맵에서도 메뉴 한 번. 설계: 「코드 설계 - 할로윈 소품 콜라이더 실제 모양」
public static class PropColliderFitter
{
    const string LogTag = "[소품 콜라이더]";

    enum Kind
    {
        Convert,        // 거친 콜라이더만 있음 → 바꿀 대상
        AlreadyFitted,  // MeshCollider만 있음 (인형방 소품, 묘비 프리팹 등)
        Mixed,          // 거친 콜라이더와 MeshCollider가 둘 다 켜짐 → 조준이 바깥 거친 쪽으로 판정됨. 경고만
        NoMesh,         // 메시 렌더러가 없어 모양을 알 수 없음. 경고만
        NoCollider      // 켜진 콜라이더가 없어 조준이 안 됨. 경고만
    }

    class PropInfo
    {
        public GameObject root;
        public Kind kind;
        public readonly List<Collider> coarse = new List<Collider>();
        public readonly List<MeshFilter> meshes = new List<MeshFilter>();
    }

    [MenuItem("Tools/소품 콜라이더/현재 씬 소품을 실제 모양으로")]
    static void FitActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        int interactable = LayerMask.NameToLayer("Interactable");
        if (interactable < 0)
        {
            Debug.LogError($"{LogTag} 'Interactable' 레이어가 없습니다.");
            return;
        }

        List<PropInfo> props = Collect(scene, interactable);
        int convert = 0, fitted = 0, mixed = 0, noMesh = 0, noCollider = 0;
        foreach (PropInfo p in props)
        {
            switch (p.kind)
            {
                case Kind.Convert: convert++; break;
                case Kind.AlreadyFitted: fitted++; break;
                case Kind.Mixed: mixed++; break;
                case Kind.NoMesh: noMesh++; break;
                case Kind.NoCollider: noCollider++; break;
            }
        }
        LogWarnings(props);

        string summary =
            $"씬 '{scene.name}'\n\n" +
            $"바꿀 소품: {convert}개 (상자·구·캡슐 → 실제 모양 MeshCollider)\n" +
            $"이미 실제 모양: {fitted}개\n" +
            $"섞임 경고: {mixed}개 (상자와 MeshCollider가 함께 켜짐)\n" +
            $"메시 없음: {noMesh}개 / 콜라이더 없음: {noCollider}개\n\n" +
            "경고 항목은 바꾸지 않고 콘솔에 위치를 남깁니다.";

        if (convert == 0)
        {
            EditorUtility.DisplayDialog("소품 콜라이더", summary + "\n\n바꿀 소품이 없습니다.", "확인");
            return;
        }
        if (!EditorUtility.DisplayDialog("소품 콜라이더", summary + "\n\n진행할까요? (Ctrl+Z 한 번으로 전부 취소)", "진행", "취소"))
            return;

        // 모든 변경을 Undo 한 묶음으로
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("소품 콜라이더 실제 모양");

        HashSet<Mesh> usedMeshes = new HashSet<Mesh>();
        StringBuilder log = new StringBuilder();
        foreach (PropInfo p in props)
        {
            if (p.kind != Kind.Convert) continue;
            Fit(p, usedMeshes);
            log.AppendLine($"  {PathOf(p.root.transform)}  (끈 콜라이더 {p.coarse.Count}, 붙인 MeshCollider {p.meshes.Count})");
        }
        Undo.CollapseUndoOperations(undoGroup);

        int madeReadable = MakeReadable(usedMeshes);   // 에셋 설정이라 Undo 대상이 아님

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"{LogTag} {convert}개 소품을 실제 모양으로 바꿨습니다. 메시 Read/Write를 켠 FBX {madeReadable}개. 씬을 저장하세요.\n{log}");
    }

    // 씬의 Prop 루트들을 모아 분류. 부모가 Prop이면 부모 쪽이 한 물체 (DollDisguise.CollectCandidates와 같은 기준)
    static List<PropInfo> Collect(Scene scene, int interactable)
    {
        List<PropInfo> result = new List<PropInfo>();
        foreach (GameObject rootObj in scene.GetRootGameObjects())
        {
            foreach (Transform t in rootObj.GetComponentsInChildren<Transform>(true))
            {
                GameObject go = t.gameObject;
                if (go.layer != interactable || !go.CompareTag("Prop")) continue;
                if (t.parent != null && t.parent.CompareTag("Prop")) continue;
                result.Add(Classify(go));
            }
        }
        return result;
    }

    static PropInfo Classify(GameObject root)
    {
        PropInfo info = new PropInfo { root = root };
        bool hasMeshCollider = false;

        foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
        {
            if (!c.enabled || c.isTrigger) continue;
            if (c is MeshCollider) hasMeshCollider = true;
            else if (c is BoxCollider || c is SphereCollider || c is CapsuleCollider) info.coarse.Add(c);
        }

        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null) continue;
            info.meshes.Add(mf);
        }

        bool hasCoarse = info.coarse.Count > 0;
        if (hasCoarse && hasMeshCollider) info.kind = Kind.Mixed;
        else if (hasMeshCollider) info.kind = Kind.AlreadyFitted;
        else if (!hasCoarse) info.kind = Kind.NoCollider;
        else if (info.meshes.Count == 0) info.kind = Kind.NoMesh;
        else info.kind = Kind.Convert;
        return info;
    }

    // 메시가 있는 자리마다 비볼록 MeshCollider를 붙이고, 거친 콜라이더는 끈다.
    // 비볼록인 이유: 볼록은 허수아비 팔 사이 같은 오목한 곳을 메워 상자와 비슷한 문제가 남는다. 소품·변장 부품 모두 Rigidbody가 없어 비볼록을 쓸 수 있다
    static void Fit(PropInfo p, HashSet<Mesh> usedMeshes)
    {
        foreach (MeshFilter mf in p.meshes)
        {
            // 꺼진 MeshCollider가 이미 있으면 새로 붙이지 않고 그것을 켜서 쓴다
            MeshCollider mc = mf.GetComponent<MeshCollider>();
            if (mc == null) mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
            else Undo.RecordObject(mc, "소품 콜라이더 실제 모양");

            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
            mc.isTrigger = false;
            mc.enabled = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(mc);
            usedMeshes.Add(mf.sharedMesh);
        }

        foreach (Collider c in p.coarse)
        {
            Undo.RecordObject(c, "소품 콜라이더 실제 모양");
            c.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        }
    }

    // 변장한 외계인은 DollDisguise가 실행 중에 같은 메시로 MeshCollider를 새로 만든다.
    // Prebake Collision Meshes가 꺼져 있고 메시가 Read/Write 꺼짐이면 빌드에서 그 콜라이더가 빌 수 있다
    // (= 변장 외계인만 조준 안 됨 = 정답이 드러남) → 원본 FBX의 Read/Write를 켠다
    static int MakeReadable(HashSet<Mesh> meshes)
    {
        HashSet<string> paths = new HashSet<string>();
        foreach (Mesh mesh in meshes)
        {
            string path = AssetDatabase.GetAssetPath(mesh);
            if (!string.IsNullOrEmpty(path)) paths.Add(path);
        }

        List<ModelImporter> toFix = new List<ModelImporter>();
        foreach (string path in paths)
        {
            // 기본 도형 등 FBX가 아닌 메시는 ModelImporter가 없다 → 건너뜀
            if (AssetImporter.GetAtPath(path) is ModelImporter importer && !importer.isReadable)
                toFix.Add(importer);
        }
        if (toFix.Count == 0) return 0;

        AssetDatabase.StartAssetEditing();   // 여러 FBX를 한 번에 다시 불러오기
        try
        {
            foreach (ModelImporter importer in toFix)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                Debug.Log($"{LogTag} Read/Write 켬: {importer.assetPath}");
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        return toFix.Count;
    }

    static void LogWarnings(List<PropInfo> props)
    {
        foreach (PropInfo p in props)
        {
            string path = PathOf(p.root.transform);
            switch (p.kind)
            {
                case Kind.Mixed:
                    Debug.LogWarning($"{LogTag} 상자(구·캡슐)와 MeshCollider가 함께 켜져 있어 조준이 바깥쪽 콜라이더로 판정됩니다: {path}", p.root);
                    break;
                case Kind.NoMesh:
                    Debug.LogWarning($"{LogTag} 메시 렌더러가 없어 실제 모양을 알 수 없습니다: {path}", p.root);
                    break;
                case Kind.NoCollider:
                    Debug.LogWarning($"{LogTag} 켜진 콜라이더가 없어 조준할 수 없는 소품입니다: {path}", p.root);
                    break;
            }
        }
    }

    static string PathOf(Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }
}
