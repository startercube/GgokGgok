using System.Collections.Generic;
using UnityEngine;

// 반응하는 소품(PropReaction) 전체를 한 곳에서 계산한다 — 첫 PropReaction이 켜질 때 스스로 생기고, 씬과 함께 사라진다
// 발걸음마다 진동이 물결처럼 퍼진다: 반경 안 소품에 "거리 ÷ 물결 속도"만큼 늦은 도착을 예약 → 가까운 것부터 차례로 출렁
// 상세 「코드 설계 - 호박등 발광 깜빡임·인형 방 물결 반응」
// IReplayPausable 아님: 리플레이에서도 녹화된 플레이어 위치로 똑같이 반응해야 같은 장면이 된다
public class PropReactionManager : MonoBehaviour
{
    const float StepLength = 1.2f;        // 이만큼 수평 이동할 때마다 '발걸음' 한 번 (PlayerMove에 발걸음 이벤트가 없음)
    const float TeleportDistance = 3f;    // 한 프레임에 이보다 멀리 가면 순간이동(인트로·시작)으로 보고 무시
    const float WaveSpeed = 6f;           // 진동이 퍼지는 속도(m/s) — 4m 끝까지 약 0.67초
    const float EdgeStrength = 0.5f;      // 반경 끝 세기 (발밑 = 1)
    const float PlayerSearchInterval = 1f;

    private static readonly List<PropReaction> all = new List<PropReaction>();
    private static readonly List<PropReaction> active = new List<PropReaction>();
    private static PropReactionManager instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSharedState()
    {
        all.Clear();
        active.Clear();
        instance = null;
    }

    public static void Register(PropReaction reaction)
    {
        if (!all.Contains(reaction)) all.Add(reaction);
        if (instance == null && Application.isPlaying)
            instance = new GameObject("[PropReactionManager]").AddComponent<PropReactionManager>();
    }

    public static void Unregister(PropReaction reaction)
    {
        all.Remove(reaction);
        if (reaction.InActiveList)
        {
            active.Remove(reaction);
            reaction.InActiveList = false;
        }
    }

    private Transform player;
    private float nextPlayerSearch;
    private Vector3 lastPos;
    private bool hasLast;
    private float travel;

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        if (player == null && Time.unscaledTime >= nextPlayerSearch)
        {
            nextPlayerSearch = Time.unscaledTime + PlayerSearchInterval;
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) { player = p.transform; hasLast = false; travel = 0f; }
        }

        if (player != null)
        {
            Vector3 pos = player.position;
            if (hasLast)
            {
                Vector3 d = pos - lastPos;
                d.y = 0f;
                float m = d.magnitude;
                if (m > TeleportDistance) travel = 0f;
                else travel += m;
            }
            lastPos = pos;
            hasLast = true;

            if (travel >= StepLength)
            {
                travel %= StepLength;
                Step(pos);
            }
        }

        float now = Time.time, dt = Time.deltaTime;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            PropReaction r = active[i];
            if (r != null && r.Tick(now, dt)) continue;
            if (r != null) r.InActiveList = false;
            active.RemoveAt(i);
        }
    }

    // 발걸음 한 번 — 반경 안 소품에 물결 도착을 예약한다 (가까울수록 빨리·세게)
    void Step(Vector3 feet)
    {
        float now = Time.time;
        foreach (PropReaction r in all)
        {
            if (r == null) continue;
            Vector3 p = r.transform.position;
            float dx = p.x - feet.x, dz = p.z - feet.z;
            float sqr = dx * dx + dz * dz;
            if (sqr > r.Radius * r.Radius) continue;
            if (Mathf.Abs(p.y - feet.y) > r.MaxHeight) continue;

            float dist = Mathf.Sqrt(sqr);
            float strength = Mathf.Lerp(1f, EdgeStrength, dist / r.Radius);
            r.Schedule(now + dist / WaveSpeed, strength, feet);
            if (!r.InActiveList)
            {
                r.InActiveList = true;
                active.Add(r);
            }
        }
    }
}
