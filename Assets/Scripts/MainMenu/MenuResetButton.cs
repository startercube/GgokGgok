using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 메인화면 전시회용 초기화 버튼 (2026-10-24). 설계: 프로젝트 문서 「코드 설계 - 전시회 초기화 버튼」
// 실수 방지 = 두 번 누르기: 한 번 누르면 "한 번 더 누르면 초기화 (3)" → 제한 시간 안에 다시 누르면 GameReset → 메인 씬 다시 불러오기
// 씬을 다시 부르는 이유: 빈대 수·수용 개체 HUD·설정 슬라이더가 전부 씬 시작 때 저장값을 읽으므로, 다시 부르면 처음 상태가 보장된다
// 클릭은 자기 Button에 런타임으로 연결 → 버튼을 손으로 복제·이동해도 연결이 따라감 (MainMenuManager 수정 없음)
[RequireComponent(typeof(Button))]
public class MenuResetButton : MonoBehaviour
{
    [Header("연결 (배치 도구가 채움)")]
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;
    [Tooltip("확인 대기 중 색을 바꿀 배경 (없어도 됨)")]
    [SerializeField] private Image background;

    [Header("지울 것")]
    [SerializeField] private bool clearRecords = true;
    [Tooltip("마우스 감도·볼륨을 기본값으로. 다음 관람객이 앞사람 설정에서 시작하지 않게 (제안 값)")]
    [SerializeField] private bool resetSettings = true;

    [Header("시간 (초, timeScale 영향 없음)")]
    [SerializeField, Min(0.5f)] private float confirmWindow = 3f;
    [SerializeField, Min(0f)] private float doneShowTime = 2f;

    [Header("글자")]
    [SerializeField] private string idleText = "게임 초기화";
    [Tooltip("{0} = 남은 초")]
    [SerializeField] private string armedText = "한 번 더 누르면 초기화 ({0})";
    [SerializeField] private string doneText = "초기화 완료";
    [SerializeField] private Color idleColor = new Color(0.75f, 0.88f, 0.79f);
    [SerializeField] private Color armedColor = new Color(1f, 0.35f, 0.35f);
    [SerializeField] private Color armedBackground = new Color(1f, 0.23f, 0.23f, 0.22f);

    // 씬을 다시 불러오면 이 컴포넌트도 새로 생기므로 "방금 초기화했다"는 정적 값으로 넘긴다.
    // 새 씬의 OnEnable이 읽는 즉시 끔 → 다른 길로 메인에 와도 완료 표시가 나오지 않는다 (TutorialRetry.pendingRetry와 같은 방식)
    static bool justReset;

    enum State { Idle, Armed, Done, Loading }
    State state;
    float stateUntil;     // Armed·Done이 끝나는 시각 (unscaled)
    int shownSeconds = -1;
    Color idleBackground;

    void Reset() { button = GetComponent<Button>(); label = GetComponentInChildren<TMP_Text>(); background = GetComponent<Image>(); }

    void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (background != null) idleBackground = background.color;

        // 메뉴 버튼 사이를 방향키·패드로 오가다 우연히 여기 닿지 않게 → 마우스 클릭으로만 누름
        var nav = button.navigation; nav.mode = Navigation.Mode.None; button.navigation = nav;
        button.onClick.AddListener(OnClick);
    }

    void OnDestroy() { if (button != null) button.onClick.RemoveListener(OnClick); }

    void OnEnable()
    {
        if (justReset)
        {
            justReset = false;
            Enter(State.Done);
        }
        else Enter(State.Idle);
    }

    void OnDisable()
    {
        if (state == State.Armed) Enter(State.Idle);   // 확인 대기 상태로 꺼졌다 켜지지 않게
    }

    void Update()
    {
        switch (state)
        {
            case State.Armed:
                float left = stateUntil - Time.unscaledTime;
                if (left <= 0f) { Enter(State.Idle); break; }
                int sec = Mathf.CeilToInt(left);
                if (sec != shownSeconds) { shownSeconds = sec; SetLabel(string.Format(armedText, sec), armedColor); }
                break;
            case State.Done:
                if (Time.unscaledTime >= stateUntil) Enter(State.Idle);
                break;
        }
    }

    void OnClick()
    {
        switch (state)
        {
            case State.Idle:
            case State.Done:
                Enter(State.Armed);
                break;
            case State.Armed:
                Confirm();
                break;
        }
    }

    void Confirm()
    {
        Enter(State.Loading);
        GameReset.ResetAll(clearRecords, resetSettings);
        justReset = true;
        StartCoroutine(Reload());
    }

    // 한 프레임 뒤에 부름 → 클릭 이벤트 처리 도중 씬이 바뀌지 않게
    IEnumerator Reload()
    {
        yield return null;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void Enter(State next)
    {
        state = next;
        shownSeconds = -1;
        if (button != null) button.interactable = next != State.Loading;   // 다시 부르는 동안 연타 방지
        if (background != null) background.color = next == State.Armed ? armedBackground : idleBackground;

        switch (next)
        {
            case State.Idle:
                SetLabel(idleText, idleColor);
                break;
            case State.Armed:
                stateUntil = Time.unscaledTime + confirmWindow;
                shownSeconds = Mathf.CeilToInt(confirmWindow);   // 이후 초가 바뀔 때마다 Update가 다시 씀
                SetLabel(string.Format(armedText, shownSeconds), armedColor);
                break;
            case State.Done:
                stateUntil = Time.unscaledTime + doneShowTime;
                SetLabel(doneText, idleColor);
                break;
        }
    }

    void SetLabel(string text, Color color)
    {
        if (label == null) return;
        label.text = text;
        label.color = color;
    }
}
