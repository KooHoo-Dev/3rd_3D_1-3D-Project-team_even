# 보급 시스템 · 라운드 종료 업그레이드 개편 — 설계 검토안

> 상태: **검토 요청 (구현 전)**  ·  작성일 2026-10-31  ·  브랜치 `chore/SupplySystemFix`
> 기준 문서: 「이벤트 버스와 외부 데이터 적용 기준」 v0.1
> 이 문서의 **5장 확인 항목**에 답을 받은 뒤 구현을 시작한다.

---

## 1. 현재 구조 분석 (연관 시스템)

```
RunDirector ──(직접 호출)──▶ RoundSequencer ──(직접 호출 Start/StopAutoDropCycle)──▶ SupplyDropper
   │  RoundStartedEvent / RoundCleanupEvent / GameStateChangedEvent / StageClearedEvent            │ Instantiate
   ▼                                                                                               ▼
UpgradeManager ◀──(C# event Changed)── UpgradeScreen                                        SupplyCrate
   │ IUpgradeState.TryPurchase / ICurrencyWallet                                                │ OnTriggerEnter (자동 획득)
   ▼                                                                                            ▼
RewardSelectionFinishedEvent ──▶ RoundSequencer → RunDirector 다음 라운드          IItemUser(PlayerItems) · IPlayerAmmo(GunSlot)
                                                                                                │
                                         SupplyDroppedEvent / SupplyCollectedEvent ◀────────────┘
                                         구독: SupplyHud · GameSoundRouter.Items · SupplyBeaconOverride · PlaytestAnalyzer
```

| 시스템 | 파일 | 보급·업그레이드와의 관계 |
|---|---|---|
| 투하 | `Supply/SupplyDropper.cs` | 종류 추첨 → 위치 탐색(링 + 지면 레이캐스트 + Terrain 높이) → 상자 생성. 상한 `SupplyMaxActive`, 실패 시 재시도 |
| 상자 | `Supply/SupplyCrate.cs` | 낙하·착지, **트리거 접촉 시 자동 획득**, 빛기둥 |
| 위치 후보 | `Core/Services/DefaultServices.cs` `TryGetSupplyDropPosition` | 플레이어 기준 XZ 링 균등 분포 |
| 라운드 | `Round/RoundSequencer.cs` | `SupplyStartTime` 에 자동 투하 시작, **`BeginBossIntro` 에서 투하 중지** |
| 런 | `Round/RunDirector.cs` | **항상 `DataManager.GetRound(1)` 사용**, 라운드 사이 `RoundCleanupEvent(false)` → 다음 `RoundStartedEvent` |
| 상호작용 | `Player/Interaction/PlayerInteractor.cs`, `Core/Contracts/InteractionContracts.cs`, `UI/InteractPromptHud.cs` | 조준선으로 `IInteractable` 탐색, F 키 실행, `InteractFocusChangedEvent(InteractPrompt)` → 프롬프트 HUD. **이미 구현되어 있음** |
| 보급 무기 칸 | `Weapon/GunSlot.cs` (`SupplySlot = 4번`, `ISupplyWeaponUser`) | 장착·교체·라운드 정리 시 제거. 현재 규칙: **탄이 떨어져도 칸에 남음** |
| 보급 무기 테스트 픽업 | `Weapon/Pickup/SupplyWeaponPickup.cs` | `IInteractable` 예시 구현 (상자 연동은 보급 담당 몫으로 남겨둠) |
| 아이템 | `Player/Item/PlayerItems.cs` (`IItemUser`) | 수류탄·힐팩. **라운드 사이 유지**, FullReset 때만 초기화 |
| 탄약 | `Weapon/Gun.cs` | **모든 `RoundCleanupEvent` 에서 탄약을 시작값으로 리셋** (`ResetForPickup`) |
| 강화 | `Reward/UpgradeManager.cs`, `UI/UpgradeScreen.cs`, `UpgradeTable.tsv` | **라운드당 1회 구매** (`HasPurchased`) — 구매/종료 조건이 Manager·Screen 양쪽에 퍼져 있음 |
| 데이터 | `SupplyTable`, `RoundTable`, `ConfigTable`, `WeaponTable`, `UpgradeTable` | 기존 TSV → DTO → Def |
| 테스트 도구 | `Supply/Tester/SupplyTester.cs`, `TesterPlayer.cs` | `TesterPlayer` 는 `IItemUser` 만 구현, 접촉 자동 획득에 의존 |

---

## 2. 설계 전 보완점 (현재 코드·데이터에서 발견)

| # | 문제 | 영향 | 제안 |
|---|---|---|---|
| P1 | **Ground 레이어 판정이 없음.** 씬의 `groundMask = Everything`. 맵(`Resources/Map/Map.prefab`)에서 Ground(3) 레이어는 `ReductedTerrain` 하나뿐이고 건물은 Buildings(7), 나무는 Trees(9) | 지붕·바위·건물 위에 착지 가능 | 레이캐스트는 **모든 레이어**로 쏘고, **첫 충돌이 Ground 레이어일 때만** 허용. (Ground 만 마스크로 쏘면 지붕을 뚫고 건물 안 지면에 떨어짐) |
| P2 | 높이 제한은 `Terrain.SampleHeight >= 15` 이면 금지 (Terrain 원점 기준 높이) | 요구 "Height 5 이하" 와 기준·경계가 다름 | `SupplyMaxTerrainHeight = 5`, 판정을 `> 5` 금지(= 5 이하 허용)로 변경. **기준(Terrain 로컬 vs 월드 Y)은 Q1 확인** |
| P3 | 거리 기본값 불일치: 테이블 20/100, 코드 폴백 30/150 (`SupplyDropper`, `DefaultServices` 두 곳) | 테이블 누락 시 다른 값으로 동작 | 테이블 20/60, 코드 폴백도 20/60 으로 맞춤 |
| P4 | 씬에 저장된 `maxPositionTries = 10` 이 코드 기본값 20 을 덮음 | Ground+높이 5+링 60m 로 조건이 좁아져 실패율 급증 예상 | 후보 탐색 횟수 상향(30) — 씬 값도 함께 갱신. 레이캐스트만이라 비용 작음 |
| P5 | 상자가 **트리거 접촉 자동 획득** | F 키 요구와 충돌 | `OnTriggerEnter/Stay` 획득 제거 → `IInteractable` 구현 |
| P6 | `PlayerInteractor` 는 `CanInteract == false` 면 프롬프트 자체를 숨김 | "가득 차서 못 먹는 상자"에 마우스를 대도 정보가 안 뜸 | 착지한 상자는 항상 `CanInteract = true`, 못 받는 경우는 `InteractPrompt.Warning` 으로 표시하고 `Interact` 가 false 반환 (Q4) |
| P7 | 보스 인트로에서 `StopAutoDropCycle` | 보스전 보급 없음 | 인트로(컷신·카메라 전환) 동안은 정지, `BossBattle` 진입 시 재개 (Q3) |
| P8 | `RunDirector` 가 항상 RoundTable **1행**만 사용 | RoundTable 2행 이후의 `SupplyStartTime/Interval` 은 무시됨 (BossId 만 진행 라운드로 조회) | 이번 범위에서는 유지. 라운드별 보급 값이 필요해지면 그때 수정 (기록만) |
| P9 | `ISupplyWeaponUser` 계약 문구 "탄이 떨어져도 칸에 남는다" | 보급 무기 제안("다 쓰면 사라짐")과 정면 충돌 | 보급 무기 확정 시 계약·GunSlot 규칙 변경 |
| P10 | 산탄총·저격총·SMG 는 이미 **Loadout 무기**(2004~2006)이고, `GunSlot` 은 무기 Id 당 **Gun 인스턴스 하나**를 2·3번 칸과 보급 칸이 공유 | 같은 총을 2번 칸과 보급 칸에 동시에 둘 수 없음 | 보급 전용 행(예: 2009 보급 산탄총)을 `Category = Supply` 로 추가 + 플레이어 프리팹에 총 오브젝트 추가 필요 (플레이어 담당 협의) |
| P11 | 강화 "1회 구매" 규칙이 `UpgradeManager`(CanFinish, canBuy) 와 `UpgradeScreen`(locked, purchasedThis, 건너뛰기 문구) 에 나뉘어 있음 | 다회 구매로 바꿀 때 두 곳 모두 수정 | Manager 가 규칙 소유, Screen 은 표시만 (아래 C 설계) |
| P12 | **깨진 UI 추정 원인**: 빌드 씬 `Monster Player Integration`(그리고 `TestMap_V2`, `SupplyImprovement`)의 `UpgradeScreen` 스프라이트·글꼴 **17칸이 전부 비어 있음** → 코드가 단색 사각형으로 대신 그림. 원본 `GU/RewardAndUpgradeSystem.unity` 에는 모두 연결돼 있음 | 강화 화면이 단색 박스로 보임 | Q5 확인 후 수정 (아래 C-1) |
| P13 | 탄약은 **매 라운드 시작값으로 리셋**, 수류탄·힐팩은 **유지** | "다음 라운드 탄약 +2" 의 의미가 '시작값 + 2' 가 됨 | Q6 확인 |
| P14 | 테스트 씬 `SupplyPackTestscene` 의 `TesterPlayer` 는 `PlayerInteractor` 가 없음 | 자동 획득 제거 시 테스트 씬에서 못 먹음 | `TesterPlayer` 에 F 키 상호작용(간단한 조준 레이캐스트) 추가 |

---

## 3. 설계안

### A. 보급 시스템 개편 (확정 요구)

#### A-1. F 키 획득 + 마우스 오버 정보 — `SupplyCrate : IInteractable`

- `CanInteract(user)` : `CurrentState == Landed && removed == false` (낙하 중에는 상호작용 불가)
- `GetPrompt(user)` : 가벼운 계산만 (매 프레임 호출 가능)
  - Title: `Def.DisplayName` (예: "수류탄 팩")
  - Description: "수류탄 1~3개 · 예비 탄창 충전" (Def 의 AmountMin/Max 사용)
  - ActionLabel: "획득", IconKey: SupplyTable 신규 열 `IconKey` (비우면 없음)
  - Warning: 아이템·탄약 모두 가득이면 "모두 가득 차 있습니다"
- `Interact(user)` : 기존 `TryCollect` 로직(직접 호출로 지급 → 실제 지급량 계산 → `SupplyCollectedEvent` → `Remove`) 재사용. 하나도 못 받으면 false
- `OnTriggerEnter/Stay` 자동 획득 **삭제**. 트리거 콜라이더는 조준선 대상으로 유지 (`PlayerInteractor` 는 트리거 포함으로 쏨)
- UI(프롬프트 HUD)는 **플레이어 담당** — 이미 있는 `InteractFocusChangedEvent` → `InteractPromptHud` 경로를 그대로 사용. 보급 쪽은 `InteractPrompt` 내용만 채움

#### A-2. 낙하 지점: Ground 레이어 + 높이 5 이하 + 거리 20~60m

```
for 후보 in 최대 N회:
    candidate = ISpawnZoneProvider 후보 → 플레이어 기준 링(20~60m) 보정
    hit = Raycast(위→아래, 모든 레이어, 트리거 무시)
    if hit.layer ∉ allowedGroundLayers → 다시      // 지붕·바위·나무 위 금지
    if 높이(hit) > SupplyMaxTerrainHeight(5) → 다시
    return hit.point
```

- `allowedGroundLayers` : **Inspector `LayerMask`** (기준 문서: raycast 참조 레이어 = Inspector). 값이 비어 있으면 `LayerMask.GetMask("Ground")` 로 폴백
- 높이 5, 거리 20/60 : **ConfigTable** (기존 행 값만 수정)
- 탐색 실패 재시도 구조(1초 뒤 최대 5번)는 그대로

#### A-3. 보스 스테이지 투하 — `RoundSequencer` 직접 호출 (기준 문서: 라운드가 Supply 진행 순서를 소유)

| 단계 | 투하 |
|---|---|
| CrowdBattle | `SupplyStartTime` 이후 `SupplyInterval` 마다 (현행) |
| BossIntro | **정지** (컷신·카메라 전환 중, 상자가 보이지 않음) |
| BossBattle | **재개** — `StartAutoDropCycle(interval, dropImmediately: false)` 로 한 간격 뒤부터 (Q3) |
| Reward 이후 | `RoundCleanupEvent` 로 정리 (현행) |

필드에 남은 군중전 상자는 보스전에서도 그대로 유지 (현행 동작).

#### A-4. 통신·데이터 판정 (기준 문서 적용)

| 통신 / 데이터 | 방식 | 근거 |
|---|---|---|
| F 입력 → 상자 획득 | **직접 호출** (`PlayerInteractor → IInteractable.Interact`) | 결과(성공/실패)가 다음 처리(프롬프트 갱신) 조건 |
| 상자 → 플레이어 지급 | **직접 호출** (`IItemUser`, `IPlayerAmmo`) | 지급 결과로 상자 유지/제거 결정 |
| 획득 사실 → HUD·사운드·분석 | 기존 `SupplyCollectedEvent` 유지 | 독립 구독자 3개 (변경 없음) |
| 마우스 오버 정보 → HUD | 기존 `InteractFocusChangedEvent` 유지 | 플레이어 모듈이 이미 소유 |
| 보스전 투하 재개 | **직접 호출** (`RoundSequencer → SupplyDropper`) | 라운드가 시작·중단 순서 소유 |
| 거리·높이 | ConfigTable 기존 행 | 반복 튜닝 |
| 지면 레이어 | Inspector | 씬·레이어 연결 정보 |
| 상자 아이콘 | SupplyTable `IconKey` 열 추가 | 종류별 콘텐츠 작성 정보 |

새 이벤트·새 테이블 없음.

---

### B. 1회성 보급 무기 (설계 제안만 — 미확정)

#### B-1. 데이터

- **SupplyTable** : `SupplyType` 에 `Weapon` 추가, `WeaponId` 열 추가 (Weapon 행만 사용, 그 외 0). 무기 종류별로 한 행 → 무기마다 **가중치·빛기둥 색·아이콘**을 표에서 조정
  ```
  4101  Weapon  샷건 보급      WeaponId=2009  Weight=8   BeaconColor=#FFD24D ...
  4102  Weapon  저격총 보급    WeaponId=2010  Weight=6   ...
  4103  Weapon  로켓 런처 보급 WeaponId=2007  Weight=4   ...
  4104  Weapon  유탄 발사기 보급 WeaponId=2008 Weight=4   ...
  4105  Weapon  SMG 보급       WeaponId=2011  Weight=8   ...
  ```
  `SupplyDef` 검증: Weapon 행은 `WeaponId` 가 WeaponTable 에 있고 `Category = Supply` 여야 함 (`AmountMin/Max` 검사는 Weapon 이 아닐 때만)
- **WeaponTable** : 보급 전용 행 3개 추가 (`Category = Supply`, `UnlockOrder = 0`). 탄 수는 `MagazineSize + StartMagazines`, `MaxMagazines = StartMagazines`, `SupplyMagazineRatio = 0` (보급 상자로 충전 안 됨)
- **플레이어 프리팹** : 보급 전용 Gun 오브젝트 3개 추가 (P10 — 플레이어/무기 담당 작업)

#### B-2. 획득 — 같은 `SupplyCrate` 가 처리

- `SupplyType == Weapon` 이면 `Interact` 에서 `user.GetComponentInParent<ISupplyWeaponUser>().EquipSupplyWeapon(WeaponId)` **직접 호출**
- 프롬프트는 현재 `SupplyWeaponPickup.GetPrompt` 내용(무기명 · 1회용 · 탄 수 · "현재 ○○ 은(는) 버려집니다")을 그대로 이동
- 이미 같은 무기를 들고 있으면 Warning "이미 보유 중" + 획득 불가
- 획득 사실은 기존 `SupplyCollectedEvent`(Def.SupplyType = Weapon) + `GunSlot` 이 이미 발행하는 `SupplyWeaponChangedEvent` 로 충분 → **새 이벤트 없음**
- `SupplyWeaponPickup` 은 테스트용으로 유지하거나 삭제

#### B-3. 탄을 다 쓰면 사라짐 — `GunSlot` 내부 처리

- `Gun` 에 **지역 C# 이벤트** `Depleted` (마지막 탄 발사 후 `IsOutOfAmmo` 가 된 순간) 추가 → `GunSlot` 이 보급 칸 총만 구독
- `GunSlot` : 발사 동작이 끝난 뒤(다음 프레임 또는 발사 애니메이션 종료 후) `ReturnSupplyWeapon` → 이전에 들던 칸으로 전환 → 기존 `SupplyWeaponChangedEvent(prev, 0)` 발행 (슬롯 HUD 가 이미 구독)
- 전역 버스 아님: 같은 무기 모듈(Gun ↔ GunSlot) 안의 협력 (기준 문서 표 "성공한 발사 → 무기 자체" 와 같은 범주)
- `ISupplyWeaponUser` 계약 문구 수정: "탄이 떨어지면 칸에서 사라진다"

#### B-4. 전용 슬롯

이미 4번 보급 칸이 있음 (`GunSlot.SupplySlot`, 숫자 4 키 / 획득 시 자동으로 꺼내 듦). 추가 작업 없음.

---

### C. 라운드 종료 후 업그레이드 개선

#### C-1. 깨진 UI 복구 (Q5 확인 후)

원인(추정, P12): 빌드 씬의 `UpgradeScreen` 에 스프라이트·글꼴이 하나도 연결되지 않아 단색 박스로 그려짐.

| 방법 | 내용 | 장단점 |
|---|---|---|
| ① 씬마다 연결 | 원본 씬 값을 통합 씬 3곳에 복사 | 가장 빠름. 새 씬마다 다시 연결해야 함 |
| **② 스킨 에셋 (권장)** | `Resources/UI/UpgradeUiSkin.asset`(ScriptableObject)에 스프라이트·글꼴을 모으고, `UpgradeScreen` 은 칸이 비었으면 스킨에서 읽음 | 이미 같은 방식(`GameFlowUiSkin`)이 있음. 여러 씬이 같은 설정을 공유하는 **실제 필요**가 있어 기준 문서의 SO 조건 충족 |
| ③ 프리팹화 | `UpgradeManager + UpgradeScreen` 프리팹 | Unity 에디터 작업 필요, 기존 씬 교체 필요 |

추가로, 다회 구매로 바뀌면 카드 상태 문구("구매 완료" 잠금 등)도 함께 고쳐야 화면이 어긋나지 않음 (C-3).

#### C-2. 다음 라운드 시작 보급 (탄약·수류탄·힐팩 각 2)

- 신규 컴포넌트 `RoundStartSupply` (Supply 모듈) : `RoundStartedEvent` 구독, **`Round >= 2` 일 때만** 지급 (재시작 = Round 1 은 제외)
  - `IItemUser.AddGrenades(2)`, `AddHealPacks(2)`, `IPlayerAmmo.AddMagazinesToAll(2)` — 직접 호출
  - 최대치 초과분은 버려짐 (기존 Add 규칙)
- 이벤트 판정: "새 라운드가 시작됐다"는 사실을 인벤토리 지급 규칙이 독립적으로 관찰 · RunDirector 는 결과가 필요 없음 · 이미 `RoundStartedEvent` 가 존재 → 구독이 적합. `RunDirector` 에 플레이어 규칙을 넣지 않음
- 순서: 탄약 리셋은 앞선 `RoundCleanupEvent` 에서 끝나므로 `RoundStartedEvent` 구독자 간 순서 의존 없음
- 수량: **ConfigTable** `RoundStartGrenades / RoundStartHealPacks / RoundStartMagazines = 2` (반복 튜닝 값, 라운드별 변형 요구 없음 → RoundTable 대신 Config)
- 알림 UI: 현재 소비자 없음 → 이벤트를 새로 만들지 않음 (Q7 에서 필요하다고 하면 그때 추가)

#### C-3. 재화가 있으면 여러 번 업그레이드

| 위치 | 변경 |
|---|---|
| `UpgradeManager` | `HasPurchased` 단일 구매 규칙 삭제 → `PurchaseCount`(이번 단계 구매 수) · `canBuy = IsOpen && next != null && Balance >= next.Cost` · `CanFinish = IsOpen` (언제든 종료) |
| `UpgradeScreen` | 카드 잠금(locked) 삭제, 카드 상태 = 구매 가능 / 재화 부족 / MAX 셋만. 구매 직후 레벨·별·효과만 갱신. 하단 버튼은 항상 "다음 라운드 ▶" (구매 0회면 "건너뛰기 ▶"). 부제 "재화가 남아 있으면 계속 강화할 수 있습니다" |
| 데이터 | 변경 없음 (`UpgradeTable` 비용 그대로). 다회 구매로 성장 속도가 빨라지므로 **비용 재조정은 플레이 후 판단** |

같은 강화를 한 번에 여러 레벨 올리는 것도 허용 (최대 레벨까지) — Q8.

---

## 4. 설계 후 보완점 (설계안 자체의 위험)

| # | 위험 | 대응 |
|---|---|---|
| R1 | Ground + 높이 5 + 20~60m 조건이 겹쳐 **투하 실패가 잦을 수 있음** (플레이어가 고지대에 있으면 주변 저지대가 부족) | 탐색 횟수 30, 실패 로그 유지. 구현 후 `SupplyTester` 로 맵 여러 지점에서 성공률 확인. 실패가 잦으면 최대 거리 확장 대신 "플레이어 기준 → 맵 저지대 후보" 보정 검토 |
| R2 | **보스 아레나 지형이 높이 5 초과**면 보스전 보급이 전혀 안 나옴 | 에디터에서 보스 위치 지형 높이 확인 필요 (YAML 로는 Terrain 높이 확인 불가) |
| R3 | 상자를 F 로 먹게 되면 **전투 중 획득 부담 증가** (기존은 지나가기만 하면 획득) | 상호작용 거리 3m 유지. 필요 시 상자 콜라이더 패딩(`pickupPadding`) 확대 |
| R4 | 3인칭(보스전)에서 조준선으로 상자를 맞히기 어려울 수 있음 | `GunSlot.AimRay` 가 3인칭에서 시작점을 플레이어 앞까지 당겨 주므로 동작은 함. 플레이 확인 항목으로 둠 |
| R5 | 보급 무기 상자가 섞이면 아이템 상자 빈도 감소 + 상한 5개 공유 | 가중치로 조정 (데이터) |
| R6 | 보급 무기 탄 소진 직후 즉시 제거하면 마지막 발 발사 연출·투사체가 끊길 수 있음 | 발사 동작 종료 후 제거 (투사체는 총과 독립) |
| R7 | 다회 구매 → 한 라운드에 재화 전부 소비, 성장 곡선 변화 | 비용 테이블 재조정은 플레이 후 |
| R8 | 탄약이 매 라운드 시작값으로 리셋되므로 "+2" 는 항상 시작값+2 → 사실상 시작 탄약 상향과 같음 | Q6 |
| R9 | `TesterPlayer`·`SupplyWeaponPickup`·`PlaytestAnalyzer`(획득 집계) 등 테스트·분석 도구 영향 | 함께 수정. 분석기는 Weapon 타입 보급 집계 추가 |
| R10 | 씬에 저장된 직렬화 값(`groundMask`, `maxPositionTries`)이 코드 기본값을 덮음 | 통합 씬 값 직접 갱신 + 문서에 명시 |

---

## 5. 확인 받을 항목

> 답을 받으면 해당 결정으로 구현. **(권장)** 은 별도 지시가 없을 때 적용할 안.

| Q | 질문 | 선택지 |
|---|---|---|
| **Q1** | "Height 5 이하"의 기준 | **(권장) Terrain 로컬 높이** (Inspector Terrain Height 와 같은 단위, 현재 방식) / 월드 Y 좌표 |
| **Q2** | Ground 판정 | **(권장) 첫 충돌이 Ground 여야 함** (지붕·바위·나무 위 금지) / Ground 만 마스크 (지붕 관통) |
| **Q3** | 보스전 보급 | **(권장) 인트로 중 정지 → 보스전 시작 후 한 간격 뒤부터, 같은 간격** / 보스전 별도 간격(RoundTable 열 추가) / 인트로 중에도 계속 |
| **Q4** | 가득 찬 상태에서 상자에 마우스를 대면 | **(권장) 정보 + 경고 표시, F 는 실패** / 프롬프트 숨김 |
| **Q5** | "깨진 UI" 가 강화 화면이 **단색 박스로 보이는 것**이 맞는지 (다른 증상이면 알려주세요) · 복구 방식 | **(권장) ② 스킨 에셋** / ① 씬마다 연결 / ③ 프리팹 |
| **Q6** | "탄약 2개" 의 의미 | **(권장) 1~3번 칸 모든 총의 예비 탄창 +2** / 지금 든 총만 +2 / 보급 상자 1회분 |
| **Q6-b** | 매 라운드 탄약 시작값 리셋 규칙 유지? | **(권장) 유지** (리셋 후 +2) / 리셋 폐지 |
| **Q7** | 다음 라운드 보급 지급 시 알림(HUD 토스트) 필요? | **(권장) 이번엔 없음** / 필요 (이벤트 1개 추가) |
| **Q8** | 같은 강화를 한 화면에서 여러 레벨 연속 구매 허용? | **(권장) 허용** / 종류당 1회 |
| **Q9** | 보급 무기(B)는 이번엔 설계만? | **(권장) 이번 구현 범위 제외**, 확정 후 별도 작업 (플레이어 프리팹 총 3개 추가 필요) |

---

## 6. 변경 예정 파일 (A·C 기준, B 제외)

- 데이터: `ConfigTable`(거리 60, 높이 5, RoundStart* 3행), `SupplyTable`(+IconKey)
- Def/DTO: `SupplyDto/Def`(IconKey), `GameConfig`(RoundStart* 키)
- 보급: `SupplyCrate`(IInteractable, 자동 획득 제거), `SupplyDropper`(Ground 판정·높이·재개 옵션), `DefaultServices`(폴백값), 신규 `RoundStartSupply`
- 라운드: `RoundSequencer`(보스전 투하 재개)
- 강화: `UpgradeManager`, `UpgradeScreen`, (②안) 신규 `UpgradeUiSkin` + 에셋
- 테스트·도구: `TesterPlayer`, `SupplyTester`, `PlaytestAnalyzer`, `RunDirectorPlayModeTests`(강화 다회 구매)
- 씬: 통합 씬의 `SupplyDropper` 직렬화 값, `RoundStartSupply` 배치
- 문서: `Docs/Tables/README.md`, 관련 Guides
