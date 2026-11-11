using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// 아직 개발 중인 스테이지 카드 — 썸네일을 어둡게 덮고, 누르면 씬 대신 안내 문구를 띄운다
[RequireComponent(typeof(Button))]
public class ComingSoonStage : MonoBehaviour
{
    [SerializeField] private string message = "해당 스테이지는 개발 중입니다.\n업데이트를 기대해주세요.";
    [Range(0f, 1f)] [SerializeField] private float dimAlpha = 0.55f;   // 썸네일 덮개 진하기
    [Range(0f, 1f)] [SerializeField] private float grayTint = 0.6f;    // 카드 전체 색에 곱할 회색 밝기
    [SerializeField] private string badge = "준비 중";                 // 자물쇠 아래 글자 (비우면 자물쇠만)
    [SerializeField] private TMP_FontAsset badgeFont;                  // 자물쇠 아래 글자 폰트 (비우면 카드 라벨 폰트)
    [SerializeField] private float showDuration = 2f;                  // 안내가 떠 있는 시간 (초)
    [SerializeField] private float fadeDuration = 0.3f;
    [SerializeField] private AudioClip denyClip;                       // 눌렀을 때 효과음
    [SerializeField] private AudioSource audioSource;                  // 비우면 부모(캐러셀)의 AudioSource 사용

    // 카드 여러 장이 안내 하나를 같이 쓴다
    private static CanvasGroup notice;
    private static TMP_Text noticeText;
    private static ComingSoonStage owner;
    private static Coroutine hideRoutine;
    private static Sprite lockSprite;   // 코드로 그린 자물쇠 — 카드끼리 공유

    private TMP_FontAsset labelFont;    // 카드 라벨 폰트 (한글)

    private void Awake()
    {
        var button = GetComponent<Button>();
        if (audioSource == null) audioSource = GetComponentInParent<AudioSource>();

        // 인스펙터에 남은 LoadScene 연결은 끄고 안내로 바꾼다
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
        }
        button.onClick.AddListener(ShowNotice);

        // 배지를 만들기 전에 라벨 폰트를 잡아둔다 (나중엔 배지 글자가 먼저 잡힘)
        var cardLabel = GetComponentInChildren<TMP_Text>();
        if (cardLabel != null) labelFont = cardLabel.font;

        // 덮개를 만들기 전에 원래 그래픽만 회색으로 누른다
        foreach (var g in GetComponentsInChildren<Graphic>(true))
        {
            g.color = new Color(g.color.r * grayTint, g.color.g * grayTint, g.color.b * grayTint, g.color.a);
        }

        Transform thumb = transform.Find("Image");
        if (thumb == null) thumb = transform;
        AddDim(thumb);   // 썸네일

        // 이름 띠 — 배경 Image와 같은 자리에 맨 마지막 자식으로 넣어 글자까지 덮는다
        Transform label = transform.Find("GameObject");
        if (label != null) AddDim(label, label.Find("Image") as RectTransform);

        AddBadge(thumb);
    }

    private void OnDisable()
    {
        // 패널이 닫히면 안내도 바로 끈다
        if (owner == this && notice != null)
        {
            notice.alpha = 0f;
            owner = null;
        }
    }

    // match가 있으면 그 사각형과 같은 자리에, 없으면 parent를 꽉 채운다
    private void AddDim(Transform parent, RectTransform match = null)
    {
        var dim = new GameObject("ComingSoonDim", typeof(RectTransform), typeof(Image));
        var rt = dim.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        if (match != null)
        {
            rt.anchorMin = match.anchorMin;
            rt.anchorMax = match.anchorMax;
            rt.pivot = match.pivot;
            rt.anchoredPosition = match.anchoredPosition;
            rt.sizeDelta = match.sizeDelta;
        }
        else
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        var img = dim.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, dimAlpha);
        img.raycastTarget = false;   // 클릭은 카드 버튼이 받도록 통과
    }

    // 썸네일 가운데에 자물쇠와 "준비 중" 글자를 올린다
    private void AddBadge(Transform parent)
    {
        var icon = new GameObject("ComingSoonLock", typeof(RectTransform), typeof(Image));
        var irt = icon.GetComponent<RectTransform>();
        irt.SetParent(parent, false);
        irt.anchoredPosition = new Vector2(0f, 35f);
        irt.sizeDelta = new Vector2(140f, 140f);

        var img = icon.GetComponent<Image>();
        img.sprite = GetLockSprite();
        img.color = new Color(1f, 1f, 1f, 0.9f);
        img.raycastTarget = false;

        if (string.IsNullOrEmpty(badge)) return;

        var go = new GameObject("ComingSoonBadge", typeof(RectTransform), typeof(TextMeshProUGUI));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchoredPosition = new Vector2(0f, -85f);
        rt.sizeDelta = new Vector2(600f, 80f);

        var text = go.GetComponent<TextMeshProUGUI>();
        if (badgeFont != null) text.font = badgeFont;
        else if (labelFont != null) text.font = labelFont;
        text.text = badge;
        text.fontSize = 56f;
        text.color = new Color(1f, 1f, 1f, 0.85f);
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
    }

    // 몸통(둥근 사각형) + 고리(반원 띠) − 열쇠 구멍을 거리장으로 그려 가장자리를 부드럽게
    private static Sprite GetLockSprite()
    {
        if (lockSprite != null) return lockSprite;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);

                // 몸통: 가로 24~104, 세로 8~72, 모서리 반경 10
                Vector2 q = new Vector2(Mathf.Abs(p.x - 64f), Mathf.Abs(p.y - 40f)) - new Vector2(30f, 22f);
                float body = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - 10f;

                // 고리: 중심 (64,72), 반경 18~30 띠의 윗반쪽
                float shackle = Mathf.Abs((p - new Vector2(64f, 72f)).magnitude - 24f) - 6f;
                if (p.y < 72f) shackle = Mathf.Max(shackle, 72f - p.y);

                // 열쇠 구멍: 동그라미 + 아래로 뻗은 홈
                float holeCircle = (p - new Vector2(64f, 46f)).magnitude - 9f;
                float holeSlot = Mathf.Max(Mathf.Abs(p.x - 64f) - 4f, Mathf.Abs(p.y - 32f) - 12f);
                float hole = Mathf.Min(holeCircle, holeSlot);

                float d = Mathf.Max(Mathf.Min(body, shackle), -hole);
                byte a = (byte)(Mathf.Clamp01(0.5f - d) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();

        lockSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return lockSprite;
    }

    private void ShowNotice()
    {
        if (audioSource != null && denyClip != null) audioSource.PlayOneShot(denyClip);
        if (notice == null) BuildNotice();

        noticeText.text = message;
        notice.transform.SetAsLastSibling();
        notice.alpha = 1f;

        if (owner != null && hideRoutine != null) owner.StopCoroutine(hideRoutine);
        owner = this;
        hideRoutine = StartCoroutine(HideAfterDelay());
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSecondsRealtime(showDuration);

        for (float t = 0f; t < fadeDuration; t += Time.unscaledDeltaTime)
        {
            notice.alpha = 1f - t / fadeDuration;
            yield return null;
        }
        notice.alpha = 0f;
        owner = null;
    }

    // 루트 캔버스 가운데에 안내 상자를 한 번만 만든다
    private void BuildNotice()
    {
        Transform canvas = GetComponentInParent<Canvas>().rootCanvas.transform;

        var box = new GameObject("ComingSoonNotice", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        var rt = box.GetComponent<RectTransform>();
        rt.SetParent(canvas, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(760f, 200f);
        box.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

        notice = box.GetComponent<CanvasGroup>();
        notice.blocksRaycasts = false;
        notice.interactable = false;

        var label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        var lrt = label.GetComponent<RectTransform>();
        lrt.SetParent(rt, false);
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;

        noticeText = label.GetComponent<TextMeshProUGUI>();
        if (labelFont != null) noticeText.font = labelFont;   // 카드 라벨과 같은 폰트
        noticeText.fontSize = 44f;
        noticeText.color = Color.white;
        noticeText.alignment = TextAlignmentOptions.Center;
        noticeText.raycastTarget = false;
    }
}
