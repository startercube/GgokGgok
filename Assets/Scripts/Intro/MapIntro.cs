using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// 맵 인트로 진행 (2026-10-23, 인형방부터) — 작전 카드 → 천장 아래 한 바퀴 + 한 주임 무전 → 플레이어 눈높이로 착지 → 작전 개시
// - CameraManagement가 게임을 잠근 뒤 Play를 부른다. 끝나면 CameraManagement에 플레이어 카메라 전환·잠금 해제를 맡긴다
// - 시간 하나(t)로 카메라·카드·무전·도장·HUD를 모두 진행한다 → 건너뛰기·일시정지에서 서로 어긋나지 않음 (Time.deltaTime 기준)
// - 카메라: 전용 시네머신 카메라(introCam, 몸·조준 부품 없음)를 우선순위 100으로 켜고 IntroPath 값으로 직접 움직인다.
//   마지막 점 = 플레이어 카메라 눈 자세 → 그 자세 그대로 컷 전환하므로 페이드가 필요 없다 (튜토리얼 기상 연출과 같은 원리)
// - 건너뛰기: 한 번 클리어한 맵에서만 (StageClearRecord), SPACE를 길게
// 설계 기록: 프로젝트 문서 「코드 설계 - 맵 인트로·무전」
public class MapIntro : MonoBehaviour
{
    [SerializeField] private MapIntroScript script;
    [SerializeField] private IntroPath path;
    [SerializeField] private CinemachineCamera introCam;
    [SerializeField] private MapIntroOverlay overlay;
    [SerializeField] private RadioBox radio;
    [Tooltip("인트로 동안 숨겼다가 조작이 풀릴 때 나타낼 HUD (빈대·얼룩 개수 등)")]
    [SerializeField] private GameObject[] hudToHide;

    [Header("연출 값")]
    [SerializeField] private float letterboxInSeconds = 0.35f;
    [SerializeField] private float letterboxOutSeconds = 0.5f;
    [SerializeField] private float hudFadeSeconds = 0.5f;

    [Header("건너뛰기 (한 번 클리어한 맵만)")]
    [SerializeField] private float skipHoldSeconds = 0.8f;
    [SerializeField] private float skipFadeSeconds = 0.25f;

    /// <summary>진행 이벤트: "landed"(플레이어 카메라로 넘어감), "released"(조작 풀림), "skipped"</summary>
    public event Action<string> OnEvent;

    public bool IsPlaying => playing;

    /// <summary>조작이 풀렸는지 (인트로가 없거나 건너뛴 경우 포함). 게임 도중 무전이 이 뒤부터 나온다</summary>
    public bool HasReleased => released || owner == null;

    private CameraManagement owner;
    private float t;
    private bool playing, landed, released, skipping, countSet;
    private int nextLine;
    private bool canSkip;
    private float skipHeld;
    private float landTime, stampTime, releaseTime;
    private int clockBaseSeconds;
    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private readonly List<CanvasGroup> hudGroups = new List<CanvasGroup>();
    private float hudFadeT = -1f;

    void Awake()
    {
        // 평소엔 꺼 둔다 — 켜진 채면 리플레이 카메라를 이겨 버림
        if (introCam != null) introCam.enabled = false;
    }

    public void Play(CameraManagement cameraManagement)
    {
        owner = cameraManagement;
        if (script == null || path == null || introCam == null)
        {
            Debug.LogWarning("[인트로] 데이터·길·카메라 중 비어 있는 게 있어 바로 시작함", this);
            StartCoroutine(FinishWithoutIntro());
            return;
        }

        // 마지막 점 = 플레이어 눈 자세
        GetPlayerEye(out var eyePos, out var eyeRot);
        path.SetPlayerEye(eyePos, eyeRot);
        landTime = path.Duration;
        stampTime = Mathf.Max(script.stampTime, landTime);
        releaseTime = Mathf.Max(script.releaseTime, landTime);

        clockBaseSeconds = ParseClock(script.clock);
        canSkip = StageClearRecord.IsCleared(SceneManager.GetActiveScene().name);

        HidePlayerBody(true);
        HideHud(true);

        if (overlay != null)
        {
            int dolls = GameProgressManager.Instance != null ? GameProgressManager.Instance.TotalDolls : 0;
            overlay.Setup(script, dolls);
            overlay.SetVisible(true);
            overlay.SetSkip(canSkip, 0f);
        }
        if (radio != null) { radio.Speaker = script.speaker; radio.ClearImmediate(); }

        introCam.Priority = 100;
        introCam.enabled = true;
        t = 0f; nextLine = 0; landed = released = skipping = countSet = false; skipHeld = 0f;
        playing = true;
        Apply();
    }

    void Update()
    {
        UpdateHudFade();
        if (!playing || skipping) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;   // 일시정지
        t += dt;

        if (canSkip && !landed)
        {
            var kb = Keyboard.current;
            bool held = kb != null && kb.spaceKey.isPressed;
            skipHeld = held ? skipHeld + dt : 0f;
            if (overlay != null) overlay.SetSkip(true, skipHeld / Mathf.Max(0.01f, skipHoldSeconds));
            if (skipHeld >= skipHoldSeconds) { StartCoroutine(Skip()); return; }
        }

        Apply();
    }

    // 지금 t에 맞춰 모든 것을 놓는다
    void Apply()
    {
        // 카메라
        if (!landed)
        {
            path.Evaluate(t, out var pos, out var rot);
            introCam.transform.SetPositionAndRotation(pos, rot);
        }

        if (overlay != null)
        {
            int chars = script.typeCharSeconds > 0f ? Mathf.FloorToInt((t - script.typeStartDelay) / script.typeCharSeconds) : int.MaxValue;
            overlay.SetTyped(chars);
            overlay.SetFold((t - script.cardFoldTime) / Mathf.Max(0.01f, script.foldSeconds));

            float boxIn = Mathf.Clamp01(t / Mathf.Max(0.01f, letterboxInSeconds));
            float boxOut = 1f - Mathf.Clamp01((t - landTime) / Mathf.Max(0.01f, letterboxOutSeconds));
            overlay.SetLetterbox(Mathf.Min(boxIn, boxOut));
            if (t > landTime) overlay.SetBandAlpha(boxOut);

            // 위쪽 띠 시각: 지금 PC 시각 (메인화면 CCTV와 같음) / 고정 시각을 쓰면 그 시각부터 흐름
            overlay.SetClock(script.useRealTime ? DateTime.Now.ToString("HH:mm:ss") : FormatClock(clockBaseSeconds + Mathf.FloorToInt(t)));
            if (!countSet && t >= stampTime - 1f && GameProgressManager.Instance != null)
            {
                // 빈대 수는 GameProgressManager.Start에서 세므로 시작 직후엔 0일 수 있다 → 도장 직전에 다시 읽음
                overlay.SetDollCount(script, GameProgressManager.Instance.TotalDolls);
                countSet = true;
            }
            overlay.SetStamp(t - stampTime, script.stampSeconds);
            if (canSkip && landed) overlay.SetSkip(false, 0f);
        }

        // 무전: 첫 줄은 연결 지직 시간만큼 먼저 넣는다. 각 줄은 다음 줄 시각까지 보여 줌
        if (radio != null && script.lines != null)
        {
            while (nextLine < script.lines.Length)
            {
                var line = script.lines[nextLine];
                float lead = nextLine == 0 ? radio.ConnectSeconds : 0f;
                if (t < line.time - lead) break;
                float until = nextLine + 1 < script.lines.Length ? script.lines[nextLine + 1].time : line.time + script.lastLineSeconds;
                radio.Say(line.text, Mathf.Max(0.5f, until - Mathf.Max(t, line.time)));
                radio.SetCounter($"{nextLine + 1} / {script.lines.Length}");
                nextLine++;
            }
        }

        if (!landed && t >= landTime) Land();
        if (!released && t >= releaseTime) Release();

        // 끝: 도장·무전까지 다 끝나면 화면 요소를 치운다
        if (released && t > stampTime + script.stampSeconds && (radio == null || radio.IsIdle))
        {
            if (overlay != null) overlay.SetVisible(false);
            playing = false;
        }
    }

    void Land()
    {
        landed = true;
        // 플레이어 카메라 각도를 착지 자세와 같게 (보통 이미 같음 — 시작 자세에서 눈 자세를 만들었으므로)
        if (owner != null) StartCoroutine(owner.HandToPlayer(introCam));
        else introCam.enabled = false;
        HidePlayerBody(false);
        OnEvent?.Invoke("landed");
    }

    void Release()
    {
        released = true;
        if (owner != null) owner.SetGameplayFrozen(false);
        HideHud(false);
        StageRestart.MarkIntroSeen(gameObject.scene.name);   // 끝까지 봤음 → 이번 실행의 재시작부터 인트로 생략 (SPACE 건너뛰기도 여기를 거침)
        OnEvent?.Invoke("released");
    }

    IEnumerator Skip()
    {
        skipping = true;
        var fader = owner != null ? owner.Fader : null;
        if (fader != null) yield return fader.FadeOut(skipFadeSeconds);

        if (radio != null) radio.ClearImmediate();
        nextLine = script.lines != null ? script.lines.Length : 0;
        t = Mathf.Max(releaseTime, stampTime + script.stampSeconds) + 0.01f;
        path.Evaluate(landTime, out var pos, out var rot);
        introCam.transform.SetPositionAndRotation(pos, rot);
        if (!landed) Land();
        if (!released) Release();
        if (overlay != null) overlay.SetVisible(false);
        playing = false;

        if (fader != null) yield return fader.FadeIn(skipFadeSeconds);
        skipping = false;
        OnEvent?.Invoke("skipped");
    }

    IEnumerator FinishWithoutIntro()
    {
        yield return null;
        if (owner != null)
        {
            yield return owner.HandToPlayer(null);
            owner.SetGameplayFrozen(false);
        }
        StageRestart.MarkIntroSeen(gameObject.scene.name);
    }

    // 플레이어 카메라 눈 위치 = 따라가는 대상 + FollowOffset, 방향 = Pan/Tilt (TutorialWakeUp.StandEyePosition과 같음)
    void GetPlayerEye(out Vector3 pos, out Quaternion rot)
    {
        var playerCam = owner != null ? owner.PlayerCam : null;
        Transform target = playerCam != null ? playerCam.Follow : null;
        if (target == null) { var p = GameObject.FindGameObjectWithTag("Player"); target = p != null ? p.transform : null; }
        var follow = playerCam != null ? playerCam.GetComponent<CinemachineFollow>() : null;
        Vector3 offset = follow != null ? follow.FollowOffset : new Vector3(0f, 0.6235f, 0f);
        pos = target != null ? target.position + target.rotation * offset : (playerCam != null ? playerCam.transform.position : Vector3.zero);

        var panTilt = playerCam != null ? playerCam.GetComponent<CinemachinePanTilt>() : null;
        rot = panTilt != null ? Quaternion.Euler(panTilt.TiltAxis.Value, panTilt.PanAxis.Value, 0f)
            : (playerCam != null ? playerCam.transform.rotation : Quaternion.identity);
    }

    // 카메라가 플레이어 머리로 내려올 때 몸이 화면을 가리지 않게
    void HidePlayerBody(bool hide)
    {
        if (hide)
        {
            hiddenRenderers.Clear();
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return;
            foreach (var r in p.GetComponentsInChildren<Renderer>(true))
                if (!r.forceRenderingOff) { r.forceRenderingOff = true; hiddenRenderers.Add(r); }
        }
        else
        {
            foreach (var r in hiddenRenderers) if (r != null) r.forceRenderingOff = false;
            hiddenRenderers.Clear();
        }
    }

    void HideHud(bool hide)
    {
        if (hide)
        {
            hudGroups.Clear();
            hudFadeT = -1f;
            if (hudToHide == null) return;
            foreach (var go in hudToHide)
            {
                if (go == null) continue;
                var g = go.GetComponent<CanvasGroup>();
                if (g == null) g = go.AddComponent<CanvasGroup>();
                g.alpha = 0f;
                hudGroups.Add(g);
            }
        }
        else hudFadeT = 0f;   // UpdateHudFade가 서서히 켬
    }

    void UpdateHudFade()
    {
        if (hudFadeT < 0f) return;
        hudFadeT += Time.unscaledDeltaTime;
        float a = Mathf.Clamp01(hudFadeT / Mathf.Max(0.01f, hudFadeSeconds));
        foreach (var g in hudGroups) if (g != null) g.alpha = a;
        if (a >= 1f) hudFadeT = -1f;
    }

    void OnDisable()
    {
        // 중간에 꺼져도 조작이 잠긴 채, 몸·HUD가 숨겨진 채 남지 않게
        if (playing && !released && owner != null) owner.SetGameplayFrozen(false);
        if (playing && !landed && introCam != null) introCam.enabled = false;
        HidePlayerBody(false);
        foreach (var g in hudGroups) if (g != null) g.alpha = 1f;
        playing = false;
    }

    static int ParseClock(string s)
    {
        if (!string.IsNullOrEmpty(s))
        {
            var parts = s.Split(':');
            if (parts.Length >= 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m))
                return h * 3600 + m * 60 + (parts.Length >= 3 && int.TryParse(parts[2], out int sec) ? sec : 0);
        }
        return 0;
    }

    static string FormatClock(int seconds)
    {
        seconds = ((seconds % 86400) + 86400) % 86400;
        return $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }
}
