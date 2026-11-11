# 🔧 Git 환경 설정 (팀원 필독)

> 저장소를 클론했거나 `dev`를 pull한 뒤 **한 번만** 하면 되는 개인 설정입니다.
> `.gitattributes`는 저장소에 커밋되지만, 아래 병합 드라이버 등록은 각자 로컬에서 해야 합니다.
>
> **문서 지도** — [개요](Prototype.md) · [스크립트 상세](ScriptReference.md) · [설계 검토](SolidReview.md) · 환경 설정(이 문서)

---

## 1. Unity SmartMerge 등록 (필수)

씬(`.unity`)과 프리팹(`.prefab`)은 텍스트(YAML)라 Git이 병합할 수 있지만, 일반 텍스트 병합기로 줄 단위로 합치면 **열리지 않는 씬**이 나올 수 있습니다. Unity가 제공하는 전용 병합기 `UnityYAMLMerge`를 등록하세요.

`.gitattributes`에 이미 `merge=unityyamlmerge`가 지정돼 있으므로, 드라이버만 등록하면 씬 충돌 시 자동으로 이 도구가 호출됩니다.

**Windows** (Unity 6000.3.19f1 기준 — 버전이 다르면 경로의 버전만 바꾸세요)

```bash
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver "'C:/Program Files/Unity/Hub/Editor/6000.3.19f1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %A %B %A"
git config merge.unityyamlmerge.recursive binary
```

**macOS**

```bash
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver "'/Applications/Unity/Hub/Editor/6000.3.19f1/Unity.app/Contents/Tools/UnityYAMLMerge' merge -p %O %A %B %A"
git config merge.unityyamlmerge.recursive binary
```

등록 확인:

```bash
git config --get merge.unityyamlmerge.driver
```

아무것도 출력되지 않으면 등록되지 않은 것입니다. 이 상태로 씬 충돌이 나면 Git 기본 병합기로 폴백되니 반드시 확인하세요.

---

## 2. Git LFS

이 저장소는 **바이너리만** LFS로 관리합니다.

| 구분 | 대상 | 저장 방식 |
|---|---|---|
| LFS | `.png` `.jpg` `.psd` `.tga`, `.fbx` `.blend` `.obj`, `.mp3` `.wav` `.ogg`, `.mp4` `.mov`, `.ttf` `.otf`, `.dll` `.zip` `.pdf` | LFS 오브젝트 |
| LFS (예외) | `*SDF.asset` — TextMesh Pro 폰트 아틀라스 | LFS 오브젝트 |
| 일반 텍스트 | `.cs` `.shader` `.hlsl`, `.unity` `.prefab` `.mat` `.asset` `.meta` `.controller` `.anim` | Git 일반 |

`.asset`은 원래 텍스트지만 **TMP 폰트 아틀라스만 예외**입니다. SDF 텍스처를 통째로 품고 있어 파일 하나가 129MB나 되는데, **GitHub은 100MB를 넘는 파일의 푸시를 거부**하기 때문입니다.

> 💡 이 폰트 아틀라스 두 개(총 258MB)가 저장소 용량의 대부분을 차지합니다. 자세한 내용과
> 개선 방법은 [§4 에셋 추가와 저장소 용량](#4-에셋-추가와-저장소-용량)을 참고하세요.

클론 직후 LFS 파일이 포인터 상태(파일 내용이 `version https://git-lfs...`)로 보인다면:

```bash
git lfs install
git lfs pull
```

### ⚠️ `.gitattributes`에 포괄 규칙을 넣지 마세요

```
Assets/** filter=lfs diff=lfs merge=lfs -text   ← 절대 금지
```

이 한 줄이 위의 확장자별 규칙을 **전부 덮어써서** 소스코드와 씬까지 LFS로 보냅니다. 그러면 다음 문제가 생깁니다.

1. **3-way 병합이 불가능해집니다.** Git이 파일 내용을 모르니 "포인터 2줄"만 놓고 충돌을 냅니다. 서로 다른 줄을 고쳐도 자동 병합되지 않고, 한쪽을 통째로 버려야 합니다.
2. **충돌 해소 중 파일이 깨집니다.** 충돌 마커가 남은 상태에서 `git add`를 하면 LFS clean 필터가 **그 마커 텍스트를 새 LFS 오브젝트로 저장**해버립니다. 결과물은 260바이트짜리 쓰레기 파일이 됩니다.
3. **diff / blame / PR 리뷰가 전부 죽습니다.** 모든 커밋이 `oid sha256:... 2줄 변경`으로만 보입니다.
4. **저장소가 커집니다.** LFS는 버전마다 파일 전체를 저장하지만 Git은 텍스트를 델타 압축합니다.

실제로 이 저장소에서 `PlayerMove.cs`와 `prototype.unity`가 위 2번 경로로 손상되어 복구 작업을 한 적이 있습니다.

---

## 3. 씬 충돌이 났을 때

SmartMerge를 등록했다면 대부분 자동으로 해결되지만, 같은 오브젝트를 양쪽에서 고친 경우는 수동 해결이 필요합니다.

```bash
# 한쪽 버전을 통째로 택하는 경우 — 반드시 이 형태로
git checkout <브랜치명> -- Assets/Scenes/prototype.unity
```

`git checkout --theirs` 후 `git add`를 하는 방식은 LFS 파일에서 위의 2번 손상을 유발할 수 있으니, 브랜치명을 직접 지정하는 위 방식을 쓰세요. 해결 후에는 **Unity에서 씬을 열어 정상인지 확인한 뒤** 커밋하세요.

---

## 4. 에셋 추가와 저장소 용량

### 현재 상태 (2026-07-28 측정)

```
전체 .git         295 MB
├─ LFS 객체       281 MB   ← 이 중 258MB가 TMP 폰트 아틀라스 2개
└─ Git 히스토리    14 MB   ← 코드 + 씬 + 프리팹 전부
```

지금까지 만든 **코드·씬·프리팹을 전부 합쳐도 14MB**입니다. 용량 문제는 사실상 바이너리 에셋 문제입니다.

### 원칙: 한 번 커밋하면 영원히 남습니다

LFS든 일반 Git이든, 커밋된 파일은 **다음 커밋에서 지워도 저장소 용량이 줄지 않습니다.** 히스토리에 남기 때문입니다. 되돌리려면 히스토리 재작성(`git lfs migrate` / BFG)이 필요하고, 그건 **강제 푸시 + 팀 전원 재클론**을 요구합니다.

그래서 **넣기 전에 거르는 것이 유일하게 싼 방법**입니다.

### 에셋을 커밋하기 전에

- **원본 소스 파일을 저장소에 넣지 마세요.** 8K PSD, 무압축 WAV, 고폴리 FBX 원본 등은 구글 드라이브에 두고, **게임에서 실제로 쓸 해상도로 줄인 것만** 커밋합니다.
- 오디오는 `.wav` 대신 `.mp3` / `.ogg`
- **에셋스토어 패키지는 Demo / Example / Documentation 폴더를 지우고 커밋**합니다. 안 쓰는 샘플 씬과 텍스처가 통째로 딸려옵니다.
- 커밋 전에 큰 파일이 섞여 있는지 확인:

```powershell
# Windows
Get-ChildItem Assets -Recurse -File | Where-Object Length -gt 10MB |
  Sort-Object Length -Descending |
  Select-Object @{n='MB';e={[math]::Round($_.Length/1MB,1)}}, FullName
```

```bash
# macOS / Git Bash
find Assets -type f -size +10M -exec ls -lh {} \;
```

### 맵을 만들 때

씬 파일 자체는 텍스트라 델타 압축이 잘 되어 용량 걱정이 거의 없습니다. 다만 **협업을 위해** 다음을 지키세요.

- 맵을 씬 하나에 다 넣지 말고 **방·구역 단위 프리팹으로 쪼갭니다.** 씬 파일은 프리팹 참조만 갖게 되어 작아지고, 두 사람이 서로 다른 방을 만들어도 충돌이 나지 않습니다.
- 반복되는 소품은 **프리팹 배리언트**로
- 규모가 커지면 **additive 멀티 씬**으로 조명 / UI / 레벨을 분리

### GitHub 한도

| 항목 | 한도 | 초과 시 |
|---|---|---|
| 파일 하나 | 100 MB | **푸시 거부** |
| 저장소 | 권장 1 GB, 5 GB부터 경고 | 경고 메일 |
| LFS 무료 | 저장 1 GB + **월 대역폭 1 GB** | 데이터 팩 구매 필요 |

**대역폭이 저장 용량보다 먼저 터집니다.** 클론할 때마다 LFS 객체를 내려받으므로, 5명이 한 번씩만 클론해도 281MB × 5 = 1.4GB로 무료 한도를 넘깁니다. 이미 클론한 사람이 다시 받지 않도록 재클론은 되도록 피하세요.

로컬 디스크만 정리하려면 (원격에는 영향 없음):

```bash
git lfs prune
```

### 가장 효과가 큰 개선: TMP 폰트 아틀라스

`Cafe24PROSlimMax SDF.asset`과 `Maplestory OTF Light SDF.asset`이 각각 **129MB**입니다. 한글 글리프를 전부 미리 구운(Static) SDF 아틀라스라서 이렇게 커졌습니다.

**Dynamic SDF**로 만들거나 아틀라스 해상도를 낮추면 수 MB로 줄어듭니다. Dynamic은 실제로 사용된 글자만 런타임에 굽기 때문에, 대사가 많지 않은 이 프로젝트에는 적합합니다.

다만 이미 히스토리에 들어간 258MB를 실제로 없애려면 히스토리 재작성이 필요하므로, **전시회가 끝난 뒤나 팀원이 적을 때 한 번에 처리**하는 것을 권합니다. 새 폰트로 교체만 해두면 앞으로의 클론 대역폭은 바로 줄어듭니다.
