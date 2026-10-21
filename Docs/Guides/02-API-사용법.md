# API 사용법 — 복붙해서 쓰는 가이드

> 먼저 [01-코드구조-이해하기](01-코드구조-이해하기.md) 를 읽으면 훨씬 쉽습니다. (비유: 계약서 / 114 안내 / 방송실 / 도서관)
> 이 문서는 **"무엇을 호출할 수 있고, 어떻게 쓰는가"** 를 레시피 형태로 정리했습니다.
> 예제 코드는 그대로 복사해서 이름만 바꿔 쓰면 됩니다.
>
> ⚠ 임시 구현입니다. 이벤트·계약 이름과 필드는 팀 리뷰 후 바뀔 수 있습니다. 정확한 최신 정의는 항상 `Assets/Scripts/Core/` 의 코드가 정답입니다.

---

## 0. 시작 전 준비 (한 번만)

### 0-1. 맨 위에 붙이는 `using` 줄 (필요한 것만)

```csharp
using HybridShooter.Core.Contracts;   // 🧾 계약 (IDamageable, IPlayerTarget …)
using HybridShooter.Core.Events;      // 📢 EventBus, 이벤트들
using HybridShooter.Core.Services;    // ☎ ServiceLocator
using HybridShooter.Core.Data;        // 📚 DataManager, enum들
using HybridShooter.Core.Data.Defs;   // 🪪 MonsterDef, WeaponDef …
using HybridShooter.Core.GameFlow;    // 🚦 GameState, CameraMode, SkillPhase
```

### 0-2. 내 코드의 네임스페이스

`프로젝트명.폴더경로` 규칙입니다. 예: `Assets/Scripts/Monster/` → `namespace HybridShooter.Monster`

### 0-3. 내가 쓸 수 있는 것 / 없는 것

| ✅ 가능 | ❌ 금지 |
|---|---|
| `HybridShooter.Core.*` 사용 | 다른 파트 폴더(`Player`, `Monster` …)의 클래스 직접 사용 |
| 계약 구현, 서비스 등록, 이벤트 발행/구독 | `FindObjectOfType<다른파트클래스>()` |

### 0-4. 🧪 혼자 테스트하기 (`Test_` 씬)

1. 새 씬 `Test_내기능` 을 만든다 (메인 씬 수정 금지).
2. 내 컴포넌트만 올리고 Play.
3. 남의 파트가 없어도 **임시 직원(기본 구현)** 이 대신 일합니다. 데이터는 **첫 접근 때 자동 로드**됩니다.

---

## 1. 📚 데이터 꺼내 쓰기 (`DataManager`)

### 1-1. 한눈에 보는 API

| 이렇게 쓰면 | 돌려주는 것 |
|---|---|
| `DataManager.Player` | `PlayerDef` (플레이어 정의, 첫 행) |
| `DataManager.Weapons.Get(2001)` | `WeaponDef` (없으면 에러 로그 + `null`) |
| `DataManager.Grenades.Get(id)` | `GrenadeDef` |
| `DataManager.Monsters.Get(3001)` | `MonsterDef` |
| `DataManager.MonsterSkills.Get(id)` | `MonsterSkillDef` |
| `DataManager.GetRound(n)` | `RoundDef` (표보다 큰 n은 마지막 행 재사용) |
| `DataManager.CrowdSpawnRules` | 생성 규칙 목록 (보통은 `RoundDef.SpawnRules` 사용) |
| `DataManager.Supplies.All` | `SupplyDef` 목록 |
| `DataManager.UpgradeTracks.Get(UpgradeType.ReloadSpeed)` | 강화 종류별 단계 묶음 |
| `DataManager.Sounds.Get("sfx_rifle_fire")` | `SoundDef` |
| `DataManager.Config.GetFloat(GameConfig.SpawnRingRadius, 35f)` | 전역 상수 값 |

**표(Table)에서 쓰는 공통 도구**

| 도구 | 설명 |
|---|---|
| `.Get(key)` | "반드시 있어야 하는" 데이터. 없으면 에러 로그 후 `null` |
| `.TryGet(key, out def)` | "없을 수도 있는" 데이터. 로그 없음 → `true/false` |
| `.Contains(key)` | 있는지만 확인 |
| `.All` | 표에 적힌 **순서 그대로** 목록 |
| `foreach (var m in DataManager.Monsters)` | 전체 순회 |

### 1-2. 레시피: 몬스터 능력치 읽기

```csharp
MonsterDef def = DataManager.Monsters.Get(3001);   // 근접형
float speed = def.MoveSpeed;
int   maxHp = def.MaxHp;
int   reward = def.Reward;
```

### 1-3. 레시피: ⭐ "설계도"와 "현재 상태" 분리하기

```csharp
public class MeleeMonster : MonoBehaviour
{
    MonsterDef def;      // 🧁 쿠키 틀 (읽기 전용, 공유)
    int currentHp;       // 🍪 이 몬스터만의 현재 상태 (내가 직접 관리)

    public void Init(MonsterDef d)
    {
        def = d;
        currentHp = def.MaxHp;   // 틀에서 시작값만 가져오고
    }                            // 이후 currentHp 만 바꾼다. def.MaxHp 는 절대 안 바꿈
}
```

### 1-4. 레시피: 전역 상수(Config) 읽기

```csharp
// 키는 문자열 직접 타이핑 금지! GameConfig 의 상수를 쓰면 오타가 안 납니다.
float radius = DataManager.Config.GetFloat(GameConfig.SpawnSafeRadius, 10f); // 두 번째 값 = 못 찾을 때 기본값
int   maxNear = DataManager.Config.GetInt(GameConfig.CrowdMaxNear, 8);
float[] stages = DataManager.Config.GetFloatList(GameConfig.BossDamageStageRatios); // "0.7;0.4;0.15" → [0.7, 0.4, 0.15]
```

### 1-5. 레시피: 몬스터 스킬 / 보스 패턴

```csharp
MonsterDef boss = DataManager.Monsters.Get(bossId);

MonsterSkillDef entrance = boss.EntranceSkill;              // 등장 공격 (없으면 null)
IReadOnlyList<MonsterSkillDef> patterns = boss.PatternSkills; // 반복 패턴들
MonsterSkillDef next = boss.PickPattern(Random.value, lastSkill); // 가중치 랜덤. lastSkill 을 넘기면 같은 패턴 연속 방지
MonsterSkillDef byKey = boss.FindSkill("SkillKey문자열");

float windup = next.WindupTime;   // 준비 시간
float recover = next.RecoverTime; // 회복(빈틈) 시간
```

### 1-6. 레시피: 라운드 정보 / 군중 생성 규칙

```csharp
RoundDef round = DataManager.GetRound(currentRound);
float bossTime = round.RollBossSpawnTime(Random.value); // Min~Max 사이 랜덤 시각
MonsterDef boss = round.Boss;

foreach (CrowdSpawnRuleDef rule in round.SpawnRules)    // 시작 시간 순으로 정렬되어 있음
{
    if (!rule.IsActiveAt(elapsedTime)) continue;         // 지금 시각에 유효한 규칙만
    // rule.Monster, rule.Interval, rule.CountPerSpawn, rule.MaxAlive, rule.SpawnPattern
}
```

### 1-7. 레시피: 보급 랜덤 뽑기

```csharp
SupplyDef pick = SupplyDef.Pick(DataManager.Supplies.All, Random.value); // Weight(가중치) 기반
// pick.SupplyType (Grenade / Heal), pick.Amount, pick.PrefabKey
```

### 1-8. 레시피: 테이블을 고친 뒤 확인하기

- 메뉴 **`Tools > Data > Validate Tables`** → 오류 0건이면 OK. (커밋 전에 한 번!)
- 오류가 있어도 게임은 켜지지만, 콘솔에 빨간 보고서가 뜹니다.
- 테이블 컬럼 설명은 [Tables/README](../Tables/README.md), 표 파일 위치는 `Assets/StreamingAssets/Tables/*.tsv`.
- 표를 읽은 뒤 알림이 필요하면: `DataManager.Loaded += 내함수;`

### 1-9. 🚫 흔한 실수

| ❌ | ✅ |
|---|---|
| `def.MaxHp = 50;` (Def는 읽기 전용이라 컴파일 에러) | 내 변수 `currentHp` 를 바꾸기 |
| `if (hp > 20)` 처럼 코드에 숫자 박기 | `def.MaxHp` / `Config` 로 읽기 |
| `Get()` 결과를 `null` 확인 없이 계속 사용 | 필수 데이터는 `Get`, 선택적이면 `TryGet` |

---

## 2. 🧾 계약 사용하기 (`Contracts`)

### 2-1. 💥 피해 주기 — `IDamageable` + `DamageInfo` (공격하는 쪽)

```csharp
// 예: 레이캐스트로 맞춘 대상에게 피해
if (Physics.Raycast(origin, dir, out RaycastHit hit, weaponDef.Range))
{
    // 콜라이더가 자식(히트박스)에 있어도 부모에서 찾아줌
    if (hit.collider.TryGetDamageable(out IDamageable target)
        && target.CanBeHitBy(Faction.Player))          // 같은 편/죽은 대상 걸러내기
    {
        var info = new DamageInfo(
            amount: weaponDef.Damage,
            kind: DamageKind.Bullet,                   // Bullet / Explosion / MonsterAttack / MonsterSkill
            attackerFaction: Faction.Player,
            attacker: gameObject,
            hitPoint: hit.point,
            direction: dir,
            sourceDefId: weaponDef.Id);                // 원인 데이터 Id (모르면 생략 가능)

        target.TakeDamage(info);
    }
}
```

### 2-2. 💥 피해 받기 — `IDamageable` 구현하기 (맞는 쪽: 플레이어·몬스터·보스)

```csharp
public class MeleeMonster : MonoBehaviour, IDamageable
{
    public Faction Faction => Faction.Monster;   // 내 진영
    public bool IsAlive => currentHp > 0;

    public void TakeDamage(in DamageInfo info)   // in 은 "복사 없이 읽기 전용으로 받기"
    {
        if (!IsAlive) return;
        currentHp -= info.Amount;
        // info.HitPoint / info.Direction 으로 피격 연출, info.Kind 로 반응 구분 가능
        if (currentHp <= 0) Die();
    }
}
```

> 콜라이더가 자식 오브젝트에 있어도, **부모 오브젝트에 이 컴포넌트가 하나만 있으면** `TryGetDamageable` 이 찾아냅니다.

### 2-3. 🎯 플레이어 위치 알기 — `IPlayerTarget`

**알려주는 쪽 (플레이어 담당)**: 등록 ([3장 참고](#3--servicelocator-사용하기))

**쓰는 쪽 (몬스터·스폰 등)**

```csharp
if (ServiceLocator.TryGet<IPlayerTarget>(out var player) && player.IsAlive)
{
    Vector3 aim = player.AimPoint.position;   // 조준 지점(가슴 높이)
    Vector3 pos = player.Root.position;       // 플레이어 위치
    Vector3 look = player.ViewPoint.forward;  // 바라보는 방향 (시야 밖 판정용)
}
```

> ⚠ `IPlayerTarget` 은 **기본 구현이 없습니다.** `Get<>()` 은 없으면 빨간 에러 로그 + `null` 이므로,
> "없을 수도 있다"면 위처럼 **`TryGet`** 을 쓰세요. (예: 플레이어 없이 몬스터만 테스트할 때)

### 2-4. 💊 회복 / 💣 수류탄 보급 — `IHealable`, `IGrenadeReceiver` (플레이어 루트의 컴포넌트)

```csharp
// 보급품 획득 시 (플레이어와 부딪혔을 때)
var healable = other.GetComponentInParent<IHealable>();
if (healable != null && healable.Heal(30))
    Destroy(gameObject);                  // Heal 이 false 면 "가득 차서 못 먹음" → 보급품 유지

var gr = other.GetComponentInParent<IGrenadeReceiver>();
if (gr != null && gr.AddGrenades(1)) { /* 획득 성공 */ }
// gr.Grenades / gr.MaxGrenades 로 현재/최대 확인
```

(플레이어 담당은 플레이어 루트에 `IHealable`, `IGrenadeReceiver` 를 구현한 컴포넌트를 붙입니다.)

---

## 3. ☎ ServiceLocator 사용하기

### 3-1. 한눈에 보는 API

| 함수 | 언제 쓰나 |
|---|---|
| `ServiceLocator.Get<T>()` | 담당자를 가져온다. 없으면 **에러 로그 + null** (기본 구현이 있는 계약이면 그게 옴) |
| `ServiceLocator.TryGet<T>(out var s)` | 없을 수도 있을 때. 에러 로그 없이 `true/false` |
| `ServiceLocator.Register<T>(this)` | "제가 T 담당입니다" 등록 (구현하는 쪽) |
| `ServiceLocator.Unregister<T>(this)` | 등록 해제 (내가 등록한 것만 지워짐) |
| `SetFallback<T>(x)` | 기본 구현 등록 (Core 전용. 보통 건드리지 않음) |

### 3-2. 🛠 레시피: 서비스를 "제공"하기 (내가 담당자일 때)

```csharp
public class SoundService : MonoBehaviour, ISoundService
{
    void OnEnable()  => ServiceLocator.Register<ISoundService>(this);    // 켜질 때 등록
    void OnDisable() => ServiceLocator.Unregister<ISoundService>(this);  // 꺼질 때 해제 (짝!)

    public void PlaySfx(string key, Vector3? position = null) { /* 재생 */ }
    public void PlayBgm(string key) { /* … */ }
    public void StopBgm() { /* … */ }
}
```

- 이 컴포넌트가 **씬에 있으면 → 진짜 구현이 우선**, 없으면 → 기본 구현(임시 직원).
- 같은 계약에 둘이 등록하면 나중 것으로 교체되고 **경고**가 뜹니다.

### 3-3. 🛠 레시피: 서비스를 "사용"하기

```csharp
ServiceLocator.Get<ISoundService>().PlaySfx(weaponDef.FireSfxKey, muzzle.position);
```

> 💡 자주 부를 거라면 `Start()` 에서 한 번 받아 변수에 담아도 됩니다.
> 단, 등록 시점이 다를 수 있으니 **담당자가 바뀔 수 있는 환경**이라면 호출 직전에 `Get` 하는 쪽이 안전합니다.

### 3-4. 📋 서비스 계약 모음

#### 🔊 `ISoundService` — 소리 (기본: 아무 소리도 안 남)

```csharp
var sound = ServiceLocator.Get<ISoundService>();
sound.PlaySfx("sfx_rifle_fire");                     // 효과음
sound.PlaySfx("sfx_grenade_explode", hitPosition);   // 위치 지정(3D)
sound.PlayBgm("bgm_battle");                          // 배경음
sound.StopBgm();
```
키 이름은 `SoundTable.tsv` 에 있는 것만 유효 (`DataManager.Sounds`).

#### 🏭 `IMonsterFactory` — 몬스터 생성/회수 (기본: `Resources/{PrefabKey}` 를 Instantiate)

```csharp
var factory = ServiceLocator.Get<IMonsterFactory>();
MonsterDef def = DataManager.Monsters.Get(3001);
GameObject go = factory.Spawn(def, position, rotation);  // 생성 성공 시 MonsterSpawnedEvent 자동 방송
factory.Despawn(go);                                      // 회수 (풀링 구현이면 재활용)
```
- 몬스터 프리팹은 `Assets/Resources/<PrefabKey>` 에 있어야 기본 구현이 찾습니다. (예: `Monsters/Melee` → `Resources/Monsters/Melee.prefab`)
- 프리팹이 없으면 에러 로그 + `null` 반환 → **null 체크** 필요.
- **구현하는 쪽(몬스터 담당)의 의무:** `Spawn` 시 `MonsterSpawnedEvent` 를 방송할 것.

#### 📍 `ISpawnZoneProvider` — 어디에 놓을까 (기본: 플레이어 주변 도넛 모양)

```csharp
var zones = ServiceLocator.Get<ISpawnZoneProvider>();

// 몬스터 위치
if (zones.TryGetSpawnPose(SpawnPattern.Flank, groupSeed: 0, out Vector3 pos, out Quaternion rot))
    factory.Spawn(def, pos, rot);

// 보급 낙하 위치
if (zones.TryGetSupplyDropPosition(out Vector3 dropPos)) { /* 보급 투하 */ }
```

| `SpawnPattern` | 의미 |
|---|---|
| `Surround` | 여러 방향에서 둘러싸기 |
| `Flank` | 측면~후방 (시야 밖) |
| `Air` | 공중에서 |
| `Herd` | 같은 `groupSeed` 끼리 한 방향에 뭉침 |

`TryGet…` 이 `false` 면 위치를 못 구한 것 → 그 회는 생성을 건너뜁니다.

#### 👛 `ICurrencyWallet` — 재화

```csharp
var wallet = ServiceLocator.Get<ICurrencyWallet>();
wallet.Add(3);                   // +3  (0 이하는 무시)
int now = wallet.Balance;        // 현재 잔액
if (wallet.TrySpend(10)) { /* 구매 성공 */ } else { /* 돈 부족 (차감 안 됨) */ }
wallet.ResetTo(0);               // 처음부터 다시 시작할 때
```
값이 바뀔 때마다 `CurrencyChangedEvent` 가 **자동 방송**됩니다. (UI 는 이걸 듣기만 하면 됨)

#### 📈 `IUpgradeState` — 강화

```csharp
var up = ServiceLocator.Get<IUpgradeState>();
int lv        = up.GetLevel(UpgradeType.ReloadSpeed);        // 현재 레벨 (0 = 미구매)
float total   = up.GetTotalValue(UpgradeType.MagazineSize);  // 지금까지 누적된 효과 값
UpgradeDef next = up.GetNext(UpgradeType.GrenadeCapacity);   // 다음 단계 정보 (최대 레벨이면 null)
if (next != null) { /* next.Cost, next.Value, next.Name, next.Description 로 화면 표시 */ }

bool ok = up.TryPurchase(UpgradeType.ReloadSpeed, ServiceLocator.Get<ICurrencyWallet>());
// 성공: 재화 차감 + 레벨 상승 + UpgradePurchasedEvent 방송

up.ResetAll();                   // 처음부터 다시
```

**무기 담당이 강화를 적용하는 방법 (예시)**
```csharp
int magazine = weaponDef.MagazineSize + (int)up.GetTotalValue(UpgradeType.MagazineSize);
float reload = weaponDef.ReloadTime * (1f - up.GetTotalValue(UpgradeType.ReloadSpeed)); // ReloadSpeed 는 "감소율"
```
(구체적으로 값이 "더하기"인지 "비율"인지는 `Docs/Tables/README.md` 와 `UpgradeTable.tsv` 주석 확인)

---

## 4. 📢 EventBus 사용하기

### 4-1. 기본 3줄

```csharp
// ① 방송하기 (Publish)
EventBus<MonsterDiedEvent>.Publish(new MonsterDiedEvent(def, transform.position, gameObject));

// ② 듣기 시작 (Subscribe)
EventBus<MonsterDiedEvent>.Subscribe(OnMonsterDied);

// ③ 듣기 해제 (Unsubscribe)
EventBus<MonsterDiedEvent>.Unsubscribe(OnMonsterDied);
```

### 4-2. ⭐ 반드시 이 틀로 쓰기 (복사해서 사용)

```csharp
public class KillCounter : MonoBehaviour
{
    int kills;

    void OnEnable()  => EventBus<MonsterDiedEvent>.Subscribe(OnMonsterDied);    // 켜질 때 듣기 시작
    void OnDisable() => EventBus<MonsterDiedEvent>.Unsubscribe(OnMonsterDied);  // 꺼질 때 반드시 해제

    void OnMonsterDied(MonsterDiedEvent e)    // 방송 내용은 e 에 담겨 옴
    {
        kills++;
        Debug.Log($"{e.Def.Name} 처치! 위치 {e.Position}, 보상 지급? {e.GrantReward}");
    }
}
```

**규칙 요약**
- `Subscribe` 와 `Unsubscribe` 는 **`OnEnable` / `OnDisable` 짝**.
- 구독 함수는 이벤트 타입 하나를 인자로 받는 `void` 함수.
- 같은 함수를 두 번 구독해도 **한 번만** 등록됩니다.
- 방송 중간에 구독/해제해도 안전합니다.
- 구독 함수가 에러를 내도 **다른 구독자에게는 영향 없음** (콘솔에 에러만 뜸).
- 방송하면 **그 자리에서 즉시** 모든 구독자 함수가 순서대로 실행됩니다. (다음 프레임이 아님)
- 디버깅: `EventBus<T>.SubscriberCount` 로 "지금 듣는 사람 수" 확인.

### 4-3. 📖 이벤트 사전 — 누가 방송하고, 누가 듣고, 무엇이 들어있나

> **표 읽는 법:** `발행` = 이 방송을 내보내는 쪽(책임자). `구독` = 주로 듣는 쪽.
> 내가 "발행자"인 이벤트는 **반드시 방송할 책임**이 있고, "구독자"는 마음대로 듣기만 하면 됩니다.

#### 🚦 게임 흐름 (발행: 코어)

| 이벤트 | 담긴 정보 | 언제 | 주 구독자 |
|---|---|---|---|
| `GameStateChangedEvent` | `Previous`, `Current` (`GameState`) | 게임 단계가 바뀔 때 | 전원 (입력 잠금, BGM, 화면 전환) |
| `RoundStartedEvent` | `Round`, `Def` | 라운드 시작 | 전원 |
| **`RoundCleanupEvent`** | `FullReset` (bool) | 라운드 정리 요청 | **전원 (필수 처리)** |
| `BossWarningEvent` | `Boss` (MonsterDef) | 보스 등장 직전 신호 | 보스, UI·사운드 |
| `GameOverEvent` | `ReachedRound` | 게임 오버 | UI |

> **`RoundCleanupEvent` 는 모두가 꼭 처리해야 합니다.**
> 받으면 내 몬스터·투사체·보급·코루틴/타이머를 전부 정리합니다.
> - `FullReset == true` → 처음부터 다시 (플레이어 위치·체력, 강화, 재화까지 초기화)
> - `FullReset == false` → 다음 라운드 (강화·재화·수류탄 수는 유지)

#### 🧍 플레이어 (발행: 플레이어)

| 이벤트 | 담긴 정보 | 주 구독자 |
|---|---|---|
| `PlayerHpChangedEvent` | `Current`, `Max`, `Ratio`(0~1) | UI (체력바) |
| `PlayerDamagedEvent` | `Info` (DamageInfo) | UI·사운드 (피격 연출) |
| `PlayerDiedEvent` | (없음) | 코어 (게임오버) |
| `CameraModeChangedEvent` | `Mode` (FirstPerson/ThirdPerson), `IsComplete` | 보스(전환 **완료 후** 등장 공격), UI |

#### 🔫 무기 (발행: 무기)

| 이벤트 | 담긴 정보 | 주 구독자 |
|---|---|---|
| `AmmoChangedEvent` | `Magazine`, `MagazineSize` | UI |
| `WeaponFiredEvent` | `Weapon`, `Muzzle` | 사운드, 이펙트 |
| `ReloadStartedEvent` | `Duration` (초) | UI (진행 표시) |
| `ReloadFinishedEvent` | (없음) | UI |
| `GrenadeCountChangedEvent` | `Current`, `Max` | UI |
| `GrenadeExplodedEvent` | `Grenade`, `Position` | 몬스터(유인), UI·사운드 |
| `DamageDealtEvent` | `Target`, `Info`, `IsKill` | UI (히트마커), 몬스터 피격 연출 |

#### 👾 몬스터 (발행: 몬스터)

| 이벤트 | 담긴 정보 | 주 구독자 |
|---|---|---|
| `MonsterSpawnedEvent` | `Def`, `Instance` | 군중 생성(카운트) |
| `MonsterDiedEvent` | `Def`, `Position`, `Instance`, `GrantReward` | 군중 생성(카운트), 보상(재화), UI·사운드 |
| `MonsterWipeRequestedEvent` | `Origin`, `GrantReward` | 몬스터 (보스 등장 공격 → 일반 몬스터 정리) |

`GrantReward == false` 는 "보스 등장 공격으로 정리돼서 돈을 안 주는 경우" 등. 지급 여부 판단은 보상 담당.

#### 🐉 보스 (발행: 보스)

| 이벤트 | 담긴 정보 | 주 구독자 |
|---|---|---|
| `BossAppearedEvent` | `Boss`, `Instance` | 코어 (BossBattle 로 전환), UI·사운드 |
| `BossSkillPhaseChangedEvent` | `Skill`, `Phase` (Windup/Active/Recover/Idle) | UI·사운드 (예고 표시·포효) |
| `BossDamageStageChangedEvent` | `Stage` (0 = 멀쩡) | 보스 외형 변화, UI |
| `BossDefeatedEvent` | `Boss`, `Position` | 코어, 보상 |

#### 🎁 보급 · 💰 보상 · 🖥 UI

| 이벤트 | 발행 | 담긴 정보 | 주 구독자 |
|---|---|---|---|
| `SupplyDroppedEvent` | 보급 | `Def`, `Position` | UI (낙하 위치 표시), 사운드 |
| `SupplyCollectedEvent` | 보급 | `Def` | UI, 사운드 |
| `CurrencyChangedEvent` | 지갑(자동) | `Balance`, `Delta` | UI |
| `UpgradePurchasedEvent` | 강화(자동) | `Upgrade` | UI, 무기(능력치 갱신) |
| `RewardSelectionFinishedEvent` | UI(강화 화면) | (없음) | 코어 (다음 라운드 시작) |

### 4-4. ✍ 레시피: 새 이벤트 추가하기

팀 합의가 필요한 공용 이벤트라면 `Assets/Scripts/Core/Events/GameEvents.cs` 에 추가합니다.
형식은 **`readonly struct`** (불변 데이터 한 덩어리).

```csharp
// 1) GameEvents.cs 에 추가
public readonly struct BossRoarEvent
{
    public readonly Vector3 Position;
    public BossRoarEvent(Vector3 position) { Position = position; }
}

// 2) 방송
EventBus<BossRoarEvent>.Publish(new BossRoarEvent(transform.position));

// 3) 듣기 (4-2 의 틀 그대로)
```

그리고 **`Docs/Architecture/02-SystemInterfaces.md` 의 이벤트 표도 같이 갱신**합니다. (파일 맨 위 주석의 규칙)
내 파트 안에서만 쓰는 이벤트라면 내 폴더에 같은 형식으로 만들어도 됩니다.

> ⚠ 이벤트 타입은 `struct` 만 가능합니다 (`class` 불가).

---

## 5. 🧩 실전 시나리오 — 처음부터 끝까지

### 시나리오 A: "라이플 사격" (무기 담당)

```csharp
public class RifleFire : MonoBehaviour
{
    WeaponDef weapon;
    int magazine;

    void Start()
    {
        weapon = DataManager.Player.Rifle;           // 📚 플레이어 정의에 연결된 무기
        magazine = weapon.MagazineSize;
    }

    void Fire(Vector3 origin, Vector3 dir)
    {
        magazine--;

        // ☎ 소리: 사운드 담당이 없어도 에러 없이 통과
        ServiceLocator.Get<ISoundService>().PlaySfx(weapon.FireSfxKey, origin);

        // 🧾 피해: 누가 맞았는지 몰라도 됨
        if (Physics.Raycast(origin, dir, out var hit, weapon.Range)
            && hit.collider.TryGetDamageable(out var target)
            && target.CanBeHitBy(Faction.Player))
        {
            target.TakeDamage(new DamageInfo(weapon.Damage, DamageKind.Bullet,
                Faction.Player, gameObject, hit.point, dir, weapon.Id));
        }

        // 📢 알림: UI·사운드 등이 알아서 반응
        EventBus<WeaponFiredEvent>.Publish(new WeaponFiredEvent(weapon, origin));
        EventBus<AmmoChangedEvent>.Publish(new AmmoChangedEvent(magazine, weapon.MagazineSize));
    }
}
```

### 시나리오 B: "몬스터가 죽는다" (몬스터 담당)

```csharp
void Die()
{
    // 반드시 방송! 재화·UI·군중 카운트가 이 방송에 의존함
    EventBus<MonsterDiedEvent>.Publish(
        new MonsterDiedEvent(def, transform.position, gameObject, grantReward: true));

    ServiceLocator.Get<IMonsterFactory>().Despawn(gameObject);  // 풀링/파괴는 팩토리에 맡김
}
```

### 시나리오 C: "죽을 때 돈 올리기" (보상 담당)

```csharp
public class RewardOnKill : MonoBehaviour
{
    void OnEnable()  => EventBus<MonsterDiedEvent>.Subscribe(OnDied);
    void OnDisable() => EventBus<MonsterDiedEvent>.Unsubscribe(OnDied);

    void OnDied(MonsterDiedEvent e)
    {
        if (!e.GrantReward) return;
        ServiceLocator.Get<ICurrencyWallet>().Add(e.Def.Reward);  // CurrencyChangedEvent 는 지갑이 자동 방송
    }
}
```

### 시나리오 D: "라운드 정리에 반응하기" (모든 파트, 필수)

```csharp
void OnEnable()  => EventBus<RoundCleanupEvent>.Subscribe(OnCleanup);
void OnDisable() => EventBus<RoundCleanupEvent>.Unsubscribe(OnCleanup);

void OnCleanup(RoundCleanupEvent e)
{
    StopAllCoroutines();               // 예약된 코루틴 정리
    // 내가 만든 몬스터/투사체/보급 모두 제거
    if (e.FullReset) { /* 처음 상태로: 체력·탄약·위치 초기화 */ }
}
```

---

## 6. ❓ 문제가 생겼을 때 (트러블슈팅)

| 증상 / 콘솔 메시지 | 원인 | 해결 |
|---|---|---|
| `[ServiceLocator] ○○ 구현이 등록되지 않았습니다` | 그 계약을 구현한 컴포넌트가 씬에 없고, 기본 구현도 없음 (예: `IPlayerTarget`) | 구현 컴포넌트를 씬에 올리기 / `TryGet` 으로 없음 처리 |
| `[ServiceLocator] ○○ 가 이미 등록되어 있어 교체합니다` | 같은 계약을 두 오브젝트가 등록 | 씬에 구현체가 2개 아닌지 확인 |
| `[DataManager] ○○Table 에 Key '…' 가 없습니다` | 없는 Id로 `Get` | TSV의 Id 확인, 선택적이면 `TryGet` |
| 콘솔에 `[DataManager] 테이블 로드 결과 - 오류 N건` | TSV 수정 중 실수 | 메시지가 `[테이블 #Id] 이유` 형식으로 정확히 알려줌 |
| `Resources/… 프리팹이 없습니다` | `PrefabKey` 경로와 `Resources` 폴더 위치 불일치 | `Assets/Resources/<PrefabKey>.prefab` 확인 |
| 방송이 안 들림 | `Subscribe` 를 안 했거나, 다른 이벤트 타입을 구독 | `EventBus<정확한타입>` 확인, `SubscriberCount` 로 확인 |
| 오브젝트가 사라진 뒤 `MissingReferenceException` | `Unsubscribe` 누락 | `OnDisable` 에서 해제 |
| 방송은 했는데 아무도 반응 안 함 | 정상일 수 있음 (구독자 0명도 OK) | 듣는 쪽이 아직 구현 전인지 확인 |

---

## 7. ✅ 코드 올리기 전 체크리스트

- [ ] 다른 파트 폴더의 클래스를 직접 쓰지 않았다 (`using HybridShooter.Core...` 만)
- [ ] 코드에 숫자를 박지 않았다 (테이블 / `Config`)
- [ ] `Def` 를 수정하지 않았다 (현재 상태는 내 변수에)
- [ ] `Subscribe` 마다 `Unsubscribe` 가 `OnDisable` 에 있다
- [ ] `RoundCleanupEvent` 를 처리했다 (내 오브젝트·코루틴 정리)
- [ ] 내가 맡은 "발행 책임" 이벤트를 방송한다 (예: 몬스터 사망 시 `MonsterDiedEvent`)
- [ ] 서비스를 제공한다면 `OnEnable` 등록 / `OnDisable` 해제
- [ ] 서비스 `Get` 결과가 `null` 일 수 있는 곳에서 확인했다
- [ ] 테이블을 고쳤다면 `Tools > Data > Validate Tables` 오류 0
- [ ] `Test_` 씬에서 혼자 Play 해봤다

---

## 8. 🗺 한 장 치트시트

```
 데이터 꺼내기   DataManager.Monsters.Get(id)           DataManager.Config.GetFloat(GameConfig.키, 기본값)
 피해 주기       col.TryGetDamageable(out t) && t.CanBeHitBy(Faction.Player) → t.TakeDamage(new DamageInfo(...))
 서비스 쓰기     ServiceLocator.Get<ISoundService>().PlaySfx("키")        없을 수도 → TryGet<T>(out s)
 서비스 제공     OnEnable: Register<T>(this)   OnDisable: Unregister<T>(this)
 방송            EventBus<XEvent>.Publish(new XEvent(...))
 듣기            OnEnable: Subscribe(OnX)       OnDisable: Unsubscribe(OnX)       (+ RoundCleanupEvent 필수)

 💬 결과가 필요하면 → 서비스 / 계약     📢 알리기만 하면 → 이벤트     📊 수치는 → 테이블
```

---

### 더 읽을거리
- 전체 설계 의도·담당 폴더·시나리오: [Architecture/02-SystemInterfaces](../Architecture/02-SystemInterfaces.md)
- 백로그별 필요한 계약·이벤트: [Architecture/03-BacklogMapping](../Architecture/03-BacklogMapping.md)
- 테이블 컬럼 명세: [Tables/README](../Tables/README.md)
- 작업 흐름(Asana + Git): [Guides/Workflow](Workflow.md)
