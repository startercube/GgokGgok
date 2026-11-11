using System.Collections.Generic;
using UnityEngine;

// 튜토리얼 구역 하나의 "문 열림" 조건 — 이 구역의 얼룩을 다 닦고 빈대를 다 잡으면 문이 열린다.
// 게임 전체 승리 조건(모든 얼룩 청소 + 모든 외계인 포획 → 문 열림)과 같은 논리를 구역 단위로 적용한 것 (튜토리얼 6번 얼룩 문).
// 잡기·청소는 PlayerInteraction이 그대로 처리한다(빈대 Destroy, 얼룩 Destroy). 여기서는 남은 것이 있는지만 본다.
public class TutorialZoneGoal : MonoBehaviour
{
    [Tooltip("조건을 채우면 열 문")]
    [SerializeField] private DoorController door;
    [Tooltip("이 구역에서 잡아야 할 빈대")]
    [SerializeField] private DollMove[] dolls = new DollMove[0];
    [Tooltip("이 구역에 미리 배치한 얼룩")]
    [SerializeField] private GameObject[] stains = new GameObject[0];

    [Header("새로 생기는 얼룩도 셀 때 (연습 구역 등)")]
    [SerializeField] private bool trackSpawnedStains = false;
    [Tooltip("이 상자(월드 좌표) 안에서 생긴 얼룩만 센다")]
    [SerializeField] private Bounds zone = new Bounds(Vector3.zero, Vector3.one);

    private readonly List<GameObject> spawned = new List<GameObject>();
    private bool done;

    public bool IsDone => done;
    public event System.Action OnCompleted;   // 대사 재생 등 진행 관리가 구독 (얼룩 문이 열릴 때 한 주임 대사)

    // static 이벤트라 반드시 쌍으로 해제
    void OnEnable()  { DollStainSpawner.OnStainSpawned += HandleStainSpawned; }
    void OnDisable() { DollStainSpawner.OnStainSpawned -= HandleStainSpawned; }

    void HandleStainSpawned(GameObject stain)
    {
        if (trackSpawnedStains && stain != null && zone.Contains(stain.transform.position))
            spawned.Add(stain);
    }

    void Update()
    {
        if (done || Remaining() > 0) return;

        done = true;
        if (door != null) door.Open();
        OnCompleted?.Invoke();
    }

    /// <summary>남은 빈대 + 남은 얼룩 수. 잡히거나 닦이면 Destroy되어 Unity의 null 비교가 true가 된다</summary>
    public int Remaining()
    {
        int n = 0;
        foreach (var d in dolls) if (d != null) n++;
        foreach (var s in stains) if (s != null && s.activeInHierarchy) n++;
        foreach (var s in spawned) if (s != null && s.activeInHierarchy) n++;
        return n;
    }

    void OnDrawGizmosSelected()
    {
        if (!trackSpawnedStains) return;
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
        Gizmos.DrawWireCube(zone.center, zone.size);
    }
}
