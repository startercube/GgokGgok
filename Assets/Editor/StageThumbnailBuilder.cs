using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 스테이지 선택 카드 썸네일을 지금 맵으로 다시 찍고, 맨 앞에 튜토리얼 카드를 만든다 (2026-10-22).
/// 설계: 프로젝트 문서 「코드 설계 - 메인화면」 9장
/// 메뉴: Tools > 메인화면 > 스테이지 썸네일 찍기 / CLI: -executeMethod StageThumbnailBuilder.ApplyBatch
/// - 구도: 방 전체를 위에서 내려다봄 (사용자 결정). 바닥에서 3m 넘게 떠 있는 것(천장·형광등)은 찍는 동안만 숨김
/// - 찍은 씬은 저장하지 않고 닫는다 → 원본 씬은 그대로
/// - 맵을 고친 뒤엔 이 메뉴만 다시 실행하면 썸네일이 따라온다
/// </summary>
public static class StageThumbnailBuilder
{
    const string MainScenePath = "Assets/Scenes/MainScene.unity";
    const string OutDir = "Assets/Scenes/MainScene/Thumbnails";
    const string TutorialCardName = "Stage0Btn_Tutorial";
    const string TutorialCardTemplate = "Stage1Btn";
    const string TutorialLabel = "튜토리얼\n흰 훈련장";
    const int Width = 1400, Height = 800;        // 카드 이미지 700×400의 2배 (같은 7:4)
    const float Fov = 35f;                       // 좁게 → 원근 왜곡이 적어 방 배치가 잘 읽힘
    const float Margin = 0.04f;                  // 화면 가장자리 여백 (뷰포트 비율)
    const float CeilingCut = 3.0f;               // 바닥에서 이만큼 넘게 떠 있으면 숨김 (벽은 바닥에서 시작해 남음)
    static readonly Color Background = new Color(0.12f, 0.12f, 0.14f);

    struct Shot
    {
        public string scene, card;
        public float yaw, pitch, brighten;
        public Shot(string scene, string card, float yaw = 0f, float pitch = 55f, float brighten = 1f)
        { this.scene = scene; this.card = card; this.yaw = yaw; this.pitch = pitch; this.brighten = brighten; }
    }

    // yaw 0 = 남쪽에서 북쪽(+Z)을 봄. pitch = 내려다보는 각도. brighten = 환경광 배율 (어두운 맵용)
    static readonly Shot[] Shots =
    {
        new Shot("Tutorial", TutorialCardName),
        new Shot("DollManiaHouse", "Stage1Btn"),
        new Shot("HalloweenNight", "Stage3Btn"),
    };

    [MenuItem("Tools/메인화면/스테이지 썸네일 찍기")]
    public static void ApplyMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Apply();
    }

    /// <summary>CLI 전용</summary>
    public static void ApplyBatch()
    {
        try { Apply(); }
        catch (Exception e) { Debug.LogError($"[썸네일] 실패: {e}"); EditorApplication.Exit(1); }
    }

    static void Apply()
    {
        EnsureFolder(OutDir);
        var sprites = new Dictionary<string, Sprite>();
        foreach (var s in Shots)
        {
            string path = $"{OutDir}/T_Thumb_{s.scene}.png";
            if (Capture(s, path)) sprites[s.card] = ImportSprite(path);
        }

        var main = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        ApplyToMenu(main, sprites);
        EditorSceneManager.MarkSceneDirty(main);
        if (!EditorSceneManager.SaveScene(main)) throw new Exception("MainScene 저장 실패");
        AssetDatabase.SaveAssets();
        Log("완료");
    }

    // ===================== 촬영 =====================

    static bool Capture(Shot s, string path)
    {
        string scenePath = $"Assets/Scenes/{s.scene}.unity";
        if (!File.Exists(scenePath)) { Warn($"{scenePath} 없음 → 건너뜀"); return false; }
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        // 찍을 렌더러: 켜져 있고, UI·플레이어가 아닌 것
        var all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(r => r.enabled && r.gameObject.scene == scene && !(r is ParticleSystemRenderer))
            .ToList();
        int hiddenPlayer = 0;
        foreach (var r in all.ToList())
        {
            if (r.gameObject.layer == 5 || IsUnderTag(r.transform, "Player")) { r.enabled = false; all.Remove(r); hiddenPlayer++; }
        }
        if (all.Count == 0) { Warn($"{s.scene}: 렌더러가 없음"); return false; }

        // 방 범위: 중심점 분위(2~98%)로 바깥 장식·지면 판 같은 이상값을 버린다
        float x0 = Quantile(all.Select(r => r.bounds.center.x), 0.02f), x1 = Quantile(all.Select(r => r.bounds.center.x), 0.98f);
        float z0 = Quantile(all.Select(r => r.bounds.center.z), 0.02f), z1 = Quantile(all.Select(r => r.bounds.center.z), 0.98f);
        float floor = Quantile(all.Select(r => r.bounds.min.y), 0.05f);

        // 천장 숨기기 (찍는 동안만, 씬은 저장 안 함)
        int hiddenCeil = 0;
        foreach (var r in all)
            if (r.bounds.min.y > floor + CeilingCut) { r.enabled = false; hiddenCeil++; }
        float top = Quantile(all.Where(r => r.enabled).Select(r => r.bounds.max.y), 0.98f);
        top = Mathf.Clamp(top, floor + 1f, floor + 5f);

        var box = new Bounds();
        box.SetMinMax(new Vector3(x0, floor, z0), new Vector3(x1, top, z1));

        if (!Mathf.Approximately(s.brighten, 1f))
        {
            RenderSettings.ambientLight *= s.brighten;
            RenderSettings.ambientIntensity *= s.brighten;
        }

        var go = new GameObject("__ThumbnailCamera");
        SceneManager.MoveGameObjectToScene(go, scene);
        var rt = new RenderTexture(Width, Height, 24);
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = Fov;
            cam.aspect = (float)Width / Height;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 500f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Background;
            cam.targetTexture = rt;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            Vector3 dir = Quaternion.Euler(s.pitch, s.yaw, 0f) * Vector3.forward;
            float d = FitDistance(cam, box, dir);
            cam.transform.SetPositionAndRotation(box.center - dir * d, Quaternion.LookRotation(dir));

            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Log($"{s.scene}: {path} (범위 {x1 - x0:0.#}×{z1 - z0:0.#}m, 거리 {d:0.#}m, 천장 등 {hiddenCeil}개·플레이어/UI {hiddenPlayer}개 숨김)");
            return true;
        }
        finally
        {
            Object.DestroyImmediate(go);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }

    // 상자 꼭짓점 8개가 모두 여백 안에 들어오는 가장 가까운 거리 (멀수록 작게 보이므로 이분 탐색)
    static float FitDistance(Camera cam, Bounds b, Vector3 dir)
    {
        var corners = new List<Vector3>();
        for (int i = 0; i < 8; i++)
            corners.Add(new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y, i / 4 == 0 ? b.min.z : b.max.z));
        float lo = 1f, hi = 400f;
        for (int it = 0; it < 40; it++)
        {
            float mid = (lo + hi) * 0.5f;
            cam.transform.SetPositionAndRotation(b.center - dir * mid, Quaternion.LookRotation(dir));
            bool fits = corners.All(c =>
            {
                var v = cam.WorldToViewportPoint(c);
                return v.z > cam.nearClipPlane && v.x >= Margin && v.x <= 1f - Margin && v.y >= Margin && v.y <= 1f - Margin;
            });
            if (fits) hi = mid; else lo = mid;
        }
        return hi;
    }

    static Sprite ImportSprite(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = false;
        ti.maxTextureSize = 2048;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ===================== 메인화면 카드 =====================

    static void ApplyToMenu(Scene main, Dictionary<string, Sprite> sprites)
    {
        var panel = main.GetRootGameObjects().Select(g => FindDeep(g.transform, "StageSelectPanel")).FirstOrDefault(t => t != null);
        var content = panel != null ? FindDeep(panel, "ContentBox") : null;
        if (content == null) { Warn("StageSelectPanel/ContentBox가 없어 카드를 고치지 못함"); return; }

        EnsureTutorialCard(main, content);

        foreach (var kv in sprites)
        {
            var card = content.Find(kv.Key);
            var img = card != null ? card.Find("Image")?.GetComponent<Image>() : null;
            if (img == null) { Warn($"카드 {kv.Key}/Image가 없음"); continue; }
            img.sprite = kv.Value;
            img.color = Color.white;
            EditorUtility.SetDirty(img);
            Log($"카드 {kv.Key} 썸네일 → {AssetDatabase.GetAssetPath(kv.Value)}");
        }
    }

    // 맨 앞 튜토리얼 카드: 기존 카드(Stage1Btn)를 복제해 모양을 그대로 따른다. 이미 있으면 연결·글자만 다시 맞춤 (순서는 손대지 않음)
    static void EnsureTutorialCard(Scene main, Transform content)
    {
        var card = content.Find(TutorialCardName);
        if (card == null)
        {
            var template = content.Find(TutorialCardTemplate);
            if (template == null) { Warn($"복제할 카드 {TutorialCardTemplate}가 없어 튜토리얼 카드를 못 만듦"); return; }
            var go = Object.Instantiate(template.gameObject, content);
            go.name = TutorialCardName;
            card = go.transform;
            card.SetSiblingIndex(0);   // Carousel은 첫 자식을 정면에 둔다
            Log("튜토리얼 카드 새로 만듦 (스테이지 카드 맨 앞)");
        }

        var label = card.GetComponentInChildren<TMP_Text>(true);
        if (label != null) { label.text = TutorialLabel; EditorUtility.SetDirty(label); }

        var btn = card.GetComponent<Button>();
        var st = main.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<StageTransition>(true)).FirstOrDefault();
        if (btn == null || st == null) { Warn("튜토리얼 카드 버튼 또는 StageTransition이 없어 연결하지 못함"); return; }
        while (btn.onClick.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(btn.onClick, 0);
        UnityEventTools.AddStringPersistentListener(btn.onClick, st.LoadScene, "Tutorial");
        EditorUtility.SetDirty(btn);
    }

    // ===================== 공용 =====================

    static float Quantile(IEnumerable<float> values, float q)
    {
        var a = values.OrderBy(v => v).ToArray();
        if (a.Length == 0) return 0f;
        float idx = Mathf.Clamp01(q) * (a.Length - 1);
        int i = Mathf.FloorToInt(idx);
        int j = Mathf.Min(i + 1, a.Length - 1);
        return Mathf.Lerp(a[i], a[j], idx - i);
    }

    static bool IsUnderTag(Transform t, string tag)
    {
        for (; t != null; t = t.parent) if (t.CompareTag(tag)) return true;
        return false;
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

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static void Log(string m) => Debug.Log($"[썸네일] {m}");
    static void Warn(string m) => Debug.LogWarning($"[썸네일] {m}");
}
