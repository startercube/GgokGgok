using UnityEngine;

// 가시성 판정 전담 — 질의받을 때만 동작한다. 실제 시야(IsPointSeen)와 멈춰야 할 범위(위험 지대)를 구분한다
// 판정 기준은 실제 카메라 화면 (2026-11-01 「코드 설계 - 빈대 시야 정지(즉시 정지·화면 기준 판정)」):
// 카메라 FOV·화면비로 만든 직사각형 시야를 여유 각도만큼 넓힌 피라미드 안에 있으면 '보임'. 여유 0 = 정확히 화면
// 거리·여유 값은 공용 설정 에셋(DollVisionSettings)에서 읽는다 → 모든 맵·모든 빈대가 같은 기준
public class DollVision : MonoBehaviour
{
    const float MaxHalfAngle = 89f;   // 반각 + 여유가 90°를 넘으면 tan이 무한대·음수가 된다 → '카메라 앞쪽 전부'로 상한

    [Header("Sight Detection")]
    [Tooltip("비우면 Camera.main")]
    public Camera playerCamera;
    [Tooltip("시선을 가리는 벽/장애물 레이어 (인형 자신의 레이어는 제외 권장)")]
    public LayerMask obstructionMask = ~0;
    [Tooltip("Raycast가 조준할 인형의 눈높이 오프셋")]
    public float eyeHeightOffset = 0.5f;

    [Header("Shared Settings")]
    [Tooltip("비우면 공용 기본값(Resources/DollVisionSettings)을 쓴다. 이 빈대만 다르게 하고 싶을 때만 다른 에셋을 끼운다")]
    [SerializeField] private DollVisionSettings settingsOverride;

    public DollVisionSettings Settings => settingsOverride != null ? settingsOverride : DollVisionSettings.Default;

    // 카메라 FOV 반각 캐시 — 경로 미리보기가 프레임마다 여러 점을 검사하므로 프레임당 한 번만 계산
    private int halfAngleFrame = -1;
    private float horizontalHalf;
    private float verticalHalf;

    void Start()
    {
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
            if (playerCamera == null)
                Debug.LogWarning("Player camera not found. Please assign playerCamera in the inspector.");
        }
    }

    // 실제 화면의 가로·세로 반각 (Camera.fieldOfView는 세로 FOV). 화면비·FOV 연출이 바뀌면 자동으로 따라간다
    void RefreshHalfAngles()
    {
        if (halfAngleFrame == Time.frameCount) return;
        halfAngleFrame = Time.frameCount;

        verticalHalf = playerCamera.fieldOfView * 0.5f;
        horizontalHalf = Camera.VerticalToHorizontalFieldOfView(playerCamera.fieldOfView, playerCamera.aspect) * 0.5f;
    }

    // 자기 위치·경로 위 미래 위치·웨이포인트 검사가 전부 이 하나를 쓴다
    // extraAngle: 화면 가장자리 바깥으로 넓힐 각도, extraDistance: 시야 거리에 더할 거리
    public bool IsPointSeen(Vector3 point, float extraAngle = 0f, float extraDistance = 0f)
    {
        if (playerCamera == null) return false;

        Transform cam = playerCamera.transform;
        Vector3 camPos = cam.position;
        Vector3 toPoint = point - camPos;
        float dist = toPoint.magnitude;

        // 1. 너무 멀면 안 보임
        if (dist > Settings.sightDistance + extraDistance) return false;

        // 2. 넓힌 화면 피라미드 밖이면 안 보임 — 카메라 기준 좌표(오른쪽 x, 위 y, 앞 z)로 바꿔 가로·세로를 따로 검사
        Vector3 local = Quaternion.Inverse(cam.rotation) * toPoint;
        if (local.z <= 0f) return false;   // 카메라 뒤

        RefreshHalfAngles();
        float maxH = Mathf.Min(horizontalHalf + extraAngle, MaxHalfAngle) * Mathf.Deg2Rad;
        float maxV = Mathf.Min(verticalHalf + extraAngle, MaxHalfAngle) * Mathf.Deg2Rad;
        if (Mathf.Abs(local.x) > local.z * Mathf.Tan(maxH)) return false;
        if (Mathf.Abs(local.y) > local.z * Mathf.Tan(maxV)) return false;

        // 3. 가림 판정 — 인형 자신은 제외한다 (곧 비켜날 몸이 미래 위치를 가리는 것은 '숨겨짐'이 아니다)
        //    트리거는 가리지 않는다 (2026-11-02): 얼룩(바닥 그림, 트리거 구)·탈출 구역은 눈에 보이는 벽이 아니다.
        //    「코드 설계 - 얼룩 물리 충돌 제거(트리거)」
        if (dist > 0f && Physics.Raycast(camPos, toPoint / dist, out RaycastHit hit, dist, obstructionMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform != transform && !hit.transform.IsChildOf(transform))
                return false;
        }

        return true;
    }

    // 정지 판정용 — 실제 화면보다 여유만큼 넓게 적용해 '보이기 전에' 멈추게 한다
    public bool IsInDangerZone()
        => IsPointInDangerZone(transform.position + Vector3.up * eyeHeightOffset);

    public bool IsPointInDangerZone(Vector3 point)
        => IsPointSeen(point, Settings.stopMarginAngle, Settings.stopMarginDistance);
}
