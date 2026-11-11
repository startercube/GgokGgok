using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 플레이어 몸(플 모델)을 모드에 따라 보여 준다 (2026-10-24).
/// - 플레이 중: 카메라(눈 1.35m)가 플 머리(1.45m) 안이라 그대로 그리면 방독면·모자 챙이 화면에 걸림 → 그림자만
/// - 리플레이 중: CCTV에서 방역복 입은 플이 보이도록 보통으로 그림 (확정 설정: 플은 리플레이에서만 보임)
/// 같은 Main Camera가 플레이·리플레이를 다 그리므로 레이어 대신 모드별 그림자 설정으로 나눈다.
/// IReplayPausable이 아님 — 리플레이에서 일해야 하므로.
/// 설계: 「코드 설계 - 플 모델·리플레이 녹화 시작」 2.3
/// </summary>
public class PlayerBodyView : MonoBehaviour
{
    public enum FirstPersonMode
    {
        ShadowsOnly,   // 화면엔 안 보이고 바닥 그림자만 (기본)
        Hidden,        // 그림자도 없음
        Visible        // 보통으로 그림 (카메라가 머리 안이라 권장하지 않음)
    }

    [Tooltip("몸 모델 루트. 비우면 이 오브젝트 아래 'PL_Visual'")]
    [SerializeField] private Transform body;

    [Tooltip("플레이 중(1인칭) 몸을 어떻게 그릴지. 리플레이 중엔 항상 보통으로 그림")]
    [SerializeField] private FirstPersonMode firstPerson = FirstPersonMode.ShadowsOnly;

    [Tooltip("리플레이에서 걷기 모션 배율. 리플레이 카메라가 약 25m 떨어져 있어 기본 크기(한 주임 값)로는 걸음이 안 보임 (설계 5.9)")]
    [SerializeField, Min(1f)] private float replayMotionScale = 4f;

    void Awake()
    {
        if (body == null) body = transform.Find("PL_Visual");
        if (body == null) return;

        // 모드는 씬을 다시 불러야 바뀌므로 시작할 때 한 번만 정한다
        bool replay = GameManager.Instance != null && GameManager.Instance.isReplayMode;
        Apply(replay ? FirstPersonMode.Visible : firstPerson);

        // 리플레이: 루트는 녹화된 카메라 방향이라 옆·뒷걸음이 미끄러져 보임 → 몸만 가는 쪽을 보게 (한 주임처럼, 설계 5장)
        if (replay && TryGetComponent(out PegDollAnimator anim))
        {
            anim.FaceMovement = true;
            anim.MotionScale = replayMotionScale;   // 멀리 있는 CCTV에서도 통통 걷는 게 보이게 (설계 5.9)
        }
    }

    void Apply(FirstPersonMode mode)
    {
        foreach (var r in body.GetComponentsInChildren<Renderer>(true))
        {
            switch (mode)
            {
                case FirstPersonMode.ShadowsOnly:
                    r.enabled = true;
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    break;
                case FirstPersonMode.Hidden:
                    r.enabled = false;
                    break;
                default:
                    r.enabled = true;
                    r.shadowCastingMode = ShadowCastingMode.On;
                    break;
            }
        }
    }
}
