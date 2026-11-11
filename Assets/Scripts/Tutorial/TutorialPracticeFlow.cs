using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// 튜토리얼 7번 연습 한 판의 흐름 (2026-10-18)
// 입장(TutorialZoneActivator) → 입구 문 닫힘 + 얼룩·빈대 개수 표시 → 승리(GameProgressManager가 같은 문을 다시 엶)
// → 들어온 문으로 나가 ㄱ자 복도에 들어서면 다음 씬(인형 마니아 방)으로
// 7-4 마무리 대사는 대사 재생이 생기면 이 복도 구간에서 재생하고, 끝난 뒤 넘어가게 바꾼다
// 패배 (2026-10-21): GameProgressManager.externalDefeat로 결과 화면 대신 OnDefeated → 멈춤 → (진행 관리가 "하...." 뒤) Retry
//   → 검은 페이드 → 씬 다시 불러오기 + 체크포인트(TutorialRetry): 연습 구역 입구 문 앞에서 다시
public class TutorialPracticeFlow : MonoBehaviour
{
    [SerializeField] private TutorialZoneActivator activator;
    [Tooltip("연습 구역 입구 문. 입장하면 닫히고, 승리하면 GameProgressManager가 다시 연다")]
    [SerializeField] private DoorController entranceDoor;
    [Tooltip("입장할 때 켤 HUD (얼룩·빈대 개수 = 게이지)")]
    [SerializeField] private GameObject[] revealOnEnter = new GameObject[0];

    [Header("Exit")]
    [Tooltip("승리 후 플레이어가 이 상자(월드 좌표) 안에 들어오면 튜토리얼 끝")]
    [SerializeField] private Bounds exitZone = new Bounds(Vector3.zero, Vector3.one);
    [SerializeField] private string nextScene = "DollManiaHouse";
    [SerializeField] private ScreenFader fader;
    [SerializeField] private float fadeDuration = 1f;
    [Tooltip("켜면 개수 표시와 퇴장을 튜토리얼 진행 관리(TutorialDirector)가 부른다 — 7-2 첫 얼룩 때 표시, 7-4 대사가 끝나면 퇴장")]
    [SerializeField] private bool directorDriven = false;

    [Header("Defeat")]
    [Tooltip("진행 관리 없이 쓸 때: 패배 후 이 시간 뒤 스스로 재시작")]
    [SerializeField] private float retryDelayWithoutDirector = 2f;

    /// <summary>연습 판 패배 (멈춘 뒤) — 진행 관리가 7-3 실패 대사에 쓴다</summary>
    public event System.Action OnDefeated;

    private Transform player;
    private bool won, leaving, defeated;

    void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;

        if (activator != null) activator.OnActivated += HandleEnter;
        if (GameProgressManager.Instance != null)
        {
            GameProgressManager.Instance.OnDoorOpened += HandleWin;
            GameProgressManager.Instance.OnDefeat += HandleDefeat;
        }
    }

    void OnDestroy()
    {
        if (activator != null) activator.OnActivated -= HandleEnter;
        if (GameProgressManager.Instance != null)
        {
            GameProgressManager.Instance.OnDoorOpened -= HandleWin;
            GameProgressManager.Instance.OnDefeat -= HandleDefeat;
        }
    }

    // 패배: 빈대와 플레이어를 멈춘다 (얼룩이 더 쌓이지 않게, 실패가 분명히 보이게)
    void HandleDefeat()
    {
        if (defeated || won) return;
        defeated = true;
        foreach (var d in FindObjectsByType<DollMove>(FindObjectsSortMode.None)) d.SetFrozen(true);
        foreach (var m in FindObjectsByType<PlayerMove>(FindObjectsSortMode.None)) m.enabled = false;
        foreach (var i in FindObjectsByType<PlayerInteraction>(FindObjectsSortMode.None)) i.enabled = false;
        OnDefeated?.Invoke();
        if (!directorDriven) StartCoroutine(RetryAfter(retryDelayWithoutDirector));
    }

    IEnumerator RetryAfter(float s) { yield return new WaitForSeconds(s); Retry(null); }

    /// <summary>검은 페이드 → 연습 구역 입구 문 앞에서 다시 (7-3 실패 대사가 끝난 뒤 진행 관리가 부름)</summary>
    public void Retry(System.Collections.Generic.IEnumerable<string> firedEventIds)
    {
        if (leaving) return;
        leaving = true;
        StartCoroutine(RetryRoutine(firedEventIds));
    }

    IEnumerator RetryRoutine(System.Collections.Generic.IEnumerable<string> firedEventIds)
    {
        if (fader != null) yield return fader.FadeOut(fadeDuration);
        Time.timeScale = 1f;
        TutorialRetry.Request(firedEventIds);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void HandleEnter()
    {
        if (entranceDoor != null) entranceDoor.Close();
        RevealHud();   // 연습 구역에 들어서면 바로 개수 표시 (2026-10-21 사용자 요청, 원래는 7-2 첫 얼룩 때)
    }

    /// <summary>얼룩·빈대 개수 표시를 켠다 (연습 구역 입장, 재시작 판)</summary>
    public void RevealHud()
    {
        foreach (var go in revealOnEnter) if (go != null) go.SetActive(true);
    }

    /// <summary>튜토리얼을 끝내고 다음 씬으로 (7-4 대사가 끝난 뒤)</summary>
    public void Leave()
    {
        if (leaving) return;
        leaving = true;
        StartCoroutine(LeaveRoutine());
    }

    void HandleWin() { if (!defeated) won = true; }

    void Update()
    {
        if (directorDriven || !won || leaving || player == null || !exitZone.Contains(player.position)) return;
        Leave();
    }

    IEnumerator LeaveRoutine()
    {
        if (fader != null) yield return fader.FadeOut(fadeDuration);
        Time.timeScale = 1f;
        // 다시하기·CCTV가 재로드할 씬. 튜토리얼 씬을 바로 실행하면 GameManager가 없다
        if (GameManager.Instance != null) GameManager.Instance.sceneName = nextScene;
        SceneManager.LoadScene(nextScene);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.6f);
        Gizmos.DrawWireCube(exitZone.center, exitZone.size);
    }
}
