using System;
using UnityEngine;

// 얼룩 신선도의 "규칙 값" 묶음 — 몇 초에 어떤 표정으로 바뀌고, 몇 초에 걸쳐 갈색이 되는지.
// 값은 플레이테스트로 계속 바뀌므로 코드가 아니라 에셋에 둔다. 맵마다 다른 에셋을 끼우면 맵별 프리셋이 된다
[CreateAssetMenu(fileName = "StainFreshness", menuName = "Game/Stain Freshness Settings")]
public class StainFreshnessSettings : ScriptableObject
{
    // 표정 한 단계 — "이 나이부터 이 스프라이트"
    [Serializable]
    public struct Stage
    {
        [Tooltip("이 나이(초)가 되면 이 표정으로 바뀐다. 첫 단계는 0")]
        [Min(0f)] public float startAge;
        public Sprite sprite;
    }

    [Header("Face (표정)")]
    [Tooltip("나이가 어린 순서로 적는다. 단계 수는 자유 — 중간 단계를 빼고 싶으면 줄을 지우면 된다")]
    [SerializeField] private Stage[] stages = Array.Empty<Stage>();

    [Header("Color (색)")]
    [Tooltip("갓 생긴 얼룩의 색 곱하기. 흰색 = 스프라이트 원래 색")]
    [SerializeField] private Color freshTint = Color.white;
    [Tooltip("다 낡은 얼룩의 색 곱하기. 노란 스프라이트에 곱해져 갈색이 된다 (검은 외곽선은 검은색 그대로)")]
    [SerializeField] private Color oldTint = new Color(0.62f, 0.42f, 0.26f, 1f);
    [Tooltip("이 시간(초)에 걸쳐 freshTint → oldTint로 서서히 바뀐다")]
    [SerializeField, Min(0.01f)] private float colorDuration = 20f;

    public int StageCount => stages.Length;

    // 나이에 맞는 단계 번호. 뒤에서부터 찾으면 "startAge <= age인 마지막 단계"가 바로 나온다
    public int GetStageIndex(float age)
    {
        for (int i = stages.Length - 1; i >= 0; i--)
        {
            if (age >= stages[i].startAge) return i;
        }
        return stages.Length > 0 ? 0 : -1;   // 음수 나이 같은 예외도 첫 단계로
    }

    public Sprite GetSprite(int stageIndex)
    {
        if (stageIndex < 0 || stageIndex >= stages.Length) return null;
        return stages[stageIndex].sprite;
    }

    // 나이 0 → freshTint, colorDuration 이후 → oldTint. 그 사이는 직선 보간
    public Color GetTint(float age)
    {
        float t = Mathf.Clamp01(age / colorDuration);
        return Color.Lerp(freshTint, oldTint, t);
    }

    // 인스펙터에서 값을 바꿀 때마다 호출 — 순서가 꼬이면 GetStageIndex가 틀린 답을 내므로 바로 알려준다
    void OnValidate()
    {
        for (int i = 1; i < stages.Length; i++)
        {
            if (stages[i].startAge < stages[i - 1].startAge)
            {
                Debug.LogWarning($"[{name}] 표정 단계는 startAge가 작은 순서로 적어야 합니다 " +
                                 $"({i - 1}번 {stages[i - 1].startAge}초 > {i}번 {stages[i].startAge}초).", this);
                return;
            }
        }
    }
}
