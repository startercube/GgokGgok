using UnityEngine;
using UnityEngine.SceneManagement;

// 스테이지 클리어 기록 (2026-10-22, 메인화면 격리실 빈대 수에 씀)
// - 맵별로 "한 번이라도 깼다"만 저장한다 (bool). 같은 맵을 여러 번 깨도 한 번
// - 읽기·쓰기는 전부 여기로 → 나중에 세이브 파일로 바꿔도 이 클래스 안만 고치면 된다
// - 볼륨·감도처럼 PlayerPrefs를 쓴다. 키: StageCleared.<씬 이름>
public static class StageClearRecord
{
    const string Prefix = "StageCleared.";

    public static bool IsCleared(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return false;
        return PlayerPrefs.GetInt(Prefix + sceneName, 0) == 1;
    }

    /// <summary>처음 깬 거면 true (메인화면에 새 빈대가 들어오는 순간)</summary>
    public static bool MarkCleared(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || IsCleared(sceneName)) return false;
        PlayerPrefs.SetInt(Prefix + sceneName, 1);
        PlayerPrefs.Save();
        Debug.Log($"[클리어 기록] {sceneName} 첫 클리어");
        return true;
    }

    public static void Clear(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;
        PlayerPrefs.DeleteKey(Prefix + sceneName);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 모든 맵의 클리어 기록을 지운다 (전시회 초기화 버튼, 2026-10-24). 지운 기록 수를 돌려준다.
    /// PlayerPrefs는 키 목록을 주지 않으므로 빌드 설정의 씬 이름을 전부 훑는다 —
    /// 기록은 EndGame(true)가 "지금 씬 이름"으로 남기고, 빌드에서 불릴 수 있는 씬은 빌드 설정에 있는 것뿐이라 빠짐없다.
    /// 맵이 늘어도 빌드 설정에 넣으면 자동 포함. 저장은 부르는 쪽(GameReset)이 한 번에 한다
    /// </summary>
    public static int ClearAll()
    {
        int cleared = 0;
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i));
            if (!IsCleared(name)) continue;
            PlayerPrefs.DeleteKey(Prefix + name);
            cleared++;
        }
        return cleared;
    }
}
