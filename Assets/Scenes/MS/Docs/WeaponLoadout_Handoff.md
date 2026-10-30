# 무기 슬롯 · 출발 전 무기 선택 · 보급 무기 · 상호작용 HUD — 작업 인계 문서

> 작성: 2026-10-30 · 브랜치 `feat/MS` (HEAD `fb079369`, `chore/몬스터-2차-밸런스-패치` 머지 완료 = origin/develop `d78aa764` 와 내용 동일)
> 상태: **계획만 확정, 코드 변경 없음.** 이 문서만 읽고 바로 1단계부터 작업할 수 있게 쓴다.
> 담당: MinSeong (Weapon · Player · 플레이어 HUD 영역)

---

## 0. 요구사항 (확정)

| # | 요구사항 | 확정된 해석 |
|---|---|---|
| R1 | 게임 시작 전 슬롯을 골라, 해금됐고 아직 고르지 않은 총 중에서 장착·교체 | **게임 씬 안**의 Ready 단계에서 선택 패널. 선택 가능 칸 = 2·3번 키(인덱스 1, 2) |
| R2 | 1번 키(인덱스 0)는 권총 고정 | `WeaponTable.Category = Sidearm` 인 총이 항상 인덱스 0. 선택 화면에서 잠김 표시 |
| R3 | 슬롯 최대 4개 | `GunSlot.SlotCount = 4` 그대로 |
| R4 | 마지막 칸(4번 키, 인덱스 3) = 보급 무기 전용 | 시작 시 비어 있음. 보급 무기만 들어감 |
| R5 | 보급 무기는 일회용 | **보급 상자로 탄 충전 불가.** 탄이 떨어져도 칸에 남는다. **다른 보급 무기로 교체될 때만 버려진다** |
| R6 | 보급 칸만 교체 가능, 교체 시 이전 무기 자동 버림 | 게임 중 교체 API 는 보급 칸 전용 |
| R7 | HUD 에 현재 슬롯 요약 이미지 | 새 `WeaponSlotHud` (4칸: 키 번호·아이콘·이름, 현재 칸 강조, 빈 보급 칸 표시) |
| R8 | 상호작용 거리에서 보급 상자에 에임 → 키와 상자 정보 표시 | 새 `PlayerInteractor` + `InteractPromptHud`. **상호작용 키 = F** |
| — | 해금 시스템 | **사용자가 나중에 직접 만든다.** 지금은 "모두 해금" 기본값 + 끼워 넣을 자리(인터페이스)만 |
| — | 보급 시스템 | **남의 영역(pinkshield).** `Assets/Scripts/Supply/*`, `SupplyTable`, `SupplyDef/Dto`, `SupplyType`, `SupplyHud`, `SupplyCollectedEvent` 는 **수정 금지.** 우리 쪽은 계약(인터페이스)과 플레이어 측 구현만 |

---

## 1. 현재 코드 구조 (작업 전에 알아야 할 것)

### GunSlot — `Assets/Scripts/Weapon/GunSlot.cs` (343줄, 내 영역)
- 플레이어 루트에 붙음. `[DefaultExecutionOrder(20)]`, `IPlayerAmmo` 구현·`ServiceLocator` 등록.
- `Start → LoadLoadout()`: `weaponSocket` 아래 **모든 Gun(현재 8자루)** 을 `Initialize` 후 끄고, `startingGuns[0..3]` 을 슬롯에, 나머지는 `reserve`(예비) 리스트로.
- 현재 Player.prefab(`Assets/Scenes/MS/Prefabs/Player.prefab`) `startingGuns` = **[Rifle 2001, Pistol 2003, ShotGun 2005, RocketLauncher 2007]** → 요구사항(0번 권총)과 다름.
- 소켓 아래 총 weaponId: 2001 라이플, 2002 돌격소총, 2003 권총, 2004 기관단총, 2005 산탄총, 2006 저격소총, 2007 로켓, 2008 유탄.
- `PrimarySlot = 0` 만 교체 가능(`ReplacePrimary`) — **교체 시 이전 총을 `Destroy`** 한다. 총은 미리 만들어 둔 풀이므로, 이대로면 같은 무기를 두 번 못 줍는다.
- `ReplacePrimaryWithNextReserve()` ← 디버그 T 키(`PlayerController.Input.cs:92`).
- `AddSupplyMagazines()` / `AddMagazinesToAll()` 는 슬롯의 **모든** 총 충전 (보급 상자 `SupplyCrate.TryCollect` 가 `IPlayerAmmo` 로 호출).
- `event Action<int, Gun> GunChanged` — **바꿔 들 때만** 발생. 구독자:
  - `UI/PlayerHud.cs:144` (무기 이름·탄약 표시)
  - `Player/Animation/PlayerAnimationDriver.cs:136` (꺼내기 동작)
  - `Audio/PlayerSoundEmitter.cs:82` (교체음, 시작 직후는 무음)
  → 슬롯 내용만 바뀌는 경우(들지 않은 칸에 보급 무기 획득)에 `GunChanged` 를 쏘면 꺼내기 동작·교체음이 잘못 나간다.

### Gun — `Assets/Scripts/Weapon/Gun.cs` (849줄)
- `Initialize(GunSlot)` 에서 `UpgradePurchasedEvent`, `RoundCleanupEvent` 구독. `ResetAmmo()` 는 **private** (Ammo = 탄창 크기, Spare = StartMagazines).
- `OnRoundCleanup`: `FullReset` 이면 장전 취소 + `ResetAmmo` + `OnInitialized`.
- `AddSupplyMagazines()`: `MaxMagazines × WeaponDef.SupplyMagazineRatio` 충전.
- `IsOutOfAmmo`, `InfiniteMagazines`(권총), `DisplayName`(= `WeaponDef.Name`), `WeaponId`, `Def`.

### 입력 — `Player/Controller/PlayerController.Input.cs`, `.InputSettings.cs`
- 키: Space 점프, LShift 달리기, LCtrl 구르기, WASD, F1 시점, Esc 커서, R 장전, B 사격모드, G 수류탄, V 회복, 1~4 슬롯, T 디버그 교체. **F 는 비어 있음.**
  - 주의: `Reward/Tester/KillRewardTester.cs:80` 이 `keyboard.fKey` 사용(테스트 씬 전용). 같은 씬에 두지 말 것.
- `flowInputBlocked` 는 `GameStateChangedEvent` 로만 켜짐(`PlayerController.BossView.cs:57`). **게임 시작 직후에는 Ready 이벤트가 안 나와서 입력이 막히지 않는다** → 선택 패널이 직접 막아야 함.
- `UI/Options/KeyGuideList.cs:39~51` 키 안내 목록 — 상호작용 줄 추가 필요.

### 게임 흐름 — `Round/RunDirector.cs` (MountainHS 영역, 수정 금지)
- `startOnPlay = true` 면 `Start` 다음 프레임에 `StartRun()`. `StartRun()` 은 public, `CurrentState == Ready` 일 때만 동작.
- `RestartRun()`(게임오버 후 재시작)은 `RoundCleanupEvent(FullReset=true)` 후 바로 다음 라운드 → **재시작 때는 선택 화면 없이 기존 선택 유지**(현재 계획).
- 통합 씬: `Assets/Scenes/TestScenes/Monster Player Integration.unity` (`startOnPlay: 1`).

### 보급 (남의 영역, 읽기만)
- `Supply/SupplyCrate.cs`: 트리거 접촉 시 자동 획득(`IItemUser` + `IPlayerAmmo.AddSupplyMagazines`). 상자 콜라이더는 **Trigger**.
- `SupplyType { Grenade, Heal }` 뿐. 무기 보급 없음.
- 설계 기준: `Docs/Design/SupplySystemRework_Checklist.md` — 새 테이블 금지·기존 TSV 에 열/행 추가, 사실 통지는 EventBus, 결과에 따라 갈리는 지급은 직접 호출.

### 데이터
- TSV 는 CsvHelper 로 **헤더 이름 = DTO 필드 이름** 매칭 (열 순서 무관). 열을 추가하면 DTO 필드도 추가해야 함.
- `WeaponTable.tsv` 는 MinSeong·pinkshield 공동 수정 이력 → 열 추가는 끝에, 기존 값은 건드리지 않기.
- enum 값 추가는 **끝에** (`Core/Data/GameEnums.cs` 주석 규칙), `Docs/Tables/README.md` 도 갱신.
- `HybridShooter.Core` 는 별도 asmdef (참조 없음). Weapon/Player/UI/Supply 는 Assembly-CSharp → Core 의 계약을 참조.

---

## 2. 설계

### 2-1. 데이터 (`WeaponTable` 열 추가, 내 영역)
- `GameEnums.cs` 끝에 `public enum WeaponCategory { Sidearm, Loadout, Supply }`
- `WeaponTable.tsv` 끝 열 추가: `Category`, `IconKey`
  - 2003 권총 = `Sidearm` / 2007 로켓, 2008 유탄 = `Supply` (기본안, 사용자 확인 전이면 이대로) / 나머지 = `Loadout`
  - `IconKey` = `Resources/UI/WeaponIcons/{IconKey}` 스프라이트. 비면 HUD 는 이름 텍스트로 대체
- `WeaponDto` 필드 2개, `WeaponDef` 에 `Category`, `IconKey` + 주석 행 설명 갱신, `Docs/Tables/README.md` 갱신
- 검증: Sidearm 행이 정확히 1개인지 `DataLoadReport` 경고

### 2-2. 계약 (Core, 새 파일만 추가)
`Assets/Scripts/Core/Contracts/InteractionContracts.cs` (새 파일)
```csharp
/// 에임으로 고르고 F 로 쓰는 대상. 보급 상자 등 (보급 담당이 SupplyCrate 에 붙이면 된다)
public interface IInteractable
{
    bool CanInteract(GameObject user);          // 거리 외 조건 (이미 획득됨 등)
    InteractPrompt GetPrompt(GameObject user);  // HUD 표시 정보
    bool Interact(GameObject user);             // 성공하면 true
    Transform InteractTransform { get; }        // 거리 기준점
}
public readonly struct InteractPrompt { Title, Description, ActionLabel("획득"), IconKey, Warning }
```
`Assets/Scripts/Core/Contracts/PlayerContracts.cs` 에 추가 (또는 같은 새 파일):
```csharp
/// 보급 무기 받기 (GunSlot 이 구현, ServiceLocator 등록 + GetComponentInParent 로도 찾음)
public interface ISupplyWeaponUser
{
    int SupplyWeaponId { get; }                 // 0 = 비어 있음
    bool CanEquipSupplyWeapon(int weaponId);    // Category == Supply 이고 같은 무기를 이미 들고 있지 않음
    bool EquipSupplyWeapon(int weaponId);       // 이전 보급 무기는 버려짐
}
/// 무기 해금 여부. 사용자가 나중에 구현해 ServiceLocator 에 등록. 미등록이면 전부 해금으로 본다
public interface IWeaponUnlocks { bool IsUnlocked(int weaponId); }
```
`Core/Events/GameEvents.cs` 끝에 추가 (기존 이벤트 수정 없음):
- `InteractFocusChangedEvent(bool HasTarget, InteractPrompt Prompt)`
- `SupplyWeaponChangedEvent(int PreviousWeaponId, int NewWeaponId)` (분석·사운드·알림용, 선택)

### 2-3. GunSlot 변경 (내 영역)
- 상수: `SidearmSlot = 0`, `SupplySlot = SlotCount - 1`, `LoadoutSlots = {1, 2}`. `PrimarySlot` 제거(외부 사용처 없음 확인됨).
- `LoadLoadout()`:
  1. 소켓 아래 총 전부 Initialize·끄기 (지금과 동일)
  2. 인덱스 0 = `Category == Sidearm` 인 총 (없으면 경고 후 `startingGuns[0]`)
  3. 인덱스 1·2 = `startingGuns` 중 `Loadout` 분류 순서대로 (선택 화면이 덮어씀) → **프리팹 수정 없이도 요구사항대로 배치됨**
  4. 인덱스 3 = 비움
- `ApplyLoadout(int[] weaponIds)` (선택 화면 확정 시 호출): 인덱스 1·2 를 예비 풀에서 꺼내 배치, 빠진 총은 예비로 되돌림(파괴 X). 검증: Loadout 분류·해금·중복 없음. 끝에 `LoadoutChanged`.
- `EquipSupplyWeapon(int weaponId)` (`ISupplyWeaponUser`):
  - 예비 풀에서 해당 총을 꺼냄 → 기존 보급 무기는 `Unequip` 후 **예비로 반납 + 탄 초기화**(= "버림". 파괴하지 않아야 다시 주울 수 있음)
  - 새 총은 `ResetForPickup()` 으로 시작 탄약. **기본안: 줍자마자 보급 칸으로 바꿔 듦**
  - `LoadoutChanged` 발생, 보급 칸을 들고 있었거나 바로 바꿔 들면 `GunChanged` 도 (꺼내기 동작·교체음 정상)
- 일회용 규칙: `AddSupplyMagazines()` / `AddMagazinesToAll()` 에서 **보급 칸 제외**. 탄이 다 떨어져도 자동으로 버리지 않음(칸에 남음, 빈 탄창 사격음).
- `RoundCleanupEvent(FullReset)` → 보급 칸 비움(예비 반납). 라운드 전환(FullReset=false)은 유지.
- 새 이벤트 `public event Action LoadoutChanged;` — `GunChanged` 의미는 그대로 둔다.
- 디버그 T 키: `ReplacePrimaryWithNextReserve` → `DebugCycleSupplyWeapon()` (Supply 분류 총을 돌아가며 보급 칸에 장착).
- `ReplacePrimary(...)` 2개 오버로드 제거(외부 호출 없음) 또는 보급 칸용으로 이름 변경.

### 2-4. Gun 변경 (최소)
- `public void ResetForPickup()` 추가: 장전 취소 + `ResetAmmo()` + `OnInitialized()` (FullReset 처리와 같은 내용, private 메서드 재사용).

### 2-5. 상호작용 (내 영역, 새 파일)
`Assets/Scripts/Player/Interaction/PlayerInteractor.cs`
- 매 프레임 `GunSlot.AimRay` 로 `Physics.RaycastNonAlloc(..., QueryTriggerInteraction.Collide)`:
  - 자기 콜라이더 제외, 거리순 정렬
  - **트리거가 아닌 콜라이더(벽 등)가 먼저 맞으면 중단**
  - `GetComponentInParent<IInteractable>()` 찾으면 후보
  - 거리 판정은 **플레이어 몸 ↔ `InteractTransform`** (3인칭 카메라 거리와 무관). 사거리 = 인스펙터 `interactRange`(기본 3m). ConfigTable 은 공용이므로 넣지 않음
- 대상이 바뀌거나 프롬프트가 바뀌면 `InteractFocusChangedEvent` 발행
- `TryInteract()` 공개 → PlayerController 가 F 입력 시 호출
- 막는 조건: 사망·부활 중·구르기·투척·외부 제어·`IsGameplayInputBlocked`·커서 풀림 (`GunSlot.CanUseWeapon` 과 동일 + 커서)

입력: `InputPlayerSettings` 에 `public Key interactKey = Key.F;`, `ItemInputHandle()` 옆에 `InteractInputHandle()`. `KeyGuideList` 에 `("상호작용", Name(k.interactKey))`.

테스트용 대상 (보급 코드를 건드리지 않기 위함):
`Assets/Scripts/Weapon/Pickup/SupplyWeaponPickup.cs` — `IInteractable` 구현, `[SerializeField] int weaponId`, 상호작용 시 `ISupplyWeaponUser.EquipSupplyWeapon` 후 자기 제거. 내 테스트 씬(`Assets/Scenes/MS/Scenes/PlayerTest.unity`)에 배치해서 확인.
→ 실제 보급 상자 연동은 **보급 담당에게 요청** (3장 참고).

### 2-6. HUD (내 영역, 새 파일. PlayerHud·SupplyHud 수정 안 함)
- `Assets/Scripts/UI/WeaponSlotHud.cs`
  - `GunSlot.GunChanged` + `GunSlot.LoadoutChanged` 구독
  - 4칸: 키 번호 · 아이콘(`IconKey`, 없으면 이름) · 현재 칸 강조 · 빈 보급 칸 "보급" 회색 · 탄 소진된 보급 무기 흐리게
  - 위치: 오른쪽 아래 PlayerHud 무기 패널(anchor (1,0), offset (-416,40), 360×188) **위쪽**
  - `GameState.Reward/GameOver/Paused` 에서 숨김 (PlayerHud 와 같은 규칙)
  - 코드로 레이아웃 생성 (PlayerHud 방식, TMP)
- `Assets/Scripts/UI/InteractPromptHud.cs`
  - `InteractFocusChangedEvent` 구독, 조준점 아래 표시: `[F] 획득` / 상자 이름 / 설명(무기명·1회용·탄 수) / 경고("현재 ○○ 은(는) 버려집니다")
  - 프롬프트 키 이름은 `keySettings.interactKey` 에서 (하드코딩 금지)

### 2-7. 출발 전 무기 선택 (게임 씬 안, 내 영역)
- `Core` 에 상태를 둘 필요 없음 (씬 이동 없음). 마지막 선택은 `PlayerPrefs` `"Loadout.Slot1"`, `"Loadout.Slot2"` 에 저장(편의용).
- `Assets/Scripts/UI/Loadout/LoadoutPanel.cs` (새 파일)
  - 씬 시작 시 열림 → 플레이어 입력 차단 + 커서 표시
  - 4칸 표시: 1번 권총(잠금), 2·3번 선택 가능, 4번 "보급 전용"(선택 불가)
  - 칸 클릭 → 후보 목록: `Category == Loadout` && 해금(`IWeaponUnlocks` 없으면 true) && **다른 칸에 없는** 총. 고르면 그 칸 교체
  - "출발" → `GunSlot.ApplyLoadout(...)` → 입력 해제 → `RunDirector.StartRun()` (public API 호출만)
- 필요한 플레이어 API (PlayerController, 내 영역): `public void SetMenuInputBlocked(bool)` — `flowInputBlocked` 와 OR 로 합쳐 판단(흐름 차단과 서로 덮어쓰지 않게 별도 플래그).
- **씬 설정 변경 1건 필요**: 통합 씬의 RunDirector `startOnPlay` 를 **false** 로 (RunDirector 코드는 수정 안 함). 통합 씬은 공용이므로 MountainHS 에게 공유 후 변경. LoadoutPanel 이 없는 씬은 지금처럼 `startOnPlay=true` 로 바로 시작.
- 재시작(`RestartRun`)에서는 다시 열지 않고 기존 선택 유지 (재선택이 필요해지면 RunDirector 담당과 협의).

---

## 3. 다른 사람 영역에 요청할 것 (직접 수정 금지)

| 대상 | 담당 | 요청 내용 |
|---|---|---|
| 보급 상자 무기 연동 | pinkshield (Supply) | `SupplyType` 끝에 `Weapon` 추가, `SupplyTable` 에 `WeaponId` 열 + 무기 상자 행, `Resources/Supply/WeaponCrate` 프리팹. 무기 상자는 자동 획득(`TryCollect`) 대신 `IInteractable` 구현 → `ISupplyWeaponUser.EquipSupplyWeapon(Def.WeaponId)` 호출, 성공 시 `SupplyCollectedEvent` 발행 후 `Remove()`. 프롬프트 정보는 `SupplyDef.DisplayName` + `WeaponDef` 로 구성 |
| 보급 탄약 규칙 변경 공지 | pinkshield | `GunSlot.AddSupplyMagazines` 가 보급 칸(인덱스 3)을 제외하게 됨 → `SupplySystemRework_Checklist.md` "슬롯의 모든 총" 문구 갱신 요청 |
| 통합 씬 `startOnPlay` | MountainHS (Round) | 통합 씬 RunDirector `startOnPlay=false` + LoadoutPanel 배치 동의 |
| 해금 | 사용자 본인 (추후) | `IWeaponUnlocks` 구현 후 `ServiceLocator.Register<IWeaponUnlocks>(...)` 만 하면 선택 화면에 반영 |

---

## 4. 작업 순서 (단계마다 커밋, 커밋 규칙 `<Feat|Fix|...>: 영문 요약` + 한국어 설명)

1. **데이터**: `WeaponCategory` enum, WeaponTable `Category`/`IconKey` 열, `WeaponDto`/`WeaponDef`, `Docs/Tables/README.md`
2. **계약·이벤트**: `InteractionContracts.cs`, `ISupplyWeaponUser`, `IWeaponUnlocks`, 새 이벤트 2개
3. **GunSlot/Gun**: 슬롯 규칙(0 권총, 3 보급), `ApplyLoadout`, `EquipSupplyWeapon`(예비 반납), 보급 칸 충전 제외, FullReset 시 보급 칸 비움, `LoadoutChanged`, 디버그 T 키, `Gun.ResetForPickup`
4. **상호작용**: F 키, `PlayerInteractor`, `SupplyWeaponPickup`(테스트용), `KeyGuideList`
5. **HUD**: `WeaponSlotHud`, `InteractPromptHud`, 무기 아이콘 스프라이트(`Resources/UI/WeaponIcons/`)
6. **선택 화면**: `PlayerController.SetMenuInputBlocked`, `LoadoutPanel`, 테스트 씬 배치 → (협의 후) 통합 씬 반영
7. **확인** (PlayerTest 씬 → 통합 씬)

## 5. 확인 체크리스트
- [ ] 시작 시 1번 = 권총, 4번 비어 있음, 1~4 / 휠 교체가 빈 칸을 건너뜀
- [ ] 선택 화면: 이미 고른 총은 다른 칸 후보에 안 나옴, 권총·보급 전용 무기 선택 불가, 확정 후 라운드 시작
- [ ] 선택 화면 동안 이동·사격·시선 회전 안 됨, 커서 보임
- [ ] 보급 무기 획득 → 4번 칸 장착, HUD 갱신, (기본안) 바로 들기
- [ ] 다른 보급 무기 획득 → 이전 것 사라지고 새 것 장착, 같은 무기를 다시 주워도 정상(풀 반납 확인)
- [ ] 보급 무기 탄 소진 → 칸에 남음, 보급 상자 먹어도 탄 안 늘어남, 다른 칸 총은 정상 충전
- [ ] 게임오버 → 재시작: 보급 칸 비워짐, 2·3번 선택은 유지
- [ ] 에임을 상자에 대면(3m 이내) `[F] 획득` + 상자 정보, 벽 너머·사거리 밖·뒤돌면 사라짐, 1인칭·3인칭 모두
- [ ] 구르기·투척·사망·커서 풀림 중 F 무시
- [ ] Console 에 error/경고 없음 (Sidearm 1개 검증 포함)

## 6. 주의 (충돌 방지)
- `Supply/*`, `SupplyTable`, `SupplyDef/Dto`, `SupplyType`, `SupplyHud`, `SupplyCollectedEvent`, `RunDirector.cs`, `RoundSequencer.cs`, `MainMenuController.cs` **수정 금지**
- `GunChanged` 의미·시그니처 유지 (PlayerHud·애니메이션·사운드 구독 중)
- `IPlayerAmmo` 시그니처 유지 (SupplyCrate 가 사용)
- Player.prefab 은 컴포넌트 추가(`PlayerInteractor`)만. `startingGuns` 순서는 코드가 분류로 재배치하므로 건드리지 않아도 됨
- 공용 씬 수정은 담당자 공유 후 (TeamRules §4: 개인 작업은 개인 씬에서)
- enum 은 끝에 추가, TSV 열은 끝에 추가
