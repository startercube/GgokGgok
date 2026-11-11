using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 움직이는 인형 AI — 보이면 멈추고, 안 보일 때 은신처를 옮기며 얼룩을 남긴다.
// 시야 판정은 DollVision, 얼룩 생성은 DollStainSpawner, 자리 예약은 WaypointRegistry 담당.
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(DollVision))]
[RequireComponent(typeof(DollStainSpawner))]
public class DollMove : MonoBehaviour, IReplayPausable
{
    [Header("Waypoints")]
    public Transform[] wayPoints;

    [Header("Player Detection")]
    public Transform playerTransform;
    public float escapeDistance = 5f;

    [Header("Stain")]
    [Tooltip("링크 도착점이 시작점보다 이만큼 높으면 '올라가는 이동'으로 보고 시작점(바닥)에 얼룩을 남김")]
    [SerializeField] private float linkStainHeight = 0.3f;
    [Tooltip("이보다 높은 은신처를 떠날 때는 그 자리에 얼룩을 남기지 않음 — 눈높이 위 얼룩은 보이지도 치우지도 못한다. 위치 단서는 올라갈 때 링크 발치에 남은 얼룩이 대신한다")]
    [SerializeField] private float stainMaxHeight = 1.3f;
    [Tooltip("떠난 자리에서 이만큼 멀어져야 그 자리에 얼룩을 남김 — 출발 직후 대기하면 얼룩 옆에 남은 인형이 정체를 드러낸다")]
    [SerializeField] private float stainLeaveDistance = 0.6f;

    [Header("Move Tokens (이동권)")]
    [Tooltip("은신(정지) 상태로 이 시간이 지날 때마다 이동권 1개 충전")]
    public float tokenChargeInterval = 5f;

    [Tooltip("이동권 최대 보유 개수")]
    public int maxMoveTokens = 3;

    [Tooltip("시작 시 보유한 이동권 개수")]
    public int startingMoveTokens = 0;

    [Tooltip("플레이어가 가까이 있을 때 과감하게 이동을 시도할 확률 (0~1)")]
    [Range(0f, 1f)] public float nearMoveChance = 0.2f;

    [Tooltip("게임 시작 직후 이동권 충전을 이만큼 미룸 — 시작하자마자 얼룩이 쏟아지는 것을 방지")]
    public float startGraceTime = 10f;

    [Header("Path Lookahead (경로 미리보기)")]
    [Tooltip("이동 중 앞으로 이 거리만큼의 경로가 위험 지대를 지나면 아직 숨어 있는 현재 위치에서 대기")]
    public float lookaheadDistance = 6f;
    [Tooltip("경로 가시성 검사의 샘플 간격 — 작을수록 촘촘하지만 Raycast 횟수가 늘어난다")]
    public float lookaheadStep = 0.5f;

    [Header("Progress")]
    [Tooltip("끄면 GameProgressManager의 '모든 인형 포획' 집계에서 빠진다. 튜토리얼 격리실 본모습처럼 잡는 대상이 아닌 개체용")]
    [SerializeField] private bool countsTowardGoal = true;
    public bool CountsTowardGoal => countsTowardGoal;

    [Header("Start / Look (2026-11-02, 튜토리얼)")]
    [Tooltip("켜면 0번 웨이포인트에서 시작한다 (기본: 플레이어에게서 가장 먼 웨이포인트). 튜토리얼 자리 섞기(TutorialSlotShuffle)가 0번을 옮겨 시작 자리를 정한다")]
    [SerializeField] private bool startAtFirstWaypoint = false;
    [Tooltip("켜면 무작위 변장(DollDisguise)을 하지 않고 자기 모습 그대로. 튜토리얼 빈대처럼 미끼와 모양을 맞춰 둔 개체용")]
    [SerializeField] private bool keepOwnLook = false;
    public bool KeepsOwnLook => keepOwnLook;

    [Header("Caught VFX")]
    [Tooltip("잡혔을 때 인형 위치에 생성할 이펙트 프리팹")]
    [SerializeField] private GameObject caughtVfxPrefab;

    [Tooltip("생성한 이펙트를 이 시간 뒤 파괴 — 0이면 파괴하지 않음")]
    [SerializeField] private float caughtVfxLifetime = 1f;

    // 이동·은신 상태. isFrozen(인트로)과 시선 정지는 일시적 멈춤이라 상태에 넣지 않는다
    private enum DollState
    {
        Hiding,               // 은신 중 — 이동권 충전
        Moving,               // 다음 은신처로 이동 중
        WaitingForClearPath,  // 갈 길이 위험 지대를 지나 대기 중
    }

    private NavMeshAgent agent;
    private DollVision vision;
    private DollStainSpawner spawner;
    private WaypointSelector selector;
    private DollState state = DollState.Moving;   // Start의 WarpToStartSpot이 곧바로 Hiding으로 바꾼다
    private bool isFrozen = false;             // 인트로 등 연출 중 완전 정지
    private int currentTargetIndex = -1;
    private int previousTargetIndex = -1;   // 이동 중에는 떠나온 자리도 계속 예약해 둔다
    private float lastNoSpotWarnTime = -999f;
    private bool isEscaping = false;
    [SerializeField] private int moveTokens;   // 현재 이동권 (디버그용으로 인스펙터에 표시)
    private float tokenChargeTimer = 0f;
    private bool leftStartSpot = false;        // 시작 자리를 한 번이라도 떠났는지
    private bool stainlessJourney = false;     // 이번 이동(첫 이동)은 얼룩 없이 진행 중
    private NavMeshPath plannedPath;           // 출발 전 경로 검사용 버퍼 (재사용)
    private readonly List<Vector3> pendingStains = new List<Vector3>();   // 떠나기로 한 자리 — 실제로 벗어난 뒤 얼룩을 남긴다

    void Awake()
    {
        vision = GetComponent<DollVision>();
        spawner = GetComponent<DollStainSpawner>();

        // 맵의 Prop 중 하나로 무작위 변장 — 리플레이 녹화·재생의 Start보다 먼저 끝나야 해서 Awake에서 한다
        DollDisguise.EnsureApplied(gameObject.scene);
    }

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        plannedPath = new NavMeshPath();

        // 링크 자동 통과 중엔 isStopped가 안 통해 "보면 안 움직인다"가 깨진다 — Update에서 직접 건넌다
        agent.autoTraverseOffMeshLink = false;

        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                playerTransform = player.transform;
            else
                Debug.LogWarning("Player object not found. Please assign playerTransform in the inspector.");
        }

        moveTokens = Mathf.Min(startingMoveTokens, maxMoveTokens);
        selector = new WaypointSelector(wayPoints, playerTransform, vision, IsSelectable);

        // 시작 배치는 순간이동 — 걸어가면 인트로 카메라에 위치·동선이 드러난다
        if (wayPoints.Length > 0)
            WarpToStartSpot();
        else
            Debug.LogWarning("Waypoints array is empty. Please assign waypoints in the inspector.");

        // 시작 직후 얼룩 러시 방지 — 첫 이동권 충전을 유예 시간만큼 미룬다
        tokenChargeTimer = -startGraceTime;
    }

    /// <summary>인트로 같은 연출 동안 인형을 완전히 정지시키거나 다시 풀어준다.</summary>
    public void SetFrozen(bool frozen)
    {
        isFrozen = frozen;
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (agent != null && agent.isOnNavMesh)
        {
            if (frozen) HaltAgent();
            else agent.isStopped = false;
        }
    }

    // 시작 은신처(플레이어에서 가장 먼 구역, startAtFirstWaypoint면 0번)로 순간이동. 실패하면 걸어간다
    void WarpToStartSpot()
    {
        int index = startAtFirstWaypoint ? 0 : selector.FindFarthest();
        if (index < 0) return;

        Vector3 goal = wayPoints[index].position;
        float tolerance = Mathf.Max(agent.radius * 2f, 0.3f);
        if (NavMesh.SamplePosition(goal, out NavMeshHit hit, tolerance, NavMesh.AllAreas) && agent.Warp(hit.position))
        {
            currentTargetIndex = index;
            WaypointRegistry.Occupy(wayPoints[index]);
            state = DollState.Hiding;   // 이미 은신처에 도착한 상태로 시작
        }
        else
        {
            SetTarget(index);   // NavMesh 문제는 SetTarget이 경고를 낸다
        }
    }

    void Update()
    {
        if (isFrozen) return;   // 인트로 연출 중 — 이동/판정 전부 중단
        if (playerTransform == null || wayPoints.Length == 0) return;

        // NavMesh 밖으로 벗어나면 이동이 영구히 실패한다 — 가까운 NavMesh 위로 복귀
        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit back, agent.height * 4f, NavMesh.AllAreas))
            {
                Debug.LogWarning($"[{name}] NavMesh 밖에 있어 복귀시킵니다 {transform.position} → {back.position}", this);
                agent.Warp(back.position);
            }
            else
            {
                Debug.LogWarning($"[{name}] 주변에 NavMesh가 없습니다 {transform.position}. 베이크를 확인하세요.", this);
                return;
            }
        }

        // 위험 지대 정지로 return하기 전에 처리 — 멈춰 있는 동안에도 이미 벗어난 자리의 얼룩은 남긴다
        UpdatePendingStains();

        // [위험 지대 정지] 실제 시야보다 넓게 판정해 시야에 들어오기 전에 멈춘다
        if (vision.IsInDangerZone())
        {
            HaltAgent();
            return;
        }
        agent.isStopped = false;

        // [링크 통과] 자동 통과가 꺼져 있으므로 여기서 순간이동으로 직접 건넌다
        if (agent.isOnOffMeshLink)
        {
            OffMeshLinkData link = agent.currentOffMeshLinkData;

            // 건너편이 위험 지대면 건너지 않는다 — 순간이동으로 시야 안에 '나타나는' 사고 방지
            if (vision.IsPointInDangerZone(link.endPos + Vector3.up * vision.eyeHeightOffset))
            {
                HaltAgent();
                state = DollState.WaitingForClearPath;
                return;
            }

            // 올라가는 이동일 때만 링크 시작점(바닥)에 얼룩 — 방향은 높이 비교로 판별
            if (!stainlessJourney && link.endPos.y > link.startPos.y + linkStainHeight)
                spawner.SpawnAt(link.startPos);

            agent.CompleteOffMeshLink();   // 순간이동으로 링크 도착점에 나타난다
            state = DollState.Moving;
        }

        // [경로 미리보기] 앞 구간이 위험 지대를 지나면 안전한 현재 위치에서 대기
        if (state != DollState.Hiding)
        {
            if (agent.hasPath && !agent.pathPending && IsPathAheadInDanger(agent.path.corners))
            {
                HaltAgent();
                state = DollState.WaitingForClearPath;
                return;
            }
            if (state == DollState.WaitingForClearPath)
                state = DollState.Moving;   // 시야가 풀렸으니 이동 재개
        }

        UpdateHidingState();
        HandleMoveDecision();
    }

    // 그 자리에서 즉시 정지 (2026-11-01 「코드 설계 - 빈대 시야 정지(즉시 정지·화면 기준 판정)」).
    // isStopped만 주면 NavMeshAgent가 가속도 값대로 감속해 약 0.2초·20cm 미끄러진다 — 빠르게 돌아본 화면에 그 미끄러짐이 보였다.
    // 멈추는 곳은 전부 이 함수를 쓴다. 다시 출발은 isStopped = false (화면 밖이므로 가속 출발이 자연스러움)
    void HaltAgent()
    {
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }

    // 경로의 앞 lookaheadDistance 구간을 lookaheadStep 간격으로 점찍어 위험 지대 통과 여부 검사
    bool IsPathAheadInDanger(Vector3[] corners)
    {
        float step = Mathf.Max(0.1f, lookaheadStep);   // 0 이하 설정 시 무한 루프 방지
        Vector3 eyeOffset = Vector3.up * vision.eyeHeightOffset;
        float remaining = lookaheadDistance;

        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 from = corners[i - 1];
            Vector3 to = corners[i];
            float segLen = Vector3.Distance(from, to);
            float limit = Mathf.Min(segLen, remaining);

            for (float d = 0f; d <= limit; d += step)
            {
                if (vision.IsPointInDangerZone(Vector3.MoveTowards(from, to, d) + eyeOffset))
                    return true;
            }

            remaining -= segLen;
            if (remaining <= 0f) break;
        }

        return false;
    }

    // 목적지 도착 시 은신 상태로 전환하고, 은신 중에는 이동권을 충전
    void UpdateHidingState()
    {
        if (state != DollState.Hiding && HasReachedDestination())
        {
            state = DollState.Hiding;
            tokenChargeTimer = 0f;
            stainlessJourney = false;    // 첫 이동 종료 — 다음부터는 정상적으로 얼룩을 남긴다
            ReleasePreviousWayPoint();   // 새 자리에 도착했으니 떠나온 자리를 반납
        }

        if (state != DollState.Hiding) return;

        tokenChargeTimer += Time.deltaTime;
        if (tokenChargeTimer >= tokenChargeInterval)
        {
            tokenChargeTimer -= tokenChargeInterval;
            moveTokens = Mathf.Min(moveTokens + 1, maxMoveTokens);
        }
    }

    // 경로 계산이 끝나고 정지 거리 안에서 실제로 멈춘 상태인지 확인
    bool HasReachedDestination()
    {
        return !agent.pathPending
            && agent.remainingDistance <= agent.stoppingDistance
            && (!agent.hasPath || agent.velocity.sqrMagnitude < 0.01f);
    }

    // 플레이어와의 거리에 따라 도망/순찰 이동을 결정
    void HandleMoveDecision()
    {
        bool playerNear = Vector3.Distance(transform.position, playerTransform.position) < escapeDistance;

        if (playerNear && !isEscaping)
        {
            // 플레이어 접근 시 1회만 판정: 기본은 제자리, nearMoveChance 확률로 가장 먼 곳으로 도망
            isEscaping = true;
            if (Random.value < nearMoveChance)
                TryMove(selector.FindFarthest());
        }
        else if (!playerNear)
        {
            isEscaping = false;
        }

        // 은신 중 + 플레이어가 멀리 있음 → 이동권이 있으면 다음 은신처로 이동
        if (state == DollState.Hiding && !playerNear && moveTokens > 0)
        {
            int next = selector.PickWeightedRandom();
            if (next < 0)
            {
                // 전부 점유라 갈 곳이 없을 때만 경고 — 모두 시야에 노출된 경우는 정상적인 대기
                if (selector.FindFarthest() < 0 && Time.time - lastNoSpotWarnTime > 5f)
                {
                    lastNoSpotWarnTime = Time.time;
                    Debug.LogWarning($"[{name}] 선택 가능한 웨이포인트가 없습니다 " +
                                     $"(전체 {wayPoints.Length}, 점유 {WaypointRegistry.Count})", this);
                }
            }
            else TryMove(next);
        }
    }

    // 예약해 둔 자리에서 충분히 멀어졌거나(짧은 이동이면 도착했거나) 플레이어 눈에 안 보일 때 얼룩을 남긴다
    void UpdatePendingStains()
    {
        for (int i = pendingStains.Count - 1; i >= 0; i--)
        {
            Vector3 spot = pendingStains[i];
            bool leftSpot = Vector3.Distance(transform.position, spot) >= stainLeaveDistance;
            if (!leftSpot && state != DollState.Hiding) continue;

            // 보고 있는 자리에 얼룩이 '생겨나는' 모습은 숨긴다 — 시선이 떠나면 남긴다
            if (vision.IsPointSeen(spot + Vector3.up * 0.1f)) continue;

            spawner.SpawnAt(spot);
            pendingStains.RemoveAt(i);
        }
    }

    // 이동권이 있을 때만 실제로 이동. 은신 중이었다면 그 자리를 얼룩 예약 (실제 생성은 UpdatePendingStains)
    void TryMove(int index)
    {
        if (moveTokens <= 0 || index < 0) return;

        // 이동 시작에 성공한 뒤에만 이동권·얼룩을 소모 — 실패 시 제자리 얼룩 반복 방지
        if (!SetTarget(index)) return;

        moveTokens--;
        // 첫 이동(인트로 직후)과 눈높이 위 은신처에서는 얼룩을 남기지 않는다
        if (state == DollState.Hiding && leftStartSpot && transform.position.y < stainMaxHeight)
            pendingStains.Add(transform.position);
        stainlessJourney = !leftStartSpot;
        leftStartSpot = true;
        state = DollState.Moving;
    }

    // 직전 목적지와 다른 Doll이 점유한 구역은 후보에서 제외 (구역이 하나뿐이면 허용)
    bool IsSelectable(int index)
    {
        if (wayPoints.Length <= 1) return true;
        if (index == currentTargetIndex || index == previousTargetIndex) return false;
        if (wayPoints[index] == null) return false;
        return !WaypointRegistry.IsOccupied(wayPoints[index]);
    }

    // 목적지 설정에 성공했는지 반환. 실패 시 현재 목적지·점유 상태를 건드리지 않는다
    bool SetTarget(int index)
    {
        if (index < 0) return false;

        Vector3 goal = wayPoints[index].position;

        // NavMesh 없는 웨이포인트는 목적지가 바닥으로 스냅돼 가구를 뚫고 들어간다 — 좁은 반경으로 걸러냄
        float tolerance = Mathf.Max(agent.radius * 2f, 0.3f);
        if (!NavMesh.SamplePosition(goal, out NavMeshHit hit, tolerance, NavMesh.AllAreas))
        {
            Debug.LogWarning($"[{name}] 웨이포인트 '{wayPoints[index].name}'에 NavMesh가 없어 이동 불가 " +
                             $"(위치 {goal}). 해당 면이 좁거나 베이크가 누락되었습니다.", wayPoints[index]);
            return false;
        }

        // SetDestination은 경로를 다음 프레임에 계산하므로, 동기로 계산해 검사 후 적용한다
        if (plannedPath == null) plannedPath = new NavMeshPath();
        if (!agent.CalculatePath(hit.position, plannedPath)) return false;

        // 출발 구간이 위험 지대를 지나면 출발 보류 — 에러가 아니라 '눈치보기'라 조용히 실패
        if (IsPathAheadInDanger(plannedPath.corners)) return false;

        if (!agent.SetPath(plannedPath)) return false;

        // 떠나온 자리는 도착할 때까지 예약 유지 — 즉시 반납하면 다른 Doll과 자리가 겹친다
        ReleasePreviousWayPoint();
        previousTargetIndex = currentTargetIndex;

        currentTargetIndex = index;
        WaypointRegistry.Occupy(wayPoints[index]);
        return true;
    }

    void ReleaseCurrentWayPoint()
    {
        if (currentTargetIndex >= 0 && currentTargetIndex < wayPoints.Length && wayPoints[currentTargetIndex] != null)
            WaypointRegistry.Release(wayPoints[currentTargetIndex]);
        currentTargetIndex = -1;
    }

    void ReleasePreviousWayPoint()
    {
        if (previousTargetIndex >= 0 && previousTargetIndex < wayPoints.Length && wayPoints[previousTargetIndex] != null)
            WaypointRegistry.Release(wayPoints[previousTargetIndex]);
        previousTargetIndex = -1;
    }

    // 플레이어에게 잡혔을 때 호출됨 (웨이포인트 점유 해제는 OnDestroy에서 처리)
    public void OnCaught()
    {
        SpawnCaughtVfx();
        Destroy(gameObject);
    }

    // 잡힌 자리에 이펙트 생성 — 리플레이(ReplayManager)도 같은 모습으로 재현하려고 따로 뺐다
    public void SpawnCaughtVfx()
    {
        if (caughtVfxPrefab == null) return;
        // 인형과 함께 파괴·비활성화되지 않도록 부모 없이 생성
        GameObject vfx = Instantiate(caughtVfxPrefab, transform.position, Quaternion.identity);
        if (caughtVfxLifetime > 0f)
            Destroy(vfx, caughtVfxLifetime);
    }

    // Doll이 사라질 때 점유를 풀어 다른 Doll이 그 구역을 쓸 수 있게 함
    void OnDestroy()
    {
        ReleaseCurrentWayPoint();
        ReleasePreviousWayPoint();
    }
}
