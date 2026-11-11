using UnityEngine;
using UnityEngine.AI;

// 얼룩 생성 전담 — 만들고 이벤트로 알리기만 한다. '언제' 남길지는 DollMove가 결정
public class DollStainSpawner : MonoBehaviour
{
    [SerializeField] private GameObject stainPrefab;
    [Tooltip("바닥에 파묻혀 안 보이지 않도록 얼룩을 이만큼 띄운다 (z-fighting 방지)")]
    [SerializeField] private float surfaceOffset = 0.02f;
    [Tooltip("요청 위치에서 이 반경 안의 바닥(NavMesh)을 찾는다. 층 사이 높이보다 작게 둘 것 — 크면 아래층 바닥을 잡는다")]
    [SerializeField] private float floorSearchRadius = 1f;

    // 얼룩은 바닥에 눕혀야 보인다 — 프리팹은 세워진 스프라이트라 X축으로 90도 눕힌다
    private static readonly Quaternion FloorRotation = Quaternion.Euler(90f, 0f, 0f);

    // 새 얼룩 생성 알림 — 진행도(GameProgressManager)·녹화기(ReplayRecorder)가 구독
    public static event System.Action<GameObject> OnStainSpawned;

    // 인형 발치에 생성
    public void Spawn() => SpawnAt(transform.position);

    public void SpawnAt(Vector3 position)
    {
        if (stainPrefab == null) return;
        GameObject stain = Create(stainPrefab, SnapToFloor(position));
        OnStainSpawned?.Invoke(stain);
    }

    /// <summary>얼룩 프리팹을 지정한 월드 좌표에 바닥을 향해 눕혀 생성한다. (높이 보정은 하지 않음)</summary>
    public static GameObject Create(GameObject prefab, Vector3 position)
    {
        GameObject stain = Instantiate(prefab, position, FloorRotation);

        // 프리팹 루트가 RectTransform이라 Instantiate에 넘긴 좌표가 프리팹에 저장된
        // 앵커 값으로 덮여 엉뚱한 곳(화면 밖)에 놓인다 — 앵커를 중립으로 지운 뒤 좌표를 다시 못박는다
        if (stain.transform is RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition3D = Vector3.zero;
        }
        stain.transform.SetPositionAndRotation(position, FloorRotation);

        return stain;
    }

    // 인형이 딛고 선 바닥 높이를 NavMesh에서 직접 읽는다.
    // 피벗 높이를 손으로 보정하던 방식과 달리 선반·2층에서도 그 층의 바닥을 잡는다
    Vector3 SnapToFloor(Vector3 position)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, floorSearchRadius, NavMesh.AllAreas))
            return hit.position + Vector3.up * surfaceOffset;

        Debug.LogWarning($"[{name}] {position} 주변 {floorSearchRadius}m 안에 바닥(NavMesh)이 없어 " +
                         $"얼룩을 요청 위치에 그대로 둡니다.", this);
        return position + Vector3.up * surfaceOffset;
    }
}
