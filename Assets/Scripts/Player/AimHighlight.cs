using System.Collections.Generic;
using UnityEngine;

// 조준 외곽선(투시)의 "무엇을, 어느 카메라에" 보관함.
// 게임 쪽(PlayerInteraction)이 넣고, 그래픽 쪽(AimOutlineFeature)이 읽기만 한다 — 서로를 모른다.
// 2026-11-02: 대상 목록 본문을 OutlineTargetSet으로 옮김 (튜토리얼 가이드 그룹 GuideHighlight와 같은 코드를 쓰려고). 공개 API는 그대로
// 설계: 「코드 설계 - 조준 외곽선(투시)」, 「코드 설계 - 튜토리얼 가이드 시각화」
public static class AimHighlight
{
    static readonly OutlineTargetSet set = new OutlineTargetSet();

    /// <summary>렌더러 기능이 그룹으로 다룰 때 쓰는 대상 목록</summary>
    public static OutlineTargetSet Targets => set;

    /// <summary>외곽선을 그릴 오브젝트 (하이라이트 단위의 루트)</summary>
    public static GameObject Target => set.Target;
    /// <summary>이 카메라가 렌더할 때만 그린다 — CCTV·무전 초상화 카메라·씬 뷰 제외</summary>
    public static Camera ViewCamera => set.ViewCamera;
    /// <summary>그릴 렌더러 목록. 대상이 바뀔 때만 다시 모은다</summary>
    public static IReadOnlyList<OutlineTargetSet.Item> Items => set.Items;

    public static bool HasTarget => set.HasTarget;

    public static bool ShouldDraw(Camera cam) => set.ShouldDraw(cam);

    /// <summary>외곽선 대상을 바꾼다. 같은 대상이면 아무것도 하지 않으므로 매 프레임 불러도 된다</summary>
    public static void Set(GameObject unit, Camera viewCamera) => set.Set(unit, viewCamera);

    public static void Clear() => set.Clear();

    // 에디터에서 "도메인 다시 불러오기 끄기"로 플레이해도 지난 판의 대상이 남지 않게
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => set.Reset();
}
