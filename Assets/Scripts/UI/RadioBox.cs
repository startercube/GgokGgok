using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 한 주임 무전 박스 (공용, 2026-10-23) — 초상(3D 촬영 화면) + 화자 이름 + 대사. 인트로와 게임 도중 이벤트에서 같이 쓴다.
// 쓰는 법은 SubtitleBox와 같다: 진행 코드는 "언제 말할지"만 정하고 Say / Enqueue를 부른다.
//   Say(대사, 초)  — 정한 시간만큼 보여 줌 (인트로처럼 시각표가 있을 때)
//   Enqueue(대사) — 글자 수로 시간 계산 (이벤트 무전)
// 상태: 꺼짐 → 연결(지직 노이즈) → 말하는 중(아래 막대가 줄어듦) → 다음 줄이 있으면 바로 이어서 / 없으면 초상이 가로줄로 접히며 꺼짐
// 조작을 막지 않는다. Time.deltaTime 기준이라 일시정지 중엔 멈춘다
// 목소리(GibberishVoice)가 있으면 줄마다 그 줄을 블라블라로 말하고, 박스가 닫히면 멈춘다 (2026-10-23, 문서 「코드 설계 - NPC 블라블라 목소리」 4.4)
// 설계 기록: 프로젝트 문서 「코드 설계 - 맵 인트로·무전」 2.6, 3.5
public class RadioBox : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private TMP_Text speakerText;
    [SerializeField] private TMP_Text bodyText;
    [Tooltip("\"2 / 5\" 같은 줄 번호 (비워 두면 숨김)")]
    [SerializeField] private TMP_Text counterText;
    [SerializeField] private RawImage portraitImage;
    [Tooltip("말하는 동안 줄어드는 막대 (가로 크기를 줄임)")]
    [SerializeField] private RectTransform progressFill;
    [Tooltip("수신 표시 점")]
    [SerializeField] private Graphic rxDot;
    [SerializeField] private RadioPortrait portrait;
    [Tooltip("블라블라 목소리 (비우면 같은 오브젝트에서 찾음, 그래도 없으면 소리 없음)")]
    [SerializeField] private GibberishVoice voice;

    [Header("Sound (비우면 소리 없음)")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip connectClip;
    [SerializeField] private AudioClip closeClip;

    [Header("Timing")]
    [SerializeField] private float connectSeconds = 0.3f;
    [SerializeField] private float closeSeconds = 0.2f;
    [SerializeField] private float fadeSeconds = 0.15f;
    [Tooltip("Enqueue로 넣은 줄의 시간 = 기본 + 글자 수 × 이 값")]
    [SerializeField] private float baseSeconds = 1.2f;
    [SerializeField] private float secondsPerChar = 0.08f;
    [SerializeField] private float minSeconds = 2f;
    [SerializeField] private float maxSeconds = 6f;
    [Tooltip("줄이 끝난 뒤 다음 줄을 기다리며 박스를 열어 두는 시간 (이 안에 새 줄이 오면 접히지 않고 이어감)")]
    [SerializeField] private float lingerSeconds = 0.4f;

    [SerializeField] private string defaultSpeaker = "한 주임";

    enum State { Off, Connecting, Talking, Linger, Closing }

    private readonly Queue<(string line, float seconds)> queue = new Queue<(string, float)>();
    private State state = State.Off;
    private float timer, lineSeconds;
    private Texture2D noise;
    private Color32[] noisePixels;
    private float portraitHeight = -1f;

    public float ConnectSeconds => connectSeconds;

    /// <summary>말하는 목소리. 다른 화자는 Voice.Profile을 바꾼 뒤 Say</summary>
    public GibberishVoice Voice => voice;

    /// <summary>말하는 줄도, 기다리는 줄도 없음</summary>
    public bool IsIdle => state == State.Off && queue.Count == 0;

    public string Speaker { get; set; }

    void Awake()
    {
        Speaker = defaultSpeaker;
        if (voice == null) voice = GetComponent<GibberishVoice>();
        if (group != null) { group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false; }
        if (portraitImage != null) portraitHeight = portraitImage.rectTransform.localScale.y;
        SetCounter("");
    }

    void OnDestroy()
    {
        if (noise != null) Destroy(noise);
    }

    /// <summary>정한 시간(초)만큼 보여 준다. seconds ≤ 0이면 글자 수로 계산</summary>
    public void Say(string line, float seconds)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        queue.Enqueue((line, seconds));
    }

    /// <summary>글자 수로 시간을 계산해 대기열 끝에 넣는다</summary>
    public void Enqueue(string line) => Say(line, -1f);

    /// <summary>대기열을 비우고 지금 줄도 닫는다</summary>
    public void Clear()
    {
        queue.Clear();
        if (state == State.Connecting || state == State.Talking || state == State.Linger) BeginClose();
    }

    /// <summary>연출 없이 바로 끈다 (건너뛰기·씬 전환)</summary>
    public void ClearImmediate()
    {
        queue.Clear();
        state = State.Off;
        if (voice != null) voice.StopImmediate();
        if (group != null) group.alpha = 0f;
        if (portrait != null) { portrait.Talking = false; portrait.SetRendering(false); }
    }

    public void SetCounter(string text)
    {
        if (counterText == null) return;
        counterText.text = text ?? "";
        counterText.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    void Update()
    {
        float dt = Time.deltaTime;
        switch (state)
        {
            case State.Off:
                if (queue.Count > 0) BeginConnect();
                break;
            case State.Connecting:
                timer -= dt;
                UpdateNoise();
                if (timer <= 0f) NextLine();
                break;
            case State.Talking:
                timer -= dt;
                if (progressFill != null)
                    progressFill.localScale = new Vector3(Mathf.Clamp01(timer / Mathf.Max(0.01f, lineSeconds)), 1f, 1f);
                if (timer <= 0f)
                {
                    if (queue.Count > 0) NextLine();
                    else { state = State.Linger; timer = lingerSeconds; if (portrait != null) portrait.Talking = false; }
                }
                break;
            case State.Linger:
                timer -= dt;
                if (queue.Count > 0) NextLine();
                else if (timer <= 0f) BeginClose();
                break;
            case State.Closing:
                timer -= dt;
                if (portraitImage != null && portraitHeight > 0f)
                {
                    float k = Mathf.Clamp01(timer / Mathf.Max(0.01f, closeSeconds));
                    var s = portraitImage.rectTransform.localScale;
                    portraitImage.rectTransform.localScale = new Vector3(s.x, Mathf.Max(0.03f, portraitHeight * k), s.z);
                }
                if (timer <= 0f)
                {
                    state = State.Off;
                    if (portrait != null) portrait.SetRendering(false);
                }
                break;
        }

        if (group != null)
        {
            float target = state == State.Off ? 0f : 1f;
            group.alpha = Mathf.MoveTowards(group.alpha, target, dt / Mathf.Max(0.01f, fadeSeconds));
        }
        if (rxDot != null) rxDot.enabled = state == State.Talking ? Mathf.Repeat(Time.time, 0.8f) < 0.5f : state == State.Connecting;
    }

    void BeginConnect()
    {
        state = State.Connecting;
        timer = connectSeconds;
        ResetPortraitScale();
        if (speakerText != null) speakerText.text = Speaker;
        if (bodyText != null) bodyText.text = "";
        if (progressFill != null) progressFill.localScale = Vector3.one;
        EnsureNoise();
        if (portraitImage != null) portraitImage.texture = noise;
        if (portrait != null) { portrait.SetRendering(true); portrait.Talking = false; }
        Play(connectClip);
    }

    void NextLine()
    {
        var (line, seconds) = queue.Dequeue();
        lineSeconds = seconds > 0f ? seconds : Mathf.Clamp(baseSeconds + line.Length * secondsPerChar, minSeconds, maxSeconds);
        timer = lineSeconds;
        state = State.Talking;
        if (bodyText != null) bodyText.text = line;
        if (speakerText != null) speakerText.text = Speaker;
        if (portraitImage != null && portrait != null && portrait.Texture != null) portraitImage.texture = portrait.Texture;
        if (portrait != null) { portrait.SetRendering(true); portrait.Talking = true; }
        if (voice != null) voice.Speak(line, lineSeconds);
    }

    void BeginClose()
    {
        state = State.Closing;
        timer = closeSeconds;
        if (portrait != null) portrait.Talking = false;
        if (voice != null) voice.Stop();
        Play(closeClip);
    }

    void ResetPortraitScale()
    {
        if (portraitImage == null || portraitHeight <= 0f) return;
        var s = portraitImage.rectTransform.localScale;
        portraitImage.rectTransform.localScale = new Vector3(s.x, portraitHeight, s.z);
    }

    void EnsureNoise()
    {
        if (noise != null) return;
        noise = new Texture2D(64, 48, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        noisePixels = new Color32[64 * 48];
        UpdateNoise();
    }

    // 지직 노이즈: 줄마다 밝기가 다른 회색 점
    void UpdateNoise()
    {
        if (noise == null) return;
        for (int y = 0; y < 48; y++)
        {
            byte rowBias = (byte)Random.Range(0, 60);
            for (int x = 0; x < 64; x++)
            {
                byte v = (byte)Mathf.Min(255, Random.Range(20, 200) + rowBias);
                noisePixels[y * 64 + x] = new Color32(v, v, v, 255);
            }
        }
        noise.SetPixels32(noisePixels);
        noise.Apply(false);
    }

    void Play(AudioClip clip)
    {
        if (clip != null && audioSource != null) audioSource.PlayOneShot(clip);
    }
}
