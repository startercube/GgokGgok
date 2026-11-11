using System.Collections.Generic;
using UnityEngine;

// 인트로 카메라 길 (2026-10-23) — 자식 IntroPathPoint들을 "도착 시각"으로 잇는다.
// 위치: 시각을 매개변수로 쓰는 캣멀-롬(에르미트) 곡선 → 정한 시각에 정확히 그 점에 있고, 점에서 속도가 끊기지 않는다.
//       첫 점·마지막 점은 접선 0 → 천천히 출발하고 천천히 착지한다.
// 시선: 점마다의 방향 사이를 구간마다 smoothstep으로 섞는다 → 점 근처에서 시선이 잠깐 머문다 ("가구 무리마다 잠깐 늦춤")
// 설계 기록: 프로젝트 문서 「코드 설계 - 맵 인트로·무전」 2.3
public class IntroPath : MonoBehaviour
{
    [Tooltip("천장 높이 (m). 기즈모에서 이 값 - 여유보다 높은 구간을 빨갛게 표시")]
    public float ceilingY = 4.5f;
    public float ceilingMargin = 0.3f;

    struct Key { public Vector3 p; public Quaternion r; public float t; }

    private readonly List<Key> keys = new List<Key>();
    private bool built;
    private bool hasEye;
    private Vector3 eyePos;
    private Quaternion eyeRot;

    /// <summary>마지막 점(endsAtPlayerEye)을 실행 시 플레이어 눈 자세로 바꾼다</summary>
    public void SetPlayerEye(Vector3 position, Quaternion rotation)
    {
        hasEye = true; eyePos = position; eyeRot = rotation;
        Build();
    }

    /// <summary>길 전체 시간 = 마지막 점 시각</summary>
    public float Duration { get { Ensure(); return keys.Count > 0 ? keys[keys.Count - 1].t : 0f; } }

    public int PointCount { get { Ensure(); return keys.Count; } }

    public void Build()
    {
        keys.Clear();
        var points = GetComponentsInChildren<IntroPathPoint>(false);
        foreach (var pt in points)
        {
            var k = new Key { p = pt.transform.position, r = pt.transform.rotation, t = pt.time };
            if (pt.endsAtPlayerEye && hasEye) { k.p = eyePos; k.r = eyeRot; }
            keys.Add(k);
        }
        keys.Sort((a, b) => a.t.CompareTo(b.t));
        built = true;
    }

    void Ensure() { if (!built) Build(); }

    public void Evaluate(float t, out Vector3 pos, out Quaternion rot)
    {
        Ensure();
        int n = keys.Count;
        if (n == 0) { pos = transform.position; rot = transform.rotation; return; }
        if (n == 1 || t <= keys[0].t) { pos = keys[0].p; rot = keys[0].r; return; }
        if (t >= keys[n - 1].t) { pos = keys[n - 1].p; rot = keys[n - 1].r; return; }

        int i = 0;
        while (i < n - 2 && t >= keys[i + 1].t) i++;
        Key a = keys[i], b = keys[i + 1];
        float h = Mathf.Max(0.0001f, b.t - a.t);
        float s = Mathf.Clamp01((t - a.t) / h);

        Vector3 m0 = Tangent(i), m1 = Tangent(i + 1);
        float s2 = s * s, s3 = s2 * s;
        pos = (2f * s3 - 3f * s2 + 1f) * a.p + (s3 - 2f * s2 + s) * h * m0
            + (-2f * s3 + 3f * s2) * b.p + (s3 - s2) * h * m1;
        rot = Quaternion.Slerp(a.r, b.r, s * s * (3f - 2f * s));
    }

    // 점 i의 속도 (m/s). 양 끝은 0
    Vector3 Tangent(int i)
    {
        if (i <= 0 || i >= keys.Count - 1) return Vector3.zero;
        float dt = keys[i + 1].t - keys[i - 1].t;
        return dt > 0.0001f ? (keys[i + 1].p - keys[i - 1].p) / dt : Vector3.zero;
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) built = false;   // 편집 중엔 점을 옮길 때마다 새로 읽음
        Ensure();
        if (keys.Count < 2) return;
        float dur = Duration;
        Evaluate(0f, out var prev, out _);
        for (float t = 0.05f; t <= dur + 0.001f; t += 0.05f)
        {
            Evaluate(t, out var p, out _);
            Gizmos.color = p.y > ceilingY - ceilingMargin ? Color.red : new Color(1f, 0.75f, 0.2f);
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
        Gizmos.color = new Color(0.5f, 0.9f, 1f);
        foreach (var k in keys)
        {
            Gizmos.DrawWireSphere(k.p, 0.12f);
            Gizmos.DrawLine(k.p, k.p + k.r * Vector3.forward * 1.2f);
        }
    }
}
