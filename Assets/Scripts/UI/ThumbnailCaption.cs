using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 스테이지 카드 썸네일 아래쪽에 반투명 띠와 한 줄 설명을 올린다 — 클릭은 그대로 둔다
public class ThumbnailCaption : MonoBehaviour
{
    [SerializeField] private string caption = "개발 초기화면";
    [SerializeField] private TMP_FontAsset captionFont;                // 비우면 카드 라벨 폰트
    [Range(0f, 1f)] [SerializeField] private float bandAlpha = 0.6f;  // 띠 진하기
    [SerializeField] private float bandHeight = 40f;                  // 띠 높이 (썸네일 기준)
    [SerializeField] private float fontSize = 30f;

    private void Awake()
    {
        // 라벨 폰트는 글자를 만들기 전에 잡아둔다
        var cardLabel = GetComponentInChildren<TMP_Text>();

        Transform thumb = transform.Find("Image");
        if (thumb == null) thumb = transform;

        var band = new GameObject("CaptionBand", typeof(RectTransform), typeof(Image));
        var rt = band.GetComponent<RectTransform>();
        rt.SetParent(thumb, false);
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, bandHeight);

        var img = band.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, bandAlpha);
        img.raycastTarget = false;   // 클릭은 카드 버튼이 받도록 통과

        var label = new GameObject("CaptionText", typeof(RectTransform), typeof(TextMeshProUGUI));
        var lrt = label.GetComponent<RectTransform>();
        lrt.SetParent(rt, false);
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;

        var text = label.GetComponent<TextMeshProUGUI>();
        if (captionFont != null) text.font = captionFont;
        else if (cardLabel != null) text.font = cardLabel.font;
        text.text = caption;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
    }
}
