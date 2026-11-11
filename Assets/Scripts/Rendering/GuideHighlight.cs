using System.Collections.Generic;
using UnityEngine;

// 튜토리얼 가이드 외곽선(봐야 할 것·가야 할 곳)의 "무엇을, 어느 카메라에" 보관함.
// AimHighlight와 같은 모양. 넣는 쪽은 TutorialFocus, 그리는 쪽은 AimOutlineFeature의 가이드 그룹(하늘색·깜빡임·보이는 부분만).
// 공용이라 다른 맵의 길 안내(한 주임 무전 등)도 여기에 넣으면 같은 표시가 나온다.
// 주의: 빈대·소품(찾을 대상)은 넣지 않는다 — 가이드는 찾기를 대신하지 않는다. 설계: 「코드 설계 - 튜토리얼 가이드 시각화」
public static class GuideHighlight
{
    static readonly OutlineTargetSet set = new OutlineTargetSet();

    public static OutlineTargetSet Targets => set;
    public static GameObject Target => set.Target;
    public static Camera ViewCamera => set.ViewCamera;
    public static IReadOnlyList<OutlineTargetSet.Item> Items => set.Items;
    public static bool HasTarget => set.HasTarget;
    public static bool ShouldDraw(Camera cam) => set.ShouldDraw(cam);

    /// <summary>가이드 대상을 바꾼다. 같은 대상이면 아무것도 하지 않으므로 매 프레임 불러도 된다</summary>
    public static void Set(GameObject unit, Camera viewCamera) => set.Set(unit, viewCamera);

    public static void Clear() => set.Clear();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => set.Reset();
}
