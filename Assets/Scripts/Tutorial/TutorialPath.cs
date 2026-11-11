using UnityEngine;

// 걸어간 거리로 대사를 트리거하기 위한 복도 중심선 (2026-10-06 확정: 복도 대사는 시간이 아니라 걸어간 거리)
// 오브젝트 이름이 대사 데이터의 target 이름이다
public class TutorialPath : MonoBehaviour
{
    public Vector3[] points = new Vector3[0];
    [Tooltip("중심선에서 이보다 멀면 복도 밖으로 보고 거리를 세지 않는다 (m)")]
    public float maxOffset = 2.2f;

    public float Length
    {
        get
        {
            float l = 0f;
            for (int i = 1; i < points.Length; i++) l += Vector3.Distance(Flat(points[i - 1]), Flat(points[i]));
            return l;
        }
    }

    /// <summary>위치를 중심선에 투영한 시작점부터의 거리. 복도 밖이면 -1</summary>
    public float Progress(Vector3 pos)
    {
        Vector3 p = Flat(pos);
        float best = float.MaxValue, bestAlong = -1f, walked = 0f;
        for (int i = 1; i < points.Length; i++)
        {
            Vector3 a = Flat(points[i - 1]), b = Flat(points[i]);
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len < 0.001f) continue;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / (len * len));
            float d = Vector3.Distance(p, a + ab * t);
            if (d < best) { best = d; bestAlong = walked + t * len; }
            walked += len;
        }
        return best <= maxOffset ? bestAlong : -1f;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.6f);
        for (int i = 1; i < points.Length; i++) Gizmos.DrawLine(points[i - 1] + Vector3.up * 0.1f, points[i] + Vector3.up * 0.1f);
    }
}
