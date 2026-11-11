using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// 외계인 본모습 연출 (공용, 2026-10-21) — 응시 / 사물 삼키기 / 사물로 변신.
// 세계관 규칙 "사물을 먹으면 그 사물이 된다"를 보여 주는 연출이라 튜토리얼 흐름과 분리했다 (리플레이·인트로 등에서 재사용).
// 규칙 "보면 굳음"을 연출에서도 지킨다: 플레이어 화면에 보이는 동안은 연출 시계가 멈춘다.
// "보이나"는 DollVision 위험 지대(실제 화면 + 공용 여유 각도)가 아니라 실제 화면 + screenMargin 기준이다 —
// 위험 지대로 재면 모니터를 볼 때 화면 밖인 창 너머까지 굳어 시연이 멈췄다 (2026-10-21 버그, 설계 9.1 #9).
// 연출 동안에는 평소 AI(DollMove)를 멈추고(SetFrozen), 이동은 NavMeshAgent.Move로 네브메시 위에서만 한다.
// 설계 기록: 프로젝트 문서 「코드 설계 - 튜토리얼 시스템」 9장
[RequireComponent(typeof(DollMove))]
public class AlienMimicFx : MonoBehaviour
{
    [Tooltip("흔들 몸 (모델). 비우면 첫 번째 자식")]
    [SerializeField] private Transform visual;
    [Tooltip("삼킬 때만 보이는 입 (몸 앞면 아래쪽의 어두운 타원). 평소엔 크기 0")]
    [SerializeField] private Transform mouth;
    [SerializeField] private Vector3 mouthOpenScale = new Vector3(0.22f, 0.15f, 0.06f);

    [Header("Gaze")]
    [SerializeField] private float turnSpeed = 160f;
    [Tooltip("올려다볼 때 몸을 뒤로 젖히는 각도")]
    [SerializeField] private float gazeTilt = 20f;

    [Header("Swallow")]
    [Tooltip("사물을 처음 보고 가만히 있는 시간")]
    [SerializeField] private float lookSeconds = 1.3f;
    [SerializeField] private float approachSpeed = 0.7f;
    [Tooltip("몸 앞면에서 이만큼 떨어진 곳까지 다가간다")]
    [SerializeField] private float reachGap = 0.06f;
    [SerializeField] private float mouthOpenSeconds = 0.45f;
    [SerializeField] private float gulpSeconds = 0.5f;
    [SerializeField] private float jiggleSeconds = 0.9f;
    [SerializeField] private float jiggleAmount = 0.14f;
    [Tooltip("입을 벌릴 때 몸이 앞·위로 늘어나는 정도")]
    [SerializeField] private float stretch = 0.12f;

    [Header("Seen check")]
    [Tooltip("화면 가장자리 바깥으로 이만큼(뷰포트 비율)까지도 보이는 것으로 친다 — 고개를 돌리는 순간 움직임이 들키지 않게")]
    [SerializeField] private float screenMargin = 0.1f;

    [Header("Transform")]
    [SerializeField] private float squashSeconds = 0.35f;
    [SerializeField] private float popSeconds = 0.35f;

    private DollMove ai;
    private DollVision vision;
    private NavMeshAgent agent;
    private Vector3 baseScale = Vector3.one;
    private Quaternion baseRot = Quaternion.identity;
    private float frontDistance = 0.3f;   // 루트에서 몸 앞면까지 (입 위치에서 계산)

    public bool Busy { get; private set; }

    void Awake()
    {
        ai = GetComponent<DollMove>();
        vision = GetComponent<DollVision>();
        agent = GetComponent<NavMeshAgent>();
        if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
        if (visual != null) { baseScale = visual.localScale; baseRot = visual.localRotation; }
        if (mouth != null)
        {
            frontDistance = Mathf.Max(0.1f, transform.InverseTransformPoint(mouth.position).z);
            mouth.localScale = Vector3.zero;
        }
    }

    // 연출 시계: 보이는 동안엔 0 → 그 자세로 굳는다
    bool IsSeen()
    {
        if (vision == null) return false;
        var cam = vision.playerCamera != null ? vision.playerCamera : Camera.main;
        if (cam == null) return false;
        Vector3 p = transform.position + Vector3.up * vision.eyeHeightOffset;
        Vector3 vp = cam.WorldToViewportPoint(p);
        if (vp.z <= 0f || vp.x < -screenMargin || vp.x > 1f + screenMargin || vp.y < -screenMargin || vp.y > 1f + screenMargin) return false;
        // 거리·가림은 DollVision과 같은 검사 (각도 검사는 위에서 화면으로 했으므로 넉넉히 넘김)
        return vision.IsPointSeen(p, 180f);
    }

    float seenTime;     // 연속으로 보인 시간 (멈춤 이유 로그용)
    bool loggedPause;
    float Dt()
    {
        if (IsSeen())
        {
            seenTime += Time.deltaTime;
            if (seenTime > 3f && !loggedPause) { loggedPause = true; Debug.Log($"[외계인 연출] {name}: 플레이어 화면에 보여서 3초 넘게 멈춤 (보면 굳음)", this); }
            return 0f;
        }
        seenTime = 0f;
        return Time.deltaTime;
    }

    // 길이 seconds만큼 연출 시간이 흐를 때까지 기다리며 매 프레임 0~1 진행값을 넘긴다
    IEnumerator Run(float seconds, Action<float> step)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Dt();
            step(Mathf.Clamp01(t / Mathf.Max(0.01f, seconds)));
            yield return null;
        }
        step(1f);
    }

    IEnumerator TurnTo(Vector3 point)
    {
        while (true)
        {
            Vector3 dir = point - transform.position; dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) yield break;
            var want = Quaternion.LookRotation(dir.normalized, Vector3.up);
            if (Quaternion.Angle(transform.rotation, want) < 1f) { transform.rotation = want; yield break; }
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * Dt());
            yield return null;
        }
    }

    // ===== 응시 =====

    /// <summary>대상 쪽으로 돌아 위로 올려다보고 seconds 동안 유지한 뒤 평소대로 (튜토리얼 3-3: CCTV)</summary>
    public IEnumerator Gaze(Transform target, float seconds)
    {
        if (target == null || visual == null) yield break;
        Busy = true;
        ai.SetFrozen(true);
        yield return TurnTo(target.position);
        yield return Run(0.4f, k => visual.localRotation = baseRot * Quaternion.Euler(-gazeTilt * Smooth(k), 0f, 0f));
        yield return Run(seconds, _ => { });
        yield return Run(0.4f, k => visual.localRotation = baseRot * Quaternion.Euler(-gazeTilt * (1f - Smooth(k)), 0f, 0f));
        ai.SetFrozen(false);
        Busy = false;
    }

    // ===== 삼키기 =====

    /// <summary>사물을 보고 → 천천히 다가가 → 입을 벌려 삼키고 → 출렁인다. 사물은 마지막에 꺼진다</summary>
    public IEnumerator Swallow(Transform item)
    {
        if (item == null || visual == null) yield break;
        while (Busy) yield return null;   // 응시 중이면 끝날 때까지
        Busy = true;
        ai.SetFrozen(true);

        yield return TurnTo(item.position);
        yield return Run(lookSeconds, _ => { });

        // 천천히 다가가기 (네브메시 위로만)
        // 안 보이는데도 1초 넘게 못 다가가면(네브메시 가장자리 등) 그 자리에서 삼킨다 — 컵이 입으로 빨려 오므로 조금 떨어져도 자연스러움
        float stuck = 0f;
        while (true)
        {
            Vector3 to = item.position - transform.position; to.y = 0f;
            float gap = to.magnitude - frontDistance - reachGap;
            if (gap <= 0f) break;
            float dt = Dt();
            if (dt > 0f)
            {
                bool useAgent = agent != null && agent.enabled && agent.isOnNavMesh;
                Vector3 before = useAgent ? agent.nextPosition : transform.position;
                Vector3 move = to.normalized * Mathf.Min(gap, approachSpeed * dt);
                if (useAgent) agent.Move(move);
                else transform.position += move;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to.normalized), turnSpeed * dt);
                float moved = ((useAgent ? agent.nextPosition : transform.position) - before).magnitude;
                stuck = moved < move.magnitude * 0.2f ? stuck + dt : 0f;
                if (stuck > 1f)
                {
                    Debug.Log($"[외계인 연출] {name}: 컵까지 {gap:0.00}m 남기고 더 못 다가가서 그 자리에서 삼킴", this);
                    break;
                }
            }
            yield return null;
        }

        // 입 열림 + 몸이 앞·위로 늘어남
        yield return Run(mouthOpenSeconds, k =>
        {
            float e = Smooth(k);
            if (mouth != null) mouth.localScale = mouthOpenScale * e;
            SetStretch(1f + stretch * e, stretch * 0.6f * e);
        });

        // 꿀꺽: 사물이 입으로 빨려 들어가며 작아지고 몸 안으로 사라짐
        Vector3 from = item.position;
        Vector3 fromScale = item.localScale;
        yield return Run(gulpSeconds, k =>
        {
            float e = k * k;   // 처음엔 천천히, 끝에 확 빨려 들어감
            Vector3 mouthPos = mouth != null ? mouth.position : transform.position + Vector3.up * 0.2f;
            Vector3 inside = transform.position + Vector3.up * 0.25f;
            item.position = e < 0.6f ? Vector3.Lerp(from, mouthPos, e / 0.6f) : Vector3.Lerp(mouthPos, inside, (e - 0.6f) / 0.4f);
            item.localScale = fromScale * Mathf.Lerp(1f, 0.25f, e);
        });
        item.gameObject.SetActive(false);

        // 입 닫힘 + 출렁임 (늘었다 줄었다 하며 잦아듦)
        yield return Run(0.2f, k =>
        {
            if (mouth != null) mouth.localScale = mouthOpenScale * (1f - k);
            SetStretch(1f + stretch * (1f - k), stretch * 0.6f * (1f - k));
        });
        yield return Run(jiggleSeconds, k =>
        {
            float t = k * jiggleSeconds;
            float wob = jiggleAmount * Mathf.Exp(-4f * t) * Mathf.Sin(t * 18f);
            SetStretch(1f + wob, 0f);
        });
        SetStretch(1f, 0f);
        Busy = false;
    }

    // ===== 변신 =====

    /// <summary>몸이 납작해지며 사라지고, 같은 자리에 사물 모습(look)이 튀어나오듯 나타난다. 이후 AI는 꺼진다 (이제 그 사물이다)</summary>
    public IEnumerator TransformInto(GameObject look)
    {
        while (Busy) yield return null;
        Busy = true;
        ai.SetFrozen(true);

        yield return Run(squashSeconds, k =>
        {
            float e = Smooth(k);
            float sy = Mathf.Lerp(1f, 0.2f, e);
            visual.localScale = Vector3.Scale(baseScale, new Vector3(Mathf.Lerp(1f, 1.35f, e), sy, Mathf.Lerp(1f, 1.35f, e)));
        });
        visual.gameObject.SetActive(false);

        if (look != null)
        {
            Vector3 lookScale = look.transform.localScale;
            look.transform.SetPositionAndRotation(new Vector3(transform.position.x, look.transform.position.y, transform.position.z), transform.rotation);
            look.SetActive(true);
            yield return Run(popSeconds, k =>
            {
                // 0 → 1.15 → 1 (살짝 넘쳤다 제자리)
                float s = k < 0.7f ? Mathf.Lerp(0f, 1.15f, Smooth(k / 0.7f)) : Mathf.Lerp(1.15f, 1f, (k - 0.7f) / 0.3f);
                look.transform.localScale = lookScale * s;
            });
        }

        ai.enabled = false;
        if (agent != null) agent.enabled = false;
        Busy = false;
    }

    // 세로 sy배 + 앞으로 forward배 늘이기 (가로는 부피 유지)
    void SetStretch(float sy, float forward)
    {
        if (visual == null) return;
        float sx = 1f / Mathf.Sqrt(Mathf.Max(0.3f, sy));
        visual.localScale = Vector3.Scale(baseScale, new Vector3(sx, sy, sx * (1f + forward)));
    }

    static float Smooth(float k) => k * k * (3f - 2f * k);
}
