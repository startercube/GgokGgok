# 📚 스크립트 레퍼런스 (Script Reference)

> **인형을 찾아라 (Find the Dolls)** — 씨애랑 2026 SW 전시회 출품작
> 이 문서는 각 스크립트의 **상세 동작**과 **씬 설정 방법**을 담은 레퍼런스입니다.
> 전체 흐름·구조·구현 현황을 빠르게 훑으려면 [Prototype.md](Prototype.md)를,
> 코드 설계 검토(SOLID·디자인 패턴)는 [SolidReview.md](SolidReview.md)를,
> 맵 치수 규격·자동화 도구는 [MapSetup.md](MapSetup.md)를 참고하세요.

---

## 3. 스크립트별 상세 설명

> 게임 흐름 관리는 **`GameProgressManager`(진행도·승패·종료·일시정지)** 와 **`GameHUD`(UI 표시·리플레이 컨트롤)** 로 나뉘어 있습니다 (과거 단일 `GameUIManagement`에서 분리).

### 3.1 DollMove.cs — 인형 AI

프로토타입의 핵심 스크립트. `NavMeshAgent` 기반으로 동작하며 `IReplayPausable`을 구현합니다 (리플레이 중 자동 정지 — [§3.17](#317-ireplaypausablecs--replayidentitycs--리플레이-공용-규칙) 참고).

**역할이 4개 파일로 나뉘어 있습니다.** DollMove는 **이동·은신 상태 머신**만 담당하고, 나머지는 아래 컴포넌트에 위임합니다 (SRP 분리 — [SolidReview §3.4](SolidReview.md) 참고):

| 담당 | 파일 | 관계 |
|---|---|---|
| 이동·은신 결정 (이동권, 도망/순찰, 링크 통과) | `DollMove` | 본체 |
| 가시성 판정 | [`DollVision`](#323-dollvisioncs--가시성-판정) | `[RequireComponent]` — 같은 오브젝트 |
| 얼룩 생성·통지 | [`DollStainSpawner`](#324-dollstainspawnercs--얼룩-생성) | `[RequireComponent]` — 같은 오브젝트 |
| 은신처 선택 점수 계산 | [`WaypointSelector`](#325-waypointselectorcs--waypointregistrycs--은신처-선택과-자리-예약) | `Start`에서 생성하는 순수 계산 객체 |
| 인형 간 자리 예약 | [`WaypointRegistry`](#325-waypointselectorcs--waypointregistrycs--은신처-선택과-자리-예약) | static 클래스 |

#### 핵심 규칙 — "위험 지대"에서 미리 멈춘다

인형은 **실제로 보이고 나서 멈추는 게 아니라, 보이기 직전에 멈춥니다.** 실제 시야각·거리보다 넓게 잡은 판정 범위를 **위험 지대**(`DollVision.IsInDangerZone`)라 부르며, 이 여유분이 "카메라를 돌렸더니 인형이 화면 안에서 굳는" 장면을 막아 줍니다. 노출 경로는 두 가지이고 각각 다른 장치가 막습니다:

| 노출 경로 | 대응 |
|---|---|
| 플레이어가 **카메라를 돌려** 인형을 시야에 넣음 | **여유각 정지** — 위험 지대에 드는 순간 정지 |
| 인형이 **걸어서** 시야 안으로 들어감 | **경로 미리보기** — 앞으로 갈 길이 위험 지대를 지나면 출발하지 않거나 도중에 대기 |

#### 상태 — `DollState`

| 상태 | 의미 |
|---|---|
| `Hiding` | 은신처 도착 후 숨는 중 — 이동권이 충전되는 유일한 상태 |
| `Moving` | 다음 은신처로 이동 중 |
| `WaitingForClearPath` | 갈 길(또는 링크 건너편)이 위험 지대라 안전한 현재 자리에서 대기 중 |

`isFrozen`(인트로 연출)과 위험 지대 정지는 **상태에 넣지 않습니다** — 하던 일을 바꾸는 게 아니라 잠시 덮어쓰는 정지라, 풀리면 원래 하던 일을 그대로 이어가야 하기 때문.

#### 매 프레임 흐름 (`Update`)

```
isFrozen? (인트로 연출 중 CameraManagement가 SetFrozen(true))
 └─ Yes → 아무것도 안 함 (이동/판정 전부 중단)
NavMesh 밖으로 벗어났나?
 └─ Yes → 근처 NavMesh로 Warp 복귀 (실패 시 경고 후 중단)
위험 지대 안인가? (vision.IsInDangerZone)
 ├─ Yes → agent.isStopped = true, 아무것도 안 함 (완전 정지)
 └─ No  → 오프메시링크 위라면:
            건너편이 위험 지대? → 건너지 않고 WaitingForClearPath로 대기
            아니면 순간이동으로 통과 (오르막이면 발치에 얼룩)
          앞 경로가 위험 지대를 지나나? (IsPathAheadInDanger)
           ├─ Yes → 정지 + WaitingForClearPath (숨은 자리에서 기다림)
           └─ No  → 대기 중이었다면 Moving으로 복귀
          UpdateHidingState()  : 은신 상태 갱신 + 이동권 충전
          HandleMoveDecision() : 도망/순찰 이동 결정
```

#### 경로 미리보기 — `IsPathAheadInDanger(corners)`

경로 폴리라인(`agent.path.corners`)을 앞에서부터 `lookaheadDistance`(기본 6m)까지 `lookaheadStep`(기본 0.5m) 간격으로 점을 찍어, 하나라도 위험 지대에 들면 `true`. 두 곳에서 씁니다:

- **출발 전** (`SetTarget`) — 경로가 노출되면 **출발 자체를 보류**해 이동권을 아낌
- **이동 중** (`Update`) — 다음 구간이 노출되면 아직 안전한 현재 위치에서 `WaitingForClearPath`로 대기, 시야가 풀리면 재개

> `lookaheadStep`을 줄이면 판정이 촘촘해지지만 Raycast 횟수가 늘어납니다. 0 이하로 설정해도 무한 루프가 나지 않도록 최소 0.1m로 고정됩니다.

#### 시작 배치 — `WarpToStartSpot()`

`Start()`에서 플레이어와 가장 먼 웨이포인트로 **걷지 않고 순간이동(Warp)** 합니다.
걸어가면 인트로 카메라가 방을 훑는 동안 인형의 위치와 동선이 그대로 드러나기 때문.
시작부터 `Hiding` 상태로 출발하며, 이 배치는 이동권을 소모하지 않습니다.

#### 인트로 정지 — `SetFrozen(bool)`

[`CameraManagement`](#34-cameramanagementcs--카메라-연출)가 인트로 시작 시 `SetFrozen(true)`, 플레이어 카메라 전환이 끝나면 `SetFrozen(false)`를 호출합니다. 컴포넌트를 끄는 대신 플래그를 쓰는 이유는 **`Start()`의 시작 배치(Warp)는 인트로 중에도 실행돼야 하기 때문.**

#### 오프메시링크 — 가구 위 은신처로 순간이동

가구 윗면 은신처는 바닥과 **NavMeshLink**로 연결됩니다 (링크 생성은 [MapSpec](#322-mapspeccs--맵-규격-자동화-에디터-전용) 담당).

- `Start()`에서 `agent.autoTraverseOffMeshLink = false` — 자동 통과 중에는 `isStopped`가 듣지 않아, **플레이어가 쳐다봐도 링크를 건너가 "보면 안 움직인다" 대전제가 깨지기 때문**
- 대신 `Update`에서 위험 지대 판정을 통과한 프레임에만 `CompleteOffMeshLink()`로 **순간이동해서 건넘** — 링크 앞에서 대기하다가 시선이 풀리는 순간 사라지듯 이동
- **건너편(`link.endPos`)이 위험 지대면 건너지 않습니다** — 링크 통과는 순간이동이라, 검사 없이 건너면 한 프레임 만에 시야 안에 '나타나는' 사고가 남
- **올라가는 링크**(도착점이 시작점보다 `linkStainHeight`(기본 0.3m) 이상 높음)를 건널 때는 링크 시작점(바닥)에 얼룩을 남김 — 높은 곳으로 사라진 인형의 위치 단서

#### 얼룩 규칙 — "언제 남길지"

**생성 자체는 [`DollStainSpawner`](#324-dollstainspawnercs--얼룩-생성)가 담당하고, DollMove는 남길지 말지만 판단합니다.** 얼룩은 "이동할 때마다 무조건"이 아니라 세 가지 예외를 둡니다:

| 규칙 | 이유 |
|---|---|
| **첫 이동은 얼룩 없음** (`leftStartSpot`/`stainlessJourney`) | 인트로에서 보여준 '원래 방 상태'가 시작 몇 초 만에 어질러지면 안 됨. 첫 이동 경로 위의 링크 얼룩도 생략 |
| **높은 은신처(`stainMaxHeight`, 기본 1.3m 이상)를 떠날 땐 얼룩 없음** | 눈높이 위 얼룩은 보이지도 치우지도 못함. 위치 단서는 올라갈 때 링크 발치에 남긴 얼룩이 대신함 |
| **오르막 링크 통과 시 발치에 얼룩** (`linkStainHeight`) | 위 규칙으로 사라진 단서를 바닥에 보존 |

#### 이동권(Move Token) 시스템

인형이 무한정 돌아다니지 못하게 하는 자원 시스템.

- 목적지 도착 → **`Hiding` 상태** 진입 (`UpdateHidingState` + `HasReachedDestination`)
- `Hiding` 상태로 `tokenChargeInterval`(기본 5초)이 지날 때마다 이동권 +1 (최대 `maxMoveTokens`, 기본 3, 시작값 `startingMoveTokens` 기본 0)
- **시작 유예** — `startGraceTime`(기본 10초) 동안은 첫 충전을 미룸 (시작하자마자 얼룩이 쏟아지는 것 방지. 인트로 동안은 frozen이라 유예는 실제 게임 시작부터 흐름)
- 이동은 `TryMove()`를 통해서만 실행되며 **이동권 1개를 소모**
- `TryMove`는 **목적지 설정(`SetTarget`)이 성공한 뒤에만** 이동권·얼룩을 소모 — 순서를 바꾸면 NavMesh가 없는 목적지에서 제자리 얼룩만 반복 생성됨

#### 이동 결정 — `HandleMoveDecision()`

플레이어와의 거리(`escapeDistance`, 기본 5m)로 두 가지 모드:

- **플레이어 접근 시** (재진입마다 1회만 판정, `isEscaping` 플래그):
  기본은 들키지 않게 제자리 유지. 단 `nearMoveChance`(기본 20%) 확률을 뚫으면 **가장 먼 웨이포인트로 과감하게 도망** (`selector.FindFarthest`)
- **플레이어가 멀 때**: `Hiding` + 이동권 보유 시 **가중치 랜덤**으로 다음 은신처 이동 (`selector.PickWeightedRandom`)
- 후보가 없을 때는 **점유 때문에 정말 갈 곳이 없는 경우에만** 5초 간격 경고 — 모든 자리가 시야에 노출돼 못 가는 것은 정상적인 대기라서 경고하지 않음

#### 목적지 설정 — `SetTarget(index)`

1. `NavMesh.SamplePosition`으로 **웨이포인트에 실제 NavMesh가 깔려 있는지 검증** (좁은 허용 반경). 검증 없이 넘기면 선반 위 좌표가 아래 바닥으로 스냅되어 "인형이 가구를 통과해 들어가는" 현상이 생김. 실패 시 경고
2. `agent.CalculatePath`로 **경로를 동기 계산** — `SetDestination`은 경로를 다음 프레임에 계산해 바로 검사할 수 없음
3. `IsPathAheadInDanger`로 출발 구간 검사 — 노출되면 **조용히 실패**(에러가 아니라 '눈치보기'). 다음 프레임에 다른 웨이포인트로 재시도됨
4. 통과하면 `agent.SetPath`로 계산해 둔 경로를 그대로 적용

실패 시 현재 목적지·자리 예약 상태를 전혀 건드리지 않습니다.

#### 포획 — `OnCaught()`

`PlayerInteraction`이 호출. `Destroy(gameObject)`만 수행하고, 웨이포인트 예약 해제는 `OnDestroy`에서 처리됨.

> 인스펙터 필드별 의미와 기본값은 코드의 `[Tooltip]` 참고. `wayPoints`만 필수 할당이고 나머지는 자동 탐색/기본값으로 동작.
> `escapeDistance`는 DollMove, `sightDistance`/`viewAngle`은 DollVision에 있으며, 방 크기에 비례하는 값이라 [MapSpec](#322-mapspeccs--맵-규격-자동화-에디터-전용)이 일괄 조정한다 (DollManiaHouse 기준 6m / 27m / 79°).

---

### 3.2 PlayerMove.cs — 플레이어 이동

`IReplayPausable` 구현 (리플레이 중 입력 차단). ⚠️ **점프는 제거되었습니다** (과거 Rigidbody AddForce 방식 점프가 있었으나 삭제 — Input System에 Jump 액션 바인딩 불필요).

- **이동**: Input System `OnMove` 콜백으로 입력(Vector2)을 받아 `Update`에서 `transform.Translate`로 이동 (기본 5 m/s — DollManiaHouse에서는 MapSpec이 눈높이 비례값 약 2.0 m/s로 조정). 로컬 좌표 기준이라 몸통이 회전하면 이동 방향도 따라감
- **회전**: Cinemachine `PanTilt`의 좌우(Pan) 값을 매 프레임 몸통 `rotation`에 동기화 → 카메라가 보는 방향 = 캐릭터 정면
- **마우스 감도**: 감도의 원본은 [`GameManager.MouseSensitivity`](#310-gamemanagercs--씬-간-데이터-전달--전역-설정-dontdestroyonload-싱글톤) (전역 static).
  - `Start`에서 현재 전역 감도를 읽어 적용하고, `OnEnable/OnDisable`에서 `GameManager.OnSensitivityChanged` 이벤트를 구독/해제 — **설정 슬라이더를 움직이면 플레이 중에도 즉시 반영**
  - `CacheBaseGains()`로 Cinemachine 입력 축(Pan/Tilt)의 원래 Gain을 보관해 두고(축마다 부호가 다름 — Pan +, Tilt −), 감도 배율을 곱해 적용 (`ApplySensitivity`)
  - 인스펙터 변경도 `OnValidate`로 즉시 반영
- `Start`에서 커서를 숨김(`Cursor.visible = false`)

---

### 3.3 PlayerInteraction.cs — 상호작용

`IReplayPausable` 구현.

#### 조준 판별 (`Update`)

매 프레임 **화면 정중앙**(`ViewportPointToRay(0.5, 0.5)`)에서 **6m** Raycast.
`Interactable` 레이어만 검사하며(`LayerMask.GetMask("Interactable")`), 트리거 콜라이더도 포함(`QueryTriggerInteraction.Collide`).

맞은 오브젝트의 **태그**로 상태 분류:

| 태그 | 상태 | 프롬프트 |
|---|---|---|
| `Stain` | 얼룩 조준 중 | "Clean [E]" |
| `Prop` | 오브젝트 조준 중 | "Catch [E]" |
| (그 외/없음) | None | 숨김 |

상태가 **바뀔 때만** `GameHUD`의 프롬프트 UI를 갱신합니다 (매 프레임 UI 갱신 방지).
프롬프트 문구("Clean [E]" / "Catch [E]")는 **PlayerInteraction 쪽에서 결정**해 `GameHUD.Instance.ShowPrompt(문구)`로 넘기며, HUD는 받은 문구를 그대로 표시만 합니다.

#### 상호작용 (`OnInteraction`) — Input System 콜백 (E 키)

- **Stain**: `GameProgressManager.CleanStain()` 호출 → `ReplayRecorder.RecordStainClean()`(Destroy 전) → 오브젝트 파괴
- **Prop**: `TryCatch()` — 조준한 Prop의 부모까지 뒤져 `DollMove` 컴포넌트 확인 (`GetComponentInParent`)
  - 있으면 → 진짜 인형! `RecordDollCaught()`(파괴 전) + `doll.OnCaught()` + `GameProgressManager.CatchDoll()`
  - 없으면 → **오답.** `catchCooldown`(기본 2초) 동안 잡기 시도 불가 (`lastWrongGuessTime`으로 판정)

> 인형이 일반 오브젝트(Prop)로 위장하는 게임 컨셉이라, "Prop처럼 보이지만 실제론 DollMove가 붙어 있는가"가 정답 판별 기준입니다.

---

### 3.4 CameraManagement.cs — 카메라 연출

`IReplayPausable` 구현. Cinemachine의 **Priority 숫자 비교**로 활성 카메라를 전환합니다 (높은 쪽이 활성). `CinemachineBrain`이 하나뿐이라 카메라를 갈아끼우지 않고 **우선순위 경쟁**으로 전환합니다.

카메라는 4대, 우선순위는 이름 있는 상수로 관리:

| 카메라 | 필드 | 상수 | 값 | 용도 |
|---|---|---|---|---|
| 전경(달리) | `overviewCam` | `OverviewPriority` | 20 | 인트로 한 바퀴 |
| 플레이어(FreeLook) | `playerCam` | `PlayerIdlePriority` / `PlayerActivePriority` | 10 → 30 | 1인칭 플레이 |
| CCTV | `ReplayCam` | `ReplayPriority` | 50 | 리플레이 시점 |
| 문 열림 연출 | `doorCam` | `DoorOpenPriority` | 1 (열릴 때 70) | 승리 시 문 클로즈업 |

```
Awake:  overviewCam=20, playerCam=10, doorCam=1  → 전경 카메라 활성
        (리플레이 모드면 ReplayCam=50            → CCTV 카메라가 전부 이김)
Start:  SetGameplayFrozen(true) — 인형·플레이어 입력 완전 정지
        코루틴 시작 → introDuration(20초) 대기
이후:   ScreenFader.FadeOut (화면을 검게 덮음, 기본 0.4초)
        → 기본 블렌드를 잠시 Cut으로 바꾸고 playerCam=30 (즉시 컷 전환)
        → ScreenFader.FadeIn (화면 걷어냄)
        → SetGameplayFrozen(false) — 플레이어 시점이 완전히 보이는 순간부터 게임 시작
```

- **블렌드 대신 컷 전환을 쓰는 이유**: 전경 카메라와 플레이어 카메라의 위치 차이가 커서 블렌드로 이어 붙이면 멀미를 유발하기 때문. 화면이 검게 가려진 사이에 `CinemachineBrain`의 `DefaultBlend`를 잠시 `Cut`으로 바꿔 전환하고, 한 프레임 뒤 원래 블렌드 설정으로 복원
- `fader`(ScreenFader)를 **비워두면 페이드 없이 즉시 컷 전환** — 페이드는 선택 사항
- **인트로 게임플레이 잠금 — `SetGameplayFrozen(bool)`**: 인형은 [`DollMove.SetFrozen`](#31-dollmovecs--인형-ai)(컴포넌트를 끄면 시작 배치 Warp가 안 돌기 때문), 플레이어는 `PlayerMove`/`PlayerInteraction` 컴포넌트 비활성화, 그리고 **`CinemachineInputAxisController`도 함께 잠가** 인트로 중 마우스 입력이 플레이어 카메라 각도를 미리 돌려놓지 않게 함
- **문 열림 연출**: `doorCam`은 평소 우선순위 1로 잠자다가, [`DoorController`](#37-doorcontrollercs--문-열림-연출)가 문을 열 때 70으로 올려 문을 비추고 1초 뒤 다시 1로 내림 (리플레이 모드에서는 CCTV 시점 유지를 위해 올리지 않음)
- 인트로 시간은 인스펙터의 `introDuration`으로 조절. 전경 카메라의 "한 바퀴 회전" 자체는 Cinemachine 카메라(달리) 설정으로 처리하며 이 스크립트는 전환 타이밍만 담당

#### 리플레이 모드 — CCTV 카메라 전환

- `Awake`에서 `GameManager.isReplayMode`를 확인해 리플레이 모드면 `ReplayCam.Priority = 50` — 다른 어떤 카메라(최대 70)보다 낮아 보일 수 있으나, 리플레이 중에는 문 열림 연출에서 doorCam을 올리지 않으므로 CCTV 시점이 유지됨
- `Start`도 리플레이 모드면 즉시 반환 (인트로 코루틴·게임플레이 잠금을 건드리지 않음). 이 컴포넌트 자체도 `IReplayPausable`이라 [`ReplayManager`](#319-replaymanagercs--cctv-리플레이-재생기)가 `Awake`에서 꺼버림 — Priority 설정은 Awake에서 이미 끝나 있어 문제없음

---

### 3.5 GameProgressManager.cs — 진행도 · 승패 · 게임 종료 · 일시정지 (싱글톤)

`GameProgressManager.Instance`로 접근하는 게임 흐름의 중심. `IReplayPausable` 구현. UI 표시는 담당하지 않고, **진행도가 바뀌면 이벤트로 [`GameHUD`](#36-gamehudcs--게임-hud--리플레이-컨트롤-싱글톤)에 알립니다.**

```csharp
public event Action OnProgressChanged;   // 카운트가 바뀔 때마다
public event Action OnDoorOpened;        // 승리 조건 달성, 문 열림
```

#### 진행도 상태 (프로퍼티, `private set`)

| 프로퍼티 | 초기화 | 증가 시점 |
|---|---|---|
| `TotalStains` | 시작 시 `Stain` 태그 오브젝트 수 | 인형이 얼룩을 남길 때 ([`DollStainSpawner.OnStainSpawned`](#324-dollstainspawnercs--얼룩-생성) 구독 → `AddStain`) |
| `CleanedStains` | 0 | 플레이어가 청소할 때 (`CleanStain`) |
| `TotalDolls` | 시작 시 씬의 `DollMove` 수 | (고정) |
| `CleanedDolls` | 0 | 인형을 잡을 때 (`CatchDoll`) |
| `PlayTime` | 0 | `Update`에서 매 프레임 `Time.deltaTime` 누적 |

- `RemainingStains => TotalStains - CleanedStains`
- `IsWarning => RemainingStains >= warningThreshold`(기본 **5**)
- **인형이 얼룩을 남기면 전체 목표(`TotalStains`)가 늘어나는 것**이 핵심 — "어지르는 만큼 치울 게 늘어난다"는 기획의 코드 구현
- 얼룩 생성은 인형이 직접 호출하지 않고 **`OnEnable`/`OnDisable`에서 `DollStainSpawner.OnStainSpawned`를 구독**해 받습니다 (static 이벤트라 해제 필수 — [§3.24](#324-dollstainspawnercs--얼룩-생성) 참고)

#### 승리 — `CheckVictory` → `TriggerVictory`

`CleanStain`/`CatchDoll` 시마다 판정: **모든 얼룩 청소 + 모든 인형 포획**이면 승리.

- 게임을 멈추지 않고 `door.Open()`으로 문을 열어 **플레이어가 직접 걸어 나가게 함**
- 문을 열 때 `ReplayRecorder.RecordDoorOpen()`으로 리플레이에도 문 열림 기록
- `OnDoorOpened` 이벤트 발행 → `GameHUD`가 "문이 열렸다" 안내 문구 표시
- `isDoorOpened` 플래그로 중복 실행 방지
- 승리 **확정**은 문이 아니라 [ExitZone](#38-exitzonecs--탈출-구역) 진입 시점

#### 패배 — `TriggerDefeat`

`AddStain` 시마다 판정: 남은 얼룩(`RemainingStains`)이 `defeatThreshold`(기본 **7**) 이상이면 패배.
`Time.timeScale = 0`으로 조작을 막고 곧바로 `EndGame(false)` 호출.

#### 게임 종료 — `EndGame(bool isClear)` → `GoToResult()`

승리(ExitZone)와 패배(TriggerDefeat)가 **모두 합류하는 단일 진입점.**

1. `isGameEnded` 가드로 1회만 실행, `IsClear`에 승/패 확정
2. **`GameManager`에 결과 저장** — `isClear` / `totalStains` / `cleanedStains` / `playTime`
3. `ReplayRecorder.FinishRecording()` 호출 (녹화기가 있으면)
4. `EndGameRoutine` 코루틴: `endFader`(흰색 ScreenFader)로 화면을 덮음 (`endFadeDuration`, 기본 1.5초. 페이더가 없으면 즉시 5번으로)
5. `GoToResult()` — `Time.timeScale = 1f` 복원 + 커서 표시 후 **`ResultPopup` 씬 로드**

> ⚠️ `EndGame`과 재시작 버튼은 `GameManager.Instance`를 null 체크 없이 사용합니다. **게임 씬을 단독 실행하면**
> (MainScene을 거치지 않아 GameManager가 없음) 게임 종료 시점에 NullReferenceException이 발생합니다. 테스트도 MainScene부터 시작하세요.

#### 일시정지 — `Pause()` (ESC 키)

`Update`에서 `Keyboard.current.escapeKey`를 감지해 `Pause()` 토글 (이때 열려 있던 설정 패널(`settingPanel`)은 항상 닫음):

- `pauseMenu` 활성/비활성, `Time.timeScale` 0↔1, 커서 표시/잠금 전환, **설정 버튼(`setBtn`)도 메뉴와 함께 토글**
- 일시정지 메뉴 버튼 3종은 `Awake`에서 리스너 연결:
  - **계속하기**(`btn_continue`) — 메뉴 닫고 재개
  - **재시작**(`btn_retry`) — `Time.timeScale=1` 후 **`GameManager.sceneName`에 기록된 씬** 재로드 (과거 `"Prototype"` 하드코딩이었으나 해결 — 결과 화면 다시하기와 같은 경로)
  - **메인**(`btn_mainExit`) — `"MainScene"` 로드
- 설정 패널의 실제 동작(볼륨/감도 슬라이더)은 [`SettingsManager`](#321-settingsmanagercs--인게임-설정-패널) 담당
- ⚠️ `isPaused`/`isActive`는 **static** 필드 — 씬 재로드로 초기화되지 않으므로 상태 꼬임에 주의

---

### 3.6 GameHUD.cs — 게임 HUD + 리플레이 컨트롤 (싱글톤)

`GameHUD.Instance`로 접근. `IReplayPausable` 구현. **표시 전담** — 게임 상태는 [`GameProgressManager`](#35-gameprogressmanagercs--진행도--승패--게임-종료--일시정지-싱글톤)의 이벤트를 구독해 갱신합니다.

#### 진행도 표시

- `Start`에서 `GameProgressManager.OnProgressChanged += Refresh`, `OnDoorOpened += ShowDoorOpenedMessage` 구독 (`OnDestroy`에서 해제)
- `Refresh()` — 얼룩/인형 카운트를 `"0 / 10"` 형식으로 갱신, `IsWarning`이면 경고 문구(`warningMessage`) 표시
- `ShowDoorOpenedMessage()` — "문이 열렸다" 문구 표시 (평소엔 꺼둠)

#### 프롬프트

- `ShowPrompt(문구)` / `HidePrompt()` — [`PlayerInteraction`](#33-playerinteractioncs--상호작용)이 전달한 문구를 그대로 표시. 문구 결정은 호출하는 쪽의 몫

#### 리플레이 모드 (Awake)

`GameManager.isReplayMode`가 켜져 있으면 `Awake`에서:

- 인형/얼룩 카운트 텍스트를 숨김 (리플레이에는 진행도 UI가 무의미)
- **리플레이 컨트롤 버튼**을 켜고 리스너 연결:
  - **Exit 버튼**(`replayExitButton`) → `ResultPopup` 씬 로드 (리플레이 → 결과 화면 복귀의 유일한 통로)
  - **배속 버튼**(`replay1XButton`/`2X`/`4X`) → `ReplayManager.playbackSpeed`를 1/2/4로 설정
- `ReplayManager`를 찾아 **`OnReplayFinished` 이벤트 구독** → 재생이 끝나면 `ShowReplayFinishedText()`:
  - `replayFinishedFader`(ScreenFader)로 화면을 `replayFinishedDimAlpha`(기본 0.85)까지 **딤 처리** — GameHUD 자신은 리플레이 중 비활성 상태라 코루틴을 돌릴 수 없으므로 `ScreenFader.PlayFadeTo`(페이더가 스스로 재생)를 사용
  - 종료 문구(`replayFinishedText`) 표시
- 이후 `ReplayManager`가 이 컴포넌트를 `IReplayPausable`로 꺼서 Start/Update는 돌지 않지만, **버튼 리스너·이벤트 콜백은 컴포넌트 활성 여부와 무관하게 동작**하므로 살아 있음
- ⚠️ Exit 버튼은 `isReplayMode`를 해제하지 않음 — 해제는 결과 화면의 다시하기/나가기 버튼 담당 ([§3.14](#314-resultpopupuics--결과-팝업-resultpopup-씬) 참고). 그래서 결과 화면에서 CCTV를 다시 누르면 리플레이를 또 볼 수 있음

---

### 3.7 DoorController.cs — 문 열림 연출

승리 시 `GameProgressManager`가 `Open()`을 호출하면:

1. (일반 모드) `camManager.doorCam.Priority = 70`으로 올려 문 클로즈업 연출 시작 — 리플레이 모드면 CCTV 시점 유지를 위해 올리지 않음
2. `openOffset`(기본 `(-3, 0, 0)`) 방향으로 `openDuration`(기본 2초) 동안 **EaseInOut 슬라이드**
   - 오프셋은 문의 로컬 회전 기준이라 문이 어느 방향을 보고 있어도 "옆으로" 밀림
   - 문이 완전히 비켜나려면 거리가 문의 가로 폭(Scale.x) 이상이어야 함 (MapSpec이 문 폭 + 0.1m로 자동 설정)
3. 다 열리면 **콜라이더 비활성화** (통과 보장) + `exitZone` 활성화
4. 1초 뒤 `doorCam` 우선순위를 1로 되돌려 플레이어 시점 복귀

`Awake`에서 `exitZone`을 미리 꺼두므로, **문이 열리기 전에는 탈출(승리 확정)이 불가능**합니다. `isOpening` 가드로 중복 호출 방지.

---

### 3.8 ExitZone.cs — 탈출 구역

열린 문 너머의 "빛나는 흰색 구역"에 붙는 트리거. `Player` 태그가 `OnTriggerEnter`로 들어오면 `GameProgressManager.EndGame(true)` 호출 — 여기가 **승리 확정 지점**입니다.

- **리플레이 모드에서는 승리 처리하지 않음** — 스냅샷이 플레이어를 이 구역으로 옮겨도 게임이 끝나면 안 되므로 `GameManager.isReplayMode`면 즉시 반환
- `GameProgressManager.Instance`가 없으면(단독 실행 등) 반환, `isExited` 가드로 1회만 발동
- 흰 화면 연출/결과 전환은 직접 하지 않고 전부 `EndGame`에 위임
- 문밖 바닥 없음(진입 후 낙하) 문제는 **DollManiaHouse에서는 MapSpec이 문 밖 바닥(`OutsideFloor`)을 생성해 해결** (prototype 씬은 여전히 주의)

---

### 3.9 ScreenFader.cs — 공용 화면 페이드

화면 전체를 덮는 Image(+`CanvasGroup`)에 붙이는 재사용 컴포넌트. **덮는 색은 Image의 색상이 결정**하므로 스크립트 하나로 여러 용도를 커버합니다.

현재 게임 씬에서의 사용처:

| 사용처 | 색 | 메서드 |
|---|---|---|
| `CameraManagement.fader` — 인트로 → 플레이어 카메라 컷 전환 시 화면 가림 | 검은색 | `FadeOut`/`FadeIn` |
| `GameProgressManager.endFader` — 승/패 공용 종료 연출 | 흰색 | `FadeOut` |
| `GameHUD.replayFinishedFader` — 리플레이 재생 종료 시 화면 딤 | 검은색 | `PlayFadeTo` |

- `FadeOut(duration)` — alpha 0 → 1 (화면 덮기), `FadeIn(duration)` — alpha 1 → 0 (걷어내기). 둘 다 **코루틴으로 반환**되어 호출 측이 `yield return`으로 완료를 기다린 뒤 다음 동작(씬 전환 등)을 이어감
- `FadeTo(targetAlpha, duration)` — 현재 알파에서 지정 농도까지만 덮는 **딤 연출용.** 완전히 가리는 게 아니므로 `blocksRaycasts`를 건드리지 않음 (위에 있는 버튼이 계속 눌려야 함)
- `PlayFadeTo(...)` — 호출자가 코루틴을 돌릴 수 없는 상황(컴포넌트 비활성 등)을 위해 **페이더가 스스로 재생**해주는 버전
- 덮인 동안만 `blocksRaycasts` 활성화(`FadeOut`/`FadeIn` 경로), `Time.unscaledDeltaTime` 사용 (패배로 게임이 멈춘 상태에서도 진행)
- 페이드 시간은 필드가 아니라 **호출자가 인자로 전달** (`CameraManagement.fadeDuration` 0.4초, `GameProgressManager.endFadeDuration` 1.5초)
- ⚠️ **오브젝트가 켜진 상태로 씬이 시작되어야 함** — 꺼져 있으면 `Awake`(CanvasGroup 캐싱)가 안 돌아 첫 페이드에서 NullReferenceException 발생 (알려진 이슈)

---

### 3.10 GameManager.cs — 씬 간 데이터 전달 + 전역 설정 (DontDestroyOnLoad 싱글톤)

MainScene에 배치되며, `Awake`에서 `DontDestroyOnLoad`로 **씬이 바뀌어도 파괴되지 않는** 싱글톤이 됩니다. 역할은 두 가지 — ① 게임 본편의 결과를 결과 씬으로 실어 나르기, ② **볼륨·마우스 감도 전역 설정 관리.**

#### ① 씬 간 결과 데이터

| 필드 | 쓰는 쪽 | 읽는 쪽 |
|---|---|---|
| `isClear` | `GameProgressManager.EndGame` | `ResultPopupUI` (승/패 제목·도장) |
| `playTime` | `GameProgressManager.EndGame` | `ResultPopupUI` (플레이 시간) |
| `totalStains` / `cleanedStains` | `GameProgressManager.EndGame` | `ResultPopupUI` (얼룩 통계) |
| `sceneName` | `StageTransition.LoadScene` (스테이지 진입 시) | `ResultPopupUI` 다시하기 / 일시정지 재시작 / CCTV (같은 씬 재로드) |
| `replayData` | `ReplayRecorder.FinishRecording` | `ReplayManager` (리플레이 재생) |
| `isReplayMode` | `ResultPopupUI` CCTV 버튼(켬) / 다시하기·나가기·`FinishRecording`(끔) | `ReplayManager.Awake`, `CameraManagement`, `GameHUD.Awake`, `DoorController`, `ExitZone` |

- `ResetGameData()` — 결과 데이터 초기화 (현재 호출하는 곳 없음. `isReplayMode`는 초기화하지 않음)

#### ② 전역 설정 (볼륨 · 마우스 감도)

```csharp
public static float MouseSensitivity { get; private set; } = 1f;
public static event Action<float> OnSensitivityChanged;
```

- **마우스 감도** — `SetMouseSensitivity(value)` static 메서드가 유일한 변경 창구: 값 저장(`PlayerPrefs "MouseSensitivity"`) + `OnSensitivityChanged` 이벤트 방송 → [`PlayerMove`](#32-playermovecs--플레이어-이동)가 구독해 Cinemachine Gain에 즉시 반영
- **볼륨** — 슬라이더 0~1 값을 데시벨(-80~0)로 로그 변환해 `AudioMixer`의 `"MasterVolume"` 파라미터에 적용 (선형으로 넣으면 체감이 이상함). `PlayerPrefs "MasterVolume"`으로 저장
- `Start`에서 저장값을 불러와(감도 기본 2.5, 볼륨 기본 0.75) **MainScene 설정 팝업의 슬라이더**(`volumeSlider`/`sensitivitySlider`)를 초기화하고 리스너 연결
- 게임 씬 쪽 설정 패널은 [`SettingsManager`](#321-settingsmanagercs--인게임-설정-패널)가 같은 창구를 통해 조작

> ⚠️ MainScene에만 존재하므로 **다른 씬을 단독 실행하면 `GameManager.Instance`가 null** — 게임 종료·결과 표시·재시작에서 NRE 발생. 항상 MainScene부터 실행할 것.

---

### 3.11 MainMenuManager.cs — 메인 메뉴

MainScene의 메뉴 UI 관리. 버튼 `OnClick`에 인스펙터로 연결해 사용합니다.

- 팝업 패널 3종 (**스테이지 선택 / 조작 방법 / 설정**) — `Toggle○○Panel()`로 열고 닫기, `Close○○Panel()`로 닫기
- `Start`에서 모든 팝업을 꺼진 상태로 초기화
- `QuitGame()` — 빌드에서는 `Application.Quit()`, 에디터에서는 플레이 모드 중지

실제 스테이지 진입은 스테이지 선택 팝업의 버튼에 연결된 [`StageTransition`](#313-stagetransitioncs--스테이지-씬-로드)가 담당하며, 스테이지 카드 UI는 [`Carousel`](#312-carouselcs--스테이지-선택-캐러셀)이 회전시킵니다.

---

### 3.12 Carousel.cs — 스테이지 선택 캐러셀

스테이지 선택 팝업의 카드들을 **타원 궤도로 회전시키는 드래그 UI**. 카드 오브젝트들을 자식으로 두면 자동으로 배치합니다.

- `Start`(`InitializeCards`) — 자식 RectTransform들을 수집, 각 카드에 `CanvasGroup`이 없으면 자동 추가, 카드 수로 각도 간격(`angleStep`) 계산
- **드래그** (`IDragHandler` 등) — 좌우 드래그량을 각도로 축적(`dragSensitivity`), 놓으면 마지막 속도를 관성으로 이어감(`damping` 감쇄) 후 가장 가까운 카드로 **스냅**(`snapSpeed`)
- `UpdateCardPositions` — 각 카드의 위치(가로 반지름 `radiusX`), 크기(`minScale`~`maxScale`), 투명도(`minAlpha`~`maxAlpha`)를 각도의 `cos`값(깊이감)으로 보간하고, `SetSiblingIndex`로 뒤 카드가 먼저 그려지게 렌더 순서 정렬
- 스테이지 진입 자체는 각 카드 버튼에 연결된 [`StageTransition`](#313-stagetransitioncs--스테이지-씬-로드)가 담당

---

### 3.13 StageTransition.cs — 스테이지 씬 로드

스테이지 선택 버튼의 `OnClick`에 연결. `LoadScene(씬이름)` 하나만 제공합니다:

1. `GameManager.sceneName`에 씬 이름 기록 — 결과 화면의 **"다시하기"·일시정지 "재시작"·CCTV가 어느 씬을 재로드할지** 여기서 정해짐
2. `SceneManager.LoadScene`으로 해당 씬 로드

현재 MainScene의 스테이지 카드는 `"DollManiaHouse"`(인형매니아 하우스)와 `"prototype"` 두 씬에 연결되어 있습니다.

---

### 3.14 ResultPopupUI.cs — 결과 팝업 (ResultPopup 씬)

게임 종료 후 로드되는 결과 씬의 UI. `Start`에서 `GameManager`의 결과 데이터를 읽어 곧바로 연출을 시작합니다.

#### 연출 순서 (`ShowResultRoutine`)

```
Time.timeScale = 0 (배경 정지, 연출은 전부 unscaled 시간 사용)
1. 배경 딤(dimmed) 페이드인 (0.25초)
2. 패널 팝 스케일 (0.8 → 1.05 → 1.0)
3. 통계 숫자 카운트업 (EaseOutCubic):
   총 플레이 시간 → 인형 이동 횟수 → 지운 얼룩 수 → 남은 정리 대상 수
4. 승/패 도장 연출 (EaseOutBack 스탬프: Clear / Game Over) — 남은 수 색상도 초록/빨강으로
5. 버튼 활성화
```

#### 버튼

| 버튼 | 동작 |
|---|---|
| **다시하기** | `timeScale` 복원 + `isReplayMode` 해제 후 `GameManager.sceneName`에 기록된 스테이지 씬 재로드 |
| **나가기** | `timeScale` 복원 + `isReplayMode` 해제 후 MainScene 로드 |
| **CCTV** | `replayData`가 있으면 `timeScale` 복원 + `isReplayMode = true`로 게임 씬(`sceneName`) 재로드 → 리플레이 모드 부팅 ([§3.19](#319-replaymanagercs--cctv-리플레이-재생기) 참고). 데이터가 없으면 무시 |

모든 통계는 **GameManager에 저장된 값만으로** 계산합니다 (남은 수 = `totalStains - cleanedStains`).
리플레이 모드에서는 게임 결과가 덮어써지지 않으므로, **CCTV를 보고 복귀해도 직전 판의 통계가 그대로 다시 표시**됩니다.

#### 알려진 이슈

- ⚠️ **인형 이동 횟수가 `totalStains - 3`으로 하드코딩** ([`ResultPopupUI.cs:41`](../Assets/Scripts/UI/ResultPopupUI.cs)) — "이동 횟수 = 늘어난 얼룩 수"라는 근사인데, 맵의 초기 얼룩 개수를 3으로 가정하고 있어 **초기 얼룩 수가 바뀌면 값이 틀어짐.** 게다가 지금은 첫 이동·높은 은신처 이동이 얼룩을 남기지 않아 근사 자체도 어긋남. 초기 얼룩 수·이동 횟수를 GameManager로 함께 넘기는 방식으로 개선 필요

---

### 3.15 DebugCheats.cs — 디버그 치트

테스트할 씬의 아무 오브젝트에나 붙여서 사용. `IReplayPausable` 구현. 시작 시 자동 실행(인스펙터 체크박스) 또는 플레이 중 컴포넌트 우클릭 컨텍스트 메뉴로 실행.

| 치트 | 동작 |
|---|---|
| **클리어 상태로 점프** (`instantClearOnStart`) | 모든 Stain/Doll을 `CleanStain`/`CatchDoll`을 호출하며 제거 → 실제 플레이와 같은 경로로 승리 판정 → 문 열림 |
| **패배 직전 상태로 점프** (`nearDefeatOnStart`) | `AddStain`으로 남은 얼룩을 `DefeatThreshold - 1`(기본 6)까지 올림. 이후 인형이 얼룩 1개만 만들면 패배 (실물 없는 가상 카운트) |

`GameProgressManager.Start()`의 목표치 집계가 끝난 뒤 실행되도록 **한 프레임 대기 후 동작** (`IEnumerator Start`). 둘 다 켜면 클리어만 실행됩니다.

---

### 3.16 ReplayData.cs — CCTV 리플레이 데이터 구조

리플레이 시스템은 영상이 아니라 **위치/이벤트 데이터를 기록해뒀다가 오브젝트를 다시 움직여 재현**하는 방식입니다. 이 파일은 그 기록의 데이터 구조만 담은 순수 C# 클래스 모음입니다 (MonoBehaviour 아님).

| 클래스 | 역할 |
|---|---|
| `ReplaySnapshot` | 일정 간격마다 찍는 위치 기록 1장 — 시각, 플레이어 위치/yaw, 인형별 위치/yaw/생존 여부 배열 |
| `ReplayEvent` | 순간 사건 1건 — 시각, 대상 ID, (생성 위치), 종류(`ReplayEventType`) |
| `ReplayData` | 한 판 전체 — 스냅샷 리스트 + 이벤트 리스트 + 총 길이 + 인형 수 + **`sampleInterval`(스냅샷 간격)** |

- `ReplayEventType`: `StainSpawn`(얼룩 생성) / `StainClean`(청소) / `DollCaught`(포획) / `WrongCaught`(오답, 아직 미기록) / `DoorOpen`(문 열림)
- 스냅샷의 **배열 인덱스가 곧 인형 ID** (ID 규칙은 [§3.17](#317-ireplaypausablecs--replayidentitycs--리플레이-공용-규칙) 참고)
- `sampleInterval`을 데이터에 함께 실어, 재생 쪽이 녹화와 같은 간격으로 인덱스를 계산하도록 함 (하드코딩 커플링 제거)
- `[Serializable]`이라 GameManager 인스펙터에서 녹화 내용을 확인 가능 (디버깅용)
- 디스크 저장 없이 메모리로만 존재 — `GameManager`(DontDestroyOnLoad)가 씬 사이를 실어 나름

---

### 3.17 IReplayPausable.cs · ReplayIdentity.cs — 리플레이 공용 규칙

리플레이 녹화/재생이 공유하는 두 가지 약속을 별도 파일로 뽑아, 규칙을 한 곳에서 관리합니다.

#### `IReplayPausable` — 리플레이 중 멈춰야 할 컴포넌트 표식 (marker interface)

- 내용 없는 마커 인터페이스. **리플레이 중 스스로 꺼져야 하는 게임 로직**이 이걸 구현합니다
- 구현체: `DollMove`, `DollLaugh`, `PlayerMove`, `PlayerInteraction`, `CameraManagement`, `GameProgressManager`, `GameHUD`, `ReplayRecorder`, `DebugCheats`
- [`ReplayManager`](#319-replaymanagercs--cctv-리플레이-재생기)가 `Awake`에서 씬의 모든 `IReplayPausable`을 찾아 `enabled = false` — 새 로직을 추가할 때 이 인터페이스만 구현하면 리플레이에서 자동으로 꺼짐 (끄는 대상을 일일이 나열하던 방식에서 개선)

#### `ReplayIdentity.SortById<T>` — ID 정렬 규칙

- 인형·얼룩 배열을 **이름 오름차순**으로 정렬하는 static 헬퍼
- 녹화(`ReplayRecorder`)와 재생(`ReplayManager`)이 **반드시 이 함수를 함께 써야** 양쪽 ID가 일치. 정렬 방식을 바꾸려면 이 파일 한 곳만 고치면 됨
- ⚠️ 인형/얼룩 **이름이 서로 유일해야 함** — 이름 정렬 순서가 곧 ID이므로 이름이 겹치면 리플레이가 어긋남

---

### 3.18 ReplayRecorder.cs — CCTV 리플레이 녹화기

게임 씬에 배치하는 싱글톤(`ReplayRecorder.Instance`). `IReplayPausable` 구현. 게임에 영향을 주지 않는 "덤" — 훅은 `Instance != null` 가드 또는 **이벤트 구독**(얼룩 생성)을 거치므로 Recorder가 없어도 게임은 정상 동작합니다.

#### 녹화 흐름

```
Awake : 싱글톤 등록, 빈 ReplayData 생성 + sampleInterval(0.1초) 저장
Start : 플레이어/인형/초기 얼룩 스캔 + ID 확정 (ReplayIdentity.SortById) → isRecording = true
Update: recordTime 누적, sampleInterval마다 RecordSnapshot()
        (프레임 드랍으로 샘플 시각을 지나쳐도 while 루프로 밀리지 않음)
게임 중: 각 스크립트가 Record○○ 호출 → 이벤트 기록
EndGame: FinishRecording() → GameManager.replayData 저장 + isReplayMode 해제 + 녹화 정지 + 로그
```

#### 이벤트 훅 위치 (기존 코드에 심어진 지점)

| 이벤트 | 호출 위치 | 주의점 |
|---|---|---|
| `RecordStainSpawn` | **[`DollStainSpawner.OnStainSpawned`](#324-dollstainspawnercs--얼룩-생성) 이벤트 구독** (`OnEnable`/`OnDisable`) | 호출이 아니라 구독 — 생성된 얼룩을 딕셔너리에 등록해야 나중에 청소 이벤트와 연결됨 |
| `RecordStainClean` | `PlayerInteraction.OnInteraction()` Stain 분기 | **`Destroy` 전에 호출** (파괴 후엔 ID 조회 불가) |
| `RecordDollCaught` | `PlayerInteraction.TryCatch()` 성공 분기 | **`OnCaught()` 전에 호출** (파괴 전이라 `IndexOf` 정상 동작) |
| `RecordDoorOpen` | `GameProgressManager.TriggerVictory()` | 승리 시 문 열림 — 재생 때 문을 다시 열기 위함 |
| `FinishRecording` | `GameProgressManager.EndGame()` | 결과 데이터 저장과 같은 지점. `isReplayMode`도 함께 해제 |

- 잡혀서 파괴된 인형은 스냅샷에서 `dollAlive = false`로만 기록 (유니티의 파괴 오브젝트 `== null` 판정 활용)
- 얼룩 오브젝트 → ID 역참조는 `Dictionary<GameObject, int>` 유지, 미등록 얼룩 청소는 경고 로그 후 무시
- ⚠️ 현재 **인트로 20초도 녹화됨** (인트로 중 게임플레이는 frozen이지만 Recorder는 돌고 있음) — 리플레이 앞부분이 지루해지므로, 추후 플레이어 카메라 전환 시점부터 녹화 시작하도록 개선 여지

---

### 3.19 ReplayManager.cs — CCTV 리플레이 재생기

게임 씬에 배치. 결과 화면의 CCTV 버튼이 `isReplayMode = true`로 씬을 재로드하면 동작합니다.

#### 부트스트랩 (Awake)

리플레이 모드가 아니면 스스로 `enabled = false` 하고 종료 (일반 게임에 무영향).
리플레이 모드면 **다른 스크립트들의 Start가 돌기 전에** 게임 로직을 전부 끕니다:

- 씬의 모든 `NavMeshAgent`를 `enabled = false` (인형이 스스로 움직이거나 Transform 직접 조작과 충돌하지 않도록)
- 씬의 모든 [`IReplayPausable`](#317-ireplaypausablecs--replayidentitycs--리플레이-공용-규칙) 컴포넌트를 `enabled = false` (인형 AI·웃음소리·플레이어 입력·녹화기·진행도/HUD·인트로 연출·치트 일괄 정지)
- 플레이어 Rigidbody → `isKinematic = true` (중력/충돌이 스냅샷 위치 적용에 간섭하지 않도록)

`enabled = false`(컴포넌트만 끔)를 쓰는 것이 핵심 — `SetActive(false)`를 쓰면 인형/플레이어가 화면에서 사라져 재생에 쓸 수 없습니다. (HUD의 Exit·배속 버튼 리스너는 `Awake`에서 이미 연결돼 컴포넌트가 꺼져도 동작 — [§3.6](#36-gamehudcs--게임-hud--리플레이-컨트롤-싱글톤) 참고)

#### 재생 준비 (Start)

Recorder와 같은 규칙(`ReplayIdentity.SortById`)으로 인형 리스트와 초기 얼룩 `Dictionary<int, GameObject>`(ID → 오브젝트, Recorder와 반대 방향)를 구성. `DoorController`도 찾아둠. `GameManager.replayData`를 받아 `totalDuration`을 확정하고, **배속을 1X로 리셋**(static이라 지난 리플레이의 배속이 남아있을 수 있음) 후 `isReplayPlaying = true`로 재생 시작.

#### 재생 루프 (Update)

매 프레임 `replayTime += Time.deltaTime * playbackSpeed`로 시계를 진행시키고(배속은 HUD의 1X/2X/4X 버튼이 조절), `totalDuration`에 도달하면 마지막 장면에서 정지합니다. 이후 두 단계를 수행:

**1) 스냅샷 보간 — `ApplySnapshots()`**

- 스냅샷이 `sampleInterval`(기본 0.1초) 간격으로 균일하다는 전제로, `replayTime / sampleInterval`의 내림 인덱스로 앞 스냅샷을 바로 찾음 (탐색 없음). `sampleInterval`은 `ReplayData`에서 읽어 녹화와 자동으로 같은 값을 씀
- 앞뒤 스냅샷 사이를 `Mathf.InverseLerp`로 비율을 구해 보간 — 위치는 `Vector3.Lerp`, 회전(yaw)은 `Mathf.LerpAngle`(360° 경계 처리)
- 플레이어와 모든 인형에 같은 방식 적용. 인형의 생존 여부는 3가지로 분기:
  - 둘 다 `dollAlive == false` → 이미 잡힌 인형, 건너뜀
  - 앞만 살아 있음 → 잡히는 순간. 앞 스냅샷 위치에 고정 (사라지는 것은 `DollCaught` 이벤트가 처리)
  - 둘 다 살아 있음 → 정상 보간

**2) 이벤트 발화 — `FireEvents()` → `HandleEvent()`**

`nextEventIndex`부터 시각이 지난 이벤트를 순서대로 소비:

| 이벤트 | 재현 방법 |
|---|---|
| `StainSpawn` | `stainPrefab`을 기록된 위치에 Instantiate + 딕셔너리에 ID 등록 |
| `StainClean` | ID로 얼룩을 찾아 `SetActive(false)` (재생 중 참조 유지를 위해 Destroy 대신 사용) |
| `DollCaught` | 해당 인형 `SetActive(false)` + 리스트 슬롯 null 처리 |
| `DoorOpen` | `DoorController.Open()` 호출 — 재생 중 문이 다시 열림 |

#### 재생 종료 / 복귀

- `replayTime`이 `totalDuration`에 도달하면 `isReplayPlaying = false` + **`OnReplayFinished` 이벤트 발행** → [`GameHUD`](#36-gamehudcs--게임-hud--리플레이-컨트롤-싱글톤)가 화면을 어둡게 딤 처리하고 종료 문구를 표시 (마지막 장면 위에 얹는 연출)
- 복귀는 화면의 **Exit 버튼**(`GameHUD.replayExitButton`)으로 — ResultPopup 씬을 재로드하며, 직전 판의 통계가 그대로 다시 표시됨 ([§3.14](#314-resultpopupuics--결과-팝업-resultpopup-씬) 참고)

#### 남은 작업

- ~~플레이어 더미~~ — **DollManiaHouse에서는 해결**: MapSpec이 플레이어에 머리+몸통 비주얼(`Visual/Head·Body`)을 부착해 CCTV에 보임 (prototype 씬은 여전히 몸통 없음)
- CCTV 오버레이 연출 (`● REC`, 타임스탬프 등)
- 재생 컨트롤 — 배속(1X/2X/4X)·종료 딤/문구는 구현됨. **진행 바(탐색)는 미구현**
- 인트로 20초 구간 스킵 — 녹화가 인트로부터 시작되어 리플레이 앞부분이 지루함 ([§3.18](#318-replayrecordercs--cctv-리플레이-녹화기) 참고)

---

### 3.20 DollLaugh.cs — 은신처 웃음소리 힌트

플레이어가 일정 거리 안으로 들어오면 웃음 효과음을 1회 재생하는 연출 컴포넌트. **이동/AI는 `DollMove`, 소리는 `DollLaugh`로 역할을 나눠** 같은 Doll 프리팹에 함께 부착합니다. `IReplayPausable` 구현 (리플레이 중 웃지 않음), `[RequireComponent(typeof(AudioSource))]`.

- `Start`에서 `playOnAwake = false`, `spatialBlend = 1`(3D) 강제 — **인형 위치에서 들리는 것 자체가 힌트**이므로 3D 사운드 고정. 플레이어는 `Player` 태그로 자동 탐색
- 매 프레임 **제곱 거리**로 판정 (Sqrt 생략 최적화):
  - `laughDistance`(기본 3.5m) 안으로 들어오면 `PlayOneShot(laughClip)` 1회 재생 후 잠금(`hasLaughed`)
  - `repeatable`이면 `rearmDistance`(기본 4.5m) **밖으로 나가야 재장전** — 두 거리를 다르게 두어 경계선에서 연속 재생되는 것을 방지 (`rearmDistance > laughDistance` 유지할 것)
  - `repeatable`을 끄면 한 판에 딱 한 번만 웃음
- `OnDrawGizmosSelected` — 씬 뷰에서 판정 반경을 노란색(웃음)/흰색(재장전) 와이어 스피어로 표시
  - ⚠️ 기즈모는 `laughDistance/2`·`rearmDistance/2` 반지름으로 그려짐 (거리 값을 지름으로 취급) — **실제 판정은 필드 값 그대로**이므로 씬 뷰 원보다 2배 먼 곳에서 웃기 시작함. 혼동 주의

---

### 3.21 SettingsManager.cs — 인게임 설정 패널

게임 씬의 **일시정지 메뉴 → 설정 패널**에 붙는 UI 스크립트. 실제 설정의 원본은 [`GameManager`](#310-gamemanagercs--씬-간-데이터-전달--전역-설정-dontdestroyonload-싱글톤)이며, 이 컴포넌트는 슬라이더를 그 창구에 연결하는 역할입니다.

- **마우스 감도** — 슬라이더 초기값을 PlayerPrefs가 아니라 **`GameManager.MouseSensitivity`에서 읽는 것이 동기화의 핵심** (메인 메뉴에서 바꾼 값이 그대로 보임). 변경 리스너도 `GameManager.SetMouseSensitivity`에 직접 연결 → 전역 방송(`OnSensitivityChanged`) → `PlayerMove` 즉시 반영
- **볼륨** — `AudioMixer "MasterVolume"` 데시벨 변환 + PlayerPrefs 저장 (GameManager와 같은 방식의 자체 구현)
- ⚠️ 자체 `MouseSensitivity` static 프로퍼티와 `SetSensitivity`/`ApplySensitivity` 메서드는 **어디에도 연결되어 있지 않은 잔재** — 감도 경로는 전부 GameManager를 거침. 정리 여지 있음

---

### 3.22 MapSpec.cs — 맵 규격 자동화 (에디터 전용)

`Assets/Editor/`에 있는 **에디터 전용 static 도구** (게임 오브젝트에 붙지 않음). 방 크기·플레이어 키를 기준으로 `DollManiaHouse` 씬의 수치를 일괄 적용합니다 — 방/벽/문 재구축, 플레이어 콜라이더·비주얼(머리+몸통), 카메라 FOV, 인형 프리팹 교체·수치 적용, 가구/프롭/웨이포인트/NavMeshLink 배치, 크기 정규화, NavMesh 베이크까지.

- **에디터에서**: `Tools > 맵 규격 적용` (현재 열린 씬), `Tools > 맵 규격 점검` (읽기 전용 진단)
- **CLI에서**: `-executeMethod MapSpec.ApplyBatch` 등 (씬을 열고 적용 후 저장 — `PlaceBatch`/`ScaleBatch`/`PlaceAndScaleBatch`/`BakeBatch` 세분화)
- 핵심 원칙: **사람 치수(키 1.45m·눈높이·속도)는 고정, 방에 비례하는 것은 `escapeDistance`/`sightDistance`뿐.** 맵 배율 `MapScale = 1.5` ('거인의 방' 구성)
- ⚠️ 인형 수치는 컴포넌트가 나뉘어 있어 **각각의 컴포넌트를 찾아 적용**한다 — `escapeDistance`는 `DollMove`, `sightDistance`/`viewAngle`은 [`DollVision`](#323-dollvisioncs--가시성-판정), `spawnYOffset`은 [`DollStainSpawner`](#324-dollstainspawnercs--얼룩-생성). 컴포넌트가 없으면 경고를 냄
- 수치 근거·NavMesh 설정·사용법 상세는 **[MapSetup.md](MapSetup.md)** 참고

---

### 3.23 DollVision.cs — 가시성 판정

[`DollMove`](#31-dollmovecs--인형-ai)와 같은 오브젝트에 붙는 **가시성 질의 전담** 컴포넌트 (`[RequireComponent]`로 강제). 스스로 도는 로직 없이 물어볼 때만 답하므로 `Update`도 `IReplayPausable`도 없습니다 — DollMove가 꺼지면 자연히 조용해집니다.

#### 핵심 API — `IsPointSeen(point, extraAngle, extraDistance)`

**임의의 점**이 플레이어 시야에 드는지 3단계로 판정. 하나라도 실패하면 "안 보임".

1. **거리**: `sightDistance`(기본 30m) `+ extraDistance`보다 멀면 안 보임
2. **시야각**: 카메라 정면과의 각도가 `viewAngle * 0.5`(기본 전체 70°) `+ extraAngle`을 벗어나면 안 보임
3. **가림 판정**: 카메라 → 해당 점으로 Raycast를 쏴서 `obstructionMask` 레이어의 벽/장애물에 먼저 맞으면 안 보임 (인형 자신이나 그 자식에 맞은 것은 가림으로 치지 않음 — 경로 위 미래 위치를 검사할 때 인형의 현재 몸이 가리는 것도 '숨겨짐'으로 치면 안 되므로)

**임의의 점을 받는 것이 설계의 핵심**입니다. 자기 위치 검사, 경로 위 미래 위치 검사, 은신처 후보 검사가 전부 이 함수 하나를 씁니다.

#### 위험 지대 — `IsInDangerZone()` / `IsPointInDangerZone(point)`

`stopMarginAngle`(기본 20°)·`stopMarginDistance`(기본 2m)를 여유분으로 넘겨 호출하는 래퍼. **실제 시야보다 넓게** 판정해 인형이 시야 가장자리 **바깥**에서 미리 멈추게 합니다.

- 마진을 키우면 안 들키지만 인형의 활동 반경이 줄어듦 — 플레이하며 조정하는 튜닝값
- ⚠️ 마우스를 순간적으로 휙 돌리면 어떤 마진으로도 막을 수 없음 (원리적 한계)
- 가림 Raycast는 마진과 무관하게 그대로 적용되므로 **벽 뒤에서는 계속 움직입니다**

---

### 3.24 DollStainSpawner.cs — 얼룩 생성

[`DollMove`](#31-dollmovecs--인형-ai)와 같은 오브젝트에 붙는 얼룩 **생성 전담** 컴포넌트 (`[RequireComponent]`). "언제 남길지"는 DollMove가 판단하고, 이쪽은 만들고 알리기만 합니다.

- `Spawn()` — 인형 발치(`spawnYOffset` 보정)에 생성 / `SpawnAt(위치)` — 지정 위치에 생성 (링크 얼룩용)
- `stainPrefab`이 비어 있으면 아무 일도 하지 않음

#### 이벤트 — `OnStainSpawned`

```csharp
public static event System.Action<GameObject> OnStainSpawned;
```

생성 후 이 **static 이벤트만 발행**하고 누가 듣는지는 모릅니다 (옵저버 패턴 — 과거에는 `ReplayRecorder`/`GameProgressManager`를 직접 호출했음). 현재 구독자는 둘:

| 구독자 | 처리 |
|---|---|
| [`GameProgressManager`](#35-gameprogressmanagercs--진행도--승패--게임-종료--일시정지-싱글톤) | `AddStain()` — 전체 목표 개수 증가 |
| [`ReplayRecorder`](#318-replayrecordercs--cctv-리플레이-녹화기) | `RecordStainSpawn()` — 리플레이 이벤트 기록 |

- 구독자가 없어도 게임은 정상 동작 (`Instance != null` 가드가 필요 없어짐)
- 리플레이 중에는 두 구독자가 `IReplayPausable`로 비활성화되며 `OnDisable`에서 자동 해제됨
- ⚠️ **static 이벤트라 반드시 `OnEnable`/`OnDisable` 쌍으로 구독·해제할 것** — 해제를 빠뜨리면 씬을 다시 로드했을 때 파괴된 옛 구독자가 남아 얼룩이 두 번 세어집니다

---

### 3.25 WaypointSelector.cs · WaypointRegistry.cs — 은신처 선택과 자리 예약

인형이 "어디로 갈지" 고르는 규칙과 "누가 어디를 찜했는지" 관리하는 두 파일. 둘 다 MonoBehaviour가 아닙니다.

#### `WaypointSelector` — 선택 점수 계산 (순수 계산 객체)

[`DollMove`](#31-dollmovecs--인형-ai)가 `Start`에서 웨이포인트 배열·플레이어·`DollVision`·후보 필터(`IsSelectable`)를 넘겨 생성합니다. 점유 규칙은 필터로 주입받으므로, 이 클래스는 **점수 계산에만 집중**합니다 (선택 규칙을 바꿔도 DollMove를 건드리지 않음).

| 메서드 | 용도 | 규칙 |
|---|---|---|
| `FindFarthest()` | 도망·시작 배치 | 플레이어에서 **가장 먼** 후보. **가시성은 따지지 않음** — 인트로 중에는 모든 자리가 노출로 판정돼 시작 배치 자체가 실패하기 때문 |
| `PickWeightedRandom()` | 순찰 | 거리 **제곱**을 가중치로 한 룰렛 선택. 멀수록 강하게 선호하되 가까운 구역도 낮은 확률로 뽑혀 동선이 다양해짐. **위험 지대에 있는 자리는 후보에서 제외** — 플레이어가 보고 있는 은신처로 뛰어들지 않음 |

둘 다 후보가 없으면 `-1`을 반환합니다.

#### `WaypointRegistry` — 인형 간 자리 예약 (static 클래스)

- **모든 인형이 공유하는 예약 목록.** 두 인형이 같은 은신처를 고르는 것을 방지 (`Occupy`/`Release`/`IsOccupied`/`Count`)
- **떠나온 자리는 '도착할 때까지' 예약 유지** (DollMove의 `previousTargetIndex`) — 즉시 반납하면 아직 그 자리에 서 있는 동안 다른 인형이 같은 곳을 목표로 잡아 겹침. 새 자리 도착 시 반납
- static이라 씬 재로드로 초기화되지 않는 문제는 **`RuntimeInitializeOnLoadMethod` + `SceneManager.sceneLoaded` 훅으로 플레이 시작·씬 로드마다 명시적으로 비워 해결** (과거 알려진 이슈였음)

> **"리플레이 후 인형이 이상하게 움직인다"는 제보가 오면 여기를 가장 먼저 의심하세요.**

---

## 4. 씬 설정 요구사항 (Setup Checklist)

### 공통

- **씬 4개를 Build Settings에 등록**: `MainScene`, `prototype` · `DollManiaHouse`(게임 본편 스테이지), `ResultPopup`
  - 코드가 이름 문자열로 직접 로드하는 것은 `"MainScene"`/`"ResultPopup"` — 이름 변경 금지
  - 게임 씬 이름은 하드코딩이 아니라 **스테이지 카드 버튼의 `StageTransition.LoadScene(이름)` 인자**로 정해지고 `GameManager.sceneName`에 기록됨 (다시하기/재시작/CCTV가 이 값을 재사용)
- **실행은 항상 MainScene부터** — GameManager(DontDestroyOnLoad)가 MainScene에만 있음

### MainScene

- **GameManager** 오브젝트 (`GameManager.cs`) — **`AudioMixer`("MasterVolume" 파라미터 노출) + 설정 팝업의 볼륨/감도 슬라이더 2개 연결 필수** (누락 시 Start에서 NRE)
- 메뉴 UI에 `MainMenuManager` — 스테이지 선택/조작법/설정 패널 3개 연결, 각 버튼 OnClick 연결
- 스테이지 선택 팝업에 `Carousel`(카드들을 자식으로) + 각 카드 버튼에 `StageTransition.LoadScene("DollManiaHouse")` / `LoadScene("prototype")` 연결

### 게임 본편 (prototype · DollManiaHouse)

- **태그**: `Player`(플레이어 — ExitZone 판정·DollLaugh 탐색에도 사용), `Stain`(얼룩), `Prop`(위장 오브젝트), `MainCamera`(플레이어 카메라)
- **레이어**: `Interactable` — 상호작용 대상(Stain, Prop)에 지정. 없으면 조준이 전혀 안 됨
- **NavMesh**: 바닥 NavMesh 베이크 필수 (DollMove가 `NavMeshAgent` 사용). 가구 윗면 은신처는 **NavMeshLink**로 바닥과 연결해야 인형이 오르내림 (DollManiaHouse는 MapSpec이 자동 생성)
- **웨이포인트**: 각 Doll의 `wayPoints` 배열에 은신처 Transform들 할당. **웨이포인트 자리에 NavMesh가 실제로 깔려 있어야 함** — 없으면 SetTarget이 경고를 내고 이동을 거부
- **Doll 프리팹**: `DollMove` + `DollVision` + `DollStainSpawner`(앞의 둘은 `[RequireComponent]`로 자동 부착 — `DollStainSpawner`의 `stainPrefab`에 얼룩 프리팹 할당 필수) + `DollLaugh`(AudioSource 자동 요구, `laughClip`에 웃음 효과음 할당)
- **Cinemachine**: `overviewCam`(전경) / `playerCam`(FreeLook) / `ReplayCam`(고정 CCTV) / `doorCam`(문 연출) 네 대를 `CameraManagement`에 할당, 플레이어 카메라엔 `CinemachinePanTilt` + `CinemachineInputAxisController` 필요
- **Input System**: `OnMove` / `OnInteraction`(E) 액션을 각 스크립트 콜백에 바인딩 (점프 액션은 제거됨)
- **문/탈출**:
  - `Door` 오브젝트에 `DoorController` — `Exit Zone` 필드에 탈출 구역, `Cam Manager` 필드에 CameraManagement 연결
  - 문 밖 흰색 구역에 `ExitZone` + **Is Trigger 켠** Collider (빛 표현은 Emission 머티리얼 + Light 권장), **문 밖 바닥**(낙하 방지 — DollManiaHouse는 MapSpec이 `OutsideFloor` 생성)
- **페이더** (각각 전체 화면 Image + `CanvasGroup` + `ScreenFader`, **오브젝트는 켠 채로** 둘 것):
  - 검은색 → `CameraManagement`의 `Fader`에 연결 (비워두면 페이드 없이 즉시 컷 전환)
  - 흰색 → `GameProgressManager`의 `End Fader`에 연결
  - 리플레이 종료 딤 → `GameHUD`의 `Replay Finished Fader`에 연결 (검은색 재사용 가능)
- **진행도 관리 (`GameProgressManager`)**: `Door`(DoorController), 흰색 `End Fader`, 규칙 값(`warningThreshold`/`defeatThreshold`), **일시정지 메뉴**(`pauseMenu` + 계속/재시작/메인 버튼 3개 + 설정 버튼 `setBtn` + 설정 패널 `settingPanel`) 할당
- **설정 패널 (`SettingsManager`)**: 일시정지 메뉴의 설정 패널에 부착 — AudioMixer + 볼륨/감도 슬라이더 연결
- **HUD (`GameHUD`)**: TMP 텍스트 2개(인형/얼룩 카운트), 상호작용 프롬프트, 경고 문구, "문이 열렸다" 문구, **리플레이 Exit 버튼 + 배속 버튼(1X/2X/4X) + 재생 종료 문구(`replayFinishedText`)** 할당
- **리플레이 (CCTV)**:
  - 빈 오브젝트에 `ReplayRecorder` 부착 (녹화 — 없어도 게임은 동작하지만 CCTV 버튼이 무반응)
  - 빈 오브젝트에 `ReplayManager` 부착, `Stain Prefab`에 얼룩 프리팹 할당 (재생용). `Door`는 자동 탐색됨
  - `CameraManagement`의 `ReplayCam`에 방 전체가 보이는 **고정 CCTV용 CinemachineCamera** 할당
  - ⚠️ 인형 오브젝트의 **이름이 서로 달라야 함** — 이름 정렬 순서가 리플레이 ID로 쓰임 ([§3.17](#317-ireplaypausablecs--replayidentitycs--리플레이-공용-규칙) 참고)
  - CCTV에 플레이어가 보이려면 플레이어에 몸통 비주얼 필요 (DollManiaHouse는 MapSpec이 머리+몸통 부착)

### DollManiaHouse 전용

- 씬 구조(방/가구/프롭/웨이포인트/링크)는 손으로 만들지 말고 **[MapSpec](#322-mapspeccs--맵-규격-자동화-에디터-전용) 도구로 생성/정비** — `Tools > 맵 규격 적용` 후 NavMesh 재베이크. 치수 규격은 [MapSetup.md](MapSetup.md) 참고

### ResultPopup

- `ResultPopupUI`에 팝업 루트/딤 CanvasGroup/패널, 텍스트 5개(제목·시간·이동·청소·남은 수), 버튼 3개(CCTV/다시하기/나가기), 승/패 도장 2세트 할당
- ⚠️ **도장(Stamp) 이미지의 `Raycast Target` 체크 해제 필수** — 도장은 alpha 0으로 숨어 있을 뿐 오브젝트가 켜져 있어, 켜두면 투명한 채로 아래 버튼(특히 CCTV)의 클릭을 가로챔. 딤 배경 등 장식용 이미지도 꺼두는 것을 권장

누락 시 각 스크립트의 `Start()`에서 `Debug.LogWarning`으로 알려주도록 되어 있습니다 (일부).

---

*최종 수정: 2026-08-10 · 브랜치: `dev` · [Prototype.md](Prototype.md)에서 분리*
