using UnityEngine;

// 인트로 카메라 길의 점 하나 (2026-10-23) — 위치 = 이 Transform 위치, 보는 방향 = 이 Transform의 정면(+Z)
// Scene 뷰에서 옮기고 돌리면 길과 시선이 바로 바뀐다. 시각만 인스펙터에서 고친다.
public class IntroPathPoint : MonoBehaviour
{
    [Tooltip("이 점에 도착하는 시각 (초)")]
    public float time;

    [Tooltip("켜면 실행할 때 위치·방향을 플레이어 카메라 눈 자세로 바꾼다 (길의 마지막 점). 씬에 놓인 자리는 미리 보기용")]
    public bool endsAtPlayerEye;
}
