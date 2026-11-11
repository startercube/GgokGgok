# 🔍 코드 설계 검토 — SOLID 원칙 & 디자인 패턴

> **인형을 찾아라 (Find the Dolls)** — 씨애랑 2026 SW 전시회 출품작
> 현재 프로토타입 스크립트를 SOLID 원칙과 디자인 패턴 관점에서 검토한 결과입니다.
> 각 시스템의 동작 설명은 [Prototype.md](Prototype.md)를 참고하세요.
>
> 이 문서에는 **지금 고칠 가치가 있는 항목만** 남겨 두었습니다.
> §3은 앞으로 자주 바뀔 부분의 **SOLID 개선점**입니다.

---

## 1. 요약 — 우선순위 한눈에 보기

| 우선순위 | 항목 | 종류 | 왜 지금 중요한가 |
|---|---|---|---|
| 🟠 1 | [PlayerInteraction의 태그 문자열 분기](#32--playerinteraction의-태그-분기-ocp) | OCP | 새 상호작용 대상 추가 시마다 남의 코드 수정 — `IInteractable`로 확장에 열기 |
| 🟠 2 | [GameProgressManager의 규칙 + 흐름 + 일시정지 혼재](#33--gameprogressmanager--규칙과-흐름-일시정지가-한-몸-srp) | SRP | 일시정지 메뉴까지 붙어 무게가 커지는 중 |
| 🟠 3 | [ReplayRecorder가 전역 모드 플래그 조작](#35--replayrecorder가-전역-모드-플래그를-조작-srp) | SRP | 녹화기가 게임 모드를 바꾸는 예상 밖 부수효과 |
| 🟠 4 | [PlayerInteraction → Recorder/Progress 직접 호출](#31--게임플레이--recorderprogress-직접-호출-dip-부분-해결) | DIP | 인형 쪽은 이벤트로 뒤집혔고, 상호작용 경로만 남음 |
| 🟡 5 | [GameManager 전 필드 public](#36--gamemanager--전-필드-public-isp) | ISP | 누가 뭘 쓰는지 코드로 강제되지 않음 |
| 🟡 6 | [매직 문자열 산재](#37--매직-문자열-산재-dip) | DIP | 오타 시 컴파일 에러 없이 런타임 실패 |
| 🟡 7 | [기타 경미한 항목](#38--기타-경미한-항목) | 여러 원칙 | 해당 부분을 어차피 수정할 때 함께 |

**LSP(리스코프 치환)는 해당 없음** — 상속 계층이 전혀 없어(모두 MonoBehaviour·인터페이스 직접 구현) 위반 지점 자체가 없습니다.

---

## 2. ✅ 해결된 항목 (요약)

지난 검토에서 지적된 아래 항목들은 **완료**되었습니다.

| 항목 | 어떻게 해결했나 |
|---|---|
| **GameUIManagement 5책임 혼재** (SRP) | `GameProgressManager`(규칙·판정·상태·흐름) + `GameHUD`(표시)로 분리, Model–View 확립 |
| **진행도 → UI 강결합** (DIP) | `OnProgressChanged`·`OnDoorOpened` 이벤트를 HUD가 구독. **옵저버 패턴** 적용, `OnDestroy`에서 해제까지 대칭 |
| **ReplayManager의 구체 타입 열거** (OCP) | `IReplayPausable` 마커 인터페이스 도입 — 정지 대상 스크립트가 직접 구현, ReplayManager는 인터페이스만 보고 일괄 정지 |
| **리플레이 ID 정렬 규칙 복붙** (DIP) | [`ReplayIdentity.SortById`](../Assets/Scripts/Replay/ReplayIdentity.cs)로 추출 — 녹화·재생이 같은 함수 사용 |
| **스냅샷 간격 이중 정의** (DIP) | `ReplayData.sampleInterval` 필드에 실어 보내 재생 쪽이 데이터에서 읽음 — 간격을 한 곳에서만 정의 |
| **DollMove 4역할 혼재** (SRP) | 시야→`DollVision`, 얼룩→`DollStainSpawner`, 선택 계산→`WaypointSelector`, 점유 목록→`WaypointRegistry`로 분리. DollMove는 이동·은신 상태 머신만 담당 ([상세](#34--dollmove--여러-역할-혼재-srp-해결)) |
| **인형 → Recorder/Progress 직접 호출** (DIP) | `DollStainSpawner.OnStainSpawned` static 이벤트로 뒤집음 — 두 구독자가 각자 `OnEnable`/`OnDisable`로 구독. `Instance != null` 가드 2개 제거 |
| **`static occupiedWayPoints`의 씬 재로드 문제** | `WaypointRegistry`로 분리하면서 `RuntimeInitializeOnLoadMethod` + `sceneLoaded` 리셋을 함께 이전 |

---

## 3. 미해결 항목 — 상세 해결법

### 3.1 🟠 게임플레이 → Recorder/Progress 직접 호출 (DIP) — 부분 해결

**인형 쪽은 해결됐습니다.** 얼룩 생성 경로는 `DollStainSpawner`가 이벤트만 쏘고, 듣고 싶은 쪽이 각자 구독하는 구조로 뒤집혔습니다.

```csharp
// DollStainSpawner: 이벤트만 쏘고 누가 듣는지 모름
public static event System.Action<GameObject> OnStainSpawned;

// 듣고 싶은 쪽이 각자 구독 (GameProgressManager · ReplayRecorder)
void OnEnable()  { DollStainSpawner.OnStainSpawned += HandleStainSpawned; }
void OnDisable() { DollStainSpawner.OnStainSpawned -= HandleStainSpawned; }
```

- `Instance != null` 가드 2개가 사라짐 — 구독자가 없으면 이벤트는 그냥 아무 일도 안 함
- **`IReplayPausable`과 자연스럽게 맞물림** — 리플레이 중 비활성화된 컴포넌트는 `OnDisable`로 구독을 놓음

> ⚠️ **static 이벤트 주의** — 반드시 `OnEnable`/`OnDisable` 쌍으로 구독·해제하세요.
> static 이벤트는 씬을 다시 로드해도 살아남아, 해제를 빠뜨리면 **파괴된 옛 오브젝트가 계속 구독된 채 남아** 얼룩이 두 번 세어지는 버그가 납니다.

**남은 것** — `PlayerInteraction`은 여전히 두 싱글톤을 직접 호출합니다.

| 남은 참조 | 직접 의존하는 싱글톤 |
|---|---|
| `PlayerInteraction.OnInteraction` / `TryCatch` | `GameProgressManager`, `ReplayRecorder` |

같은 방식(`OnStainCleaned`/`OnDollCaught` 이벤트)으로 뒤집을 수 있지만, [3.2의 `IInteractable` 도입](#32--playerinteraction의-태그-분기-ocp)과 **같은 코드를 건드리므로 함께 처리하는 것이 효율적**입니다.

---

### 3.2 🟠 PlayerInteraction의 태그 분기 (OCP)

**현재 문제** — `"Stain"`/`"Prop"` 태그 비교와 `InteractionState` switch가 `Update`와 `OnInteraction` **두 곳**에 흩어져 있습니다. 새 상호작용 대상(문 손잡이, 아이템 등)을 추가하려면 이 클래스의 두 곳을 모두 수정해야 합니다.

**해결법** — 전략(Strategy) 패턴. **유니티에서 가장 교과서적인 OCP 개선 지점**입니다.

1) 새 파일 `Assets/Scripts/Player/IInteractable.cs`:

```csharp
// 플레이어가 바라보고 상호작용할 수 있는 모든 대상의 공통 계약
public interface IInteractable
{
    string GetPromptText();   // 화면에 띄울 안내 문구 ("Clean [E]" 등)
    void Interact();          // 실제 상호작용 수행
}
```

2) 각 대상이 자기 행동을 직접 구현:

```csharp
public class Stain : MonoBehaviour, IInteractable
{
    public string GetPromptText() => "Clean [E]";
    public void Interact()
    {
        GameProgressManager.Instance?.CleanStain();
        ReplayRecorder.Instance?.RecordStainClean(gameObject);
        Destroy(gameObject);
    }
}
```

3) `PlayerInteraction`은 인터페이스만 알면 됨:

```csharp
IInteractable target = hitInfo.collider.GetComponentInParent<IInteractable>();
if (target != null)
{
    GameHUD.Instance.ShowPrompt(target.GetPromptText());
    // 입력 시: target.Interact();
}
```

**효과** — 새 상호작용 대상을 추가할 때 `PlayerInteraction`을 **전혀 수정하지 않습니다.** 태그 문자열 오타 위험도 사라집니다.

---

### 3.3 🟠 GameProgressManager — 규칙과 흐름, 일시정지가 한 몸 (SRP)

**현재 상태** — 분리 덕에 무게가 줄었지만, 최근 **일시정지 메뉴(Pause/버튼 3종 리스너)**까지 이 클래스에 들어오면서 성격이 다른 책임이 다시 늘고 있습니다.

| 책임 | 위치 | 성격 |
|---|---|---|
| 규칙·상태·승패 판정 | `CheckVictory` / `TriggerVictory` / `TriggerDefeat` | Model — 여기 남을 책임 |
| 종료 흐름 + 씬 전환 + timeScale + 커서 | `EndGame` / `GoToResult` / `EndGameRoutine` | Controller — 뗄 여지 |
| 일시정지 메뉴 + 버튼 리스너 | `Awake`의 `btn_*.onClick` / `Pause` | UI 흐름 — 뗄 여지 |
| GameManager로 결과 복사 + Recorder 종료 호출 | `EndGame` 본문 | 다른 시스템 직접 조작 |

**그때의 해결법** — 흐름·일시정지 UI를 별도 컨트롤러로 분리:

```csharp
// GameProgressManager는 "판정"만 하고 결과를 알림
private void TriggerDefeat()
{
    IsClear = false;
    OnGameEnded?.Invoke(false);   // 흐름 제어는 남에게 위임
}

// GameFlowController가 "그 다음 무슨 일이 일어나는지"를 전담
void OnEnable() { progress.OnGameEnded += HandleGameEnded; }
private void HandleGameEnded(bool isClear)
{
    // 결과 데이터 저장 → 녹화 종료 → 페이드 → 씬 전환
}
```

> 💡 참고로 `Awake`에서 `btn_continue`·`btn_retry`·`btn_mainExit`·`pauseMenu`를 인스펙터로 연결하지 않으면 리스너 등록 단계에서 `NullReferenceException`이 납니다. 분리하든 두든, 최소한 null 체크나 `[SerializeField]` 필수 연결을 확실히 해 두세요.

---

### 3.4 ✅ DollMove — 여러 역할 혼재 (SRP) — 해결

인형 이동·은신 로직을 고도화하기 전에 **선행 리팩토링으로 처리했습니다.** 4개 역할이 각자의 파일로 나뉘었습니다.

| 역할 | 이전 위치 | 현재 |
|---|---|---|
| 이동·은신 상태 머신 | 본체 | `DollMove` — 여기 남은 책임 |
| 시선 감지 | `IsSeenByPlayer` | `DollVision` (컴포넌트) |
| 얼룩 생성 + 통지 | `SpawnStain`/`SpawnStainAt` | `DollStainSpawner` (컴포넌트) + [3.1의 이벤트](#31--게임플레이--recorderprogress-직접-호출-dip-부분-해결) |
| 은신처 선택 점수 계산 | `FindFarthestWayPoint` 등 3개 | `WaypointSelector` (순수 계산 객체) |
| 전역 웨이포인트 점유 관리 | `static occupiedWayPoints` | `WaypointRegistry` (static 클래스) |

동작 상세는 [ScriptReference §3.1](ScriptReference.md#31-dollmovecs--인형-ai)·[§3.23~3.25](ScriptReference.md#323-dollvisioncs--가시성-판정) 참고.

**분리가 실제로 값을 한 지점** — 이후 "인형이 시야에 들어오고 나서야 멈춘다"를 고치면서, `DollVision`의 판정 API를 `IsPointSeen(임의의 점)`으로 설계한 덕에 세 기능이 **같은 함수 하나를 재사용**했습니다: ① 자기 위치의 여유각 정지, ② 경로 위 미래 위치 검사, ③ 은신처 후보의 노출 여부. 시야 판정이 DollMove 안에 있었다면 이동 로직과 얽혀 훨씬 복잡해졌을 부분입니다.

**`WaypointRegistry`에 대한 메모** — 원래 검토안은 "씬에 배치하는 MonoBehaviour"였지만, 코드에 이미 `RuntimeInitializeOnLoadMethod` + `SceneManager.sceneLoaded` 리셋이 들어와 씬 재로드 문제가 해결된 상태였습니다. 그래서 **씬마다 오브젝트를 배치할 필요가 없는 plain static 클래스**로 옮기고 리셋 코드를 그대로 가져갔습니다.

> 그래도 **"리플레이 후 인형이 이상하게 움직인다"는 제보가 오면 여기를 가장 먼저 의심하세요.** static 상태라는 성질 자체는 그대로입니다.

**분리하지 않은 것** — NavMesh 배관(`SetTarget`·링크 통과·NavMesh 밖 복귀)은 `agent`와 상태를 함께 만져 떼면 왕복 호출이 생깁니다. 파일이 다시 커질 때 `DollNavigator`로 분리하는 것을 후보로 남겨 둡니다.

---

### 3.5 🟠 ReplayRecorder가 전역 모드 플래그를 조작 (SRP)

**현재 문제** — [`FinishRecording()`](../Assets/Scripts/Replay/ReplayRecorder.cs#L173)이 녹화를 끝내면서 **전역 게임 모드까지 바꿉니다.**

```csharp
public void FinishRecording()
{
    // ... 녹화 데이터 마무리 ...
    GameManager.Instance.isReplayMode = false;   // ⚠️ 녹화기의 일이 아님
}
```

"녹화기"가 게임 모드를 조작하는 것은 이름에서 예상할 수 없는 부수효과입니다. 나중에 다른 곳에서 `FinishRecording()`을 부르면 의도치 않게 리플레이 모드가 꺼집니다.

**해결법** — 모드 전환은 흐름을 아는 쪽(`GameProgressManager.EndGame` 또는 3.3의 `GameFlowController`)으로 옮깁니다. 1~2줄 이동이라 비용이 거의 없습니다.

```csharp
// ReplayRecorder.FinishRecording — 녹화 데이터만 마무리
// GameProgressManager.EndGame — 모드 전환은 여기서 명시적으로
if (ReplayRecorder.Instance != null)
    ReplayRecorder.Instance.FinishRecording();
GameManager.Instance.isReplayMode = false;   // 한 판이 끝났으니 일반 모드로
```

---

### 3.6 🟡 GameManager — 전 필드 public (ISP)

**현재 문제** — `Instance`조차 프로퍼티가 아닌 public 필드이고, 모든 데이터 필드(`isClear`, `playTime`, `sceneName` 등)가 public이라 **누구든 어디서든 아무 필드나 덮어쓸 수 있습니다.**

**해결법** — 이미 `GameProgressManager`가 쓰고 있는 방식(`{ get; private set; }`)을 그대로 적용합니다.

```csharp
public static GameManager Instance { get; private set; }
public bool IsClear { get; private set; }
public float PlayTime { get; private set; }
public ReplayData ReplayData { get; private set; }

// 쓰기는 의도가 드러나는 메서드로만
public void SaveResult(bool isClear, float playTime, int cleaned, int total)
{
    IsClear = isClear; PlayTime = playTime;
    CleanedStains = cleaned; TotalStains = total;
}
```

**효과** — `EndGame`의 필드 4줄 대입이 `SaveResult(...)` 한 줄이 되고, 실수로 다른 곳에서 결과를 덮어쓰는 일이 컴파일 단계에서 막힙니다.

---

### 3.7 🟡 매직 문자열 산재 (DIP)

**현재 문제** — 씬 이름과 태그가 여러 파일에 문자열로 흩어져, 오타나 이름 변경 시 **컴파일 에러 없이 런타임에만 실패**합니다.

- 씬: `"ResultPopup"`·`"MainScene"`·`"Prototype"` (GameProgressManager, StageTransition 등)
- 태그: `"Player"`, `"Stain"`, `"Prop"` (여러 파일)

**해결법** — 상수 클래스 하나로 모읍니다.

```csharp
// Assets/Scripts/Core/GameConst.cs
public static class GameConst
{
    public static class Scene
    {
        public const string Result    = "ResultPopup";
        public const string Main       = "MainScene";
        public const string Prototype  = "Prototype";
    }
    public static class Tag
    {
        public const string Player = "Player";
        public const string Stain  = "Stain";
        public const string Prop   = "Prop";
    }
}
```

**효과** — 오타가 컴파일 에러로 잡히고, IDE 자동완성이 동작하며, 이름 변경 시 한 곳만 고치면 됩니다.

---

### 3.8 🟡 기타 경미한 항목

어차피 그 부분을 수정할 때 함께 처리하면 되는 수준입니다.

| 항목 | 문제 | 해결 방향 |
|---|---|---|
| **MainMenuManager 패널 복붙** | 패널 3종에 Toggle/Close 메서드가 복사·반복 | `public void TogglePanel(GameObject panel)` 하나로 일반화하고 인스펙터에서 대상 연결 |
| **ResultPopupUI의 계산 인라인** | 호출부에 `totalStains - 3` 같은 규칙이 노출 | 표시할 값은 `GameProgressManager`가 계산해 넘기기 — 규칙이 UI로 새지 않게 |
| **CameraManagement의 모드 분기** | 카메라 연출이 `GameManager.isReplayMode`를 직접 읽어 분기 | 리플레이 판단은 `ReplayManager`가 하고 카메라엔 "CCTV 켜라"고 명령 |
| **Carousel의 죽은 코드** | `radiusZ` 등 주석 처리된 블록 잔존 | 삭제 (git이 기억하므로 주석으로 남길 이유 없음) |

---

## 4. 디자인 패턴 관점 (요약)

**이미 잘 쓰고 있는 패턴** — 마커 인터페이스(`IReplayPausable`), 옵저버(`GameProgressManager` → `GameHUD`, **`DollStainSpawner` → 진행도·녹화기**), 메멘토/이벤트 소싱(리플레이 스냅샷+이벤트), 코루틴 시퀀싱(`DoorController` 등), **컴포넌트 분해**(DollMove ↔ DollVision·DollStainSpawner).

**도입하면 효과 있는 패턴**

| 패턴 | 적용 지점 | 효과 |
|---|---|---|
| **옵저버 확장** | [3.1의 남은 절반](#31--게임플레이--recorderprogress-직접-호출-dip-부분-해결) | null 가드 제거, 씬 단독 실행. 얼룩 경로에서 이미 검증됨 |
| **전략 (`IInteractable`)** | [3.2](#32--playerinteraction의-태그-분기-ocp) | 상호작용 대상 추가 시 기존 코드 무수정 |

> ⚠️ **싱글톤 과다** — `GameManager`, `GameProgressManager`, `GameHUD`, `ReplayRecorder`. 정말 전역인 것(씬 간 데이터 운반)에만 쓰는 게 좋습니다.

---

## 5. 결론 — 무엇을 언제 고칠 것인가

### 어차피 그 부분을 수정할 때 함께
- 상호작용 대상을 추가할 때 → [`IInteractable` 도입 (3.2)](#32--playerinteraction의-태그-분기-ocp) + [남은 직접 호출을 이벤트로 (3.1)](#31--게임플레이--recorderprogress-직접-호출-dip-부분-해결) — **같은 코드를 건드리므로 한 번에**
- 종료 연출·일시정지 UI가 더 붙을 때 → [흐름/일시정지 분리 (3.3)](#33--gameprogressmanager--규칙과-흐름-일시정지가-한-몸-srp)
- "리플레이 후 인형이 이상하다"는 제보가 오면 → [`WaypointRegistry`의 static 상태 (3.4)](#34--dollmove--여러-역할-혼재-srp--해결)부터 의심

### 이번에 배운 것 — 분리는 '기능을 고치기 직전'이 가장 싸다

[3.4](#34--dollmove--여러-역할-혼재-srp--해결)는 문서에 "인형 기능을 확장할 때 함께"라고 적어 둔 항목이었고, 실제로 인형 이동·은신 로직을 고도화하기 **직전에** 처리해 값을 뽑았습니다. 시야 판정을 먼저 컴포넌트로 떼어 `IsPointSeen(임의의 점)`으로 설계했더니, 뒤이은 세 기능(여유각 정지·경로 미리보기·은신처 노출 검사)이 그 함수 하나를 공유했습니다. **분리를 먼저 하고 기능을 얹은 것이 순서상 이득**이었던 사례입니다.

작업은 5단계로 쪼개 커밋을 나눴고, 각 단계마다 플레이로 확인했습니다: ① `DollVision` 분리(동작 불변) → ② 여유각 정지 → ③ 상태 enum + 경로 미리보기 → ④ `WaypointSelector` 분리 + 은신처 점수 고도화 → ⑤ `DollStainSpawner`·`WaypointRegistry` 분리. **동작 불변 리팩토링과 기능 변경 커밋을 섞지 않는 것**이 버그 원인을 좁히는 데 결정적이었습니다.

> ⚠️ **직렬화 필드가 이사하면 따라와야 할 것들** — 인스펙터 값(프리팹 재저장)과, 그 필드를 이름으로 찾는 **에디터 스크립트**(`MapSpec`은 어셈블리가 달라 IDE 리네임에 안 잡힘)를 반드시 함께 고치세요. 이번에도 `sightDistance`/`viewAngle`/`spawnYOffset` 이사 때 MapSpec이 컴파일 에러를 냈습니다.

> SOLID는 보험이고, 보험료(추상화 비용)가 공짜가 아닙니다. 판단 기준은 **"이 코드가 앞으로 얼마나 자주, 여러 사람에 의해 바뀔 것인가"**입니다. 남은 항목들은 해당 부분을 실제로 손댈 때 함께 개선하면 충분합니다.

---

*최종 수정: 2026-08-10 · 브랜치: `dev`*
