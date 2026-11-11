using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 스테이지 선택 카드를 좌우 드래그로 회전시키는 캐러셀 — 관성 후 가장 가까운 카드로 스냅
public class Carousel : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
{
    [Header("Orbit Settings")]
    [SerializeField] private float radiusX = 400f;            // 가로 반지름 (픽셀)
    [SerializeField] private Vector2 centerOffset = Vector2.zero;   // 회전 중심점 오프셋

    [Header("Visual Scale & Alpha")]
    [Range(0.1f, 1f)] [SerializeField] private float minScale = 0.6f;
    [Range(0.1f, 1f)] [SerializeField] private float maxScale = 1.0f;
    [Range(0.1f, 1f)] [SerializeField] private float minAlpha = 0.4f;
    [Range(0.1f, 1f)] [SerializeField] private float maxAlpha = 1.0f;

    [Header("Physics & Snapping")]
    [SerializeField] private float dragSensitivity = 0.005f; // 드래그 감도
    [SerializeField] private float damping = 0.92f;           // 관성 감쇄율
    [SerializeField] private float snapSpeed = 10f;          // 스냅 이동 속도

    [Header("Sound")]
    [SerializeField] private AudioClip turnClip;             // 정면 카드가 바뀔 때 재생 — 같은 오브젝트의 AudioSource로 재생

    private List<RectTransform> stageCards = new List<RectTransform>();
    private List<CanvasGroup> cardCanvasGroups = new List<CanvasGroup>();

    private float currentAngle = 0f;    // 현재 회전 각도 (Radian)
    private float velocity = 0f;        // 회전 속도 (관성용)
    private bool isDragging = false;
    private float angleStep;            // 카드 간 각도 간격
    private int lastFrontStep;          // 직전 프레임의 정면 카드 스텝 (효과음 판정용)
    private AudioSource audioSource;

    private void Start()
    {
        audioSource = GetComponent<AudioSource>();
        InitializeCards();
    }

    private void InitializeCards()
    {
        stageCards.Clear();
        cardCanvasGroups.Clear();

        int childCount = transform.childCount;
        if (childCount == 0) return;

        angleStep = (Mathf.PI * 2f) / childCount;

        for (int i = 0; i < childCount; i++)
        {
            RectTransform card = transform.GetChild(i) as RectTransform;
            if (card == null) continue;

            stageCards.Add(card);

            // 투명도 조절용 — 없으면 자동 부착
            if (!card.TryGetComponent<CanvasGroup>(out var canvasGroup))
            {
                canvasGroup = card.gameObject.AddComponent<CanvasGroup>();
            }
            cardCanvasGroups.Add(canvasGroup);
        }

        lastFrontStep = Mathf.RoundToInt(currentAngle / angleStep);
        UpdateCardPositions();
    }

    private void Update()
    {
        if (stageCards.Count == 0) return;

        if (isDragging)
        {
            UpdateCardPositions();
        }
        else
        {
            // 손을 뗀 뒤에는 관성으로 굴리다가, 충분히 느려지면 스냅
            if (Mathf.Abs(velocity) > 0.001f)
            {
                currentAngle += velocity;
                velocity *= damping;
                UpdateCardPositions();
            }
            else
            {
                velocity = 0f;
                SnapToNearest();
            }
        }
    }

    // 카드의 위치·크기·투명도·렌더 순서를 각도로부터 다시 계산
    private void UpdateCardPositions()
    {
        int cardCount = stageCards.Count;

        for (int i = 0; i < cardCount; i++)
        {
            float angle = currentAngle + (i * angleStep);

            float x = centerOffset.x + Mathf.Sin(angle) * radiusX;
            float y = centerOffset.y;

            stageCards[i].anchoredPosition = new Vector2(x, y);

            // cos을 0~1로 정규화한 '깊이감' — 정면일수록 1에 가깝다
            float normalizedDepth = (Mathf.Cos(angle) + 1f) * 0.5f;

            float scale = Mathf.Lerp(minScale, maxScale, normalizedDepth);
            stageCards[i].localScale = new Vector3(scale, scale, 1f);

            float alpha = Mathf.Lerp(minAlpha, maxAlpha, normalizedDepth);
            cardCanvasGroups[i].alpha = alpha;
            // 정면 칸 안에 있는 카드만 클릭 가능
            cardCanvasGroups[i].interactable = Mathf.Cos(angle) >= Mathf.Cos(angleStep * 0.5f);
        }

        SortSiblingIndex();
        PlayTurnSoundIfChanged();
    }

    // 정면 카드가 다음 칸으로 넘어갈 때마다 한 번씩 재생
    private void PlayTurnSoundIfChanged()
    {
        int frontStep = Mathf.RoundToInt(currentAngle / angleStep);
        if (frontStep == lastFrontStep) return;

        lastFrontStep = frontStep;
        if (audioSource != null && turnClip != null)
        {
            audioSource.PlayOneShot(turnClip);
        }
    }


    private void SortSiblingIndex()
    {
        List<RectTransform> sortedList = new List<RectTransform>(stageCards);
        sortedList.Sort((a, b) => a.localScale.x.CompareTo(b.localScale.x));

        // 작은(뒤에 있는) 카드부터 그려지도록 SiblingIndex 부여
        for (int i = 0; i < sortedList.Count; i++)
        {
            sortedList[i].SetSiblingIndex(i);
        }
    }

    // 정면에 가장 가까운 카드 각도로 부드럽게 보정
    private void SnapToNearest()
    {
        float targetAngle = Mathf.Round(currentAngle / angleStep) * angleStep;
        currentAngle = Mathf.Lerp(currentAngle, targetAngle, Time.deltaTime * snapSpeed);
        UpdateCardPositions();
    }

    #region EventSystem Drag Handlers

    public void OnBeginDrag(PointerEventData eventData)
    {
        isDragging = true;
        velocity = 0f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        // 좌우 드래그량을 각도로 축적. 마지막 delta가 손을 뗐을 때의 관성이 된다
        float delta = eventData.delta.x * dragSensitivity;
        currentAngle += delta;
        velocity = delta;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false;
    }

    #endregion
}
