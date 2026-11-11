using UnityEngine;

// 프레임 상한 — 발열·쿨링팬 대책 (2026-11-02). 설계: 「코드 설계 - 프레임 제한(발열)」
// 품질 설정 두 단계 모두 VSync가 꺼져 있고 targetFrameRate를 정하는 코드도 없어서, 빌드가 GPU가 허락하는 만큼 무제한으로 그리고 있었다.
// ① 시작할 때 VSync를 켠다 → 모니터 주사율(60·144Hz…)에 맞춰 그리고 화면 찢어짐도 없다
// ② 그래픽 드라이버가 VSync를 강제로 끄면 ①이 무시된다 → 실제 fps를 재서 주사율보다 확실히 빠르면 targetFrameRate = 주사율로 바꾼다
// 씬에 놓지 않는다: 어느 씬으로 시작해도(튜토리얼·맵을 바로 실행해도) 첫 씬을 불러오기 전에 스스로 만들어진다.
// ⚠️ 나중에 QualitySettings.SetQualityLevel을 쓰면 vSyncCount가 그 품질 단계 값(지금 0)으로 덮인다 → 전환 직후 ApplyDefault()를 다시 부를 것
public sealed class FrameRateLimiter : MonoBehaviour
{
    const float WindowSeconds = 2f;          // fps를 재는 창 길이
    const float OverspeedRatio = 1.25f;      // 평균 fps가 주사율의 이 배수를 넘으면 'VSync가 안 먹힘' (59.94Hz 같은 오차 흡수)
    const int OverspeedWindowsToCap = 2;     // 연속 이만큼의 창에서 넘어야 전환 — 순간 튀는 값 무시
    const int DefaultRefreshRate = 60;       // 주사율을 못 읽을 때
    const int MinValidRefreshRate = 30;      // 이보다 낮게 읽히면 잘못 읽은 값으로 본다

    static FrameRateLimiter instance;

    float windowTime;
    int windowFrames;
    int overspeedWindows;
    bool capMode;   // true = VSync 대신 targetFrameRate로 상한 중

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        // 도메인 리로드를 끈 플레이 모드에서도 이전 오브젝트는 파괴되어 Unity 기준 null → 새로 만든다
        if (instance != null) return;

        ApplyDefault();

        var go = new GameObject("[FrameRateLimiter]");   // Hierarchy의 DontDestroyOnLoad 아래에서 확인 가능
        DontDestroyOnLoad(go);
        instance = go.AddComponent<FrameRateLimiter>();
    }

    /// <summary>기본 정책(VSync 켜기)을 적용하고 감지를 처음부터 다시 한다. 품질 단계 전환 뒤에 다시 부를 것</summary>
    public static void ApplyDefault()
    {
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = -1;   // 데스크톱은 VSync가 켜져 있으면 어차피 무시되지만, 이전 상한이 남지 않게 초기화
        if (instance != null) instance.ResetDetection();
    }

    void ResetDetection()
    {
        windowTime = 0f;
        windowFrames = 0;
        overspeedWindows = 0;
        capMode = false;
    }

    void Update()
    {
        // unscaled: 일시정지(timeScale 0)에도 실제 경과 시간으로 잰다
        windowTime += Time.unscaledDeltaTime;
        windowFrames++;
        if (windowTime < WindowSeconds) return;

        float fps = windowFrames / windowTime;
        windowTime = 0f;
        windowFrames = 0;
        int refresh = CurrentRefreshRate();

        if (capMode)
        {
            // 창을 다른 주사율 모니터로 옮긴 경우를 따라간다
            if (Application.targetFrameRate != refresh) Application.targetFrameRate = refresh;
            return;
        }

        // VSync가 정상이면 fps는 주사율을 넘을 수 없다. 느려지는 쪽(로딩 끊김·포커스 복귀)은 fps를 낮출 뿐이라 잘못 전환시키지 않는다
        overspeedWindows = fps > refresh * OverspeedRatio ? overspeedWindows + 1 : 0;
        if (overspeedWindows >= OverspeedWindowsToCap) EnterCapMode(refresh, fps);
    }

    void EnterCapMode(int refresh, float measuredFps)
    {
        capMode = true;
        QualitySettings.vSyncCount = 0;          // 0이어야 데스크톱에서 targetFrameRate가 적용된다
        Application.targetFrameRate = refresh;
        Debug.Log($"[프레임 제한] VSync가 적용되지 않아(평균 {measuredFps:F0}fps, 주사율 {refresh}Hz) 프레임 상한 {refresh}fps로 전환합니다.");
    }

    // 지금 화면의 주사율(정수로 반올림, 59.94 → 60). 창 모드면 창이 있는 모니터의 데스크톱 주사율
    static int CurrentRefreshRate()
    {
        double hz = Screen.currentResolution.refreshRateRatio.value;
        if (double.IsNaN(hz) || hz < MinValidRefreshRate) return DefaultRefreshRate;
        return Mathf.RoundToInt((float)hz);
    }
}
