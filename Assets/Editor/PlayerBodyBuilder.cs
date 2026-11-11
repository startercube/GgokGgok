using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 플레이어 몸을 플 모델로 바꾸는 배치 도구 (2026-10-24, 튜토리얼 정장 추가 2026-10-25).
/// 씬마다 옷을 고른다: 현장(인형방·할로윈 방) = 방역복 + 장비 / 튜토리얼(회사 내부) = 정장, 장비 없음
/// 메뉴 Tools > 플레이어 > 플 모델 적용 (튜토리얼·인형방·할로윈 방) / CLI: -executeMethod PlayerBodyBuilder.ApplyBatch (Unity 에디터를 끈 상태)
/// - Player 아래 PL_Visual은 매번 새로 만든다 → 그 아래는 손으로 고치지 않기
/// - 예전 몸(Visual: 임시 머리·캡슐)은 끄기만 한다 (삭제 아님, 켜면 예전 모습)
/// - PegDollAnimator(걷는 모습)·PlayerBodyView(1인칭 그림자만 / 리플레이 보임)를 붙이고 값을 맞춘다
/// 설계 기록: 프로젝트 문서 「코드 설계 - 플 모델·리플레이 녹화 시작」 2장, 「코드 설계 - 튜토리얼 플 모델(정장)」
/// </summary>
public static class PlayerBodyBuilder
{
    const string ModelDir = "Assets/3DModel&Asset/TrainingRoom/";
    const string MatDir = ModelDir + "Materials/";
    const string PlModel = ModelDir + "Models/CH_PL.fbx";
    const string PlGear = ModelDir + "Models/CH_PL_FieldGear.fbx";
    const string VisualName = "PL_Visual";
    const string OldVisualName = "Visual";

    // 옷: CH_PL 재질 (FBX 슬롯 순서: 몸, 얼굴) + 현장 장비를 붙일지
    struct Outfit
    {
        public string name; public string[] bodyMats; public bool gear;
        public Outfit(string name, string[] bodyMats, bool gear) { this.name = name; this.bodyMats = bodyMats; this.gear = gear; }
    }
    static readonly Outfit Field = new Outfit("방역복", new[] { "TK_PL_Field", "TK_Face_PL" }, true);    // 현장
    static readonly Outfit Suit = new Outfit("정장", new[] { "TK_PL_Suit", "TK_Face_PL" }, false);      // 회사 내부 (튜토리얼)

    // 맵이 늘면 줄 하나 추가
    static readonly (string path, Outfit outfit)[] Scenes =
    {
        ("Assets/Scenes/Tutorial.unity", Suit),
        ("Assets/Scenes/DollManiaHouse.unity", Field),
        ("Assets/Scenes/HalloweenNight.unity", Field),
    };
    // CH_PL_FieldGear 재질 (Blender tk_gear.gear_mats 슬롯 순서, 한 주임 장비와 같음 — MapIntroBuilder.GearMats)
    static readonly string[] GearMats = { "TK_Gear_Rubber", "TK_Gear_Navy", "TK_Gear_White", "TK_Metal", "TK_Gear_Hose", "TK_Gear_Blue", "TK_Gear_Lens", "TK_Gear_MaskGray", "TK_Gear_Filter" };

    // 페그돌 걸음 (PlayerMove.moveSpeed 5에 맞춤, 박자는 한 주임 1.6/0.55와 같게)
    const float WalkFullSpeed = 5f;
    const float WalkStride = 1.72f;   // 2026-10-24 한 주임과 같은 박자: 5 × 0.55 / 1.6 (예전 1.4)

    [MenuItem("Tools/플레이어/플 모델 적용 (튜토리얼·인형방·할로윈 방)")]
    public static void ApplyMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string reopen = SceneManager.GetActiveScene().path;
        foreach (var (path, outfit) in Scenes)
        {
            try { Apply(path, outfit); }
            catch (Exception e) { Debug.LogError($"[플 모델] {path} 실패: {e}"); }
        }
        if (!string.IsNullOrEmpty(reopen) && reopen != Scenes[Scenes.Length - 1].path)
            EditorSceneManager.OpenScene(reopen, OpenSceneMode.Single);
    }

    public static void ApplyBatch()
    {
        try { foreach (var (path, outfit) in Scenes) Apply(path, outfit); }
        catch (Exception e) { Debug.LogError($"[플 모델] 실패: {e}"); EditorApplication.Exit(1); }
    }

    static void Apply(string scenePath, Outfit outfit)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        string label = scene.name;

        var player = FindPlayer(scene) ?? throw new Exception($"{label}: Player 태그 오브젝트가 없음");
        var capsule = player.GetComponent<CapsuleCollider>();
        float footY = capsule != null ? capsule.center.y - capsule.height * 0.5f : -0.725f;   // 캡슐 바닥 = 발바닥

        // 1) 예전 PL_Visual 지우고 새로 만들기
        var old = player.transform.Find(VisualName);
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

        var visual = new GameObject(VisualName).transform;
        visual.SetParent(player.transform, false);
        visual.localPosition = new Vector3(0f, footY, 0f);
        visual.SetSiblingIndex(0);   // PegDollAnimator 기본값(첫 자식)과도 맞게

        AddModel(PlModel, "CH_PL", visual, outfit.bodyMats, $"{label} 몸({outfit.name})");
        if (outfit.gear) AddModel(PlGear, "CH_PL_FieldGear", visual, GearMats, $"{label} 현장 장비");

        // 2) 예전 몸 끄기
        var oldVisual = player.transform.Find(OldVisualName);
        if (oldVisual != null && oldVisual.gameObject.activeSelf)
        {
            oldVisual.gameObject.SetActive(false);
            Log($"{label}: 예전 몸(Visual) 꺼 둠 — 켜면 예전 모습");
        }

        // 3) 걷는 모습
        if (!player.TryGetComponent(out PegDollAnimator anim)) anim = player.AddComponent<PegDollAnimator>();   // 에디터의 GetComponent는 가짜 null을 돌려줘 ??가 안 먹음
        var aso = new SerializedObject(anim);
        aso.FindProperty("visual").objectReferenceValue = visual;
        aso.FindProperty("fullSpeed").floatValue = WalkFullSpeed;
        aso.FindProperty("strideLength").floatValue = WalkStride;
        aso.ApplyModifiedPropertiesWithoutUndo();

        // 4) 1인칭 그림자만 / 리플레이 보임
        if (!player.TryGetComponent(out PlayerBodyView view)) view = player.AddComponent<PlayerBodyView>();
        var vso = new SerializedObject(view);
        vso.FindProperty("body").objectReferenceValue = visual;
        vso.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception($"{label}: 씬 저장 실패");
        Log($"{label}: 완료 — {outfit.name}, PL_Visual 발바닥 y {footY:F3}, 걸음 {WalkFullSpeed}m/s·보폭 {WalkStride}m");
    }

    static GameObject FindPlayer(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.CompareTag("Player")) return t.gameObject;
        return null;
    }

    static void AddModel(string path, string name, Transform parent, string[] mats, string label)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) { Warn($"{label}: 모델 없음 {path}"); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        go.name = name;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        // 콜라이더가 있으면 잡기 조준·빈대 시야 레이에 걸림 → 알림만 (FBX 설정 addColliders 0이라 정상이면 없음)
        int cols = go.GetComponentsInChildren<Collider>(true).Length;
        if (cols > 0) Warn($"{label}: 콜라이더 {cols}개가 있음 — FBX 가져오기 설정 Generate Colliders를 끌 것");
        SetMats(go.GetComponentInChildren<Renderer>(), mats, label);
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

    static void Log(string m) => Debug.Log($"[플 모델] {m}");
    static void Warn(string m) => Debug.LogWarning($"[플 모델] {m}");
}
