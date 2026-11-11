using UnityEngine;

// [임시] 진행 문을 한 주임 대신 열어 주는 자리표시 (2026-10-18).
// 플레이어가 문 가까이 오고, 이 문을 지나기 전에 잡아야 할 빈대를 다 잡았으면 연다.
// 한 주임 NPC(앞장서 걷기·문 열어 주기)와 튜토리얼 진행 관리가 생기면 이 컴포넌트는 지우고 그쪽에서 door.Open()을 부른다.
public class TutorialDoorOpener : MonoBehaviour
{
    [SerializeField] private DoorController door;
    [Tooltip("문 중심(바닥)에서 이 거리(수평) 안에 플레이어가 오면 연다")]
    [SerializeField] private float openDistance = 2.2f;
    [Tooltip("이 빈대들을 모두 잡아야 열린다 (방 A·B 출구)")]
    [SerializeField] private DollMove[] requiredCaught = new DollMove[0];

    private Transform player;
    private bool opened;

    void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
    }

    void Update()
    {
        if (opened || door == null || player == null) return;

        foreach (var d in requiredCaught)
            if (d != null) return;   // 아직 안 잡힘

        Vector3 diff = player.position - door.transform.position;
        diff.y = 0f;
        if (diff.sqrMagnitude > openDistance * openDistance) return;

        opened = true;
        door.Open();
    }
}
