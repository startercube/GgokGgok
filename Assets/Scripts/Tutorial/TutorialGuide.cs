using System;
using UnityEngine;

// 튜토리얼 한 주임 (2026-10-21) — 정해진 길(구간)을 따라 앞장서 걷고, 진행 문을 열어 주고, 서 있을 땐 플레이어를 본다.
// 구간은 배치 도구가 평면 배치 좌표로 만든다. TutorialDirector가 대사 데이터의 동작 "guide:leg:이름"으로 구간을 요청한다.
// - 구간은 정해진 순서대로만 걷는다: 뒤 구간을 먼저 요청받으면 앞 구간부터 서둘러 걸어 따라잡는다 (지나야 할 문을 빠뜨리지 않게)
// - lead 구간: 플레이어가 뒤처지면(길 위 거리 차가 leadGap 넘게) 멈춰 돌아보고 기다린다 (스토리보드 2 구현 메모, 제안)
// - 문 자리에 오면 그 문을 열고 열릴 때까지 기다렸다 지나간다
// - 네브메시를 쓰지 않는다: 길이 정해져 있어 더 단순하고, 닫힌 문 때문에 길이 끊기는 문제가 없다
[RequireComponent(typeof(Rigidbody))]
public class TutorialGuide : MonoBehaviour
{
    [Serializable] public class Leg
    {
        public string name;
        public Vector3[] points = new Vector3[0];
        [Tooltip("points와 같은 순서. 그 자리에 도착하면 열 문 (없으면 비움)")]
        public DoorController[] doors = new DoorController[0];
        [Tooltip("플레이어가 뒤처지면 기다린다")]
        public bool lead;
    }

    [SerializeField] private Leg[] legs = new Leg[0];
    [SerializeField] private PegDollAnimator anim;

    [Header("Move")]
    [Tooltip("평소 걷는 속도. 2026-11-02 1.6 → 3.2 (플레이어 7.5m/s가 문 앞에서 기다리는 시간 줄이기, 「코드 설계 - 튜토리얼 템포」). 올리면 PegDollAnimator 보폭도 같은 비율로 — 걸음 박자 유지")]
    [SerializeField] private float walkSpeed = 3.2f;
    [Tooltip("밀린 구간이 있거나 플레이어가 앞서 있을 때")]
    [SerializeField] private float catchUpSpeed = 5.0f;   // 2026-11-02 2.8 → 5.0
    [SerializeField] private float turnSpeed = 320f;
    [Tooltip("문을 연 뒤 지나가기 전까지 기다리는 시간 (문 열림 시간 1초 + 여유)")]
    [SerializeField] private float doorWait = 1.15f;

    [Header("Lead (2026-11-02 고무줄 앞장서기, 「코드 설계 - 튜토리얼 템포」 9장)")]
    [Tooltip("플레이어보다 이만큼(길 위 거리, m) 앞서기 전까지는 걷기 속도 그대로")]
    [SerializeField] private float comfortGap = 3f;
    [Tooltip("이만큼 앞서면 멈춘다. comfortGap~leadGap 사이에선 간격이 벌어질수록 서서히 느려짐 (4 → 7)")]
    [SerializeField] private float leadGap = 7f;
    [Tooltip("멈춘 뒤 플레이어가 이 안으로 오면 다시 출발 (2.2 → 4.5, leadGap과 2.5m 차이 = 멈춤·출발 반복 방지)")]
    [SerializeField] private float resumeGap = 4.5f;
    [Tooltip("leadGap에서의 속도 = 걷기 속도 × 이 값")]
    [SerializeField, Range(0.1f, 1f)] private float minPace = 0.35f;
    [Tooltip("멈춘 뒤 이만큼(초) 더 기다려도 안 오면 그때 돌아본다 — 잠깐 머뭇거리는 플레이어 때문에 고개를 돌리지 않게")]
    [SerializeField] private float lookBackDelay = 0.7f;

    private Rigidbody rb;
    private Transform player;
    private int requestedUpTo = -1;   // 이 번호까지의 구간을 걸어야 함
    private int nextLeg;              // 다음에 시작할 구간
    private int curLeg = -1;
    private int curPoint;
    private float doorTimer;
    private DoorController waitingDoor;
    private bool waitingForPlayer;
    private float waitedTime;         // 플레이어를 기다린 시간 (lookBackDelay가 지나야 돌아봄)
    private Transform faceTarget;      // null이면 서 있을 때 플레이어를 본다
    private bool busy;                 // 연출 중 (컵 던지기 등) — 다음 구간을 시작하지 않는다
    private int lastFinished = -1;     // 끝까지 걸은 마지막 구간 번호

    public bool IsWalking => curLeg >= 0 && doorTimer <= 0f && !waitingForPlayer;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        if (anim == null) anim = GetComponent<PegDollAnimator>();
    }

    void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
    }

    // ===== 진행 관리가 부르는 것 =====

    public void RequestLeg(string legName)
    {
        int i = Array.FindIndex(legs, l => l.name == legName);
        if (i < 0) { Debug.LogWarning($"[{name}] 모르는 구간: {legName}", this); return; }
        requestedUpTo = Mathf.Max(requestedUpTo, i);
    }

    public void FaceTarget(Transform t) => faceTarget = t;
    /// <summary>켜 두는 동안 다음 구간을 시작하지 않고 그 자리에서 faceTarget(없으면 플레이어)을 본다</summary>
    public void SetBusy(bool value) => busy = value;
    /// <summary>그 구간을 끝까지 걸었는지</summary>
    public bool HasFinished(string legName)
    {
        int i = Array.FindIndex(legs, l => l.name == legName);
        return i >= 0 && i <= lastFinished;
    }
    public void FacePlayer() => faceTarget = null;

    /// <summary>그 구간까지 다 걸은 것으로 하고 끝 점에 선다. 지나온 구간의 문은 연다 (연습 판 재시작 체크포인트, 2026-10-21)</summary>
    public void SkipTo(string legName)
    {
        int i = Array.FindIndex(legs, l => l.name == legName);
        if (i < 0) { Debug.LogWarning($"[{name}] 모르는 구간: {legName}", this); return; }
        for (int k = 0; k <= i; k++)
            foreach (var d in legs[k].doors) if (d != null) d.Open();
        var pts = legs[i].points;
        if (pts.Length > 0)
        {
            Vector3 p = pts[pts.Length - 1];
            Vector3 pos = new Vector3(p.x, transform.position.y, p.z);
            transform.position = pos;
            if (rb != null) rb.position = pos;
        }
        requestedUpTo = Mathf.Max(requestedUpTo, i);
        nextLeg = i + 1;
        curLeg = -1;
        lastFinished = i;
        doorTimer = 0f;
        waitingDoor = null;
        waitingForPlayer = false;
        faceTarget = null;
    }
    public void LookDown() { if (anim != null) anim.LookDown(); }

    // ===== 이동 =====

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        if (busy && curLeg < 0) { TurnTowards(FacePoint(), dt); return; }

        if (curLeg < 0 && nextLeg <= requestedUpTo && nextLeg < legs.Length)
        {
            curLeg = nextLeg++;
            curPoint = 0;
            faceTarget = null;
        }

        if (curLeg < 0) { TurnTowards(FacePoint(), dt); return; }
        var leg = legs[curLeg];

        // 문 여는 중
        if (doorTimer > 0f)
        {
            doorTimer -= dt;
            if (waitingDoor != null) TurnTowards(waitingDoor.transform.position, dt);
            if (doorTimer <= 0f) Advance(leg);
            return;
        }

        // 앞장서기: 플레이어가 뒤처지면 걸음을 늦추고, 한참 멀어지면 멈춰서 (조금 뒤에) 돌아본다
        float pace = 1f;
        if (leg.lead && player != null)
        {
            float gap = Progress(leg, transform.position) - Progress(leg, player.position);
            if (waitingForPlayer) waitingForPlayer = gap > resumeGap;
            else waitingForPlayer = gap > leadGap;
            if (waitingForPlayer)
            {
                waitedTime += dt;
                if (waitedTime >= lookBackDelay) TurnTowards(player.position, dt);
                return;
            }
            waitedTime = 0f;
            pace = Mathf.Lerp(1f, minPace, Mathf.InverseLerp(comfortGap, leadGap, gap));   // 고무줄: 벌어질수록 느리게
        }
        else waitedTime = 0f;

        Vector3 target = leg.points[curPoint];
        Vector3 to = target - rb.position; to.y = 0f;
        bool behindSchedule = requestedUpTo > curLeg ||
                              (player != null && Progress(leg, player.position) > Progress(leg, transform.position) + 2f);
        float speed = behindSchedule ? catchUpSpeed : walkSpeed * pace;
        float step = speed * dt;

        if (to.magnitude <= step)
        {
            rb.MovePosition(new Vector3(target.x, rb.position.y, target.z));
            var door = curPoint < leg.doors.Length ? leg.doors[curPoint] : null;
            if (door != null)
            {
                door.Open();
                waitingDoor = door;
                doorTimer = doorWait;
            }
            else Advance(leg);
        }
        else
        {
            rb.MovePosition(rb.position + to.normalized * step);
            TurnTowards(rb.position + to, dt);
        }
    }

    void Advance(Leg leg)
    {
        waitingDoor = null;
        curPoint++;
        if (curPoint >= leg.points.Length) { lastFinished = curLeg; curLeg = -1; }   // 구간 끝 — 서서 플레이어를 본다
    }

    Vector3 FacePoint()
    {
        if (faceTarget != null) return faceTarget.position;
        return player != null ? player.position : transform.position + transform.forward;
    }

    void TurnTowards(Vector3 point, float dt)
    {
        Vector3 dir = point - rb.position; dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        var want = Quaternion.LookRotation(dir.normalized, Vector3.up);
        rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, want, turnSpeed * dt));
    }

    // 구간 길 위에서의 거리 (시작점부터). 길에서 떨어져 있어도 가장 가까운 지점으로 본다
    static float Progress(Leg leg, Vector3 pos)
    {
        var pts = leg.points;
        if (pts.Length == 0) return 0f;
        Vector3 p = new Vector3(pos.x, 0f, pos.z);
        float best = float.MaxValue, along = 0f, walked = 0f;
        for (int i = 1; i < pts.Length; i++)
        {
            Vector3 a = new Vector3(pts[i - 1].x, 0f, pts[i - 1].z), b = new Vector3(pts[i].x, 0f, pts[i].z);
            Vector3 ab = b - a; float len = ab.magnitude;
            if (len < 0.001f) continue;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / (len * len));
            float d = Vector3.Distance(p, a + ab * t);
            if (d < best) { best = d; along = walked + t * len; }
            walked += len;
        }
        return along;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.8f);
        foreach (var leg in legs)
            for (int i = 1; i < leg.points.Length; i++) Gizmos.DrawLine(leg.points[i - 1] + Vector3.up * 0.2f, leg.points[i] + Vector3.up * 0.2f);
    }
}
