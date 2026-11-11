# 🗺️ 맵 설계 규격 (Map Setup)

> **인형을 찾아라 (Find the Dolls)** — 씨애랑 2026 SW 전시회 출품작
> `DollManiaHouse` 씬을 기준으로 정리한 **치수 규격 · 자동화 도구 · NavMesh 설정** 문서입니다.
> 스크립트 동작은 [ScriptReference.md](ScriptReference.md), 전체 흐름은 [Prototype.md](Prototype.md) 참고.

---

## 1. 핵심 원칙 — 기준은 방이 아니라 사람

Unity는 **1 유닛 = 1 미터**가 전제입니다 (중력 -9.81, NavMeshAgent 반경, Rigidbody 질량이 모두 이 가정 위에서 동작). 그래서 방 크기를 바꿔도 **사람 치수는 바뀌지 않습니다.**

| 방 크기와 무관하게 고정 | 방 크기에 비례 |
|---|---|
| 플레이어 키 · 눈높이 · 이동 속도 | `escapeDistance` |
| 카메라 FOV | `sightDistance` |
| 상호작용 거리 | 웨이포인트 · 프롭 개수 |

**방 크기를 정하는 실질 변수는 "한 시야에 프롭이 몇 개 들어오는가"** 입니다. 이 게임의 재미가 "이 중에 뭐가 인형이지?"이므로, 한 화면에 프롭 4~7개가 들어와야 고민이 생깁니다. 1~2개면 찍고, 15개면 포기합니다.

---

## 2. 확정 수치

### 2.1 플레이어 (초등 4~5학년)

| 항목 | 값 | 비고 |
|---|---|---|
| 키 | **1.45m** | CapsuleCollider Height |
| 눈높이 | **1.35m** | 키 × 0.93 |
| 캡슐 Radius | 0.181 | 키 × 0.125 |
| Follow Offset Y | 0.624 | 피벗이 캡슐 중앙일 때 = 눈높이 − 키/2 |
| 이동 속도 | 2.02 m/s | 눈높이 × 1.5 |
| 팔 뻗기 한계 | 1.75m | 벽 선반까지 닿음 |
| 카메라 vFOV | **50** | 16:9에서 수평 약 79° |

> ⚠️ **키를 Transform Scale로 조절하지 말 것.** 메시가 왜곡되고 CapsuleCollider가 비균등 스케일을 받아 물리 판정이 깨집니다. 항상 Scale은 `(1,1,1)`, 키는 콜라이더 Height로.

> 체감 속도는 **눈높이에 반비례**합니다. 눈높이 1.35m에서 2.8m/s로 움직이면 성인이 달리는 것과 같은 광류(optical flow)가 생겨 어지럽습니다. 그래서 2.0m/s가 상한에 가깝습니다.

### 2.2 방 (`MapScale = 1.5` 적용 후)

| 항목 | 기준값 | 실제 (×1.5) | Unity |
|---|---|---|---|
| 바닥 | 10 × 11.3m | **15 × 16.95m** (77평) | Plane Scale `1.5 / 1 / 1.7` |
| 천장 높이 | 3.0m | **4.5m** | — |
| 벽 두께 | 0.2m | 0.3m | Cube Scale = 미터 |
| 문 | 0.9 × 2.0m | 1.35 × 3.0m | — |

Plane 원본은 **10 × 10 유닛**이라 `÷10`, Cube 원본은 **1 유닛**이라 Scale 값이 곧 미터입니다.

### 2.3 인형 · 프롭 · 얼룩

| 항목 | 값 | 비고 |
|---|---|---|
| 인형 높이 | **0.6m** | 기준 0.4m × 1.5 |
| 프롭 "인형" | **0.6m** | 진짜 인형과 **완전히 동일한 치수** |
| 얼룩 지름 | 0.45m | — |
| 프롭 총 개수 | 121 | 가구 위 105 (티세트·곰 포함) + 바닥 16 |
| 인형 NavMeshAgent | Radius 0.3 / Height 0.75 / Speed 1.8 | — |

> **인형과 프롭을 같은 배율로 키우는 것이 필수입니다.** 한쪽만 키우면 겉보기로 구분이 되어 위장이 깨집니다. 현재 둘 다 0.6m라 "움직였나"로만 판별할 수 있습니다 — README의 *"유사한 디자인의 인형들이 밀도 있게 배치되어 높은 난이도"* 가 이 조건에서 성립합니다.

### 인형 프리팹 (2026-07-30)

| 프리팹 | 역할 | 비고 |
|---|---|---|
| `Assets/doll.prefab` | **안 움직이는 인형** (진짜 오브젝트, 프롭) | 인형 계열 프롭(인형·큰인형·미니인형·거대한곰인형)은 전부 이 프리팹으로 생성 |
| `Assets/doll_move.prefab` | **돌아다니는 인형** (얼룩을 뿌리는 doll) | `ApplyBatch`가 씬의 수제 DollMove(캡슐)를 이 프리팹 인스턴스로 자동 교체 |

* 두 프리팹은 같은 `DollCharacter` 비주얼을 공유 → **겉모습만으로 구분 불가** (위장 성립)
* `doll_move.prefab`에는 `DollMove`·`DollVision`·`DollStainSpawner`·`DollLaugh`가 함께 붙어 있다. 인형 수치를 코드로 건드릴 때는 **컴포넌트별로 찾아 적용**할 것 (§2.4 표 참고)
* `doll.prefab`의 비주얼이 루트에서 (1.22, 0, 9.36) 어긋나 있던 것을 원점으로 정렬하고, 1m 큐브였던 콜라이더를 실측(0.36×0.53×0.53)으로 수정
* 프리팹 피벗이 발밑과 정확히 일치하지 않아, `ScaleLevelObjects`가 실측 발 높이로 `baseOffset`·`spawnYOffset`을 자동 보정

### 2.4 거리 파라미터 (방 크기에서 자동 산출)

| 항목 | 붙는 컴포넌트 | 공식 | 현재 값 |
|---|---|---|---|
| `escapeDistance` | `DollMove` | min(가로, 세로) × 0.4 | 6.0m |
| `sightDistance` | `DollVision` | 방 대각선 × 1.2 | 27.2m |
| `viewAngle` | `DollVision` | 카메라 수평 FOV × 0.88 | 70° |

> `viewAngle`을 카메라 FOV에서 역산하는 것이 중요합니다. 이 값이 실제 화면보다 넓으면 **인형이 화면 밖에서도 "보였다"고 판정해 얼어붙습니다.** 초기에 vFOV 20(수평 35°) + viewAngle 70 조합이라 인형이 거의 움직이지 못했습니다.

**위 두 값은 "실제로 보이는 범위"이고, 인형이 실제로 멈추는 범위는 그보다 넓습니다.** `DollVision`의 `stopMarginAngle`(기본 20°)·`stopMarginDistance`(기본 2m)를 더한 **위험 지대**에 들어오면 미리 정지해, 카메라를 돌리는 도중에 움직이는 모습이 잡히지 않습니다. MapSpec은 마진을 건드리지 않으므로 **맵 크기와 무관한 튜닝값**으로 다루세요 (인형이 너무 소극적이면 줄이고, 여전히 움직이는 게 보이면 늘림).

---

## 3. 높이 계층 (T0 ~ T3)

눈높이 **1.35m**를 기준으로 은신처의 성질이 완전히 달라집니다. 얼룩은 바닥에 눕힌 스프라이트(`Euler(90,0,0)`)라 **표면이 눈높이보다 충분히 낮아야 보입니다.**

| 단계 | 높이 | 가구 예시 | 얼룩 가시성 | 얼룩 위치 |
|---|---|---|---|---|
| **T0** | 0m | 바닥, 러그 | ✅ 최상 | 그 자리 |
| **T1** | 0.6~0.9m | 침대, 정리함, 티테이블 | ✅ 잘 보임 | 그 자리 |
| **T2** | 1.1~1.35m | 책상, 서랍장, 낮은 책장 | ✅ 한계선 | 그 자리 |
| **T3** | 1.4m 이상 | 책장, 옷장, 벽 선반, 블록성 | ❌ **안 보임** | **링크 발치(바닥)** |

**T2와 T3 사이가 분기점**입니다. 1.2m 거리에서 얼룩이 화면에 보이는 비율:

| 표면 높이 | 보이는 비율 |
|---|---|
| 바닥 | 69% |
| 0.9m | 53% |
| 1.35m (눈높이) | **0%** — 완전히 투명 |
| 1.5m 이상 | 가구에 가려 아예 안 보임 |

### 얼룩 생성 규칙 (구현됨 — `DollMove.Update`의 링크 통과 처리)

인형이 **NavMeshLink로 높은 곳에 올라갈 때, 링크 시작점(바닥)에 얼룩을 남긴다.**

* 링크 시작점은 정의상 NavMesh 위이므로 **플레이어가 반드시 접근 가능** — "가구 안에 얼룩이 갇히는" 문제가 원천적으로 없음
* 인형이 실제로 밟고 지나간 지점이라 인과가 자연스러움
* 얼룩이 "위를 봐라"는 단서가 됨 → 높은 곳은 안전하지만 위치를 노출하는 트레이드오프
* `endPos.y > startPos.y` 로 **올라갈 때만** 판정. 내려올 때는 도착지가 바닥이라 기존 규칙으로 충분
* 방향은 반드시 **런타임 `currentOffMeshLinkData`의 startPos/endPos 비교**로 판정 (링크가 양방향이라 인스펙터 값으로는 알 수 없음)

---

## 4. `MapScale` 구조

`Assets/Editor/MapSpec.cs` 상단의 `MapScale` 하나로 맵 전체 배율을 조절합니다.

```csharp
const float MapScale = 1.5f;
```

| 배율을 따라가는 것 | 배율과 무관한 것 |
|---|---|
| 방 · 천장 · 벽 · 문 | **플레이어 키 · 눈높이 · 속도** |
| 가구 · 프롭 | 카메라 FOV |
| 인형 · 얼룩 | — |
| Agent Radius / Height | — |
| 각종 여유·오프셋 | — |

가구 치수는 **배율 1.0 기준으로 적어두고** `Furn.SX/SY/SZ` 접근자에서 곱합니다. 위치는 방 크기에 대한 **비율(-1~1)** 로 정의되어 있어 방 크기를 바꾸면 저절로 따라갑니다.

---

## 5. 자동화 도구 — `MapSpec.cs`

`Assets/Editor/MapSpec.cs`. 수치를 바꾸고 다시 실행하면 씬 전체가 재구성됩니다. **모든 엔트리포인트는 재실행 안전(idempotent)** 입니다.

### 에디터에서

| 메뉴 | 동작 |
|---|---|
| `Tools > 맵 규격 적용` | 현재 열린 씬에 전체 적용 |
| `Tools > 맵 규격 점검` | **읽기 전용** 진단 (저장하지 않음) |

### CLI에서 (Unity를 닫아야 함)

```bash
"C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe" \
  -batchmode -quit -nographics \
  -projectPath "<프로젝트 경로>" \
  -executeMethod MapSpec.ApplyBatch \
  -logFile -
```

| 메서드 | 동작 |
|---|---|
| `ApplyBatch` | 씬 열기 → 전체 적용 → 저장 (수제 DollMove → doll_move 프리팹 교체 포함) |
| `PlaceAndScaleBatch` | 배치 + 크기 정규화만 |
| `BakeBatch` | NavMeshSurface 재베이크 + 데이터 에셋 저장 — `ApplyBatch` 후 반드시 실행 |
| `DiagnoseBatch` | 진단만 (저장 안 함) |

> ⚠️ `Temp/UnityLockfile`이 있으면 CLI가 프로젝트를 열지 못하고 **exit 1**로 즉시 종료됩니다. 에디터를 먼저 닫으세요.
>
> ⚠️ 에디터에 **저장하지 않은 변경**이 있으면 CLI 결과와 충돌합니다. 닫기 전에 저장하세요.

### 생성되는 것

| 그룹 | 내용 |
|---|---|
| `Map/Wall` | 벽 6장 (좌·우·뒤 + 앞벽 3조각으로 문 구멍) |
| `Map/Plane` | 바닥 |
| `Map/OutsideFloor` | 문 밖 바닥 — ExitZone 진입 후 낙하 방지 |
| `Map/Furniture` | 가구 24점 |
| `Map/Props` | 프롭 121개 (`Prop` 태그 + `Interactable` 레이어) — 인형 계열은 `doll.prefab` 인스턴스 |
| `Map/NavLinks` | NavMeshLink 20개 |
| `WayPointGroup` | 웨이포인트 23곳 (가구 윗면 20 + 바닥 3) |
| `Player/Visual` | `Head` + `Body` |

`Random.InitState(20260729)` 고정이라 **다시 실행해도 같은 배치**가 나옵니다.

### 자동 검사

윗면 최소변이 `MinSpotSize`(= Agent 지름 + 0.3m = 0.9m)보다 작으면 **은신처에서 자동 제외**되고 경고가 뜹니다. 가구 치수나 `MapScale`을 바꿀 때 조용히 깨지지 않습니다.

현재 제외 상태: `정리함3`, `정리함4` (최소변 0.75m) — 가구로는 남아 시야만 가립니다.

---

## 6. NavMesh 설정 (⚠️ 가장 많이 터진 부분)

### 6.1 Agent Type — `Window > AI > Navigation > Agents`

| 항목 | 값 | 기본값이면 안 되는 이유 |
|---|---|---|
| Radius | **0.3** | 0.5면 최소변 1.0m 필요 → 책장(0.975m) 탈락 |
| Height | **0.75** | 2.0이면 표면 위 2m 여유 필요 → 옷장(여유 1.5m) 등 **높은 은신처 전멸** |
| Step Height | 0.225 | — |
| Max Slope | 45 | — |

### 6.2 NavMeshSurface

**오브젝트마다 붙이는 게 아닙니다.** `Collect Objects = All`인 **하나**가 씬 전체를 긁어모아 베이크합니다.

| 항목 | 값 |
|---|---|
| Collect Objects | All Game Objects |
| Include Layers | Everything |
| Use Geometry | Render Meshes |
| **Minimum Region Area** | **0** ← 반드시 |

> **`Minimum Region Area = 2`(기본값)가 가구 윗면을 거의 전부 잘라냅니다.** 이 옵션은 다른 영역과 연결되지 않은 작은 NavMesh 섬을 버립니다. 가구 윗면은 바닥과 기하적으로 분리된 섬이고, Agent Radius만큼 침식되면 면적이 0.2~0.8㎡라 전부 기준 미달입니다. 침대(2.52㎡)만 살아남습니다.

윗면이 좁아 불안정하면 `Override Voxel Size`를 켜고 **0.05**로 낮추면 해상도가 2배가 됩니다.

### 6.3 프롭은 베이크에서 제외

프롭 하나가 **자기 크기 + Agent Radius** 만큼 NavMesh를 파냅니다. 가구 윗면은 침식 후 폭이 0.375m뿐이라 프롭 하나로 패치가 전멸합니다.

프롭·얼룩에 `NavMeshModifier.ignoreFromBuild = true`를 붙여 해결했습니다 (`MapSpec.ExcludeFromNavMesh`). 레이어 마스크 설정과 무관하게 동작합니다.

> 프롭은 **장난감**입니다. 인형이 그 사이를 헤집고 다녀야 위장이 되므로, 통행 장애물로 취급할 이유가 없습니다.

### 6.4 NavMeshLink

링크 끝점은 윗면 **중앙이 아니라 "방 중앙 쪽 가장자리"** 여야 합니다. 중앙으로 이으면 직선이 가구 몸통을 관통해 **인형이 뚫고 들어가는 것처럼** 보입니다.

```
시작점: 가구 발치에서 0.4m × MapScale 떨어진 바닥
끝점  : 윗면 가장자리에서 Agent Radius만큼 안쪽 (NavMesh가 실제로 있는 지점)
```

`Generate Links`(NavMeshSurface 옵션)는 지오메트리에서 자동 생성하는 낙하·점프 링크로, `NavMeshLink` 컴포넌트와는 무관합니다. 꺼두어도 됩니다.

---

## 7. 해결한 버그

### 씬 · 오브젝트

| 증상 | 원인 | 수정 |
|---|---|---|
| 화면이 좁고 답답함 | 카메라 vFOV가 **20** (망원렌즈 수준) | vFOV 50, `viewAngle` 70과 동기화 |
| 플레이어가 벽에 비해 거대해 보임 | `body_head` 메시가 루트에 있고 **피벗이 +1.13m 오프셋** → 콜라이더(0~1.45m) 위 **1.52~2.19m에 떠 있음** | `Visual/Head`(스케일·높이 보정) + `Visual/Body` 자식 구조로 분리 |
| 플레이어 메시 왜곡 | Transform Scale Y = 1.45로 키를 조절 | Scale `(1,1,1)` + CapsuleCollider Height |
| 벽이 끝없는 복도처럼 보임 | Wall Scale Z = **100** (40m 맵 잔재) | 방 크기에서 산출해 재생성 |
| 얼룩이 저장 후 방 밖으로 튐 | 얼룩이 `RectTransform`이라 world `position`이 **직렬화 왕복에서 어긋남** | `localPosition` 기준으로 고정 |
| 문 통과 불가 | 캡슐 Radius 0.5(지름 1m) > 문 폭 0.9m | Radius 0.181 |

### 인형 AI (`DollMove.cs`)

| 증상 | 원인 | 수정 |
|---|---|---|
| **같은 자리에 얼룩이 계속 쌓임** | `TryMove`가 이동 성공 여부와 무관하게 얼룩부터 찍음 → 도착 판정 → 은신 → 충전 → 실패한 이동 → 얼룩 … 5초마다 반복 | `SetTarget`이 `bool` 반환, **성공 후에** 이동권·얼룩 소모 |
| 인형이 가구를 통과해 안으로 들어감 | 웨이포인트가 NavMesh 위에 없으면 `SetDestination`이 **가까운 바닥으로 스냅** | `NavMesh.SamplePosition`으로 좁은 허용 반경(0.6m) 검사 후 실패 처리 + 경고 로그 |
| 인형이 영구히 멈춤 | `agent.isOnNavMesh == false` (가구 위 NavMesh 섬이 잘려나간 경우) → `SetDestination` 계속 실패 | `agent.Warp`로 자동 복귀 + 경고 로그 |
| **구역 점유 제외가 작동하지 않음** | 점유 목록이 `static`인데 **씬 재로드 시 초기화되지 않음** → 파괴된 웨이포인트 참조가 남아 판정 무력화 | `RuntimeInitializeOnLoadMethod` + `sceneLoaded`에서 `Clear()` (현재 `WaypointRegistry`가 담당) |
| 두 인형이 같은 자리에 겹침 | 점유가 **목적지만** 예약. `SetTarget`에서 이전 웨이포인트를 즉시 반납하지만 인형은 아직 그 자리에 있음 | `previousTargetIndex` 추가 — **도착할 때까지 떠나온 자리도 예약** |
| 원인 불명의 정지 | 은신처 선택이 `-1`을 조용히 반환 | 5초 throttle 경고 로그. 단 **모든 후보가 시야에 노출돼 못 가는 경우는 정상 대기**라 경고하지 않음 |
| **인형이 시야에 들어온 뒤에 멈춤** | 실제 시야각 그대로 판정해, 참이 되는 시점엔 이미 화면 안 | `DollVision`의 **위험 지대**(시야 + 마진)로 미리 정지 + **경로 미리보기**로 노출 구간 진입 자체를 회피 (§2.4 참고) |

### NavMesh

| 증상 | 원인 | 수정 |
|---|---|---|
| 가구 윗면에 NavMesh가 안 생김 | Agent Radius 0.5 / Height 2.0 (기본값) | 0.3 / 0.75 |
| 가구 윗면 NavMesh가 침대만 남음 | `Minimum Region Area = 2`가 작은 섬을 전부 폐기 | **0** |
| 가구 윗면·바닥에 구멍 | 프롭이 장애물로 수집되어 NavMesh를 파냄 | `NavMeshModifier.ignoreFromBuild` |
| 베이크에 아무것도 안 들어옴 | `Plane`의 NavMeshSurface `Include Layers`가 **`Ground` 레이어만** | Everything |

---

## 8. 남은 작업

### ~~인형 AI — 링크 통과 처리~~ (2026-07-30 구현 완료)

**오프메시링크 통과 중에는 `agent.isStopped`가 듣지 않아**, auto-traverse 상태에서는 플레이어가 쳐다봐도 인형이 링크를 떠서 건너갔다 — "보면 안 움직인다" 대전제가 깨지는 지점이었다.

`DollMove`에 구현된 처리: `Start`에서 `agent.autoTraverseOffMeshLink = false`, `Update`의 위험 지대 판정(`vision.IsInDangerZone()`) 통과 직후(= 안 보일 때만 도달하는 지점)에서 `agent.isOnOffMeshLink`면 **`CompleteOffMeshLink()`로 순간이동**. 올라가는 이동(`endPos.y > startPos.y + linkStainHeight`)이면 링크 시작점(바닥)에 얼룩을 남긴다 — §3의 얼룩 규칙.

**건너편(`link.endPos`)도 함께 검사한다** — 링크 통과는 순간이동이라, 도착점이 위험 지대면 건너지 않고 이쪽에서 대기한다. 검사가 없으면 한 프레임 만에 시야 안에 '나타나는' 사고가 난다.

결과: **순간이동 + 올라갈 때만 얼룩 + 보는 동안 정지**. 시선을 떼는 순간 인형이 선반 위에 나타나는 연출이 된다. 플레이어가 보는 동안 인형은 링크 앞에서 대기한다.

### 그 밖에

| 항목 | 내용 |
|---|---|
| 1인칭에서 머리 안쪽이 렌더링됨 | 카메라(1.35m)가 머리 메시(1.0~1.45m) 안에 있음. `PlayerVisual` 레이어를 만들어 플레이어 카메라 Culling Mask에서 제외, CCTV 카메라는 포함 |
| prototype 잔재 | `ThisIsSunban`, 낡은 `NavLinkup`/`NavLink_down` 2개, `Cylinder`×11, `Cube` — 40m 맵 시절 좌표에 남아 있음 |
| NavMeshSurface 중복 | `ThisIsSunban`과 `Plane`에 2개. 같은 Agent Type으로 씬 전체를 중복 베이크 |
| 조준 거리 하드코딩 | [PlayerInteraction.cs:42](../Assets/Scripts/Player/PlayerInteraction.cs)의 Raycast가 `6f` 고정. 팔 뻗기(1.75m)에 맞춰 **2.0m**로, 그리고 인스펙터 노출 필요 |
| 기획 불일치 | README의 패배 조건은 **25개**, `GameProgressManager.defeatThreshold`는 **7**. 어느 쪽이 현재 기획인지 확정 필요 |
| 은신처 제외 상태 | `정리함3`, `정리함4` (최소변 0.75m < 0.9m). 키우면 복귀 |
| 가구 윗면 여유 | 침식 후 폭 0.375m로 통행은 되지만 빡빡함. 책장류 깊이 0.65 → 0.9(기준값)로 키우면 0.75m 확보 |

---

## 9. 방 크기를 다시 조절할 때

`MapSpec.cs` 상단 세 줄만 고치고 `Tools > 맵 규격 적용`을 누르면 나머지가 전부 따라옵니다.

```csharp
const float MapScale = 1.5f;
const float BaseRoomWidth = 10f;
const float BaseRoomDepth = 11.3f;
```

바꾼 뒤 **반드시 NavMesh 재베이크**가 필요합니다. `MapScale`을 바꿨다면 Agent Radius/Height도 함께 조정하세요 (`0.2 × MapScale` / `0.5 × MapScale`).

### 참고 — 방 크기별 체감

| 크기 | 평 | 판정 |
|---|---|---|
| 4 × 4m | 5평 | ❌ 대각선 5.8m < `escapeDistance` → 인형이 영구 정지 |
| 6 × 7m | 13평 | ✅ 단일 공간 최소선 |
| 7.5 × 9m | 20평 | ✅ 교실 크기 — 기본값과 가장 잘 맞음 |
| **15 × 17m** | **77평** | 현재. `MapScale 1.5`로 가구가 커져 차폐가 강함 |
| 40 × 40m | 484평 | ❌ 초기 prototype. 너무 넓어 탐색이 지루함 |

---

---

## 10. 할로윈 나이트 (`HalloweenNight`)

인형매니아 가구와 무관하게 새로 지은 **밤의 묘지** 맵. 1회성 생성 도구로 만든 뒤 도구는 삭제했으므로, 이후 수정은 씬에서 직접 한다. 가구·장식을 옮기면 `Map/Plane`의 NavMeshSurface에서 **Bake**를 다시 누를 것 (결과는 `Assets/Scenes/HalloweenNight/`).
방 크기(15 × 16.95m)·문·플레이어 규격은 인형매니아와 같다. ⚠️ 이 씬에서 `Tools > 맵 규격 적용`을 누르면 인형매니아 가구로 덮어쓴다.

### 은신처 — 묘지 구조물

| 계층 | 구조물 (높이) |
|---|---|
| T3 | 납골당 (2.6m) · 종탑 (2.2m) · 큰건초더미 (1.6m) |
| T2 | 제단 (1.2m, 양 끝 촛불) · 우물 (1.1m) |
| T1 | 석관 ×3 (0.8m) · 건초더미 (0.9m) · 나무상자 (0.9m) |
| T0 | 바닥 은신처 4곳 |
| — | 시야 차단 돌담 ×4 (2.0m, 은신처 아님) · 무덤탑 ×2 (2.2m) |

**트인 묘지는 시선이 맵 끝까지 닿아 인형이 늘 '보이는 중'으로 멈춘다** — 눈높이보다 높은 불투명 돌담으로 시선을 끊어야 인형이 움직인다 (인형매니아의 진열장·파티션 역할). 윗면은 NavMesh가 확실히 깔리도록 프리팹이 아닌 **박스 구조물**(돌·건초·나무 재질)이다. 링크 규칙은 §6.4와 같되, 윗면 끝점을 **각 변에서 Agent Radius + 0.2m 안쪽으로 클램프**한다 — 진행 방향으로만 radius를 빼면 대각선 링크의 끝점이 침식된 NavMesh 밖에 떨어져 링크가 연결되지 않고, 윗면에서 시작한 인형이 못 내려온다. 생성 후 모든 은신처와 바닥 사이 경로를 양방향으로 계산해 점검한다.

### 인형 — 호박 · 허수아비

| 모습 | 모델 | 높이 | 움직이는 인형 |
|---|---|---|---|
| 호박 | `Pumpkin_Carved_A` | 0.6m | 2마리 |
| 허수아비 | `Scarecrow_A` | 1.1m | 1마리 |

* 움직이는 인형과 가짜는 **같은 구조**(빈 루트 + `Visual` 자식 + 루트 BoxCollider)로 만들어 겉모습이 같다.
* 인형은 `doll_move` 프리팹 연결을 끊고 비주얼만 교체 — 씬의 참조·설정값과 리플레이 ID(이름순)는 그대로.
* 피벗 = 발밑이라 `baseOffset` 0, `spawnYOffset` 0.02.
* 인형 루트에 `NavMeshModifier.ignoreFromBuild` — Surface의 'NavMeshAgent 무시'는 에이전트가 붙은 오브젝트만 빼고 **비주얼 자식은 굽는다.** 표식이 없으면 시작 자리에 구멍이 나 그 은신처가 '도달 불가'가 된다.
* 장식에는 이 두 모델을 쓰지 않는다 — 조준해도 `Catch` 프롬프트가 안 뜨는 '가짜 호박'이 생기면 구분 단서가 된다. 불 켜진 호박등(`Pumpkin_Carved_Lit_*`)은 장식이지만, 비석·무덤탑·관과 함께 **`Prop` 태그 + `Interactable` 레이어**를 달아 조준 시 `Catch [E]`가 뜬다(잡으면 오답). 소품과 달리 NavMesh에는 남아 인형이 피해 다닌다.

### 환경

| 항목 | 내용 |
|---|---|
| 외벽 | 묘지 담장(`Wall_Graveyard_2m`·기둥, 틈이 벌어져 보이지 않게 조각 길이 ×1.08로 겹침) — 기존 벽은 렌더러만 꺼서 투명 충돌벽으로 유지 |
| 나무 | 방 안 고목 5그루(2.8~3.5m) + 담장 밖 8그루(8~10m, 실루엣). 담장 밖 나무는 콜라이더 없음·NavMesh 제외, 높이 2.5m 아래 메시 꼭짓점이 담장 선에서 0.8m 안쪽으로 들어오면 바깥으로 밀어냄(뿌리가 담장 안에 안 보이게). 방 안 나무는 프리팹의 볼록 MeshCollider(가지 끝까지 감싸는 덩어리)를 줄기 CapsuleCollider(반경 0.18m)로 교체 |
| 장식 | 비석 · 무덤탑 · 관 · 유령. 구조물·링크 시작점·은신처·문 통로는 피해서 배치, NavMesh에 포함돼 인형이 피해 다님 |
| 조명 | 수치 — 달빛(청록 0.45) + 보라 채움광 0.38(`Map/Lighting`), 선형 안개 -10~50m, `M_Skybox_Night`, 데모의 세 색을 Gradient 앰비언트로 |
| 광원 | 호박등 9 · 가로등 3 · 문 밖 가로등 2 · 제단 촛불 2 (그림자 없음, PC 렌더러가 Forward+라 개수 제한 없음) |
| 후처리 | `HalloweenNightVolume.asset` — Bloom · Vignette · Neutral 톤매핑 |
| NavMesh | `Assets/Scenes/HalloweenNight/`에 저장. 생성 후 모든 은신처·링크 끝점의 NavMesh 도달 여부를 자동 점검 |

---

*최종 수정: 2026-09-30 · 브랜치: `dev_Halloween` · 대상 씬: `DollManiaHouse`, `HalloweenNight`*
