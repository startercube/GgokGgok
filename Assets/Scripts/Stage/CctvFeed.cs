using UnityEngine;

// CCTV 카메라 → 모니터 화면 (2026-10-21, 튜토리얼 격리실에서 처음 씀)
// 카메라가 Render Texture에 그리고, 모니터 화면 재질이 그 텍스처를 보여 준다 (연결은 배치 도구가 한다).
// - 모니터로 보는 것은 "관측"이 아니다: DollVision은 플레이어 카메라(Camera.main)만 검사하므로 이 카메라엔 MainCamera 태그를 달지 않는다
// - 카메라 하나를 더 그리는 비용을 아끼려고, 플레이어가 모니터 근처에 있을 때만 켠다 (멀어지면 마지막 화면이 남는다)
// - 일정 간격으로만 그려 CCTV처럼 살짝 끊겨 보이게 할 수 있다 (fps 0 = 매 프레임)
[RequireComponent(typeof(Camera))]
public class CctvFeed : MonoBehaviour
{
    [Tooltip("이 위치(모니터)에서 activeRadius 안에 플레이어가 있을 때만 그린다")]
    [SerializeField] private Transform monitor;
    [SerializeField] private float activeRadius = 12f;
    [Tooltip("초당 그리는 횟수. 0이면 매 프레임")]
    [SerializeField] private float fps = 15f;

    private Camera cam;
    private Transform player;
    private float nextRender;

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.enabled = false;
        if (CompareTag("MainCamera")) Debug.LogWarning($"[{name}] CCTV 카메라에 MainCamera 태그가 있으면 빈대 시야 판정이 이 카메라를 쓰게 됩니다.", this);
    }

    void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
    }

    void LateUpdate()
    {
        bool near = player == null || monitor == null ||
                    (player.position - monitor.position).sqrMagnitude <= activeRadius * activeRadius;
        if (!near) { cam.enabled = false; return; }

        if (fps <= 0f) { cam.enabled = true; return; }

        // 간격마다 한 프레임만 켠다 → 그 프레임에 텍스처가 갱신되고, 다음 프레임엔 꺼서 그리지 않는다
        if (Time.time >= nextRender)
        {
            cam.enabled = true;
            nextRender = Time.time + 1f / fps;
        }
        else cam.enabled = false;
    }
}
