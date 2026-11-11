using UnityEngine;

// 얼룩 1개에 붙는 "나이표". 언제 생겼는지만 기억하고, 모습은 매니저가 알려주는 시각으로 계산해 바꾼다.
// 스스로 Update를 돌지 않는다 — 시계와 갱신은 StainFreshnessManager 한 곳이 맡는다
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class StainFreshness : MonoBehaviour
{
    [Tooltip("생길 때 이미 이만큼 나이를 먹은 상태로 시작한다 (초). 미리 배치한 얼룩에 손으로 넣는 값")]
    [SerializeField, Min(0f)] private float startAge = 0f;
    [Tooltip("켜면 나이가 startAge에서 멈춘다. 튜토리얼 문 앞처럼 기다려도 모습이 변하면 안 될 때")]
    [SerializeField] private bool freezeAge = false;

    private SpriteRenderer spriteRenderer;
    private float birthTime;         // 나이 0이었던 시각 (매니저 시계 기준). startAge만큼 과거로 당겨 둔다
    private bool hasBirthTime;       // 출생 시각이 정해졌나 — 한 번 정해지면 다시 켜져도 유지
    private int shownStage = -1;     // 지금 보여 주는 표정 단계 — 바뀔 때만 스프라이트를 교체하려고 기억

    public bool HasBirthTime => hasBirthTime;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    // 켜지고 꺼질 때 명단에 넣고 뺀다 — Destroy(청소)·SetActive(false)(리플레이 청소) 둘 다 OnDisable을 부른다
    void OnEnable()  { StainFreshnessManager.Register(this); }
    void OnDisable() { StainFreshnessManager.Unregister(this); }

    // 모든 Awake가 끝난 뒤라 이때도 매니저가 없으면 정말 씬에 없는 것
    void Start()
    {
        if (StainFreshnessManager.Instance == null)
            Debug.LogWarning($"[{name}] 씬에 StainFreshnessManager가 없어 얼룩이 나이를 먹지 않습니다. " +
                             "메뉴 Tools > 얼룩 신선도 > 현재 씬에 매니저 설치 로 추가하세요.", this);
    }

    /// <summary>
    /// 이 얼룩이 만들어진 시각을 정한다. 게임에서는 매니저가 등록 순간의 시각으로,
    /// 리플레이에서는 ReplayManager가 녹화된 생성 시각으로 부른다.
    /// </summary>
    public void MarkCreated(float createdTime)
    {
        birthTime = createdTime - startAge;
        hasBirthTime = true;
    }

    public float GetAge(float now) => freezeAge ? startAge : now - birthTime;

    // 매니저가 매 프레임 호출 — 나이를 계산해 표정과 색을 맞춘다
    public void Apply(StainFreshnessSettings settings, float now)
    {
        if (!hasBirthTime) return;
        float age = GetAge(now);

        // 스프라이트 교체는 단계가 바뀌는 순간에만 (즉시 교체, 연출 없음 — 2026-10-15 결정)
        int stage = settings.GetStageIndex(age);
        if (stage != shownStage)
        {
            Sprite sprite = settings.GetSprite(stage);
            if (sprite != null) spriteRenderer.sprite = sprite;
            shownStage = stage;
        }

        spriteRenderer.color = settings.GetTint(age);
    }
}
