using UnityEngine;


// 플레이어가 가까이 오면 웃음소리 — 인형 위치에서 들리는 것 자체가 힌트다
// 2026-11-02 「코드 설계 - 위화감(시간이 멈춘 사물)·웃음소리 방향」:
//  · 굳어 있으면(위험 지대 안 = DollMove가 멈춘 상태) 못 웃는다 → 웃음 = "지금 네 시야 밖에 있다"는 소거 단서.
//    보고 있는 동안은 참고(재장전 유지), 고개를 돌리는 순간 등 뒤에서 키득
//  · 개체 쿨다운 + 모든 빈대 공유 간격 → 너무 자주 웃지 않게 (테스터 "계속 웃는다")
//  · 등 뒤면 살짝, 벽 너머면 많이 먹먹하게 → Unity 기본 3D 소리가 못 주는 앞뒤·같은 방 여부를 소리 질감으로
[RequireComponent(typeof(AudioSource))]
public class DollLaugh : MonoBehaviour, IReplayPausable
{
    const float OpenCutoff = 22000f;   // 저역 통과 필터를 사실상 연 값 (가청 상한)

    [Header("Audio")]
    [Tooltip("재생할 웃음 효과음")]
    [SerializeField] private AudioClip laughClip;
    [Tooltip("추가 웃음 변주 (비워도 됨). 기본 클립과 섞어서 무작위로 고른다")]
    [SerializeField] private AudioClip[] extraClips;
    [Tooltip("웃을 때마다 음높이를 ±이 비율만큼 흔든다 (같은 소리 반복 피로 방지)")]
    [SerializeField, Range(0f, 0.2f)] private float pitchJitter = 0.06f;
    [Tooltip("플레이어가 이 거리 안으로 들어오면 웃는다 (미터)")]
    [SerializeField] private float laughDistance = 3.5f;
    [Tooltip("한 번 웃은 뒤 이 거리 밖으로 나가야 다시 웃는다. laughDistance보다 크게 둘 것 (경계선에서 연속 재생 방지)")]
    [SerializeField] private float rearmDistance = 4.5f;
    [Tooltip("체크를 끄면 한 판에 딱 한 번만 웃는다")]
    [SerializeField] private bool repeatable = true;

    [Header("Thriller")]
    [Tooltip("굳어 있는 동안(위험 지대 안)은 웃지 않고 참는다. 고개를 돌리면 그때 웃는다")]
    [SerializeField] private bool onlyWhenUnseen = true;
    [Tooltip("이 빈대가 한 번 웃은 뒤 다시 웃기까지 최소 초")]
    [SerializeField, Min(0f)] private float personalCooldown = 8f;
    [Tooltip("모든 빈대가 공유하는 웃음 사이 최소 간격 (여러 마리가 연달아 웃는 소음 방지)")]
    [SerializeField, Min(0f)] private float globalGap = 3f;

    [Header("Direction Cues")]
    [Tooltip("등 뒤·벽 너머를 먹먹함으로 구분한다 (같은 오브젝트에 AudioLowPassFilter를 붙인다)")]
    [SerializeField] private bool directionCues = true;
    [Tooltip("정확히 등 뒤일 때의 고음 차단 주파수(Hz). 낮을수록 더 먹먹")]
    [SerializeField, Range(500f, 22000f)] private float behindCutoff = 3000f;
    [Tooltip("벽·가구에 가려졌을 때의 고음 차단 주파수(Hz)")]
    [SerializeField, Range(200f, 22000f)] private float occludedCutoff = 900f;
    [Tooltip("벽·가구에 가려졌을 때 소리 크기 배율")]
    [SerializeField, Range(0f, 1f)] private float occludedVolume = 0.7f;
    [Tooltip("재생 중 방향이 바뀔 때 먹먹함이 따라가는 빠르기")]
    [SerializeField, Min(0.1f)] private float filterSmoothing = 10f;

    // 모든 빈대가 공유하는 마지막 웃음 시각 — 도메인 리로드가 꺼져 있어도 플레이마다 초기화
    private static float lastAnyLaughTime = float.NegativeInfinity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSharedState() => lastAnyLaughTime = float.NegativeInfinity;

    private AudioSource audioSource;
    private AudioLowPassFilter lowPass;
    private DollVision vision;
    private Transform playerTransform;
    private Transform listener;
    private bool hasLaughed = false;   // 이번 접근에서 이미 웃었는지
    private float lastLaughTime = float.NegativeInfinity;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[8];

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;   // 인형 위치에서 들리도록 3D 고정
        audioSource.dopplerLevel = 0f;   // 움직이는 빈대의 음높이 흔들림이 방향 단서를 흐리지 않게

        vision = GetComponent<DollVision>();

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            playerTransform = player.transform;
        else
            Debug.LogWarning("Player object not found. DollLaugh will not play.");

        if (directionCues)
        {
            lowPass = GetComponent<AudioLowPassFilter>();
            if (lowPass == null) lowPass = gameObject.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = OpenCutoff;
        }
    }

    void OnValidate()
    {
        if (rearmDistance < laughDistance) rearmDistance = laughDistance;
    }

    void Update()
    {
        if (playerTransform == null || !HasAnyClip()) return;

        float sqrDistance = (playerTransform.position - transform.position).sqrMagnitude;   // Sqrt 생략

        if (!hasLaughed)
        {
            // 반경 안이어도 조건이 안 맞으면 '참는다' — 재장전 상태를 유지해 조건이 풀리는 순간 웃는다
            if (sqrDistance <= laughDistance * laughDistance && CanLaughNow())
            {
                hasLaughed = true;
                Laugh();
            }
        }
        else if (repeatable && sqrDistance > rearmDistance * rearmDistance)
        {
            hasLaughed = false;   // 충분히 멀어졌으니 다음 접근을 위해 재장전
        }

        if (lowPass != null && audioSource.isPlaying)
            UpdateDirectionFilter(false);
    }

    // 싼 검사부터 — 위험 지대 판정(레이캐스트)은 마지막에
    bool CanLaughNow()
    {
        float now = Time.time;
        if (now - lastLaughTime < personalCooldown) return false;
        if (now - lastAnyLaughTime < globalGap) return false;
        if (onlyWhenUnseen && vision != null && vision.IsInDangerZone()) return false;   // 굳어 있다 = 참는다
        return true;
    }

    void Laugh()
    {
        lastLaughTime = Time.time;
        lastAnyLaughTime = Time.time;

        audioSource.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);

        float volume = 1f;
        if (lowPass != null && UpdateDirectionFilter(true))
            volume = occludedVolume;   // 벽 너머 = 작게. PlayOneShot 배율이라 다른 설정은 건드리지 않는다

        audioSource.PlayOneShot(PickClip(), volume);
    }

    bool HasAnyClip()
    {
        if (laughClip != null) return true;
        if (extraClips != null)
            foreach (AudioClip c in extraClips)
                if (c != null) return true;
        return false;
    }

    AudioClip PickClip()
    {
        int extraCount = extraClips != null ? extraClips.Length : 0;
        if (extraCount == 0) return laughClip;

        // 0 = 기본 클립, 1.. = 변주. 빈 칸이 걸리면 다음 칸으로
        int start = Random.Range(0, extraCount + 1);
        for (int i = 0; i <= extraCount; i++)
        {
            int index = (start + i) % (extraCount + 1);
            AudioClip c = index == 0 ? laughClip : extraClips[index - 1];
            if (c != null) return c;
        }
        return laughClip;
    }

    // 먹먹함을 계산해 필터에 넣는다. snap이면 즉시, 아니면 부드럽게. 반환값 = 가려졌는지
    bool UpdateDirectionFilter(bool snap)
    {
        if (listener == null || !listener.gameObject.activeInHierarchy) ResolveListener();
        if (listener == null) return false;

        Vector3 soundPoint = transform.position + Vector3.up * (vision != null ? vision.eyeHeightOffset : 0.5f);
        Vector3 listenerPos = listener.position;

        // 앞뒤 — 수평면에서 비교. 정면·옆 = 0, 정확히 등 뒤 = 1
        Vector3 forward = Vector3.ProjectOnPlane(listener.forward, Vector3.up);
        Vector3 toSound = Vector3.ProjectOnPlane(soundPoint - listenerPos, Vector3.up);
        float behind = 0f;
        if (forward.sqrMagnitude > 1e-4f && toSound.sqrMagnitude > 1e-4f)
            behind = Mathf.Clamp01(-Vector3.Dot(forward.normalized, toSound.normalized));

        float target = LogLerp(OpenCutoff, behindCutoff, behind);

        bool occluded = IsOccluded(soundPoint, listenerPos);
        if (occluded) target = Mathf.Min(target, occludedCutoff);

        float current = lowPass.cutoffFrequency;
        float k = snap ? 1f : 1f - Mathf.Exp(-filterSmoothing * Time.deltaTime);
        lowPass.cutoffFrequency = LogLerp(current, target, k);
        return occluded;
    }

    // 사람 귀는 주파수를 로그로 느낀다 → 22000→900을 그냥 직선으로 섞으면 끝에서만 갑자기 먹먹해진다
    static float LogLerp(float a, float b, float t)
        => Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(a, 1f)), Mathf.Log(Mathf.Max(b, 1f)), t));

    // 소리 → 청취자 사이에 벽·가구가 있는지. 자기 몸과 플레이어 몸(카메라를 감싼 캡슐)은 무시
    bool IsOccluded(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float dist = delta.magnitude;
        if (dist < 0.01f) return false;

        int mask = vision != null ? (int)vision.obstructionMask : Physics.DefaultRaycastLayers;
        int count = Physics.RaycastNonAlloc(from, delta / dist, hitBuffer, dist, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform hit = hitBuffer[i].transform;
            if (hit == transform || hit.IsChildOf(transform)) continue;
            if (playerTransform != null && (hit == playerTransform || hit.IsChildOf(playerTransform))) continue;
            if (hit == listener || hit.IsChildOf(listener)) continue;
            return true;
        }
        return false;
    }

    // 켜져 있는 AudioListener (인트로·리플레이 카메라 전환 뒤에도 지금 듣는 쪽을 찾는다)
    void ResolveListener()
    {
        listener = null;
        foreach (AudioListener al in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            if (al.enabled && al.gameObject.activeInHierarchy)
            {
                listener = al.transform;
                return;
            }
        }
        if (Camera.main != null) listener = Camera.main.transform;
    }

    // 실제 판정 거리 그대로 그린다 (2026-11-02 이전에는 절반 크기로 그려져 실제보다 작아 보였음)
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, laughDistance);
        Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, rearmDistance);
    }
}
