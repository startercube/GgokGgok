using UnityEngine;

// 페그돌 캐릭터 공용 절차 애니메이션 (2026-10-21) — 리깅 없이 몸 전체를 움직여 걷는 느낌을 낸다.
// 캐릭터 규칙(확정): 팔다리 없는 페그돌 → 호핑·기울임·스쿼시 앤 스트레치로 표현.
// 루트(이 컴포넌트가 붙은 오브젝트)가 움직인 속도를 읽어, 자식 visual만 흔든다 → 이동 스크립트와 독립 (한 주임, 나중에 플·다른 NPC에도)
// - 걷기: 보폭마다 통통 뛰기(hop) + 앞으로 기울기(lean) + 좌우 흔들림(sway) + 착지 때 납작·뛸 때 길쭉 (부피 유지)
// - 서 있기: 아주 약한 숨쉬기
// - 한 번짜리: 고개 숙이기(LookDown, 클립보드 확인 1-2), 던지기(Throw, 컵 3-5)
// - 가는 쪽 보기(faceMovement, 2026-10-24): 루트를 이동 스크립트가 돌리지 않는 경우(리플레이 속 플) visual만 가는 쪽으로 돌린다
public class PegDollAnimator : MonoBehaviour
{
    [Tooltip("흔들 대상. 비우면 첫 번째 자식")]
    [SerializeField] private Transform visual;

    [Header("Walk")]
    [Tooltip("이 속도(m/s)에서 걷기 모션이 최대")]
    [SerializeField] private float fullSpeed = 1.6f;
    [Tooltip("한 번 뛸 때 나아가는 거리 (m)")]
    [SerializeField] private float strideLength = 0.55f;
    // 2026-10-21 플레이테스트 "깐족거린다" → 뛰기·기울기·좌우·스쿼시를 줄이고 보폭을 늘려 박자를 느리게 (처음 값 0.45 / 0.05 / 7 / 3.5 / 0.06)
    [SerializeField] private float hopHeight = 0.02f;
    [SerializeField] private float leanDegrees = 4f;
    [SerializeField] private float swayDegrees = 1f;
    [Tooltip("스쿼시 앤 스트레치 세기 (0.03 = 키가 ±3%)")]
    [SerializeField] private float squash = 0.03f;
    [Tooltip("뛰기·스쿼시·기울기·좌우 흔들림 전체 배율. 멀리서 보는 카메라(리플레이 CCTV)에서 걸음이 보이게 키울 때 — PlayerBodyView가 리플레이에서 설정")]
    [SerializeField, Min(0f)] private float motionScale = 1f;

    [Header("Idle")]
    [SerializeField] private float breatheAmount = 0.012f;
    [SerializeField] private float breathePeriod = 3.2f;

    [Header("Face Movement")]
    [Tooltip("켜면 visual이 가는 쪽을 본다 (루트 회전은 그대로). 리플레이 속 플용 — PlayerBodyView가 리플레이에서 켬")]
    [SerializeField] private bool faceMovement = false;
    [Tooltip("도는 속도 (도/초). 한 주임 TutorialGuide.turnSpeed와 같음")]
    [SerializeField] private float faceTurnSpeed = 320f;
    [Tooltip("이 속도(m/s)보다 빠르면 이동 방향을 목표로")]
    [SerializeField] private float faceMinSpeed = 0.3f;
    [Tooltip("이만큼(초) 서 있으면 루트 앞(녹화된 카메라 방향)으로 돌아섬")]
    [SerializeField] private float faceIdleDelay = 0.35f;

    private Vector3 lastPos;
    private float phase;           // 보폭 진행 (라디안, π마다 한 번 착지)
    private float moveAmount;      // 0 = 서 있음, 1 = 최대 걷기 (부드럽게)
    private float lookDownTime, lookDownDuration, lookDownDegrees;
    private float throwTime = 999f;
    private const float ThrowDuration = 0.75f;
    private Vector3 baseLocalPos;
    private Quaternion baseLocalRot;
    private Vector3 baseLocalScale = Vector3.one;
    private float heading;         // 몸이 보는 월드 방향 (도, faceMovement일 때)
    private float idleTime;

    /// <summary>걷기 모션 크기 배율 (1 = 기본). 박자·숨쉬기·한 번짜리 몸짓에는 안 곱함</summary>
    public float MotionScale { get => motionScale; set => motionScale = Mathf.Max(0f, value); }

    /// <summary>visual이 가는 쪽을 보게 할지. 켜는 순간 지금 루트 방향에서 시작</summary>
    public bool FaceMovement
    {
        get => faceMovement;
        set { if (value && !faceMovement) { heading = transform.eulerAngles.y; idleTime = 0f; } faceMovement = value; }
    }

    /// <summary>지금 걷는 속도 (m/s, 수평)</summary>
    public float Speed { get; private set; }

    void Awake()
    {
        if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
        if (visual != null)
        {
            baseLocalPos = visual.localPosition;
            baseLocalRot = visual.localRotation;
            baseLocalScale = visual.localScale;
        }
        lastPos = transform.position;
        heading = transform.eulerAngles.y;
    }

    /// <summary>고개(몸)를 잠깐 숙였다 든다</summary>
    public void LookDown(float seconds = 1.4f, float degrees = 16f)
    {
        lookDownDuration = Mathf.Max(0.1f, seconds);
        lookDownDegrees = degrees;
        lookDownTime = 0f;
    }

    /// <summary>던지기: 몸을 살짝 뒤로 젖혔다가(0~0.2초) 앞으로 숙이며 던지고(0.2~0.4초) 돌아온다 (튜토리얼 3-5 컵)</summary>
    public void Throw() => throwTime = 0f;

    // 던지기 몸짓의 앞뒤 기울기 (도)
    float ThrowPitch()
    {
        if (throwTime >= ThrowDuration) return 0f;
        float t = throwTime;
        if (t < 0.2f) return Mathf.Lerp(0f, -9f, t / 0.2f);
        if (t < 0.4f) return Mathf.Lerp(-9f, 16f, (t - 0.2f) / 0.2f);
        return Mathf.Lerp(16f, 0f, (t - 0.4f) / (ThrowDuration - 0.4f));
    }

    void LateUpdate()
    {
        if (visual == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;   // 일시정지 중엔 그대로

        Vector3 d = transform.position - lastPos; d.y = 0f;
        lastPos = transform.position;
        Speed = d.magnitude / dt;
        moveAmount = Mathf.MoveTowards(moveAmount, Mathf.Clamp01(Speed / Mathf.Max(0.01f, fullSpeed)), dt * 4f);

        // 걸은 거리만큼 보폭이 진행 — 서면 착지 자세(phase = kπ)로 천천히 수렴
        if (Speed > 0.05f) phase += d.magnitude / Mathf.Max(0.05f, strideLength) * Mathf.PI;
        else phase = Mathf.MoveTowards(phase, Mathf.Round(phase / Mathf.PI) * Mathf.PI, dt * 6f);

        float s = Mathf.Sin(phase);
        float hop = Mathf.Abs(s);                    // 0 = 착지, 1 = 공중
        float k = moveAmount;
        float m = motionScale;

        // 스쿼시 앤 스트레치: 공중에선 길쭉, 착지 순간엔 납작
        float sy = 1f + squash * m * k * (hop * 2f - 1f);
        sy *= 1f + breatheAmount * (1f - k) * Mathf.Sin(Time.time * Mathf.PI * 2f / Mathf.Max(0.1f, breathePeriod));
        float sxz = 1f / Mathf.Sqrt(Mathf.Max(0.5f, sy));

        float pitch = leanDegrees * m * k;
        if (lookDownTime < lookDownDuration)
        {
            lookDownTime += dt;
            pitch += Mathf.Sin(Mathf.Clamp01(lookDownTime / lookDownDuration) * Mathf.PI) * lookDownDegrees;
        }
        if (throwTime < ThrowDuration) { throwTime += dt; pitch += ThrowPitch(); }
        float roll = swayDegrees * m * k * Mathf.Sin(phase);   // 한 걸음마다 좌우로

        // 가는 쪽 보기: 월드 방향(heading)을 목표로 돌리고, 루트와의 차이만큼 visual을 돌린다
        Quaternion face = Quaternion.identity;
        if (faceMovement)
        {
            float rootYaw = transform.eulerAngles.y;
            float target = heading;
            if (Speed > faceMinSpeed) { target = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; idleTime = 0f; }
            else if ((idleTime += dt) > faceIdleDelay) target = rootYaw;
            heading = Mathf.MoveTowardsAngle(heading, target, faceTurnSpeed * dt);
            face = Quaternion.Euler(0f, Mathf.DeltaAngle(rootYaw, heading), 0f);
        }

        visual.localPosition = baseLocalPos + Vector3.up * (hopHeight * m * k * hop);
        visual.localRotation = face * baseLocalRot * Quaternion.Euler(pitch, 0f, roll);
        visual.localScale = Vector3.Scale(baseLocalScale, new Vector3(sxz, sy, sxz));
    }
}
