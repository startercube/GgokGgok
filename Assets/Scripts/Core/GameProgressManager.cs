using System;
using System.Collections;
using TMPro;
using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 진행도·승패 판정·게임 종료·일시정지. 표시는 하지 않고 이벤트로 GameHUD에 알린다
public class GameProgressManager : MonoBehaviour, IReplayPausable
{
    public static GameProgressManager Instance { get; private set; }

    [Header("Rules")]
    [Tooltip("이 개수 이상의 얼룩이 남으면 경고 켜짐")]
    [SerializeField] private int warningThreshold = 5;
    [Tooltip("경고가 켜진 뒤 이 개수 이하로 줄어야 꺼짐 (켜짐 기준보다 작아야 함). 경계에서 하나 닦고 하나 생기는 것을 반복해도 경고가 깜빡이지 않게 (히스테리시스, 2026-11-02)")]
    [SerializeField] private int warningClearThreshold = 3;
    [Tooltip("이 개수 이상의 얼룩이 남으면 패배")]
    [SerializeField] private int defeatThreshold = 7;
    [Tooltip("켜면 패배 순간 OnDefeat만 알리고 시간 정지·결과 화면은 하지 않는다 (튜토리얼 연습 판 재시작용, 2026-10-21). 기본 꺼짐")]
    [SerializeField] private bool externalDefeat = false;

    [Header("Victory")]
    [Tooltip("모든 목표를 달성하면 열리는 문")]
    [SerializeField] private DoorController door;
    [Tooltip("문 쪽 카메라로 넘어가기(포획 이펙트 짤림 방지)")]
    [SerializeField] private float doorOpenDelay = 3f;

    [Header("Result")]
    [Tooltip("게임 종료 시 화면을 덮는 흰색 연출 (승리/패배 공용)")]
    [SerializeField] private ScreenFader endFader;
    [SerializeField] private float endFadeDuration = 1.5f;

    [Header("Pause")]
    [SerializeField] private GameObject pauseMenu;
    [SerializeField] private Button btn_continue;
    [SerializeField] private Button btn_retry;
    [SerializeField] private Button btn_mainExit;

    [SerializeField] private GameObject settingPanel;

    public static bool isPaused = false;
    public static bool isActive = false;

    [SerializeField] private GameObject setBtn;

    // ===== 상태 =====
    public int TotalStains { get; private set; }
    public int CleanedStains { get; private set; }
    public int TotalDolls { get; private set; }
    public int CleanedDolls { get; private set; }
    public float PlayTime { get; private set; }
    public bool IsClear { get; private set; }
    public int RemainingStains => TotalStains - CleanedStains;
    public int DefeatThreshold => defeatThreshold;
    // 저장된 상태: 켜짐 기준(≥ warningThreshold)과 꺼짐 기준(≤ warningClearThreshold)이 달라 그 사이에선 이전 상태 유지
    public bool IsWarning { get; private set; }

    private bool isGameEnded = false;
    private bool isDoorOpened = false;

    // ===== 이벤트 (HUD가 구독) =====
    public event Action OnProgressChanged;   // 카운트가 바뀔 때마다
    public event Action OnDoorOpened;        // 승리 조건 달성, 문 열림
    public event Action OnDefeat;            // 패배 (externalDefeat일 때 처리는 구독자가)
    public event Action OnWarningRaised;     // 경고가 꺼져 있다가 켜지는 순간만 (알람용). 시작 시점 상태는 알리지 않음

    void OnValidate()
    {
        if (warningThreshold < 1) warningThreshold = 1;
        warningClearThreshold = Mathf.Clamp(warningClearThreshold, 0, warningThreshold - 1);
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        btn_continue.onClick.AddListener(() =>
        {
            Debug.Log("계속하기");
            isPaused = false;
            pauseMenu.SetActive(false);   // ← 아래 문제 2 참고
            Time.timeScale = 1;
            Cursor.visible = false;
        });

        btn_retry.onClick.AddListener(() =>
        {
            Debug.Log("재시작");
            Time.timeScale = 1; // 씬 넘어가기 전 반드시 복구
            // 이미 본 맵 인트로는 건너뛰고 다시 시작 (「코드 설계 - 재시작 시 인트로 생략」)
            StageRestart.Reload(GameManager.Instance != null ? GameManager.Instance.sceneName : SceneManager.GetActiveScene().name);
        });

        btn_mainExit.onClick.AddListener(() =>
        {
            Debug.Log("메인씬");
            Time.timeScale = 1;
            SceneManager.LoadScene("MainScene");
        });
    }

    // static 이벤트라 반드시 쌍으로 해제 — 안 하면 씬 재로드 후 파괴된 구독자가 남아 이중 카운트
    void OnEnable()  { DollStainSpawner.OnStainSpawned += HandleStainSpawned; }
    void OnDisable() { DollStainSpawner.OnStainSpawned -= HandleStainSpawned; }

    private void HandleStainSpawned(GameObject stain) => AddStain();

    void Start()
    {
        TotalStains = GameObject.FindGameObjectsWithTag("Stain").Length;
        // 잡는 대상이 아닌 개체(튜토리얼 격리실 본모습 등, CountsTowardGoal 꺼짐)는 빼고 센다
        TotalDolls = System.Array.FindAll(FindObjectsByType<DollMove>(FindObjectsSortMode.None), d => d.CountsTowardGoal).Length;
        PlayTime = 0;
        UpdateWarning(false);   // 시작부터 켜져 있어도 알람·무전은 내지 않음
        OnProgressChanged?.Invoke();
        pauseMenu.SetActive(false);
    }

    void Update()
    {

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Pause();
            settingPanel.SetActive(false);

        }

        PlayTime += Time.deltaTime;
    }

    // ===== 진행도 갱신 =====

    // 경고 상태 갱신 — OnProgressChanged보다 먼저 불러야 구독자(HUD·무전)가 새 상태를 읽는다
    private void UpdateWarning(bool notify)
    {
        int remaining = RemainingStains;
        if (!IsWarning && remaining >= warningThreshold)
        {
            IsWarning = true;
            if (notify) OnWarningRaised?.Invoke();
        }
        else if (IsWarning && remaining <= warningClearThreshold)
        {
            IsWarning = false;
        }
    }

    public void AddStain()
    {
        TotalStains++;
        UpdateWarning(true);
        OnProgressChanged?.Invoke();
        if (RemainingStains >= defeatThreshold)
            TriggerDefeat();
    }

    public void CleanStain()
    {
        CleanedStains++;
        UpdateWarning(true);
        OnProgressChanged?.Invoke();
        CheckVictory();
    }

    public void CatchDoll()
    {
        CleanedDolls++;
        OnProgressChanged?.Invoke();
        CheckVictory();
    }

    // ===== 승패 판정 =====

    private void CheckVictory()
    {
        if (CleanedStains >= TotalStains && CleanedDolls >= TotalDolls)
            TriggerVictory();
    }

    private void TriggerVictory()
    {
        if (isDoorOpened) return;
        isDoorOpened = true;

        Invoke(nameof(OpenDoor), doorOpenDelay);
    }

    // 클리어 직후 doorOpenDelay만큼 기다렸다가 문을 연다 (문 카메라 전환도 이때)
    private void OpenDoor()
    {
        if (door != null)
        {
            door.Open();
            if (ReplayRecorder.Instance != null)
                ReplayRecorder.Instance.RecordDoorOpen();
        }

        OnDoorOpened?.Invoke();
    }

    private bool isDefeated = false;

    private void TriggerDefeat()
    {
        if (externalDefeat)
        {
            if (isDefeated) return;
            isDefeated = true;
            OnDefeat?.Invoke();
            return;
        }
        OnDefeat?.Invoke();
        Time.timeScale = 0f;
        EndGame(false);
    }

    // ===== 게임 종료 → 결과 화면 =====

    public void EndGame(bool isClear)
    {
        if (isGameEnded) return;
        isGameEnded = true;
        IsClear = isClear;

        // 메인화면 격리실 빈대 수용 기록 (2026-10-22). 리플레이로 다시 보는 중엔 기록하지 않는다
        if (isClear && (GameManager.Instance == null || !GameManager.Instance.isReplayMode))
            StageClearRecord.MarkCleared(SceneManager.GetActiveScene().name);

        // 스테이지 씬을 바로 실행하면 GameManager(MainScene 전용)가 없다 — 결과 전달만 건너뛴다
        if (GameManager.Instance != null)
        {
            GameManager.Instance.isClear = isClear;
            GameManager.Instance.totalStains = TotalStains;
            GameManager.Instance.cleanedStains = CleanedStains;
            GameManager.Instance.playTime = PlayTime;
        }

        if (ReplayRecorder.Instance != null)
            ReplayRecorder.Instance.FinishRecording();

        StartCoroutine(EndGameRoutine());
    }

    private IEnumerator EndGameRoutine()
    {
        if (endFader != null)
            yield return endFader.FadeOut(endFadeDuration);

        GoToResult();
    }

    public void GoToResult()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("ResultPopup");

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Pause()
    {
        isPaused = !isPaused;
        isActive = !isActive;
        pauseMenu.SetActive(isPaused);

        Time.timeScale = isPaused ? 0 : 1;
        Cursor.visible = isPaused;
        Cursor.lockState = isPaused ? CursorLockMode.None : CursorLockMode.Locked;

        setBtn.SetActive(isPaused);

    }



}
