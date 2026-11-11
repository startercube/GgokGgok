using UnityEngine;

// 게임 초기화 (2026-10-24, 전시회용 초기화 버튼). 설계: 프로젝트 문서 「코드 설계 - 전시회 초기화 버튼」
// "무엇을 지우는지"만 여기서 정한다 — 저장 키는 각 주인(StageClearRecord·GameManager)만 안다.
// PlayerPrefs.DeleteAll을 쓰지 않는 이유: Windows 빌드에선 Unity가 해상도·전체 화면 설정도 같은 저장소에 넣어 전시 PC 화면 설정까지 지워진다.
// 저장 항목이 늘면 그 항목의 주인에 Clear 함수를 만들고 ResetAll에 한 줄 추가
public static class GameReset
{
    /// <summary>
    /// 저장된 진행·설정과 지난 판 결과를 지운다. 화면(빈대 수·슬라이더)은 부르는 쪽이 씬을 다시 불러 되돌린다
    /// </summary>
    /// <param name="clearRecords">맵 클리어 기록 (메인화면 격리실 빈대 수)</param>
    /// <param name="resetSettings">마우스 감도·볼륨을 기본값으로</param>
    public static void ResetAll(bool clearRecords = true, bool resetSettings = true)
    {
        int records = clearRecords ? StageClearRecord.ClearAll() : 0;
        if (resetSettings) GameManager.ResetSettings();

        // 지난 판 결과 (메모리에만 있음): 결과·리플레이 데이터, 리플레이 모드
        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.ResetGameData();
            gm.isReplayMode = false;
        }

        // 재시작 인트로 생략 기록 (메모리) → 다음 관람객은 인트로부터
        StageRestart.ClearAll();

        PlayerPrefs.Save();
        Debug.Log($"[초기화] 클리어 기록 {(clearRecords ? records + "개 지움" : "유지")} · 설정 {(resetSettings ? "기본값" : "유지")}");
    }
}
