using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

// 튜토리얼 장면 1 기상 연출 (2026-10-21) — 순서만 진행하고, 눈꺼풀·흐림은 공용 EyelidOverlay에 맡긴다.
// 흐름 (사용자 결정): 조금 뜸(매우 흐림, 누워서 천장) → 감음 → 더 크게 뜸(조금 흐림, 한 주임 쪽으로 돌아누움) → wakeLook
//       → (1-1·1-2 대사, 1-2가 끝나면 진행 관리가 Stand) → 감음 → 뜨며 일어남 + 어지러운 듯 고개 돌리기 → wakeDone
// 카메라는 전용 시네머신 카메라(wakeCam)를 높은 우선순위로 두고 직접 움직인다. 마지막 자세를 플레이어 카메라와 똑같이 맞춘 뒤
// 블렌드를 한 프레임 Cut으로 바꿔 넘긴다 (CameraManagement 인트로 전환과 같은 방식)
// 재시작 판(TutorialRetry.IsRetry)에서는 건너뛴다
// 설계 기록: 프로젝트 문서 「코드 설계 - 튜토리얼 시스템」 10장
public class TutorialWakeUp : MonoBehaviour
{
    [SerializeField] private EyelidOverlay eyelids;
    [SerializeField] private CinemachineCamera wakeCam;
    [SerializeField] private CinemachineCamera playerCam;
    [Tooltip("돌아누워 볼 사람 (한 주임)")]
    [SerializeField] private Transform lookAtPerson;
    [SerializeField] private float personEyeHeight = 1.4f;

    [Header("Lying")]
    [Tooltip("누운 머리 높이 (바닥 위)")]
    [SerializeField] private float lieHeight = 0.3f;
    [Tooltip("누운 머리가 선 자리에서 몸 방향(한 주임과 직각)으로 떨어진 거리")]
    [SerializeField] private float lieBack = 0.55f;
    [Tooltip("돌아누울 때 화면이 기우는 정도 (0 = 안 기움, 1 = 완전히 옆으로)")]
    [Range(0f, 1f)] [SerializeField] private float sideRoll = 0.5f;

    [Header("Step 1: 조금 뜸")]
    [SerializeField] private float blackStart = 1.0f;
    [SerializeField] private float open1 = 0.28f;
    [SerializeField] private float blur1 = 1f;
    [SerializeField] private float open1Seconds = 1.4f, hold1Seconds = 1.4f, close1Seconds = 0.5f, black1Seconds = 0.9f;

    [Header("Step 2: 더 크게 뜸 + 돌아눕기")]
    [SerializeField] private float open2 = 0.65f;
    [SerializeField] private float blur2 = 0.45f;
    [SerializeField] private float open2Seconds = 1.1f, turnDelay = 0.4f, turnSeconds = 1.6f;
    [Tooltip("진행 관리가 Stand를 부르지 않으면 이 시간 뒤 스스로 일어난다 (진행 관리 없이 테스트할 때)")]
    [SerializeField] private float autoStandAfter = 12f;

    [Header("Step 3: 일어나기 + 어지러움")]
    [SerializeField] private float close2Seconds = 0.45f, black2Seconds = 0.5f;
    [SerializeField] private float riseSeconds = 1.8f;
    [Tooltip("일어나며 숙이는 고개 각도")]
    [SerializeField] private float dizzyDownDegrees = 14f;
    [SerializeField] private float dizzyTurns = 2.5f;
    [SerializeField] private float dizzySeconds = 2.2f;
    [SerializeField] private float dizzyRadius = 5f;
    [SerializeField] private float settleSeconds = 0.6f;
    [Tooltip("다 일어난 뒤 시선 (+ = 아래)")]
    [SerializeField] private float finalPitch = 3f;

    /// <summary>진행 이벤트 (wakeLook, wakeDone) — 진행 관리가 대사 조건으로 쓴다</summary>
    public event Action<string> OnEvent;

    private Transform player;
    private bool standRequested;
    private bool running;

    void Start()
    {
        // Start에서 (모든 Awake 뒤 = EyelidOverlay 초기화 뒤, 첫 화면 그리기 전)
        if (TutorialRetry.IsRetry || wakeCam == null)
        {
            if (wakeCam != null) wakeCam.enabled = false;
            if (eyelids != null) { eyelids.Open = 1f; eyelids.Blur = 0f; }
            enabled = false;
            return;
        }
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p == null) { Debug.LogWarning("[기상 연출] Player가 없어 건너뜀", this); wakeCam.enabled = false; if (eyelids != null) eyelids.Open = 1f; return; }
        player = p.transform;
        if (eyelids != null) { eyelids.Open = 0f; eyelids.Blur = 1f; }   // 첫 프레임부터 감은 상태
        SetLocked(true);
        wakeCam.Priority = 100;
        running = true;
        StartCoroutine(Run());
    }

    /// <summary>1-2 대사가 끝나면 진행 관리가 부른다 (동작 wake:stand)</summary>
    public void Stand() => standRequested = true;

    IEnumerator Run()
    {
        // ---- 자세 계산 ----
        // 몸은 한 주임 방향과 직각으로 눕힌다 → 한 주임이 옆에 서 있고, "돌아눕기"로 그쪽을 보게 됨
        // fwd = 머리 → 발 방향
        Vector3 toPersonFlat = lookAtPerson != null ? Vector3.ProjectOnPlane(lookAtPerson.position - player.position, Vector3.up) : player.forward;
        if (toPersonFlat.sqrMagnitude < 0.01f) toPersonFlat = player.forward;
        Vector3 fwd = Vector3.Cross(Vector3.up, toPersonFlat.normalized).normalized;
        var col = player.GetComponent<Collider>();
        float feetY = col != null ? col.bounds.min.y : player.position.y - 0.725f;
        Vector3 lieHead = new Vector3(player.position.x, feetY + lieHeight, player.position.z) - fwd * lieBack;
        Vector3 standEye = StandEyePosition();

        Vector3 personEye = lookAtPerson != null ? lookAtPerson.position + Vector3.up * personEyeHeight : standEye + fwd * 2f;
        Quaternion rotCeiling = Quaternion.LookRotation(Vector3.up, -fwd);                       // 천장, 화면 위쪽 = 정수리 쪽
        Vector3 upHint = Vector3.Slerp(Vector3.up, -fwd, sideRoll).normalized;                   // 옆으로 누운 듯 기운 시야
        Quaternion rotSide = Quaternion.LookRotation((personEye - lieHead).normalized, upHint);

        Vector3 toPerson = Vector3.ProjectOnPlane(personEye - standEye, Vector3.up);
        float finalYaw = toPerson.sqrMagnitude > 0.01f ? Mathf.Atan2(toPerson.x, toPerson.z) * Mathf.Rad2Deg : player.eulerAngles.y;
        Quaternion rotFinal = Quaternion.Euler(finalPitch, finalYaw, 0f);

        SetPose(lieHead, rotCeiling);

        // ---- 1 조금 뜸 (매우 흐림, 천장) ----
        yield return Wait(blackStart);
        if (eyelids != null) yield return eyelids.To(open1, blur1, open1Seconds);
        yield return Hold(hold1Seconds, lieHead, rotCeiling);
        if (eyelids != null) yield return eyelids.To(0f, blur1, close1Seconds);
        yield return Wait(black1Seconds);

        // ---- 2 더 크게 뜸 + 한 주임 쪽으로 돌아누움 ----
        if (eyelids != null) StartCoroutine(eyelids.To(open2, blur2, open2Seconds));
        yield return Wait(turnDelay);
        yield return Tween(turnSeconds, k => SetPose(lieHead + fwd * 0.04f * k, Quaternion.Slerp(rotCeiling, rotSide, Ease(k))));
        OnEvent?.Invoke("wakeLook");   // 1-1 "이제야 일어났냐."

        // 1-1·1-2 대사 동안 그 자세로 (숨쉬듯 아주 조금 흔들림)
        float waited = 0f;
        Vector3 sidePos = lieHead + fwd * 0.04f;
        while (!standRequested && waited < autoStandAfter)
        {
            waited += Time.deltaTime;
            SetPose(sidePos, rotSide * Breath(waited));
            yield return null;
        }

        // ---- 3 감았다 뜨며 일어남 + 어지러움 ----
        if (eyelids != null) yield return eyelids.To(0f, blur2, close2Seconds);
        yield return Wait(black2Seconds);
        if (eyelids != null) StartCoroutine(OpenWhileRising());

        Quaternion rotDown = Quaternion.Euler(finalPitch + dizzyDownDegrees, finalYaw, 0f);
        Vector3 ctrl = new Vector3(Mathf.Lerp(sidePos.x, standEye.x, 0.7f), feetY + 0.9f, Mathf.Lerp(sidePos.z, standEye.z, 0.7f));
        yield return Tween(riseSeconds, k =>
        {
            float e = Ease(k);
            Vector3 a = Vector3.Lerp(sidePos, ctrl, e), b = Vector3.Lerp(ctrl, standEye, e);
            SetPose(Vector3.Lerp(a, b, e), Quaternion.Slerp(rotSide, rotDown, e));
        });

        // 고개를 숙인 채 작은 원을 두세 번 — 점점 잦아듦
        yield return Tween(dizzySeconds, k =>
        {
            float th = k * dizzyTurns * Mathf.PI * 2f;
            float r = dizzyRadius * (1f - 0.65f * k);
            var rot = Quaternion.Euler(finalPitch + dizzyDownDegrees + r * Mathf.Sin(th), finalYaw + r * Mathf.Cos(th), r * 0.4f * Mathf.Sin(th));
            Vector3 sway = (Quaternion.Euler(0f, finalYaw, 0f) * new Vector3(Mathf.Cos(th), 0f, 0f)) * 0.015f * (1f - k);
            SetPose(standEye + sway, rot);
        });
        Quaternion from = wakeCam.transform.rotation;
        yield return Tween(settleSeconds, k => SetPose(standEye, Quaternion.Slerp(from, rotFinal, Ease(k))));

        // ---- 플레이어 카메라로 넘기기 ----
        var panTilt = playerCam != null ? playerCam.GetComponent<CinemachinePanTilt>() : null;
        if (panTilt != null)
        {
            panTilt.PanAxis.Value = Mathf.Repeat(finalYaw + 180f, 360f) - 180f;
            panTilt.TiltAxis.Value = finalPitch;
        }
        var brain = CinemachineBrain.ActiveBrainCount > 0 ? CinemachineBrain.GetActiveBrain(0) : null;
        if (brain != null)
        {
            var original = brain.DefaultBlend;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            wakeCam.enabled = false;   // 꺼진 카메라는 절대 선택되지 않음 (우선순위 동점 걱정 없음)
            yield return null;
            brain.DefaultBlend = original;
        }
        else wakeCam.enabled = false;

        if (eyelids != null) { eyelids.Open = 1f; eyelids.Blur = 0f; }
        SetLocked(false);
        running = false;
        OnEvent?.Invoke("wakeDone");   // 1-3 "걸을 줄은 알지? 따라와."
    }

    IEnumerator OpenWhileRising()
    {
        yield return eyelids.To(1f, 0.15f, 0.9f);
        yield return eyelids.To(1f, 0f, 1.5f);
    }

    // 플레이어 카메라 눈 위치 = 따라가는 대상 + FollowOffset (지금 LockToTarget, y 0.6235)
    Vector3 StandEyePosition()
    {
        Transform target = playerCam != null && playerCam.Follow != null ? playerCam.Follow : player;
        var follow = playerCam != null ? playerCam.GetComponent<CinemachineFollow>() : null;
        Vector3 offset = follow != null ? follow.FollowOffset : new Vector3(0f, 0.6235f, 0f);
        return target.position + target.rotation * offset;
    }

    void SetPose(Vector3 pos, Quaternion rot) => wakeCam.transform.SetPositionAndRotation(pos, rot);

    IEnumerator Hold(float seconds, Vector3 pos, Quaternion rot)
    {
        float t = 0f;
        while (t < seconds) { t += Time.deltaTime; SetPose(pos, rot * Breath(t)); yield return null; }
    }

    static Quaternion Breath(float t) => Quaternion.Euler(Mathf.Sin(t * 1.3f) * 0.6f, Mathf.Sin(t * 0.8f) * 0.5f, 0f);

    // 연출 동안 플레이어 이동·잡기·조준점·마우스 시점 잠금 (CameraManagement.SetGameplayFrozen과 같은 목록, 빈대는 건드리지 않음)
    void SetLocked(bool locked)
    {
        foreach (var m in FindObjectsByType<PlayerMove>(FindObjectsSortMode.None)) m.enabled = !locked;
        foreach (var i in FindObjectsByType<PlayerInteraction>(FindObjectsSortMode.None)) i.enabled = !locked;
        foreach (var c in FindObjectsByType<Crosshair>(FindObjectsSortMode.None)) c.enabled = !locked;
        if (playerCam != null)
        {
            var axis = playerCam.GetComponent<CinemachineInputAxisController>();
            if (axis != null) axis.enabled = !locked;
        }
        // 누운 카메라 바로 옆에 서 있는 플레이어 몸(Visual)이 보이지 않게
        if (player != null)
            foreach (var r in player.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = locked;
    }

    void OnDisable()
    {
        if (running) { SetLocked(false); running = false; }   // 중간에 꺼져도 조작이 잠긴 채 남지 않게
    }

    static IEnumerator Wait(float s) { float t = 0f; while (t < s) { t += Time.deltaTime; yield return null; } }

    static IEnumerator Tween(float seconds, Action<float> step)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            step(Mathf.Clamp01(t / Mathf.Max(0.0001f, seconds)));
            yield return null;
        }
        step(1f);
    }

    static float Ease(float k) => k * k * (3f - 2f * k);
}
