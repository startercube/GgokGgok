using UnityEngine;
using Unity.Cinemachine;
using System.Collections;

// 카메라 전환 담당 — CinemachineBrain이 하나뿐이라 카메라를 갈아끼우지 않고 Priority 경쟁으로 전환한다
public class CameraManagement : MonoBehaviour, IReplayPausable
{
    private const int OverviewPriority = 20;
    private const int PlayerIdlePriority = 10;
    private const int PlayerActivePriority = 30;
    private const int ReplayPriority = 50;
    private const int DoorOpenPriority = 1;

    [Header("Cameras")]
    public CinemachineCamera overviewCam;  // 인트로 전경(달리)
    public CinemachineCamera playerCam;    // 1인칭 플레이
    public CinemachineCamera ReplayCam;    // CCTV 리플레이
    public CinemachineCamera doorCam;      // 문 열림 연출

    [Tooltip("인트로(한 바퀴 도는 데) 걸리는 시간")]
    public float introDuration = 20f;

    [Header("Transition")]
    [Tooltip("전환 시 화면을 덮는 검은 페이드. 비워두면 페이드 없이 즉시 컷 전환")]
    public ScreenFader fader;

    [Tooltip("페이드 아웃/인에 각각 걸리는 시간(초)")]
    public float fadeDuration = 0.4f;

    [Header("Map Intro")]
    [Tooltip("연결하면 인트로를 MapIntro가 진행한다 (2026-10-23, 인형방). 비우면 예전처럼 introDuration 뒤 페이드 전환 (할로윈 방)")]
    [SerializeField] private MapIntro mapIntro;

    [Tooltip("재시작으로 인트로를 건너뛸 때 검은 화면에서 플레이어 시점으로 걷히는 시간(초). 설계: 「코드 설계 - 재시작 시 인트로 생략」")]
    [SerializeField] private float restartFadeSeconds = 0.35f;

    public ScreenFader Fader => fader;
    public CinemachineCamera PlayerCam => playerCam;

    void Awake()
    {
        overviewCam.Priority = OverviewPriority;
        playerCam.Priority = PlayerIdlePriority;
        doorCam.Priority = DoorOpenPriority;
        if (GameManager.Instance != null && GameManager.Instance.isReplayMode)
        {
            ReplayCam.Priority = ReplayPriority;
        }
    }

    void Start()
    {
        // 재시작 표시는 리플레이 여부와 상관없이 먼저 읽어 끈다 (남아 있다가 다음 판에 쓰이지 않게)
        bool restart = StageRestart.ConsumeRestart(gameObject.scene.name);

        // 리플레이 모드는 ReplayManager가 게임 로직(IReplayPausable)을 통째로 끄므로 건드리지 않는다
        if (GameManager.Instance != null && GameManager.Instance.isReplayMode)
            return;

        SetGameplayFrozen(true);   // 인트로 동안 인형·플레이어 모두 정지

        // 같은 판을 다시 시작: 이미 본 인트로는 건너뛰고 바로 플레이어 시점 (MapIntro.Play를 부르지 않으면 인트로 화면 요소는 켜지지 않음)
        if (restart)
        {
            StartCoroutine(StartWithoutIntro());
            return;
        }

        // 새 맵 인트로가 있으면 진행을 맡긴다 — 끝날 때 MapIntro가 HandToPlayer·SetGameplayFrozen(false)를 부름
        if (mapIntro != null && mapIntro.isActiveAndEnabled)
        {
            mapIntro.Play(this);
            return;
        }
        StartCoroutine(SwitchAfterIntro());
    }

    // 인트로 후 화면을 덮고 컷으로 전환 — 두 카메라의 위치 차가 커서 blend는 멀미를 유발한다
    IEnumerator SwitchAfterIntro()
    {
        yield return new WaitForSeconds(introDuration);

        if (fader != null)
            yield return fader.FadeOut(fadeDuration);

        // 화면이 가려진 사이 컷으로 전환
        yield return HandToPlayer(null);

        if (fader != null)
            yield return fader.FadeIn(fadeDuration);

        SetGameplayFrozen(false);   // 플레이어 시점이 완전히 보이는 순간부터 게임 시작
        StageRestart.MarkIntroSeen(gameObject.scene.name);
    }

    // 재시작 판: 검은 화면 → 컷 → 짧은 페이드 인 → 조작 풀림
    // Start 안에서 바로 덮으므로 첫 화면부터 검다 → 전경 카메라가 한 프레임도 보이지 않음
    IEnumerator StartWithoutIntro()
    {
        if (fader != null)
            yield return fader.FadeOut(0f);   // 시간 0 = 즉시 덮기

        yield return HandToPlayer(null);

        if (fader != null)
            yield return fader.FadeIn(restartFadeSeconds);

        SetGameplayFrozen(false);
        StageRestart.MarkIntroSeen(gameObject.scene.name);   // 재시작 판에서 또 재시작해도 생략
    }

    /// <summary>
    /// 기본 blend를 한 프레임 Cut으로 바꿔 플레이어 카메라로 즉시 전환한다.
    /// disableCam: 같은 프레임에 끌 카메라 (MapIntro의 인트로 카메라 — 우선순위가 더 높아 끄지 않으면 계속 이김)
    /// </summary>
    public IEnumerator HandToPlayer(CinemachineCamera disableCam)
    {
        var brain = CinemachineBrain.ActiveBrainCount > 0 ? CinemachineBrain.GetActiveBrain(0) : null;
        if (brain != null)
        {
            var originalBlend = brain.DefaultBlend;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);

            playerCam.Priority = PlayerActivePriority;
            if (disableCam != null) disableCam.enabled = false;
            yield return null;   // 브레인이 컷 전환을 처리할 프레임 확보

            brain.DefaultBlend = originalBlend;
        }
        else
        {
            playerCam.Priority = PlayerActivePriority;
            if (disableCam != null) disableCam.enabled = false;
        }
    }

    // 인트로 동안 게임플레이 잠금. 인형만 컴포넌트를 끄지 않는다 — Start의 시작 배치는 인트로 중에도 돌아야 한다
    public void SetGameplayFrozen(bool frozen)
    {
        foreach (var doll in FindObjectsByType<DollMove>(FindObjectsSortMode.None))
            doll.SetFrozen(frozen);

        foreach (var move in FindObjectsByType<PlayerMove>(FindObjectsSortMode.None))
            move.enabled = !frozen;

        foreach (var interaction in FindObjectsByType<PlayerInteraction>(FindObjectsSortMode.None))
            interaction.enabled = !frozen;

        // 조준점은 플레이어 시점일 때만 보인다
        foreach (var crosshair in FindObjectsByType<Crosshair>(FindObjectsSortMode.None))
            crosshair.enabled = !frozen;

        // 인트로 중 마우스 입력이 플레이어 카메라 각도를 미리 돌려놓지 않도록 입력 축도 잠근다
        if (playerCam != null)
        {
            var axis = playerCam.GetComponent<CinemachineInputAxisController>();
            if (axis != null) axis.enabled = !frozen;
        }

        // 잠긴 동안은 리플레이도 찍지 않는다 (인트로가 리플레이 앞부분을 정지 화면으로 채우던 문제, 2026-10-24)
        if (ReplayRecorder.Instance != null)
            ReplayRecorder.Instance.SetHeld(frozen);
    }
}
