using System.Collections.Generic;
using UnityEngine;

// 메인화면 격리실 빈대 배회 (2026-10-22)
// - 메인화면은 CCTV 화면이라 "보면 굳음"이 일어나지 않는다 (확정 규칙: CCTV로 보는 건 관측이 아님) → 시야 판정 없음
// - 가구 없는 빈 방이라 NavMesh 없이 사각형 안 직선 이동. 게임용 DollMove(시야·이동권·얼룩·웃음·승리 집계)는 쓰지 않는다
// - 걷는 모양(출렁임)은 같은 오브젝트의 PegDollAnimator가 루트 이동 속도를 읽어 만든다 → 여기선 루트만 움직인다
// 상태: 쉬기(가끔 제자리 돌기) → 목표 고르기 → 몸 돌리기 → 직진 → 쉬기
public class MenuAlienWander : MonoBehaviour
{
    [Header("Speed")]
    [SerializeField] private Vector2 speedRange = new Vector2(0.55f, 0.8f);
    [Tooltip("초당 도는 각도")]
    [SerializeField] private float turnSpeed = 160f;
    [Tooltip("목표 방향과 이 각도 안으로 들어오면 앞으로 간다")]
    [SerializeField] private float walkAngle = 35f;

    [Header("Rest")]
    [SerializeField] private Vector2 restRange = new Vector2(1.5f, 4f);
    [Tooltip("쉬는 동안 제자리에서 한 번 돌아볼 확률")]
    [Range(0f, 1f)] [SerializeField] private float lookAroundChance = 0.45f;

    [Header("Spacing")]
    [Tooltip("목표를 고를 때 다른 빈대(위치·목표)와 벌릴 거리")]
    [SerializeField] private float targetSpacing = 0.9f;
    [Tooltip("걷는 중 앞쪽 이 거리 안에 다른 빈대가 있으면 멈췄다가 다시 고른다")]
    [SerializeField] private float personalSpace = 0.7f;
    [Tooltip("목표는 지금 자리에서 이 거리 이상")]
    [SerializeField] private float minTravel = 0.8f;

    static readonly List<MenuAlienWander> All = new List<MenuAlienWander>();

    private MenuIsolationRoom room;
    private float speed;
    private Vector3 target;
    private bool walking;
    private float restLeft;
    private float lookYaw;          // 쉬는 중 돌아볼 방향 (NaN = 안 돎)

    public Vector3 Target => walking ? target : transform.position;

    /// <summary>MenuIsolationRoom이 생성 직후 부른다</summary>
    public void Init(MenuIsolationRoom owner)
    {
        room = owner;
        speed = Random.Range(speedRange.x, speedRange.y);
        Rest(Random.Range(0.2f, restRange.y));   // 시작 박자를 개체마다 어긋나게
    }

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Update()
    {
        if (room == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (!walking)
        {
            if (!float.IsNaN(lookYaw)) TurnToward(lookYaw, dt);
            restLeft -= dt;
            if (restLeft <= 0f) PickTarget();
            return;
        }

        Vector3 to = target - transform.position; to.y = 0f;
        float dist = to.magnitude;
        if (dist < 0.05f) { Rest(Random.Range(restRange.x, restRange.y)); return; }

        float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float off = TurnToward(yaw, dt);
        if (Mathf.Abs(off) > walkAngle) return;   // 먼저 몸을 돌리고 나서 걷는다

        if (Blocked()) { Rest(Random.Range(0.5f, 1.2f)); return; }

        float step = Mathf.Min(dist, speed * dt);
        transform.position += to / dist * step;
    }

    // 남은 각도(도)를 돌려준다
    float TurnToward(float yaw, float dt)
    {
        float cur = transform.eulerAngles.y;
        float next = Mathf.MoveTowardsAngle(cur, yaw, turnSpeed * dt);
        transform.rotation = Quaternion.Euler(0f, next, 0f);
        return Mathf.DeltaAngle(next, yaw);
    }

    void Rest(float seconds)
    {
        walking = false;
        restLeft = seconds;
        lookYaw = Random.value < lookAroundChance
            ? transform.eulerAngles.y + Random.Range(-70f, 70f)
            : float.NaN;
    }

    bool Blocked()
    {
        Vector3 me = transform.position, fwd = transform.forward;
        foreach (var o in All)
        {
            if (o == this) continue;
            Vector3 d = o.transform.position - me; d.y = 0f;
            if (d.sqrMagnitude < personalSpace * personalSpace && Vector3.Dot(d.normalized, fwd) > 0.3f) return true;
        }
        return false;
    }

    void PickTarget()
    {
        Vector3 best = transform.position;
        float bestScore = -1f;
        for (int i = 0; i < 12; i++)
        {
            Vector3 p = room.RandomPoint();
            if ((p - transform.position).sqrMagnitude < minTravel * minTravel) continue;
            float crowd = Clearance(p);
            if (crowd >= targetSpacing) { best = p; bestScore = crowd; break; }   // 충분히 비었으면 바로 씀
            if (crowd > bestScore) { best = p; bestScore = crowd; }             // 아니면 가장 덜 붐비는 곳
        }
        if (bestScore < 0f) { Rest(Random.Range(restRange.x, restRange.y)); return; }
        target = best;
        walking = true;
    }

    // p에서 가장 가까운 다른 빈대(현재 위치·목표)까지 거리
    float Clearance(Vector3 p)
    {
        float min = float.MaxValue;
        foreach (var o in All)
        {
            if (o == this) continue;
            min = Mathf.Min(min, Flat(p - o.transform.position), Flat(p - o.Target));
        }
        return min;
    }

    static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
}
