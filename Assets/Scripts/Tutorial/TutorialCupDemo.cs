using System;
using System.Collections;
using UnityEngine;

// 튜토리얼 장면 3 컵 시연 + CCTV 응시 (2026-10-21) — 순서만 진행하고, 외계인 연출은 AlienMimicFx에 맡긴다.
// 흐름: 한 주임 투입구 앞으로 → 클립보드 집어넣기 → 컵 들기 → 덮개 열기 → 던지기(포물선) → 착지(cupLanded)
//       → 외계인이 보고 다가가 삼킴 → 머그컵으로 변신 → 한 박자 뒤 transformed
// 대사는 진행 관리(TutorialDirector)가 이 이벤트에 붙여 낸다 (대사 데이터의 event 조건)
// 설계 기록: 프로젝트 문서 「코드 설계 - 튜토리얼 시스템」 9장
public class TutorialCupDemo : MonoBehaviour
{
    [Header("Alien")]
    [SerializeField] private AlienMimicFx alien;
    [Tooltip("3-3 응시 대상 (CCTV 카메라)")]
    [SerializeField] private Transform cctv;
    [SerializeField] private float gazeSeconds = 2.5f;
    [Tooltip("모니터를 처음 본 뒤 평소처럼 돌아다니는 시간 (바로 쳐다보면 부자연스러움, 2026-10-21 피드백)")]
    [SerializeField] private float gazeDelay = 2f;
    [Tooltip("이 각도·거리 안에서 플레이어가 모니터(watchTarget)를 보고 있을 때 응시한다 → 떡밥을 놓치지 않게")]
    [SerializeField] private float monitorLookAngle = 30f;
    [SerializeField] private float monitorLookDistance = 12f;
    [Tooltip("변신 뒤 남는 머그컵 모습 (처음엔 꺼 둠)")]
    [SerializeField] private GameObject mugLook;

    [Header("Han")]
    [SerializeField] private TutorialGuide guide;
    [SerializeField] private PegDollAnimator hanAnim;
    [SerializeField] private string slotLeg = "ToSlot";
    [Tooltip("던질 때 한 주임이 바라볼 곳 (투입구)")]
    [SerializeField] private Transform slotFacing;
    [Tooltip("던진 뒤 한 주임이 바라볼 곳 (모니터). 3-3 응시 때 플레이어가 모니터를 보는지 검사에도 씀")]
    [SerializeField] private Transform watchTarget;
    [SerializeField] private GameObject clipboard;
    [Tooltip("컵을 쥐는 자리 (한 주임 몸에 붙은 빈 오브젝트)")]
    [SerializeField] private Transform hand;

    [Header("Cup")]
    [SerializeField] private Transform cup;
    [SerializeField] private Vector3 landPoint;
    [Tooltip("포물선 꼭짓점이 직선보다 이만큼 높다 (투입구 구멍을 지나도록 배치 도구가 계산)")]
    [SerializeField] private float arcHeight = 0.5f;
    [SerializeField] private float flightSeconds = 0.85f;
    [Tooltip("켜면 컵이 수평으로 손 → holePoint(투입구 구멍 가운데) → landPoint 꺾은선을 따라 난다. 꺾이는 곳이 벽 안이라 안 보임 (2026-10-25)")]
    [SerializeField] private bool throughHole;
    [SerializeField] private Vector3 holePoint;

    [Header("Slot flap")]
    [SerializeField] private Transform flap;
    [SerializeField] private Quaternion flapClosed = Quaternion.identity;
    [SerializeField] private Quaternion flapOpen = Quaternion.identity;
    [Tooltip("켜면 덮개가 위치도 보간 = 위로 밀어 올리는 셔터 (2026-10-25). 끄면 예전 회전 방식")]
    [SerializeField] private bool flapSlides;
    [SerializeField] private Vector3 flapClosedPos;
    [SerializeField] private Vector3 flapOpenPos;

    [Tooltip("변신이 끝나고 transformed를 알리기까지 (한 주임 \"한 박자 늦게\")")]
    [SerializeField] private float beatAfterTransform = 1.0f;

    /// <summary>진행 이벤트 (gazeDone, cupLanded, transformed) — 진행 관리가 대사 조건으로 쓴다</summary>
    public event Action<string> OnEvent;

    private bool gazed, started, cupLanded;
    private Vector3 cupScale = Vector3.one;

    void Awake()
    {
        if (cup != null) { cupScale = cup.localScale; cup.gameObject.SetActive(false); }
        if (mugLook != null) mugLook.SetActive(false);
        if (flap != null) SetFlap(0f);
    }

    /// <summary>3-3: 처음 모니터를 본 뒤, 외계인이 잠시 돌아다니다가 CCTV를 올려다본다 (한 번만)</summary>
    public void StartGaze()
    {
        if (gazed || alien == null || cctv == null) return;
        gazed = true;
        StartCoroutine(GazeRoutine());
    }

    IEnumerator GazeRoutine()
    {
        // 평소 AI 그대로 돌아다니게 둔다
        yield return Wait(gazeDelay);
        // 플레이어가 모니터를 보고 있을 때 일어나야 "모니터 속 외계인과 눈이 마주침"이 된다
        while (!cupLanded && !PlayerLooksAtMonitor()) yield return null;
        if (cupLanded) yield break;   // 컵이 먼저 떨어지면 응시는 건너뜀 (삼키기와 겹치지 않게)
        yield return alien.Gaze(cctv, gazeSeconds);
        OnEvent?.Invoke("gazeDone");   // 3-3b "이번엔 창으로 다시 봐."
    }

    // 진행 관리(TutorialDirector.IsLookingAt)와 같은 방식: 기준점이 아니라 렌더러 가운데로 잰다 (맵 모델 기준점은 화면 가운데가 아닐 수 있음)
    private Renderer monitorRenderer;
    bool PlayerLooksAtMonitor()
    {
        if (watchTarget == null) return true;
        var cam = Camera.main;
        if (cam == null) return false;
        if (monitorRenderer == null) monitorRenderer = watchTarget.GetComponentInChildren<Renderer>();
        Vector3 center = monitorRenderer != null ? monitorRenderer.bounds.center : watchTarget.position;
        Vector3 to = center - cam.transform.position;
        return to.magnitude <= monitorLookDistance && Vector3.Angle(cam.transform.forward, to) <= monitorLookAngle;
    }

    /// <summary>3-5: 컵 시연 시작 (한 번만)</summary>
    public void StartDemo()
    {
        if (started) return;
        started = true;
        StartCoroutine(DemoRoutine());
    }

    // 덮개 열림 정도 0(닫힘)~1(열림): 회전은 항상, 셔터면 위치도
    void SetFlap(float t)
    {
        flap.localRotation = Quaternion.Slerp(flapClosed, flapOpen, t);
        if (flapSlides) flap.localPosition = Vector3.Lerp(flapClosedPos, flapOpenPos, t);
    }

    // 컵의 수평 위치: throughHole이면 손 → 구멍 가운데 → 착지점 꺾은선 위 거리 비율 k, 아니면 직선 (높이는 호출하는 쪽에서)
    Vector3 FlightXZ(Vector3 from, float k)
    {
        if (!throughHole) return Vector3.Lerp(from, landPoint, k);
        Vector3 a = new Vector3(from.x, 0f, from.z), h = new Vector3(holePoint.x, 0f, holePoint.z), b = new Vector3(landPoint.x, 0f, landPoint.z);
        float d1 = Vector3.Distance(a, h), d2 = Vector3.Distance(h, b);
        float s = k * (d1 + d2);
        return s <= d1 ? Vector3.Lerp(a, h, d1 > 0f ? s / d1 : 1f) : Vector3.Lerp(h, b, d2 > 0f ? (s - d1) / d2 : 1f);
    }

    IEnumerator DemoRoutine()
    {
        // 1 한 주임이 투입구 앞으로
        if (guide != null)
        {
            guide.RequestLeg(slotLeg);
            while (!guide.HasFinished(slotLeg)) yield return null;
            guide.SetBusy(true);
            guide.FaceTarget(slotFacing);
        }
        yield return Wait(0.5f);

        // 2 클립보드 집어넣기 → 컵 들기
        if (clipboard != null)
        {
            Vector3 s = clipboard.transform.localScale;
            yield return Tween(0.3f, k => clipboard.transform.localScale = s * (1f - k));
            clipboard.SetActive(false);
        }
        if (cup != null && hand != null)
        {
            cup.SetParent(hand, false);
            cup.localPosition = Vector3.zero;
            cup.localRotation = Quaternion.identity;
            cup.gameObject.SetActive(true);
            yield return Tween(0.25f, k => cup.localScale = cupScale * Back(k));
        }
        yield return Wait(0.35f);

        // 3 덮개 열기
        if (flap != null) yield return Tween(flapSlides ? 0.4f : 0.3f, k => SetFlap(Mathf.SmoothStep(0f, 1f, k)));

        // 4 던지기: 몸을 젖혔다 앞으로 (PegDollAnimator.Throw), 앞으로 갈 때 컵이 손을 떠남
        if (hanAnim != null) hanAnim.Throw();
        yield return Wait(0.22f);
        if (cup != null)
        {
            cup.SetParent(transform, true);
            Vector3 from = cup.position;
            Quaternion fromRot = cup.rotation;
            Quaternion spin = Quaternion.AngleAxis(200f, Vector3.Cross(Vector3.up, (landPoint - from).normalized));
            yield return Tween(flightSeconds, k =>
            {
                Vector3 p = FlightXZ(from, k);
                p.y = Mathf.Lerp(from.y, landPoint.y, k) + 4f * arcHeight * k * (1f - k);
                cup.position = p;
                cup.rotation = Quaternion.Slerp(fromRot, spin * fromRot, k);
            });
            // 착지: 바로 세워지며 살짝 튐
            Quaternion upright = Quaternion.Euler(0f, cup.eulerAngles.y, 0f);
            yield return Tween(0.18f, k =>
            {
                cup.position = landPoint + Vector3.up * (0.05f * Mathf.Sin(k * Mathf.PI));
                cup.rotation = Quaternion.Slerp(cup.rotation, upright, k);
            });
            cup.SetPositionAndRotation(landPoint, upright);
        }
        cupLanded = true;
        OnEvent?.Invoke("cupLanded");

        // 5 덮개 닫기, 한 주임은 모니터를 본다 (밀린 구간이 있으면 이제 따라간다)
        yield return Wait(0.4f);
        if (flap != null) yield return Tween(flapSlides ? 0.4f : 0.3f, k => SetFlap(1f - Mathf.SmoothStep(0f, 1f, k)));
        if (guide != null) { guide.SetBusy(false); guide.FaceTarget(watchTarget); }

        // 6 외계인: 보고 → 다가가 → 삼키고 → 변신 (보이는 동안엔 멈춤)
        if (alien != null && cup != null)
        {
            yield return alien.Swallow(cup);
            yield return new WaitForSeconds(0.6f);
            yield return alien.TransformInto(mugLook);
        }

        yield return new WaitForSeconds(beatAfterTransform);
        OnEvent?.Invoke("transformed");
        if (guide != null) guide.FacePlayer();
    }

    static IEnumerator Wait(float s) { yield return new WaitForSeconds(s); }

    static IEnumerator Tween(float seconds, Action<float> step)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            step(Mathf.Clamp01(t / seconds));
            yield return null;
        }
        step(1f);
    }

    // 0 → 1.1 → 1 (튀어나오듯)
    static float Back(float k) => k < 0.7f ? Mathf.Lerp(0f, 1.1f, k / 0.7f) : Mathf.Lerp(1.1f, 1f, (k - 0.7f) / 0.3f);
}
