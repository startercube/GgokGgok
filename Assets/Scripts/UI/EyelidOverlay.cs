using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// 눈꺼풀 + 흐림 화면 효과 (공용, 2026-10-21) — 눈을 감았다 뜨는 장면(튜토리얼 기상, 나중에 기절·졸음 등)에 쓴다.
// 위·아래 검은 판 두 장이 가장자리 부드러운 그라데이션으로 화면을 덮는다. 열림 정도(Open) 0 = 감음, 1 = 다 뜸.
// 흐림(Blur)은 전역 Volume(피사계 심도 + 비네트)의 weight — URP 후처리라 셰이더를 따로 만들지 않는다. Volume이 없으면 흐림만 빠진다.
// 언제 감고 뜰지는 부르는 쪽(예: TutorialWakeUp)이 정한다 — 자막 박스(SubtitleBox)와 같은 "보여 주기 / 언제" 분리
// 설계 기록: 프로젝트 문서 「코드 설계 - 튜토리얼 시스템」 10장
[RequireComponent(typeof(RectTransform))]
public class EyelidOverlay : MonoBehaviour
{
    [Tooltip("흐림용 전역 Volume (피사계 심도·비네트). 비우면 흐림 없음")]
    [SerializeField] private Volume blurVolume;
    [Tooltip("눈꺼풀 가장자리 부드러운 부분의 두께 (화면 높이 비율)")]
    [SerializeField] private float edgeSoftness = 0.2f;
    [SerializeField] private Color lidColor = Color.black;
    [Range(0f, 1f)] [SerializeField] private float startOpen = 1f;
    [Range(0f, 1f)] [SerializeField] private float startBlur = 0f;

    private RectTransform top, bottom;
    private float open, blur;
    private bool built;

    public float Open { get => open; set { open = Mathf.Clamp01(value); Apply(); } }
    public float Blur { get => blur; set { blur = Mathf.Clamp01(value); Apply(); } }

    void Awake()
    {
        Build();
        open = startOpen;
        blur = startBlur;
        Apply();
    }

    /// <summary>seconds 동안 부드럽게 열림·흐림을 바꾼다 (Time.deltaTime 기준 → 일시정지 중엔 멈춤)</summary>
    public IEnumerator To(float targetOpen, float targetBlur, float seconds)
    {
        float o0 = open, b0 = blur, t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / Mathf.Max(0.0001f, seconds));
            k = k * k * (3f - 2f * k);
            open = Mathf.Lerp(o0, targetOpen, k);
            blur = Mathf.Lerp(b0, targetBlur, k);
            Apply();
            yield return null;
        }
        open = Mathf.Clamp01(targetOpen);
        blur = Mathf.Clamp01(targetBlur);
        Apply();
    }

    void Build()
    {
        if (built) return;
        built = true;
        var rt = (RectTransform)transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        // 판 높이(1 + edge) 중 아래쪽 edge 부분만 투명 → 불투명 그라데이션, 나머지는 불투명
        const int H = 128;
        float soft = edgeSoftness / (1f + edgeSoftness);
        var tex = new Texture2D(1, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "EyelidGradient" };
        for (int y = 0; y < H; y++)
        {
            float v = (y + 0.5f) / H;
            float a = v >= soft ? 1f : Mathf.SmoothStep(0f, 1f, v / soft);
            tex.SetPixel(0, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();

        top = MakeLid("TopLid", tex, new Rect(0f, 0f, 1f, 1f));
        bottom = MakeLid("BottomLid", tex, new Rect(0f, 1f, 1f, -1f));   // 위아래 뒤집기
    }

    RectTransform MakeLid(string lidName, Texture tex, Rect uv)
    {
        var go = new GameObject(lidName, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(transform, false);
        var img = go.GetComponent<RawImage>();
        img.texture = tex;
        img.uvRect = uv;
        img.color = lidColor;
        img.raycastTarget = false;
        var r = (RectTransform)go.transform;
        r.offsetMin = r.offsetMax = Vector2.zero;
        return r;
    }

    void Apply()
    {
        if (!built) Build();
        float e = edgeSoftness;
        // 위 눈꺼풀의 불투명 경계: 감음 0.5 → 다 뜸 1 + e (부드러운 가장자리까지 화면 밖)
        float b = 0.5f + open * (0.5f + e);
        top.anchorMin = new Vector2(0f, b - e);
        top.anchorMax = new Vector2(1f, b + 1f);
        float c = 0.5f - open * (0.5f + e);
        bottom.anchorMin = new Vector2(0f, c - 1f);
        bottom.anchorMax = new Vector2(1f, c + e);
        bool visible = open < 0.999f;
        top.gameObject.SetActive(visible);
        bottom.gameObject.SetActive(visible);

        if (blurVolume != null)
        {
            blurVolume.weight = blur;
            blurVolume.enabled = blur > 0.001f;
        }
    }
}
