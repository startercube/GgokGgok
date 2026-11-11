using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// 화면 중앙 조준으로 얼룩 청소·인형 잡기. 위장한 Prop 중 DollMove가 붙은 것이 진짜 인형이다
// 조준은 벽·문·가구 뒷판 너머를 맞히지 않는다 (2026-10-23~24): 조준선에서 대상보다 앞에 단단한 콜라이더가 있으면 가려진 것.
// - 대상 = 맞은 콜라이더에서 부모로 올라가며 처음 만나는 Interactable 레이어 + Prop/Stain 태그 오브젝트 (인형 모델 자식 콜라이더는 가림이 아님)
// - 얼룩은 구 콜라이더(0.44m, 벽을 뚫고 나옴)가 아니라 조준선이 얼룩 그림 면을 지나는 점으로 판정. 구는 트리거라 몸은 막지 않고 후보 찾기에만 쓴다
// 가구는 실제 모양 MeshCollider라(DollRoomFurnitureBuilder) 선반 칸 속 인형은 열린 쪽에서 그대로 잡힌다. 설계: 「코드 설계 - 조준 가림 판정·튜토리얼 바라보기」
// 조준 대상은 외곽선(투시)으로 표시한다 (2026-11-01): 대상이 속한 물체 전체를 AimHighlight에 넘기고 AimOutlineFeature가 그린다.
// 변장한 외계인은 부품마다 Prop이라, 부품이 아니라 인형 전체가 빛나야 미끼와 똑같이 보인다(HighlightUnitOf). 설계: 「코드 설계 - 조준 외곽선(투시)」
// 오답 쿨타임은 막 찍기에만 길어진다 (2026-11-01): 쿨타임이 끝나자마자(hastyWindow 안) 또 틀리는 일이 이어지면 늘고,
// 생각하고 찍은 오답·정답 포획이면 처음 길이로 돌아간다(NextCooldown). 설계: 「코드 설계 - 오답 쿨타임 막 찍기 가중」
public class PlayerInteraction : MonoBehaviour, IReplayPausable
{
    enum InteractionState
    {
        None,
        Stain,
        Prop
    }

    [Header("오답 쿨타임 (막 찍기 가중)")]
    [Tooltip("첫 오답·생각하고 찍은 오답의 쿨타임(초)")]
    [SerializeField] private float baseCooldown = 2f;
    [Tooltip("쿨타임이 끝나고 이 시간(초) 안에 또 틀리면 '막 찍기'로 센다")]
    [SerializeField] private float hastyWindow = 2f;
    [Tooltip("막 찍기가 이만큼 이어지면 쿨타임이 늘어나기 시작한다")]
    [SerializeField] private int hastyCountToEscalate = 2;
    [Tooltip("늘어날 때마다 곱하는 배수")]
    [SerializeField] private float escalateMultiplier = 2f;
    [Tooltip("쿨타임 상한(초)")]
    [SerializeField] private float maxCooldown = 10f;
    [Space]
    [Tooltip("조준이 닿는 거리")]
    [SerializeField] private float interactDistance = 6f;
    [Tooltip("조준선이 얼룩 그림 가장자리에서 이만큼 벗어나도 얼룩을 조준한 것으로 친다")]
    [SerializeField] private float stainAimPad = 0.03f;
    [Tooltip("얼룩이 놓인 면(바닥·가구 윗면)으로 보고 가림에서 뺄 높이 차이 — 얼룩은 NavMesh 표면 + 2cm라 바닥 콜라이더와 조금 어긋남")]
    [SerializeField] private float stainSurfaceTolerance = 0.06f;
    [Tooltip("켜면 조준 대상이 바뀔 때 무엇을 조준했는지·무엇에 가렸는지 콘솔에 남긴다 (벽 너머 잡기 확인용)")]
    [SerializeField] private bool debugAim = false;

    private Camera playerCamera;
    private InteractionState currentInteractionState;
    private GameObject currentTarget;   // 조준 중인 오브젝트 — E키의 대상
    private int interactableLayer;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[32];
    private GameObject lastAimLogged;
    private string lastBlockLogged;
    private static readonly IComparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));
    private float cooldownEndTime = -999f;  // 지금(또는 마지막) 오답 쿨타임이 끝나는 시각
    private int hastyStreak;                 // 쿨타임 직후 바로 틀린 오답이 연달아 몇 번인가
    private float currentCooldown;

    /// <summary>마지막 오답에 매긴 쿨타임 길이(초)</summary>
    public float CurrentCooldown => currentCooldown;
    /// <summary>막 찍기 연속 횟수 (0 = 생각하고 찍음)</summary>
    public int HastyStreak => hastyStreak;

    /// <summary>잡기 오답 알림 — 튜토리얼 진행 관리가 "첫 오답" 대사에 쓴다 (2026-10-19)</summary>
    public static event System.Action OnWrongGuess;
    /// <summary>쿨타임 중 잡기 시도 알림 — 튜토리얼 "야야, 장비 부서진다." 대사 (2026-10-21). 나중에 확정안의 틱 소리·조준점 흔들림도 여기에</summary>
    public static event System.Action OnCatchBlocked;

    void Start()
    {
        playerCamera = Camera.main;
        if (playerCamera == null)
            Debug.LogWarning("Main Camera not found. Please ensure the main camera is tagged as 'MainCamera'.");

        currentInteractionState = InteractionState.None;
        interactableLayer = LayerMask.NameToLayer("Interactable");
    }

    void Update()
    {
        if (playerCamera == null) return;

        InteractionState newState = InteractionState.None;
        GameObject newTarget = null;

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        GameObject aimed = FindAimTarget(ray);
        if (aimed != null)
        {
            if (aimed.CompareTag("Stain"))
            {
                newState = InteractionState.Stain;
                newTarget = aimed;
            }
            else if (aimed.CompareTag("Prop"))
            {
                newState = InteractionState.Prop;
                newTarget = aimed;
            }
        }

        currentTarget = newTarget;
        // 외곽선: 같은 대상이면 AimHighlight가 아무것도 하지 않으므로 매 프레임 불러도 된다. GameHUD가 없는 씬에서도 동작하게 프롬프트보다 앞에
        AimHighlight.Set(newTarget != null ? HighlightUnitOf(newTarget) : null, playerCamera);

        // 매 프레임 UI를 건드리지 않도록, 상태가 바뀔 때만 프롬프트 갱신
        if (newState != currentInteractionState)
        {
            currentInteractionState = newState;

            if (GameHUD.Instance == null) return;

            if (currentInteractionState == InteractionState.None)
                GameHUD.Instance.HidePrompt();
            else
                GameHUD.Instance.ShowPrompt(GetPromptText(currentInteractionState));
        }
    }

    // 리플레이(IReplayPausable 일괄 끄기)·튜토리얼 잠금·카메라 전환으로 꺼지면 외곽선도 지운다
    void OnDisable()
    {
        if (AimHighlight.ViewCamera == playerCamera) AimHighlight.Clear();
    }

    // 외곽선을 그릴 단위: 조준 대상에서 바로 위 부모가 Interactable + 같은 태그인 동안 올라간 가장 바깥 오브젝트.
    // 변장한 외계인(DollDisguise)은 부품마다 Prop이고 부모 인형 루트도 Prop → 인형 전체. 미끼 소품·얼룩은 자기 자신.
    // 잡기도 부품 어디를 잡든 GetComponentInParent<DollMove>()로 인형 전체를 잡으므로 외곽선 범위 = 잡기 범위.
    // 중간에 다른 오브젝트가 끼면 멈춤 — Prop 태그가 붙은 묶음 오브젝트가 있어도 그 아래 소품이 한꺼번에 빛나지 않게
    GameObject HighlightUnitOf(GameObject owner)
    {
        Transform unit = owner.transform;
        for (Transform t = unit.parent; t != null; t = t.parent)
        {
            if (t.gameObject.layer != interactableLayer || !t.CompareTag(owner.tag)) break;
            unit = t;
        }
        return unit.gameObject;
    }

    // 조준 대상(Prop/Stain 오브젝트). 조준선에서 그 대상보다 앞에 단단한 콜라이더가 있으면 가려진 것 → null
    GameObject FindAimTarget(Ray ray)
    {
        // 얼룩 구는 트리거다(몸을 막지 않게, 2026-11-02) → 반드시 Collide. Ignore로 바꾸면 얼룩이 조준되지 않는다
        int n = Physics.RaycastNonAlloc(ray, hitBuffer, interactDistance, Physics.AllLayers, QueryTriggerInteraction.Collide);
        System.Array.Sort(hitBuffer, 0, n, ByDistance);   // RaycastNonAlloc은 순서를 보장하지 않음

        GameObject best = null;
        float bestDist = float.PositiveInfinity;
        Collider blockedBy = null;
        GameObject blockedTarget = null;

        for (int i = 0; i < n; i++)
        {
            GameObject owner = OwnerOf(hitBuffer[i].collider);
            if (owner == null) continue;

            // 대상까지의 거리: 소품은 맞은 거리, 얼룩은 조준선이 얼룩 그림 면을 지나는 거리 (그림 밖이면 조준 안 됨)
            float dist = hitBuffer[i].distance;
            float surfaceY = float.NaN;
            if (owner.CompareTag("Stain") && !StainAimDistance(ray, owner, out dist, out surfaceY)) continue;
            if (dist >= bestDist) continue;

            Collider blocker = FirstBlocker(n, dist, surfaceY);
            if (blocker != null)
            {
                if (blockedBy == null) { blockedBy = blocker; blockedTarget = owner; }
                continue;
            }
            best = owner;
            bestDist = dist;
        }

        if (best != null) LogAim(best, null);
        else LogAim(blockedTarget, blockedBy);
        return best;
    }

    // 맞은 콜라이더의 잡기·청소 대상: 자신부터 부모로 올라가며 처음 만나는 Interactable + Prop/Stain 오브젝트.
    // 인형 프리팹은 루트 BoxCollider 아래 모델 자식에도 MeshCollider가 있어, 자식이 먼저 맞아도 같은 대상으로 본다
    GameObject OwnerOf(Collider col)
    {
        for (Transform t = col.transform; t != null; t = t.parent)
        {
            GameObject go = t.gameObject;
            if (go.layer == interactableLayer && (go.CompareTag("Prop") || go.CompareTag("Stain"))) return go;
        }
        return null;
    }

    // dist보다 앞에서 맞은 단단한 콜라이더 (트리거·플레이어 자신·잡기 대상의 일부는 제외).
    // 얼룩이면 그 얼룩이 놓인 면(위를 향하고 얼룩 높이와 거의 같은 곳)도 제외
    Collider FirstBlocker(int n, float dist, float surfaceY)
    {
        for (int j = 0; j < n; j++)
        {
            RaycastHit h = hitBuffer[j];
            if (h.distance >= dist) break;
            Collider c = h.collider;
            if (c.isTrigger || c.transform.IsChildOf(transform) || OwnerOf(c) != null) continue;
            if (!float.IsNaN(surfaceY) && h.normal.y > 0.7f && Mathf.Abs(h.point.y - surfaceY) <= stainSurfaceTolerance) continue;
            return c;
        }
        return null;
    }

    // 얼룩은 바닥에 눕힌 그림. 조준선이 그 수평면을 지나는 점이 그림 범위(+여유) 안이면 그 거리를 돌려준다
    bool StainAimDistance(Ray ray, GameObject stain, out float dist, out float surfaceY)
    {
        surfaceY = stain.transform.position.y;
        dist = 0f;
        if (ray.direction.y > -0.01f) return false;   // 위나 수평을 보면 바닥 그림을 조준할 수 없음
        dist = (surfaceY - ray.origin.y) / ray.direction.y;
        if (dist <= 0f || dist > interactDistance) return false;
        Vector3 p = ray.origin + ray.direction * dist;

        Renderer r = stain.GetComponent<Renderer>();
        if (r == null) return true;   // 그림이 없으면 구 콜라이더에 맞은 것으로 충분
        Bounds b = r.bounds;
        return Mathf.Abs(p.x - b.center.x) <= b.extents.x + stainAimPad
            && Mathf.Abs(p.z - b.center.z) <= b.extents.z + stainAimPad;
    }

    void LogAim(GameObject target, Collider blockedBy)
    {
        if (!debugAim) return;
        string block = blockedBy != null ? blockedBy.name : null;
        if (target == lastAimLogged && block == lastBlockLogged) return;
        lastAimLogged = target; lastBlockLogged = block;
        if (target == null) return;
        if (blockedBy != null) Debug.Log($"[조준] {target.name} → {blockedBy.name}에 가림", target);
        else Debug.Log($"[조준] {target.name} 조준됨", target);
    }

    string GetPromptText(InteractionState state)
    {
        return state == InteractionState.Stain ? "Clean" : "Catch";
    }

    public void OnInteraction(InputAction.CallbackContext context)
    {
        if (context.performed && currentTarget != null)
        {
            switch (currentInteractionState)
            {
                case InteractionState.Stain:
                    Debug.Log("Clean Stain");
                    if (GameProgressManager.Instance != null)
                        GameProgressManager.Instance.CleanStain();
                    if (GameHUD.Instance != null)
                        GameHUD.Instance.PlayStainCleaned();
                    // 파괴 후엔 ID 조회가 불가하므로 Destroy 전에 기록
                    if (ReplayRecorder.Instance != null)
                        ReplayRecorder.Instance.RecordStainClean(currentTarget);
                    Destroy(currentTarget);
                    currentTarget = null;
                    break;
                case InteractionState.Prop:
                    TryCatch();
                    break;
            }
        }
    }

    // 조준한 Prop이 진짜 Doll인지 판별. 오답이면 쿨타임 동안 잡기 시도 불가
    void TryCatch()
    {
        if (Time.time < cooldownEndTime)
        {
            Debug.Log("잡기 쿨타임 중...");
            OnCatchBlocked?.Invoke();
            return;
        }

        DollMove doll = currentTarget.GetComponentInParent<DollMove>();
        if (doll != null)
        {
            Debug.Log("Doll을 잡았다: " + doll.name);
            hastyStreak = 0;  // 정답이면 막 찍기 기록 초기화
            // OnCaught가 인형을 파괴하므로 그 전에 기록
            if (ReplayRecorder.Instance != null)
                ReplayRecorder.Instance.RecordDollCaught(doll);
            doll.OnCaught();
            if (GameProgressManager.Instance != null)
                GameProgressManager.Instance.CatchDoll();
            currentTarget = null;
        }
        else
        {
            currentCooldown = NextCooldown();
            cooldownEndTime = Time.time + currentCooldown;
            OnWrongGuess?.Invoke();
            if (GameHUD.Instance != null)
                GameHUD.Instance.ShowCatchFailed(currentCooldown);
            Debug.Log($"오답! {currentCooldown:0.#}초 동안 잡기 불가 (막 찍기 연속 {hastyStreak})");
        }
    }

    // 이번 오답의 쿨타임 길이. 쿨타임이 끝난 지 hastyWindow가 안 됐으면 막 찍기 +1, 아니면 0으로.
    // 막 찍기가 hastyCountToEscalate번 이어진 때부터 단계마다 escalateMultiplier배 (maxCooldown까지)
    float NextCooldown()
    {
        bool hasty = Time.time - cooldownEndTime < hastyWindow;
        hastyStreak = hasty ? hastyStreak + 1 : 0;

        int step = Mathf.Max(0, hastyStreak - hastyCountToEscalate + 1);
        float cooldown = baseCooldown * Mathf.Pow(escalateMultiplier, step);
        return Mathf.Min(maxCooldown, cooldown);
    }
}
