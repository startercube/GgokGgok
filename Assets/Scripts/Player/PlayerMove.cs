using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

// 플레이어 이동 + 카메라 좌우 회전을 몸통에 동기화. 감도의 원본은 GameManager
// 카메라 입력 축 설정(Gain·CancelDeltaTime·가감속)은 씬 값 대신 ApplySensitivity가 덮어쓴다 → 모든 맵에서 같은 감도
[RequireComponent(typeof(Rigidbody))]
public class PlayerMove : MonoBehaviour, IReplayPausable
{
    [Header("시네머신")]
    public CinemachineCamera cinemachineCamera;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 7.5f;

    private float mouseSensitivity = 1f;

    private CinemachinePanTilt panTilt;
    private CinemachineInputAxisController axisController;
    private float[] axisSigns;   // 축 방향(Pan +, Tilt -)만 씬 Gain의 부호에서 가져온다. 크기는 코드가 정하므로 씬마다 Gain이 달라도 결과가 같다
    private Rigidbody rb;
    private Vector3 moveInput;
    private float yaw;           // 카메라 Pan 각도 = 몸통이 바라보는 방향

    // 벽·가구에 비빌 때 화면이 떨리지 않도록 하는 물리 설정 (2026-10-18)
    // 원인: transform.Translate로 콜라이더 안까지 밀어 넣으면 물리가 매 스텝 밖으로 튕겨내 위치가 앞뒤로 진동했다
    [Header("Wall Contact")]
    [Tooltip("콜라이더에 파고들었을 때 밀려나는 최대 속도. 낮을수록 부드럽게 빠져나옴")]
    [SerializeField] private float maxDepenetrationVelocity = 1f;

    // ===== 초기화 =====

    void Start()
    {
        mouseSensitivity = GameManager.MouseSensitivity;

        rb = GetComponent<Rigidbody>();
        ConfigurePhysics();

        if (cinemachineCamera != null)
        {
            panTilt = cinemachineCamera.GetComponent<CinemachinePanTilt>();
            axisController = cinemachineCamera.GetComponent<CinemachineInputAxisController>();
            CacheAxisSigns();
            ApplySensitivity();
        }
        else
            Debug.LogWarning("CinemachineCamera가 할당되지 않았습니다. 카메라 회전 동기화가 동작하지 않습니다.");

        Cursor.visible = false;
    }

    // ===== 이동 =====

    void Update()
    {
        // 카메라의 좌우(Pan) 회전 = 보는 방향이 곧 정면. 실제 회전 적용은 FixedUpdate에서 물리로
        if (panTilt != null)
            yaw = panTilt.PanAxis.Value;
    }

    // 이동은 위치를 직접 옮기지 않고 속도로 준다 — 벽에 닿으면 물리가 벽을 따라 미끄러지게 처리해 떨림이 없다
    void FixedUpdate()
    {
        if (rb == null || rb.isKinematic)
            return;

        Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
        rb.MoveRotation(facing);

        Vector3 wish = facing * Vector3.ClampMagnitude(moveInput, 1f) * moveSpeed;
        Vector3 v = rb.linearVelocity;
        v.x = wish.x;
        v.z = wish.z;      // y(중력)는 물리에 맡긴다
        rb.linearVelocity = v;
    }

    // 벽 비빔 떨림 방지: 보간·연속 충돌·회전 고정·마찰 0
    private void ConfigurePhysics()
    {
        rb.interpolation = RigidbodyInterpolation.Interpolate;              // 물리 스텝 사이 위치를 보간 → 카메라가 계단식으로 튀지 않음
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.constraints = RigidbodyConstraints.FreezeRotation;               // 모서리에 부딪혀 몸통이 돌지 않게 (Y 회전은 스크립트가 MoveRotation으로)
        rb.maxDepenetrationVelocity = maxDepenetrationVelocity;

        // 마찰이 있으면 벽에 붙었다 떨어졌다를 반복하며 덜컹거린다
        var col = GetComponent<Collider>();
        if (col != null)
        {
            col.material = new PhysicsMaterial("PlayerFrictionless")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
        }

        yaw = transform.eulerAngles.y;
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        Vector2 dir = context.ReadValue<Vector2>();
        moveInput = new Vector3(dir.x, 0f, dir.y);
    }

    void OnEnable()
    {
        GameManager.OnSensitivityChanged += HandleSensitivityChanged;
    }

    void OnDisable()
    {
        GameManager.OnSensitivityChanged -= HandleSensitivityChanged;   // static 이벤트라 해제 필수

        // 속도 이동이라 꺼질 때 마지막 속도가 남으면 계속 미끄러진다 (인트로 정지 등)
        moveInput = Vector3.zero;
        if (rb != null && !rb.isKinematic)
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
    }

    // 설정 슬라이더를 움직이면 플레이 중에도 즉시 반영된다
    private void HandleSensitivityChanged(float value)
    {
        mouseSensitivity = value;
        ApplySensitivity();
    }

    // ===== 마우스 감도 =====

    private void CacheAxisSigns()
    {
        if (axisController == null)
            return;

        var controllers = axisController.Controllers;
        axisSigns = new float[controllers.Count];
        for (int i = 0; i < controllers.Count; i++)
            axisSigns[i] = controllers[i].Input.Gain < 0f ? -1f : 1f;
    }

    // 설정 UI 슬라이더(OnValueChanged)에 연결하는 진입점
    public void SetMouseSensitivity()
    {
        ApplySensitivity();
    }

    // 회전(도) = 마우스 카운트 × DegreesPerCount × 감도 가 되도록 카메라 입력 축(Pan/Tilt)을 설정 (2026-11-01 「코드 설계 - 마우스 감도 프레임 독립」)
    // 예전엔 시네머신이 마우스 이동량을 속도로 보고 deltaTime을 한 번 더 곱해, FPS가 높은 맵일수록 감도가 낮아졌다
    private void ApplySensitivity()
    {
        if (axisController == null || axisSigns == null)
            return;

        var controllers = axisController.Controllers;
        for (int i = 0; i < controllers.Count && i < axisSigns.Length; i++)
        {
            var c = controllers[i];
            c.Input.CancelDeltaTime = true;   // 입력값 ÷ deltaTime → 드라이버의 × deltaTime과 상쇄. 마우스 델타는 이미 '이번 프레임 이동량'
            c.Input.Gain = axisSigns[i] * GameManager.DegreesPerCount * mouseSensitivity;
            c.Driver.AccelTime = 0f;          // 가속·관성 없이 손 움직임 1:1 (인스펙터에서 바뀌어도 마우스 감각 고정)
            c.Driver.DecelTime = 0f;
        }
    }

    // 인스펙터에서 감도 값을 바꾸면 플레이 중에도 바로 반영
    void OnValidate()
    {
        if (Application.isPlaying)
            ApplySensitivity();
    }
}
