using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// 공용 대사 자막 박스 (2026-10-21, 튜토리얼에서 분리) — 화자 이름 + 대사 한 줄을 대기열 순서대로 보여 준다.
// 쓰는 법: Assets/Prefabs/UI/SubtitleBox.prefab을 씬의 Canvas에 넣고, 대사를 내는 스크립트가 Enqueue를 부른다.
// - 앞 줄이 끝나기 전에 새 줄이 오면 이어서 재생 (복도 대사 규칙, 2026-10-06 확정)
// - Time.deltaTime 기준이라 일시정지(timeScale 0) 중엔 자막도 멈춘다
// - 화자 이름을 비우면 이름 칸을 숨긴다
// - 넘기기 (2026-11-02): Space를 누르면 지금 줄을 바로 끝낸다 (한 번에 한 줄). 맵 인트로 건너뛰기와 같은 키. 「코드 설계 - 튜토리얼 템포」
// 맵마다 다른 진행 규칙(언제 무슨 대사를 낼지)은 각자 스크립트에서 이 박스를 참조해 만든다 (예: TutorialDirector)
public class SubtitleBox : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private TMP_Text speakerText;
    [SerializeField] private TMP_Text bodyText;

    [Header("Timing")]
    [Tooltip("한 줄을 보여 주는 시간 = 기본 + 글자 수 × 이 값 (초)")]
    [SerializeField] private float baseSeconds = 1.0f;
    [SerializeField] private float secondsPerChar = 0.075f;
    [SerializeField] private float minSeconds = 1.8f;
    [SerializeField] private float maxSeconds = 6f;
    [Tooltip("줄과 줄 사이 빈 시간")]
    [SerializeField] private float gapSeconds = 0.25f;
    [SerializeField] private float fadeSeconds = 0.15f;

    [Header("Skip (2026-11-02)")]
    [Tooltip("켜면 skipKey로 지금 줄을 바로 끝낼 수 있다")]
    [SerializeField] private bool allowSkip = true;
    [Tooltip("맵 인트로 건너뛰기(Space)와 같은 키 — 게임 전체에서 Space = 건너뛰기")]
    [SerializeField] private Key skipKey = Key.Space;
    [Tooltip("줄이 뜬 뒤 이 시간(초) 안의 입력은 무시 — 연타하다 막 뜬 줄을 못 보고 넘기지 않게")]
    [SerializeField] private float skipGuardSeconds = 0.15f;
    [Tooltip("넘기기 안내 문구. 비우면 화자 이름 글자를 복제해 박스 오른쪽 위에 만든다")]
    [SerializeField] private TMP_Text skipHintText;
    [SerializeField] private string skipHintLabel = "Space 넘기기";
    [SerializeField] private float skipHintSize = 22f;
    [SerializeField, Range(0f, 1f)] private float skipHintAlpha = 0.55f;

    private readonly Queue<(string speaker, string line)> queue = new Queue<(string, string)>();
    private float timer;      // 지금 줄의 남은 시간
    private float gapTimer;
    private bool showing;
    private float shownTime;  // 지금 줄이 뜬 뒤 지난 시간 (넘기기 보호)

    /// <summary>보여 줄 줄도, 기다리는 줄도 없음</summary>
    public bool IsIdle => !showing && queue.Count == 0 && gapTimer <= 0f;

    void Awake()
    {
        if (group != null) group.alpha = 0f;
        SetupSkipHint();
    }

    /// <summary>지금 보이는 줄을 바로 끝낸다 (줄 사이 쉼 뒤 다음 줄). 키 말고 다른 입력에서도 부를 수 있게 공개</summary>
    public void Skip()
    {
        if (!showing) return;
        timer = 0f;
    }

    // 안내 문구: 박스(CanvasGroup) 안에 두므로 박스와 같이 나타나고 사라진다. 프리팹을 다시 만들지 않아도 되게 실행 중에 만든다
    void SetupSkipHint()
    {
        if (skipHintText == null && allowSkip && speakerText != null)
        {
            var go = Instantiate(speakerText.gameObject, transform, false);
            go.name = "SkipHint";
            go.SetActive(true);
            skipHintText = go.GetComponent<TMP_Text>();
            var rt = (RectTransform)go.transform;
            var srt = (RectTransform)speakerText.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-srt.anchoredPosition.x, srt.anchoredPosition.y);   // 화자 이름과 같은 높이, 반대쪽
            rt.sizeDelta = srt.sizeDelta;
            skipHintText.alignment = TextAlignmentOptions.TopRight;
            skipHintText.fontSize = skipHintSize;
            skipHintText.color = new Color(1f, 1f, 1f, skipHintAlpha);
        }
        if (skipHintText != null)
        {
            skipHintText.text = skipHintLabel;
            skipHintText.gameObject.SetActive(allowSkip);
        }
    }

    /// <summary>대사 한 줄을 대기열 끝에 넣는다</summary>
    public void Enqueue(string speaker, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        queue.Enqueue((speaker, line));
    }

    /// <summary>보여 주던 줄과 대기열을 모두 비운다 (씬 전환·컷 건너뛰기 등)</summary>
    public void Clear()
    {
        queue.Clear();
        showing = false;
        gapTimer = 0f;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (showing)
        {
            shownTime += dt;
            // 넘기기: 일시정지 중(timeScale 0)은 무시
            if (allowSkip && Time.timeScale > 0f && shownTime >= skipGuardSeconds)
            {
                var kb = Keyboard.current;
                if (kb != null && kb[skipKey].wasPressedThisFrame) Skip();
            }
            timer -= dt;
            if (timer <= 0f) { showing = false; gapTimer = gapSeconds; }
        }
        else if (gapTimer > 0f)
        {
            gapTimer -= dt;
        }
        else if (queue.Count > 0)
        {
            var (speaker, line) = queue.Dequeue();
            if (speakerText != null)
            {
                speakerText.text = speaker;
                speakerText.gameObject.SetActive(!string.IsNullOrEmpty(speaker));
            }
            if (bodyText != null) bodyText.text = line;
            timer = Mathf.Clamp(baseSeconds + line.Length * secondsPerChar, minSeconds, maxSeconds);
            showing = true;
            shownTime = 0f;
        }

        if (group != null)
        {
            // 줄 사이 빈 시간에 바로 다음 줄이 있으면 박스는 켜 둔다 (깜빡임 방지)
            float target = showing || (gapTimer > 0f && queue.Count > 0) ? 1f : 0f;
            group.alpha = Mathf.MoveTowards(group.alpha, target, dt / Mathf.Max(0.01f, fadeSeconds));
        }
    }
}
