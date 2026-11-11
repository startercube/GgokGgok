using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

// 튜토리얼 연습 판 실패 → 자동 재시작의 체크포인트 (2026-10-21)
// 재시작 = 씬을 다시 불러온다 (잡힌 빈대·런타임 얼룩·진행도·웨이포인트 예약이 전부 처음 상태가 보장됨). 대신 이미 지나온 부분을 여기서 건너뛴다:
// - Awake: 앞 장면 빈대·문 앞 미리 배치 얼룩을 끔 → GameProgressManager가 Start에서 셀 때 빠짐 (꺼진 오브젝트는 안 셈)
// - Start: 플레이어를 연습 구역 입구 문 앞에, 한 주임을 ToPractice 구간 끝에, 지나온 문을 열고, 개수 표시를 켜고, 검은 화면에서 페이드 인
// "재시작 판인가"는 재시작 직전에 켠 정적 값을 이 컴포넌트의 Awake(가장 먼저 실행)가 읽어 옮기고 끈다 → 다른 길로 들어오면 항상 처음부터
// 설계 기록: 프로젝트 문서 「코드 설계 - 튜토리얼 시스템」 11장
[DefaultExecutionOrder(-1000)]
public class TutorialRetry : MonoBehaviour
{
    private static bool pendingRetry;
    private static readonly HashSet<string> firedOnce = new HashSet<string>();

    /// <summary>지금 판이 연습 판 재시작인지 (이 씬이 불린 동안 유지)</summary>
    public static bool IsRetry { get; private set; }
    /// <summary>재시작 전 판에서 이미 나온 한 번짜리 대사 id (첫 오답 등) — 재시작 뒤 다시 나오지 않게</summary>
    public static IReadOnlyCollection<string> FiredOnce => firedOnce;

    /// <summary>다음 씬 로드를 재시작 판으로 표시한다 (씬을 부르기 직전에)</summary>
    public static void Request(IEnumerable<string> firedEventIds)
    {
        pendingRetry = true;
        firedOnce.Clear();
        if (firedEventIds != null) foreach (var id in firedEventIds) firedOnce.Add(id);
    }

    [Tooltip("재시작 판에서 끌 것: 앞 장면 빈대, 문 앞 미리 배치 얼룩")]
    [SerializeField] private GameObject[] disableOnRetry = new GameObject[0];
    [Tooltip("재시작 판에서 열어 둘 문 (얼룩 문 등). 한 주임 길의 문은 SkipTo가 연다")]
    [SerializeField] private DoorController[] openOnRetry = new DoorController[0];
    [SerializeField] private Vector3 playerPosition;
    [SerializeField] private float playerYaw;
    [SerializeField] private CinemachineCamera playerCam;
    [SerializeField] private TutorialGuide guide;
    [Tooltip("한 주임이 여기까지 걸은 것으로 하고 그 끝에 선다")]
    [SerializeField] private string guideLeg = "ToPractice";
    [SerializeField] private TutorialPracticeFlow practice;
    [SerializeField] private ScreenFader fader;
    [SerializeField] private float fadeInSeconds = 1f;

    void Awake()
    {
        IsRetry = pendingRetry;
        pendingRetry = false;
        if (!IsRetry) { firedOnce.Clear(); return; }
        foreach (var go in disableOnRetry) if (go != null) go.SetActive(false);
    }

    IEnumerator Start()
    {
        if (!IsRetry) yield break;
        if (fader != null) StartCoroutine(fader.FadeTo(1f, 0f));   // 첫 프레임부터 검게 (시간 0이라 바로 끝남)

        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            var rot = Quaternion.Euler(0f, playerYaw, 0f);
            p.transform.SetPositionAndRotation(playerPosition, rot);
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null) { rb.position = playerPosition; rb.rotation = rot; rb.linearVelocity = Vector3.zero; }
        }
        var panTilt = playerCam != null ? playerCam.GetComponent<CinemachinePanTilt>() : null;
        if (panTilt != null)
        {
            panTilt.PanAxis.Value = Mathf.Repeat(playerYaw + 180f, 360f) - 180f;
            panTilt.TiltAxis.Value = 0f;
        }
        if (guide != null) guide.SkipTo(guideLeg);
        foreach (var d in openOnRetry) if (d != null) d.Open();
        if (practice != null) practice.RevealHud();

        yield return null;
        if (playerCam != null) playerCam.PreviousStateIsValid = false;   // 옮긴 자리로 카메라가 따라 미끄러지지 않게
        if (fader != null) yield return fader.FadeIn(fadeInSeconds);
    }
}
