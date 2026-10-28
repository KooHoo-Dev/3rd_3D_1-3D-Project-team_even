# 사운드 시스템 리팩토링 v3 — develop 최신 기준 재점검 (검토안)

> 기준: develop `53fff7f9` (= 현재 `feat/FixedSoundSystem`) · 이전 설계: [SoundSystemRefactor.md](SoundSystemRefactor.md) v2.3 (사운드 1~3단계, develop 머지 `1eeb90f2`)
> 적용 기준: `이벤트 버스와 외부 데이터 적용 기준` v0.1 — **판정 단위는 통신 한 건과 데이터 항목**
> 상태: **검토안**. 6장의 보완 사항을 확인받은 뒤 코드 작업을 시작한다.

---

## 1. 이전 작업 진행 현황

| 항목 | 상태 | 비고 |
|---|---|---|
| R1 몬스터 공격 예고·기절·도약 착지 (`IMonsterSoundSource`) | ✅ 완료 | `MonsterController` 구현, 이미터 자동 부착 |
| R2 몬스터 Def 출처 단일화 | ✅ 완료 | 이미터가 `IMonsterSoundSource.Def` 우선 |
| R3 사망 시 울음 정지 | ✅ 완료 | Router 가 `MonsterDiedEvent.Instance` 의 이미터를 찾아 `Silence()` → **v3 에서 재검토 (S1)** |
| R4 Router partial 분할 + UI·아나운서 | ✅ 완료 | |
| R5 `PlayerSoundEmitter` | ✅ 완료 | 흙 발소리만 |
| R6 엔티티 소리 테이블 컬럼 | ✅ 완료 | 무기·보급·독가스·기절·충돌·착지 |
| R7 `ISoundService` 확장 | ✅ 완료 | |
| R8 AudioMixer | ✅ 코드·에셋 완료 | Unity 재생 확인 기록 없음 |
| R9 `SoundService` 가벼운 정리 | ✅ 완료 | `BgmPlayer` 분리 |
| R10 `DamageKind.Status` (독장판 기침) | ✅ 완료 | |
| **R11 보스 사운드 경로** | ❌ **미착수** | `BossSoundEmitter` 없음 (Router 주석에만 언급). Router 보스 처리는 옛 `MonsterDef` 이벤트 그대로 |
| M5 `SoundTester` 보스 버튼 | ❌ 미착수 | `MonsterTable` 의 보스 행이 없어 동작 안 함 |
| M7 테이블 머지 충돌 반복 | ✅ **원인 해소** | 사운드 키 열이 `1eeb90f2` 로 develop 에 들어감. 이후 develop 의 `MonsterTable` 수정(`10180a06`)에서도 키 값 유지 확인 |

## 2. develop 에서 새로 생긴 변경 (사운드 영향)

| 커밋 | 변경 | 사운드 영향 |
|---|---|---|
| `1b833210` (10-27) | 플레이어 **부활·목숨 시스템**: `ReviveStarted`·`ReviveBodyRestored`·`Revived`·`FinalDied`·`LifeReset` C# 이벤트, `PlayerRevivedEvent`·`PlayerFinalDeathEvent`·`PlayerLivesChangedEvent` | 부활 소리 없음. `Died` 가 "최종 사망"이 아니게 됨 → 사망 목소리는 그대로 맞음. 게임오버 소리는 `GameOverEvent` 기준인데 **발행자가 없음** (4장 참고) |
| `0cd8dc8c` | 공중 몬스터 사망 시 땅으로 추락 (`flyingExecutor.StartFalling`) | 추락·바닥 충돌음 없음 (선택) |
| `24e14f66` → `eb3acf44` | 보스 **흡입** 패턴 기본 구현: `InhaleSource`(Core), `IPlayerExternalVelocity` | 흡입 중 빨아들이는 바람 소리 필요. 패턴 단계로 받으면 R11 계약에 `Inhale` 하나 추가로 해결 |
| `4a51e817` | 보스 등장·발걸음 카메라 흔들림 (`CameraShakeRequestedEvent`) | 보스 발소리와 같은 순간. 흔들림 요청 이벤트를 소리 신호로 재사용하지 **않는다** (요청이지 사실이 아님) → 보스 쪽 `Stepped` 알림 제안 (S2) |
| `10180a06` | 몬스터 프리팹 5종이 `Resources/Prefabs/Monsters/` 로 이동, `MonsterTable.PrefabKey` 갱신 | 사운드 키 열 유지 확인. 부포모프·덴솝테르 프리팹은 아직 없음 (사운드 무관) |

## 3. 추가 필요한 사운드

### 3-1. 키는 있지만 클립 파일이 없다 (무음)
`bgm_crowd`, `bgm_boss`, `sfx_hit_flesh`, `sfx_boss_warning`, `sfx_supply_drop`, `ui_hit_marker`, `ui_kill_confirm` — 코드 경로는 모두 연결돼 있고 **에셋만 넣으면 동작**.

### 3-2. 코드 경로가 없거나 미완성
| 우선 | 소리 | 연결 지점 | 에셋 |
|---|---|---|---|
| 🔴 | 보스 등장·패턴 예고·안테나 개방·사망 | R11 (S2) | 기존 `mon_clypeosaurus_*` 재배정 |
| 🔴 | 보스 피격 히트마커·타격음·처치 확인음 | `BossController` 가 `DamageDealtEvent` 미발행 (S5) | 있음 |
| 🟠 | 보스 흡입 루프 (신규) | R11 `PatternPhaseChanged(Inhale)` | **없음** — 요청 |
| 🟠 | 보스 짓밟기 착지·포격 착탄·전격 발사 | R11 (S2 `PatternImpact`) | 착지는 `sfx_explosion_large` 대여, 나머지 없음 |
| 🟠 | 보스 발소리 | R11 (S2 `Stepped`) | 없음 |
| 🟠 | 플레이어 부활 시작·완료 (신규) | `PlayerController.ReviveStarted`·`Revived` | **없음** — 요청. 에셋이 오면 `PlayerSoundEmitter` 에 2줄 |
| 🟢 | 공중 몬스터 추락 충돌음 (신규) | 몬스터 쪽 알림 없음 | 없음 — 보류 |
| 🟢 | 회복 사용·구르기 | `HealStarted`·`RollStarted` (이미 있음) | 없음 |

### 3-3. 에셋은 있지만 쓸 기능이 아직 없다 (대기, 코드 추가 안 함)
`ann_count_1~3`(카운트다운 없음), `ann_headshot`(헤드샷 판정 없음), 연속 처치 아나운서 9종(R4-A 결정으로 미사용), `ann_defeat`, `ui_menu_cursor/select`(메뉴 UI 없음), `mon_pardathrox_vanish`(은신 없음), `sfx_player_footstep_metal`(맵 미확정)

### 3-4. 대여 중 (전용 에셋 오면 SoundTable `Path` 만 교체)
`sfx_weapon_switch`, `sfx_supply_collect`, `sfx_smg/sniper/grenade_launcher_fire`, 돌진 벽 충돌(`sfx_explosion_small`)

---

## 4. 기준 적용 — 통신 한 건씩 판정

판정 질문: ① 소유자가 확정한 **사실**인가 ② 수신자들의 책임이 독립적인가 ③ 발행자가 결과·순서를 필요로 하는가 ④ 왜 전역이어야 하는가 ⑤ 지금 실제 소비자가 있는가

### 4-1. 전역 EventBus 구독 (`GameSoundRouter`·`SoundService`·`PlayerSoundEmitter`)

| 통신 | 다른 소비자 | 판정 | 근거 |
|---|---|---|---|
| `GameStateChangedEvent` → Reward 스냅샷 | UpgradeManager, PlayerHud, PlayerController.BossView | **유지** | 코어 사실, 독립 소비자 여럿 |
| `RoundStartedEvent` → 군중 BGM·`ann_fight` | UpgradeManager, KillRewardCollector | **유지** | 기준 문서 예시 그대로 ("라운드 시작 → 독립 배너·BGM") |
| `RoundCleanupEvent` → 경고·아나운서 정지, 전체 SFX 정지 | 거의 전 파트 | **유지** | |
| `GameOverEvent` → 게임오버음·아나운서 | (없음) | **유지 + 위험 보고** | 코어 소유 사실이 맞다. 단 **발행자가 없다** (RoundDirector 미구현). `PlayerFinalDeathEvent` 로 바꾸지 않는다 — 게임오버 판정은 코어 소유 |
| `MonsterSpawnedEvent` → 이미터 자동 부착 | 코어(스폰 카운트) | **유지** | R1-a 결정. 사실을 관찰해 자기 컴포넌트를 붙일 뿐 발행자에게 결과를 돌려주지 않음 |
| `MonsterDiedEvent` → 이미터 `Silence()` + 사망음 | KillRewardCollector | **변경 (S1)** | 사망음·울음 정지는 **몬스터 자기 몸 소리**. 기준표 "몬스터 피격 → 자기 Animator·VFX = 지역 이벤트" 와 같은 경우. 지금은 Router 가 전역 사실을 받아 `Instance` 안의 컴포넌트를 다시 찾아 조정한다 |
| `DamageDealtEvent` → 타격음·히트마커·처치 확인음 | UI(데미지 숫자) | **유지** | 독립 소비자, 공격자 무관한 2D 피드백 |
| `PlayerDamagedEvent` → 피격 목소리·기침 | DamageVignetteHud, PlayerAnimationDriver | **구독자 이동 (S6)** | 이벤트 사용은 맞다. 다만 플레이어 목소리(점프·사망·숨)는 `PlayerSoundEmitter` 에 있어 **치명타 순간 피격+사망 목소리가 겹친다**. 한 컴포넌트에서 처리해야 막을 수 있다 |
| `PlayerHpChangedEvent` / `PlayerDiedEvent` → 저체력 경고 반복 | PlayerHud, DamageVignetteHud | **유지** | UI 피드백 |
| `BossWarningEvent` → 경고음 + 몬스터 덕킹 | (코어 → 보스·UI 예정) | **유지** | 페이로드는 읽지 않음 |
| `BossAppearedEvent` → 보스 BGM | UpgradeManager, CameraShake | **유지 (BGM 만)** | 보스 포효는 보스 몸 소리 → S2 |
| `BossSkillPhaseChangedEvent` → 공격 예고음 | **없음** (SoundTester 만 발행) | **삭제 (S3)** | 패턴 소리는 보스 몸 소리 → 지역 이벤트. 실제 소비자·발행자 모두 없음 |
| `BossDamageStageChangedEvent` → 보스 Hurt | **없음** (SoundTester 만 발행) | **삭제 (S3)** | 안테나 개방 = 보스 지역 알림 |
| `BossDefeatedEvent` → 사망음·BGM 정지·클리어·`ann_victory` | UpgradeManager, KillRewardCollector | **유지 (사망음 제외)** | 사망음은 보스 몸 소리 → S2 (`Died` 이미 있음) |
| `UpgradePurchasedEvent` → `ui_upgrade_select` | Gun, PlayerItems | **유지** | |
| `SupplyCollectedEvent` → 획득음 | SupplyHud | **유지** | |
| `CameraModeChangedEvent` → 리스너 다시 찾기 (`SoundService`) | — | **유지** | |
| `PlayerStaminaChangedEvent` → 숨소리 루프 (`PlayerSoundEmitter`) | PlayerHud | **유지** | 플레이어에 지역 C# 이벤트가 없음 |

> S1·S3 후 Router 는 **보스 이벤트 페이로드(`MonsterDef`)를 하나도 읽지 않는다** → 보스 이벤트를 `BossDef` 로 바꾸는 코어 결정이 사운드 작업을 막지 않게 된다.

### 4-2. 직접 호출·지역 이벤트 (유지 확인)

| 통신 | 판정 | 근거 |
|---|---|---|
| `Gun` 발사·장전·빈 탄창 → `ISoundService` 직접 | 유지 | "성공한 발사 → 무기 자체 효과 = 직접 호출" |
| `GunProjectile`·`Grenade` 폭발 → 직접 | 유지 | 같은 이유 |
| `SupplyDropper` 투하음 → 직접 | 유지 | 보급 모듈 내부 |
| `PoisonProjectile` 착탄·`PoisonZone` 루프 → 직접 | 유지 | 몬스터 모듈 내부 |
| `MonsterController` → `MonsterSoundEmitter` (`IMonsterSoundSource`) | 유지 + `Died` 추가 (S1) | 객체 지역 이벤트 |
| `PlayerController`·`PlayerItems`·`GunSlot` → `PlayerSoundEmitter` | 유지 | 객체 지역 이벤트 |
| **`BossController` → `BossSoundEmitter`** | **신규 (S2)** | 객체 지역 이벤트 |

### 4-3. 데이터 항목 판정

| 항목 | 현재 위치 | 판정 | 근거 |
|---|---|---|---|
| 몬스터·공격·상태이상·무기·수류탄·보급 사운드 키 | TSV 열 → `LinkSounds` | **유지** | 종류별 변형, 로드 시 키 검증 |
| SoundTable (볼륨·쿨다운·최대 수·거리) | TSV | **유지** | 행 비교·일괄 조정 대상 |
| Router 의 BGM·UI·아나운서 키, 덕킹 값 | Router Inspector | **유지** | 한 벌뿐. 라운드별 BGM 요구가 생기면 그때 `RoundTable.BgmKey` |
| 플레이어 소리 키·보폭·숨 비율 | `PlayerSoundEmitter` Inspector | **유지** | 플레이어 1종, 화면 보며 조정 |
| **보스 사운드 키** | (없음) R11 ④-a = `BossTable`+`ClypeosaurusTable` 열 | **재검토 (S4)** | 보스 1종·패턴 테이블 1행이라 행 비교 이익이 없다. 테이블안은 DTO·Def 각 10필드 + `LinkSounds` 추가 비용 |
| `MonsterTable.HurtSfxKey` | TSV 열 (전 행 빈칸) | **삭제 (S9)** | 보스 분리 후 쓰는 곳이 Router 의 옛 보스 처리뿐. S3 후 소비자 0 |
| 로켓·유탄 폭발음 | `WeaponTable.ExplodeSfxKey` **+** 코드 기본값/프리팹 `sfxKey` | **기준값 하나로 (S10)** | "같은 의미의 값이 둘 이상이면 기준을 명시" |
| 런타임 상태 (루프 핸들, 다음 울음 시각, 기절 종료 시각) | 컴포넌트 필드 | 유지 | |

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
- `MonsterController`: `MonsterDiedEvent` 를 발행하는 바로 앞에서 `Died?.Invoke()` 1줄 (+ 선언)
- `MonsterSoundEmitter`: `Died` → `Silence()` + `Def.DeathSfx` (사망 위치)
- Router: `MonsterDiedEvent` 구독과 `OnMonsterDied` 삭제. `IsBoss` 분기도 사라짐
- 다른 파트: 몬스터 2줄

### S2. 보스 사운드 (R11 구현) — `IBossSoundSource` + `BossSoundEmitter`
```csharp
// Core/Contracts/BossContracts.cs (신규)
public enum BossPattern { Stomp, Barrage, Electric, Inhale }   // Inhale = develop 흡입 패턴 (신규)

public interface IBossSoundSource
{
    event Action Appeared;                                    // 등장 연출 시작 (확정)
    event Action<BossPattern, SkillPhase> PatternPhaseChanged; // Windup·Active·Recover·Idle (확정)
    event Action<Vector3> BarrageShotFired;                   // 포격 1발 발사 (확정)
    event Action<int> AntennaOpened;                          // 개방 안테나 수 4/6 (확정)
    event Action<BossPattern, Vector3> PatternImpact;         // 신규 제안: 짓밟기 착지·포격 착탄·전격 명중 지점
    event Action<Vector3> Stepped;                            // 신규 제안: 발 디딤 (카메라 흔들림과 같은 순간)
    event Action Died;                                        // 이미 있음
}
```
- **인터페이스로 두는 이유**: 결정된 "보스 이벤트를 발행해 주는 테스터"가 지금 바로 두 번째 구현자다. `BossController` 의 C# 이벤트는 외부에서 발행할 수 없어서, 테스터가 가짜 소스(`BossSoundTestSource : IBossSoundSource`)를 구현하는 쪽이 가장 단순하다. `IMonsterSoundSource` 와 같은 모양.
- `BossSoundEmitter` (보스 프리팹, 3D): 등장 포효 + 몬스터 덕킹, 패턴 Windup 예고음, 흡입 Active~Recover 동안 입 위치 루프, 착지·착탄음, 안테나 개방 포효, 발소리, 사망음
- Router `.Boss`: 경고(경고음·덕킹), 등장(보스 BGM), 처치(BGM 정지·클리어·아나운서)만 남김. `boss`·`bossInstance` 필드, `BossPosition()` 삭제
- `BossController` 쪽 작업(보스 담당과 함께): 인터페이스 구현 선언 + 패턴 구현 시 발행. 지금은 `Appeared`·`PatternPhaseChanged` 등 발행 지점이 없으므로 **선언만 먼저 넣고 발행은 패턴 구현 때** 넣는다

### S3. 쓰지 않는 보스 EventBus 이벤트 삭제
`BossSkillPhaseChangedEvent`, `BossDamageStageChangedEvent` — 실제 발행자·구독자 없음 (Router·SoundTester 만). `GameEvents.cs`·`02-SystemInterfaces.md` 표 갱신.
`BossWarning/Appeared/Defeated` 의 `MonsterDef` → `BossDef` 교체는 보상·카메라·`RoundDef.Boss` 까지 걸린 **코어 결정**이라 이번 범위에서 제외 (S1·S3 후 사운드는 페이로드를 읽지 않음).

### S4. 보스 사운드 키 위치 — 테이블 → 이미터 Inspector (재검토)
| 안 | 내용 | 비용 |
|---|---|---|
| **b. `BossSoundEmitter` 직렬화 필드 (권장)** | 보스 프리팹에서 키 지정. 보스가 늘면 그때 테이블로 옮김 | 없음. 단 키 오타가 로드 시 검증되지 않음 → 이미터 `Awake` 에서 `DataManager.Sounds` 로 확인해 경고 |
| a. `BossTable`+`ClypeosaurusTable` 열 (기존 결정) | R6 규칙과 같음 | DTO·Def 10필드 + `LinkSounds` + 보스 담당 테이블 수정 (M7 같은 충돌 위험) |

기준 문서 3장 2번 질문("누가 어떤 튜닝·종류 추가를 실제로 쉽게 하는가")에 테이블안이 답하지 못한다: 보스 1종, 패턴 테이블 1행.

### S5. 보스 피격 피드백
`BossController.TakeDamage` 에 `DamageDealtEvent` 발행 1줄 (`MonsterController` 와 같은 형태, 처치면 `IsKill = true`). 보스 파일.

### S6. 플레이어 피격 목소리를 `PlayerSoundEmitter` 로
- `PlayerDamagedEvent` 구독을 Router → `PlayerSoundEmitter` 로 이동 (Router 의 `playerHitKey`·`playerStatusHitKey` 도 이동)
- 같은 프레임에 사망했으면 피격 목소리를 생략 (`player.IsAlive == false` 확인) → 피격+사망 목소리 겹침 해결
- 다른 파트 수정 없음

### S7. 보스 이미터 부착 방식 — 자동 → 프리팹 (재검토)
R1 결정은 "보스 이미터도 자동 부착"이었다. 보스는 `BossAppearedEvent` 를 받을 때 붙이게 되는데, 같은 순간 보스가 지역 `Appeared` 를 먼저 발행하면 이미터가 놓친다 → **구독자 순서 의존**(기준 문서가 피하라고 한 것). 보스 프리팹은 1개이므로 `BossSoundEmitter` 를 프리팹에 직접 붙인다 (보스 프리팹 수정 1회).

### S8. 테스터
- `SoundTester`: 보스 1~6·S 버튼(옛 이벤트) 삭제, 경고·등장 버튼은 페이로드 없이 유지
- `BossSoundTestSource` (Audio/Tester): `IBossSoundSource` 가짜 구현, 키로 각 알림 발행 → 테스트 씬에서 보스 소리 확인 (M5 해결)

### S9. `MonsterTable.HurtSfxKey` 열 삭제
TSV 열, `MonsterDto`/`MonsterDef.HurtSfx(Key)`, `LinkSounds` 1줄, 헤더 주석. S3 이후 사용처 0.

### S10. 폭발음 기준값 하나로
`RocketLauncher`·`GrenadeLauncher` 코드 기본값 `sfxKey = "sfx_grenade_explode"` 제거 → `WeaponTable.ExplodeSfxKey` 만 기준. MS 프리팹에 저장된 값은 테이블 값이 항상 덮으므로 동작 변화 없음 (프리팹은 건드리지 않음).

### S11. 문서
`SoundSystemGuide.md`(팀 공유), `02-SystemInterfaces.md` 이벤트 표, `GameEvents.cs` 주석, 이 문서 결과 반영.

---

## 6. 보완 사항 재점검 — 확인 필요

| # | 질문 | 권장 | 영향 |
|---|---|---|---|
| Q1 | S1 몬스터 사망을 지역 이벤트(`IMonsterSoundSource.Died`)로 옮길까? | 예 | 몬스터 파일 2줄 |
| Q2 | S2 보스 계약에 `PatternImpact`·`Stepped` 를 추가할까? (보스 담당 확정 목록 밖) | 추가 — 단, 보스 담당 확인 | 계약만, 발행은 패턴 구현 때 |
| Q3 | S3 `BossSkillPhaseChanged`·`BossDamageStageChanged` 이벤트를 Core 에서 삭제할까? | 삭제 | Core `GameEvents.cs` |
| Q4 | S4 보스 사운드 키를 테이블(기존 결정) 대신 이미터 Inspector 로? | Inspector | 기존 R11 ④-a 결정 변경 |
| Q5 | S6 플레이어 피격 목소리를 `PlayerSoundEmitter` 로 옮길까? | 예 | Audio 만 |
| Q6 | S7 보스 이미터를 자동 부착 대신 보스 프리팹에 직접? | 프리팹 | 기존 R1 결정 중 보스 부분 변경, 보스 프리팹 1회 수정 |
| Q7 | S9 `MonsterTable.HurtSfxKey` 열 삭제? | 삭제 | 데이터 열 1개 |
| Q8 | S10 폭발음 코드 기본값 제거? | 제거 | 무기 2파일 기본값만 |

### 범위 밖 — 담당 파트 전달
- **코어**: `GameOverEvent`·`RoundStartedEvent`·`GameStateChangedEvent`·`BossWarning/Appeared/Defeated` 의 **실게임 발행자 없음** (RoundDirector 미구현). 사운드는 이 이벤트를 그대로 구독한다. 보스 이벤트 `BossDef` 교체 여부
- **보스**: S2 계약 구현·발행, S5 `DamageDealtEvent`, S7 프리팹 부착
- **몬스터**: S1 `Died` 발행, 공중 몬스터 추락 착지 알림이 필요하면 추가 (보류)
- **에셋 요청**: 3-1 7개, 보스 흡입 루프·전격·착탄·발소리·사망, 플레이어 부활, 도약 착지

### 작업 순서 (확인 후)
1. Audio 단독: S6, S8(SoundTester 정리), S10
2. 계약: S1 `Died`, S2 `IBossSoundSource`·`BossPattern`, S3 이벤트 삭제
3. 연결: S1 `MonsterController`, S2 `BossSoundEmitter` + Router `.Boss` 정리 + 테스트 소스, S5, S7
4. 데이터: S9 (S4 를 a 로 하면 보스 테이블 열도 여기)
5. 문서: S11 → 테스트 씬 확인
