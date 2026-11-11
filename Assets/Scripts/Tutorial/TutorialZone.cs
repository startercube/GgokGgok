using UnityEngine;

// 튜토리얼 진행 관리가 쓰는 이름 붙은 구역 (월드 좌표 상자). 오브젝트 이름이 대사 데이터의 target 이름이다
public class TutorialZone : MonoBehaviour
{
    public Bounds bounds = new Bounds(Vector3.zero, Vector3.one);

    public bool Contains(Vector3 p) => bounds.Contains(p);

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.35f);
        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }
}
