using System.Collections.Generic;
using UnityEngine;

// 얼룩 신선도의 시계 + 일괄 갱신기. 씬마다 1개.
// - 게임: Time.deltaTime을 쌓아 시계를 돌린다 → 일시정지(timeScale 0) 중엔 저절로 멈춘다
// - 리플레이: ReplayManager가 재생 시각을 밀어 넣는다 → 배속에도 같은 모습이 재현된다
// IReplayPausable을 구현하지 않는다 — 리플레이에서도 얼룩 모습은 계속 갱신돼야 하기 때문
public class StainFreshnessManager : MonoBehaviour
{
    public static StainFreshnessManager Instance { get; private set; }

    // 살아 있는(켜진) 얼룩 명단. static인 이유: 씬 로드 때 얼룩의 OnEnable이 매니저의 Awake보다 먼저 올 수 있어서
    // 매니저가 없어도 등록할 곳이 필요하다
    private static readonly List<StainFreshness> stains = new List<StainFreshness>();

    [Tooltip("이 씬에서 쓸 단계·색 규칙. 맵마다 다른 에셋을 끼우면 맵별 프리셋")]
    [SerializeField] private StainFreshnessSettings settings;

    /// <summary>얼룩 시계의 현재 시각 (초). 씬 시작 = 0, 일시정지 중엔 멈춤</summary>
    public float Now { get; private set; }

    private bool driveByReplay;      // true면 스스로 시계를 돌리지 않고 ReplayManager가 넣어 주는 값을 쓴다

    // Enter Play Mode 옵션으로 도메인 리로드를 끄면 static이 지난 플레이 값을 들고 있다 — 시작할 때 비운다
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        stains.Clear();
        Instance = null;
    }

    // ===== 등록 =====

    public static void Register(StainFreshness stain)
    {
        stains.Add(stain);
        // 매니저가 이미 있으면 지금이 출생 시각. 없으면 매니저 Awake에서 한꺼번에 정한다
        if (Instance != null && !stain.HasBirthTime)
            stain.MarkCreated(Instance.Now);
    }

    public static void Unregister(StainFreshness stain)
    {
        stains.Remove(stain);
    }

    // ===== 생명주기 =====

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[{name}] StainFreshnessManager가 씬에 2개 이상입니다. 이 컴포넌트는 끕니다.", this);
            enabled = false;
            return;
        }
        Instance = this;
        Now = 0f;
        driveByReplay = GameManager.Instance != null && GameManager.Instance.isReplayMode;

        if (settings == null)
            Debug.LogWarning($"[{name}] StainFreshnessSettings가 비어 있어 얼룩 모습이 바뀌지 않습니다.", this);

        // 나보다 먼저 켜진 얼룩(씬에 미리 배치된 것)들의 출생 시각을 지금(0초)으로
        foreach (StainFreshness stain in stains)
        {
            if (!stain.HasBirthTime) stain.MarkCreated(Now);
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ===== 시계 =====

    void Update()
    {
        if (!driveByReplay)
            Now += Time.deltaTime;
    }

    /// <summary>리플레이 전용 — 재생 중인 시각을 그대로 얼룩 시계로 쓴다. ReplayManager가 매 프레임 호출</summary>
    public void SetReplayTime(float replayTime)
    {
        driveByReplay = true;
        Now = replayTime;
    }

    // ===== 갱신 =====

    // LateUpdate인 이유: 이번 프레임 Update에서 생긴 얼룩(인형·리플레이 이벤트)까지 화면에 그리기 전에 모습을 맞추려고
    void LateUpdate()
    {
        if (settings == null) return;
        for (int i = 0; i < stains.Count; i++)
        {
            stains[i].Apply(settings, Now);
        }
    }
}
