using UnityEngine;

// 문 너머 탈출 구역 — 여기가 승리 확정 지점. 연출·씬 전환은 EndGame에 위임한다
[RequireComponent(typeof(Collider))]
public class ExitZone : MonoBehaviour
{
    private bool isExited = false;

    void OnTriggerEnter(Collider other)
    {
        // 리플레이 중에는 스냅샷이 플레이어를 여기로 옮겨도 게임이 끝나면 안 된다
        if (GameManager.Instance != null && GameManager.Instance.isReplayMode) return;
        if (isExited || !other.CompareTag("Player")) return;
        if (GameProgressManager.Instance == null) return;

        isExited = true;
        GameProgressManager.Instance.EndGame(true);
    }
}
