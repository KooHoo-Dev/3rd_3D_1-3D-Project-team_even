# 사운드 시스템 리팩토링 v3.1 — develop 최신 기준 재점검 (검토안)

> 기준: `feat/FixedSoundSystem` `6b1ef86f` = develop `3acdf56b`(PR #42 보스 패턴) 머지 · 이전 설계: [SoundSystemRefactor.md](SoundSystemRefactor.md) v2.3 (사운드 1~3단계, develop 머지 `1eeb90f2`)
> 적용 기준: `이벤트 버스와 외부 데이터 적용 기준` v0.1 — **판정 단위는 통신 한 건과 데이터 항목**
> v3 → v3.1: PR #42 로 보스 패턴(짓밟기·마그마 포격·흡입)이 실제로 구현됨 → **보스 설계(S2)를 실제 구조(`BossAttack` 컴포넌트) 기준으로 다시 씀**. Core 인터페이스·`BossPattern` enum 안은 철회. M9(BossDto 중복 재발), 보스 처치 이벤트 발행 불가 문제 추가.
> 상태: **검토안**. 6장의 보완 사항을 확인받은 뒤 코드 작업을 시작한다.

---

## 0. 머지 점검 (`6b1ef86f`)

| # | 문제 | 영향 | 조치 |
|---|---|---|---|
| **M9** | `Core/Data/Dtos/BossDto.cs` 와 `BossDTO.cs` 가 **다시 둘 다** Git 에 있다 (내용 동일, `.meta` 는 `BossDTO.cs.meta` 하나). M3 에서 정리했던 것이 PR #42 (`8cee2c35`) 로 재발 | Windows 는 한 파일로 보이지만 macOS/Linux 에서 클래스 중복, Unity GUID 혼란 | ✅ **`BossDto.cs` 로 통일** (팀 결정, M3 의 `BossDTO.cs` 결정을 뒤집음) — 인덱스의 `BossDTO.cs` 제거, `BossDTO.cs.meta` → `BossDto.cs.meta` (GUID `48509b32…` 유지). **보스 담당 브랜치도 `BossDto.cs` 로 맞춰야** 다음 머지에 재발하지 않음 |
| M10 | 사운드 테이블 열 | `BossTable`·`ClypeosaurusTable` 이 바뀌었지만 사운드 열이 없던 테이블이라 충돌 없음. 몬스터·무기 테이블 사운드 키 유지 확인 | 없음 |

## 1. 이전 작업 진행 현황

| 항목 | 상태 | 비고 |
|---|---|---|
| R1~R10 (몬스터 예고·기절·착지, Router 분할, 플레이어, 테이블 열, 계약, 믹서, 정리, 독장판) | ✅ 완료 | R8 은 Unity 재생 확인 기록 없음 |
| R3 사망 시 울음 정지 | ✅ 완료 | Router 가 `MonsterDiedEvent.Instance` 에서 이미터를 찾아 `Silence()` → **재검토 (S1)** |
| **R11 보스 사운드 경로** | ❌ **미착수** | `BossSoundEmitter` 없음. Router 보스 처리는 옛 `MonsterDef` 이벤트 그대로 |
| M5 `SoundTester` 보스 버튼 | ❌ 미착수 | |
| M7 테이블 머지 충돌 반복 | ✅ 원인 해소 | 사운드 키 열이 develop 에 들어감 |

## 2. develop 에서 새로 생긴 변경 (사운드 영향)

### 2-1. PR #42 보스 패턴 (`3acdf56b`)

| 바뀐 점 | 내용 | 사운드 영향 |
|---|---|---|
| **`BossAttack` 기반 클래스** | 패턴 = 보스 오브젝트의 `BossAttack` 컴포넌트. `Phase`(Windup→Active→Recover→Idle, `SkillPhase` 재사용), `TryStart`·`Cancel`. **단계 변화 알림(이벤트)은 없다** | 예고음·루프 시점을 받으려면 단계 변화 알림이 필요 |
| `BossPatternRunner` | 시작 가능한 패턴 중 무작위 선택. 공통 쿨타임 `BossTable.PatternCooldown` | — |
| **짓밟기** `StompAttack` | 예고(IdleLookAround) → `SmashAttackForward`. 애니메이션 이벤트 **`OnStompImpact`** 에서 타격 (중심 고정 `lockedCenter`, 반경) | 예고 포효 + **착지 충격음**(정확한 순간 = `OnStompImpact`) |
| **마그마 포격** `MagmaBarrageAttack` + `MagmaProjectile` | 예고 → `SpittersAttack`. 애니메이션 이벤트 **`OnBarrageFire`** 부터 발사구마다 `BarrageInterval` 간격으로 투사체 생성 → 포물선 비행(`BarrageFlightTime`) → **충돌 시 폭발 피해 후 파괴** | 예고음, (발사음 — 에셋 없음), **착탄 폭발음**(투사체 `OnCollisionEnter`) |
| **흡입** `SuctionAttack` | 예고 → Active 에서 `InhaleSource.Start` (SuctionStart 애니메이션) → 시간 초과·근접 체류로 끝나면 SuctionEnd → Recover. 실패·대상 상실은 `Cancel` | 예고음 + **흡입 루프**(Active 동안, `inhaleCenter` 위치) |
| 전격·안테나 | 데이터(`Electric*`, `FourOpenHpRatio` 등)만 있고 **코드 없음** | 보류 |
| 등장·사망 연출 | 없음. `BossController.Died` 만 있음 (사망 애니메이션 호출 없음) | 사망음은 `Died` 로 가능 |
| 이동 | `BossMovement.IsMoving`(일시정지 반영), `BossView` 가 Animator `IsMoving` 갱신. Walk 클립에 애니메이션 이벤트 없음 | 발소리 (에셋 없음) |
| 초기화 | 실게임 초기화 코드 없음. `BossDamageTester.Start` 가 보스·3패턴 `Initialize` | 테스트는 `Scenes/CY/TestMap_V1` 또는 사운드 테스트 씬에 보스 배치 |
| 이벤트 | `BossController` 는 **EventBus 이벤트를 하나도 발행하지 않음** (`DamageDealtEvent`·`BossDefeatedEvent` 포함) | 보스 피격 히트마커·처치 BGM 정지·클리어음 없음 (S5, 4장) |

### 2-2. 그 전 develop 변경 (v3 에서 확인)

| 커밋 | 변경 | 사운드 영향 |
|---|---|---|
| `1b833210` | 플레이어 부활·목숨 (`ReviveStarted`·`Revived`·`FinalDied` C# 이벤트, `PlayerFinalDeathEvent`) | 부활 소리 없음 (에셋 없음) |
| `0cd8dc8c` | 공중 몬스터 사망 시 추락 | 추락 충돌음 (선택, 알림 없음) |
| `eb3acf44` | `InhaleSource` (흡입 기반) | 흡입 루프 → PR #42 의 `SuctionAttack` 에서 연결 |
| `4a51e817` | 카메라 흔들림 요청 이벤트 | 소리 신호로 재사용하지 않음 (요청이지 사실이 아님) |

## 3. 추가 필요한 사운드

### 3-0. 임시 합성음 (2026-10-28, 코드 합성 — 정식 에셋이 오면 같은 경로에 덮어쓰기)

| 키 | 파일 | 내용 |
|---|---|---|
| `ui_hit_marker` | `Audio/UI/HitMarker.wav` | 0.09초 금속성 틱 (3kHz 중심) |
| `ui_kill_confirm` | `Audio/UI/KillConfirm.wav` | 0.45초 상승 2음 차임(C6→G6) + 저음 쿵 |
| `sfx_boss_warning` | `Audio/Sfx/BossWarning.wav` | 3.2초 저음 브람 + 하강 경보 3회 + 땅울림 |
| `mon_clypeosaurus_suction_loop` (**신규 키**) | `Audio/Monster/Clypeosaurus/Clypeosaurus_SuctionLoop.wav` | 4초 바람 흡입 루프 (이음매 크로스페이드) |
| `sfx_hit_flesh` | `Audio/Sfx/HitFlesh_01~03.wav` (**Path 를 3변형으로 변경**) | 0.16초 '퍽' — 저음 쿵 + 젖은 잡음 + 짧은 틱, 변형마다 높이 ±9% |
| `sfx_supply_drop` | `Audio/Sfx/SupplyDrop.wav` | 1.3초 쿵 + 금속·나무 공명 + 먼지, 0.19초 뒤 작은 되튐 |
| `mon_clypeosaurus_stomp_impact` (**신규 키**) | `Audio/Monster/Clypeosaurus/Clypeosaurus_StompImpact.wav` | 2.2초 초저음 붐(62→34Hz) + 갈라짐 + 흩어지는 잔해 |
| `mon_clypeosaurus_magma_impact` (**신규 키**) | `Audio/Monster/Clypeosaurus/Clypeosaurus_MagmaImpact.wav` | 1.1초 폭발 + 철퍽 + 지글거림. 동시 4개·쿨다운 0.05초 (연사 대비) |
| `mon_pardathrox_land` (**신규 키**) | `Audio/Monster/Pardathrox/Pardathrox_Land.wav` | 0.55초 쿵 + 흙 + 발톱 긁힘. **`MonsterAttackTable` 200201.ImpactSfxKey 에 연결 → 이미 있는 `AttackLanded` 경로로 바로 재생** |

전부 48kHz 16bit 모노, 피크 -1~-3 dBFS. 위 4개(히트마커·처치·경고·흡입)는 합성 품질 "좋음", 아래 5개는 "보통" — 정식 에셋 우선 교체 대상. Unity 에서 처음 열 때 `.meta` 가 생기므로 그 뒤에 함께 커밋 (`*.wav` 는 LFS).

### 3-1. 키는 있지만 클립 파일이 없다 (무음)
`bgm_crowd`, `bgm_boss` — 코드 경로는 연결돼 있고 **에셋만 넣으면 동작**. (나머지 5개는 3-0 임시 합성음으로 채움)

### 3-2. 보스 (지금 구현된 패턴 기준)

| 순간 | 신호 | 소리 (현재 키 / 대여) | 에셋 |
|---|---|---|---|
| 짓밟기 예고 | `StompAttack` Windup | `mon_clypeosaurus_attack` | 있음 |
| 짓밟기 착지 | `OnStompImpact` | `mon_clypeosaurus_stomp_impact` (임시 합성음) | 전용 요청 (선택) |
| 포격 예고 | `MagmaBarrageAttack` Windup | `mon_clypeosaurus_skill_attack` | 있음 |
| 포격 발사 (발사구마다) | `FireNext` | — | 요청 (없어도 됨) |
| 포격 착탄 | `MagmaProjectile` 폭발 | `mon_clypeosaurus_magma_impact` (임시 합성음) | 전용 요청 (선택) |
| 흡입 예고 | `SuctionAttack` Windup | `mon_clypeosaurus_shout` (Shout_2 변형) | 있음 |
| 흡입 중 | `SuctionAttack` Active ~ 끝 | `mon_clypeosaurus_suction_loop` (임시 합성음) | 전용 요청 (선택) |
| 사망 | `BossController.Died` | `mon_clypeosaurus_hurt` 대여 | 요청 |
| 피격 피드백 | `DamageDealtEvent` (S5) | 히트마커·타격음·처치 확인음 (Router 기존 경로) | 3-1 |
| 등장·전격·안테나·발소리 | 보스 코드 없음 | `mon_clypeosaurus_attack2` 등 대기 | — |

### 3-3. 그 외
| 우선 | 소리 | 연결 지점 | 에셋 |
|---|---|---|---|
| 🟠 | 플레이어 부활 시작·완료 | `ReviveStarted`·`Revived` | 없음 — 오면 `PlayerSoundEmitter` 2줄 |
| 🟢 | 공중 몬스터 추락 충돌 | 몬스터 알림 없음 | 보류 |
| 🟢 | 회복 사용·구르기 | `HealStarted`·`RollStarted` | 없음 |
| 대기 | `ann_count_*`·`ann_headshot`·연속 처치·`ann_defeat`·`ui_menu_*`·`mon_pardathrox_vanish`·`sfx_player_footstep_metal` | 기능 없음 | 코드 추가 안 함 |
| 대여 중 | `sfx_weapon_switch`, `sfx_supply_collect`, SMG·저격·유탄 발사음, 돌진 벽 충돌 | — | 전용 오면 `Path` 만 교체 |

---

## 4. 기준 적용 — 통신 한 건씩 판정

판정 질문: ① 소유자가 확정한 **사실**인가 ② 수신자 책임이 독립적인가 ③ 발행자가 결과·순서를 필요로 하는가 ④ 왜 전역이어야 하는가 ⑤ 지금 실제 소비자가 있는가

### 4-1. 전역 EventBus 구독

| 통신 | 다른 소비자 | 판정 | 근거 |
|---|---|---|---|
| `GameStateChangedEvent` → Reward 스냅샷 | UpgradeManager, PlayerHud, BossView(플레이어) | **유지** | 코어 사실, 독립 소비자 여럿 |
| `RoundStartedEvent` → 군중 BGM·`ann_fight` | UpgradeManager, KillRewardCollector | **유지** | 기준 문서 예시 그대로 |
| `RoundCleanupEvent` → 경고·아나운서·SFX 정지 | 거의 전 파트 | **유지** | |
| `GameOverEvent` → 게임오버음 | (없음) | **유지 + 위험 보고** | 코어 소유 사실. **발행자 없음** (RoundDirector 미구현) |
| `MonsterSpawnedEvent` → 이미터 자동 부착 | 코어 | **유지** | R1-a 결정. 결과를 돌려주지 않는 관찰 |
| `MonsterDiedEvent` → 이미터 `Silence()` + 사망음 | KillRewardCollector | **변경 (S1)** | 몬스터 자기 몸 소리 = 지역 이벤트. Router 가 `Instance` 안 컴포넌트를 찾아 조정하는 구조 제거 |
| `DamageDealtEvent` → 타격음·히트마커·처치 확인음 | UI(데미지 숫자) | **유지** | |
| `PlayerDamagedEvent` → 피격 목소리·기침 | DamageVignetteHud, PlayerAnimationDriver | **구독자 이동 (S6)** | 치명타 때 피격+사망 목소리 겹침. 플레이어 목소리를 한 컴포넌트에서 처리해야 막을 수 있다 |
| `PlayerHpChangedEvent`·`PlayerDiedEvent` → 저체력 경고 | PlayerHud 등 | **유지** | |
| `BossWarningEvent` → 경고음 + 덕킹 | (예정) | **유지** | 페이로드 안 읽음 |
| `BossAppearedEvent` → 보스 BGM | UpgradeManager, CameraShake | **유지** | 페이로드 안 읽음 |
| `BossSkillPhaseChangedEvent` → 공격 예고음 | **없음** | **삭제 (S3)** | 실제 보스는 `BossAttack` 컴포넌트 → 지역 이벤트(S2). 발행자·소비자 없음 |
| `BossDamageStageChangedEvent` → 보스 Hurt | **없음** | **삭제 (S3)** | 안테나 코드 없음. 생기면 보스 지역 알림으로 |
| `BossDefeatedEvent` → 사망음·BGM 정지·클리어·`ann_victory` | UpgradeManager, KillRewardCollector | **유지 (사망음 제외)** | 사망음은 보스 몸 소리 → S2 (`Died` 지역 이벤트) |
| `UpgradePurchasedEvent`·`SupplyCollectedEvent` | Gun·PlayerItems / SupplyHud | **유지** | |
| `CameraModeChangedEvent`·`PlayerStaminaChangedEvent` | — | **유지** | |

> S1·S2·S3 후 Router 는 **보스 이벤트 페이로드(`MonsterDef`)를 하나도 읽지 않는다.**

### 4-2. 직접 호출·지역 이벤트

| 통신 | 판정 | 근거 |
|---|---|---|
| `Gun`·`GunProjectile`·`Grenade`·`SupplyDropper`·`PoisonProjectile`·`PoisonZone` → `ISoundService` 직접 | 유지 | 자기 동작의 효과 = 직접 호출 |
| `MonsterController` → `MonsterSoundEmitter` | 유지 + `Died` (S1) | 객체 지역 이벤트 |
| `PlayerController`·`PlayerItems`·`GunSlot` → `PlayerSoundEmitter` | 유지 | 객체 지역 이벤트 |
| **`BossAttack`·`BossController` → `BossSoundEmitter`** | **신규 (S2)** | 같은 보스 프리팹 안의 협력. 전역 버스·Core 인터페이스 불필요 |
| **`MagmaProjectile` 폭발 → `ISoundService` 직접** | **신규 (S2)** | `GunProjectile`·`PoisonProjectile` 과 같은 "투사체 자기 효과" |

### 4-3. 데이터 항목

| 항목 | 현재 위치 | 판정 | 근거 |
|---|---|---|---|
| 몬스터·공격·상태이상·무기·수류탄·보급 사운드 키 | TSV → `LinkSounds` | 유지 | 종류별 변형, 로드 시 검증 |
| SoundTable | TSV | 유지 | |
| Router 의 BGM·UI·아나운서 키, 덕킹 값 | Router Inspector | 유지 | 한 벌뿐 |
| 플레이어 소리 키 | `PlayerSoundEmitter` Inspector | 유지 | 플레이어 1종 |
| **보스 패턴·사망 사운드 키** | (없음) | **`BossSoundEmitter` Inspector (S4)** | 보스 1종, 패턴 = 프리팹 컴포넌트. 패턴 컴포넌트를 Inspector 에서 바로 짝지을 수 있다. 테이블안은 `BossDto/Def`·`ClypeosaurusDto/Def` 필드 + `LinkSounds` + 보스 담당 테이블 수정 비용 |
| **마그마 착탄음 키** | (없음) | **`MagmaProjectile` 프리팹 Inspector (S2)** | 투사체 프리팹에 묶인 값 (`GunProjectile.sfxKey` 와 같은 방식) |
| `MonsterTable.HurtSfxKey` | TSV 열 (전 행 빈칸) | **삭제 (S9)** | S3 후 소비자 0 |
| 로켓·유탄 폭발음 | `WeaponTable.ExplodeSfxKey` + 코드 기본값 | **기준값 하나로 (S10)** | 같은 의미의 값 두 곳 |

---

## 5. 설계 — 변경 항목

### S1. 몬스터 사망 소리를 지역 이벤트로
```csharp
public interface IMonsterSoundSource
{
    // 기존: Def, AttackPhaseChanged, Stunned, AttackLanded
    /// <summary>사망 확정. 울음·루프를 끄고 사망음을 낸다</summary>
    event Action Died;
}
```
- `MonsterController`: `MonsterDiedEvent` 발행 바로 앞에 `Died?.Invoke()` (+ 선언) — 몬스터 2줄
- `MonsterSoundEmitter`: `Died` → `Silence()` + `Def.DeathSfx`
- Router: `MonsterDiedEvent` 구독·`OnMonsterDied` 삭제

### S2. 보스 사운드 — `BossAttack` 지역 이벤트 + `BossSoundEmitter` (R11 다시 설계)

**v3 안(Core `IBossSoundSource` + `BossPattern` enum)을 철회한다.** 실제 보스는 패턴마다 `BossAttack` 컴포넌트이고, Audio 와 Boss 가 같은 어셈블리라 직접 참조할 수 있다. 가짜 소스 테스터가 필요 없어졌다(실제 보스로 테스트 가능) → 인터페이스의 현재 이익이 없다.

**보스 코드 추가 (보스 담당 파일)**
```csharp
// BossAttack (기반 클래스) — Phase 를 바꾸는 5곳을 SetPhase 하나로 모으고 알림
public event Action<BossAttack, SkillPhase> PhaseChanged;
/// <summary>패턴이 땅·대상에 닿은 순간 (지금은 짓밟기 착지만)</summary>
public event Action<BossAttack, Vector3> Impacted;
protected void RaiseImpacted(Vector3 point) => Impacted?.Invoke(this, point);

// StompAttack.OnStompImpact — 타격 판정 직전 1줄
RaiseImpacted(lockedCenter);

// MagmaProjectile — 폭발 지점에서 1~2줄 + [SerializeField] string explodeSfxKey
```
- 패턴별 하위 클래스는 단계 알림을 직접 다루지 않는다 (기반 클래스가 처리)
- **Windup 알림은 `OnWindup()` 이 끝난 뒤, 아직 실행 중일 때만** 보낸다. 포격은 `OnWindup` 안에서 낙하 지점을 못 찾으면 바로 `Cancel` 하므로, 먼저 알리면 취소된 공격의 예고음이 난다. Active 알림도 같은 이유로 `OnActiveStarted()` 뒤 (흡입은 시작 실패 시 `Cancel`)
- 사망: 이미 있는 `BossController.Died` 사용 (추가 없음)

**`BossSoundEmitter` (Audio, 보스 프리팹에 부착 — S7)**
```csharp
[Serializable] struct AttackSound
{
    public BossAttack Attack;      // 같은 프리팹의 패턴 컴포넌트
    public string WindupKey;       // 예고 시작
    public string ActiveLoopKey;   // Active 동안 루프 (흡입). Recover/Idle 에서 정지
    public Transform LoopPoint;    // 루프 위치 (흡입 = inhaleCenter). 비우면 보스
    public string ImpactKey;       // Impacted 지점
}
[SerializeField] AttackSound[] attacks;
[SerializeField] string deathKey;
```
- `Awake`: 목록의 공격을 구독. 키는 `DataManager.Sounds` 로 확인해 없으면 경고 (로드 시 검증 대신)
- `Died` → 모든 루프 정지 + 사망음. 사망 시 공격은 `Cancel` 되므로 단계 알림(Idle)으로도 루프가 멈춘다
- 기본값: 짓밟기 `mon_clypeosaurus_attack` / 착지 `mon_clypeosaurus_stomp_impact`, 포격 `mon_clypeosaurus_skill_attack`, 흡입 `mon_clypeosaurus_shout` / 루프 `mon_clypeosaurus_suction_loop`, 사망 `mon_clypeosaurus_hurt`(대여), 마그마 착탄 `mon_clypeosaurus_magma_impact`

**검토했지만 쓰지 않는 안**
| 안 | 이유 |
|---|---|
| `BossSoundEmitter` 에 `OnStompImpact()`·`OnBarrageFire()` 메서드를 두어 같은 애니메이션 이벤트를 받기 (보스 코드 수정 0) | 애니메이션 이벤트 이름에 몰래 묶이고, `StompAttack` 의 Active 단계·중복 방지 판정을 거치지 않아 취소된 공격에도 소리가 난다 |
| `BossAttack.Phase` 를 이미터가 매 프레임 읽기 (보스 코드 수정 0) | 예고 시작을 한 프레임 늦게 알고, 같은 프레임 안 Windup→Cancel(포격 낙하 지점 실패) 을 놓친다 |
| `BossController` 가 패턴 알림을 대신 전달 | 컨트롤러는 시작·끝만 알고 중간 단계를 모른다 → 결국 `BossAttack` 수정 필요 |

**Router `.Boss` 정리**: 경고(경고음·덕킹), 등장(보스 BGM), 처치(BGM 정지·클리어·아나운서)만 남김. `boss`·`bossInstance`·`BossPosition()`·`OnBossSkillPhaseChanged`·`OnBossDamageStageChanged` 삭제.

### S3. 쓰지 않는 보스 EventBus 이벤트 삭제
`BossSkillPhaseChangedEvent`, `BossDamageStageChangedEvent` (발행자·소비자 없음). `GameEvents.cs`·`02-SystemInterfaces.md` 갱신.

### S4. 보스 사운드 키 = `BossSoundEmitter` Inspector
기존 R11 ④-a(보스 테이블 열) 결정을 바꾼다. 보스가 늘거나 표로 비교할 일이 생기면 그때 테이블로 옮긴다.

### S5. 보스 피격 피드백
`BossController.TakeDamage` 에 `DamageDealtEvent` 발행 1줄 (처치면 `IsKill = true`) — 보스 파일.

### S6. 플레이어 피격 목소리를 `PlayerSoundEmitter` 로
`PlayerDamagedEvent` 구독과 키 2개를 Router → `PlayerSoundEmitter`. 피해로 죽었으면(`IsAlive == false`) 피격 목소리 생략.

### S7. 보스 이미터 = 프리팹 직접 부착
자동 부착은 등장 이벤트 순서에 의존하고, 지금은 보스 등장 이벤트 발행자도 없다. `Resources/Boss/Clypeosaurus/Prefab/Clypeosaurus.prefab` 에 `BossSoundEmitter` 추가 (보스 프리팹 1회 수정).

### S8. 테스터
- `SoundTester`: 보스 1~6·S 버튼(옛 이벤트) 삭제, 경고·등장 버튼 유지
- 보스 소리 확인: `Tools > Audio > Create Sound Test Scene` 에 보스 프리팹 + `BossDamageTester` 배치 (테스트 플레이어 타깃은 기존 `SoundTestPlayerTarget`), 또는 `Scenes/CY/TestMap_V1` 사용 — **Q9**

### S9. `MonsterTable.HurtSfxKey` 열 삭제
TSV 열, `MonsterDto/Def`, `LinkSounds` 1줄, 헤더 주석.

### S10. 폭발음 기준값 하나로
`RocketLauncher`·`GrenadeLauncher` 코드 기본값 `sfxKey = "sfx_grenade_explode"` 제거. 동작 변화 없음.

### S11. 문서
`SoundSystemGuide.md`, `02-SystemInterfaces.md`, `GameEvents.cs` 주석, 이 문서 결과 반영.

---

## 6. 보완 사항 재점검 — 확인 필요

| # | 질문 | 권장 | 영향 |
|---|---|---|---|
| Q1 | S1 몬스터 사망을 `IMonsterSoundSource.Died` 지역 이벤트로 | 예 | 몬스터 2줄 |
| Q2 | S2 보스 소리를 `BossAttack.PhaseChanged`·`Impacted` 지역 이벤트로 (v3 의 Core 인터페이스안 철회) | 예 | 보스 기반 클래스 ~15줄, `StompAttack` 1줄, `MagmaProjectile` 2줄 + 키 필드 |
| Q3 | S3 안 쓰는 보스 이벤트 2개 Core 에서 삭제 | 삭제 | `GameEvents.cs` |
| Q4 | S4 보스 키를 테이블 대신 이미터 Inspector (기존 결정 변경) | Inspector | |
| Q5 | S6 플레이어 피격 목소리 이동 | 예 | Audio 만 |
| Q6 | S7 보스 이미터 프리팹 직접 부착 (기존 결정 중 보스 부분 변경) | 프리팹 | 보스 프리팹 1회 |
| Q7 | S9 `HurtSfxKey` 열 삭제 | 삭제 | 데이터 열 1개 |
| Q8 | S10 폭발음 코드 기본값 제거 | 제거 | 무기 2파일 |
| Q9 | S8 보스 소리 테스트 위치: 사운드 테스트 씬에 보스 추가 / `TestMap_V1` 사용 | 사운드 테스트 씬 | 씬 생성 메뉴 수정 |
| ~~Q10~~ | ~~M9 `BossDto` 중복 정리~~ | ✅ `BossDto.cs` 로 통일 완료 | |

### 범위 밖 — 담당 파트 전달
- **코어·보상**: `BossDefeatedEvent(MonsterDef)` 는 **새 보스가 발행할 수 없다** (`BossController` 는 `BossDef` 만 있고, `KillRewardCollector` 가 `e.Boss.Reward`(MonsterDef) 를 읽음 — `BossDef` 에는 `Reward` 없음). 이대로면 실게임에서 보스 처치 보상·강화 화면·BGM 정지·클리어음이 모두 없다. 페이로드를 `BossDef` 로 바꾸고 `BossTable.Reward` 를 둘지 결정 필요. 사운드는 페이로드를 읽지 않으므로 어느 쪽이든 영향 없음
- **코어**: `GameOverEvent`·`RoundStartedEvent`·`GameStateChangedEvent`·`BossWarning/Appeared` 실게임 발행자 없음 (RoundDirector 미구현)
- **보스**: S2 기반 클래스 알림, S5 `DamageDealtEvent`, S7 프리팹, M9 대소문자 정리, `SmashAttack.FBX` 의 이름 없는 애니메이션 이벤트(`NewEvent`)는 받는 곳이 없어 재생 시 경고가 날 수 있음
- **에셋 요청**: BGM 2곡, 보스 사망, 플레이어 부활 (필수) / 3-0 임시 합성음 9개의 정식 교체 (선택, "보통" 5개 우선)

### 작업 순서 (확인 후)
1. 정리: M9, S10
2. Audio 단독: S6, S8(SoundTester 정리)
3. 계약·알림: S1 `IMonsterSoundSource.Died` + `MonsterController`, S2 `BossAttack` 알림 + `StompAttack`·`MagmaProjectile`, S3 이벤트 삭제, S5
4. 연결: `MonsterSoundEmitter` 사망, `BossSoundEmitter` 신규 + Router `.Boss` 정리, S7 프리팹 부착
5. 데이터: S9
6. 테스트 씬(S8) → 문서(S11)
