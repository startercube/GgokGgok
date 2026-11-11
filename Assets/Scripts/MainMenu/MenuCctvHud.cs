using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 메인화면 CCTV 녹화기 표시 (2026-10-22)
// REC 깜빡임 · 날짜·시각 · 수용 개체 수 · 주사선 · 가끔 화면 깜빡임. 화면 속 3D가 아니라 녹화기 위에 덧씌운 표시라 UI에서 한다
// 메인화면에서 timeScale이 어떻든 돌아가도록 전부 unscaled 시간을 쓴다
public class MenuCctvHud : MonoBehaviour
{
    [SerializeField] private TMP_Text clockText;
    [SerializeField] private TMP_Text countText;
    [Tooltip("{0} = 수용 개체 수")]
    [SerializeField] private string countFormat = "수용 개체 {0:00} · 상태 이상 없음";
    [SerializeField] private Graphic recDot;
    [SerializeField] private float recPeriod = 1f;

    [Header("Scanlines")]
    [Tooltip("세로로 반복되는 주사선 텍스처 (T_Scanline: 3px 중 1px 어두움)")]
    [SerializeField] private RawImage scanlines;
    [Tooltip("텍스처 한 장이 차지할 화면 픽셀 수 (세로)")]
    [SerializeField] private float scanlineTilePixels = 3f;

    [Header("Flicker")]
    [SerializeField] private Graphic flicker;
    [SerializeField] private float flickerInterval = 7f;
    [SerializeField] private float flickerDuration = 0.12f;
    [Range(0f, 0.3f)] [SerializeField] private float flickerPeak = 0.07f;

    private int lastSecond = -1;
    private int lastScreenH = -1;
    private float flickerAt;

    void OnEnable()
    {
        flickerAt = Time.unscaledTime + flickerInterval * UnityEngine.Random.Range(0.4f, 1f);
        lastSecond = -1;
        lastScreenH = -1;
    }

    public void SetCount(int n)
    {
        if (countText != null) countText.text = string.Format(countFormat, n);
    }

    void Update()
    {
        float t = Time.unscaledTime;

        if (recDot != null) recDot.enabled = (t % recPeriod) < recPeriod * 0.5f;

        var now = DateTime.Now;
        if (clockText != null && now.Second != lastSecond)
        {
            lastSecond = now.Second;
            clockText.text = now.ToString("yyyy-MM-dd  HH:mm:ss");
        }

        if (scanlines != null && Screen.height != lastScreenH)
        {
            lastScreenH = Screen.height;
            scanlines.uvRect = new Rect(0f, 0f, 1f, Screen.height / Mathf.Max(1f, scanlineTilePixels));
        }

        if (flicker != null)
        {
            float k = (t - flickerAt) / Mathf.Max(0.01f, flickerDuration);
            float a = (k >= 0f && k <= 1f) ? Mathf.Sin(k * Mathf.PI) * flickerPeak : 0f;
            var c = flicker.color; c.a = a; flicker.color = c;
            if (k > 1f) flickerAt = t + flickerInterval * UnityEngine.Random.Range(0.7f, 1.3f);
        }
    }
}
