using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

// NPC 블라블라 목소리 재생기 (2026-10-23).
// Speak(대사) → GibberishPlanner가 블립 계획을 만들고, 여기서 오디오 시계(dspTime)에 맞춰 AudioSource 여러 개로 예약 재생한다.
//  - 소스 6개를 돌려 씀: 블립마다 자기 pitch, 겹쳐도 끊기지 않음
//  - 0.1초 앞까지만 예약 → Stop·일시정지에 바로 반응
//  - Time.timeScale 0(일시정지)이면 같이 멈추고, 풀리면 멈춘 시간만큼 밀어서 이어 감
//  - 무전 효과(저역·고역 차단 + 찌그러짐)는 말투(VoiceProfile.radio)에서 켬
// 쓰는 곳: RadioBox (줄이 바뀌면 Speak, 닫히면 Stop). 현장 NPC는 캐릭터에 붙이고 spatialBlend 1
// 설계 기록: 프로젝트 문서 「코드 설계 - NPC 블라블라 목소리」 2.4, 2.7, 4.3
public class GibberishVoice : MonoBehaviour
{
    [SerializeField] private VoiceProfile profile;
    [Tooltip("믹서 그룹 (SFX). 비우면 믹서를 거치지 않음 → 설정 볼륨이 안 먹음")]
    [SerializeField] private AudioMixerGroup output;
    [Tooltip("0 = 화면 소리(무전), 1 = 3D 위치 소리(현장 NPC)")]
    [Range(0f, 1f)] [SerializeField] private float spatialBlend = 0f;
    [Tooltip("동시에 겹칠 수 있는 블립 수")]
    [Range(2, 12)] [SerializeField] private int poolSize = 6;
    [Tooltip("몇 초 앞까지 미리 예약할지")]
    [SerializeField] private float lookahead = 0.1f;
    [Tooltip("멈출 때 볼륨을 내리는 시간 (뚝 끊기는 틱 소리 방지)")]
    [SerializeField] private float stopFade = 0.04f;

    [Header("미리 듣기 (플레이 중, 컴포넌트 ⋮ 메뉴)")]
    [TextArea] [SerializeField] private string previewLine = "도착했냐. 인형 모으는 집이다.";

    /// <summary>블립이 실제로 소리 나는 순간 (그 블립의 볼륨). 입 모양·초상 들썩임용</summary>
    public event Action<float> OnBlip;

    class Voice
    {
        public AudioSource src;
        public AudioHighPassFilter hp;
        public AudioLowPassFilter lp;
        public AudioDistortionFilter dist;
        public double start, end;
        public float volume;
        public bool paused;
    }

    private Voice[] pool;
    private int nextVoice;
    private List<GibberishPlanner.Blip> blips;
    private int nextBlip;
    private double startDsp, endDsp;
    private readonly List<(double time, float volume)> notices = new List<(double, float)>();
    private bool paused;
    private double pausedAt;
    private bool fading;
    private float fadeLeft;

    public VoiceProfile Profile { get => profile; set => profile = value; }

    /// <summary>말하는 중 (예약이 남았거나 소리가 끝나지 않음)</summary>
    public bool IsSpeaking => blips != null && (paused || AudioSettings.dspTime < endDsp);

    void Awake() => EnsurePool();

    void OnDisable() => StopImmediate();

    void EnsurePool()
    {
        if (pool != null) return;
        pool = new Voice[poolSize];
        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject($"Voice_{i}");
            go.transform.SetParent(transform, false);
            var v = new Voice
            {
                src = go.AddComponent<AudioSource>(),
                hp = go.AddComponent<AudioHighPassFilter>(),
                lp = go.AddComponent<AudioLowPassFilter>(),
                dist = go.AddComponent<AudioDistortionFilter>(),
            };
            v.src.playOnAwake = false;
            v.src.loop = false;
            v.src.dopplerLevel = 0f;
            v.src.priority = 64;
            pool[i] = v;
        }
    }

    /// <summary>지금 말을 끊고 새 대사를 말한다. maxSeconds > 0이면 그 안에 끝나게 줄임. 말하는 길이(초)를 돌려줌</summary>
    public float Speak(string text, float maxSeconds = 0f)
    {
        StopImmediate();
        if (profile == null || string.IsNullOrWhiteSpace(text) || !isActiveAndEnabled) return 0f;
        EnsurePool();
        ApplySettings();

        var plan = GibberishPlanner.Build(text, profile, maxSeconds);
        if (plan.blips.Count == 0) return 0f;
        blips = plan.blips;
        nextBlip = 0;
        startDsp = AudioSettings.dspTime + 0.03; // 첫 블립도 정확히 예약되게 살짝 뒤
        endDsp = startDsp + plan.Duration;
        paused = Time.timeScale <= 0f;
        if (paused) pausedAt = AudioSettings.dspTime;
        else Schedule();
        return plan.Duration;
    }

    /// <summary>짧게 볼륨을 내리고 멈춘다</summary>
    public void Stop()
    {
        if (pool == null || blips == null || fading) return;
        double now = AudioSettings.dspTime;
        nextBlip = blips.Count; // 더 예약하지 않음
        notices.Clear();
        foreach (var v in pool)
        {
            if (v.start > now) v.src.Stop(); // 아직 시작 안 한 예약은 취소
            v.volume = v.src.volume;
        }
        fading = true;
        fadeLeft = stopFade;
    }

    /// <summary>바로 멈춘다 (씬 전환·건너뛰기)</summary>
    public void StopImmediate()
    {
        blips = null;
        notices.Clear();
        fading = false;
        paused = false;
        if (pool == null) return;
        foreach (var v in pool)
        {
            v.src.Stop();
            v.paused = false;
            v.start = v.end = 0;
        }
    }

    void Update()
    {
        if (pool == null) return;

        if (fading)
        {
            fadeLeft -= Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(fadeLeft / Mathf.Max(0.001f, stopFade));
            foreach (var v in pool) v.src.volume = v.volume * k;
            if (fadeLeft <= 0f) StopImmediate();
            return;
        }
        if (blips == null) return;

        bool gamePaused = Time.timeScale <= 0f;
        if (gamePaused && !paused) Pause();
        else if (!gamePaused && paused) Resume();
        if (paused) return;

        Schedule();

        double now = AudioSettings.dspTime;
        while (notices.Count > 0 && notices[0].time <= now)
        {
            OnBlip?.Invoke(notices[0].volume);
            notices.RemoveAt(0);
        }

        if (nextBlip >= blips.Count && now >= endDsp) blips = null; // 다 말함
    }

    void Schedule()
    {
        double now = AudioSettings.dspTime;
        while (nextBlip < blips.Count && startDsp + blips[nextBlip].time <= now + lookahead)
        {
            var b = blips[nextBlip++];
            if (b.clip == null) continue;
            var v = pool[nextVoice];
            nextVoice = (nextVoice + 1) % pool.Length;

            double at = Math.Max(startDsp + b.time, now);
            v.src.Stop();
            v.src.clip = b.clip;
            v.src.pitch = b.pitch;
            v.src.volume = Mathf.Clamp(b.volume, 0f, 1f);
            v.src.PlayScheduled(at);
            v.start = at;
            v.end = at + b.Length;
            v.paused = false;
            notices.Add((at, b.volume));
        }
    }

    void Pause()
    {
        paused = true;
        pausedAt = AudioSettings.dspTime;
        foreach (var v in pool)
        {
            if (v.start > pausedAt) { v.src.Stop(); v.start = v.end = 0; }            // 예약만 된 것: 취소
            else if (v.end > pausedAt) { v.src.Pause(); v.paused = true; }          // 소리 나는 중: 멈춤
        }
        // 취소한 예약은 다시 대기열로
        while (nextBlip > 0 && startDsp + blips[nextBlip - 1].time > pausedAt) nextBlip--;
        notices.RemoveAll(n => n.time > pausedAt);
    }

    void Resume()
    {
        double offset = AudioSettings.dspTime - pausedAt;
        startDsp += offset;
        endDsp += offset;
        paused = false;
        foreach (var v in pool)
        {
            if (!v.paused) continue;
            v.src.UnPause();
            v.paused = false;
            v.end += offset;
        }
    }

    void ApplySettings()
    {
        foreach (var v in pool)
        {
            v.src.outputAudioMixerGroup = output;
            v.src.spatialBlend = spatialBlend;
            bool radio = profile.radio;
            v.hp.enabled = radio;
            v.lp.enabled = radio;
            v.dist.enabled = radio && profile.distortion > 0f;
            v.hp.cutoffFrequency = profile.highpassHz;
            v.lp.cutoffFrequency = profile.lowpassHz;
            v.dist.distortionLevel = profile.distortion;
        }
    }

    [ContextMenu("미리 듣기")]
    void Preview()
    {
        if (!Application.isPlaying) { Debug.Log("[목소리] 미리 듣기는 플레이 중에만 됩니다"); return; }
        float d = Speak(previewLine);
        Debug.Log($"[목소리] 미리 듣기 {d:0.00}초: {previewLine}");
    }
}
