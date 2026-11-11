using TMPro;
using UnityEngine;

// 맵 인트로 화면 요소 (2026-10-23) — 표시만 한다. 언제 무엇을 보여 줄지는 MapIntro가 시간 하나로 정한다.
// 위아래 얇은 띠 / 작전 카드(한 글자씩) / 카드가 접힌 뒤 위쪽 띠의 REC·시각·지역 / 정찰 화면 모서리 / 작전 개시 도장 / 건너뛰기 안내
// 프리팹: Assets/Prefabs/UI/MapIntroOverlay.prefab (MapIntroFactory가 없을 때만 만듦 → 손으로 다듬어도 유지)
public class MapIntroOverlay : MonoBehaviour
{
    [SerializeField] private CanvasGroup root;

    [Header("위아래 띠")]
    [SerializeField] private RectTransform letterTop;
    [SerializeField] private RectTransform letterBottom;
    [SerializeField] private float letterHeight = 66f;

    [Header("위쪽 띠 글자 (카드가 접힌 뒤)")]
    [SerializeField] private CanvasGroup band;
    [SerializeField] private TMP_Text bandClock;
    [SerializeField] private TMP_Text bandArea;
    [SerializeField] private CanvasGroup corners;

    [Header("작전 카드")]
    [SerializeField] private CanvasGroup card;
    [SerializeField] private RectTransform cardRect;
    [SerializeField] private TMP_Text cardClock;
    [SerializeField] private TMP_Text cardCode;
    [SerializeField] private TMP_Text cardLabel;
    [SerializeField] private TMP_Text cardName;
    [SerializeField] private TMP_Text cardDetail;
    [Tooltip("민원 태그 (배경 포함 묶음). 글자는 자식 TMP")]
    [SerializeField] private GameObject[] complaintTags;

    [Header("도장")]
    [SerializeField] private CanvasGroup stamp;
    [SerializeField] private RectTransform stampRect;
    [SerializeField] private TMP_Text stampText;
    [SerializeField] private TMP_Text stampSub;
    [Tooltip("도장 전체 배율 (판·글자·부제 함께). 2026-10-24 사용자 요청으로 2배")]
    [SerializeField, Min(0.1f)] private float stampSize = 2f;

    [Header("건너뛰기")]
    [SerializeField] private CanvasGroup skip;
    [SerializeField] private RectTransform skipFill;

    private TMP_Text[] typed;        // 한 글자씩 드러낼 순서
    private GameObject[] typedReveal; // 그 글자가 처음 보일 때 함께 켤 묶음 (민원 태그 배경)
    private int totalChars;
    private Vector2 cardBasePos;

    public int TotalChars => totalChars;

    void Awake()
    {
        if (root != null) { root.alpha = 0f; root.blocksRaycasts = false; root.interactable = false; }
        if (cardRect != null) cardBasePos = cardRect.anchoredPosition;
    }

    public void Setup(MapIntroScript s, int dollCount)
    {
        if (s == null) return;
        Set(cardClock, s.useRealTime ? System.DateTime.Now.ToString("HH:mm") : s.clock);
        Set(cardCode, s.areaCode);
        Set(cardLabel, s.areaLabel);
        Set(cardName, s.areaName);
        Set(cardDetail, s.areaDetail);
        Set(bandArea, string.IsNullOrEmpty(s.bandAreaCode) ? s.areaName : $"<color=#F2B544>{s.bandAreaCode}</color>  {s.areaName}");
        Set(stampText, s.stampText);
        SetDollCount(s, dollCount);

        // 한 글자씩: 시각 → 코드 → 라벨 → 이름 → 설명 → 민원들
        var list = new System.Collections.Generic.List<TMP_Text>();
        var reveal = new System.Collections.Generic.List<GameObject>();
        void Add(TMP_Text t, GameObject r) { if (t != null) { list.Add(t); reveal.Add(r); } }
        Add(cardClock, null); Add(cardCode, null); Add(cardLabel, null); Add(cardName, null); Add(cardDetail, null);
        int tags = complaintTags != null ? complaintTags.Length : 0;
        for (int i = 0; i < tags; i++)
        {
            var tag = complaintTags[i];
            if (tag == null) continue;
            bool has = s.complaints != null && i < s.complaints.Length && !string.IsNullOrEmpty(s.complaints[i]);
            tag.SetActive(false);
            if (!has) continue;
            var t = tag.GetComponentInChildren<TMP_Text>(true);
            if (t == null) continue;
            t.text = s.complaints[i];
            Add(t, tag);
        }
        typed = list.ToArray();
        typedReveal = reveal.ToArray();
        totalChars = 0;
        foreach (var t in typed) { t.ForceMeshUpdate(); totalChars += t.textInfo.characterCount; }
        SetTyped(0);
    }

    /// <summary>도장 아래 "포획 대상 빈대 N마리" (N이 0이면 숨김)</summary>
    public void SetDollCount(MapIntroScript s, int dollCount)
    {
        if (stampSub == null || s == null) return;
        bool show = !string.IsNullOrEmpty(s.stampSubFormat) && dollCount > 0;
        stampSub.gameObject.SetActive(show);
        if (show) stampSub.text = string.Format(s.stampSubFormat, dollCount);
    }

    static void Set(TMP_Text t, string v) { if (t != null) t.text = v ?? ""; }

    public void SetVisible(bool on)
    {
        if (root != null) root.alpha = on ? 1f : 0f;
    }

    /// <summary>앞에서부터 n글자만 보이게</summary>
    public void SetTyped(int n)
    {
        if (typed == null) return;
        int left = Mathf.Max(0, n);
        for (int i = 0; i < typed.Length; i++)
        {
            int count = typed[i].textInfo != null ? typed[i].textInfo.characterCount : typed[i].text.Length;
            int show = Mathf.Min(left, count);
            typed[i].maxVisibleCharacters = show;
            if (typedReveal[i] != null) typedReveal[i].SetActive(show > 0);
            left -= show;
        }
    }

    /// <summary>0 = 카드, 1 = 위쪽 띠로 접힘</summary>
    public void SetFold(float k)
    {
        k = Mathf.Clamp01(k);
        if (card != null) card.alpha = 1f - k;
        if (cardRect != null) cardRect.anchoredPosition = cardBasePos + new Vector2(0f, 60f * k * k);
        if (band != null) band.alpha = k;
        if (corners != null) corners.alpha = k;
    }

    /// <summary>위아래 띠 (0 = 없음, 1 = 다 내려옴). 위쪽 띠 글자도 같이 사라진다</summary>
    public void SetLetterbox(float k)
    {
        k = Mathf.Clamp01(k);
        if (letterTop != null) letterTop.sizeDelta = new Vector2(letterTop.sizeDelta.x, letterHeight * k);
        if (letterBottom != null) letterBottom.sizeDelta = new Vector2(letterBottom.sizeDelta.x, letterHeight * k);
    }

    public void SetBandAlpha(float a) { if (band != null) band.alpha = a; if (corners != null) corners.alpha = a; }

    public void SetClock(string text) => Set(bandClock, text);

    /// <summary>도장 연출. t = 도장 시각부터 흐른 시간, total = 보여 주는 시간</summary>
    public void SetStamp(float t, float total)
    {
        if (stamp == null) return;
        if (t < 0f || t > total) { stamp.alpha = 0f; return; }
        const float punch = 0.14f, fadeOut = 0.4f;
        float scale = t < punch ? Mathf.Lerp(1.5f, 1f, t / punch) : 1f;
        float a = t < punch ? t / punch : (t > total - fadeOut ? (total - t) / fadeOut : 1f);
        stamp.alpha = Mathf.Clamp01(a);
        if (stampRect != null) stampRect.localScale = Vector3.one * (scale * stampSize);
    }

    public void SetSkip(bool visible, float fill)
    {
        if (skip == null) return;
        skip.alpha = visible ? 1f : 0f;
        if (skipFill != null) skipFill.localScale = new Vector3(Mathf.Clamp01(fill), 1f, 1f);
    }
}
