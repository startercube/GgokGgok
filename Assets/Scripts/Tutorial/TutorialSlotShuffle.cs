using System.Collections.Generic;
using UnityEngine;

// 실행할 때 빈대와 미끼들의 자리(위치·회전)를 무작위로 섞는다 (2026-11-02).
// 배치 도구가 놓은 자리들을 "후보 자리"로 보고 다시 나눠 준다 → 판마다 정답 자리가 바뀌고, 빈 바닥에 혼자 선 물건이 생기지 않는다.
// 빈대 자리가 바뀌면 시작 웨이포인트(waypointOf)도 같이 옮긴다 — DollMove.startAtFirstWaypoint를 켜 두면 그 자리에서 시작.
// Awake에서 끝내므로 모든 Start(DollMove의 시작 자리 결정 포함)보다 먼저다.
// 쓰는 곳: 튜토리얼 방 A(상자 3개 한 줄), 방 B(상자 3개). 설계: 「코드 설계 - 튜토리얼 방 A 곁눈질 찾기·방 B 정답 위치」
public class TutorialSlotShuffle : MonoBehaviour
{
    [Tooltip("자리를 섞을 오브젝트들 (빈대·미끼). 지금 놓인 자리가 곧 후보 자리")]
    [SerializeField] private Transform[] items = new Transform[0];
    [Tooltip("items와 같은 순서. 그 오브젝트와 함께 옮길 웨이포인트 (빈대의 0번 웨이포인트). 없으면 비움")]
    [SerializeField] private Transform[] waypointOf = new Transform[0];
    [Tooltip("끄면 섞지 않는다 (배치 그대로 확인할 때)")]
    [SerializeField] private bool shuffleOnAwake = true;

    void Awake()
    {
        if (shuffleOnAwake) Shuffle();
    }

    public void Shuffle()
    {
        var list = new List<int>();
        for (int i = 0; i < items.Length; i++) if (items[i] != null) list.Add(i);
        if (list.Count < 2) return;

        // 지금 자리(포즈) 모으기
        var poses = new List<(Vector3 pos, Quaternion rot)>();
        foreach (int i in list) poses.Add((items[i].position, items[i].rotation));

        // Fisher–Yates
        for (int k = poses.Count - 1; k > 0; k--)
        {
            int j = Random.Range(0, k + 1);
            (poses[k], poses[j]) = (poses[j], poses[k]);
        }

        var log = new System.Text.StringBuilder($"[자리 섞기] {name}:");
        for (int n = 0; n < list.Count; n++)
        {
            int i = list[n];
            var (pos, rot) = poses[n];
            items[i].SetPositionAndRotation(pos, rot);
            if (i < waypointOf.Length && waypointOf[i] != null)
            {
                Vector3 w = waypointOf[i].position;
                waypointOf[i].position = new Vector3(pos.x, w.y, pos.z);
                log.Append($" {items[i].name} → ({pos.x:0.0}, {pos.z:0.0})");
            }
        }
        Debug.Log(log.ToString(), this);
    }
}
