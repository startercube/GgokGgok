using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 화면 중앙 조준점 — 스프라이트 없이 메시로 직접 그린다.
// 기본(작은 점) → 조준(링 + E 표시) → 오답 직후(빨간 깨진 링 + X) → 회복 중(링이 다시 차오름)
[RequireComponent(typeof(CanvasRenderer))]
public class Crosshair : MaskableGraphic
{
    enum State
    {
        Idle,
        Aim,
        Wrong,
        Recovering
    }

    [Header("크기")]
    [SerializeField] private float dotRadius = 3f;
    [SerializeField] private float ringRadius = 18f;
    [SerializeField] private float ringThickness = 5f;
    [Tooltip("원 한 바퀴를 몇 조각으로 나눠 그릴지 — 클수록 매끄럽다")]
    [SerializeField] private int segments = 48;

    [Header("색")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color wrongColor = new Color(0.85f, 0.36f, 0.36f);
    [Tooltip("회복 중 아직 차오르지 않은 링 부분과 가운데 점의 색")]
    [SerializeField] private Color dimColor = new Color(1f, 1f, 1f, 0.25f);

    [Header("연출")]
    [Tooltip("링이 나타나고 사라지는 속도 (초당 0→1)")]
    [SerializeField] private float ringFadeSpeed = 10f;
    [Tooltip("오답 표시(빨간 X)를 유지하는 시간 — 이후 남은 쿨타임 동안 링이 차오른다")]
    [SerializeField] private float wrongHoldDuration = 1.2f;
    [Tooltip("오답 순간 조준점이 흔들리는 세기(px)")]
    [SerializeField] private float shakeStrength = 10f;
    [Tooltip("오답 순간 X가 커졌다 돌아오는 최대 배율")]
    [SerializeField] private float wrongPopScale = 1.75f;
    [Tooltip("커졌다 원래 크기로 돌아오기까지 걸리는 시간")]
    [SerializeField] private float wrongPopDuration = 0.25f;

    [Header("E 키 표시")]
    [SerializeField] private bool showKeyBadge = true;
    [Tooltip("조준점 중심에서 배지 중심까지의 거리")]
    [SerializeField] private Vector2 badgeOffset = new Vector2(36f, -36f);
    [SerializeField] private float badgeSize = 32f;
    [SerializeField] private float badgeCornerRadius = 6f;
    [SerializeField] private float badgeBorder = 1f;
    [SerializeField] private Color badgeFillColor = new Color(0.12f, 0.12f, 0.12f, 1f);
    [SerializeField] private Color badgeBorderColor = new Color(0.4f, 0.4f, 0.4f, 1f);
    [SerializeField] private float badgeFontSize = 26f;

    private TextMeshProUGUI keyLabel;
    private State state;
    private bool aiming;
    private float ringVisible;          // 0 = 점만, 1 = 링 완전히 펼쳐짐
    private float recoverProgress;      // 회복 중 링이 차오른 비율
    private float wrongStartTime = -999f;
    private float wrongEndTime = -999f;
    private float cooldownEndTime = -999f;
    private Vector2 basePosition;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;  // 화면 중앙 UI 클릭을 가로막지 않도록
    }

    protected override void Start()
    {
        base.Start();
        basePosition = rectTransform.anchoredPosition;
        if (Application.isPlaying && showKeyBadge)
            CreateKeyLabel();
    }

    // 배지 배경은 메시로 그리고, 글자만 TMP 자식으로 띄운다
    void CreateKeyLabel()
    {
        var go = new GameObject("KeyLabel", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = badgeOffset;
        rt.sizeDelta = new Vector2(badgeSize, badgeSize);

        keyLabel = go.AddComponent<TextMeshProUGUI>();
        keyLabel.text = "E";
        keyLabel.fontSize = badgeFontSize;
        keyLabel.alignment = TextAlignmentOptions.Center;
        keyLabel.color = normalColor;
        keyLabel.raycastTarget = false;
        keyLabel.alpha = 0f;
    }

    // ===== 외부 호출 =====

    public void SetAiming(bool value)
    {
        aiming = value;
    }

    // 잡기 오답 — cooldown 동안 오답 표시 후 링이 다시 차오른다
    public void PlayWrong(float cooldown)
    {
        float hold = Mathf.Min(wrongHoldDuration, cooldown * 0.5f);
        wrongStartTime = Time.time;
        wrongEndTime = Time.time + hold;
        cooldownEndTime = Time.time + cooldown;
    }

    // ===== 상태 갱신 =====

    void Update()
    {
        if (!Application.isPlaying) return;

        float now = Time.time;

        if (now < wrongEndTime)
            state = State.Wrong;
        else if (now < cooldownEndTime)
        {
            state = State.Recovering;
            recoverProgress = Mathf.InverseLerp(wrongEndTime, cooldownEndTime, now);
        }
        else
            state = aiming ? State.Aim : State.Idle;

        float target = state == State.Idle ? 0f : 1f;
        ringVisible = Mathf.MoveTowards(ringVisible, target, ringFadeSpeed * Time.deltaTime);

        if (keyLabel != null)
            keyLabel.alpha = BadgeAlpha();

        // 오답 순간 흔들림 — 남은 오답 시간에 비례해 잦아든다
        Vector2 shake = Vector2.zero;
        if (state == State.Wrong && wrongHoldDuration > 0f)
            shake = Random.insideUnitCircle * shakeStrength * ((wrongEndTime - now) / wrongHoldDuration);
        rectTransform.anchoredPosition = basePosition + shake;

        SetVerticesDirty();
    }

    // ===== 그리기 =====

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Vector2 c = rectTransform.rect.center;

        switch (state)
        {
            case State.Wrong:
                // 링이 세 조각으로 깨지고 가운데에 X — 순간 커졌다 돌아온다
                float pop = WrongPop();
                float wr = ringRadius * pop;
                float wt = ringThickness * pop;
                for (int i = 0; i < 3; i++)
                    AddArc(vh, c, wr, wt, 20f + i * 120f, 80f, wrongColor);
                float x = wr * 0.55f;
                float y = wr * 0.55f;
                AddLine(vh, c + new Vector2(-x, y), c + new Vector2(x, -y), wt, wrongColor);
                AddLine(vh, c + new Vector2(-x, -y), c + new Vector2(x, y), wt, wrongColor);
                break;

            case State.Recovering:
                AddArc(vh, c, ringRadius, ringThickness, 0f, 360f, dimColor);
                AddArc(vh, c, ringRadius, ringThickness, 0f, 360f * recoverProgress, normalColor);
                AddDisc(vh, c, dotRadius, dimColor);
                break;

            default:
                AddDisc(vh, c, dotRadius, normalColor);
                if (ringVisible > 0f)
                {
                    // 점에서 링으로 퍼지듯 나타난다
                    float r = Mathf.Lerp(dotRadius * 2f, ringRadius, ringVisible);
                    Color ringColor = normalColor;
                    ringColor.a *= ringVisible;
                    AddArc(vh, c, r, ringThickness, 0f, 360f, ringColor);
                }
                break;
        }

        float badgeAlpha = BadgeAlpha();
        if (badgeAlpha > 0f)
        {
            Vector2 bc = c + badgeOffset;
            Vector2 size = new Vector2(badgeSize, badgeSize);
            Color border = badgeBorderColor;
            Color fill = badgeFillColor;
            border.a *= badgeAlpha;
            fill.a *= badgeAlpha;
            AddRoundedRect(vh, bc, size, badgeCornerRadius, border);
            AddRoundedRect(vh, bc, size - Vector2.one * badgeBorder * 2f, Mathf.Max(0f, badgeCornerRadius - badgeBorder), fill);
        }
    }

    // E 배지는 조준 상태에서 링과 함께 나타난다
    float BadgeAlpha()
    {
        return showKeyBadge && state == State.Aim ? ringVisible : 0f;
    }

    // 오답 직후 1 → wrongPopScale → 1 로 한 번 튀는 배율
    float WrongPop()
    {
        if (wrongPopDuration <= 0f) return 1f;
        float t = Mathf.Clamp01((Time.time - wrongStartTime) / wrongPopDuration);
        return 1f + (wrongPopScale - 1f) * Mathf.Sin(t * Mathf.PI);
    }

    // 각도는 12시 방향을 0으로 시계방향 증가
    static Vector2 Dir(float deg)
    {
        float rad = deg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
    }

    void AddArc(VertexHelper vh, Vector2 c, float radius, float thickness, float startDeg, float sweepDeg, Color col)
    {
        if (sweepDeg <= 0f) return;

        int steps = Mathf.Max(2, Mathf.CeilToInt(segments * sweepDeg / 360f));
        int start = vh.currentVertCount;
        float inner = radius - thickness * 0.5f;
        float outer = radius + thickness * 0.5f;

        for (int i = 0; i <= steps; i++)
        {
            Vector2 d = Dir(startDeg + sweepDeg * i / steps);
            AddVert(vh, c + d * inner, col);
            AddVert(vh, c + d * outer, col);
        }
        for (int i = 0; i < steps; i++)
        {
            int v = start + i * 2;
            vh.AddTriangle(v, v + 1, v + 3);
            vh.AddTriangle(v, v + 3, v + 2);
        }
    }

    void AddDisc(VertexHelper vh, Vector2 c, float radius, Color col)
    {
        AddFan(vh, c, radius, 0f, 360f, col);
    }

    // 부채꼴 — 둥근 모서리와 원에 같이 쓴다
    void AddFan(VertexHelper vh, Vector2 c, float radius, float startDeg, float sweepDeg, Color col)
    {
        int steps = Mathf.Max(2, Mathf.CeilToInt(segments * sweepDeg / 360f));
        int start = vh.currentVertCount;
        AddVert(vh, c, col);
        for (int i = 0; i <= steps; i++)
            AddVert(vh, c + Dir(startDeg + sweepDeg * i / steps) * radius, col);
        for (int i = 1; i <= steps; i++)
            vh.AddTriangle(start, start + i, start + i + 1);
    }

    void AddRect(VertexHelper vh, Vector2 min, Vector2 max, Color col)
    {
        int start = vh.currentVertCount;
        AddVert(vh, new Vector2(min.x, min.y), col);
        AddVert(vh, new Vector2(min.x, max.y), col);
        AddVert(vh, new Vector2(max.x, max.y), col);
        AddVert(vh, new Vector2(max.x, min.y), col);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    // 가로·세로 사각형 두 개 + 네 모서리 부채꼴
    void AddRoundedRect(VertexHelper vh, Vector2 c, Vector2 size, float radius, Color col)
    {
        Vector2 h = size * 0.5f;
        float r = Mathf.Min(radius, h.x, h.y);

        AddRect(vh, c + new Vector2(-h.x, -h.y + r), c + new Vector2(h.x, h.y - r), col);
        if (r <= 0f) return;
        AddRect(vh, c + new Vector2(-h.x + r, h.y - r), c + new Vector2(h.x - r, h.y), col);
        AddRect(vh, c + new Vector2(-h.x + r, -h.y), c + new Vector2(h.x - r, -h.y + r), col);

        AddFan(vh, c + new Vector2(h.x - r, h.y - r), r, 0f, 90f, col);
        AddFan(vh, c + new Vector2(h.x - r, -h.y + r), r, 90f, 90f, col);
        AddFan(vh, c + new Vector2(-h.x + r, -h.y + r), r, 180f, 90f, col);
        AddFan(vh, c + new Vector2(-h.x + r, h.y - r), r, 270f, 90f, col);
    }

    void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color col)
    {
        Vector2 n = Vector2.Perpendicular((b - a).normalized) * thickness * 0.5f;
        int start = vh.currentVertCount;
        AddVert(vh, a - n, col);
        AddVert(vh, a + n, col);
        AddVert(vh, b + n, col);
        AddVert(vh, b - n, col);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    static void AddVert(VertexHelper vh, Vector2 pos, Color col)
    {
        UIVertex v = UIVertex.simpleVert;
        v.position = pos;
        v.color = col;
        vh.AddVert(v);
    }
}
