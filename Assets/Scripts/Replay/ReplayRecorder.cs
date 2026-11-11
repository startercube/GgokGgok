using UnityEngine;
using System.Collections.Generic;

// CCTV 리플레이 녹화기 — 위치는 주기적 스냅샷으로, 사건은 Record○○ 이벤트로 남긴다
public class ReplayRecorder : MonoBehaviour, IReplayPausable
{
    public static ReplayRecorder Instance { get; private set;}

    ReplayData replayData;
    float recordTime = 0f;          // 녹화 경과 시간 — 스냅샷/이벤트가 공유하는 시계
    float sampleInterval = 0.1f;    // 스냅샷 촬영 간격 (초)
    float nextSampleTime = 0f;      // 다음 스냅샷을 찍을 시각

    public Transform playerTransform;
    private List<DollMove> dolls = new List<DollMove>();   // 인덱스 = 인형 ID (이름 정렬 순서)
    public int dollCount = 0;
    private Dictionary<GameObject, int> stains = new Dictionary<GameObject, int>();   // 얼룩 오브젝트 → ID 역참조
    int nextStainId = 0;            // 새 얼룩에 발급할 다음 ID (초기 얼룩 수부터 시작)

    bool isRecording;               // Start에서 준비가 끝나야 true — 준비 실패 시 샘플링 방지
    bool held;                      // 게임이 잠긴 동안(인트로) 시계를 멈춤 — CameraManagement.SetGameplayFrozen이 알려 줌 (2026-10-24)

    /// <summary>녹화 시계가 멈춰 있는지 (인트로 등 게임 잠금 중)</summary>
    public bool IsHeld => held;

    /// <summary>
    /// 게임 잠금과 함께 녹화 시계를 멈추거나 다시 흘린다. 끄는 게 아니라 시계만 멈춤 →
    /// 스냅샷·이벤트가 같은 시계를 쓰므로 둘 다 자동으로 맞고, 리플레이 0초 = 조작이 풀린 순간이 된다.
    /// 설계: 「코드 설계 - 플 모델·리플레이 녹화 시작」 1장
    /// </summary>
    public void SetHeld(bool hold) => held = hold;

    // ===== 초기화 =====

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        replayData = new ReplayData();
        replayData.sampleInterval = sampleInterval;   // 재생 쪽이 같은 간격으로 인덱스를 계산하도록 함께 저장
    }

    // static 이벤트라 반드시 쌍으로 해제 — 안 하면 씬 재로드 후 파괴된 구독자가 남는다
    void OnEnable()  { DollStainSpawner.OnStainSpawned += RecordStainSpawn; }
    void OnDisable() { DollStainSpawner.OnStainSpawned -= RecordStainSpawn; }

    // 녹화 대상 수집 + ID 확정. ⚠️ 이름 정렬이 곧 ID — 바꾸려면 ReplayManager도 함께
    void Start()
    {
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null)
        {
            Debug.LogWarning("Player object not found. Please ensure the player has the 'Player' tag.");
            return;
        }
        playerTransform = playerObj.transform;

        DollMove[] dollMoves = FindObjectsByType<DollMove>(FindObjectsSortMode.None);
        if (dollMoves.Length == 0)
        {
            Debug.LogWarning("No DollMove components found in the scene.");
            return;
        }
        ReplayIdentity.SortById(dollMoves);

        dolls.AddRange(dollMoves);
        dollCount = dolls.Count;
        replayData.dollDisguises = DollDisguise.Assignments;   // 변장은 DollMove.Awake에서 이미 끝났다

        GameObject[] stainObjects = GameObject.FindGameObjectsWithTag("Stain");
        if (stainObjects.Length == 0)
        {
            Debug.LogWarning("No Stain objects found in the scene.");
        }
        ReplayIdentity.SortById(stainObjects);

        for (int i = 0; i < stainObjects.Length; i++)
        {
            stains[stainObjects[i]] = i;
        }
        nextStainId = stainObjects.Length;
        isRecording = true;
    }

    // ===== 스냅샷 녹화 =====

    void Update()
    {
        if (!isRecording || held) return;   // 인트로 동안은 찍지 않음
        recordTime += Time.deltaTime;

        // 프레임이 길어져 샘플 시각을 여러 번 지나쳤어도 밀리지 않도록 while 사용
        while (recordTime >= nextSampleTime)
        {
            RecordSnapshot();
            nextSampleTime += sampleInterval;
        }
    }

    // 현재 순간의 플레이어/인형 위치를 스냅샷 1장으로 기록
    void RecordSnapshot()
    {
        ReplaySnapshot snapshot = new ReplaySnapshot();
        snapshot.time = recordTime;
        snapshot.playerPos = playerTransform.position;
        snapshot.playerYaw = playerTransform.eulerAngles.y;

        snapshot.dollPositions = new Vector3[dollCount];
        snapshot.dollYaws = new float[dollCount];
        snapshot.dollAlive = new bool[dollCount];

        for (int i = 0; i < dollCount; i++)
        {
            // 잡혀서 파괴된 인형은 유니티가 null로 판정 — alive는 false로 남기고 건너뜀
            if (dolls[i] == null) continue;
            DollMove doll = dolls[i];
            snapshot.dollPositions[i] = doll.transform.position;
            snapshot.dollYaws[i] = doll.transform.eulerAngles.y;
            snapshot.dollAlive[i] = true;
        }

        replayData.snapshots.Add(snapshot);
    }

    // ===== 이벤트 녹화 (외부에서 호출) =====

    // DollStainSpawner.OnStainSpawned 구독 — 새 얼룩에 ID를 발급하고 생성 이벤트 기록
    public void RecordStainSpawn(GameObject stain)
    {
        stains[stain] = nextStainId;   // 이 얼룩이 나중에 청소될 때 ID를 찾을 수 있도록 등록
        ReplayEvent spawnEvent = new ReplayEvent();
        spawnEvent.time = recordTime;
        spawnEvent.id = nextStainId++;
        spawnEvent.pos = stain.transform.position;
        spawnEvent.type = ReplayEventType.StainSpawn;
        replayData.events.Add(spawnEvent);
    }

    // PlayerInteraction이 얼룩 청소 시 호출 (Destroy 전에 불러야 함)
    public void RecordStainClean(GameObject stain)
    {
        if (stains.TryGetValue(stain, out int stainId))
        {
            ReplayEvent cleanEvent = new ReplayEvent();
            cleanEvent.time = recordTime;
            cleanEvent.id = stainId;
            cleanEvent.type = ReplayEventType.StainClean;
            replayData.events.Add(cleanEvent);
        }
        else
        {
            Debug.LogWarning("Attempted to record cleaning of a stain that was not registered: " + stain.name);
        }
    }

    // PlayerInteraction이 인형 포획 시 호출 (OnCaught로 파괴되기 전에 불러야 함)
    public void RecordDollCaught(DollMove doll)
    {
        ReplayEvent caughtEvent = new ReplayEvent();
        caughtEvent.time = recordTime;
        caughtEvent.id = dolls.IndexOf(doll);
        caughtEvent.type = ReplayEventType.DollCaught;
        replayData.events.Add(caughtEvent);
    }

    public void RecordDoorOpen()
    {
        ReplayEvent doorEvent = new ReplayEvent();
        doorEvent.time = recordTime;
        doorEvent.type = ReplayEventType.DoorOpen;
        replayData.events.Add(doorEvent);
    }

    // ===== 녹화 종료 =====

    // GameProgressManager.EndGame이 호출 — 결과를 GameManager에 저장하고 녹화 정지
    public void FinishRecording()
    {
        replayData.totalDuration = recordTime;
        replayData.dollCount = dollCount;
        if (GameManager.Instance != null)
        {
            GameManager.Instance.replayData = replayData;
            GameManager.Instance.isReplayMode = false;
        }
        isRecording = false;

        Debug.Log($"[ReplayRecorder] 녹화 종료 — 길이 {replayData.totalDuration:F1}초, " +
                  $"스냅샷 {replayData.snapshots.Count}개, 이벤트 {replayData.events.Count}개 " +
                  $"(인형 {dollCount}마리, 얼룩 ID {nextStainId}번까지 발급)");
    }
}
