using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 맵 인트로 UI 프리팹 두 개를 만드는 에디터 도구 (2026-10-23).
/// - Assets/Prefabs/UI/RadioBox.prefab : 한 주임 무전 박스 (공용 — 인트로·게임 도중 이벤트)
/// - Assets/Prefabs/UI/MapIntroOverlay.prefab : 위아래 띠·작전 카드·위쪽 띠 글자·모서리·도장·건너뛰기
/// 없을 때만 만든다 → 만든 뒤 손으로 다듬어도 배치 도구가 덮어쓰지 않는다 (SubtitleBoxFactory와 같은 원칙)
/// 처음 모양으로: Tools > 인트로 > UI 프리팹 다시 만들기
/// 크기는 Canvas 기준 해상도 1920×1080 (시안 1280×720의 1.5배)
/// </summary>
public static class MapIntroFactory
{
    public const string RadioPath = "Assets/Prefabs/UI/RadioBox.prefab";
    public const string OverlayPath = "Assets/Prefabs/UI/MapIntroOverlay.prefab";
    const string TitleFontPath = "Assets/TextMesh Pro/Fonts/Cafe24PROSlimMax SDF.asset";

    static readonly Color Accent = Hex("#F2B544");
    static readonly Color Yellow = Hex("#FFD54A");
    static readonly Color Ink = Hex("#0E111A");
    static readonly Color Paper = Hex("#F4F4F0");
    static readonly Color Muted = Hex("#AEB5C6");
    static readonly Color Line = Hex("#3A4357");
    static readonly Color StampRed = Hex("#C8372D");

    public static GameObject EnsureRadio() => AssetDatabase.LoadAssetAtPath<GameObject>(RadioPath) ?? CreateRadio();
    public static GameObject EnsureOverlay() => AssetDatabase.LoadAssetAtPath<GameObject>(OverlayPath) ?? CreateOverlay();

    [MenuItem("Tools/인트로/UI 프리팹 다시 만들기")]
    static void RecreateMenu()
    {
        if (!EditorUtility.DisplayDialog("맵 인트로 UI", "무전 박스·인트로 화면 프리팹을 처음 모양으로 다시 만듭니다. 손으로 고친 내용은 사라집니다.", "다시 만들기", "취소"))
            return;
        CreateRadio();
        Selection.activeObject = CreateOverlay();
    }

    // ───────────────────────── 무전 박스 ─────────────────────────
    static GameObject CreateRadio()
    {
        EnsureFolder();
        var body = SubtitleBoxFactory.LoadFont();
        var title = LoadTitleFont();

        var root = new GameObject("RadioBox", typeof(RectTransform), typeof(CanvasGroup));
        try
        {
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(108f, 96f);   // 아래 띠(66) 위, 왼쪽
            rt.sizeDelta = new Vector2(750f, 150f);
            var group = root.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false;

            // 왼쪽: 무전기 화면
            var frame = Box(root.transform, "PortraitFrame", Hex("#1A1F2B"));
            Place(frame, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(150f, 0f));
            AddBorder(frame);
            var channel = Box(frame, "Channel", Hex("#2A2416"));
            Place(channel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(-16f, 20f));
            var ch = Text(channel, "Label", body, 14f, Accent, "CH 03");
            Fill(ch, new Vector2(8f, 0f), new Vector2(-24f, 0f));
            ch.alignment = TextAlignmentOptions.MidlineLeft;
            var rx = Box(channel, "RxDot", Hex("#FF6B5A"));
            Place(rx, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(9f, 9f));

            var mask = new GameObject("PortraitMask", typeof(RectTransform), typeof(RectMask2D));
            mask.transform.SetParent(frame, false);
            Place(mask.transform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), new Vector2(-16f, -40f));
            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            portraitGo.transform.SetParent(mask.transform, false);
            Fill(portraitGo.transform, Vector2.zero, Vector2.zero);
            var portrait = portraitGo.GetComponent<RawImage>();
            portrait.color = Color.white; portrait.raycastTarget = false;

            // 오른쪽: 대사 판
            var panel = Box(root.transform, "Panel", new Color(0.055f, 0.067f, 0.10f, 0.86f));
            Place(panel, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(75f, 0f), new Vector2(-150f, 0f));
            AddBorder(panel);

            var speaker = Text(panel, "Speaker", title, 28f, Yellow, "한 주임");
            Place(speaker.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -12f), new Vector2(160f, 36f));
            speaker.alignment = TextAlignmentOptions.MidlineLeft;

            var tag = Box(panel, "RadioTag", Accent);
            Place(tag, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(130f, -20f), new Vector2(48f, 20f));
            var tagText = Text(tag, "Label", body, 14f, Ink, "무전");
            Fill(tagText, Vector2.zero, Vector2.zero);
            tagText.alignment = TextAlignmentOptions.Center;

            var counter = Text(panel, "Counter", body, 16f, Hex("#6F7890"), "1 / 5");
            Place(counter.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -16f), new Vector2(100f, 26f));
            counter.alignment = TextAlignmentOptions.MidlineRight;

            var line = Text(panel, "Body", body, 27f, Paper, "");
            Fill(line, new Vector2(24f, 18f), new Vector2(-24f, -54f));
            line.alignment = TextAlignmentOptions.TopLeft;
            line.textWrappingMode = TextWrappingModes.Normal;
            line.lineSpacing = 6f;

            var barBg = Box(panel, "ProgressBg", Hex("#262C3A"));
            Place(barBg, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 5f));
            var fill = Box(barBg, "ProgressFill", Accent);
            Fill(fill, Vector2.zero, Vector2.zero);
            ((RectTransform)fill).pivot = new Vector2(0f, 0.5f);

            var box = root.AddComponent<RadioBox>();
            var so = new SerializedObject(box);
            so.FindProperty("group").objectReferenceValue = group;
            so.FindProperty("speakerText").objectReferenceValue = speaker;
            so.FindProperty("bodyText").objectReferenceValue = line;
            so.FindProperty("counterText").objectReferenceValue = counter;
            so.FindProperty("portraitImage").objectReferenceValue = portrait;
            so.FindProperty("progressFill").objectReferenceValue = (RectTransform)fill;
            so.FindProperty("rxDot").objectReferenceValue = rx.GetComponent<Image>();
            so.ApplyModifiedPropertiesWithoutUndo();
            VoiceSetupTool.AttachVoice(root, box); // 블라블라 목소리 (프리팹을 다시 만들어도 빠지지 않게)

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RadioPath);
            Debug.Log($"[인트로] 무전 박스 프리팹 만듦: {RadioPath}");
            return prefab;
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ───────────────────────── 인트로 화면 ─────────────────────────
    static GameObject CreateOverlay()
    {
        EnsureFolder();
        var body = SubtitleBoxFactory.LoadFont();
        var title = LoadTitleFont();

        var root = new GameObject("MapIntroOverlay", typeof(RectTransform), typeof(CanvasGroup));
        try
        {
            Fill(root.transform, Vector2.zero, Vector2.zero);
            var rootGroup = root.GetComponent<CanvasGroup>();
            rootGroup.blocksRaycasts = false; rootGroup.interactable = false;

            // 정찰 화면 모서리 (띠 아래쪽 안쪽)
            var corners = Group(root.transform, "Corners");
            Fill(corners.transform, Vector2.zero, Vector2.zero);
            Corner(corners.transform, new Vector2(0f, 1f), new Vector2(36f, -96f), 1f, -1f);
            Corner(corners.transform, new Vector2(1f, 1f), new Vector2(-36f, -96f), -1f, -1f);
            Corner(corners.transform, new Vector2(0f, 0f), new Vector2(36f, 96f), 1f, 1f);
            Corner(corners.transform, new Vector2(1f, 0f), new Vector2(-36f, 96f), -1f, 1f);

            // 작전 카드
            var card = Group(root.transform, "Card");
            var cardRt = (RectTransform)card.transform;
            Place(cardRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(144f, -40f), new Vector2(660f, 340f));
            var cardBg = Box(card.transform, "Bg", new Color(0.04f, 0.047f, 0.07f, 0.72f));
            Fill(cardBg, Vector2.zero, Vector2.zero);
            var bar = Box(card.transform, "AccentBar", Accent);
            Place(bar, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(9f, 0f));

            var clock = Text(card.transform, "Clock", title, 92f, Color.white, "23:47");
            Place(clock.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(45f, -24f), new Vector2(260f, 100f));
            clock.alignment = TextAlignmentOptions.BottomLeft;
            var code = Text(card.transform, "Code", body, 22f, Accent, "OPERATION AREA 01");
            Place(code.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(300f, -86f), new Vector2(340f, 32f));
            code.alignment = TextAlignmentOptions.BottomLeft;
            code.characterSpacing = 8f;
            var label = Text(card.transform, "Label", body, 20f, Muted, "작전지역");
            Place(label.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(45f, -134f), new Vector2(400f, 28f));
            label.characterSpacing = 6f;
            var name = Text(card.transform, "Name", title, 64f, Color.white, "인형 마니아의 집");
            Place(name.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(45f, -162f), new Vector2(600f, 74f));
            var detail = Text(card.transform, "Detail", body, 24f, Hex("#D9DCE4"), "주택 2층 · 수집 인형 약 300점");
            Place(detail.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(45f, -238f), new Vector2(600f, 32f));

            var tags = new GameObject("Complaints", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            tags.transform.SetParent(card.transform, false);
            Place(tags.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(45f, -284f), new Vector2(600f, 40f));
            var hl = tags.GetComponent<HorizontalLayoutGroup>();
            hl.spacing = 14f; hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            hl.childAlignment = TextAnchor.UpperLeft;
            var tag1 = ComplaintTag(tags.transform, "Complaint1", body, "민원 1 · 밤마다 웃음소리");
            var tag2 = ComplaintTag(tags.transform, "Complaint2", body, "민원 2 · 인형 자리가 바뀜");
            var tag3 = ComplaintTag(tags.transform, "Complaint3", body, "");

            // 위아래 띠 (카드·모서리 위에 그림)
            var top = Box(root.transform, "LetterTop", Hex("#07080C"));
            Place(top, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 66f));
            var bottom = Box(root.transform, "LetterBottom", Hex("#07080C"));
            Place(bottom, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 66f));

            // 위쪽 띠 글자
            var band = Group(root.transform, "Band");
            Place(band.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 66f));
            var recDot = Box(band.transform, "RecDot", Hex("#FF6B5A"));
            Place(recDot, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(48f, 0f), new Vector2(12f, 12f));
            var rec = Text(band.transform, "Rec", body, 21f, Hex("#FF6B5A"), "REC");
            Place(rec.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(70f, 0f), new Vector2(60f, 30f));
            rec.alignment = TextAlignmentOptions.MidlineLeft;
            var bandClock = Text(band.transform, "Clock", body, 21f, Paper, "23:47:00");
            Place(bandClock.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(134f, 0f), new Vector2(140f, 30f));
            bandClock.alignment = TextAlignmentOptions.MidlineLeft;
            var bandArea = Text(band.transform, "Area", body, 21f, Hex("#D9DCE4"), "AREA 01  인형 마니아의 집");
            Place(bandArea.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-48f, 0f), new Vector2(600f, 30f));
            bandArea.alignment = TextAlignmentOptions.MidlineRight;
            bandArea.richText = true;

            // 도장
            var stamp = Group(root.transform, "Stamp");
            var stampRt = (RectTransform)stamp.transform;
            Place(stampRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 190f), new Vector2(520f, 170f));
            stampRt.localRotation = Quaternion.Euler(0f, 0f, 4f);
            var plate = Box(stamp.transform, "Plate", new Color(1f, 1f, 1f, 0.78f));
            Place(plate, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(460f, 112f));
            Frame(plate, StampRed, 6f);
            var stampText = Text(plate, "Text", title, 86f, StampRed, "작전 개시");
            Fill(stampText, Vector2.zero, Vector2.zero);
            stampText.alignment = TextAlignmentOptions.Center;
            var subPlate = Box(stamp.transform, "SubPlate", new Color(1f, 1f, 1f, 0.78f));
            Place(subPlate, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(320f, 38f));
            var stampSub = Text(subPlate, "Text", body, 22f, StampRed, "포획 대상 빈대 3마리");
            Fill(stampSub, Vector2.zero, Vector2.zero);
            stampSub.alignment = TextAlignmentOptions.Center;
            stampSub.fontStyle = FontStyles.Bold;

            // 건너뛰기 (아래 띠 안 오른쪽)
            var skip = Group(root.transform, "Skip");
            Place(skip.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0.5f), new Vector2(-48f, 33f), new Vector2(460f, 30f));
            var skipText = Text(skip.transform, "Text", body, 19f, Hex("#8A93A8"), "SPACE 길게 눌러 건너뛰기");
            Place(skipText.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(300f, 30f));
            skipText.alignment = TextAlignmentOptions.MidlineRight;
            var skipBg = Box(skip.transform, "BarBg", Hex("#262C3A"));
            Place(skipBg, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-316f, 0f), new Vector2(120f, 5f));
            var skipFill = Box(skipBg, "Fill", Accent);
            Fill(skipFill, Vector2.zero, Vector2.zero);
            ((RectTransform)skipFill).pivot = new Vector2(0f, 0.5f);

            var ov = root.AddComponent<MapIntroOverlay>();
            var so = new SerializedObject(ov);
            so.FindProperty("root").objectReferenceValue = rootGroup;
            so.FindProperty("letterTop").objectReferenceValue = (RectTransform)top;
            so.FindProperty("letterBottom").objectReferenceValue = (RectTransform)bottom;
            so.FindProperty("band").objectReferenceValue = band;
            so.FindProperty("bandClock").objectReferenceValue = bandClock;
            so.FindProperty("bandArea").objectReferenceValue = bandArea;
            so.FindProperty("corners").objectReferenceValue = corners;
            so.FindProperty("card").objectReferenceValue = card;
            so.FindProperty("cardRect").objectReferenceValue = cardRt;
            so.FindProperty("cardClock").objectReferenceValue = clock;
            so.FindProperty("cardCode").objectReferenceValue = code;
            so.FindProperty("cardLabel").objectReferenceValue = label;
            so.FindProperty("cardName").objectReferenceValue = name;
            so.FindProperty("cardDetail").objectReferenceValue = detail;
            var arr = so.FindProperty("complaintTags");
            arr.arraySize = 3;
            arr.GetArrayElementAtIndex(0).objectReferenceValue = tag1;
            arr.GetArrayElementAtIndex(1).objectReferenceValue = tag2;
            arr.GetArrayElementAtIndex(2).objectReferenceValue = tag3;
            so.FindProperty("stamp").objectReferenceValue = stamp;
            so.FindProperty("stampRect").objectReferenceValue = stampRt;
            so.FindProperty("stampText").objectReferenceValue = stampText;
            so.FindProperty("stampSub").objectReferenceValue = stampSub;
            so.FindProperty("skip").objectReferenceValue = skip;
            so.FindProperty("skipFill").objectReferenceValue = (RectTransform)skipFill;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 처음 상태: 카드만 보이고 나머지는 숨김 (실행 시 MapIntro가 정함)
            band.alpha = 0f; corners.alpha = 0f; stamp.alpha = 0f; skip.alpha = 0f;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, OverlayPath);
            Debug.Log($"[인트로] 인트로 화면 프리팹 만듦: {OverlayPath}");
            return prefab;
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ───────────────────────── 만들기 도우미 ─────────────────────────
    static GameObject ComplaintTag(Transform parent, string name, TMP_FontAsset font, string text)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(HorizontalLayoutGroup));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>(); img.color = Paper; img.raycastTarget = false;
        var hl = go.GetComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(14, 14, 6, 6); hl.childControlWidth = true; hl.childControlHeight = true;
        hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
        var t = Text(go.transform, "Text", font, 22f, Ink, text);
        t.fontStyle = FontStyles.Bold;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return go;
    }

    static void Corner(Transform parent, Vector2 anchor, Vector2 pos, float sx, float sy)
    {
        var a = Box(parent, "CornerH", Color.white);
        Place(a, anchor, anchor, new Vector2(sx > 0 ? 0f : 1f, sy > 0 ? 0f : 1f), pos, new Vector2(54f, 4f));
        var b = Box(parent, "CornerV", Color.white);
        Place(b, anchor, anchor, new Vector2(sx > 0 ? 0f : 1f, sy > 0 ? 0f : 1f), pos, new Vector2(4f, 54f));
        a.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.85f);
        b.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.85f);
    }

    static void Frame(Transform parent, Color c, float w)
    {
        var l = Box(parent, "FrameL", c); Place(l, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(w, 0f));
        var r = Box(parent, "FrameR", c); Place(r, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(w, 0f));
        var t = Box(parent, "FrameT", c); Place(t, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, w));
        var b = Box(parent, "FrameB", c); Place(b, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, w));
    }

    static void AddBorder(Transform t)
    {
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = Line; o.effectDistance = new Vector2(2f, -2f);
    }

    static CanvasGroup Group(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);
        var g = go.GetComponent<CanvasGroup>(); g.blocksRaycasts = false; g.interactable = false;
        return g;
    }

    static Transform Box(Transform parent, string name, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>(); img.color = c; img.raycastTarget = false;
        return go.transform;
    }

    static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, float size, Color c, string text)
    {
        var t = SubtitleBoxFactory.MakeText(parent, name, font, size, c);
        t.text = text;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.alignment = TextAlignmentOptions.TopLeft;
        return t;
    }

    static void Place(Component c, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size) => Place(c.transform, aMin, aMax, pivot, pos, size);
    static void Place(Transform t, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var rt = (RectTransform)t;
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    static void Fill(Component c, Vector2 offMin, Vector2 offMax) => Fill(c.transform, offMin, offMax);
    static void Fill(Transform t, Vector2 offMin, Vector2 offMax)
    {
        var rt = (RectTransform)t;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = offMin; rt.offsetMax = offMax;
    }

    static TMP_FontAsset LoadTitleFont()
    {
        var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);
        if (f == null) { Debug.LogWarning($"[인트로] 제목 글꼴 없음: {TitleFontPath} → 본문 글꼴 사용"); f = SubtitleBoxFactory.LoadFont(); }
        return f;
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI")) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
    }

    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
}
