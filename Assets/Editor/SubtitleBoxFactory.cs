using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 공용 자막 박스 프리팹(Assets/Prefabs/UI/SubtitleBox.prefab)을 만드는 에디터 도구 (2026-10-21).
/// - EnsurePrefab: 프리팹이 없을 때만 만든다 → 만든 뒤 프리팹을 손으로 다듬어도 배치 도구가 덮어쓰지 않는다
/// - 메뉴 Tools > UI > 자막 박스 프리팹 다시 만들기: 처음 모양으로 되돌린다 (손으로 고친 것이 사라짐)
/// 모양: 화면 아래 가운데, 반투명 검은 박스 + 노란 화자 이름 + 흰 대사 (Maplestory 글꼴)
/// </summary>
public static class SubtitleBoxFactory
{
    public const string PrefabPath = "Assets/Prefabs/UI/SubtitleBox.prefab";
    const string FontPath = "Assets/TextMesh Pro/Fonts/Maplestory OTF Light SDF.asset";

    public static GameObject EnsurePrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        return existing != null ? existing : Create();
    }

    [MenuItem("Tools/UI/자막 박스 프리팹 다시 만들기")]
    static void RecreateMenu()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
            !EditorUtility.DisplayDialog("자막 박스", "프리팹을 처음 모양으로 다시 만듭니다. 손으로 고친 내용은 사라집니다.", "다시 만들기", "취소"))
            return;
        Selection.activeObject = Create();
    }

    static GameObject Create()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI")) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
        var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(FontPath);
        if (font == null) Debug.LogWarning($"[자막 박스] 글꼴 없음: {FontPath}");

        var panel = new GameObject("SubtitleBox", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        try
        {
            var rt = (RectTransform)panel.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 70f);
            rt.sizeDelta = new Vector2(1180f, 150f);
            var img = panel.GetComponent<Image>();
            img.color = new Color(0.06f, 0.06f, 0.07f, 0.72f);
            img.raycastTarget = false;
            panel.GetComponent<CanvasGroup>().blocksRaycasts = false;

            var speaker = MakeText(panel.transform, "Speaker", font, 30f, new Color(1f, 0.82f, 0.45f));
            var srt = (RectTransform)speaker.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 1f);
            srt.anchoredPosition = new Vector2(30f, -14f);
            srt.sizeDelta = new Vector2(400f, 40f);
            speaker.text = "";

            var body = MakeText(panel.transform, "Body", font, 40f, Color.white);
            var brt = (RectTransform)body.transform;
            brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(1f, 1f);
            brt.offsetMin = new Vector2(30f, 16f); brt.offsetMax = new Vector2(-30f, -56f);
            body.textWrappingMode = TMPro.TextWrappingModes.Normal;
            body.text = "";

            var box = panel.AddComponent<SubtitleBox>();
            var so = new SerializedObject(box);
            so.FindProperty("group").objectReferenceValue = panel.GetComponent<CanvasGroup>();
            so.FindProperty("speakerText").objectReferenceValue = speaker;
            so.FindProperty("bodyText").objectReferenceValue = body;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(panel, PrefabPath);
            Debug.Log($"[자막 박스] 프리팹 만듦: {PrefabPath}");
            return prefab;
        }
        finally { Object.DestroyImmediate(panel); }
    }

    /// <summary>UI 글자 하나 만들기 (다른 에디터 도구에서도 씀)</summary>
    public static TMPro.TextMeshProUGUI MakeText(Transform parent, string name, TMPro.TMP_FontAsset font, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TMPro.TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.raycastTarget = false;
        return t;
    }

    public static TMPro.TMP_FontAsset LoadFont() => AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(FontPath);
}
