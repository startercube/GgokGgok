using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 스테이지 재시작 표시 (2026-11-01). 설계: 프로젝트 문서 「코드 설계 - 재시작 시 인트로 생략」
// - 재시작 버튼(일시정지 "재시작", 결과 "다시하기")이 Reload로 표시를 켜고 씬을 부른다
// - 새 씬의 CameraManagement.Start가 ConsumeRestart로 읽자마자 끈다 → 맵 인트로 대신 바로 시작
// - 씬을 다시 부르면 씬 안 컴포넌트는 새로 만들어져 기억이 없으므로 정적 값에 둔다 (TutorialRetry.pendingRetry와 같은 방식)
// - 이번 실행에서 인트로를 끝까지 본 맵만 생략 → 처음 맵은 인트로 도중 재시작해도 다시 보여 줌 ("처음엔 건너뛰기 불가" 확정 규칙의 의도)
public static class StageRestart
{
    private static readonly HashSet<string> introSeen = new HashSet<string>();
    private static string pendingScene;

    // 에디터 "도메인 다시 불러오기 끄기"에서도 지난 플레이의 기록이 남지 않게
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ClearAll();

    /// <summary>같은 스테이지를 다시 불러온다. 이번 실행에서 그 맵의 인트로를 봤으면 새 판은 인트로 없이 시작</summary>
    public static void Reload(string scene)
    {
        if (string.IsNullOrEmpty(scene)) scene = SceneManager.GetActiveScene().name;
        pendingScene = HasSeenIntro(scene) ? scene : null;
        Time.timeScale = 1f;   // 일시정지 중에 눌렸을 수 있음
        SceneManager.LoadScene(scene);
    }

    /// <summary>이 씬이 재시작으로 불렸는지. 결과와 상관없이 표시를 끈다 (한 번의 재시작이 다음 판으로 번지지 않게)</summary>
    public static bool ConsumeRestart(string scene)
    {
        bool hit = pendingScene != null && pendingScene == scene;
        pendingScene = null;
        return hit;
    }

    /// <summary>이 맵의 인트로를 끝까지 봤다고 기록 (조작이 풀리는 순간)</summary>
    public static void MarkIntroSeen(string scene)
    {
        if (!string.IsNullOrEmpty(scene)) introSeen.Add(scene);
    }

    public static void MarkIntroSeen() => MarkIntroSeen(SceneManager.GetActiveScene().name);

    public static bool HasSeenIntro(string scene) => !string.IsNullOrEmpty(scene) && introSeen.Contains(scene);

    /// <summary>표시·기록 모두 지움 (전시회 초기화 버튼 → 다음 관람객은 처음부터)</summary>
    public static void ClearAll()
    {
        introSeen.Clear();
        pendingScene = null;
    }
}
