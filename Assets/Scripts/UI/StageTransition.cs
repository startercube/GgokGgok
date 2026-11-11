using UnityEngine;
using UnityEngine.SceneManagement;

// 스테이지 카드 버튼의 OnClick에 연결
public class StageTransition : MonoBehaviour
{
    public void LoadScene(string sceneName)
    {
        // 다시하기·재시작·CCTV가 재로드할 씬을 여기서 정한다
        GameManager.Instance.sceneName = sceneName;
        SceneManager.LoadScene(sceneName);
    }

}

