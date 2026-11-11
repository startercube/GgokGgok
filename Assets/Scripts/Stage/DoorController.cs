using System.Collections;
using UnityEngine;

// 승리 조건(모든 Stain 청소 + 모든 Doll 포획) 달성 시 옆으로 밀려 열리는 문
[RequireComponent(typeof(Collider))]
public class DoorController : MonoBehaviour
{
    [Header("Open Motion")]
    [Tooltip("문이 열릴 때 밀려날 방향과 거리 (문의 로컬 기준). 문의 가로 폭(Scale.x) 이상이어야 완전히 비켜난다")]
    [SerializeField] private Vector3 openOffset = new Vector3(-3f, 0f, 0f);
    [Tooltip("문이 완전히 열리기까지 걸리는 시간(초)")]
    [SerializeField] private float openDuration = 2f;

    [Header("Exit")]
    [Tooltip("문이 다 열린 뒤 활성화할 탈출 구역 (빛나는 흰색 구역). 시작 시 자동으로 꺼둔다")]
    [SerializeField] private GameObject exitZone;
    [SerializeField] private CameraManagement camManager;

    [Header("Split Sliding (선택, 2026-10-12 캡슐형 문)")]
    [Tooltip("비워 두면 기존처럼 문 전체가 openOffset만큼 밀린다. 채우면 각 문짝이 문 중심 기준 자기 쪽(좌·우)으로 panelSlide만큼 갈라져 벽 속으로 들어간다")]
    [SerializeField] private Transform[] splitPanels;
    [SerializeField] private float panelSlide = 0.7f;

    [Header("Fade Out_할로윈")]
    [Tooltip("켜면 옆으로 밀지 않고 제자리에서 아래→위로 그라데이션되며 사라진다. 문 머티리얼이 Custom/DoorFadeLit 셰이더여야 한다")]
    [SerializeField] private bool fadeOut = false;

    [Header("Status Light (선택)")]
    [Tooltip("테두리 빛 렌더러. 잠김 = lockedColor, 열림 = openColor (Open()을 부르는 순간 바뀜)")]
    [SerializeField] private Renderer[] statusLights;
    [SerializeField] private Color lockedColor = new Color(1f, 0.22f, 0.18f);
    [SerializeField] private Color openColor = new Color(0.2f, 1f, 0.5f);
    [Tooltip("잠김일 때 밝기 (어둡게). 색약이어도 밝기 차이로 잠김/열림이 구분되게")]
    [SerializeField] private float lockedIntensity = 0.6f;
    [Tooltip("열림일 때 밝기 (밝게)")]
    [SerializeField] private float openIntensity = 3f;
    [Tooltip("열리는 순간 반짝임: 최대 밝기 / 횟수 / 한 번 길이(초)")]
    [SerializeField] private float flashIntensity = 8f;
    [SerializeField] private int flashCount = 2;
    [SerializeField] private float flashDuration = 0.18f;

    [Header("Sound (선택)")]
    [Tooltip("Open()을 부르는 순간 재생. 비우면 소리 없음. 자신 또는 부모의 AudioSource로 재생 (없으면 자동 추가)")]
    [SerializeField] private AudioClip openClip;
    [Tooltip("Close()를 부르는 순간 재생. 비우면 openClip을 재생")]
    [SerializeField] private AudioClip closeClip;

    private Collider doorCollider;
    private AudioSource audioSource;
    private bool isOpening = false;
    private Vector3 closedLocalPos;        // 닫힌 자리 (Close가 되돌아갈 곳)
    private Vector3[] closedPanelPos;

    void Awake()
    {
        doorCollider = GetComponent<Collider>();
        if (openClip != null || closeClip != null)
        {
            audioSource = GetComponentInParent<AudioSource>();   // 문 자신 → 부모(Doors) 순으로 찾음. Doors에 하나 붙이면 모든 문이 공유
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }
        }
        closedLocalPos = transform.localPosition;
        if (splitPanels != null)
        {
            closedPanelPos = new Vector3[splitPanels.Length];
            for (int i = 0; i < splitPanels.Length; i++)
                if (splitPanels[i] != null) closedPanelPos[i] = splitPanels[i].localPosition;
        }

        // 문이 열리기 전에는 탈출(승리 확정)이 불가능해야 한다
        if (exitZone != null)
            exitZone.SetActive(false);

        SetLight(false);
    }

    void OnValidate()
    {
        if (!Application.isPlaying) SetLight(false);
    }

    /// <summary>테두리 빛 상태. open = true면 열림(밝은 초록), false면 잠김(어두운 빨강) — 색 + 밝기 두 가지로 구분</summary>
    public void SetLight(bool open)
    {
        ApplyLight(open ? openColor : lockedColor, open ? openIntensity : lockedIntensity);
    }

    private void ApplyLight(Color c, float intensity)
    {
        if (statusLights == null) return;
        var mpb = new MaterialPropertyBlock();
        float baseK = Mathf.Clamp01(intensity / Mathf.Max(openIntensity, 0.01f) + 0.2f);   // 잠김이면 바탕색도 어둡게
        foreach (var r in statusLights)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", c * baseK);
            mpb.SetColor("_EmissionColor", c * intensity);
            r.SetPropertyBlock(mpb);
        }
    }

    // 열리는 순간 flashCount번 반짝인 뒤 열림 밝기로 유지
    private IEnumerator FlashRoutine()
    {
        for (int i = 0; i < flashCount; i++)
        {
            float e = 0f;
            while (e < flashDuration)
            {
                e += Time.deltaTime;
                float k = Mathf.Sin(Mathf.Clamp01(e / flashDuration) * Mathf.PI);
                ApplyLight(openColor, Mathf.Lerp(openIntensity, flashIntensity, k));
                yield return null;
            }
        }
        SetLight(true);
    }

    public void Open()
    {
        if (isOpening) return;

        isOpening = true;
        SetLight(true);
        if (audioSource != null) audioSource.PlayOneShot(openClip);
        if (statusLights != null && statusLights.Length > 0 && flashCount > 0)
            StartCoroutine(FlashRoutine());
        // GameManager는 MainScene에만 있다 — 스테이지 씬을 바로 실행하면 null (ExitZone과 같은 방식으로 방어)
        bool isReplay = GameManager.Instance != null && GameManager.Instance.isReplayMode;
        if (camManager != null && !isReplay)
        {
            camManager.doorCam.Priority = 70;
        }

        StartCoroutine(OpenRoutine());
        
    }

    private IEnumerator OpenRoutine()
    {
        if (fadeOut)
            yield return FadeRoutine();
        else
            yield return SlideRoutine();

        doorCollider.enabled = false;   // 벽에 덜 들어가도 통과는 보장

        if (exitZone != null)
            exitZone.SetActive(true);

        if (camManager != null)
        {
            yield return new WaitForSeconds(1f);
            camManager.doorCam.Priority = 1;
        }
    }

    // 제자리에서 아래→위로 사라짐 (DoorFadeLit 셰이더의 _Fade를 0→1)
    private IEnumerator FadeRoutine()
    {
        var renderers = GetComponentsInChildren<MeshRenderer>();
        var mpb = new MaterialPropertyBlock();
        foreach (var r in renderers)
        {
            // 셰이더가 메시 높이 기준으로 그라데이션을 계산하도록 바닥·꼭대기 높이를 넘긴다
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            Bounds b = mf.sharedMesh.bounds;
            r.GetPropertyBlock(mpb);
            mpb.SetFloat("_FadeMinY", b.min.y);
            mpb.SetFloat("_FadeMaxY", b.max.y);
            r.SetPropertyBlock(mpb);
        }

        float elapsed = 0f;
        while (elapsed < openDuration)
        {
            elapsed += Time.deltaTime;
            SetFade(renderers, mpb, EaseInOutCubic(Mathf.Clamp01(elapsed / openDuration)));
            yield return null;
        }
        SetFade(renderers, mpb, 1f);

        foreach (var r in renderers)
            r.enabled = false;
    }

    private void SetFade(MeshRenderer[] renderers, MaterialPropertyBlock mpb, float fade)
    {
        foreach (var r in renderers)
        {
            r.GetPropertyBlock(mpb);
            mpb.SetFloat("_Fade", fade);
            r.SetPropertyBlock(mpb);
        }
    }

    private IEnumerator SlideRoutine()
    {
        Vector3 startPos = transform.localPosition;
        // 회전에 맞춰 변환 — 문이 어느 방향을 보고 있어도 '옆으로' 밀린다
        Vector3 endPos = startPos + transform.localRotation * openOffset;

        // 좌우 분할 문: 각 문짝이 문 중심 기준 자기 쪽으로 갈라짐 (문짝은 이 오브젝트의 자식)
        bool split = splitPanels != null && splitPanels.Length > 0;
        Vector3[] pStart = null, pEnd = null;
        if (split)
        {
            pStart = new Vector3[splitPanels.Length];
            pEnd = new Vector3[splitPanels.Length];
            for (int i = 0; i < splitPanels.Length; i++)
            {
                var p = splitPanels[i];
                if (p == null) continue;
                var r = p.GetComponentInChildren<Renderer>();
                float side = r != null ? Mathf.Sign(transform.InverseTransformPoint(r.bounds.center).x) : (i == 0 ? -1f : 1f);
                pStart[i] = p.localPosition;
                pEnd[i] = p.localPosition + new Vector3(side * panelSlide, 0f, 0f);
            }
        }

        float elapsed = 0f;
        while (elapsed < openDuration)
        {
            elapsed += Time.deltaTime;
            float t = EaseInOutCubic(Mathf.Clamp01(elapsed / openDuration));
            if (split)
            {
                for (int i = 0; i < splitPanels.Length; i++)
                    if (splitPanels[i] != null) splitPanels[i].localPosition = Vector3.Lerp(pStart[i], pEnd[i], t);
            }
            else
                transform.localPosition = Vector3.Lerp(startPos, endPos, t);
            yield return null;
        }
        if (split)
        {
            for (int i = 0; i < splitPanels.Length; i++)
                if (splitPanels[i] != null) splitPanels[i].localPosition = pEnd[i];
        }
        else
            transform.localPosition = endPos;
    }

    /// <summary>열린 문을 다시 닫는다 (2026-10-18, 튜토리얼 연습 구역: 입장하면 닫히고 승리하면 Open으로 다시 열림). 닫힌 뒤 Open을 다시 부를 수 있다</summary>
    public void Close()
    {
        StopAllCoroutines();
        isOpening = false;
        doorCollider.enabled = true;
        if (exitZone != null) exitZone.SetActive(false);
        SetLight(false);
        AudioClip clip = closeClip != null ? closeClip : openClip;
        if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
        StartCoroutine(CloseRoutine());
    }

    private IEnumerator CloseRoutine()
    {
        bool split = splitPanels != null && splitPanels.Length > 0 && closedPanelPos != null;
        Vector3 startPos = transform.localPosition;
        Vector3[] pFrom = null;
        if (split)
        {
            pFrom = new Vector3[splitPanels.Length];
            for (int i = 0; i < splitPanels.Length; i++)
                if (splitPanels[i] != null) pFrom[i] = splitPanels[i].localPosition;
        }

        float elapsed = 0f;
        while (elapsed < openDuration)
        {
            elapsed += Time.deltaTime;
            float t = EaseInOutCubic(Mathf.Clamp01(elapsed / openDuration));
            if (split)
            {
                for (int i = 0; i < splitPanels.Length; i++)
                    if (splitPanels[i] != null) splitPanels[i].localPosition = Vector3.Lerp(pFrom[i], closedPanelPos[i], t);
            }
            else
                transform.localPosition = Vector3.Lerp(startPos, closedLocalPos, t);
            yield return null;
        }
        if (split)
        {
            for (int i = 0; i < splitPanels.Length; i++)
                if (splitPanels[i] != null) splitPanels[i].localPosition = closedPanelPos[i];
        }
        else
            transform.localPosition = closedLocalPos;
    }

    private float EaseInOutCubic(float t)
    {
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
    }
}
