# 🧸 프로토타입 문서 (Prototype Documentation)

> **인형을 찾아라 (Find the Dolls)** — 씨애랑 2026 SW 전시회 출품작
> 이 문서는 프로토타입의 **전체 흐름·구조·구현 현황을 빠르게 훑기 위한** 개요 문서입니다.
> 각 스크립트의 상세 동작과 씬 설정 방법은 [ScriptReference.md](ScriptReference.md)로 분리했습니다.
> 전체 기획은 루트의 [README.md](../README.md)를, 코드 설계 검토(SOLID·디자인 패턴)는 [SolidReview.md](SolidReview.md)를 참고하세요.
>
> **문서 지도** — 개요(이 문서) · [스크립트 상세 §3 / 씬 설정 §4](ScriptReference.md) · [설계 검토](SolidReview.md) · [맵 규격](MapSetup.md) · [기획](../README.md) · [Git 환경 설정](GitSetup.md)

---

## 1. 프로토타입 개요

### 씬 구성과 전체 흐름

프로토타입은 4개 씬으로 구성됩니다 (게임 본편 스테이지 2개).

```
MainScene (메인 메뉴)
  └─ 스테이지 선택(Carousel 카드) → StageTransition.LoadScene("DollManiaHouse" / "prototype")
       └─ GameManager.sceneName에 씬 이름 기록 (다시하기·재시작·CCTV용)

DollManiaHouse / prototype (게임 본편 스테이지)
  ├─ ESC → 일시정지 메뉴 (계속하기 / 재시작 / 메인 / 설정)
  └─ 승리/패배 → EndGame → GameManager에 결과 저장 → 흰 페이드 → ResultPopup 씬 로드

ResultPopup (결과 화면)
  ├─ 다시하기 → GameManager.sceneName에 기록된 씬 재로드
  ├─ 나가기   → MainScene
  └─ CCTV    → GameManager.isReplayMode = true로 게임 씬 재로드
       └─ ReplayManager가 게임 로직을 전부 끄고 CCTV 카메라(ReplayCam)로 한 판을 재생
          ├─ 배속 1X/2X/4X 조절, 문 열림까지 재현
          ├─ 재생 종료 → 화면 딤 + 종료 문구 (OnReplayFinished)
          └─ Exit 버튼 → ResultPopup 씬으로 복귀
          (⚠️ 오버레이 연출·진행 바는 미구현 — ScriptReference §3.19 참고)
```

씬 간 데이터(승/패, 플레이 시간, 얼룩 통계)와 **전역 설정(볼륨·마우스 감도)** 은 **`GameManager`(DontDestroyOnLoad 싱글톤)** 가 실어 나릅니다.
GameManager는 MainScene에 배치되어 있으므로 **게임은 반드시 MainScene부터 실행**해야 합니다 ([§3.10](ScriptReference.md#310-gamemanagercs--씬-간-데이터-전달--전역-설정-dontdestroyonload-싱글톤) 참고).

`DollManiaHouse`(인형매니아 하우스 테마) 씬은 에디터 도구 **`MapSpec`** 으로 방·가구·프롭·웨이포인트를 자동 생성/정비합니다 ([MapSetup.md](MapSetup.md) 참고).

### 게임 본편(게임 씬) 플레이 흐름

1. 게임 시작 → **전경 카메라가 방을 한 바퀴 보여주는 인트로** (기본 20초)
   - 인트로 동안 **게임플레이는 완전 정지** (인형 frozen, 플레이어 입력·카메라 축 잠금) — 인형은 이미 시작 은신처로 **순간이동해 숨어 있는 상태**라 동선이 드러나지 않음
2. **검은 페이드로 화면을 가린 뒤 컷 전환**으로 플레이어 카메라 진입 → **1인칭 이동/시점 조작** (점프는 제거됨)
3. **인형(Doll)** 은 웨이포인트 사이를 NavMesh로 이동하며 숨음
   - 플레이어 시야에 **들어오기 직전에 미리 정지** — 실제 시야각·거리보다 넓게 잡은 '위험 지대'(시야각 + 거리 + Raycast 판정 + 여유 마진)에 들면 멈춤
   - **앞으로 갈 길이 시야를 지나면 아예 출발하지 않거나 도중에 대기** (경로 미리보기) — 걸어서 시야로 들어가는 일이 없음
   - 목적지를 고를 때도 **플레이어가 보고 있는 은신처는 후보에서 제외**
   - 가구 위 은신처는 **NavMeshLink를 순간이동으로 건너감** (시선에 잡혀 있거나 건너편이 노출돼 있으면 건너지 않음)
   - 이동할 때마다 이전 은신처에 **얼룩(Stain)을 남김** — 단 첫 이동과 눈높이 위 은신처는 예외, 올라가는 링크는 발치에 얼룩을 남김
   - 은신처 근처로 다가가면 **웃음소리 힌트**(DollLaugh)가 3D 사운드로 재생됨
4. 플레이어는 화면 중앙 조준으로 상호작용 (`E` 키, 6m)
   - **Stain** → 청소 (제거)
   - **Prop** → 잡기 시도. 진짜 인형이면 포획, 오답이면 쿨타임(2초) 동안 재시도 불가
5. 상단 UI(GameHUD)에 **잡은 인형 수 / 청소한 얼룩 수** 표시
   - 남은 얼룩이 `warningThreshold`(기본 5) 이상이면 경고 문구 표시
6. **패배**: 남은 얼룩이 `defeatThreshold`(기본 7) 이상이 되면 게임 정지 → 흰 화면 연출 → 결과 화면
7. **승리**: 모든 얼룩 청소 + 모든 인형 포획 → **"문이 열렸다" 문구와 함께 문이 옆으로 슬라이드 오픈** (전용 카메라로 클로즈업)
8. 열린 문 너머의 **빛나는 흰색 구역(ExitZone)** 으로 걸어 나가면 → 화면이 하얗게 덮이며 결과 화면으로

승리(ExitZone)·패배(TriggerDefeat) 모두 `GameProgressManager.EndGame()`으로 합류하며, 결과 데이터와 **CCTV 리플레이 녹화본(`ReplayData`)** 을
`GameManager`에 저장하고 흰 화면 페이드 후 `GoToResult()`가 **ResultPopup 씬을 로드**합니다.

플레이 중에는 `ReplayRecorder`가 플레이어/인형의 위치를 0.1초 간격으로 녹화하고(스냅샷),
얼룩 생성/청소·인형 포획·문 열림을 이벤트로 기록합니다 ([§3.18](ScriptReference.md#318-replayrecordercs--cctv-리플레이-녹화기) 참고).
BGM 등 사운드 전반은 미구현입니다 (웃음 힌트·볼륨 설정만 존재). ([§5. 기획 대비 구현 현황](#5-기획-대비-구현-현황) 참고)

### 게임 흐름의 두 축 — GameProgressManager & GameHUD

게임 진행의 핵심은 두 스크립트로 나뉘어 있습니다 (역할 분리):

- **`GameProgressManager`** (Core) — 진행도 카운트, 승패 판정, 게임 종료, ESC 일시정지 등 **상태·규칙** 담당. 값이 바뀌면 `OnProgressChanged`/`OnDoorOpened` **이벤트**로 알림
- **`GameHUD`** (UI) — 그 이벤트를 구독해 카운트·경고·프롬프트·문 열림 문구를 **표시**하고, 리플레이 모드에서는 Exit·배속 버튼과 재생 종료 연출을 제공

설정도 같은 패턴입니다 — **`GameManager`** 가 전역 상태(감도 static + `OnSensitivityChanged` 이벤트)를 갖고, `PlayerMove`가 구독해 즉시 반영하며, `SettingsManager`(인게임 설정 패널)는 슬라이더를 그 창구에 연결만 합니다.

---

## 2. 스크립트 구조

스크립트는 `Assets/Scripts/` 아래 역할별 폴더로 정리되어 있습니다 (에디터 도구는 `Assets/Editor/`).
각 스크립트의 상세 동작은 [ScriptReference.md §3](ScriptReference.md#3-스크립트별-상세-설명)을 참고하세요.

| 폴더 | 스크립트 | 역할 | 붙는 오브젝트 (씬) |
|---|---|---|---|
| `Player/` | `PlayerMove.cs` | 플레이어 이동, 카메라-몸통 회전 동기화, 감도 적용 | Player (게임 씬) |
| `Player/` | `PlayerInteraction.cs` | 조준 대상 판별, 청소/잡기 상호작용 | Player (게임 씬) |
| `Player/` | `CameraManagement.cs` | 인트로→플레이어 카메라 전환, 인트로 게임플레이 잠금, 카메라 우선순위 제어 | 씬 매니저 오브젝트 (게임 씬) |
| `Doll/` | `DollMove.cs` | 인형 AI 본체 — 이동·은신 상태 머신 (이동권, 도망/순찰, 링크 순간이동, 경로 미리보기) | 각 Doll 프리팹 (게임 씬) |
| `Doll/` | `DollVision.cs` | 가시성 판정 — 임의 지점이 플레이어 시야(+여유 마진)에 드는가 | 각 Doll 프리팹 (게임 씬) |
| `Doll/` | `DollStainSpawner.cs` | 얼룩 생성 + `OnStainSpawned` 이벤트 발행 | 각 Doll 프리팹 (게임 씬) |
| `Doll/` | `WaypointSelector.cs` | 은신처 선택 점수 계산 (거리 가중치 + 가시성 제외) | (컴포넌트 아님 — DollMove가 생성) |
| `Doll/` | `WaypointRegistry.cs` | 인형 간 웨이포인트 자리 예약 (static) | (컴포넌트 아님) |
| `Doll/` | `DollLaugh.cs` | 접근 시 웃음소리 힌트 1회 재생 (3D 사운드) | 각 Doll 프리팹 (게임 씬) |
| `Stage/` | `DoorController.cs` | 승리 시 슬라이드로 열리는 문 + 문 카메라 연출 | Door (게임 씬) |
| `Stage/` | `ExitZone.cs` | 문밖 탈출 구역 — 진입 시 승리 확정 | 흰색 구역 트리거 (게임 씬) |
| `Core/` | `GameProgressManager.cs` | 진행도·승패·게임 종료·일시정지 (싱글톤, 이벤트 발행) | 매니저 오브젝트 (게임 씬) |
| `UI/` | `GameHUD.cs` | 진행도/프롬프트/경고 표시, 리플레이 Exit·배속·종료 연출 (싱글톤, 이벤트 구독) | HUD (게임 씬) |
| `UI/` | `ScreenFader.cs` | 화면을 덮었다 걷어내는 공용 페이드/딤 (카메라 컷 전환 · 게임 종료 · 리플레이 종료) | 전체 화면 Image — 검은색/흰색 |
| `UI/` | `MainMenuManager.cs` | 메인 메뉴 팝업(스테이지 선택/조작법/설정) 토글, 게임 종료 | 메뉴 UI (MainScene) |
| `UI/` | `Carousel.cs` | 스테이지 선택 카드 캐러셀 (드래그 회전) | 스테이지 선택 팝업 (MainScene) |
| `UI/` | `StageTransition.cs` | 스테이지 씬 로드 + 다시하기용 씬 이름 기록 | 스테이지 카드 버튼 (MainScene) |
| `UI/` | `ResultPopupUI.cs` | 결과 팝업 연출 (통계 카운트업, 도장, 다시하기/나가기/CCTV) | 결과 UI (ResultPopup) |
| `Core/` | `GameManager.cs` | 씬 간 결과 데이터 전달 + 전역 설정(볼륨/감도) (DontDestroyOnLoad 싱글톤) | GameManager 오브젝트 (MainScene) |
| `Core/` | `DebugCheats.cs` | 디버그 치트 (클리어/패배 직전 점프) | 테스트용 아무 오브젝트 (게임 씬) |
| `Setting/` | `SettingsManager.cs` | 인게임 설정 패널 — 슬라이더를 GameManager에 연결 | 일시정지 설정 패널 (게임 씬) |
| `Replay/` | `ReplayData.cs` | CCTV 리플레이 기록의 데이터 구조 (순수 C# 클래스) | (컴포넌트 아님) |
| `Replay/` | `IReplayPausable.cs` | 리플레이 중 정지 대상 표식 (마커 인터페이스) | (인터페이스) |
| `Replay/` | `ReplayIdentity.cs` | 인형/얼룩 ID 정렬 규칙 (녹화·재생 공용 static) | (컴포넌트 아님) |
| `Replay/` | `ReplayRecorder.cs` | 플레이 중 위치 스냅샷·이벤트 녹화 (싱글톤) | Recorder 오브젝트 (게임 씬) |
| `Replay/` | `ReplayManager.cs` | 리플레이 모드 부트스트랩 + 스냅샷/이벤트 재생 + 종료 이벤트 | Replay 오브젝트 (게임 씬) |
| `Editor/` | `MapSpec.cs` | DollManiaHouse 맵 규격 일괄 적용 도구 ([MapSetup.md](MapSetup.md)) | (에디터 전용 — 씬에 안 붙음) |

### 스크립트 간 의존 관계

```
[게임 본편 — 게임 씬]
PlayerInteraction ──(잡기)──▶ DollMove.OnCaught()
PlayerInteraction ──(청소/포획)──▶ GameProgressManager.CleanStain/CatchDoll
DollMove ──(가시성 질의)──▶ DollVision.IsInDangerZone / IsPointInDangerZone
DollMove ──(은신처 선택)──▶ WaypointSelector ──(자리 예약)──▶ WaypointRegistry
DollMove ──(얼룩 남길 타이밍 판단)──▶ DollStainSpawner.Spawn/SpawnAt
DollStainSpawner ──(OnStainSpawned 이벤트)──▶ GameProgressManager.AddStain() · ReplayRecorder.RecordStainSpawn()
DollLaugh ──(플레이어 접근 감지)──▶ AudioSource.PlayOneShot (웃음 힌트, DollMove와 독립)
GameProgressManager ──(진행도/문열림 이벤트)──▶ GameHUD (OnProgressChanged / OnDoorOpened)
PlayerInteraction ──(프롬프트)──▶ GameHUD.ShowPrompt/HidePrompt
PlayerMove ──(회전 동기화)──▶ CinemachinePanTilt
GameManager ──(감도 변경 방송 OnSensitivityChanged)──▶ PlayerMove (Cinemachine Gain 재계산)
SettingsManager ──(슬라이더)──▶ GameManager.SetMouseSensitivity / AudioMixer
CameraManagement ──(Priority 제어)──▶ CinemachineCamera × 4 (전경/플레이어/CCTV/문)
CameraManagement ──(인트로 잠금/해제)──▶ DollMove.SetFrozen · PlayerMove/PlayerInteraction.enabled
CameraManagement ──(전환 시 화면 가림)──▶ ScreenFader(검은색, 선택)

[승리]  GameProgressManager.CheckVictory ──▶ TriggerVictory ──▶ DoorController.Open()
        DoorController ──(열 때)──▶ doorCam Priority 70 → 다 열리면 ExitZone 활성화, 1초 뒤 복귀
        ExitZone ──(플레이어 진입)──▶ GameProgressManager.EndGame(true)
[패배]  GameProgressManager.AddStain ──(남은 얼룩 ≥ defeatThreshold)──▶ TriggerDefeat ──▶ EndGame(false)
[공통]  EndGame ──▶ GameManager에 결과 저장 ──▶ ScreenFader(흰색).FadeOut
        ──▶ GoToResult ──▶ SceneManager.LoadScene("ResultPopup")
[일시정지] ESC ──▶ GameProgressManager.Pause() ──▶ 일시정지 메뉴 (계속/재시작/메인/설정)
[디버그] DebugCheats ──▶ GameProgressManager (CleanStain/CatchDoll/AddStain)

[씬 전환]
MainMenuManager ──(팝업 토글)──▶ 메뉴 패널  /  Carousel ──(드래그)──▶ 스테이지 카드 회전
StageTransition.LoadScene ──(씬 이름 기록)──▶ GameManager.sceneName ──▶ 스테이지 씬 로드
ResultPopupUI ──(결과 읽기)──▶ GameManager (isClear/playTime/얼룩 통계)
ResultPopupUI ──(다시하기)──▶ sceneName 재로드 / (나가기)──▶ MainScene

[CCTV 리플레이]
PlayerInteraction·GameProgressManager ──(이벤트 기록)──▶ ReplayRecorder.Record○○
DollStainSpawner ──(OnStainSpawned 구독)──▶ ReplayRecorder.RecordStainSpawn
GameProgressManager.EndGame ──(녹화 종료)──▶ ReplayRecorder.FinishRecording ──▶ GameManager.replayData
ResultPopupUI(CCTV 버튼) ──(isReplayMode = true)──▶ 게임 씬 재로드 ──▶ ReplayManager 부팅
ReplayManager ──(IReplayPausable 전부 비활성화)──▶ 스냅샷 보간 + 이벤트 발화(얼룩/포획/문열림) 재현
CameraManagement ──(리플레이 모드 감지)──▶ ReplayCam Priority 50 (CCTV 시점)
ReplayManager ──(재생 종료 OnReplayFinished)──▶ GameHUD (화면 딤 + 종료 문구)
GameHUD ──(Exit / 배속 1X·2X·4X)──▶ ResultPopup 복귀 / ReplayManager.playbackSpeed
```

---

## 3~4. 스크립트별 상세 & 씬 설정 → [ScriptReference.md](ScriptReference.md)

각 스크립트의 내부 동작(§3)과 씬별 셋업 체크리스트(§4)는 분량이 커서 별도 문서로 옮겼습니다.
필요한 항목만 골라 보세요:

- **스크립트 상세**
  - Player/Doll — [§3.1 DollMove](ScriptReference.md#31-dollmovecs--인형-ai) · [§3.2 PlayerMove](ScriptReference.md#32-playermovecs--플레이어-이동) · [§3.3 PlayerInteraction](ScriptReference.md#33-playerinteractioncs--상호작용) · [§3.4 CameraManagement](ScriptReference.md#34-cameramanagementcs--카메라-연출) · [§3.20 DollLaugh](ScriptReference.md#320-dolllaughcs--은신처-웃음소리-힌트)
  - Doll 보조 컴포넌트 — [§3.23 DollVision](ScriptReference.md#323-dollvisioncs--가시성-판정) · [§3.24 DollStainSpawner](ScriptReference.md#324-dollstainspawnercs--얼룩-생성) · [§3.25 WaypointSelector·WaypointRegistry](ScriptReference.md#325-waypointselectorcs--waypointregistrycs--은신처-선택과-자리-예약)
  - 흐름/UI — [§3.5 GameProgressManager](ScriptReference.md#35-gameprogressmanagercs--진행도--승패--게임-종료--일시정지-싱글톤) · [§3.6 GameHUD](ScriptReference.md#36-gamehudcs--게임-hud--리플레이-컨트롤-싱글톤) · [§3.7 DoorController](ScriptReference.md#37-doorcontrollercs--문-열림-연출) · [§3.8 ExitZone](ScriptReference.md#38-exitzonecs--탈출-구역) · [§3.9 ScreenFader](ScriptReference.md#39-screenfadercs--공용-화면-페이드)
  - 씬 전환/메뉴/설정 — [§3.10 GameManager](ScriptReference.md#310-gamemanagercs--씬-간-데이터-전달--전역-설정-dontdestroyonload-싱글톤) · [§3.11 MainMenuManager](ScriptReference.md#311-mainmenumanagercs--메인-메뉴) · [§3.12 Carousel](ScriptReference.md#312-carouselcs--스테이지-선택-캐러셀) · [§3.13 StageTransition](ScriptReference.md#313-stagetransitioncs--스테이지-씬-로드) · [§3.14 ResultPopupUI](ScriptReference.md#314-resultpopupuics--결과-팝업-resultpopup-씬) · [§3.15 DebugCheats](ScriptReference.md#315-debugcheatscs--디버그-치트) · [§3.21 SettingsManager](ScriptReference.md#321-settingsmanagercs--인게임-설정-패널)
  - 리플레이 — [§3.16 ReplayData](ScriptReference.md#316-replaydatacs--cctv-리플레이-데이터-구조) · [§3.17 IReplayPausable·ReplayIdentity](ScriptReference.md#317-ireplaypausablecs--replayidentitycs--리플레이-공용-규칙) · [§3.18 ReplayRecorder](ScriptReference.md#318-replayrecordercs--cctv-리플레이-녹화기) · [§3.19 ReplayManager](ScriptReference.md#319-replaymanagercs--cctv-리플레이-재생기)
  - 에디터 도구 — [§3.22 MapSpec](ScriptReference.md#322-mapspeccs--맵-규격-자동화-에디터-전용) (상세는 [MapSetup.md](MapSetup.md))
- **씬 설정 체크리스트** — [§4 Setup Checklist](ScriptReference.md#4-씬-설정-요구사항-setup-checklist) (MainScene / 게임 본편 / DollManiaHouse / ResultPopup)

---

## 5. 기획 대비 구현 현황

README의 기획 대비 현재 프로토타입 상태:

| 기획 항목 | 상태 | 비고 |
|---|---|---|
| 메인 메뉴 / 스테이지 선택 | ✅ 구현 | `MainMenuManager` + `Carousel`(카드 회전) + `StageTransition`. 조작법/설정 팝업 포함 |
| 인트로 카메라 연출 | ✅ 구현 | `CameraManagement` — 검은 페이드 + 컷 전환. 인트로 동안 게임플레이 완전 정지, 인형은 은신처로 순간이동해 시작 |
| 인형 시선 감지 정지 (Raycast) | ✅ 구현 | FOV + 거리 + 가림 3단계(`DollVision`). **여유 마진을 둬 시야에 들기 전에 미리 정지.** NavMeshLink 통과도 시선에 잡히면 중단 |
| 이동 중 노출 회피 | ✅ 구현 | 경로 미리보기(`lookaheadDistance` 기본 6m) — 출발 전·이동 중 검사해 노출 구간이면 대기. 목적지 선택에서도 노출된 은신처 제외 |
| 인형 이동 쿨타임 | ✅ 구현 | 이동권(토큰) 시스템 + 시작 유예(`startGraceTime` 기본 10초) |
| 이동 시 얼룩 남기기 | ✅ 구현 | `DollStainSpawner` → `OnStainSpawned` 이벤트 → 진행도 목표치 증가. 첫 이동·눈높이 위 은신처는 예외, 올라가는 링크는 발치에 얼룩 |
| 근접 시 20% 과감 이동 | ✅ 구현 | `nearMoveChance` |
| 얼룩 청소 / 인형 잡기 | ✅ 구현 | 오답 쿨타임 포함, 조준 거리 6m |
| 진행도 UI (인형/얼룩) | ✅ 구현 | `GameProgressManager`(상태) + `GameHUD`(표시, 이벤트 구독) |
| 위기 경고 문구 | ✅ 구현 | 남은 얼룩 `warningThreshold`(기본 5) 이상 |
| 일시정지 메뉴 (ESC) | ✅ 구현 | `GameProgressManager.Pause` — 계속/재시작/메인/설정. 재시작은 `GameManager.sceneName` 사용 (하드코딩 해결) |
| 승리 조건 (문 열림/탈출) | ✅ 구현 | 문 슬라이드 오픈(전용 카메라 연출) → ExitZone 진입으로 확정. 문밖 낙하는 DollManiaHouse에서 `OutsideFloor`로 해결 |
| 패배 조건 (얼룩 임계 초과) | ✅ 구현 | `defeatThreshold`(기본 7), 승리와 같은 종료 흐름 합류 |
| 결과 화면 연동 | ✅ 구현 | `EndGame` → `GameManager` 저장 → ResultPopup 씬 → 통계 카운트업 + 도장 연출 + 다시하기/나가기. ⚠️ 이동 횟수 `-3` 하드코딩 |
| 도장 평가 (Clear/Game Over) | ✅ 구현 | `ResultPopupUI` 스탬프 연출 |
| 플레이 시간 기록 | ✅ 구현 | `GameProgressManager.PlayTime` → 결과 화면 표시 |
| 설정 (볼륨/마우스 감도) | ✅ 구현 | `GameManager`(전역 상태 + PlayerPrefs 저장 + 감도 변경 이벤트) + `SettingsManager`(인게임 패널). 메인/인게임 어디서 바꿔도 동기화 |
| 은신처 웃음소리 힌트 | ✅ 구현 | `DollLaugh` — 접근 시 1회 재생, 재장전 거리로 연속 재생 방지, 3D 사운드 |
| 구역 번호 기반 인근 이동 (5번→3~7번) | ⚠️ 다르게 구현 | 현재는 **플레이어 거리 기반 가중치 랜덤**. 기획 확정 필요 |
| 스테이지 테마 / DLC | ⚠️ 일부 구현 | **인형매니아 하우스(`DollManiaHouse`) 씬 구현** — `MapSpec` 도구로 생성 ([MapSetup.md](MapSetup.md)). 나머지 테마 미구현 |
| 인형 외형 랜덤 위장 | ❌ 미구현 | 랜덤은 아니나, DollManiaHouse에선 프롭 인형(`doll`)과 움직이는 인형(`doll_move`)이 동일 외형이라 위장 자체는 성립 |
| BGM 루프 / 사운드 전반 | ❌ 미구현 | 웃음 힌트(`DollLaugh`)와 볼륨 설정(AudioMixer)만 존재 |
| CCTV 리플레이 | ✅ 구현 | 녹화(`ReplayRecorder`) → CCTV 버튼 → 리플레이 부팅 → CCTV 카메라 재생(배속 1X/2X/4X, 문 열림 재현, 종료 시 딤+문구) → Exit 복귀. 플레이어 더미는 DollManiaHouse에서 해결(MapSpec이 머리+몸통 부착). ⚠️ **오버레이 연출·진행 바·인트로 스킵 미구현** ([ScriptReference §3.19](ScriptReference.md#319-replaymanagercs--cctv-리플레이-재생기) 참고) |
| Before/After | ❌ 미구현 | |

---

*최종 수정: 2026-08-10 · 브랜치: `dev`*
