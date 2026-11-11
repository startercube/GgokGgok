using UnityEngine;

// 메인 메뉴 팝업 토글 — 버튼 OnClick에 인스펙터로 연결해 쓴다
public class MainMenuManager : MonoBehaviour
{
    [Header("스테이지 선택 팝업")]
    [Tooltip("스테이지 목록을 보여줄 패널(Panel) 오브젝트를 연결하세요")]
    [SerializeField] private GameObject stageSelectPanel;

    [Header("조작 방법 팝업")]
    [Tooltip("조작 방법을 보여줄 패널(Panel) 오브젝트를 연결하세요")]
    [SerializeField] private GameObject howToPlayPanel;

    [Header("설정 팝업")]
    [Tooltip("설정 화면을 보여줄 패널(Panel) 오브젝트를 연결하세요")]
    [SerializeField] private GameObject settingsPanel;

    [Header("크레딧 팝업 (2026-10-22, 조작 방법 대신)")]
    [Tooltip("크레딧을 보여줄 패널. 메인화면 배치 도구가 만들어 연결한다")]
    [SerializeField] private GameObject creditsPanel;

    private void Start()
    {
        // 모든 팝업은 꺼진 상태로 시작
        if (stageSelectPanel != null)
        {
            stageSelectPanel.SetActive(false);
        }

        if (howToPlayPanel != null)
        {
            howToPlayPanel.SetActive(false);
        }

        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }

        if (creditsPanel != null)
        {
            creditsPanel.SetActive(false);
        }
    }

    public void ToggleStageSelectPanel()
    {
        if (stageSelectPanel == null)
        {
            Debug.LogWarning("StageSelectPanel이 연결되지 않았습니다.");
            return;
        }

        bool isActive = stageSelectPanel.activeSelf;
        stageSelectPanel.SetActive(!isActive);
    }


    public void CloseStageSelectPanel()
    {
        if (stageSelectPanel != null)
        {
            stageSelectPanel.SetActive(false);
        }
    }

    public void ToggleHowToPlayPanel()
    {
        if (howToPlayPanel == null)
        {
            Debug.LogWarning("HowToPlayPanel이 연결되지 않았습니다.");
            return;
        }

        bool isActive = howToPlayPanel.activeSelf;
        howToPlayPanel.SetActive(!isActive);
    }

    public void CloseHowToPlayPanel()
    {
        if (howToPlayPanel != null)
        {
            howToPlayPanel.SetActive(false);
        }
    }

    public void ToggleSettingsPanel()
    {
        if (settingsPanel == null)
        {
            Debug.LogWarning("SettingsPanel이 연결되지 않았습니다.");
            return;
        }

        bool isActive = settingsPanel.activeSelf;
        settingsPanel.SetActive(!isActive);
    }

    public void CloseSettingsPanel()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }
    }


    public void ToggleCreditsPanel()
    {
        if (creditsPanel == null)
        {
            Debug.LogWarning("CreditsPanel이 연결되지 않았습니다.");
            return;
        }

        creditsPanel.SetActive(!creditsPanel.activeSelf);
    }

    public void CloseCreditsPanel()
    {
        if (creditsPanel != null)
        {
            creditsPanel.SetActive(false);
        }
    }

    /// <summary>에디터에서는 플레이 모드 중지, 빌드에서는 프로그램 종료</summary>
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}