using UnityEngine;
using UnityEngine.UI;

// 튜토리얼 가이드 시각화 (2026-11-02) — "지금 봐야 할 것 / 가야 할 곳"을 보여 준다.
//  ① 발광: 대상을 GuideHighlight에 넣는다 → AimOutlineFeature가 하늘색 깜빡이는 외곽선(보이는 부분만)으로 그림
//  ② 화살표: 대상이 화면 밖인 채 arrowDelay초가 지나면 화면 가장자리에 대상 방향을 가리키는 화살표
// 언제 무엇을 보여 줄지는 TutorialDirector가 정한다 (대사 데이터의 focus·focusUntil). 이 스크립트는 "어떻게 보여 주나"만.
// 끝나는 방식: untilReach = 플레이어가 대상 reachDistance 안에 오면 이번 표시를 끔 (문: "저리로 가라") / 아니면 Hide까지 (유리창·모니터)
// 규칙: 빈대·찾을 소품은 대상으로 받지 않는다 (IsAllowedTarget) — 가이드는 찾기 전 단계만 돕고 찾기를 대신하지 않는다
// 화살표 UI는 런타임에 자막 박스와 같은 Canvas, 자막 박스 바로 아래 층에 만든다 → 일시정지 화면(PausePage)보다 아래. 씬을 다시 만들 필요 없음
// 설계: 「코드 설계 - 튜토리얼 가이드 시각화」
public class TutorialFocus : MonoBehaviour
{
    [Tooltip("대상이 화면 밖인 채 이만큼(초) 지나면 화살표. 대사 데이터의 focusArrowDelay가 있으면 그 값으로 덮어씀")]
    [SerializeField] private float arrowDelay = 4f;
    [Tooltip("문처럼 '가야 할 곳' 대상: 이 거리(m) 안에 오면 이번 표시를 끈다")]
    [SerializeField] private float reachDistance = 2.5f;
    [Header("화살표")]
    [Tooltip("1080p 기준 픽셀 (Canvas 크기 조절을 따라감)")]
    [SerializeField] private float arrowSize = 96f;
    [Tooltip("화면 가장자리에서 안쪽으로 (1080p 기준 픽셀)")]
    [SerializeField] private float edgeMargin = 80f;
    [Tooltip("외곽선 가이드 색과 맞춤")]
    [SerializeField] private Color arrowColor = new Color(0.35f, 0.85f, 1f, 0.95f);
    [Tooltip("비우면 코드로 그린 삼각형. 진짜 아이콘을 넣을 땐 오른쪽을 가리키는 그림으로")]
    [SerializeField] private Sprite arrowSprite;
    [Tooltip("가리키는 방향으로 들락날락하는 거리 (1080p 기준 픽셀)와 주기(초)")]
    [SerializeField] private float bobDistance = 14f;
    [SerializeField] private float bobPeriod = 0.8f;
    [Tooltip("나타나고 사라지는 데 걸리는 시간(초)")]
    [SerializeField] private float fadeTime = 0.2f;
    [Tooltip("화면 안으로 치는 범위 (뷰포트 가장자리 여유)")]
    [SerializeField] private float onScreenInset = 0.03f;

    public float ArrowDelay { get => arrowDelay; set => arrowDelay = Mathf.Max(0f, value); }
    /// <summary>지금 표시 중인 대상 (도착·파괴로 끝났으면 null)</summary>
    public GameObject Current => IsActive ? target : null;

    private GameObject target;
    private Renderer[] renderers = new Renderer[0];
    private bool untilReach;
    private bool reached;
    private float offscreenTime;
    private float arrowAlpha;

    private Camera cam;
    private Transform player;
    private Canvas canvas;
    private RectTransform arrow;
    private Image arrowImage;
    private Texture2D generatedTexture;
    private Sprite generatedSprite;

    bool IsActive => target != null && !reached;

    // ===== 진행 관리가 부르는 것 =====

    /// <summary>화살표 UI를 만든다. 자막 박스가 있는 Canvas의 자막 바로 아래 층</summary>
    public void Init(SubtitleBox subtitle)
    {
        if (arrow != null) return;
        Transform under = subtitle != null ? subtitle.transform : null;
        canvas = under != null ? under.GetComponentInParent<Canvas>() : null;
        if (canvas != null) canvas = canvas.rootCanvas;
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) { Debug.LogWarning("[튜토리얼 가이드] Canvas가 없어 화살표를 만들지 못했습니다. 발광만 동작합니다.", this); return; }

        var go = new GameObject("TutorialFocusArrow", typeof(RectTransform));
        arrow = (RectTransform)go.transform;
        arrow.SetParent(canvas.transform, false);
        if (under != null && under.parent == canvas.transform) arrow.SetSiblingIndex(under.GetSiblingIndex());   // 자막 바로 아래
        arrow.anchorMin = arrow.anchorMax = arrow.pivot = new Vector2(0.5f, 0.5f);
        arrow.sizeDelta = new Vector2(arrowSize, arrowSize);
        arrowImage = go.AddComponent<Image>();
        arrowImage.sprite = arrowSprite != null ? arrowSprite : MakeArrowSprite();
        arrowImage.raycastTarget = false;
        arrowImage.preserveAspect = true;
        go.SetActive(false);
    }

    /// <summary>대상을 보여 주기 시작한다. 같은 대상·같은 방식이면 그대로 둔다</summary>
    public void Show(GameObject go, bool untilReach)
    {
        if (go == null) { Hide(); return; }
        if (ReferenceEquals(go, target) && untilReach == this.untilReach) return;
        target = go;
        this.untilReach = untilReach;
        reached = false;
        offscreenTime = 0f;
        renderers = go.GetComponentsInChildren<Renderer>(false);
    }

    public void Hide()
    {
        target = null;
        renderers = new Renderer[0];
        reached = false;
        offscreenTime = 0f;
    }

    /// <summary>가이드 대상으로 써도 되나: 빈대(DollMove)나 찾을 소품(Prop 태그)이 들어 있으면 안 된다 (정답을 빛내지 않는다)</summary>
    public static bool IsAllowedTarget(GameObject go, out string reason)
    {
        reason = null;
        if (go == null) { reason = "대상 없음"; return false; }
        if (go.GetComponentInChildren<DollMove>(true) != null || go.GetComponentInParent<DollMove>(true) != null)
        {
            reason = "빈대(DollMove)가 들어 있음";
            return false;
        }
        for (Transform t = go.transform; t != null; t = t.parent)
            if (t.CompareTag("Prop")) { reason = $"찾을 소품(Prop 태그: {t.name})"; return false; }
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            if (t.CompareTag("Prop")) { reason = $"찾을 소품(Prop 태그: {t.name})이 들어 있음"; return false; }
        return true;
    }

    // ===== 매 프레임 =====

    void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;

        bool hasBounds = TryGetBounds(out Bounds b);
        if (target != null && !hasBounds) target = null;   // 대상이 파괴되었거나(얼룩을 닦음) 렌더러가 모두 꺼짐

        if (IsActive && untilReach && player != null && b.SqrDistance(player.position) <= reachDistance * reachDistance)
            reached = true;   // 도착 — 이번 표시는 끝 (다음 컷에서 다시 켜질 수 있음)

        // ① 발광
        if (IsActive) GuideHighlight.Set(target, cam);
        else if (ReferenceEquals(GuideHighlight.ViewCamera, cam)) GuideHighlight.Clear();   // 내가 넣은 것만 지움

        // ② 화살표
        bool want = false;
        Vector2 dir = Vector2.zero;
        if (IsActive && cam != null)
        {
            bool onScreen = IsOnScreen(b.center, out dir);
            offscreenTime = onScreen ? 0f : offscreenTime + Time.deltaTime;
            want = !onScreen && offscreenTime >= arrowDelay;
        }
        else offscreenTime = 0f;
        UpdateArrow(want, dir);
    }

    void OnDisable()
    {
        if (GuideHighlight.Target != null && ReferenceEquals(GuideHighlight.Target, target)) GuideHighlight.Clear();
        arrowAlpha = 0f;
        if (arrow != null) arrow.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (arrow != null) Destroy(arrow.gameObject);
        if (generatedSprite != null) Destroy(generatedSprite);
        if (generatedTexture != null) Destroy(generatedTexture);
    }

    // ===== 도우미 =====

    // 대상 렌더러 범위 전체 (문이 열리며 움직여도 매 프레임 다시). 켜진 렌더러가 하나도 없으면 false
    bool TryGetBounds(out Bounds b)
    {
        b = default;
        bool any = false;
        if (target == null) return false;
        foreach (var r in renderers)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return any;
    }

    // 화면 안이면 true. 밖이면 dir = 화면 가운데에서 대상 쪽 방향 (픽셀 비율 반영, 정규화). 카메라 뒤면 뒤집어 계산
    bool IsOnScreen(Vector3 world, out Vector2 dir)
    {
        Vector3 vp = cam.WorldToViewportPoint(world);
        bool behind = vp.z < 0f;
        float lo = onScreenInset, hi = 1f - onScreenInset;
        bool on = !behind && vp.x >= lo && vp.x <= hi && vp.y >= lo && vp.y <= hi;

        Vector2 d = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
        if (behind) d = -d;
        d.x *= Screen.width;
        d.y *= Screen.height;
        dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.down;   // 정확히 뒤 = 아래를 가리킴 ("돌아봐")
        return on;
    }

    void UpdateArrow(bool want, Vector2 dir)
    {
        if (arrow == null) return;
        float step = fadeTime > 0f ? Time.unscaledDeltaTime / fadeTime : 1f;
        arrowAlpha = Mathf.MoveTowards(arrowAlpha, want ? 1f : 0f, step);
        bool visible = arrowAlpha > 0.001f;
        if (arrow.gameObject.activeSelf != visible) arrow.gameObject.SetActive(visible);
        if (!visible) return;

        // 사라지는 중에는 마지막 방향을 유지
        if (want) lastDir = dir;
        Vector2 d = lastDir;

        float scale = canvas != null ? canvas.scaleFactor : 1f;
        float halfW = Screen.width * 0.5f - (edgeMargin + arrowSize * 0.5f) * scale;
        float halfH = Screen.height * 0.5f - (edgeMargin + arrowSize * 0.5f) * scale;
        halfW = Mathf.Max(halfW, 1f); halfH = Mathf.Max(halfH, 1f);
        // 가운데에서 dir 쪽으로 쏜 선이 안쪽 사각형 가장자리와 만나는 점
        float t = Mathf.Min(Mathf.Abs(d.x) > 1e-4f ? halfW / Mathf.Abs(d.x) : float.MaxValue,
                            Mathf.Abs(d.y) > 1e-4f ? halfH / Mathf.Abs(d.y) : float.MaxValue);
        float bob = bobPeriod > 0f ? (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI / bobPeriod)) * bobDistance * scale : 0f;
        Vector2 px = d * (t - bob);   // 가리키는 쪽 가장자리에서 안으로 살짝 들어왔다 나갔다

        arrow.anchoredPosition = px / Mathf.Max(scale, 0.0001f);
        arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        Color c = arrowColor;
        c.a *= arrowAlpha;
        arrowImage.color = c;
    }

    private Vector2 lastDir = Vector2.down;

    // 오른쪽을 가리키는 삼각형 (가장자리 1.5px 부드럽게). 그림 에셋 없이 시작하려고 코드로 그린다
    Sprite MakeArrowSprite()
    {
        const int N = 64;
        generatedTexture = new Texture2D(N, N, TextureFormat.RGBA32, false)
        {
            name = "TutorialFocusArrow",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        Vector2 a = new Vector2(10f, 6f), bb = new Vector2(58f, 32f), c = new Vector2(10f, 58f);
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
            // 세 변까지의 부호 있는 거리 중 가장 작은 값 (안쪽 +)
            float d = Mathf.Min(EdgeDist(a, bb, p), Mathf.Min(EdgeDist(bb, c, p), EdgeDist(c, a, p)));
            byte alpha = (byte)(Mathf.Clamp01(d / 1.5f) * 255f);
            px[y * N + x] = new Color32(255, 255, 255, alpha);
        }
        generatedTexture.SetPixels32(px);
        generatedTexture.Apply(false, true);
        generatedSprite = Sprite.Create(generatedTexture, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        generatedSprite.name = "TutorialFocusArrow";
        return generatedSprite;
    }

    // 변 a→b의 왼쪽(반시계 방향 삼각형의 안쪽)이 + 가 되는 거리
    static float EdgeDist(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 e = b - a;
        float len = e.magnitude;
        return len > 0f ? (e.x * (p.y - a.y) - e.y * (p.x - a.x)) / len : 0f;
    }
}
