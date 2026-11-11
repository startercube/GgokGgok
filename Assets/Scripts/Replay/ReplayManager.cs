using UnityEngine;
using System.Collections.Generic;
using System;

// CCTV 리플레이 재생기 — isReplayMode로 씬이 재로드되면 게임 로직을 끄고 스냅샷·이벤트로 한 판을 재현
public class ReplayManager : MonoBehaviour
{
    private Dictionary<int, GameObject> stains = new Dictionary<int, GameObject>();   // 얼룩 ID → 오브젝트 (Recorder와 반대 방향)
    private List<DollMove> dolls = new List<DollMove>();   // 인덱스 = 인형 ID (Recorder와 같은 이름 정렬 순서)
    private Transform playerTransform;
    private ReplayData replayData;
    private float totalDuration;
    private bool isReplayPlaying = false;
    private int dollCount = 0;
    private float replayTime = 0f;          // 재생 경과 시간 (녹화 때의 recordTime과 같은 축)
    private int nextEventIndex = 0;         // 아직 발화하지 않은 첫 이벤트 위치
    public static float playbackSpeed = 1f;       // 재생 배속 (1x/2x/4x)
    [Tooltip("StainSpawn 이벤트 재현에 사용할 얼룩 프리팹")]
    [SerializeField] private GameObject stainPrefab;
    [SerializeField] private DoorController door;

    public event Action OnReplayFinished;  // 재생 종료 시 호출되는 이벤트


    // ===== 부트스트랩: 게임 로직 정지 =====

    // 다른 스크립트의 Start()가 돌기 전에 꺼야 하므로 Awake에서 처리
    void Awake()
    {
        // 일반 게임 모드면 자신을 끄고 아무것도 하지 않는다
        if (GameManager.Instance == null || !GameManager.Instance.isReplayMode)
        {
            enabled = false;
            return;
        }


        foreach (UnityEngine.AI.NavMeshAgent agent in FindObjectsByType<UnityEngine.AI.NavMeshAgent>(FindObjectsSortMode.None))
        {
            agent.enabled = false;
        }

        // 리플레이 때 꺼져야 할 게임 로직은 스스로 IReplayPausable을 구현한다
        foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (mb is IReplayPausable)
                mb.enabled = false;
        }

        // 물리(중력/충돌)가 스냅샷 위치 적용에 간섭하지 않도록 플레이어를 kinematic으로
        playerTransform = GameObject.FindWithTag("Player").transform;
        var playerRb = playerTransform.GetComponent<Rigidbody>();
        playerRb.isKinematic = true;
        playerRb.interpolation = RigidbodyInterpolation.None;   // 보간이 켜져 있으면 스냅샷으로 옮긴 위치를 덮어쓴다
    }

    // ===== 재생 준비: ID 매칭 =====

    // ⚠️ ReplayRecorder.Start와 같은 이름 정렬 규칙이어야 ID가 일치한다
    void Start()
    {
        DollMove[] dollMoves = FindObjectsByType<DollMove>(FindObjectsSortMode.None);
        if (dollMoves.Length == 0)
        {
            Debug.LogWarning("No DollMove components found in the scene.");
            return;
        }
        ReplayIdentity.SortById(dollMoves);

        dolls.AddRange(dollMoves);
        dollCount = dolls.Count;

        // 씬 재로드 직후는 게임 시작 상태와 같으므로, 초기 얼룩을 스캔해 ID 0부터 등록
        GameObject[] stainObjects = GameObject.FindGameObjectsWithTag("Stain");
        if (stainObjects.Length == 0)
        {
            Debug.LogWarning("No Stain objects found in the scene.");
        }
        ReplayIdentity.SortById(stainObjects);

        for (int i = 0; i < stainObjects.Length; i++)
        {
            stains[i] = stainObjects[i];
        }

        door = FindFirstObjectByType<DoorController>();

        if (GameManager.Instance.replayData != null)
        {
            replayData = GameManager.Instance.replayData;
            totalDuration = replayData.totalDuration;
            playbackSpeed = 1f;   // static이라 지난 리플레이의 배속이 남아 있다
            isReplayPlaying = true;
        }
        else
        {
            Debug.LogWarning("Replay data not found in GameManager.");
        }
    }

    // ===== 재생 루프 =====

    void Update()
    {
        if (!isReplayPlaying) return;

        replayTime += Time.deltaTime * playbackSpeed;
        bool finished = false;
        if (replayTime > totalDuration)
        {
            replayTime = totalDuration;
            isReplayPlaying = false;
            finished = true;
        }

        // 얼룩 신선도 시계를 재생 시각에 맞춘다 — 이벤트로 얼룩이 생기기 전에 해 둬야 출생 시각이 맞다
        if (StainFreshnessManager.Instance != null)
            StainFreshnessManager.Instance.SetReplayTime(replayTime);

        ApplySnapshots();
        FireEvents();

        if (finished)
        {
            OnReplayFinished?.Invoke();
        }
    }

    void ApplySnapshots()
    {
        if (replayData == null || replayData.snapshots.Count < 2) return;

        int i = Mathf.FloorToInt(replayTime / replayData.sampleInterval); // 녹화 때의 간격을 데이터에서 읽어 인덱스 계산
        i = Mathf.Clamp(i, 0, replayData.snapshots.Count - 2); // 마지막 스냅샷은 다음이 없으므로 제외
        ReplaySnapshot snapA = replayData.snapshots[i];
        ReplaySnapshot snapB = replayData.snapshots[i + 1];

        float t = Mathf.InverseLerp(snapA.time, snapB.time, replayTime);

        Vector3 playerPos = Vector3.Lerp(snapA.playerPos, snapB.playerPos, t);
        float playerYaw = Mathf.LerpAngle(snapA.playerYaw, snapB.playerYaw, t);
        playerTransform.position = playerPos;
        playerTransform.rotation = Quaternion.Euler(0f, playerYaw, 0f);

        for (int k = 0; k < dollCount; k++)
        {
            // 이미 잡힌 인형
            if (!snapA.dollAlive[k] && !snapB.dollAlive[k]) continue;

            // 잡히는 순간 — 앞 스냅샷 위치에 고정 (사라지는 것은 DollCaught 이벤트가 처리)
            if (snapA.dollAlive[k] && !snapB.dollAlive[k])
            {
                if (dolls[k] != null)
                {
                    dolls[k].transform.position = snapA.dollPositions[k];
                    dolls[k].transform.rotation = Quaternion.Euler(0f, snapA.dollYaws[k], 0f);
                }
                continue;
            }

            Vector3 dollPos = Vector3.Lerp(snapA.dollPositions[k], snapB.dollPositions[k], t);
            float dollYaw = Mathf.LerpAngle(snapA.dollYaws[k], snapB.dollYaws[k], t);

            if (dolls[k] != null)
            {
                dolls[k].transform.position = dollPos;
                dolls[k].transform.rotation = Quaternion.Euler(0f, dollYaw, 0f);
            }
        }
    }

    void FireEvents()
    {
        while (nextEventIndex < replayData.events.Count && replayData.events[nextEventIndex].time <= replayTime)
        {
            HandleEvent(replayData.events[nextEventIndex]);
            nextEventIndex++;
        }
    }

    void HandleEvent(ReplayEvent replayEvent)
    {
        switch (replayEvent.type)
        {
            case ReplayEventType.StainSpawn:
                // 녹화된 좌표는 이미 바닥에 맞춰진 값 — 높이는 건드리지 않고 생성만 위임한다
                GameObject newStain = DollStainSpawner.Create(stainPrefab, replayEvent.pos);
                stains[replayEvent.id] = newStain;
                // 이벤트는 배속 때문에 늦은 프레임에 발화할 수 있다 — 발화 시각이 아니라 녹화된 생성 시각을 출생 시각으로
                if (newStain.TryGetComponent(out StainFreshness freshness))
                    freshness.MarkCreated(replayEvent.time);
                break;

            case ReplayEventType.StainClean:
                if (stains.TryGetValue(replayEvent.id, out GameObject stainToClean))
                {
                    stainToClean.SetActive(false);   // Destroy 대신 — 재생 중 참조를 유지해야 한다
                    stains.Remove(replayEvent.id);
                }
                break;

            case ReplayEventType.DollCaught:
                if (replayEvent.id >= 0 && replayEvent.id < dolls.Count)
                {
                    DollMove dollToCatch = dolls[replayEvent.id];
                    if (dollToCatch != null)
                    {
                        dollToCatch.SpawnCaughtVfx();
                        dollToCatch.gameObject.SetActive(false);
                        dolls[replayEvent.id] = null;
                    }

                }
                break;

            case ReplayEventType.DoorOpen:
                if (door != null)
                {
                    door.Open();
                }
                break;
        }
    }
}
