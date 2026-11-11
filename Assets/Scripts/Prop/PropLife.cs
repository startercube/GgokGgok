using System;
using System.Collections.Generic;
using UnityEngine;

// 진짜 소품의 '생활감' — 촛불 떨림, 바람에 흔들림, 회전, 둥실, 조명 깜빡임, 발광 깜빡임(호박등).
// 변장 빈대(DollDisguise)는 메시·콜라이더만 복사하고 이 컴포넌트는 가져가지 않는다
// → 변장한 사물은 '시간이 멈춘' 모습이 된다 (위화감 A, 2026-11-02 시험 적용)
// 세계관: 빈대는 굳은 기억만 가져온다 → 먹힌 순간의 모습으로 굳어 있다
// 상세 「코드 설계 - 위화감(시간이 멈춘 사물)·웃음소리 방향」
// IReplayPausable 아님 — 리플레이에서도 진짜 소품은 흔들려야 같은 장면이 된다
[DisallowMultipleComponent]
public class PropLife : MonoBehaviour
{
    public enum MotionType { Sway, Spin, Bob, Pulse }

    [Serializable]
    public class Motion
    {
        [Tooltip("움직일 대상. 비우면 이 오브젝트. 콜라이더가 없는 자식(불꽃·잎)을 권장 — 콜라이더가 같이 움직이면 물리 비용이 든다")]
        public Transform target;
        [Tooltip("Sway = 진자처럼 흔들림 / Spin = 계속 회전 / Bob = 위아래 둥실 / Pulse = 커졌다 작아짐. 대상 하나에 회전 종류(Sway·Spin)는 하나만")]
        public MotionType type = MotionType.Sway;
        [Tooltip("대상의 로컬 축. Sway·Spin = 회전축, Bob = 움직이는 방향, Pulse는 무시")]
        public Vector3 axis = Vector3.forward;
        [Tooltip("Sway = 각도(도), Bob = 거리(m), Pulse = 크기 비율(0.05 = ±5%), Spin은 무시")]
        public float amplitude = 3f;
        [Tooltip("한 번 왕복(Spin은 한 바퀴)하는 데 걸리는 초")]
        [Min(0.05f)] public float period = 1f;
        [Tooltip("불규칙 떨림 비율. 0이면 깔끔한 진자, 촛불은 0.5 이상")]
        [Range(0f, 1f)] public float jitter = 0f;

        [NonSerialized] public Transform resolved;
        [NonSerialized] public Vector3 restPos;
        [NonSerialized] public Quaternion restRot;
        [NonSerialized] public Vector3 restScale;
    }

    [Serializable]
    public class Flicker
    {
        public Light light;
        [Tooltip("가장 어두울 때 기본 세기의 몇 배인지")]
        [Range(0f, 1f)] public float minRatio = 0.7f;
        [Tooltip("가장 밝을 때 기본 세기의 몇 배인지. min·max 평균이 1이면 진짜의 평균 밝기 = 변장 조명의 고정 밝기 (밝기로는 구분 불가)")]
        [Range(1f, 2f)] public float maxRatio = 1f;
        [Tooltip("깜빡임 빠르기 (클수록 빠르게 떨림)")]
        [Min(0.1f)] public float speed = 8f;

        [NonSerialized] public float baseIntensity;
    }

    // 재질 발광 세기 깜빡임 (2026-11-02, 「코드 설계 - 모든 호박등 통일·발광 재질 인스턴스」)
    // 첫 Update에서 이 렌더러의 발광 재질만 복사본으로 바꾸고 복사본의 _EmissionColor를 Material.SetColor로 바꾼다.
    //  · 블록(MaterialPropertyBlock.SetColor)은 감마→선형 변환을 한 번 더 해서 HDR 발광(가로등 767)이 폭발했다
    //    → 읽기·쓰기를 같은 Material API로 해야 변환이 대칭이 된다
    //  · 변장 복사(DollMove.Awake)는 첫 Update보다 먼저라 원본 공유 재질을 가져감 → 변장은 기본 밝기로 고정 = '멈춘 불빛'
    [Serializable]
    public class EmissionFlicker
    {
        public Renderer renderer;
        [Tooltip("가장 어두울 때 기본 발광의 몇 배")]
        [Range(0f, 1f)] public float minRatio = 0.3f;
        [Tooltip("가장 밝을 때 기본 발광의 몇 배. min·max 평균이 1이면 진짜의 평균 밝기 = 변장의 고정 밝기")]
        [Range(1f, 3f)] public float maxRatio = 1.7f;
        [Tooltip("깜빡임 빠르기")]
        [Min(0.1f)] public float speed = 6f;

        [NonSerialized] public int[] slots;            // 발광이 켜진 재질 칸
        [NonSerialized] public Color[] baseColors;     // 칸별 기본 발광 색
        [NonSerialized] public Material[] originals;   // 원래 공유 재질 (복원용)
        [NonSerialized] public Material[] instances;   // 바꿔 끼운 재질 배열 (발광 칸만 복사본)
    }

    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [SerializeField] private List<Motion> motions = new List<Motion>();
    [SerializeField] private List<Flicker> flickers = new List<Flicker>();
    [SerializeField] private List<EmissionFlicker> emissions = new List<EmissionFlicker>();
    [Tooltip("어떤 카메라에도 안 보이면 계산을 건너뛴다 (보이지 않는 움직임은 단서가 아니다)")]
    [SerializeField] private bool skipWhenInvisible = true;

    private Renderer[] renderers;
    private float phase;           // 소품 위치에서 계산 → 같은 자리 = 같은 위상 = 리플레이에서도 똑같이 흔들림
    private bool restCaptured;

    public bool HasLife => motions.Count > 0 || flickers.Count > 0 || emissions.Count > 0;

    /// <summary>도구가 장식 불빛에서 끈다 — 조명은 소품이 화면 밖이어도 벽·바닥을 비추므로 계속 일렁여야 한다</summary>
    public bool SkipWhenInvisible { get => skipWhenInvisible; set => skipWhenInvisible = value; }
    public IReadOnlyList<Motion> Motions => motions;

    /// <summary>도구(PropLifeInstaller)가 기본값을 넣을 때 쓴다. 기존 값은 덮어쓴다</summary>
    public void Configure(IEnumerable<Motion> newMotions, IEnumerable<Flicker> newFlickers = null,
                          IEnumerable<EmissionFlicker> newEmissions = null)
    {
        motions = newMotions != null ? new List<Motion>(newMotions) : new List<Motion>();
        flickers = newFlickers != null ? new List<Flicker>(newFlickers) : new List<Flicker>();
        emissions = newEmissions != null ? new List<EmissionFlicker>(newEmissions) : new List<EmissionFlicker>();
    }

    // 변장 복사(DollMove.Awake)보다 먼저든 나중이든 상관없다 — 움직임은 Update에서만 생기므로 복사되는 건 항상 휴식 자세
    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        Vector3 p = transform.position;
        phase = Mathf.Abs(p.x * 12.9898f + p.y * 78.233f + p.z * 37.719f) % 1000f;

        foreach (Motion m in motions)
        {
            m.resolved = m.target != null ? m.target : transform;
            m.restPos = m.resolved.localPosition;
            m.restRot = m.resolved.localRotation;
            m.restScale = m.resolved.localScale;
        }
        foreach (Flicker f in flickers)
            if (f.light != null) f.baseIntensity = f.light.intensity;

        foreach (EmissionFlicker e in emissions)
            CaptureEmission(e);

        restCaptured = true;
    }

    void Update()
    {
        if (!restCaptured) return;
        if (skipWhenInvisible && !AnyVisible()) return;

        float t = Time.time;   // ESC 일시정지(timeScale 0)·리플레이 배속을 그대로 따른다
        for (int i = 0; i < motions.Count; i++)
            ApplyMotion(motions[i], t, i);

        for (int i = 0; i < flickers.Count; i++)
        {
            Flicker f = flickers[i];
            if (f.light == null) continue;
            float noise = Mathf.PerlinNoise(phase + i * 17.3f, t * f.speed);
            f.light.intensity = f.baseIntensity * Mathf.Lerp(f.minRatio, f.maxRatio, noise);
        }

        for (int i = 0; i < emissions.Count; i++)
        {
            EmissionFlicker e = emissions[i];
            if (e.renderer == null || e.slots == null || e.slots.Length == 0) continue;
            EnsureInstances(e);
            float noise = Mathf.PerlinNoise(phase + 53.1f + i * 11.7f, t * e.speed);
            SetEmission(e, Mathf.Lerp(e.minRatio, e.maxRatio, noise));
        }
    }

    // 발광 키워드가 켜진 재질 칸만 고른다 (팔레트 재질 M_Universal_A처럼 발광 맵이 있는 경우)
    void CaptureEmission(EmissionFlicker e)
    {
        if (e.renderer == null) return;
        Material[] mats = e.renderer.sharedMaterials;
        var slots = new List<int>();
        var colors = new List<Color>();
        for (int i = 0; i < mats.Length; i++)
        {
            Material m = mats[i];
            if (m == null || !m.HasProperty(EmissionColorId) || !m.IsKeywordEnabled("_EMISSION")) continue;
            slots.Add(i);
            colors.Add(m.GetColor(EmissionColorId));
        }
        e.slots = slots.ToArray();
        e.baseColors = colors.ToArray();
        e.originals = mats;

        // 발광 칸이 없으면 일렁임이 조용히 아무 일도 안 한다 — 콘솔에서 바로 알 수 있게 (2026-11-02 호박등 사례)
        if (e.slots.Length == 0)
            Debug.LogWarning($"[위화감] '{name}'의 '{e.renderer.name}' 재질에 발광(_EMISSION)이 없어 발광 일렁임이 보이지 않습니다", this);
    }

    // 발광 칸만 복사본으로 — 나머지 칸은 공유 재질 그대로
    static void EnsureInstances(EmissionFlicker e)
    {
        if (e.instances != null) return;
        e.instances = (Material[])e.originals.Clone();
        foreach (int slot in e.slots)
            e.instances[slot] = new Material(e.originals[slot]) { name = e.originals[slot].name + " (PropLife)" };
        e.renderer.sharedMaterials = e.instances;
    }

    static void SetEmission(EmissionFlicker e, float ratio)
    {
        for (int k = 0; k < e.slots.Length; k++)
            e.instances[e.slots[k]].SetColor(EmissionColorId, e.baseColors[k] * ratio);
    }

    // 원본 재질로 되돌리고 복사본은 파괴 (재질 누수 방지, 플레이 종료 뒤 씬이 그대로)
    static void ReleaseInstances(EmissionFlicker e)
    {
        if (e.instances == null) return;
        if (e.renderer != null) e.renderer.sharedMaterials = e.originals;
        foreach (int slot in e.slots)
            if (e.instances[slot] != null) Destroy(e.instances[slot]);
        e.instances = null;
    }

    // 잡혀서 꺼지거나 리플레이 전환으로 꺼질 때 휴식 자세로 되돌린다
    void OnDisable()
    {
        if (!restCaptured) return;
        foreach (Motion m in motions)
        {
            if (m.resolved == null) continue;
            m.resolved.localPosition = m.restPos;
            m.resolved.localRotation = m.restRot;
            m.resolved.localScale = m.restScale;
        }
        foreach (Flicker f in flickers)
            if (f.light != null) f.light.intensity = f.baseIntensity;
        foreach (EmissionFlicker e in emissions)
            ReleaseInstances(e);
    }

    bool AnyVisible()
    {
        foreach (Renderer r in renderers)
            if (r != null && r.enabled && r.isVisible) return true;
        return false;
    }

    // 휴식 자세를 기준으로 매번 덮어쓴다 → 프레임이 쌓여도 오차가 누적되지 않음
    void ApplyMotion(Motion m, float t, int index)
    {
        if (m.resolved == null) return;
        Vector3 axis = m.axis.sqrMagnitude > 1e-6f ? m.axis.normalized : Vector3.up;
        float ph = phase + index * 1.7f;

        switch (m.type)
        {
            case MotionType.Sway:
                m.resolved.localRotation = m.restRot * Quaternion.AngleAxis(m.amplitude * Wave(m, t, ph), axis);
                break;
            case MotionType.Spin:
                float angle = Mathf.Repeat((t / m.period) * 360f + ph * 57.3f, 360f);
                m.resolved.localRotation = m.restRot * Quaternion.AngleAxis(angle, axis);
                break;
            case MotionType.Bob:
                m.resolved.localPosition = m.restPos + (m.restRot * axis) * (m.amplitude * Wave(m, t, ph));
                break;
            case MotionType.Pulse:
                m.resolved.localScale = m.restScale * (1f + m.amplitude * Wave(m, t, ph));
                break;
        }
    }

    // -1~1 사인파. jitter만큼 2.7배 빠른 작은 파를 섞어 촛불처럼 일정하지 않게
    static float Wave(Motion m, float t, float ph)
    {
        float a = t * (2f * Mathf.PI / m.period) + ph;
        float clean = Mathf.Sin(a);
        if (m.jitter <= 0f) return clean;
        float rough = 0.55f * clean + 0.45f * Mathf.Sin(a * 2.7f + 1.3f);
        return Mathf.Lerp(clean, rough, m.jitter);
    }

    // ── 프리셋 (컴포넌트 ⋮ 메뉴) ─────────────────────────────
    [ContextMenu("프리셋: 촛불 불꽃")]
    void PresetCandle()
    {
        RecordForUndo("촛불 불꽃");
        Transform flame = FindChildByKeywords("flame", "fire", "불꽃", "불");
        motions.Clear();
        motions.Add(new Motion { target = flame, type = MotionType.Sway, axis = Vector3.forward,
                                 amplitude = flame != null ? 6f : 2f, period = 0.9f, jitter = 0.6f });
        flickers.Clear();
        foreach (Light l in GetComponentsInChildren<Light>(true))
            flickers.Add(new Flicker { light = l });
        MarkDirty();
    }

    [ContextMenu("프리셋: 바람에 흔들림")]
    void PresetWind()
    {
        RecordForUndo("바람에 흔들림");
        motions.Clear();
        motions.Add(new Motion { type = MotionType.Sway, axis = Vector3.forward, amplitude = 2.5f, period = 3.2f, jitter = 0.25f });
        MarkDirty();
    }

    [ContextMenu("프리셋: 천천히 회전")]
    void PresetSpin()
    {
        RecordForUndo("천천히 회전");
        motions.Clear();
        motions.Add(new Motion { type = MotionType.Spin, axis = Vector3.up, period = 6f });
        MarkDirty();
    }

    [ContextMenu("프리셋: 둥실")]
    void PresetBob()
    {
        RecordForUndo("둥실");
        motions.Clear();
        motions.Add(new Motion { type = MotionType.Bob, axis = Vector3.up, amplitude = 0.03f, period = 2.5f });
        MarkDirty();
    }

    Transform FindChildByKeywords(params string[] keywords)
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child == transform) continue;
            string n = child.name.ToLowerInvariant();
            foreach (string k in keywords)
                if (n.Contains(k)) return child;
        }
        return null;
    }

    void RecordForUndo(string label)
    {
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "생활감 프리셋: " + label);
#endif
    }

    void MarkDirty()
    {
#if UNITY_EDITOR
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
