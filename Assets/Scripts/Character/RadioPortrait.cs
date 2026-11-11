using UnityEngine;

// 무전 초상 촬영장 (2026-10-23) — 화면 밖(바닥 아래)에 둔 한 주임 모델을 전용 카메라로 찍어 Render Texture에 그린다.
// - 무전 박스가 보이는 동안만, 초당 fps번만 그림 (CctvFeed와 같은 방식: 그릴 프레임에만 카메라를 켬)
// - 말하는 동안 음절 박자로 몸을 살짝 눌렀다 폄 + 끄덕임 (페그돌 규칙: 리깅 없이 스쿼시·기울임, 부피 유지)
//   얼굴 텍스처가 하나뿐이라 입은 움직이지 않는다. 표정 텍스처가 생기면 SetFace로 교체
// - 촬영 카메라에 MainCamera 태그를 달지 않는다 → 빈대 시야 판정(Camera.main)과 무관
// 설계 기록: 프로젝트 문서 「코드 설계 - 맵 인트로·무전」 2.7
public class RadioPortrait : MonoBehaviour
{
    [SerializeField] private Camera cam;
    [Tooltip("움직일 모델 (피벗 = 발바닥)")]
    [SerializeField] private Transform visual;
    [Tooltip("얼굴 재질이 있는 렌더러 (SetFace용)")]
    [SerializeField] private Renderer faceRenderer;
    [SerializeField] private int faceMaterialIndex = 1;

    [Tooltip("초당 그리는 횟수. 너무 낮으면 움직임이 끊겨 떨려 보임 (처음 값 24)")]
    [SerializeField] private float renderFps = 30f;

    [Header("말하기 움직임 (2026-10-23 피드백 \"부들부들 떤다\" → 느리고 작게)")]
    [Tooltip("말할 때 몸이 눌렸다 펴지는 횟수 (초당). 처음 값 6.5는 떨림처럼 보였음")]
    [SerializeField] private float talkBeatsPerSecond = 2.2f;
    [Tooltip("말할 때 세로로 눌리는 정도. 처음 값 0.03")]
    [SerializeField] private float talkSquashAmount = 0.01f;
    [Tooltip("말할 때 고개 끄덕임 (도). 처음 값 1.5")]
    [SerializeField] private float talkNod = 0.6f;
    [Tooltip("말하기 시작·끝날 때 움직임이 커지고 작아지는 시간 (초)")]
    [SerializeField] private float talkBlendSeconds = 0.25f;
    [SerializeField] private float breathAmount = 0.012f;
    [SerializeField] private float breathPeriod = 3.2f;

    /// <summary>무전 박스가 말하는 동안 켬</summary>
    public bool Talking { get; set; }

    public RenderTexture Texture => cam != null ? cam.targetTexture : null;

    private bool rendering;
    private float renderTimer;
    private float phase, time, talkWeight;
    private Vector3 baseScale = Vector3.one;
    private Quaternion baseRot = Quaternion.identity;

    void Awake()
    {
        if (cam != null) cam.enabled = false;
        if (visual != null) { baseScale = visual.localScale; baseRot = visual.localRotation; }
    }

    /// <summary>무전 박스가 보이는 동안 true</summary>
    public void SetRendering(bool on)
    {
        if (on && !rendering) renderTimer = 0f;   // 켜지는 순간 바로 한 장
        rendering = on;
    }

    /// <summary>표정 교체 (얼굴 텍스처). 지금은 face_han.png 하나뿐</summary>
    public void SetFace(Texture tex)
    {
        if (faceRenderer == null || tex == null) return;
        var mats = faceRenderer.materials;
        if (faceMaterialIndex < 0 || faceMaterialIndex >= mats.Length) return;
        if (mats[faceMaterialIndex].HasProperty("_BaseMap")) mats[faceMaterialIndex].SetTexture("_BaseMap", tex);
        else mats[faceMaterialIndex].mainTexture = tex;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;

        if (cam != null)
        {
            cam.enabled = false;
            if (rendering)
            {
                renderTimer -= dt;
                if (renderTimer <= 0f)
                {
                    cam.enabled = true;   // 이번 프레임에만 그림
                    renderTimer = 1f / Mathf.Max(1f, renderFps);
                }
            }
        }

        if (visual == null || dt <= 0f) return;
        time += dt;
        // 말하기 ↔ 숨쉬기를 끊지 않고 섞는다 (갑자기 움직임이 바뀌면 튀어 보임)
        talkWeight = Mathf.MoveTowards(talkWeight, Talking ? 1f : 0f, dt / Mathf.Max(0.01f, talkBlendSeconds));
        phase += dt * talkBeatsPerSecond;
        float talk = Mathf.Sin(phase * Mathf.PI * 2f) * 0.5f + 0.5f;            // 0~1, 부드러운 사인 (꺾이는 |sin| 대신)
        float breath = Mathf.Sin(time * Mathf.PI * 2f / Mathf.Max(0.1f, breathPeriod));
        float y = 1f + breathAmount * breath * (1f - talkWeight) - talkSquashAmount * talk * talkWeight;
        float nod = talkNod * Mathf.Sin(phase * Mathf.PI) * talkWeight;   // 끄덕임은 눌림의 절반 박자
        float xz = 1f / Mathf.Sqrt(y);   // 부피 유지
        visual.localScale = Vector3.Scale(baseScale, new Vector3(xz, y, xz));
        visual.localRotation = baseRot * Quaternion.Euler(nod, 0f, 0f);
    }
}
