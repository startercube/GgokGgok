using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// CCTV 리플레이에서 재현할 순간 이벤트의 종류
public enum ReplayEventType
{
    StainSpawn,   // 인형이 얼룩을 남김 (pos 위치에 생성)
    StainClean,   // 플레이어가 얼룩을 청소함 (id로 대상 식별)
    DollCaught,   // 플레이어가 인형을 잡음 (id로 대상 식별)
    WrongCaught,  // 잡기 오답 (연출용, 아직 기록하지 않음)
    DoorOpen      // 문 열림
}

// sampleInterval마다 찍는 위치 기록 1장 — 재생 시 앞뒤를 보간해 부드럽게 만든다
[System.Serializable]
public class ReplaySnapshot
{
    public float time;              // 녹화 시작 기준 경과 시간 (초)
    public Vector3 playerPos;
    public float playerYaw;         // 플레이어 몸통 Y축 회전
    public Vector3[] dollPositions; // 배열 인덱스 = 인형 ID (이름 정렬 순서)
    public float[] dollYaws;
    public bool[] dollAlive;        // false = 이미 잡혀서 파괴된 인형 (위치값 없음)
}

// 스냅샷으로는 잡을 수 없는 순간 사건 1건 (얼룩 생성/청소, 인형 포획)
[System.Serializable]
public class ReplayEvent
{
    public float time;            // 스냅샷과 같은 시계(녹화 경과 시간) 사용
    public int id;                // 얼룩 ID 또는 인형 ID
    public Vector3 pos;           // StainSpawn일 때만 사용 (생성 위치)
    public ReplayEventType type;
}

// 한 판의 전체 기록 — Recorder가 채워 GameManager에 싣고, Manager가 읽어 재생한다
[System.Serializable]
public class ReplayData
{
    public List<ReplaySnapshot> snapshots = new List<ReplaySnapshot>();
    public List<ReplayEvent> events = new List<ReplayEvent>();
    public float totalDuration;   // 녹화 총 길이 (초)
    public int dollCount;         // 인형 수 (스냅샷 배열 크기와 동일)
    public int[] dollDisguises;   // 인형 ID별 변장 후보 인덱스 (DollDisguise) — 재생 때 같은 모습으로 복원
    public float sampleInterval = 0.1f;   // 스냅샷 간격 — 재생 쪽이 같은 값으로 인덱스를 계산하도록 함께 저장
}
