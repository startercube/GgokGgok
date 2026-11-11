using System.Collections;
using UnityEngine;

// 디버그 치트 — 아무 오브젝트에나 붙여서 인스펙터 체크박스 또는 컴포넌트 우클릭 메뉴로 실행
public class DebugCheats : MonoBehaviour, IReplayPausable
{
    [Header("시작 시 자동 실행")]
    [Tooltip("켜면 시작하자마자 모든 Stain/Doll을 제거하고 클리어 상태로 점프 (문 열림)")]
    [SerializeField] private bool instantClearOnStart = false;
    [Tooltip("켜면 시작하자마자 패배 직전 상태로 점프 (얼룩이 1개만 더 생기면 패배)")]
    [SerializeField] private bool nearDefeatOnStart = false;

    IEnumerator Start()
    {
        yield return null;   // GameProgressManager.Start의 목표 집계가 끝난 뒤 실행되도록

        if (instantClearOnStart)
            InstantClear();
        else if (nearDefeatOnStart)
            NearDefeat();
    }

    // 실제 플레이와 같은 경로(CleanStain/CatchDoll)로 제거해 승리 판정까지 태운다
    [ContextMenu("클리어 상태로 점프")]
    private void InstantClear()
    {
        if (GameProgressManager.Instance == null) return;

        foreach (GameObject stain in GameObject.FindGameObjectsWithTag("Stain"))
        {
            GameProgressManager.Instance.CleanStain();
            Destroy(stain);
        }

        foreach (DollMove doll in FindObjectsByType<DollMove>(FindObjectsSortMode.None))
        {
            GameProgressManager.Instance.CatchDoll();
            Destroy(doll.gameObject);
        }
    }

    // 실물 없는 가상 카운트로 패배 기준 바로 아래까지 올린다 — 얼룩 1개만 더 생기면 패배
    [ContextMenu("패배 직전 상태로 점프")]
    private void NearDefeat()
    {
        if (GameProgressManager.Instance == null) return;

        while (GameProgressManager.Instance.RemainingStains < GameProgressManager.Instance.DefeatThreshold - 1)
            GameProgressManager.Instance.AddStain();
    }
}
