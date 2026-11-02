# 튜토리얼 씬 기획안 — "강하 전 출격 준비"

2026-11-02 · 브랜치 (구현 시) `feat/TutorialScene`
상태: **기획 검토 대기** — 아래 7장 "결정 필요" 확정 후 구현

---

## 1. 목표

| # | 목표 | 판정 기준 (DoD 초안) |
|---|---|---|
| 1 | 본편에 필요한 **조작을 전부 한 번씩 직접 해 본다** | 2장 조작표의 모든 키를 튜토리얼 안에서 최소 1회 입력해야 다음 단계로 넘어간다 |
| 2 | 본편의 **게임 규칙**(군중전 → 보스 → 강화, 보급, 생체 자원, 목숨, 무기 잠김)을 미리 이해한다 | 각 규칙이 대사·시연으로 1회 이상 등장한다 |
| 3 | **시놉시스를 체험**한다: 강하 직전 모함 → 강하선 탑승 → (인트로 크롤의) 격추 | 튜토리얼 끝이 IntroCrawl 로 자연스럽게 이어진다 |
| 4 | 10분 안에 끝나고, 언제든 **건너뛸 수 있다** | 숙련자 기준 5~7분, Esc 메뉴에 [튜토리얼 건너뛰기] |
| 5 | 본편 상태를 **오염시키지 않는다** | 튜토리얼에서 번 재화·강화·해금은 본편 시작 시 0 / 초기값 |

---

## 2. 가르칠 내용 (실제 키 기준)

> 주의: `InputSystem_Actions.inputactions` 는 게임에서 쓰지 않는다. 실제 키는 `PlayerController.InputSettings.cs` 가 기준이며, 안내 문구의 키 이름은 `KeyGuideList` 에서 가져와 옵션 화면과 항상 일치시킨다.

| 분류 | 키 | 내용 | 튜토리얼 단계 |
|---|---|---|---|
| 이동 | WASD · 마우스 | 이동, 시선 | ① |
| | Left Shift | 질주 (스태미나 소모) | ② |
| | Space | 점프 | ② |
| | Left Ctrl | 구르기 회피 (스태미나, 순간 무적) | ② · ⑦ |
| | F1 | 1인칭 ↔ 3인칭 | ② |
| 사격 | 좌클릭 | 발사 | ③ |
| | 우클릭 | 조준 (지상에서만) | ③ |
| | R | 재장전 (예비 탄창 소모, 탄창 없으면 거부) | ③ |
| | B | 사격 모드 (연사 → 점사 → 단발) | ③ |
| | 1~4 · 휠 | 무기 칸 교체 (1 보조무기 · 2·3 주무기 · 4 보급 전용) | ③ |
| 장비 | G | 수류탄 | ④ |
| | V | 회복 팩 (3초간 회복) | ④ |
| 상호작용 | F | 보급 상자·단말기·탑승 | ⑤ · ⑥ · ⑧ |

규칙 안내: 목숨 3개(자동 부활), 생체 자원 = 처치 보상 재화, 보급 포드는 일정 주기로 자동 낙하·1회용 보급 무기, 스테이지 클리어마다 무기 해금, 보스 등장 시 3인칭 자동 전환, 약점 부위 추가 피해.

---

## 3. 스토리 연출

### 3-1. 배경 — 궤도 모함 「(가칭) 아이언 하버」 강하 격납고
- 시점: **인트로 크롤 직전**. 하이브 근원 행성 궤도에 도착한 모함. 플레이어(전투 사이보그)는 정비 포드에서 깨어나 출격 점검을 받는다.
- 안내자: **모함 관제 AI "오퍼레이터"** (자막 + 타자기 효과). 짧은 군대식 말투.
- 창 밖으로 붉은 하이브 행성이 보인다 (인트로의 `HivePlanet` 셰이더·`DeepSpaceSky` 재사용 → 인트로와 같은 행성).
- 전투 훈련은 **"포획 표본 전투 시뮬레이션"**: 홀로그램 격벽 안에서 실제 하이브 개체(본편 몬스터 프리팹)를 상대. 본편 몬스터를 미리 보여 주는 역할도 한다.

### 3-2. 복선 — 본편과의 연결
| 튜토리얼 | 본편 (불시착 후) |
|---|---|
| 무기고에서 **전 무장을 정상적으로** 써 본다 | 격추로 동력로 파손 → **무장 대부분 잠김**. 스테이지 클리어마다 "동력 공급 재개"로 해금 |
| 모함이 보급 포드를 쏘아 내려 준다 | 같은 모함이 궤도에서 보급 포드를 투하 |
| 정비 단말기에서 생체 자원으로 장비 개조 | 강화 화면(UpgradeScreen)과 같은 화면 |
| 보스 홀로그램 브리핑 "근원 행성의 대형 개체" | 본편 보스 (클리페오사우루스) |
| 강하선 탑승 → 사출 | 인트로 크롤: 대기권 진입 중 격추 |

### 3-3. 오퍼레이터 대사 예시 (톤 확인용)
- ① "기상 확인. 신경 동조 98%. 시선을 움직여 시각 센서를 보정하라."
- ③ "실탄 사격 허가. 표적의 **붉은 부위**를 노려라. 하이브 개체는 약점이 있다."
- ⑤ "궤도 보급 포드 시험 투하. 지상에서도 같은 방식으로 받게 된다. 빛기둥을 찾아라."
- ⑧ "출격 점검 완료. 강하선에 탑승하라. …행운을 빈다, 사냥꾼."

---

## 4. 단계 구성 (격납고 한 방향 동선, 약 40m × 120m)

```
 [⑧ 강하선 사출구]  Spaceship_01 + 사출 게이트
        │
 [⑦ 보스 브리핑실]  보스 홀로그램 · 장판 회피
        │
 [⑥ 정비 단말기]    생체 자원 → 강화 1회 구매
        │
 [⑤ 보급 투하장]    천장 해치에서 포드 낙하 → F
        │
 [④ 전투 시뮬레이터] 하이브 개체 소수 웨이브 · 수류탄 · 회복
        │
 [③ 사격장]        무기고 + 표적 (약점 · 재장전 · 무기 교체)
        │
 [② 기동 훈련로]    질주 · 점프 장애물 · 구르기 게이트 · F1
        │
 [① 정비 포드]      기상, 시선·이동
```

| 단계 | 하는 일 | 완료 조건 (코드 기준) | 문 열림 |
|---|---|---|---|
| ① 기상 | 마우스로 둘러보기 → WASD 로 포드 밖 표시점까지 이동 | 시선 누적 회전 ≥ 180° → 트리거 진입 | 격벽 A |
| ② 기동 | 질주 구간 → 화물 상자 점프 → 레이저 게이트 구르기 통과 → F1 로 3인칭 확인 후 복귀 | `IsSprinting` 2초, `Jumped`, 게이트 안에서 `RollStarted`, `ViewModeChanged` ×2 | 격벽 B |
| ③ 사격 | 표적 사격 → 약점 명중 → 재장전 → B → 우클릭 조준 → 2·3번 무기로 교체 사격 | `DamageDealtEvent`(표적), 약점 명중, `ReloadFinishedEvent`, 사격 모드 변경, 조준, 무기 교체 | 격벽 C |
| ④ 시뮬레이터 | 소수 몬스터 2웨이브 (2웨이브는 뭉쳐 나옴 → 수류탄 유도). 일부러 피해를 받게 한 뒤 V 로 회복. 처치 시 생체 자원 획득 표시 | 웨이브 전멸, `GrenadeExplodedEvent`, 회복 팩 사용. HP 가 10 밑으로 떨어지지 않게 보호 | 격벽 D |
| ⑤ 보급 | 천장에서 무기 포드(로켓 런처) + 수류탄 포드 낙하 → F 로 회수 → 4번 칸 로켓으로 표적 파괴 | `SupplyCollectedEvent` ×2, 보급 무기 발사 | 격벽 E |
| ⑥ 정비 | 단말기에 F → 강화 화면 → 하나 구매 → 닫기 | `UpgradePurchasedEvent` → `RewardSelectionFinishedEvent` | 격벽 F |
| ⑦ 브리핑 | 보스 홀로그램 등장 → **자동 3인칭 전환** 시연 → 바닥 경고 장판 3회 → 구르기·이동으로 회피 → 홀로그램 약점(발광 구체) 사격 | 장판 3회 버티기, 약점 구체 전부 파괴 | 사출구 |
| ⑧ 출격 | 강하선에 F → 페이드아웃 → `IntroCrawl` | 상호작용 | — |

공통 규칙
- 각 단계는 **목표 패널(우상단)** + **오퍼레이터 자막(하단)** + **키 힌트 배지**로 안내한다.
- 목표를 일정 시간(예 20초) 못 하면 힌트를 한 번 더 띄운다.
- 튜토리얼 중에는 사망하지 않는다 (HP 하한 보호). 목숨 개념은 ④에서 대사로만 설명.

---

## 5. 기술 설계

### 5-1. 씬과 흐름
```
Test_MainMenu ──GAME START──▶ Tutorial_Hangar ──탑승──▶ IntroCrawl ──▶ Monster Player Integration
              (튜토리얼 완료 기록이 있으면 바로 IntroCrawl)
```
- 완료 기록: `PlayerPrefs "Tutorial.Done"`. 메인 메뉴에 [TUTORIAL] 버튼 추가로 다시 볼 수 있게 (7장 결정 B).
- 씬 전환은 기존 `SceneTransition.LoadAsync` 그대로 사용. 새 씬은 Build Settings 에 등록.
- 씬에 `RunDirector` 를 **두지 않는다** → 라운드·해금·LoadoutPanel 이 동작하지 않고, 튜토리얼이 흐름을 직접 쥔다.

### 5-2. 새 스크립트 (`Assets/Scripts/Tutorial/`, `HybridShooter.Tutorial`)
| 클래스 | 역할 |
|---|---|
| `TutorialDirector` | 단계 목록을 차례로 실행, 격벽 열기, 건너뛰기, 종료 시 상태 초기화 + 씬 전환 |
| `TutorialStep` (추상) + 단계별 구현 | `Begin / IsComplete / End`. EventBus·`PlayerController` 이벤트를 구독해 완료 판정 |
| `TutorialScript` (ScriptableObject) | 단계별 대사·목표 문구·힌트. `IntroCrawlText` 와 같은 방식이라 기획자가 인스펙터에서 수정 |
| `TutorialHud` | 목표 패널 · 오퍼레이터 자막(타자기) · 키 배지. `HudFactory` + `GameFlowUiSkin(DialogBox)` 재사용 |
| `TrainingTarget` | `IDamageable` 표적. 약점 콜라이더, 맞으면 쓰러졌다 다시 서기 |
| `TutorialGate` | 격벽 문 (열림 연출 + 콜라이더 해제) |
| `TutorialTerminal`, `BoardingPoint` | `IInteractable` (F) — 강화 화면 열기 / 탑승 |
| `TutorialHazardTelegraph` | ⑦ 경고 장판 → 일정 시간 뒤 피해 |
| `Editor/TutorialHangarBuilder` | `Tools/Map/Build Tutorial Hangar` — Sci-Fi Base 격납고 에셋으로 씬 조립 (기존 `SciFiMapBuilder` 도우미 재사용) |

### 5-3. 기존 시스템 연결 (수정 최소화)
| 필요 | 방법 | 기존 코드 수정 |
|---|---|---|
| 몬스터 웨이브 | `IMonsterFactory.Spawn` 으로 지정 위치에 소환 (직접 추적 모드) | 없음 |
| 생체 자원 | 씬에 `KillRewardCollector` 배치 → 처치 시 자동 적립. 튜토리얼 HUD 에 잔액 표시 | 없음 |
| 보급 포드 | 씬에 `SupplyDropper` 배치, 자동 주기 없이 `Drop(def)` 만 호출. 낙하 위치는 마커로 지정 | **소**: 위치 지정 Drop 오버로드 추가 필요할 수 있음 |
| 강화 화면 | `UpgradeManager` + `UpgradeScreen` 배치, 단말기에서 `GameStateChangedEvent(→Reward)` 발행 / 끝나면 `→CrowdBattle` (`KillRewardTester` 와 같은 방식) | 없음 |
| 3인칭 자동 전환 | `CameraModeRequestedEvent(ThirdPerson)` | 없음 |
| 전 무장 사용 | 튜토리얼 동안 `IWeaponUnlocks` 를 "전부 해금" 대체 서비스로 등록, 끝나면 원래 서비스 복원 | 없음 (ServiceLocator 교체) |
| 사망 방지 | `PlayerDebugMode` 무적 또는 HP 하한 회복 | 확인 후 결정 |
| 키 이름 | `KeyGuideList` 재사용 | 없음 (접근자 public 여부 확인) |

### 5-4. 상태 오염 방지 (중요)
`CoreBootstrap` 의 서비스(재화 지갑, 강화 장부, 해금 장부)는 **씬을 넘어 유지되는 정적 객체**다. 튜토리얼 종료·건너뛰기 시 반드시:
- `ICurrencyWallet.ResetTo(0)` / `IUpgradeState.ResetAll()`
- 대체한 `IWeaponUnlocks` → 원래 `WeaponUnlockLedger` 로 복원 (PlayerPrefs `Unlock.Weapons` 는 건드리지 않음)
- `GameState` 를 Reward 로 둔 채 나가지 않기, `Time.timeScale = 1`

### 5-5. 맵 — 모함 격납고

**구성 원칙**: 실내 모듈 키트로 **복도·방의 껍데기**(벽·바닥·천장)를 짜고, Sci-Fi Base 장비로 **넓은 격납고 공간**을 채우는 혼합 구성. 모든 머티리얼이 이미 URP Lit 이라 변환 작업은 없다.

| 용도 | 에셋 | 쓰는 곳 |
|---|---|---|
| 실내 바닥·벽·천장 | `ExternalAssets/Sci-fi-Weapons/SciFiWorld/Modules`: `MFloor350x350`, `MWall350x350`, `MWall700x350`, `MCeiling700x700`, `MCeilingTrim250x250`(발광 조명 띠, `MTrimLights01A`) | ① 정비 포드실, ② 기동 훈련로, ③ 사격장, ⑥ 정비실, ⑦ 브리핑실 — 복도형 구간 |
| 격납고 갑판 | `Sci-Fi Base`: `Hangar_Ground_Plate1~5` | ④ 시뮬레이터, ⑤ 보급 투하장, ⑧ 사출구 — 넓은 구간 |
| 격납고 벽·창 | `Wall_Hangar1`, `Wall_Hangar1_Window`, `Wall_Large_*` | 갑판 외벽, 창 너머 행성 |
| 격벽 문 | `Wall1_Gate` (+ 모듈 벽 조각) | 단계 사이 문 — 코드로 미끄러져 열림 (`TutorialGate`) |
| 기계·배관 | `Crane_01`, `Generator_Set_*`, `Tank_*`, `Pipe2/3_*`, `Powerbox_*`, `Radiator_Big` | 격납고 벽면·천장 쪽 장식 |
| 화물·장애물 | `Container_01`, `Prop1~10`, `Prop_Barrel1*` | ② 점프 장애물, 갑판 화물 |
| 칸막이·엄폐 | `Barrier_*`, `Cover Panel*` | ③ 사격 부스, ④ 시뮬레이터 엄폐물 |
| 조명 | `Lamp_03_B`, `Lamp_05`, `Lamp_Position` + 모듈 `MCeilingTrim` | 실내광, 갑판 유도등 |
| 무기고 | `SciFiWorld/Weapons`: `WeaponStorageCase01/02`, `SciFiWeaponCase01`, 진열용 총기 모델(`SciFiRifle`·`SciFiSMG`·`SciFiShotGun`·`SciFiSniperRifle`·`SciFiRocketLauncher`) | ③ 사격장 입구 무기 거치대 (연출용, 실제 무기는 `GunSlot`) |
| 보급·의료 소품 | `SciFiAmmoBox01`, `SciFiMedKit01`, `SciFiMedPack01` | ③ 재장전 안내, ④ 회복 팩 안내 |
| 강하선 | `Assets/Scenes/GU/Prefabs/Spaceship_01.prefab` | ⑧ 사출구. 메인 메뉴·본편의 `Spaceship_Broken` 과 같은 기체의 정상 모습 |
| 창 밖 행성·우주 | `Prefabs/Intro/PlanetSphere` + `HivePlanet.mat`, `DeepSpaceSky.shader` | 격납고 창, 사출구 개방 시 — 인트로와 같은 행성 |
| 홀로그램·경고 효과 | `ExternalAssets/QFX/Sci-Fi VFX` | ④ 시뮬레이터 격벽, ⑦ 보스 홀로그램·경고 장판 |

- 조명: 차가운 청백색 실내광 + 경고등(주황) — 본편의 붉은 외계 대기와 대비되는 "마지막 안전지대" 느낌
- 천장: 복도형 구간은 `MCeiling700x700`, 격납고 갑판은 높은 천장(모듈 천장 + 크레인 레일). ⑤ 보급 투하장만 천장 해치를 열어 포드가 떨어진다.

**제약·확인 사항**
- 실내 모듈은 5종뿐(문·창·계단 없음) → 반복 배치가 단조롭지 않게 구간마다 Sci-Fi Base 장비·조명 색을 다르게 섞는다.
- 열리는 문 모델이 없다 → `Wall1_Gate`·모듈 벽 조각을 `TutorialGate` 가 이동시켜 대체.
- 모듈 이름의 `350`·`700` 이 3.5m·7m 인지 첫 배치에서 Renderer Bounds 로 확인한다 (빌더는 기존 방식대로 Bounds 를 재서 배치).
- Sci-Fi Base 는 야외 키트라 천장 부품이 없다 → 천장은 모듈로만 덮는다.

---

## 6. 작업 순서 (예상)

1. 격납고 빌더 + 씬 (`Tutorial_Hangar.unity`), NavMesh
2. `TutorialDirector` · `TutorialStep` · `TutorialHud` · `TutorialScript` 뼈대 + ①② 단계
3. ③ 사격장 (`TrainingTarget`, 무기고) · ④ 시뮬레이터
4. ⑤ 보급 · ⑥ 정비 단말기
5. ⑦ 보스 브리핑 · ⑧ 탑승 → IntroCrawl 전환, 건너뛰기, 상태 초기화
6. 메인 메뉴 연결 · Build Settings 등록 · 플레이 테스트

---

## 7. 결정 필요 (검토 요청)

| # | 질문 | 제안 (기본안) |
|---|---|---|
| A | 맵: 기존 `TutorialMap.unity`(불시착 후 협곡)를 버리고 **모함 격납고**로 새로 만드는가? | **새로 만든다.** 요청한 "출발 전" 설정과 맞지 않음. 기존 협곡 씬은 삭제하지 않고 그대로 둔다 (맵 담당과 협의) |
| B | 진입 방식 | 첫 GAME START 시 자동 진행 + 메인 메뉴 [TUTORIAL] 버튼으로 재진입 + 튜토리얼 중 Esc → 건너뛰기 |
| C | ⑦ 보스 브리핑 범위 | 실제 보스 프리팹 대신 **홀로그램(재질만 바꾼 정지 모델) + 경고 장판 + 약점 구체 표적**. 실제 보스 AI는 쓰지 않음 |
| D | 대사 분량·안내자 이름 | 단계당 2~4줄, 이름 "오퍼레이터". 시나리오 담당이 `TutorialScript` 에셋에서 수정 |
| E | 음성/BGM | 이번 범위는 자막 + 기존 UI 효과음. 튜토리얼 전용 BGM 은 추후 |

---

## 8. 다른 PC에서 이어서 작업할 때 (인계)

### 8-1. 현재 상태 (2026-11-02)
- [x] 시놉시스·기존 시스템 조사 → 이 기획안 작성
- [x] 격납고용 에셋 조사 → 5-5 반영
- [ ] **7장 결정 A~E 확정** ← 다음 할 일. 확정되면 표의 "제안" 칸을 결과로 바꾸고 6장 순서대로 구현
- 코드·씬·프리팹은 **아직 하나도 만들지 않았다.** 이 문서만 커밋됨 (브랜치 `feat/TutorialScene`)

### 8-2. 조사로 확인한 사실 (다시 찾지 않아도 되는 것)
| 항목 | 내용 | 위치 |
|---|---|---|
| 시놉시스 | 연합군 전투 사이보그, 하이브 근원 행성 섬멸 작전, 대기권 진입 중 격추 → 불시착, 무장 대부분 잠김, 보급 포드·생체 자원으로 버팀 | `Assets/Prefabs/Intro/IntroCrawlText.asset` |
| 실제 조작 키 | `.inputactions` 는 **안 쓴다**. 키 정의는 코드 | `Scripts/Player/Controller/PlayerController.InputSettings.cs`, 소비는 `PlayerController.Input.cs`, 키 이름 목록은 `Scripts/UI/Options/KeyGuideList.cs` |
| 플레이어 프리팹 | 1인칭 시작, 4칸 무기 | `Assets/Scenes/MS/Prefabs/Player.prefab` |
| 플레이어 이벤트 | `Jumped`, `Landed`, `RollStarted/Ended`, `ViewModeChanged`, `IsSprinting` | `PlayerController.API.cs` |
| 입력 잠금 | `SetMenuInputBlocked(bool)`, `IPlayerCinematic.SetCinematic(bool)` | `PlayerController.Input.cs:19`, `PlayerController.Cinematic.cs:16` |
| 시점 강제 전환 | `CameraModeRequestedEvent(mode, duration)` | `Core/Events/GameEvents.cs` |
| 무기 칸·해금 | `GunSlot.ApplyLoadout`, `EquipSupplyWeapon(id)`, `GunSlot.IsUnlocked` 는 `IWeaponUnlocks` 서비스로 판정 | `Scripts/Weapon/GunSlot.cs:274,282,347`, `Core/Services/WeaponUnlockLedger.cs` |
| 상호작용 | `IInteractable` 구현 → F 키·`InteractPromptHud` 자동 연동 | `Core/Contracts/InteractionContracts.cs`, `Player/Interaction/PlayerInteractor.cs` |
| 보급 | `SupplyDropper.Drop(def)` / `DropRandom()` 수동 호출 가능, 자동 주기는 `StartAutoDropCycle` | `Scripts/Supply/SupplyDropper.cs:119,137,309` |
| 재화 | 처치 시 `KillRewardCollector` 가 `ICurrencyWallet` 에 자동 적립. 인게임 HUD 에 잔액 표시는 없음 | `Scripts/Reward/KillRewardCollector.cs`, `Core/Services/DefaultServices.cs:328` |
| 강화 화면 | `UpgradeManager`+`UpgradeScreen` 배치 후 `GameStateChangedEvent(→Reward)` 발행으로 열림 | `Scripts/Reward/UpgradeManager.cs:183`, 예시 `KillRewardTester.cs:137` |
| 몬스터 단독 소환 | `ServiceLocator.Get<IMonsterFactory>().Spawn(DataManager.Monsters.Get(id), pos, rot)` — 크라우드 없이 직접 추적 | `Monster/.../MonsterController.cs`, `MonsterMovement.cs:169` |
| 서비스 수명 | `CoreBootstrap`(BeforeSceneLoad) 서비스는 **씬을 넘어 유지** → 5-4 초기화 필수 | `Core/Services/DefaultServices.cs:16` |
| 씬 전환 | `SceneTransition.LoadAsync(name, minWait, token)`, Build Settings 등록 필요 | `Scripts/UI/MainMenu/SceneTransition.cs:20` |
| 현재 씬 흐름 | `Test_MainMenu` → (씬에서 gameSceneName = `IntroCrawl`) → `IntroCrawlDirector.nextSceneName` = `Monster Player Integration` | `MainMenuController.cs:65`, `IntroCrawlDirector.cs:89` |
| Build Settings 경로 | `Test_MainMenu` 경로가 옛 위치(`Scenes/GU/`)로 남아 있음. 실제는 `Scenes/TestScenes/` (GUID 같음) | `ProjectSettings/EditorBuildSettings.asset` |
| 기존 튜토리얼 맵 | 불시착 후 협곡 컨셉, 마커만 있음, 로직 없음. 결정 A 대상 | `Assets/Scenes/GU/TutorialMap.unity`, `Scenes/GU/Map/Editor/TutorialMapBuilder.cs`, `Docs/Design/MapDesign_SciFiBase.md` 5장 |
| 재사용 UI 조각 | `HudFactory`, `GameFlowUiSkin`(DialogBox), `InteractPromptHud`, `TyranopodeAnalysisHud`(타자기 배너) | `Scripts/UI/`, `Scripts/UI/Loadout/` |
| 디버그 키 주의 | F12 보스 스킵, ` 디버그 패널, F8~F11 테스터 — 튜토리얼 키와 겹치지 않게 | `RoundSequencer.cs:142` 등 |

### 8-3. Claude Code 로 이어서 할 때
- 브랜치 `feat/TutorialScene` 을 pull 받고 이 문서를 첨부한 뒤, 예를 들어 **"7장 결정은 기본안으로 확정, 6장 1단계부터 구현"** 처럼 요청한다.
- Unity 버전 **6000.3.15f1**
