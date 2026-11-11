using UnityEngine;

// 빈대 시야의 "규칙 값" 묶음 — 모든 맵·모든 빈대가 같은 기준을 쓰도록 한 곳에 둔다
// (2026-11-01 「코드 설계 - 빈대 시야 정지(즉시 정지·화면 기준 판정)」). 기준값은 인형방(DollManiaHouse).
// 기본 에셋은 Resources/DollVisionSettings — 빈대에 아무것도 연결하지 않아도 이것을 쓴다.
// 특정 빈대만 다르게 하려면 DollVision의 settingsOverride에 다른 에셋을 끼운다
[CreateAssetMenu(fileName = "DollVisionSettings", menuName = "Game/Doll Vision Settings")]
public class DollVisionSettings : ScriptableObject
{
    const string ResourcePath = "DollVisionSettings";

    [Header("Sight (실제 시야)")]
    [Tooltip("이보다 멀면 화면 안이어도 보이지 않는 것으로 처리")]
    [Min(0f)] public float sightDistance = 35f;

    [Header("Stop Margin (위험 지대)")]
    [Tooltip("실제 화면 가장자리 바깥으로 이 각도까지를 위험 지대로 보고 미리 멈춘다. 중심점만 검사하므로 빈대 몸집만큼의 여유 역할")]
    [Range(0f, 60f)] public float stopMarginAngle = 30f;   // 실제 화면 + 30° = 16:9 좌우 70°, 예전 인형방(시야각 100 + 여유 20)과 같은 멈춤 범위
    [Tooltip("sightDistance에 이 거리를 더한 곳까지 위험 지대로 취급")]
    [Min(0f)] public float stopMarginDistance = 2f;

    static DollVisionSettings cachedDefault;

    /// <summary>모든 빈대의 기본 설정. Resources에서 한 번만 불러오고, 에셋이 없으면 코드 기본값(인형방 값)으로 만든다</summary>
    public static DollVisionSettings Default
    {
        get
        {
            if (cachedDefault != null) return cachedDefault;

            cachedDefault = Resources.Load<DollVisionSettings>(ResourcePath);
            if (cachedDefault == null)
            {
                Debug.LogWarning($"Resources/{ResourcePath} 에셋이 없어 코드 기본값으로 동작합니다. (Create > Game > Doll Vision Settings로 만들어 Resources 폴더에 두세요)");
                cachedDefault = CreateInstance<DollVisionSettings>();
                cachedDefault.name = ResourcePath + " (코드 기본값)";
            }
            return cachedDefault;
        }
    }
}
