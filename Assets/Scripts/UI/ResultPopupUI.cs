using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

// 결과 팝업 연출 — GameManager에 저장된 값만으로 통계를 계산하고 카운트업·도장을 재생한다
public class ResultPopupUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject popupRoot;
    [SerializeField] private CanvasGroup dimmedGroup;
    [SerializeField] private RectTransform panelTransform;

    [Header("Texts")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text playTimeText;
    [SerializeField] private TMP_Text moveCountText;
    [SerializeField] private TMP_Text eraseCountText;
    [SerializeField] private TMP_Text remainCountText;

    [Header("Buttons")]
    [SerializeField] private Button cctvButton;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button exitButton;

    [Header("ClearStamp")]
    [SerializeField] private GameObject clearStamp;
    [SerializeField] private RectTransform clearStampTransform;
    [SerializeField] private CanvasGroup clearStampGroup;

    [Header("GameoverStamp")]
    [SerializeField] private GameObject gameoverStamp;
    [SerializeField] private RectTransform gameOverStampTransform;
    [SerializeField] private CanvasGroup gameOverStampGroup;

    [Header("Clear Effect")]
    [SerializeField] private GameObject confettiPrefab;
    [Tooltip("도장이 찍힌 뒤 꽃가루를 쏘기까지 기다리는 시간 (초)")]
    [SerializeField] private float confettiDelay = 1f;
    [Tooltip("화면 양쪽 위 모서리에서 안쪽으로 기울이는 각도. 0=위로, 90=수평으로 안쪽, 180=아래로")]
    [SerializeField] private float confettiAngle = 120f;
    [Tooltip("쏘는 속도 배율 — 클수록 멀리 날아간다")]
    [SerializeField] private float confettiSpeed = 1.5f;
    [Tooltip("꽃가루가 원래보다 더 남아 있는 시간 (초)")]
    [SerializeField] private float confettiExtraLifetime = 2f;
    [Tooltip("카메라에서 파티클까지 거리 — 가까울수록 크게 보인다. Canvas Plane Distance보다 작아야 UI 위에 보임")]
    [SerializeField] private float confettiDistance = 25f;
    [Tooltip("파티클 크기 배율")]
    [SerializeField] private float confettiScale = 1f;

    private void Start()
    {
        popupRoot.SetActive(false);
        clearStamp.SetActive(false);
        gameoverStamp.SetActive(false);

        retryButton.onClick.AddListener(OnClickRetry);
        exitButton.onClick.AddListener(OnClickExit);
        cctvButton.onClick.AddListener(OnClickCCTV);
        ShowResult(GameManager.Instance.isClear, GameManager.Instance.playTime, GameManager.Instance.totalStains - 3, GameManager.Instance.cleanedStains, GameManager.Instance.totalStains - GameManager.Instance.cleanedStains);
    }
    

    public void ShowResult(bool isClear, float totalPlaySeconds, int moveCount, int eraseCount, int remainCount)
    {
        StopAllCoroutines();
        popupRoot.SetActive(true);

        titleText.text = isClear ? "게임 결과 - Clear!" : "게임 결과 - Game Over";

        StartCoroutine(ShowResultRoutine(isClear, totalPlaySeconds, moveCount, eraseCount, remainCount));
    }

    private IEnumerator ShowResultRoutine(bool isClear, float totalPlaySeconds, int moveCount, int eraseCount, int remainCount)
    {
        Time.timeScale = 0f;   // 배경 정지 — 연출은 전부 unscaled 시간을 쓴다

        float unscaled = Time.unscaledDeltaTime;

        dimmedGroup.alpha = 0f;
        panelTransform.localScale = Vector3.one * 0.8f;

        playTimeText.text = "총 플레이 시간: 0:00:00";
        moveCountText.text = "인형 이동 횟수: 0";
        eraseCountText.text = "지운 얼룩 수: 0";
        remainCountText.text = "남은 정리해야 할 것의 수: 0";

        clearStampGroup.alpha = 0f;
        clearStampTransform.localScale = Vector3.one * 1.8f;
        clearStampTransform.localRotation = Quaternion.Euler(0f, 0f, -20f);

        gameOverStampGroup.alpha = 0f;
        gameOverStampTransform.localScale = Vector3.one * 1.8f;
        gameOverStampTransform.localRotation = Quaternion.Euler(0f, 0f, -20f);

        SetButtonsInteractable(false);

        // 배경 페이드인
        yield return StartCoroutine(FadeCanvasGroup(dimmedGroup, 0f, 1f, 0.25f));

        // 패널
        yield return StartCoroutine(ScaleRect(panelTransform, 0.8f, 1.05f, 0.18f));
        yield return StartCoroutine(ScaleRect(panelTransform, 1.05f, 1.0f, 0.10f));

        // 숫자 카운트
        yield return StartCoroutine(AnimateTime(totalPlaySeconds, 0.9f));
        yield return StartCoroutine(AnimateIntText(moveCountText, "인형 이동 횟수: ", moveCount, 0.5f));
        yield return StartCoroutine(AnimateIntText(eraseCountText, "지운 얼룩 수: ", eraseCount, 0.5f));
        yield return StartCoroutine(AnimateIntText(remainCountText, "남은 정리해야 할 것의 수: ", remainCount, 0.5f));

        // 도장 연출
        if (isClear)
        {
            remainCountText.color = Color.limeGreen;
            yield return new WaitForSecondsRealtime(1f);
            yield return StartCoroutine(
                PlayStampEffect(clearStampGroup, clearStampTransform));
            yield return new WaitForSecondsRealtime(confettiDelay);
            PlayConfetti();
        }
        else
        {
            remainCountText.color = Color.red;
            yield return new WaitForSecondsRealtime(1f);
            yield return StartCoroutine(
                PlayStampEffect(gameOverStampGroup, gameOverStampTransform));
            titleText.text = "게임 결과 - Game Over";
            
        }

        SetButtonsInteractable(true);
    }

    private IEnumerator AnimateTime(float targetSeconds, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float current = Mathf.Lerp(0f, targetSeconds, EaseOutCubic(t));

            playTimeText.text = $"총 플레이 시간: {FormatTime(current)}";
            yield return null;
        }

        playTimeText.text = $"총 플레이 시간: {FormatTime(targetSeconds)}";
    }

    private IEnumerator AnimateIntText(TMP_Text textUI, string prefix, int targetValue, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            int current = Mathf.RoundToInt(Mathf.Lerp(0, targetValue, EaseOutCubic(t)));

            textUI.text = prefix + current;
            yield return null;
        }

        textUI.text = prefix + targetValue;
    }

    private IEnumerator PlayStampEffect(CanvasGroup stampGroup, RectTransform stampTransform)
    {
        stampGroup.gameObject.SetActive(true);   // 넘겨받은 도장만 켠다
        if (stampGroup.TryGetComponent(out AudioSource stampSound))
            stampSound.Play();   // 도장이 나타나는 순간에 소리

        float duration = 0.25f;
        float elapsed = 0f;

        Vector3 startScale = Vector3.one * 1.8f;
        Vector3 endScale = Vector3.one * 0.9f;

        float startRot = -20f;
        float endRot = -8f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = EaseOutBack(t);

            stampGroup.alpha = t;
            stampTransform.localScale = Vector3.Lerp(startScale, endScale, eased);

            float z = Mathf.Lerp(startRot, endRot, eased);
            stampTransform.localRotation = Quaternion.Euler(0f, 0f, z);

            yield return null;
        }

        yield return StartCoroutine(ScaleRect(stampTransform, 0.9f, 1.05f, 0.08f));
        yield return StartCoroutine(ScaleRect(stampTransform, 1.05f, 1.0f, 0.08f));
    }

    private void PlayConfetti()
    {
        if (confettiPrefab == null) return;

        Canvas canvas = panelTransform.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        if (cam == null) return;

        // UI보다 위에 그려지도록 Canvas보다 한 칸 높은 순서로
        int sortingOrder = canvas != null ? canvas.sortingOrder + 1 : 0;

        // 프리팹은 위로 쏘므로 왼쪽은 시계 방향, 오른쪽은 반시계 방향으로 기울여 안쪽을 향하게
        // 양쪽 모서리 + 그 아래 한 개씩 — 아래쪽은 조금 더 수평으로 쏴서 겹치지 않고 퍼지게
        SpawnConfetti(cam, new Vector2(0f, 1f), -confettiAngle, sortingOrder);
        SpawnConfetti(cam, new Vector2(0f, 0.75f), -(confettiAngle - 20f), sortingOrder);
        SpawnConfetti(cam, new Vector2(1f, 1f), confettiAngle, sortingOrder);
        SpawnConfetti(cam, new Vector2(1f, 0.75f), confettiAngle - 20f, sortingOrder);
    }

    private void SpawnConfetti(Camera cam, Vector2 viewportPos, float zAngle, int sortingOrder)
    {
        // Canvas 자식으로 두면 1단위=1픽셀이라 종이가 점만 해진다 — 카메라 앞 월드 공간에 생성
        Vector3 worldPos = cam.ViewportToWorldPoint(new Vector3(viewportPos.x, viewportPos.y, confettiDistance));
        Quaternion rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, zAngle);
        GameObject confetti = Instantiate(confettiPrefab, worldPos, rotation);
        confetti.transform.localScale = Vector3.one * confettiScale;

        // 루트가 꽃가루 본체 — 자식(반짝임·빛)은 순간 번쩍임이라 속도·수명은 원본 그대로 둔다
        if (confetti.TryGetComponent(out ParticleSystem paper))
        {
            var paperMain = paper.main;
            paperMain.startSpeedMultiplier *= confettiSpeed;
            ParticleSystem.MinMaxCurve lifetime = paperMain.startLifetime;
            if (lifetime.mode == ParticleSystemCurveMode.TwoConstants)
                paperMain.startLifetime = new ParticleSystem.MinMaxCurve(
                    lifetime.constantMin + confettiExtraLifetime, lifetime.constantMax + confettiExtraLifetime);
            else if (lifetime.mode == ParticleSystemCurveMode.Constant)
                paperMain.startLifetime = lifetime.constant + confettiExtraLifetime;
        }

        foreach (ParticleSystem ps in confetti.GetComponentsInChildren<ParticleSystem>())
        {
            var main = ps.main;
            main.useUnscaledTime = true;   // 팝업 중엔 timeScale=0이라 끄면 파티클이 멈춰 있다
            ps.GetComponent<ParticleSystemRenderer>().sortingOrder = sortingOrder;
            ps.Play();
        }
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup group, float from, float to, float duration)
    {
        float elapsed = 0f;
        group.alpha = from;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            group.alpha = Mathf.Lerp(from, to, t);
            yield return null;
        }

        group.alpha = to;
    }

    private IEnumerator ScaleRect(RectTransform rect, float from, float to, float duration)
    {
        float elapsed = 0f;
        rect.localScale = Vector3.one * from;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float scale = Mathf.Lerp(from, to, t);
            rect.localScale = Vector3.one * scale;
            yield return null;
        }

        rect.localScale = Vector3.one * to;
    }

    private void SetButtonsInteractable(bool value)
    {
        cctvButton.interactable = value;
        retryButton.interactable = value;
        exitButton.interactable = value;
    }

    private string FormatTime(float seconds)
    {
        int total = Mathf.FloorToInt(seconds);
        int h = total / 3600;
        int m = (total % 3600) / 60;
        int s = total % 60;
        return $"{h}:{m:00}:{s:00}";
    }

    private float EaseOutCubic(float t)
    {
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    private float EaseOutBack(float t)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    private void OnClickRetry()
    {
        Time.timeScale = 1f;
        Debug.Log("다시하기 버튼 클릭");
        // 리플레이를 보고 돌아온 경우 플래그가 남아있으면 게임 대신 리플레이가 시작되므로 확실히 해제
        GameManager.Instance.isReplayMode = false;
        // 판을 끝냈으니 인트로는 이미 봤음 → 인트로 없이 바로 시작 (「코드 설계 - 재시작 시 인트로 생략」)
        StageRestart.Reload(GameManager.Instance.sceneName);
    }

    private void OnClickExit()
    {
        Time.timeScale = 1f;
        Debug.Log("나가기");
        GameManager.Instance.isReplayMode = false;
        SceneManager.LoadScene("MainScene");
    }

    // isReplayMode를 켠 채 게임 씬을 재로드하면 ReplayManager가 리플레이로 부팅한다
    private void OnClickCCTV()
    {
        Debug.Log("CCTV 버튼 클릭");
        if (GameManager.Instance.replayData != null)
        {
            Time.timeScale = 1f;
            GameManager.Instance.isReplayMode = true;
            SceneManager.LoadScene(GameManager.Instance.sceneName);
        }
    }
}

