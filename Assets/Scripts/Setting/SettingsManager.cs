using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Audio;

// 인게임 설정 패널 — 설정의 원본은 GameManager이고, 이 컴포넌트는 슬라이더를 거기 연결만 한다
public class SettingsManager : MonoBehaviour
{
    [Header("Audio")]
    public AudioMixer audioMixer;
    public Slider volumeSlider;

    [Header("Mouse")]
    public Slider sensitivitySlider;

    // 감도 원본은 GameManager (예전 코드 호환용으로 남김)
    public static float MouseSensitivity => GameManager.MouseSensitivity;

    void Start()
    {
        float volume = PlayerPrefs.GetFloat(GameManager.VolumeKey, GameManager.VolumeDefault);

        volumeSlider.value = volume;

        ApplyVolume(volume);

        volumeSlider.onValueChanged.AddListener(SetVolume);
        // 감도: 범위·값·저장·즉시 반영을 원본(GameManager)에 맡김 → 메인에서 바꾼 값과 항상 같음
        GameManager.BindSensitivitySlider(sensitivitySlider);
    }


    public void SetVolume(float value)
    {
        ApplyVolume(value);
        PlayerPrefs.SetFloat(GameManager.VolumeKey, value);
    }

    // 예전 진입점 → 원본으로
    public void SetSensitivity(float value) => GameManager.SetMouseSensitivity(value);

    void ApplyVolume(float value)
    {
        // 슬라이더 0~1 → 데시벨 로그 변환. 선형으로 넣으면 체감이 이상하다
        float dB = Mathf.Log10(Mathf.Clamp(value, 0.0001f, 1f)) * 20f;
        audioMixer.SetFloat("MasterVolume", dB);
    }
}
