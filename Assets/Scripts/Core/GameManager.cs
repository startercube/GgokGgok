using System;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 씬 간 결과 데이터 운반 + 전역 설정(볼륨·감도). MainScene에만 있는 DontDestroyOnLoad 싱글톤
public class GameManager : MonoBehaviour
{
    [Header("Audio")]
    public AudioMixer audioMixer;
    public Slider volumeSlider;

    [Header("Mouse")]
    public Slider sensitivitySlider;

    // 감도 원본 (2026-10-24 「코드 설계 - 마우스 감도 범위·동기화」) — 범위·기본값·저장 키는 여기 한 곳.
    // 씬 슬라이더에 저장된 최소·최대(예전 1~4.5)는 BindSensitivitySlider가 실행 때 이 값으로 덮어쓴다
    // 범위 0.3~5 (2026-11-01 「코드 설계 - 마우스 감도 프레임 독립」 3장) — 0.022도/카운트 기준으로 고DPI는 더 느리게, 슬라이더는 더 섬세하게
    public const float SensitivityMin = 0.3f;
    public const float SensitivityDefault = 1.25f;   // 예전 2.5의 절반
    public const float SensitivityMax = 5f;
    // v3 (2026-11-01): 감도가 프레임 시간에 의존하던 시절(v2)에 맞춘 숫자는 새 공식에서 의미가 달라 한 번 기본값으로 시작
    const string SensitivityKey = "MouseSensitivity.v3";

    // 감도 1 = 마우스 1카운트당 0.022도 (2026-11-01 「코드 설계 - 마우스 감도 프레임 독립」).
    // Source 엔진(CS 등)과 같은 기준 → 감도 숫자가 CS 감도와 같은 의미. 회전 = 카운트 × DegreesPerCount × 감도 (FPS·맵 무관)
    public const float DegreesPerCount = 0.022f;

    // 볼륨 저장 키·기본값 (2026-10-24, GameManager·SettingsManager에 따로 적혀 있던 것을 모음)
    // 믹서 매개변수 이름 "MasterVolume"(ApplyVolume)은 저장 키와 이름만 같고 별개
    public const string VolumeKey = "MasterVolume";
    public const float VolumeDefault = 0.75f;

    static float? mouseSensitivity;
    /// <summary>지금 감도. 처음 읽을 때 저장값을 불러옴 → 메인을 거치지 않아도, 누가 먼저 읽어도 같은 값</summary>
    public static float MouseSensitivity =>
        mouseSensitivity ??= Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, SensitivityDefault), SensitivityMin, SensitivityMax);
    public static event Action<float> OnSensitivityChanged;

    public static GameManager Instance;

    public ReplayData replayData;   // ReplayRecorder가 쓰고 ReplayManager가 읽음

    public bool isClear;
    public bool isReplayMode;   // true면 게임 씬이 리플레이 모드로 부팅됨 (CCTV 버튼이 켬)

    public float playTime;

    public int cleanedStains;
    public int totalStains;

    public string sceneName;


    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }



    void Start()
    {
        // PlayerPrefs 저장값으로 메인 메뉴 슬라이더를 초기화 (없으면 기본값)
        float volume = PlayerPrefs.GetFloat(VolumeKey, VolumeDefault);

        volumeSlider.value = volume;

        ApplyVolume(volume);

        volumeSlider.onValueChanged.AddListener(SetVolume);
        BindSensitivitySlider(sensitivitySlider);
    }

    // 감도 변경의 유일한 창구 — 범위로 자르고 저장 후 구독자(PlayerMove)에게 방송해 플레이 중에도 즉시 반영
    public static void SetMouseSensitivity(float value)
    {
        value = Mathf.Clamp(value, SensitivityMin, SensitivityMax);
        if (mouseSensitivity.HasValue && Mathf.Approximately(mouseSensitivity.Value, value)) return;
        mouseSensitivity = value;
        PlayerPrefs.SetFloat(SensitivityKey, value);
        OnSensitivityChanged?.Invoke(value);
    }

    /// <summary>
    /// 설정 화면의 감도 슬라이더를 원본에 연결 (메인 GameManager·게임 중 SettingsManager 공용).
    /// 범위를 코드 값으로 맞추고, 알림 없이 지금 값을 넣고(잘린 값이 저장되지 않게), 리스너는 하나만 건다
    /// </summary>
    public static void BindSensitivitySlider(Slider slider)
    {
        if (slider == null) return;
        slider.onValueChanged.RemoveListener(SetMouseSensitivity);
        slider.wholeNumbers = false;
        slider.minValue = SensitivityMin;
        slider.maxValue = SensitivityMax;
        slider.SetValueWithoutNotify(MouseSensitivity);
        slider.onValueChanged.AddListener(SetMouseSensitivity);
    }



    /// <summary>
    /// 감도·볼륨을 기본값으로 (전시회 초기화 버튼, 2026-10-24 「코드 설계 - 전시회 초기화 버튼」).
    /// 키만 지우면 감도 캐시 때문에 같은 실행 동안 옛 값이 남으므로 캐시를 비우고 기본값을 방송한다.
    /// 슬라이더는 메인 씬을 다시 불러올 때 SettingsManager가 저장값(=기본값)으로 다시 맞춘다. 저장(Save)은 부르는 쪽이 한다
    /// </summary>
    public static void ResetSettings()
    {
        PlayerPrefs.DeleteKey(SensitivityKey);
        PlayerPrefs.DeleteKey(VolumeKey);

        mouseSensitivity = null;
        OnSensitivityChanged?.Invoke(MouseSensitivity);   // 다시 읽으면 기본값 (키가 없으므로)

        if (Instance != null) Instance.ApplyVolume(VolumeDefault);
    }

    public void ResetGameData()
    {
        isClear = false;
        playTime = 0f;
        cleanedStains = 0;
        totalStains = 0;
        replayData = null;
    }

    public void SetVolume(float value)
    {
        ApplyVolume(value);
        PlayerPrefs.SetFloat(VolumeKey, value);
    }

    // 예전 진입점 (씬 이벤트가 이름으로 부를 때를 위해 남김) → 원본으로
    public void SetSensitivity(float value) => SetMouseSensitivity(value);

    void ApplyVolume(float value)
    {
        // 슬라이더 0~1 → 데시벨 로그 변환. 선형으로 넣으면 체감이 이상하다
        float dB = Mathf.Log10(Mathf.Clamp(value, 0.0001f, 1f)) * 20f;
        audioMixer.SetFloat("MasterVolume", dB);
    }


}
