using System;
using UnityEngine;

// 맵 인트로 데이터 (2026-10-23) — 작전 카드 문구, 한 주임 무전 대사, 시각표. 맵마다 에셋 하나.
// 인형방: Assets/Data/Intro/DollManiaHouse_Intro.asset (배치 도구가 없을 때만 만듦)
// 기본값은 시안(제안 단계) 내용이다. 대사·문구는 에셋 인스펙터에서 고친다.
// 설계 기록: 프로젝트 문서 「코드 설계 - 맵 인트로·무전」
[CreateAssetMenu(menuName = "Game/Map Intro Script", fileName = "MapIntro")]
public class MapIntroScript : ScriptableObject
{
    [Header("작전 카드 (0초부터 한 글자씩)")]
    [Tooltip("켜면 시각을 PC의 지금 시각으로 보여 준다 (메인화면 CCTV와 같음, 2026-10-23 사용자 결정). 끄면 아래 clock 고정")]
    public bool useRealTime = true;
    [Tooltip("useRealTime을 껐을 때 보여 줄 시각 (HH:mm)")]
    public string clock = "23:47";
    public string areaCode = "OPERATION AREA 01";
    public string areaLabel = "작전지역";
    public string areaName = "인형 마니아의 집";
    public string areaDetail = "주택 2층 · 수집 인형 약 300점";
    public string[] complaints = { "민원 1 · 밤마다 웃음소리", "민원 2 · 인형 자리가 바뀜" };

    [Header("위쪽 띠 (카드가 접힌 뒤)")]
    public string bandAreaCode = "AREA 01";

    [Header("무전 (입력 없이 자동 진행)")]
    public string speaker = "한 주임";
    public RadioLine[] lines =
    {
        new RadioLine { time = 3.5f,  text = "도착했냐. 인형 모으는 집이다." },
        new RadioLine { time = 6.6f,  text = "민원은 두 개. 밤마다 웃음소리, 아침마다 인형 자리가 바뀐다." },
        new RadioLine { time = 10.5f, text = "인형이 몇백 개다. 그중에 인형 아닌 게 섞여 있어." },
        new RadioLine { time = 13.9f, text = "한 바퀴 잘 봐 둬. 들어가면 다 똑같아 보인다." },
        new RadioLine { time = 17.5f, text = "...시작해." },
    };
    [Tooltip("마지막 줄을 보여 주는 시간 (초)")]
    public float lastLineSeconds = 1.8f;

    [Header("게임 도중 무전 (판마다 한 번씩, 비우면 안 나옴) — MapEventRadio")]
    [Tooltip("첫 오답 때 놀리는 한 줄")]
    [TextArea(1, 3)] public string wrongGuessLine = "";
    [Tooltip("얼룩 경고가 처음 켜질 때 (HUD 경고 문구와 같은 기준)")]
    [TextArea(1, 3)] public string stainWarningLine = "";

    [Header("시각표 (초)")]
    [Tooltip("작전 카드 한 글자마다 걸리는 시간")]
    public float typeCharSeconds = 0.035f;
    public float typeStartDelay = 0.3f;
    [Tooltip("작전 카드가 위쪽 띠로 접히기 시작하는 시각")]
    public float cardFoldTime = 3.5f;
    public float foldSeconds = 0.4f;
    [Tooltip("작전 개시 도장 시각. 카메라 착지(길의 마지막 점 시각)보다 이르면 착지 시각으로 맞춘다")]
    public float stampTime = 17.5f;
    public float stampSeconds = 1.6f;
    [Tooltip("조작이 풀리고 HUD가 나타나는 시각. 착지보다 이르면 착지 시각으로 맞춘다")]
    public float releaseTime = 18.3f;

    [Header("도장")]
    public string stampText = "작전 개시";
    [Tooltip("{0} = 이 맵의 빈대 수 (GameProgressManager.TotalDolls). 비우면 줄을 숨김")]
    public string stampSubFormat = "포획 대상 빈대 {0}마리";
}

[Serializable]
public struct RadioLine
{
    [Tooltip("이 줄이 나오는 시각 (초)")]
    public float time;
    [TextArea(1, 3)] public string text;
}
