using System.Collections.Generic;
using UnityEngine;

// 메인화면 격리실 (2026-10-22) — 시작할 때 빈대를 들여보낸다
// 빈대 수 = 1(튜토리얼 격리실 원래 개체) + 처음 클리어한 맵 수. 맵에서 온 빈대는 그 현장 기념품을 단다
// 방 모양·카메라는 배치 도구(Tools > 메인화면 > 격리실 CCTV 배치)가 튜토리얼 격리실을 복사해 만든다
public class MenuIsolationRoom : MonoBehaviour
{
    public enum Source
    {
        SaveData,   // 클리어 기록대로 (기본)
        ShowAll,    // 목록 전부 (전시회에서 북적이게 보여줄 때)
        StartOnly,  // 1마리만
    }

    [SerializeField] private MenuAlienRoster roster;
    [SerializeField] private Source source = Source.SaveData;
    [SerializeField] private Transform alienParent;
    [SerializeField] private MenuCctvHud hud;

    [Header("Area (월드 좌표)")]
    [Tooltip("빈대가 다닐 수 있는 바닥 사각형 가운데 (벽 면에서 여백을 뺀 안쪽)")]
    [SerializeField] private Vector3 areaCenter;
    [SerializeField] private Vector2 areaSize = new Vector2(4.7f, 4.7f);
    [Tooltip("CCTV 바로 아래: 화면을 가릴 만큼 크게 잘려 보이므로 들어가지 않는다")]
    [SerializeField] private Vector3 keepOutCenter;
    [SerializeField] private float keepOutRadius = 1.6f;
    [Tooltip("시작 자리끼리 최소 거리")]
    [SerializeField] private float spawnSpacing = 1.0f;

    private readonly List<Transform> spawned = new List<Transform>();

    public int Count => spawned.Count;

    void Start()
    {
        if (Time.timeScale <= 0f)
        {
            // 다른 씬에서 일시정지·결과 화면을 복구하지 않고 넘어오면 메인화면 빈대가 멈춘다
            Debug.LogWarning("[메인화면] timeScale이 0이라 1로 되돌림");
            Time.timeScale = 1f;
        }
        Spawn();
    }

    void Spawn()
    {
        if (roster == null || roster.alienPrefab == null)
        {
            Debug.LogWarning("[메인화면] 빈대 목록(MenuAlienRoster) 또는 빈대 프리팹이 없음", this);
            return;
        }

        // 처음부터 있는 1마리 + 들어올 자격이 있는 줄들
        var souvenirs = new List<GameObject> { null };
        var names = new List<string> { "튜토리얼 격리실" };
        if (source != Source.StartOnly)
            foreach (var e in roster.entries)
            {
                if (e == null) continue;
                if (source == Source.SaveData && !StageClearRecord.IsCleared(e.sceneName)) continue;
                souvenirs.Add(e.souvenirPrefab);
                names.Add(string.IsNullOrEmpty(e.label) ? e.sceneName : e.label);
            }

        var parent = alienParent != null ? alienParent : transform;
        for (int i = 0; i < souvenirs.Count; i++)
        {
            var go = Instantiate(roster.alienPrefab, SpawnPoint(), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), parent);
            go.name = $"MenuAlien_{i:00}";

            if (souvenirs[i] != null)
            {
                var anchor = FindDeep(go.transform, "SouvenirAnchor");
                if (anchor == null) Debug.LogWarning($"[메인화면] {go.name}에 SouvenirAnchor가 없어 기념품을 못 붙임", go);
                else Instantiate(souvenirs[i], anchor, false);
            }

            var wander = go.GetComponent<MenuAlienWander>();
            if (wander != null) wander.Init(this);
            spawned.Add(go.transform);
        }

        if (hud != null) hud.SetCount(spawned.Count);
        Debug.Log($"[메인화면] 격리실 수용 {spawned.Count}마리: {string.Join(", ", names)} (모드 {source})");
    }

    /// <summary>다닐 수 있는 바닥 위 무작위 점 (카메라 아래 금지 원 밖)</summary>
    public Vector3 RandomPoint()
    {
        Vector3 p = areaCenter;
        for (int i = 0; i < 20; i++)
        {
            p = areaCenter + new Vector3(Random.Range(-0.5f, 0.5f) * areaSize.x, 0f, Random.Range(-0.5f, 0.5f) * areaSize.y);
            if (!InKeepOut(p)) return p;
        }
        return p;
    }

    bool InKeepOut(Vector3 p)
    {
        Vector3 d = p - keepOutCenter; d.y = 0f;
        return d.sqrMagnitude < keepOutRadius * keepOutRadius;
    }

    Vector3 SpawnPoint()
    {
        Vector3 best = areaCenter;
        float bestGap = -1f;
        for (int i = 0; i < 30; i++)
        {
            Vector3 p = RandomPoint();
            float gap = float.MaxValue;
            foreach (var t in spawned)
            {
                Vector3 d = p - t.position; d.y = 0f;
                gap = Mathf.Min(gap, d.magnitude);
            }
            if (gap >= spawnSpacing) return p;
            if (gap > bestGap) { best = p; bestGap = gap; }
        }
        return best;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            var r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.4f, 1f, 0.8f);
        Gizmos.DrawWireCube(areaCenter, new Vector3(areaSize.x, 0.02f, areaSize.y));
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
        Gizmos.DrawWireSphere(keepOutCenter, keepOutRadius);
    }
}
