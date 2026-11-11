using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

// HUD 표시 전담 — 게임 상태는 GameProgressManager의 이벤트를 구독해 받는다
public class GameHUD : MonoBehaviour, IReplayPausable
{
    public static GameHUD Instance { get; private set; }

    [Header("UI")]
    [Tooltip("청소 진행도를 표시할 텍스트 (예: 0 / 10)")]
    [SerializeField] private TMP_Text stainCountText;
    [Tooltip("잡아야하는 인형 진행도를 표시할 텍스트 (예: 0 / 3)")]
    [SerializeField] private TMP_Text dollCountText;
    [Tooltip("플레이어가 상호작용 가능한 오브젝트를 바라볼 때 표시되는 프롬프트")]
    [SerializeField] private GameObject interactionPrompt;
    [Tooltip("화면 중앙 조준점 — 조준·오답 상태를 표시")]
    [SerializeField] private Crosshair crosshair;
    [Tooltip("남은 얼룩이 위험 수치에 근접할 때 표시되는 경고 문구 (예: 빨리 치워!)")]
    [SerializeField] private GameObject warningMessage;
    [Tooltip("문이 열렸을 때 표시할 안내 문구 (예: 문이 열렸다)")]
    [SerializeField] private GameObject doorOpenedMessage;

    [Header("Sound")]
    [Tooltip("잡기 오답 순간 재생할 효과음 — 같은 오브젝트의 AudioSource로 재생")]
    [SerializeField] private AudioClip catchFailedClip;
    [Tooltip("얼룩을 지운 순간 재생할 효과음")]
    [SerializeField] private AudioClip stainCleanedClip;
    [Tooltip("경고 알람(경고 문구 오브젝트의 AudioSource)이 다시 울리기까지 최소 간격(초). 경고가 빠르게 꺼졌다 켜져도 소리가 겹치지 않게 (2026-11-02)")]
    [SerializeField] private float alarmMinInterval = 8f;

    [Header("Replay")]
    [SerializeField] private Button replayExitButton;
    [SerializeField] private Button replay1XButton;
    [SerializeField] private Button replay2XButton;
    [SerializeField] private Button replay4XButton;
    [SerializeField] private GameObject replayFinishedText;
    [Tooltip("리플레이 재생이 끝났을 때 화면을 어둡게 깔아줄 페이더")]
    [SerializeField] private ScreenFader replayFinishedFader;
    [Tooltip("재생 종료 시 화면이 어두워지는 정도 (0 = 그대로, 1 = 완전 검정)")]
    [Range(0f, 1f)]
    [SerializeField] private float replayFinishedDimAlpha = 0.85f;
    [Tooltip("어두워지는 데 걸리는 시간(초)")]
    [SerializeField] private float replayFinishedDimDuration = 0.5f;

    private TMP_Text promptText;
    private ReplayManager replayManager;
    private AudioSource audioSource;
    private AudioSource warningAlarm;          // 경고 문구에 붙은 알람 소리 — 켜질 때 자동 재생 대신 여기서 직접 재생
    private float lastAlarmTime = float.NegativeInfinity;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        audioSource = GetComponent<AudioSource>();

        if (interactionPrompt != null)
            promptText = interactionPrompt.GetComponentInChildren<TMP_Text>();

        // 씬에선 Play On Awake라 문구가 켜질 때마다 울렸다 → 런타임에 끄고 OnWarningRaised에서만 재생 (씬 파일은 그대로)
        if (warningMessage != null)
        {
            warningAlarm = warningMessage.GetComponent<AudioSource>();
            if (warningAlarm != null)
                warningAlarm.playOnAwake = false;
        }

        if (replayFinishedText != null)
            replayFinishedText.SetActive(false);

        // 씬에 조준점을 따로 배치하지 않았으면 직접 만든다 (리플레이에선 조준을 안 하므로 생략)
        bool isReplay = GameManager.Instance != null && GameManager.Instance.isReplayMode;
        if (crosshair == null && !isReplay)
            crosshair = CreateCrosshair();

        // 리플레이 모드: 진행도 UI를 숨기고 재생 컨트롤로 교체.
        // 리스너는 컴포넌트가 꺼져도 동작하므로 Awake에서 미리 연결해 둔다 (ReplayManager가 곧 이 컴포넌트를 끔)
        if (GameManager.Instance != null && GameManager.Instance.isReplayMode)
        {
            stainCountText.gameObject.SetActive(false);
            dollCountText.gameObject.SetActive(false);
            replayExitButton.gameObject.SetActive(true);
            replay1XButton.gameObject.SetActive(true);
            replay2XButton.gameObject.SetActive(true);
            replay4XButton.gameObject.SetActive(true);

            replayExitButton.onClick.AddListener(() =>
            {
                SceneManager.LoadScene("ResultPopup");
            });
            replay1XButton.onClick.AddListener(() =>
            {
                ReplayManager.playbackSpeed = 1f;
            });
            replay2XButton.onClick.AddListener(() =>
            {
                ReplayManager.playbackSpeed = 2f;
            });
            replay4XButton.onClick.AddListener(() =>
            {
                ReplayManager.playbackSpeed = 4f;
            });
            
            replayManager = FindFirstObjectByType<ReplayManager>();
            if (replayManager != null)
                replayManager.OnReplayFinished += ShowReplayFinishedText;
        }
    }

    void Start()
    {
        if (doorOpenedMessage != null)
            doorOpenedMessage.SetActive(false);

        var progress = GameProgressManager.Instance;
        if (progress != null)
        {
            progress.OnProgressChanged += Refresh;
            progress.OnDoorOpened += ShowDoorOpenedMessage;
            progress.OnWarningRaised += PlayWarningAlarm;
            Refresh();
        }
    }
   

    void OnDestroy()
    {
        var progress = GameProgressManager.Instance;
        if (progress != null)
        {
            progress.OnProgressChanged -= Refresh;
            progress.OnDoorOpened -= ShowDoorOpenedMessage;
            progress.OnWarningRaised -= PlayWarningAlarm;
        }
        if (replayManager != null)
        {
            replayManager.OnReplayFinished -= ShowReplayFinishedText;
        }
    }

    private Crosshair CreateCrosshair()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null && interactionPrompt != null)
            canvas = interactionPrompt.GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return null;

        Transform root = canvas.rootCanvas.transform;
        var go = new GameObject("Crosshair", typeof(RectTransform));
        go.transform.SetParent(root, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(60f, 60f);

        // 설정창·페이드 같은 뒤쪽 패널에 가려지도록 프롬프트와 같은 층에 둔다
        int index = 0;
        if (interactionPrompt != null)
        {
            Transform t = interactionPrompt.transform;
            while (t.parent != null && t.parent != root)
                t = t.parent;
            if (t.parent == root)
                index = t.GetSiblingIndex();
        }
        go.transform.SetSiblingIndex(index);

        return go.AddComponent<Crosshair>();
    }

    // ===== 표시 갱신 =====

    private void Refresh()
    {
        var p = GameProgressManager.Instance;

        // 숫자 앞 그림 = TMP 인라인 스프라이트 (Resources/Sprite Assets/HudIcons, Tools > HUD > 개수 아이콘 만들기)
        if (stainCountText != null)
            stainCountText.text = $"<sprite=\"HudIcons\" name=\"Stain\"> {p.CleanedStains} / {p.TotalStains}";

        if (dollCountText != null)
            dollCountText.text = $"<sprite=\"HudIcons\" name=\"Doll\"> {p.CleanedDolls} / {p.TotalDolls}";

        if (warningMessage != null)
            warningMessage.SetActive(p.IsWarning);
    }

    

    // 경고가 새로 켜진 순간 (GameProgressManager가 히스테리시스로 판정) — 최소 간격 안이면 생략
    private void PlayWarningAlarm()
    {
        if (warningAlarm == null) return;
        if (Time.time - lastAlarmTime < alarmMinInterval) return;
        lastAlarmTime = Time.time;
        // 이 이벤트는 OnProgressChanged(Refresh)보다 먼저 온다 — 꺼진 오브젝트의 AudioSource는 재생되지 않으므로 먼저 켠다
        warningMessage.SetActive(true);
        warningAlarm.Play();
    }

    private void ShowDoorOpenedMessage()
    {
        if (doorOpenedMessage != null)
            doorOpenedMessage.SetActive(true);
    }

    private void ShowReplayFinishedText()
    {
        // 리플레이 중 이 컴포넌트는 비활성이라 코루틴을 못 돌린다 — 페이더가 직접 재생
        if (replayFinishedFader != null)
            replayFinishedFader.PlayFadeTo(replayFinishedDimAlpha, replayFinishedDimDuration);

        if (replayFinishedText != null)
            replayFinishedText.SetActive(true);
    }

    // ===== 프롬프트 =====

    public void ShowPrompt(string text)
    {
        if (crosshair != null)
            crosshair.SetAiming(true);

        if (interactionPrompt == null) return;

        interactionPrompt.SetActive(true);
        if (promptText != null)
            promptText.text = text;
    }

    public void HidePrompt()
    {
        if (crosshair != null)
            crosshair.SetAiming(false);

        if (interactionPrompt != null)
            interactionPrompt.SetActive(false);
    }

    public void PlayStainCleaned()
    {
        if (audioSource != null && stainCleanedClip != null)
            audioSource.PlayOneShot(stainCleanedClip);
    }

    public void ShowCatchFailed(float cooldown)
    {
        if (audioSource != null && catchFailedClip != null)
            audioSource.PlayOneShot(catchFailedClip);
        if (crosshair != null)
            crosshair.PlayWrong(cooldown);
    }
}
