using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 튜토리얼 진행 관리 (2026-10-19) — 대사 데이터(JSON)의 컷을 순서대로 진행한다.
// 컷마다 "넘어가는 조건(when)"이 채워지면 한 주임 대사를 자막에 쌓고, 동작(onStart/onEnd)을 실행한다.
// - 조건을 기다리는 동안 hintDelay초가 지나면 그 컷의 막힘 힌트를 한 번 던진다 (답이 아니라 방향만)
// - 플레이어가 앞질러 가면(뒤 컷의 구역·거리·포획 조건이 먼저 채워지면) 그 컷으로 건너뛴다 → 어디서도 진행이 막히지 않게
//   단, gate 컷은 넘지 못한다 → 반드시 봐야 하는 장면(3 격리실)은 끝까지 (2026-10-23, 설계: 「코드 설계 - 튜토리얼 앞지르기 관문(gate)」)
// - events는 순서와 상관없이 한 번씩 (첫 오답, 첫 얼룩 7-2)
// - 기상 연출(TutorialWakeUp) 이벤트 wakeLook·wakeDone, 동작 wake:stand / 연습 판 패배 이벤트 defeat, 동작 retry → 재시작 판은 retryFrom 컷부터 (2026-10-21)
// - 컷 mode: first = 처음 판에서만, retry = 재시작 판에서만
// - 가이드 시각화 (2026-11-02): 지금 기다리는 컷의 focus(오브젝트 이름)를 발광시키고, 화면 밖이면 화살표 (TutorialFocus)
//   focusUntil: done = 컷이 넘어갈 때까지 / reach = 도착하면 끔. 비우면 lookAt 컷은 done, 나머지는 reach
//   빈대·찾을 소품은 focus로 받지 않는다 (시작할 때 오류를 내고 그 컷의 표시를 끔). 설계: 「코드 설계 - 튜토리얼 가이드 시각화」
// - startFrozen: 시작할 때 얼려 둘 빈대 / 동작 doll:free:이름 · doll:freeze:이름 (2026-11-02 방 A 곁눈질 찾기 — 방에 들어와 대사가 끝난 뒤 풂)
// 대사·조건을 바꿀 땐 Assets/Data/Tutorial/tutorial_script.json을 고친다
public class TutorialDirector : MonoBehaviour
{
    [Serializable] public class When { public string type; public string target; public float value; }
    [Serializable] public class Beat
    {
        public string id;
        public bool disabled;
        [Tooltip("\"first\" = 처음 판에서만, \"retry\" = 연습 판 재시작 판에서만, 비우면 둘 다 (2026-10-21)")]
        public string mode;
        public When when;
        public string require;
        [Tooltip("true면 앞지르기가 이 컷을 넘지 못한다 — 이 컷이 나오기 전엔 뒤 컷으로 건너뛰지 않음 (반드시 봐야 하는 장면의 마지막 컷에, 2026-10-23)")]
        public bool gate;
        public string[] lines = new string[0];
        public string hint;
        public string[] onStart = new string[0];
        public string[] onEnd = new string[0];
        [Tooltip("이 컷을 기다리는 동안 발광·화살표로 보여 줄 오브젝트 이름 (봐야 할 것·가야 할 곳만, 2026-11-02)")]
        public string focus;
        [Tooltip("\"done\" = 컷이 넘어갈 때까지, \"reach\" = 플레이어가 도착하면 끔. 비우면 lookAt 컷은 done, 나머지는 reach")]
        public string focusUntil;
    }
    [Serializable] public class Script
    {
        public string speaker = "한 주임";
        public float hintDelay = 20f;
        [Tooltip("연습 판 재시작 판에서 시작할 컷 id")]
        public string retryFrom;
        [Tooltip("focus 대상이 화면 밖인 채 이만큼(초) 지나면 화살표 (2026-11-02)")]
        public float focusArrowDelay = 4f;
        [Tooltip("시작할 때 얼려 둘 빈대 이름 (doll:free:이름으로 풂, 2026-11-02)")]
        public string[] startFrozen = new string[0];
        public Beat[] beats = new Beat[0];
        public Beat[] events = new Beat[0];
    }

    [SerializeField] private TextAsset scriptAsset;
    [Tooltip("공용 자막 박스 (Assets/Prefabs/UI/SubtitleBox.prefab)")]
    [SerializeField] private SubtitleBox subtitle;
    [Tooltip("구역(TutorialZone)·경로(TutorialPath)를 담은 부모")]
    [SerializeField] private Transform markersRoot;
    [SerializeField] private TutorialPracticeFlow practice;
    [SerializeField] private TutorialZoneActivator practiceActivator;
    [Tooltip("1-3 이동 키 표시. 플레이어가 조금 걸으면 사라진다")]
    [SerializeField] private GameObject moveKeys;
    [SerializeField] private float lookMaxDistance = 9f;
    [Tooltip("한 주임 (동작 guide:leg:구간 / guide:face:물체 / guide:facePlayer / guide:clipboard)")]
    [SerializeField] private TutorialGuide guide;
    [Tooltip("장면 3 컵 시연 (동작 demo:gaze / demo:cup, 이벤트 cupLanded / transformed)")]
    [SerializeField] private TutorialCupDemo cupDemo;
    [Tooltip("장면 1 기상 연출 (동작 wake:stand, 이벤트 wakeLook / wakeDone)")]
    [SerializeField] private TutorialWakeUp wakeUp;
    [Tooltip("오답 쿨타임 한 번 동안 E를 이만큼 누르면 이벤트 cooldownSpam")]
    [SerializeField] private int cooldownSpamCount = 3;
    [Tooltip("가이드 시각화 (발광·화살표). 비우면 같은 오브젝트에 자동으로 붙인다 (2026-11-02)")]
    [SerializeField] private TutorialFocus focus;

    private Script data;
    private readonly List<Beat> beats = new List<Beat>();
    private readonly List<Beat> events = new List<Beat>();
    private readonly HashSet<Beat> firedEvents = new HashSet<Beat>();
    private readonly HashSet<string> flags = new HashSet<string>();
    private readonly Dictionary<string, TutorialZone> zones = new Dictionary<string, TutorialZone>();
    private readonly Dictionary<string, TutorialPath> paths = new Dictionary<string, TutorialPath>();
    private readonly Dictionary<string, float> pathProgress = new Dictionary<string, float>();
    private readonly Dictionary<string, Renderer> lookTargets = new Dictionary<string, Renderer>();
    private readonly Dictionary<string, DollMove> dolls = new Dictionary<string, DollMove>();
    private readonly Dictionary<string, TutorialZoneGoal> goals = new Dictionary<string, TutorialZoneGoal>();
    private readonly List<string[]> pendingOnEnd = new List<string[]>();

    private Transform player;
    private Camera cam;
    private int current;          // 다음에 조건을 기다릴 컷 번호
    private float waitTime;       // 지금 컷을 기다린 시간 (자막이 비어 있는 동안만 셈)
    private bool hintShown;
    private float startTime;
    private Vector3 moveKeysFrom;
    private int blockedCount;     // 이번 오답 쿨타임 동안 막힌 잡기 시도 수
    private readonly Dictionary<Beat, GameObject> focusTargets = new Dictionary<Beat, GameObject>();
    private Beat focusBeat;       // 지금 가이드를 보여 주고 있는 컷 (바뀔 때만 Show/Hide)

    void Awake()
    {
        if (scriptAsset == null) { Debug.LogError($"[{name}] 대사 데이터가 비어 있습니다.", this); enabled = false; return; }
        data = JsonUtility.FromJson<Script>(scriptAsset.text);
        bool retry = TutorialRetry.IsRetry;   // TutorialRetry.Awake가 먼저 실행됨 (실행 순서 -1000)
        bool Use(Beat b) => !b.disabled && b.when != null && b.mode != (retry ? "first" : "retry");
        foreach (var b in data.beats) if (Use(b)) beats.Add(b);
        foreach (var b in data.events) if (Use(b)) events.Add(b);
        // 재시작 전 판에서 이미 나온 한 번짜리 대사는 다시 내지 않는다
        if (retry) foreach (var e in events) if (TutorialRetry.FiredOnce.Contains(e.id)) firedEvents.Add(e);
        if (moveKeys != null) moveKeys.SetActive(false);
        if (focus == null) focus = GetComponent<TutorialFocus>();
        if (focus == null) focus = gameObject.AddComponent<TutorialFocus>();   // 씬을 다시 만들지 않아도 동작하게
    }

    void OnEnable()
    {
        PlayerInteraction.OnWrongGuess += HandleWrongGuess;
        PlayerInteraction.OnCatchBlocked += HandleCatchBlocked;
        DollStainSpawner.OnStainSpawned += HandleStainSpawned;
    }

    void OnDisable()
    {
        PlayerInteraction.OnWrongGuess -= HandleWrongGuess;
        PlayerInteraction.OnCatchBlocked -= HandleCatchBlocked;
        DollStainSpawner.OnStainSpawned -= HandleStainSpawned;
        if (practiceActivator != null) practiceActivator.OnActivated -= HandlePracticeEnter;
        if (GameProgressManager.Instance != null) GameProgressManager.Instance.OnDoorOpened -= HandleVictory;
    }

    void Start()
    {
        startTime = Time.time;
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
        cam = Camera.main;

        if (markersRoot != null)
        {
            foreach (var z in markersRoot.GetComponentsInChildren<TutorialZone>(true)) zones[z.name] = z;
            foreach (var pa in markersRoot.GetComponentsInChildren<TutorialPath>(true)) { paths[pa.name] = pa; pathProgress[pa.name] = -1f; }
        }
        foreach (var d in FindObjectsByType<DollMove>(FindObjectsSortMode.None)) dolls[d.name] = d;
        foreach (var g in FindObjectsByType<TutorialZoneGoal>(FindObjectsSortMode.None)) goals[g.name] = g;

        // 연습 판 재시작 판: retryFrom 컷부터 (앞 컷은 지나온 것)
        if (TutorialRetry.IsRetry)
        {
            int i = beats.FindIndex(b => b.id == data.retryFrom);
            if (i >= 0) current = i;
            else Debug.LogWarning($"[튜토리얼 진행] 재시작 컷 '{data.retryFrom}'이 없어 처음부터 진행합니다.", this);
        }

        // 데이터가 가리키는 이름이 씬에 없으면 처음에 알려 준다 (이름 오타로 컷이 영영 안 넘어가는 것 방지). 재시작 판은 지나온 컷 제외
        for (int i = current; i < beats.Count; i++) { CheckTarget(beats[i]); CheckFocus(beats[i]); }
        foreach (var b in events) CheckTarget(b);

        // 방에 들어오기 전에 벽 너머에서 미리 움직이지 않게 (방 A: 줄을 먼저 보여 줘야 "줄에서 벗어남"이 보인다)
        if (data.startFrozen != null)
            foreach (var n in data.startFrozen)
            {
                if (dolls.TryGetValue(n, out var d) && d != null) d.SetFrozen(true);
                else if (!TutorialRetry.IsRetry) Debug.LogWarning($"[튜토리얼 진행] startFrozen: 빈대 '{n}'을(를) 찾지 못했습니다.", this);
            }

        if (focus != null)
        {
            focus.Init(subtitle);
            focus.ArrowDelay = data.focusArrowDelay;
        }

        if (practiceActivator != null) practiceActivator.OnActivated += HandlePracticeEnter;
        if (GameProgressManager.Instance != null) GameProgressManager.Instance.OnDoorOpened += HandleVictory;
        if (cupDemo != null) cupDemo.OnEvent += HandleDemoEvent;
        if (wakeUp != null) wakeUp.OnEvent += HandleDemoEvent;
        if (practice != null) practice.OnDefeated += HandleDefeat;
    }

    void OnDestroy()
    {
        if (cupDemo != null) cupDemo.OnEvent -= HandleDemoEvent;
        if (wakeUp != null) wakeUp.OnEvent -= HandleDemoEvent;
        if (practice != null) practice.OnDefeated -= HandleDefeat;
    }

    void HandleDefeat() => flags.Add("defeat");

    void HandleDemoEvent(string e) => flags.Add(e);

    void CheckTarget(Beat b)
    {
        string t = b.when.target;
        bool ok = b.when.type switch
        {
            "enterZone" => zones.ContainsKey(t),
            "walk" => paths.ContainsKey(t),
            "caught" => dolls.ContainsKey(t),
            "goalDone" => goals.ContainsKey(t),
            "lookAt" => FindLookTarget(t) != null,
            "start" or "afterPrev" or "event" => true,
            _ => false,
        };
        if (!ok) Debug.LogWarning($"[튜토리얼 진행] 컷 {b.id}: 조건 {b.when.type} '{t}'을(를) 씬에서 찾지 못했습니다.", this);
    }

    // focus 이름을 씬에서 찾아 둔다. 없으면 경고, 빈대·찾을 소품이면 오류 + 표시 안 함 (정답을 빛내지 않는다)
    void CheckFocus(Beat b)
    {
        if (string.IsNullOrEmpty(b.focus)) return;
        var go = GameObject.Find(b.focus);
        if (go == null)
        {
            Debug.LogWarning($"[튜토리얼 진행] 컷 {b.id}: focus '{b.focus}'을(를) 씬에서 찾지 못했습니다. 이 컷은 가이드 표시 없이 진행합니다.", this);
            return;
        }
        if (!TutorialFocus.IsAllowedTarget(go, out string reason))
        {
            Debug.LogError($"[튜토리얼 진행] 컷 {b.id}: focus '{b.focus}'는 가이드로 쓸 수 없습니다 — {reason}. 찾을 대상은 빛내지 않습니다 (표시 끔).", this);
            return;
        }
        if (go.GetComponentInChildren<Renderer>() == null)
            Debug.LogWarning($"[튜토리얼 진행] 컷 {b.id}: focus '{b.focus}' 아래에 렌더러가 없어 발광·화살표가 나오지 않습니다. 모양이 있는 오브젝트 이름으로 바꾸세요.", this);
        focusTargets[b] = go;
    }

    // ===== 이벤트 =====

    void HandleWrongGuess() { flags.Add("wrongGuess"); blockedCount = 0; }
    void HandleCatchBlocked()
    {
        if (++blockedCount >= cooldownSpamCount) flags.Add("cooldownSpam");
    }
    void HandlePracticeEnter() => flags.Add("practiceEnter");
    void HandleVictory() => flags.Add("victory");
    void HandleStainSpawned(GameObject stain)
    {
        if (flags.Contains("practiceEnter")) flags.Add("firstStain");
    }

    // ===== 진행 =====

    void Update()
    {
        if (player == null) return;
        UpdatePathProgress();
        UpdateMoveKeys();

        // 대사가 끝난 컷의 onEnd 동작
        if (pendingOnEnd.Count > 0 && subtitle != null && subtitle.IsIdle)
        {
            foreach (var acts in pendingOnEnd) RunActions(acts);
            pendingOnEnd.Clear();
        }

        // 순서와 상관없는 이벤트
        foreach (var e in events)
            if (!firedEvents.Contains(e) && Satisfied(e)) { firedEvents.Add(e); Fire(e); }

        if (current >= beats.Count) return;

        // 앞질러 간 경우: 뒤 컷 중 자리·진행 조건이 이미 채워진 것이 있으면 그리로 건너뛴다
        // 단, 아직 안 나온 gate 컷(지금 기다리는 컷 포함)은 넘지 못한다 → 장면 3을 전실에 서서 통째로 건너뛰던 버그 (2026-10-23)
        for (int i = current + 1; i < beats.Count; i++)
        {
            if (beats[i - 1].gate) break;
            var b = beats[i];
            if (IsSkipAnchor(b) && Satisfied(b))
            {
                Debug.Log($"[튜토리얼 진행] 앞질러 감: 컷 {beats[current].id} → {b.id}");
                current = i;
                break;
            }
        }

        var beat = beats[current];
        if (Satisfied(beat))
        {
            Fire(beat);
            current++;
            waitTime = 0f;
            hintShown = false;
            return;
        }

        // 막힘 힌트: 자막이 비어 있는 동안 기다린 시간만 센다
        if (subtitle == null || subtitle.IsIdle) waitTime += Time.deltaTime;
        if (!hintShown && !string.IsNullOrEmpty(beat.hint) && waitTime >= data.hintDelay)
        {
            hintShown = true;
            subtitle?.Enqueue(data.speaker, beat.hint);
        }
    }

    // 가이드: 지금 기다리는 컷이 바뀌었을 때만 표시를 바꾼다 (진행 판정이 끝난 뒤라 같은 프레임에 반영)
    void LateUpdate()
    {
        if (focus == null || player == null) return;
        Beat want = current < beats.Count ? beats[current] : null;
        if (want == focusBeat) return;
        focusBeat = want;
        if (want != null && focusTargets.TryGetValue(want, out var go))
        {
            bool untilReach = want.focusUntil == "reach" || (string.IsNullOrEmpty(want.focusUntil) && want.when.type != "lookAt");
            focus.Show(go, untilReach);   // 얼룩처럼 이미 파괴됐으면 TutorialFocus가 알아서 끔
        }
        else focus.Hide();
    }

    static bool IsSkipAnchor(Beat b)
    {
        string t = b.when.type;
        return t == "enterZone" || t == "walk" || t == "caught" || t == "goalDone" || t == "event";
    }

    bool Satisfied(Beat b)
    {
        if (!string.IsNullOrEmpty(b.require) && !flags.Contains(b.require)) return false;
        var w = b.when;
        switch (w.type)
        {
            case "start": return Time.time - startTime >= w.value;
            case "afterPrev": return subtitle == null || subtitle.IsIdle;
            case "enterZone": return zones.TryGetValue(w.target, out var z) && z.Contains(player.position);
            case "walk": return pathProgress.TryGetValue(w.target, out var prog) && prog >= w.value;
            case "lookAt": return IsLookingAt(w.target, w.value > 0f ? w.value : 15f);
            case "caught": return dolls.TryGetValue(w.target, out var d) && d == null;   // 잡히면 Destroy → Unity null
            case "goalDone": return goals.TryGetValue(w.target, out var g) && g != null && g.IsDone;
            case "event": return flags.Contains(w.target);
            default: return false;
        }
    }

    void Fire(Beat b)
    {
        RunActions(b.onStart);
        if (subtitle != null)
            foreach (var line in b.lines) subtitle.Enqueue(data.speaker, line);
        if (b.onEnd != null && b.onEnd.Length > 0) pendingOnEnd.Add(b.onEnd);
    }

    void RunActions(string[] actions)
    {
        if (actions == null) return;
        foreach (var a in actions)
        {
            switch (a)
            {
                case "moveKeys":
                    if (moveKeys != null) { moveKeys.SetActive(true); moveKeysFrom = player.position; }
                    break;
                case "revealHud":
                    if (practice != null) practice.RevealHud();
                    break;
                case "leave":
                    if (practice != null) practice.Leave();
                    break;
                case "retry":
                    if (practice != null) practice.Retry(firedEvents.Select(e => e.id).ToArray());
                    break;
                case "wake:stand":
                    if (wakeUp != null) wakeUp.Stand();
                    break;
                default:
                    if (a.StartsWith("guide:")) RunGuideAction(a.Substring(6));
                    else if (a == "demo:gaze") { if (cupDemo != null) cupDemo.StartGaze(); }
                    else if (a == "demo:cup") { if (cupDemo != null) cupDemo.StartDemo(); }
                    else if (a.StartsWith("doll:")) RunDollAction(a.Substring(5));
                    else Debug.LogWarning($"[튜토리얼 진행] 모르는 동작: {a}", this);
                    break;
            }
        }
    }

    // doll:free:이름 / doll:freeze:이름 — 빈대 얼리기·풀기 (DollMove.SetFrozen)
    void RunDollAction(string s)
    {
        string[] p = s.Split(':');
        if (p.Length < 2) { Debug.LogWarning($"[튜토리얼 진행] 빈대 동작 형식: doll:free:이름 / doll:freeze:이름 ({s})", this); return; }
        if (!dolls.TryGetValue(p[1], out var d) || d == null) return;   // 이미 잡혔거나(파괴) 재시작 판에서 꺼진 빈대
        if (p[0] == "free") d.SetFrozen(false);
        else if (p[0] == "freeze") d.SetFrozen(true);
        else Debug.LogWarning($"[튜토리얼 진행] 모르는 빈대 동작: {s}", this);
    }

    void RunGuideAction(string s)
    {
        if (guide == null) return;
        string[] p = s.Split(':');
        switch (p[0])
        {
            case "leg": if (p.Length > 1) guide.RequestLeg(p[1]); break;
            case "face":
                var go = p.Length > 1 ? GameObject.Find(p[1]) : null;
                if (go != null) guide.FaceTarget(go.transform);
                else Debug.LogWarning($"[튜토리얼 진행] guide:face 대상 없음: {s}", this);
                break;
            case "facePlayer": guide.FacePlayer(); break;
            case "clipboard": guide.LookDown(); break;
            default: Debug.LogWarning($"[튜토리얼 진행] 모르는 한 주임 동작: {s}", this); break;
        }
    }

    // ===== 도우미 =====

    void UpdatePathProgress()
    {
        foreach (var kv in paths)
        {
            float p = kv.Value.Progress(player.position);
            if (p > pathProgress[kv.Key]) pathProgress[kv.Key] = p;   // 되돌아가도 줄지 않는다 (이미 들은 대사를 다시 틀지 않게)
        }
    }

    void UpdateMoveKeys()
    {
        if (moveKeys == null || !moveKeys.activeSelf) return;
        Vector3 d = player.position - moveKeysFrom; d.y = 0f;
        if (d.sqrMagnitude > 1.5f * 1.5f) moveKeys.SetActive(false);
    }

    Renderer FindLookTarget(string objName)
    {
        if (lookTargets.TryGetValue(objName, out var r) && r != null) return r;
        var go = GameObject.Find(objName);
        r = go != null ? go.GetComponentInChildren<Renderer>() : null;
        lookTargets[objName] = r;
        return r;
    }

    // 화면 가운데 근처(각도)에 대상이 있고 충분히 가까우면 "보고 있음". 가림은 따지지 않는다 (창·모니터 시연용)
    // 배타 판정 (2026-10-23): 데이터의 다른 바라보기 대상이 화면 가운데에 더 가까우면(같아도) 그쪽을 보는 것으로 친다
    // → 창·모니터가 한 화면에 들어오는 자리에서 3-3(모니터)·3-4(창)가 고개를 안 돌려도 함께 통과하던 문제
    bool IsLookingAt(string objName, float maxAngle)
    {
        if (cam == null) cam = Camera.main;
        var r = FindLookTarget(objName);
        if (cam == null || r == null) return false;
        float a = AngleFromCenter(r);
        if (a < 0f || a > maxAngle) return false;
        foreach (var kv in lookTargets)
        {
            if (kv.Key == objName || kv.Value == null || kv.Value == r) continue;
            float other = AngleFromCenter(kv.Value);
            if (other >= 0f && other <= a) return false;
        }
        return true;
    }

    // 카메라 정면과 대상(렌더러 가운데) 사이 각도. 바라보기 거리 밖이면 -1
    float AngleFromCenter(Renderer r)
    {
        Vector3 to = r.bounds.center - cam.transform.position;
        if (to.magnitude > lookMaxDistance) return -1f;
        return Vector3.Angle(cam.transform.forward, to);
    }
}
