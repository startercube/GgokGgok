using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// 위화감 기본 생활감 적용 도구 (2026-11-02)
// Tools > 위화감 > 기본 생활감 적용 / 제거 (현재 씬)
// 규칙표대로 진짜 소품에 생활감을 붙인다. 변장 빈대는 복사 때 이것들을 '멈춘' 상태로 가져간다
//  · 인형 방: "걸으면 진짜 물건은 전부 반응한다" — 봉제 = 폭신, 단단한 물건 = 덜컥, 오뚝이 = 오래 흔들림, 주전자·찻잔 = 김 + 달그락
//  · 할로윈: "지켜보면 보인다" — 해골양초 = 불꽃 / 허수아비 = 바람 / 가마솥 = 연두 김 / 호박등 = 얼굴 발광 일렁임 / 마법책 = 마법 먼지
//    (호박·사탕바구니·마녀모자는 일부러 비워 둔 '단서 없는 은신처')
// 다시 실행해도 같은 결과 — 도구가 만든 자식(이름 PropLife_…)과 생활감 컴포넌트를 지우고 새로 붙인다
// 저장은 사용자(Ctrl+S). Ctrl+Z 한 번에 전부 취소
// 상세 「코드 설계 - 반응하는 인형·기본 생활감 적용 도구」, 「코드 설계 - 생활감 확장(단단한 물건·호박등·마법책)」,
//      「코드 설계 - 호박등 발광 깜빡임·인형 방 물결 반응」
public static class PropLifeInstaller
{
    const string ToolChildPrefix = "PropLife_";
    const string SettingsFolder = "Assets/Settings/PropLife";
    const string SteamMaterialPath = SettingsFolder + "/M_PropLife_Steam.mat";
    const string SoftTexturePath = SettingsFolder + "/T_PropLife_Soft.png";

    // 촛불마다 작은 깜빡이는 조명 — 할로윈 씬 조명 수가 이미 많아 기본은 끔 (static readonly: 끈 상태에서도 '도달 불가 코드' 경고가 안 나게)
    static readonly bool AddCandleLight = false;

    // 반응 모양 — 같은 PropReaction, 값만 다르다 (「코드 설계 - 생활감 확장」 3장 표)
    struct ReactionLook
    {
        public float squash, tilt, duration, wobbles, radius;
        public ReactionLook(float squash, float tilt, float duration, float wobbles, float radius)
        {
            this.squash = squash; this.tilt = tilt; this.duration = duration; this.wobbles = wobbles; this.radius = radius;
        }
    }
    // 2026-11-02 물결 반응: 값 키움. 반경은 4m → 2.5m (사용자 결정, 「코드 설계 - 호박등 불 켜진 모델·장식 불빛 일렁임·발걸음 반경 2.5m」)
    const float ReactRadius = 2.5f;
    static readonly ReactionLook Soft = new ReactionLook(0.18f, 9f, 1f, 2.5f, ReactRadius);     // 봉제 인형: 폭신
    static readonly ReactionLook Rattle = new ReactionLook(0f, 10f, 0.6f, 3f, ReactRadius);     // 단단한 물건: 덜컥
    static readonly ReactionLook RollPoly = new ReactionLook(0f, 14f, 1.8f, 4f, ReactRadius);   // 오뚝이: 오래 흔들리다 섬

    // 장식 불빛: 바깥 프리팹 이름에 이 글자가 들어간 것의 조명·발광을 일렁이게 한다 (불 켜진 호박등·촛대·가로등)
    static readonly string[] DecorLightKeywords = { "_Lit", "Lantern" };
    // 일렁이지 않게 둘 장식 — 가로등은 잡을 수 없고 크고 밝아 일렁이면 눈만 아프다 (사용자 2026-11-02)
    // 수집은 하되 이전 실행에서 붙은 일렁임을 지운다. 촛대도 빼려면 "Candle" 추가
    static readonly string[] ExcludedDecorKeywords = { "Lantern" };
    const string DecorPumpkinPrefix = "Pumpkin_Carved_Lit";

    enum DecorKind { Pumpkin, Flame, Excluded }

    // 불 일렁임 공통 값 — 모든 호박등·가로등·촛대가 똑같이 (「코드 설계 - 모든 호박등 통일·발광 재질 인스턴스」)
    // 조명 0.6~1.4 → 평균 = 기본 세기 = 변장 조명의 고정 세기 (밝기로는 구분 불가, 지켜봐야 구분)
    const float FlameLightMin = 0.6f, FlameLightMax = 1.4f, FlameLightSpeed = 8f;
    const float FlameEmissionMin = 0.3f, FlameEmissionMax = 1.7f, FlameEmissionSpeed = 6f;

    // 표준 호박등 조명 — 장식 호박등 7개 중 다수 값. 잡기 대상·장식 호박등 모두 이 값으로 통일
    static readonly Color PumpkinLightColor = new Color(0.8679245f, 0.39920625f, 0f);
    const float PumpkinLightIntensity = 4.8f;
    const float PumpkinLightRange = 5f;
    const string UnlitPumpkinPrefix = "SM_Pumpkin_Carved_";
    const string LitPumpkinPrefix = "SM_Pumpkin_Carved_Lit_";

    // 파티클 모양 — 김·연두 김·마법 먼지가 같은 생성 함수를 쓴다
    class ParticleLook
    {
        public string name;
        public Color color = Color.white;
        public float alpha = 0.35f;
        public bool sizeInMeters;                 // false면 소품 폭에 대한 비율
        public float sizeMin = 0.35f, sizeMax = 0.6f;
        public float lifeMin = 1.4f, lifeMax = 2.2f;
        public float speedMin = 0.06f, speedMax = 0.14f;
        public float rateMin = 5f, rateMax = 12f;  // 작은 소품 → 큰 소품
        public float coneAngle = 10f;
        public float radiusRatio = 0.2f;
        public float noise = 0.05f;
        public int maxParticles = 40;
    }
    static readonly ParticleLook Steam = new ParticleLook { name = "Steam" };
    static readonly ParticleLook GreenSteam = new ParticleLook { name = "Steam", color = new Color(0.75f, 1f, 0.7f) };
    static readonly ParticleLook MagicDust = new ParticleLook
    {
        name = "MagicDust", color = new Color(0.8f, 0.6f, 1f), alpha = 0.9f,
        sizeInMeters = true, sizeMin = 0.015f, sizeMax = 0.03f,
        lifeMin = 1.5f, lifeMax = 2.5f, speedMin = 0.02f, speedMax = 0.06f,
        rateMin = 6f, rateMax = 6f, coneAngle = 35f, radiusRatio = 0.35f, noise = 0.04f, maxParticles = 30,
    };

    class Rule
    {
        public string label;
        public Func<GameObject, bool> match;
        public Action<GameObject> apply;
    }

    // 위에서부터 처음 맞는 규칙 하나만 적용한다(한 규칙이 여러 생활감을 겹쳐 붙일 수는 있다).
    // 원본 프리팹 이름을 먼저 보므로 할로윈 '호박인형'은 인형 규칙에 걸리지 않는다
    static List<Rule> BuildRules() => new List<Rule>
    {
        // 인형 방
        new Rule { label = "인형 (폭신)",                  match = go => SourceName(go) == "doll",               apply = go => AddReaction(go, Soft) },
        new Rule { label = "동료 인형 (폭신)",             match = go => SourceName(go).StartsWith("DR_Buddy_"), apply = go => AddReaction(go, Soft) },
        new Rule { label = "오뚝이 (오래 흔들림)",         match = go => SourceName(go) == "DR_Toy_RolyPoly",    apply = go => AddReaction(go, RollPoly) },
        new Rule { label = "저금통·자동차·오르골 (덜컥)",  match = go => SourceIs(go, "DR_Toy_Piggy", "DR_Toy_Car", "DR_Toy_MusicBox"),
                   apply = go => AddReaction(go, Rattle) },
        new Rule { label = "주전자·찻잔 (김 + 달그락)",    match = go => NameStarts(go, "주전자", "찻잔"),
                   apply = go => { AddParticles(go, Steam); AddReaction(go, Rattle); } },
        new Rule { label = "블록·공·쟁반 (덜컥)",          match = IsHardToy,                                    apply = go => AddReaction(go, Rattle) },
        // 할로윈 방
        new Rule { label = "해골양초 (불꽃)",              match = go => NameStarts(go, "해골양초"),             apply = AddCandleFire },
        new Rule { label = "허수아비 (바람)",              match = go => NameStarts(go, "허수아비"),
                   apply = go => AddSway(go, PropReaction.FindVisualRoot(go.transform), 2f, 3.5f, 0.3f) },
        new Rule { label = "가마솥 (연두 김)",             match = go => NameStarts(go, "가마솥"),               apply = go => AddParticles(go, GreenSteam) },
        new Rule { label = "호박등 (불 켜진 모델 + 조명 + 일렁임)", match = IsJackOLantern,
                   apply = go => { SwapPumpkinMesh(go, true); EnsurePumpkinLight(go); AddFlameLife(go); } },
        new Rule { label = "마법책 (마법 먼지)",           match = go => NameStarts(go, "마법책"),               apply = go => AddParticles(go, MagicDust) },
    };

    // 조각 호박등 — 이름이 "호박인형"(씬 오브젝트) 또는 원본 프리팹이 Pumpkin_Carved_Lit(Prop 태그가 덮어씌워진 잡기 대상) 또는 조각 호박 메시.
    // 불 켜진 호박등 7개는 Prop이라 장식 불빛 수집에서도, 이름 규칙에서도 빠졌었다 (2026-11-02, 「코드 설계 - 모든 호박등 통일」 11장)
    static bool IsJackOLantern(GameObject go)
        => NameStarts(go, "호박인형")
        || SourceName(go).StartsWith(DecorPumpkinPrefix, StringComparison.Ordinal)
        || MeshName(go).StartsWith("SM_Pumpkin_Carved", StringComparison.Ordinal);

    // 블록·공·쟁반은 씬 오브젝트라 원본 프리팹 이름이 없다 → 메시 이름을 먼저, 없으면 이름(가구 '블록성'과 헷갈리지 않게 정확한 이름만)
    static bool IsHardToy(GameObject go)
    {
        string mesh = MeshName(go);
        if (mesh.StartsWith("DR_Prop_Block") || mesh.StartsWith("DR_Prop_Ball") || mesh.StartsWith("DR_Prop_Tray")) return true;
        string baseName = BaseName(go);
        return baseName == "블록" || baseName == "공" || baseName == "쟁반";
    }

    // ── 메뉴 ─────────────────────────────────────────────

    [MenuItem("Tools/위화감/기본 생활감 적용 (현재 씬)")]
    static void ApplyDefaults()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("기본 생활감 적용", "플레이 중에는 실행할 수 없습니다.", "확인");
            return;
        }

        List<Rule> rules = BuildRules();
        var plan = new List<(GameObject go, Rule rule)>();
        foreach (GameObject go in CollectCandidates())
        {
            Rule rule = rules.FirstOrDefault(r => r.match(go));
            if (rule != null) plan.Add((go, rule));
        }

        List<(GameObject root, DecorKind kind)> decorAll = CollectDecorLights();
        var decor = decorAll.Where(d => d.kind != DecorKind.Excluded).ToList();
        var excludedWithLife = decorAll.Where(d => d.kind == DecorKind.Excluded && d.root.GetComponent<PropLife>() != null)
                                       .Select(d => d.root).ToList();

        if (plan.Count == 0 && decor.Count == 0 && excludedWithLife.Count == 0)
        {
            EditorUtility.DisplayDialog("기본 생활감 적용", "이 씬에는 규칙에 맞는 소품이 없습니다.", "확인");
            return;
        }

        string summary = string.Join("\n", plan.GroupBy(p => p.rule.label).Select(g => $"{g.Key}: {g.Count()}개"));
        int decorPumpkins = decor.Count(d => d.kind == DecorKind.Pumpkin);
        if (decorPumpkins > 0)
            summary += (summary.Length > 0 ? "\n" : "") + $"장식 호박등 (표준 조명 + 일렁임): {decorPumpkins}개";
        if (decor.Count - decorPumpkins > 0)
            summary += (summary.Length > 0 ? "\n" : "") + $"장식 불빛 촛대 등 (일렁임): {decor.Count - decorPumpkins}개";
        if (excludedWithLife.Count > 0)
            summary += (summary.Length > 0 ? "\n" : "") + $"가로등 일렁임 제거 (원래대로): {excludedWithLife.Count}개";
        if (!EditorUtility.DisplayDialog("기본 생활감 적용",
                summary + "\n\n이미 붙어 있던 생활감은 지우고 다시 붙입니다.", "적용", "취소"))
            return;

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("기본 생활감 적용");

        foreach (var (go, rule) in plan)
        {
            ClearLife(go);
            rule.apply(go);
        }
        foreach (var (root, kind) in decor)
        {
            if (kind == DecorKind.Pumpkin) EnsurePumpkinLight(root);
            AddFlameLife(root);
        }
        foreach (GameObject root in excludedWithLife)
            Undo.DestroyObjectImmediate(root.GetComponent<PropLife>());

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[위화감] 기본 생활감 적용 — {summary.Replace("\n", " / ")} (저장은 Ctrl+S, 확인은 Tools > 위화감 > 현재 씬 생활감 검사)");
    }

    [MenuItem("Tools/위화감/기본 생활감 제거 (현재 씬)")]
    static void RemoveAll()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("기본 생활감 제거", "플레이 중에는 실행할 수 없습니다.", "확인");
            return;
        }

        List<GameObject> targets = CollectCandidates().Where(HasAnyLife).ToList();
        List<GameObject> decor = CollectDecorLights().Select(d => d.root).Where(r => r.GetComponent<PropLife>() != null).ToList();
        targets.AddRange(decor);
        if (targets.Count == 0)
        {
            EditorUtility.DisplayDialog("기본 생활감 제거", "생활감이 붙은 소품이 없습니다.", "확인");
            return;
        }
        if (!EditorUtility.DisplayDialog("기본 생활감 제거",
                $"소품 {targets.Count}개에서 생활감(PropLife·PropReaction·도구가 만든 김·불꽃)을 지웁니다.", "제거", "취소"))
            return;

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("기본 생활감 제거");
        foreach (GameObject go in targets)
        {
            ClearLife(go);
            if (NameStarts(go, "호박인형")) SwapPumpkinMesh(go, false);   // 불 켜진 모델 → 원래 모델
        }
        Undo.CollapseUndoOperations(group);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[위화감] 기본 생활감 제거 — 소품 {targets.Count}개 (저장은 Ctrl+S)");
    }

    // ── 대상 찾기 ─────────────────────────────────────────

    // 변장 후보와 같은 기준: 현재 씬, Prop 태그, 빈대 밑이 아님, 최상위 Prop. 꺼진 것도 포함(같은 종류는 전부 같게)
    static List<GameObject> CollectCandidates()
    {
        Scene scene = SceneManager.GetActiveScene();
        var result = new List<GameObject>();
        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (go.scene != scene || !go.CompareTag("Prop")) continue;
            Transform t = go.transform;
            if (t.GetComponentInParent<DollMove>(true) != null) continue;
            if (t.parent != null && t.parent.CompareTag("Prop")) continue;
            result.Add(go);
        }
        return result;
    }

    static string SourceName(GameObject go)
    {
        GameObject src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
        return src != null ? src.name : "";
    }

    static bool NameStarts(GameObject go, params string[] prefixes)
    {
        foreach (string p in prefixes)
            if (go.name.StartsWith(p, StringComparison.Ordinal)) return true;
        return false;
    }

    static bool SourceIs(GameObject go, params string[] names)
    {
        string src = SourceName(go);
        foreach (string n in names)
            if (src == n) return true;
        return false;
    }

    // "블록 (12)" → "블록"
    static string BaseName(GameObject go)
    {
        string n = go.name;
        int paren = n.LastIndexOf(" (", StringComparison.Ordinal);
        return paren > 0 && n.EndsWith(")") ? n.Substring(0, paren) : n;
    }

    // 도구가 만든 자식을 뺀 첫 메시 이름
    static string MeshName(GameObject go)
    {
        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || mf.name.StartsWith(ToolChildPrefix, StringComparison.Ordinal)) continue;
            return mf.sharedMesh.name;
        }
        return "";
    }

    static bool HasAnyLife(GameObject go)
    {
        if (go.GetComponent<PropLife>() != null || go.GetComponent<PropReaction>() != null) return true;
        return ToolChildren(go).Count > 0;
    }

    static List<GameObject> ToolChildren(GameObject go)
    {
        var list = new List<GameObject>();
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            if (t != go.transform && t.name.StartsWith(ToolChildPrefix, StringComparison.Ordinal))
                list.Add(t.gameObject);
        return list;
    }

    static void ClearLife(GameObject go)
    {
        foreach (GameObject child in ToolChildren(go))
            if (child != null) Undo.DestroyObjectImmediate(child);   // 자식의 자식도 함께 지워지므로 null 확인
        PropLife life = go.GetComponent<PropLife>();
        if (life != null) Undo.DestroyObjectImmediate(life);
        PropReaction reaction = go.GetComponent<PropReaction>();
        if (reaction != null) Undo.DestroyObjectImmediate(reaction);
    }

    // ── 규칙 적용 ─────────────────────────────────────────

    static void AddReaction(GameObject go, ReactionLook look)
    {
        PropReaction r = Undo.AddComponent<PropReaction>(go);
        r.Configure(look.squash, look.radius, look.tilt, look.duration, look.wobbles);
        MarkModified(r);
    }

    // 흔들림 축은 '세계 앞쪽'을 대상의 로컬로 바꿔서 쓴다 → 모델이 돌아가 있어도 항상 수평축으로 좌우로 흔들림
    static void AddSway(GameObject go, Transform target, float degrees, float period, float jitter)
    {
        PropLife life = Undo.AddComponent<PropLife>(go);
        Vector3 axis = target.InverseTransformDirection(Vector3.forward);
        life.Configure(new[]
        {
            new PropLife.Motion
            {
                target = target == go.transform ? null : target,
                type = PropLife.MotionType.Sway,
                axis = axis,
                amplitude = degrees,
                period = period,
                jitter = jitter,
            }
        });
        MarkModified(life);
    }

    static void AddParticles(GameObject go, ParticleLook look)
    {
        if (!TryGetMeshBounds(go, out Bounds b))
        {
            Debug.LogWarning($"[위화감] '{go.name}'에 메시가 없어 {look.name} 파티클을 붙이지 않았습니다", go);
            return;
        }

        GameObject fx = new GameObject(ToolChildPrefix + look.name);
        Undo.RegisterCreatedObjectUndo(fx, look.name);
        fx.transform.SetParent(go.transform, false);
        fx.transform.position = new Vector3(b.center.x, b.max.y + 0.01f, b.center.z);
        fx.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);   // 파티클 원뿔은 로컬 +Z로 뿜는다 → 위를 보게

        float w = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z), 0.05f, 1.5f);   // 소품 폭에 맞춰 크기·개수·속도
        float sizeT = Mathf.InverseLerp(0.1f, 1f, w);
        float speedScale = Mathf.Lerp(1f, 2f, sizeT);
        float sizeScale = look.sizeInMeters ? 1f : w;

        ParticleSystem ps = fx.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.prewarm = true;                 // 시작부터 피어 있게 (변장 복사본도 이 상태에서 멈춤)
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(look.lifeMin, look.lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(look.speedMin * speedScale, look.speedMax * speedScale);
        main.startSize = new ParticleSystem.MinMaxCurve(look.sizeMin * sizeScale, look.sizeMax * sizeScale);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(look.color.r, look.color.g, look.color.b, look.alpha);
        main.maxParticles = look.maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // 변장 복사본은 Local로 바뀌어 빈대에 붙어 다닌다
        main.scalingMode = ParticleSystemScalingMode.Local;           // 부모 크기 무시 → 크기 값이 곧 미터

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = Mathf.Lerp(look.rateMin, look.rateMax, sizeT);

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = look.coneAngle;
        shape.radius = w * look.radiusRatio;

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
        col.color = gradient;

        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.5f));

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = look.noise * speedScale;
        noise.frequency = 0.8f;
        noise.scrollSpeed = 0.3f;

        ParticleSystemRenderer renderer = fx.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = GetSteamMaterial();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // 호박인형 보이는 메시를 불 켜진 모델(얼굴 UV가 발광 칸)로 바꾸거나 되돌린다.
    // 충돌 메시는 그대로 — 모양이 같고, 불 꺼진 FBX만 Read/Write가 켜져 있어 변장 런타임 복사에 안전하다
    static void SwapPumpkinMesh(GameObject go, bool toLit)
    {
        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || mf.name.StartsWith(ToolChildPrefix, StringComparison.Ordinal)) continue;
            string path = AssetDatabase.GetAssetPath(mf.sharedMesh);
            if (string.IsNullOrEmpty(path)) continue;
            string file = Path.GetFileNameWithoutExtension(path);

            string target;
            if (toLit && file.StartsWith(UnlitPumpkinPrefix, StringComparison.Ordinal) && !file.StartsWith(LitPumpkinPrefix, StringComparison.Ordinal))
                target = LitPumpkinPrefix + file.Substring(UnlitPumpkinPrefix.Length);
            else if (!toLit && file.StartsWith(LitPumpkinPrefix, StringComparison.Ordinal))
                target = UnlitPumpkinPrefix + file.Substring(LitPumpkinPrefix.Length);
            else
                continue;   // 이미 원하는 모델

            string targetPath = (Path.GetDirectoryName(path) + "/" + target + Path.GetExtension(path)).Replace('\\', '/');
            Mesh mesh = AssetDatabase.LoadAllAssetsAtPath(targetPath).OfType<Mesh>().FirstOrDefault();
            if (mesh == null)
            {
                Debug.LogWarning($"[위화감] '{go.name}': 짝 모델 '{targetPath}'을 찾지 못해 메시를 바꾸지 않았습니다", go);
                continue;
            }
            Undo.RecordObject(mf, "호박등 모델 교체");
            mf.sharedMesh = mesh;
            MarkModified(mf);
        }
    }

    // 호박등 조명: 없으면 PropLife_LanternLight를 만들고, 있으면 표준 값으로 맞춘다 (모든 호박등 통일)
    // 변장은 자식 조명을 '멈춘 조명'으로 복사하므로(DollDisguise.CopyFrozenLights) 변장 호박등도 빛난다
    static void EnsurePumpkinLight(GameObject go)
    {
        List<Light> lights = go.GetComponentsInChildren<Light>(true)
            .Where(l => l.type == LightType.Point || l.type == LightType.Spot).ToList();

        if (lights.Count == 0)
        {
            if (!TryGetMeshBounds(go, out Bounds b)) return;
            GameObject lightGo = new GameObject(ToolChildPrefix + "LanternLight");
            Undo.RegisterCreatedObjectUndo(lightGo, "호박등 조명");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.position = b.center + Vector3.up * (b.size.y * 0.25f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            lights.Add(light);
        }

        foreach (Light light in lights)
        {
            Undo.RecordObject(light, "호박등 조명 표준 값");
            light.type = LightType.Point;
            light.color = PumpkinLightColor;
            light.intensity = PumpkinLightIntensity;
            light.range = PumpkinLightRange;
            light.shadows = LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Realtime;   // 실행 중 세기 변경이 보이게
            MarkModified(light);
        }
    }

    // 불 일렁임 공통 — 자식 조명 전부 + 발광 렌더러 전부를 같은 값으로. 호박등·장식 호박등·가로등·촛대 공용
    static void AddFlameLife(GameObject go)
    {
        PropLife old = go.GetComponent<PropLife>();
        if (old != null) Undo.DestroyObjectImmediate(old);

        var lights = go.GetComponentsInChildren<Light>(true)
            .Where(l => l.type == LightType.Point || l.type == LightType.Spot)
            .Select(l => new PropLife.Flicker { light = l, minRatio = FlameLightMin, maxRatio = FlameLightMax, speed = FlameLightSpeed })
            .ToList();
        List<PropLife.EmissionFlicker> emissions = EmissionTargets(go);
        if (lights.Count == 0 && emissions.Count == 0)
        {
            Debug.LogWarning($"[위화감] '{go.name}'에 조명도 발광 재질도 없어 불 일렁임을 붙이지 않았습니다", go);
            return;
        }

        PropLife life = Undo.AddComponent<PropLife>(go);
        life.Configure(null, lights, emissions);
        life.SkipWhenInvisible = false;   // 소품이 화면 밖이어도 벽·바닥에 비친 빛은 보인다
        MarkModified(life);
    }

    // 장식 불빛: Prop이 아닌 불 켜진 호박등·촛대·가로등 프리팹 (자식 Point/Spot Light 기준)
    // 조명이 실제로 속한 프리팹 = 가장 '가까운' 프리팹 루트 (장식 호박등은 더 큰 프리팹 안에 있어 가장 바깥 루트로 찾으면 빠졌다)
    static List<(GameObject root, DecorKind kind)> CollectDecorLights()
    {
        Scene scene = SceneManager.GetActiveScene();
        var result = new List<(GameObject root, DecorKind kind)>();
        var seen = new HashSet<GameObject>();
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (light.gameObject.scene != scene) continue;
            if (light.type != LightType.Point && light.type != LightType.Spot) continue;
            if (light.name.StartsWith(ToolChildPrefix, StringComparison.Ordinal)) continue;   // 도구가 만든 호박등 조명

            // 씬에서 덧붙인 조명은 프리팹 인스턴스 자체가 아닐 수 있어 부모를 따라 올라가며 찾는다
            GameObject root = null;
            for (Transform t = light.transform; t != null && root == null; t = t.parent)
                root = PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
            if (root == null || !seen.Add(root)) continue;
            if (root.CompareTag("Prop") || root.GetComponentInParent<DollMove>(true) != null) continue;

            string src = SourceName(root);
            string n = src.Length > 0 ? src : root.name;
            if (!DecorLightKeywords.Any(k => n.Contains(k))) continue;

            DecorKind kind = n.StartsWith(DecorPumpkinPrefix, StringComparison.Ordinal) ? DecorKind.Pumpkin
                           : ExcludedDecorKeywords.Any(k => n.Contains(k)) ? DecorKind.Excluded
                           : DecorKind.Flame;
            result.Add((root, kind));
        }
        return result;
    }

    static List<PropLife.EmissionFlicker> EmissionTargets(GameObject go)
    {
        var entries = new List<PropLife.EmissionFlicker>();
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer) || r.name.StartsWith(ToolChildPrefix, StringComparison.Ordinal)) continue;
            bool hasEmission = r.sharedMaterials.Any(m => m != null && m.HasProperty("_EmissionColor") && m.IsKeywordEnabled("_EMISSION"));
            if (hasEmission) entries.Add(new PropLife.EmissionFlicker { renderer = r, minRatio = FlameEmissionMin, maxRatio = FlameEmissionMax, speed = FlameEmissionSpeed });
        }
        return entries;
    }

    static void AddCandleFire(GameObject go)
    {
        GameObject prefab = FindPrefab("Fx_Candle_Fire");
        if (prefab == null)
        {
            Debug.LogWarning("[위화감] Fx_Candle_Fire 프리팹을 찾지 못해 촛불 불꽃을 붙이지 않았습니다 (Poly Halloween/Prefabs/Particles)");
            return;
        }
        if (!TryGetMeshBounds(go, out Bounds b)) return;

        var fx = (GameObject)PrefabUtility.InstantiatePrefab(prefab, go.scene);
        Undo.RegisterCreatedObjectUndo(fx, "불꽃");
        fx.name = ToolChildPrefix + "CandleFire";
        fx.transform.SetParent(go.transform, false);
        fx.transform.position = new Vector3(b.center.x, b.max.y + 0.01f, b.center.z);   // 심지 = 윗면 가운데로 가정
        fx.transform.rotation = prefab.transform.rotation;

        if (AddCandleLight)
        {
            GameObject lightGo = new GameObject(ToolChildPrefix + "CandleLight");
            Undo.RegisterCreatedObjectUndo(lightGo, "촛불 조명");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.position = fx.transform.position + Vector3.up * 0.05f;
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.7f, 0.4f);
            light.intensity = 0.6f;
            light.range = 1.5f;
            light.shadows = LightShadows.None;

            PropLife life = Undo.AddComponent<PropLife>(go);
            life.Configure(null, new[] { new PropLife.Flicker { light = light } });
            MarkModified(life);
        }
    }

    // ── 보조 ─────────────────────────────────────────────

    static bool TryGetMeshBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            if (r.name.StartsWith(ToolChildPrefix, StringComparison.Ordinal)) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    static GameObject FindPrefab(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == name)
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }

    static void MarkModified(Object obj)
    {
        PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        EditorUtility.SetDirty(obj);
    }

    // URP 파티클 Unlit + 부드러운 원 텍스처, 반투명. 없으면 만든다
    static Material GetSteamMaterial()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(SteamMaterialPath);
        if (mat != null) return mat;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            Debug.LogWarning("[위화감] URP 파티클 셰이더를 찾지 못해 김 재질을 만들지 못했습니다");
            return null;
        }

        EnsureFolder(SettingsFolder);
        mat = new Material(shader) { name = "M_PropLife_Steam" };
        mat.SetTexture("_BaseMap", GetSoftTexture());
        mat.SetColor("_BaseColor", Color.white);
        // 반투명 — URP 파티클 재질 인스펙터가 Surface Type을 Transparent로 바꿀 때 하는 설정을 코드로
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)RenderQueue.Transparent;

        AssetDatabase.CreateAsset(mat, SteamMaterialPath);
        AssetDatabase.SaveAssets();
        return mat;
    }

    // 가운데가 진하고 가장자리로 갈수록 투명한 64×64 원 (내장 리소스 이름에 기대지 않으려고 직접 만든다)
    static Texture2D GetSoftTexture()
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(SoftTexturePath);
        if (existing != null) return existing;

        EnsureFolder(SettingsFolder);
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size * 2f - 1f;
            float dy = (y + 0.5f) / size * 2f - 1f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01(1f - d);
            a = a * a * (3f - 2f * a);   // smoothstep → 가장자리가 부드럽게
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        File.WriteAllBytes(SoftTexturePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(SoftTexturePath);
        if (AssetImporter.GetAtPath(SoftTexturePath) is TextureImporter importer)
        {
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(SoftTexturePath);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
