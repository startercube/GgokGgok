using UnityEngine;

// 반응하는 소품 (위화감 B안, 2026-11-02 시험 적용)
// 플레이어가 걸으면 발걸음 진동이 물결처럼 퍼져, 진짜 물건은 차례로 출렁인다(봉제 = 눌리며 기우뚱, 단단한 것 = 덜컥).
// 변장 빈대(DollDisguise)는 이 컴포넌트를 복사하지 않으므로 반응하지 않는다 — "굳은 것은 반응하지 않는다"
// 할로윈 방 = 지켜보는 방, 인형 방 = 건드려 보는 방 (같은 규칙, 맵마다 다른 찾기 행동)
// 2026-11-02 물결 반응: 반경 1.5m는 플레이어 옆·뒤라 안 보이고 반응이 매번 처음부터 다시 시작해 끊겼다
//  → 감쇠 스프링(임펄스를 더함)으로 끊김 없이, 관리자가 거리만큼 늦게 도착시키는 물결로 앞쪽까지
// 계산은 PropReactionManager가 한다 (이 컴포넌트에는 Update가 없다)
// 상세 「코드 설계 - 호박등 발광 깜빡임·인형 방 물결 반응」
[DisallowMultipleComponent]
public class PropReaction : MonoBehaviour
{
    const float MaxStep = 0.05f;           // 적분 한 번의 최대 dt — 프레임이 끊겨도 스프링이 폭발하지 않게
    const float OverDrive = 1.6f;          // 연속 발걸음으로 쌓여도 최대치의 이 배를 넘지 않게
    const float KickGain = 1.5f;           // 임펄스 한 번의 최대 출렁임 ≈ tilt·squash 가 되도록 맞춘 배율 (ζ≈0.25 기준)
    const float RhythmRatio = 0.35f;       // 같은 소품은 duration × 이 비율에 한 번만 임펄스를 받는다

    [Tooltip("움직일 대상. 비우면 자동(루트에 메시가 있으면 루트, 아니면 메시를 가진 자식이 하나뿐일 때 그 자식)")]
    [SerializeField] private Transform target;
    [Tooltip("발밑 세기로 한 번 맞았을 때 눌리는 비율 (0.18 = 18%). 단단한 물건은 0")]
    [SerializeField, Range(0f, 0.4f)] private float squash = 0.18f;
    [Tooltip("발밑 세기로 한 번 맞았을 때 플레이어 반대쪽으로 기우뚱하는 각도(도)")]
    [SerializeField, Range(0f, 25f)] private float tilt = 9f;
    [Tooltip("한 번 출렁임이 잦아들기까지 초")]
    [SerializeField, Min(0.05f)] private float duration = 1f;
    [Tooltip("잦아드는 동안 출렁이는 횟수")]
    [SerializeField, Min(0.5f)] private float wobbles = 2.5f;
    [Tooltip("발걸음 진동이 닿는 수평 거리(m)")]
    [SerializeField, Min(0.1f)] private float radius = 4f;
    [Tooltip("플레이어와의 높이 차가 이 안이면 반응한다 (선반 위 물건도 가구를 타고 진동이 올라간다고 본다)")]
    [SerializeField, Min(0f)] private float maxHeight = 2.5f;

    private Transform resolved;
    private Vector3 restScale;
    private Quaternion restRot;
    private int upAxis = 1;          // 세계 위쪽과 가장 가까운 로컬 축 (모델이 90° 돌아가 있어도 위아래로 눌리게)
    private Renderer[] renderers;
    private bool initialized;

    // 스프링 상태 — 기울기는 세계 수평(x,z) 2D 벡터(도): 방향 = 기우는 쪽, 크기 = 각도
    private Vector2 lean, leanVel;
    private float press, pressVel;   // 눌림 비율 (양수 = 눌림)
    private bool moving;
    private float lastKickTime = float.NegativeInfinity;

    // 물결 도착 예약 (한 칸) — 이미 있으면 더 이른 시각·더 큰 세기를 유지
    private bool pending;
    private float pendingTime;
    private float pendingStrength;
    private Vector3 pendingSource;

    public float Radius => radius;
    public float MaxHeight => maxHeight;
    internal bool InActiveList { get; set; }   // 관리자의 활성 목록에 들어 있는지 (목록 검색 없이 중복 방지)

    /// <summary>도구(PropLifeInstaller)가 반응 모양(폭신·덜컥·오뚝이)을 넣을 때 쓴다</summary>
    public void Configure(float squashAmount, float reactRadius, float tiltDegrees, float seconds, float wobbleCount)
    {
        squash = squashAmount;
        radius = reactRadius;
        tilt = tiltDegrees;
        duration = Mathf.Max(0.05f, seconds);
        wobbles = Mathf.Max(0.5f, wobbleCount);
    }

    void Awake() => Init();

    void Init()
    {
        if (initialized) return;
        resolved = target != null ? target : FindVisualRoot(transform);
        restScale = resolved.localScale;
        restRot = resolved.localRotation;
        upAxis = FindUpAxis(resolved);
        renderers = resolved.GetComponentsInChildren<Renderer>(true);
        initialized = true;
    }

    void OnEnable()
    {
        Init();
        PropReactionManager.Register(this);
    }

    void OnDisable()
    {
        PropReactionManager.Unregister(this);
        ResetState();
    }

    void ResetState()
    {
        pending = false;
        moving = false;
        lean = leanVel = Vector2.zero;
        press = pressVel = 0f;
        if (resolved == null) return;
        resolved.localScale = restScale;
        resolved.localRotation = restRot;
    }

    /// <summary>물결 도착을 예약한다. time = 도착 시각, source = 발 위치(기우는 방향을 정함)</summary>
    public void Schedule(float time, float strength, Vector3 source)
    {
        strength = Mathf.Clamp01(strength);
        if (pending)
        {
            if (time < pendingTime) { pendingTime = time; pendingSource = source; }
            pendingStrength = Mathf.Max(pendingStrength, strength);
            return;
        }
        pending = true;
        pendingTime = time;
        pendingStrength = strength;
        pendingSource = source;
    }

    /// <summary>관리자가 매 프레임 부른다. 예약이나 출렁임이 남아 있으면 true</summary>
    public bool Tick(float now, float dt)
    {
        if (resolved == null) return false;

        if (pending && now >= pendingTime)
        {
            pending = false;
            Kick(pendingStrength, pendingSource, now);
        }
        if (!moving) return pending;
        if (dt <= 0f) return true;   // 일시정지(timeScale 0) — 그 자세로 멈춰 있는다

        // 감쇠 진동자: 가속 = −ω²·x − 2ζω·v (반암시적 오일러). ζ는 duration 동안 약 2%로 잦아들게
        float omega = 2f * Mathf.PI * wobbles / duration;
        float zeta = 4f / (2f * Mathf.PI * wobbles);
        float k = omega * omega, c = 2f * zeta * omega;

        float remaining = dt;
        while (remaining > 0f)
        {
            float h = Mathf.Min(remaining, MaxStep);
            remaining -= h;
            leanVel += (-k * lean - c * leanVel) * h;
            lean += leanVel * h;
            pressVel += (-k * press - c * pressVel) * h;
            press += pressVel * h;
        }

        lean = Vector2.ClampMagnitude(lean, Mathf.Max(tilt * OverDrive, 0.01f));
        float maxPress = Mathf.Min(squash * OverDrive, 0.45f);
        press = Mathf.Clamp(press, -maxPress, maxPress);

        if (lean.sqrMagnitude < 0.0025f && leanVel.sqrMagnitude < 0.25f &&
            Mathf.Abs(press) < 0.002f && Mathf.Abs(pressVel) < 0.02f)
        {
            moving = false;
            lean = leanVel = Vector2.zero;
            press = pressVel = 0f;
            resolved.localScale = restScale;
            resolved.localRotation = restRot;
            return pending;
        }

        if (AnyVisible()) ApplyPose();   // 안 보이면 계산만 하고 적용은 건너뜀
        return true;
    }

    // 임펄스 — 속도에 더한다. 끊김 없이 연속 발걸음이 자연스럽게 쌓인다
    void Kick(float strength, Vector3 source, float now)
    {
        if (now - lastKickTime < duration * RhythmRatio) return;   // 너무 잦은 임펄스는 떨림처럼 보인다
        lastKickTime = now;

        Vector3 away = transform.position - source;
        Vector2 dir = new Vector2(away.x, away.z);
        dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right;   // 난수 없음 → 리플레이에서 같은 출렁임

        float kick = 2f * Mathf.PI * wobbles / duration * KickGain * strength;
        leanVel += dir * (tilt * kick);
        pressVel += squash * kick;
        moving = true;
    }

    void ApplyPose()
    {
        Quaternion rot = restRot;
        float angle = lean.magnitude;
        if (angle > 1e-3f)
        {
            // 세계 기준 축 = 위 × 기우는 방향 → 휴식 자세 기준 로컬 축으로 바꿔 돌린다 (모델이 돌아가 있어도 맞게)
            Vector3 dirW = new Vector3(lean.x, 0f, lean.y) / angle;
            Vector3 axisW = Vector3.Cross(Vector3.up, dirW);
            Quaternion parentRot = resolved.parent != null ? resolved.parent.rotation : Quaternion.identity;
            Vector3 axisL = Quaternion.Inverse(parentRot * restRot) * axisW;
            rot = restRot * Quaternion.AngleAxis(angle, axisL);
        }
        resolved.localRotation = rot;

        // 부피 유지(가로 = 1/√세로)는 PegDollAnimator와 같은 규칙
        float down = 1f - press;
        float side = 1f / Mathf.Sqrt(Mathf.Max(down, 0.5f));
        Vector3 s = new Vector3(side, side, side);
        s[upAxis] = down;
        resolved.localScale = Vector3.Scale(restScale, s);
    }

    bool AnyVisible()
    {
        foreach (Renderer r in renderers)
            if (r != null && r.enabled && r.isVisible) return true;
        return false;
    }

    /// <summary>루트에 메시가 있으면 루트, 아니면 메시를 가진 자식이 하나뿐일 때 그 자식(모델·Visual), 그 밖엔 루트</summary>
    public static Transform FindVisualRoot(Transform root)
    {
        if (HasMesh(root)) return root;
        Transform found = null;
        foreach (Transform child in root)
        {
            if (!HasMeshInChildren(child)) continue;
            if (found != null) return root;
            found = child;
        }
        return found != null ? found : root;
    }

    static bool HasMesh(Transform t)
        => t.GetComponent<MeshRenderer>() != null || t.GetComponent<SkinnedMeshRenderer>() != null;

    static bool HasMeshInChildren(Transform t)
        => t.GetComponentInChildren<MeshRenderer>(true) != null || t.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;

    static int FindUpAxis(Transform t)
    {
        float x = Mathf.Abs(Vector3.Dot(t.right, Vector3.up));
        float y = Mathf.Abs(Vector3.Dot(t.up, Vector3.up));
        float z = Mathf.Abs(Vector3.Dot(t.forward, Vector3.up));
        if (y >= x && y >= z) return 1;
        return x >= z ? 0 : 2;
    }
}
