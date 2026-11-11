using UnityEngine;

// 다음 은신처 선택 — 순수 계산 전담. 점유/예약 규칙은 selectable 판정으로 넘겨받는다
public class WaypointSelector
{
    private readonly Transform[] wayPoints;
    private readonly Transform player;
    private readonly DollVision vision;
    private readonly System.Predicate<int> selectable;

    public WaypointSelector(Transform[] wayPoints, Transform player, DollVision vision, System.Predicate<int> selectable)
    {
        this.wayPoints = wayPoints;
        this.player = player;
        this.vision = vision;
        this.selectable = selectable;
    }

    // [도망·시작 배치] 플레이어에게서 가장 먼 인덱스. 없으면 -1 (가시성은 따지지 않는다)
    public int FindFarthest()
    {
        int bestIndex = -1;
        float maxDistance = -1f;

        for (int i = 0; i < wayPoints.Length; i++)
        {
            if (!selectable(i)) continue;

            float dist = DistanceToPlayer(i);
            if (dist > maxDistance)
            {
                maxDistance = dist;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    // [순찰] 거리² 가중치 랜덤 — 멀수록 선호하되 가까운 곳도 낮은 확률로 뽑힌다. 없으면 -1
    public int PickWeightedRandom()
    {
        float[] weights = new float[wayPoints.Length];
        float totalWeight = 0f;

        for (int i = 0; i < wayPoints.Length; i++)
        {
            if (!selectable(i)) continue;
            if (IsSpotInDanger(i)) continue;   // 플레이어가 보고 있는 자리로는 뛰어들지 않는다

            float dist = DistanceToPlayer(i);
            weights[i] = dist * dist;
            totalWeight += weights[i];
        }

        if (totalWeight <= 0f) return -1;

        // 룰렛 방식 — 0~전체 가중치에서 하나 뽑아 어느 구간에 떨어지는지 찾는다
        float pick = Random.value * totalWeight;
        for (int i = 0; i < wayPoints.Length; i++)
        {
            if (weights[i] <= 0f) continue;

            pick -= weights[i];
            if (pick <= 0f)
                return i;
        }

        return -1;
    }

    float DistanceToPlayer(int index)
        => Vector3.Distance(wayPoints[index].position, player.position);

    bool IsSpotInDanger(int index)
        => vision.IsPointInDangerZone(wayPoints[index].position + Vector3.up * vision.eyeHeightOffset);
}
