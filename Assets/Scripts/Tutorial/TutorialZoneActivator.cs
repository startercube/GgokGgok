using UnityEngine;

// 구역의 빈대를 플레이어가 그 구역에 들어올 때까지 얼려 둔다 (튜토리얼 7번 연습 구역).
// 안 그러면 플레이어가 앞 방에 있는 동안 빈대가 미리 돌아다니며 얼룩을 쌓는다.
// 얼어 있는 동안은 이동권 충전·시작 유예 시간도 멈추므로, 유예 시간은 입장 순간부터 흐른다.
public class TutorialZoneActivator : MonoBehaviour
{
    [SerializeField] private DollMove[] dolls = new DollMove[0];
    [Tooltip("플레이어가 이 상자(월드 좌표) 안에 들어오면 빈대를 푼다")]
    [SerializeField] private Bounds zone = new Bounds(Vector3.zero, Vector3.one);

    private Transform player;
    private bool activated;

    public bool IsActivated => activated;
    public event System.Action OnActivated;

    // 모든 Start가 끝난 뒤 첫 Update 전에 얼려야 한다 — DollMove.Start(시작 배치 순간이동)와 순서는 상관없음
    void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
        else Debug.LogWarning($"[{name}] Player를 찾지 못해 빈대를 풀 수 없습니다.", this);

        foreach (var d in dolls)
            if (d != null) d.SetFrozen(true);
    }

    void Update()
    {
        if (activated || player == null || !zone.Contains(player.position)) return;

        activated = true;
        foreach (var d in dolls)
            if (d != null) d.SetFrozen(false);
        OnActivated?.Invoke();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(zone.center, zone.size);
    }
}
