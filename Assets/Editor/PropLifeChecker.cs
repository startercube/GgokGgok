using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// 위화감 A "시간이 멈춘 사물" 검사 도구 (읽기 전용, 씬을 고치지 않는다)
// 같은 모양 소품끼리 생활감(PropLife·PropReaction·파티클·조명) 유무가 섞이면 '의도치 않은 단서'가 된다
// (예: 촛불 4개 중 3개만 흔들림 → 안 흔들리는 진짜 촛불을 빈대로 오해). 위화감 미채택 때 세운
// "같은 방 변신 가능 소품끼리 의도치 않은 차이가 없게" 규칙을 도구로 지킨다
// 상세 「코드 설계 - 위화감(시간이 멈춘 사물)·웃음소리 방향」
public static class PropLifeChecker
{
    class Group
    {
        public int total;
        public int alive;
        public readonly List<string> aliveNames = new List<string>();
        public readonly List<string> stillNames = new List<string>();
    }

    [MenuItem("Tools/위화감/현재 씬 생활감 검사")]
    static void Check()
    {
        var groups = new SortedDictionary<string, Group>();
        var warnings = new List<string>();
        int candidates = 0, alive = 0, reactions = 0, effects = 0;

        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // DollDisguise.CollectCandidates와 같은 기준: Prop 태그, 빈대 밑이 아님, 최상위 Prop, 메시 있음
            if (!go.CompareTag("Prop")) continue;
            Transform t = go.transform;
            if (t.GetComponentInParent<DollMove>(true) != null) continue;
            if (t.parent != null && t.parent.CompareTag("Prop")) continue;

            Mesh firstMesh = null;
            bool staticBatched = false;
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                if (firstMesh == null) firstMesh = mf.sharedMesh;
                if ((GameObjectUtility.GetStaticEditorFlags(mf.gameObject) & StaticEditorFlags.BatchingStatic) != 0)
                    staticBatched = true;
            }
            if (firstMesh == null) continue;

            // 생활감 = 움직임·깜빡임(PropLife) 또는 반응(PropReaction) 또는 파티클·조명(변장은 멈춘 채로 복사됨)
            PropLife life = go.GetComponentInChildren<PropLife>(true);
            bool hasReaction = go.GetComponentInChildren<PropReaction>(true) != null;
            bool hasEffect = go.GetComponentInChildren<ParticleSystem>(true) != null || go.GetComponentInChildren<Light>(true) != null;
            bool hasLife = (life != null && life.HasLife) || hasReaction || hasEffect;

            if (staticBatched)
            {
                if (hasLife) warnings.Add($"'{go.name}': Batching Static이라 변장 후보가 아니고, 생활감 대상이 움직이지 않을 수 있습니다");
                continue;
            }

            candidates++;
            if (!groups.TryGetValue(firstMesh.name, out Group g))
                groups[firstMesh.name] = g = new Group();
            g.total++;
            if (hasLife)
            {
                alive++;
                if (hasReaction) reactions++;
                if (hasEffect) effects++;
                g.alive++;
                g.aliveNames.Add(go.name);
                if (life != null) CheckMovingColliders(life, warnings);
            }
            else
            {
                g.stillNames.Add(go.name);
            }
        }

        var report = new StringBuilder();
        report.AppendLine($"[위화감] 변장 후보 {candidates}개 중 생활감 있음 {alive}개 (반응 {reactions}개 · 파티클/조명 {effects}개)");
        int mixed = 0;
        foreach (var pair in groups)
        {
            Group g = pair.Value;
            string mark = g.alive == 0 ? "  " : (g.alive == g.total ? "OK" : "!!");
            report.AppendLine($"  {mark} {pair.Key}: 있음 {g.alive} / 없음 {g.total - g.alive}");
            if (g.alive > 0 && g.alive < g.total)
            {
                mixed++;
                warnings.Add($"'{pair.Key}' 묶음에 생활감이 섞였습니다 — 없음: {string.Join(", ", g.stillNames)}");
            }
        }
        foreach (string w in warnings) report.AppendLine("  경고: " + w);

        if (warnings.Count > 0) Debug.LogWarning(report.ToString());
        else Debug.Log(report.ToString());

        EditorUtility.DisplayDialog("생활감 검사",
            $"변장 후보 {candidates}개 중 생활감 있음 {alive}개\n반응 {reactions}개 · 파티클/조명 {effects}개\n섞인 묶음 {mixed}개, 경고 {warnings.Count}개\n\n자세한 목록은 콘솔을 보세요.", "확인");
    }

    // 움직이는 대상에 콜라이더가 붙어 있으면 매 프레임 물리 갱신 비용이 든다 (특히 비볼록 MeshCollider)
    static void CheckMovingColliders(PropLife life, List<string> warnings)
    {
        foreach (PropLife.Motion m in life.Motions)
        {
            Transform target = m.target != null ? m.target : life.transform;
            foreach (Collider c in target.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || !c.enabled) continue;
                warnings.Add($"'{life.name}': 움직이는 대상 '{target.name}'에 콜라이더 '{c.name}'가 같이 움직입니다 — 콜라이더 없는 자식(불꽃·잎)을 대상으로 권장");
                break;
            }
        }
    }
}
