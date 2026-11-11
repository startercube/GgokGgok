using UnityEngine;

// 게임 도중 한 주임 이벤트 무전 (2026-10-24) — 판마다 한 번씩:
//   ① 첫 오답: 유쾌하게 놀리는 한 줄 (PlayerInteraction.OnWrongGuess)
//   ② 얼룩 경고: 빨리 치우라는 한 줄 (GameProgressManager.IsWarning이 꺼져 있다가 켜지는 순간 — HUD 경고 문구와 같은 기준)
// 대사는 맵 데이터(MapIntroScript)의 wrongGuessLine · stainWarningLine. 비워 두면 그 무전은 안 나옴
// 인트로가 끝나 조작이 풀린 뒤부터만, 리플레이에서는 안 나옴. 무전 박스가 말하는 중이면 대기열 뒤에 붙는다 (조작을 막지 않음)
// 설계 기록: 프로젝트 문서 「코드 설계 - 맵 인트로·무전」 10장
public class MapEventRadio : MonoBehaviour
{
    [SerializeField] private RadioBox radio;
    [SerializeField] private MapIntroScript script;
    [Tooltip("있으면 인트로가 끝나 조작이 풀린 뒤부터 무전을 낸다")]
    [SerializeField] private MapIntro intro;

    private bool wrongDone, stainDone, wasWarning;
    private GameProgressManager progress;

    void OnEnable() => PlayerInteraction.OnWrongGuess += HandleWrongGuess;
    void OnDisable() => PlayerInteraction.OnWrongGuess -= HandleWrongGuess;

    void Start()
    {
        if (GameManager.Instance != null && GameManager.Instance.isReplayMode) { enabled = false; return; }
        progress = GameProgressManager.Instance;
        if (progress != null)
        {
            wasWarning = progress.IsWarning;   // 시작부터 켜져 있는 경우는 세지 않음
            progress.OnProgressChanged += HandleProgress;
        }
    }

    void OnDestroy()
    {
        if (progress != null) progress.OnProgressChanged -= HandleProgress;
    }

    bool Ready => isActiveAndEnabled && radio != null && script != null && (intro == null || intro.HasReleased);

    void HandleWrongGuess()
    {
        if (wrongDone || !Ready) return;
        wrongDone = true;
        Say(script.wrongGuessLine);
    }

    void HandleProgress()
    {
        bool warning = progress != null && progress.IsWarning;
        bool risen = warning && !wasWarning;
        wasWarning = warning;
        if (!risen || stainDone || !Ready) return;
        stainDone = true;
        Say(script.stainWarningLine);
    }

    void Say(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        radio.SetCounter("");   // 인트로의 "n / 5" 줄 번호를 지움
        radio.Speaker = script.speaker;
        radio.Enqueue(line);
    }
}
