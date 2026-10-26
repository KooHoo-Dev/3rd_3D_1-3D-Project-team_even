# 사운드 시스템 리팩토링 설계 (v2 — 팀 결정용)

> 브랜치: `feat/SoundSystem` · 기준: develop `b097ea3f`(PR #27 돌진형 몬스터) 머지 `2dbcc206` + 사운드 에셋 커밋 `53277ae7`, `fab5592b`
> v1 대비 바뀐 점: 새로 들어온 사운드(플레이어·무기·UI·아나운서·독가스)를 반영하고, 코드를 다시 확인해 **팀원 코드 수정이 필요 없어진 항목**을 정리했다. R1~R10 은 항목마다 **선택지 + 권장안 + 결정할 것**으로 다시 썼다.
> v2.1: PR #27(중간체 돌진·기절) 반영 → **2-1장**, R1(기절 알림 추가), R6(돌진 소리), 에셋 요청 목록 보완.

---

## 📋 회의용 요약 — 결정할 것

| # | 항목 | 권장안 | 다른 파트 수정 | 결정할 것 |
|---|---|---|---|---|
| R1 | 몬스터 공격음 → 예고(Windup) 이벤트 + **기절 알림** | **A** 코드 이벤트 | 몬스터: `MonsterController` 에 인터페이스 구현 (추가만) | 구현 방식 A/B, 이미터 부착 방식 |
| R2 | 몬스터 Def 출처 단일화 | R1 과 함께 | 없음 (R1 에 포함) | — (R1 따라감) |
| R3 | 사망 시 울음 정지 | 이벤트 기반 | 없음 | — |
| R4 | Router 분할 + 새 이벤트 소리 | partial 분할 | 없음 | **아나운서를 어디에 쓸지** |
| R5 | 플레이어 소리 (`PlayerSoundEmitter`) | 새 컴포넌트 | **없음** (필요한 이벤트가 이미 있음) | 바닥 재질 판별, 부착 방식 |
| R6 | 엔티티 소리 → 테이블 컬럼 | 무기·보급·독가스만 | 무기: `Gun` 1줄 / 보급: `SupplyDropper` 1줄 | 독가스·폭발음 키를 둘 위치 |
| R7 | `ISoundService` 확장 | 볼륨·덕킹·루프(키) 추가 | 코어: 계약 추가 | — |
| R8 | AudioMixer 도입 | 지금 도입 | 없음 (에디터 작업 5분) | 도입 시기 |
| R9 | `SoundService` 내부 정리 | 가벼운 정리만 | 없음 | 테스트용 asmdef 분리 여부 |
| R10 | 독장판 틱 피격음 | **A** `DamageKind.Status` | 코어: enum 값 추가 / 몬스터: `PoisonZone` 1줄 | 방식 A/B/C |

---

## 1. 사운드 에셋 현황

### 1-1. 새로 들어온 것

| 분류 | 에셋 | SoundTable 키 | 재생 코드 |
|---|---|---|---|
| 무기 발사 | 돌격소총 8, 권총 8, 샷건(SF) 2, 샷건(실사) 2, 로켓 2 | `sfx_{총기}_fire` 8종 | ✅ `Gun` |
| 무기 장전·빈 탄창 | 라이플·권총·반자동·샷건·SF 장전 17 | `sfx_{총기}_reload` 6, `sfx_*_dry_fire` 4 | 장전 ✅ / 빈 탄창 ❌ |
| 폭발 | 수류탄 1, 대형 2, 소형 2 | `sfx_grenade_explode`, `sfx_explosion_large/small` | 수류탄 ✅ / 대·소형 ❌ |
| 플레이어 | 발소리 흙(달리기 12·착지 10)·금속 16, 목소리(피격·사망·점프·기침·숨) 10, 투척 5 | `sfx_player_*` 9, `sfx_grenade_throw` | 피격만 ✅ |
| UI | 메뉴 커서·선택, 강화 선택, 체력 경고, 게임오버, 스테이지 클리어 | `ui_*` 6 | ❌ |
| 아나운서 | Fight·카운트·연속 처치·헤드샷·승리·패배 등 18 | `ann_*` 18 | ❌ |
| **독가스 (미커밋)** | `Monster/Attack SFX/PoisonGas_Escaped`(0.78초), `PoisonGas_inside`(3.05초) | 없음 → **추가 필요** | ❌ |
| **보스 포효 (미커밋)** | `Clypeosaurus_Shout_2`(3.76초), `Clypeosaurus_Shout`·`GiantSlug_Attack` 교체 | `mon_clypeosaurus_shout` 에 변형으로 추가 가능 | 보스 미구현 |

> 미커밋 에셋 정리 제안: `Monster/Attack SFX/` → `Monster/Poison/` 로 옮기고 `Poison_Release`, `Poison_ZoneLoop` 로 이름 변경 (다른 폴더와 같은 `폴더명_동작` 규칙, 공백 없는 폴더명).

### 1-2. 아직 없는 것 (에셋 요청 목록)

| 우선 | 필요한 소리 | 현재 상태 |
|---|---|---|
| 🔴 | BGM 군중·보스 | 키만 있음, 무음 |
| 🔴 | 총알 타격음(`sfx_hit_flesh`), 히트마커, 처치 확인음 | 키만 있음, 무음 (피드백 핵심) |
| 🔴 | 보스 경고음 | 키만 있음, 무음 |
| 🟠 | SMG·저격·유탄 전용 발사음 | 다른 총 소리 빌려 씀 |
| 🟠 | 보급 낙하·획득 | 낙하 키만 있음, 무음 |
| 🟠 | 돌진 벽 충돌음 (레탈로브라크가 벽에 박히는 순간) | 없음 — 임시로 `sfx_explosion_small` 대여 가능 |
| 🟢 | 돌진 달리기 루프 (쿵쿵 발소리) | 없음 — 없어도 예고 포효로 신호는 충분 |
| 🟢 | 무기 교체, 회복 사용, 구르기, 스태미나 고갈 숨소리(현재 `BreathLoop` 는 조깅 숨) | 없음 |

---

## 2. 최근 머지된 변경점 (사운드 영향만)

| 영역 | 바뀐 점 | 사운드 영향 |
|---|---|---|
| 몬스터 행동 | `MonsterController` + 상태 머신. 공격이 **Windup → Active → Recover → Idle** 로 진행되고 `MonsterAttack.PhaseChanged` 로 알린다. `Def` 는 프리팹 `monsterId` 로 직접 읽는다 | 공격음을 애니메이션 이벤트 없이 **예고 시점**에 낼 수 있다 |
| | `DamageDealtEvent`, `MonsterDiedEvent(Instance 포함)` 실제 발행 시작 | 타격음·히트마커·사망음이 실게임에서 동작 가능 |
| 독안개 몬스터 | `PoisonProjectile` → `PoisonZone`(지속 피해), 풀링 | 소리 없음 + 장판 틱마다 피격음 반복 |
| 플레이어 | `PlayerController` partial 분할. **`Jumped`, `Landed`, `RollStarted`, `Died`, `ViewModeChanged` C# 이벤트**, `IsSprinting`, `HorizontalSpeed`, `IsOnGround`. 보스 등장 시 3인칭 | 플레이어 소리를 **플레이어 코드 수정 없이** 붙일 수 있다 |
| 아이템 | `PlayerItems.ThrowStarted / HealStarted / HealEnded` | 투척·회복 소리 연결 지점 |
| 무기 | 총기 8종, **`GunSlot.GunChanged` 이벤트**, 탄이 0 이면 `TryFire` 가 자동 장전 | 교체음은 무기 코드 수정 없이, 빈 탄창음은 `Gun` 1줄 필요 |
| 보급·보상 | `SupplyCollectedEvent`, `UpgradePurchasedEvent`, `RewardSelectionFinishedEvent` | 획득·구매 소리 연결 지점 |
| 보스 | `Scripts/Boss/` 비어 있음. 보스 이벤트는 테스터만 발행 (PR #27 이후에도 동일) | 보스 사운드는 실게임 미검증 |

### 2-1. 최신 머지 — PR #27 돌진형 몬스터 (`feature/rusher-charge-attack`)

> 보스(클리페오사우루스 3001)가 아니라 **중간체**(GradeId 2) 업데이트다. 보스 코드는 아직 없다.

| 바뀐 점 | 내용 | 사운드 영향 |
|---|---|---|
| **돌진 공격** `ChargeAttack` (Rusher = 레탈로브라크 2001) | 예고 1.2초 동안 플레이어를 겨눔 → Active 에서 12m/s 로 돌진 → 접촉 1회 피해 → 감속 → 멈춤. 끝나면 Idle 에서 천천히 걸으며 정면을 다시 맞춘다 | 예고 시작 = 돌진 신호. `MonsterAttackTable 200101.SfxKey = mon_letalobrach_attack` 이 이미 있어서 **R1 만 되면 바로 "포효 → 돌진"이 된다** |
| **벽 충돌 → 기절** | 달리다 벽에 막히면 `AttackResult.WallHit` → `StateMachine.Stun(WallHitStunTime=2초)` → `StunnedState`, Animator `IsStunned` | 기절 = 벽 충돌 순간. `mon_letalobrach_groggy` 클립이 있는데 **재생 경로가 없다** (애니메이션 이벤트 `PlaySfxKey` 전제였음) |
| 이동 = 공격 | `MonsterAttack.UsesMoveAnimation`, `BodyMoveSpeed` 추가. 돌진은 Attack 클립 대신 이동 클립을 속도에 맞춰 재생 | Active 시작의 Animator `Attack` 트리거가 돌진에는 **없다** → 애니메이션 이벤트 방식(R1-B)은 돌진 몬스터에 쓸 수 없다 |
| 침투형 공격 | Infiltrator(파르다트록스 2002)가 `StrikeAttack`(근접) 사용 | 근접 공격음은 R1 로 해결. **은신(Vanish) 동작은 아직 없음** → `mon_pardathrox_vanish` 는 대기 |
| 프리팹 | `Letalobrach.prefab`, `Pardathrox.prefab` 추가 (`ChargeSettings` 포함) | 두 프리팹 모두 `MonsterSoundEmitter` **없음** → R1 의 자동 부착(a)이 필요한 이유가 하나 더 늘었다 |
| 상태 머신 | `MonsterStateMachine.StateChanged(previous, current)`, `CurrentAttack`, `IsResting` 공개 | 기절·사망·Idle 복귀를 이벤트로 받을 수 있다 (R1 계약에 기절 알림 추가) |

**이번 머지로 생긴 사운드 연결점 정리**

| 순간 | 코드 위치 | 재생할 소리 | 경로 |
|---|---|---|---|
| 돌진 예고 시작 | `MonsterAttack.PhaseChanged(Windup)` | `mon_letalobrach_attack` (포효) | R1 |
| 돌진 시작 | `PhaseChanged(Active)` | (선택) 돌진 달리기 루프 — 에셋 없음 | R6 |
| 플레이어 접촉 | `PlayerDamagedEvent` | `sfx_player_hit` | 이미 동작 |
| 벽 충돌 + 기절 | `StateMachine.Stun` → `StateChanged(Stunned)` | 벽 충돌음(에셋 없음) + `mon_letalobrach_groggy` | R1 기절 알림 |
| 기절 해제 | `StateChanged(Stunned → Idle)` | 없음 | — |

---

## 3. 현재 문제 (보완할 부분)

| 상태 | 문제 | 해결 항목 |
|---|---|---|
| 🔴 | 몬스터 울음·이동 루프·공격음이 실게임에서 안 남 (`MonsterSoundEmitter` 가 어떤 프리팹에도 없음, 애니메이션 이벤트도 없음) | R1·R2 |
| 🔴 | 사망 연출 중에도 울음·루프 계속 (애니메이션 이벤트 의존) | R3 |
| 🔴 | 돌진 몬스터 벽 충돌·기절 소리 경로 없음 (`mon_letalobrach_groggy` 미사용), 기절 중에도 울음 계속 | R1 (기절 알림) |
| 🔴 | 독장판 위에서 피격음이 틱마다 반복 | R10 |
| 🔴 | 로켓·유탄 폭발음이 수류탄 소리 (코드·프리팹에 키 하드코딩) | R6 |
| ✅ 해결 | ~~무기 8종이 같은 소리~~ → 총기별 키 연결 완료 (3종은 대여 클립) | — |
| ✅ 해결 | ~~무기·플레이어·UI 클립 없음~~ → 에셋 추가 완료 | — |
| 🟠 | 플레이어·UI·아나운서·빈 탄창 키는 있는데 재생 코드 없음 | R4·R5·R6 |
| 🟠 | 독가스 클립은 있는데 키·코드 없음 | R6 |
| 🟠 | 믹서 없음 → 옵션 볼륨·보상 화면 효과 불가 | R7·R8 |
| 🟠 | 3인칭에서 리스너(카메라)가 플레이어 뒤로 빠짐 | R5(플레이어 소리 2D), R9 |
| 🟠 | 사운드 로직 테스트 없음 | R9 |

---

## 4. R1 ~ R10 상세

각 항목: **문제 → 선택지 → 권장 → 수정 범위 → 결정할 것**

### R1. 몬스터 공격음을 예고(Windup) 시점으로 ★핵심
**문제** 공격음이 애니메이션 이벤트(`PlayAttackSfx`)에 의존하는데 연결된 곳이 없다. `MonsterAttackDef.Sfx` 는 원래 "예고 시작에 재생"하도록 정의돼 있고, 새 몬스터 코드가 그 시점을 `PhaseChanged` 로 알려준다.

| 선택지 | 내용 | 장점 | 단점 |
|---|---|---|---|
| **A. 코드 이벤트 (권장)** | Core 에 `IMonsterSoundSource` 추가 → `MonsterController` 가 구현 → 이미터가 Windup 에 재생 | 데이터와 시점이 정확히 맞음. 애니메이션 작업 불필요. 돌진처럼 Attack 클립이 없는 공격도 동작. 보스도 같은 구조면 그대로 동작 | 몬스터 담당 파일에 15줄 내외 추가 |
| B. 애니메이션 이벤트 | 몬스터 클립마다 `PlayAttackSfx` 이벤트 삽입 | 몬스터 코드 수정 없음 | 클립·몬스터마다 수작업. Windup 이 아니라 Active 애니메이션 기준이라 "예고음" 역할 못함. **PR #27 돌진은 Attack 클립을 재생하지 않아 아예 불가** |

**계약 (PR #27 반영 — 기절 알림 추가)**
```csharp
public interface IMonsterSoundSource
{
    MonsterDef Def { get; }
    /// <summary>attack = 테이블 공격 Def (AttackSpec.SourceDefId 로 조회, 모르면 null)</summary>
    event Action<MonsterAttackDef, SkillPhase> AttackPhaseChanged;
    /// <summary>기절 시작. 지금은 돌진 벽 충돌(WallHit)에서만 발생. seconds = 기절 시간</summary>
    event Action<float> Stunned;
}
```
- `MonsterController` 쪽 구현: 이미 구독 중인 `OnAttackPhaseChanged` 에서 `AttackPhaseChanged` 발행, 이미 있는 `OnStateChanged` 의 `Stunned` 분기에서 `Stunned` 발행 → **기존 로직 변경 없이 발행 2줄 + 선언**.
- 이미터: `Stunned` → 울음·루프 일시 정지 + 벽 충돌음 + 몬스터별 기절음 재생. 기절음 키는 `MonsterTable.StunSfxKey` 컬럼(신규, 레탈로브라크 = `mon_letalobrach_groggy`)으로 둔다 (R6 과 같은 패턴).
- 같은 이유로 `PlaySfxKey("mon_letalobrach_groggy")` 애니메이션 이벤트 안은 철회.

**이미터 부착 방식** (A·B 공통)

| 선택지 | 내용 |
|---|---|
| **a. 자동 부착 (권장)** | `GameSoundRouter` 가 `MonsterSpawnedEvent` 에서 이미터가 없으면 `AddComponent`. 씬에 직접 둔 몬스터는 시작 시 한 번 검색 |
| b. 프리팹 직접 부착 | 몬스터 프리팹마다 컴포넌트 추가 (몬스터 담당 프리팹 수정) |

- 보스: 보스도 `MonsterAttack` 을 쓰면 공격음은 이미터로 통일하고, Router 의 `BossSkillPhaseChanged` 처리는 **등장 공격 덕킹만** 남긴다 (소리 하나에 경로 하나).
- **수정 범위** Core 계약 1개 추가, `MonsterController` 구현 추가, `MonsterTable.StunSfxKey` 컬럼, `MonsterSoundEmitter` 수정(`PlayAttackSfx` 제거, `PlaySfxKey` 는 은신(Vanish)처럼 상태 머신 밖 동작용으로만 유지)
- **결정할 것** ① A / B ② 부착 a / b ③ 보스가 `MonsterAttack` 을 재사용하는지 (보스 담당) ④ 기절음 키를 `MonsterTable` 컬럼으로 둘지

### R2. 몬스터 Def 출처 단일화
**문제** `MonsterController` 는 `monsterId` 로 Def 를 읽고, 이미터는 `MonsterSpawnedEvent` 또는 자기 `monsterId` 로 따로 받는다. 종류 전환(날개 파괴 1003 → 1002) 시 두 곳을 맞춰야 한다.
**권장** R1-A 를 하면 이미터가 `IMonsterSoundSource.Def` 를 우선 사용 → 자동 해결. 소스가 없는 프리팹만 기존 방식으로 대체.
**결정할 것** 없음 (R1 결정을 따라감)

### R3. 사망 시 울음·루프 정지
**문제** `Silence()` 가 애니메이션 이벤트에서만 호출된다.
**권장** Router 가 `MonsterDiedEvent.Instance` 의 이미터에 `Silence()` 호출. 애니메이션 이벤트는 선택 사항으로 강등.
**수정 범위** Audio 폴더만 · **결정할 것** 없음

### R4. `GameSoundRouter` 분할 + 새 이벤트 소리
**문제** 플레이어·보급·UI·아나운서가 붙으면 한 파일이 비대해진다.
**권장** develop 의 `PlayerController.*.cs` 방식대로 partial 분할

```
GameSoundRouter.cs          필드·구독/해제
GameSoundRouter.Round.cs    BGM, 라운드 시작, 게임오버
GameSoundRouter.Combat.cs   타격·처치·사망·플레이어 피격(R10)
GameSoundRouter.Boss.cs     경고·등장·페이즈·처치·덕킹
GameSoundRouter.Items.cs    보급 획득, 강화 구매, 보상
GameSoundRouter.Ui.cs       저체력 경고, 아나운서
```

**새로 연결할 이벤트 (에셋 있음)**

| 이벤트 | 소리 |
|---|---|
| `GameOverEvent` | `ui_game_over` (+ 아나운서?) |
| `UpgradePurchasedEvent` | `ui_upgrade_select` |
| `PlayerHpChangedEvent` (≤ `Config.LowHpRatio`) | `ui_health_warning` 주기 반복 (BL-007) |
| `BossDefeatedEvent` | `ui_stage_clear` (+ 아나운서?) |
| `RoundStartedEvent` | 아나운서? |

**아나운서 사용 선택지** — 에셋 18개가 있지만 게임 톤과 맞는지 팀 판단 필요

| 선택지 | 쓰는 곳 |
|---|---|
| A. 최소 | 라운드 시작 `ann_fight`, 보스 처치 `ann_victory`, 게임오버 `ann_game_over` |
| B. 연속 처치 포함 | A + 짧은 시간 내 처치 수로 `double_kill`→`triple_kill`→`killing_spree`→`rampage`… (처치 카운터 추가) |
| C. 사용 안 함 | UI 효과음만 |

**결정할 것** ① 아나운서 A / B / C ② 헤드샷 판정 필요 여부(`ann_headshot` — 약점 부위 정보는 `MonsterPartDef` 쪽, 몬스터 담당 확인)

### R5. 플레이어 전용 `PlayerSoundEmitter` 신설
**v1 대비 변경** 필요한 이벤트가 `PlayerController` 에 **이미 있어서 플레이어 코드 수정이 필요 없다.**

| 소리 | 연결 지점 | 키 |
|---|---|---|
| 발소리 | 이동 거리 누적(보폭 m 마다 1회), `IsOnGround`·`IsSprinting`·`HorizontalSpeed` | `sfx_player_footstep(_metal)` |
| 점프 | `Jumped` | `sfx_player_jump` |
| 착지 | `Landed` | `sfx_player_land` |
| 사망 | `Died` | `sfx_player_death` |
| 숨소리 | `PlayerStaminaChangedEvent` (낮으면 루프 시작) | `sfx_player_breath_loop` |
| 투척 | `PlayerItems.ThrowStarted` | `sfx_grenade_throw` |
| 회복 | `PlayerItems.HealStarted` | (에셋 없음) |
| 구르기 | `RollStarted` | (에셋 없음 — 착지음 대체 가능) |

- 플레이어 자신의 소리는 전부 2D → 3인칭 리스너 위치 문제를 피한다.
- 발소리를 애니메이션 이벤트가 아닌 거리 기준으로 하는 이유: 1인칭에서 몸 애니메이션과 무관하게 일정하고, 애니메이터 수정이 없다.
- 키는 **이미터의 직렬화 필드**로 둔다 (v1 의 "PlayerTable 컬럼"안은 철회 — 플레이어는 1행뿐이라 테이블 컬럼의 이점이 없음).

**바닥 재질 판별 선택지** (흙 / 금속 발소리)

| 선택지 | 내용 |
|---|---|
| **A. 흙만 (권장, 1차)** | 금속은 맵이 정해진 뒤 추가 |
| B. 레이어/태그 | 발밑 레이캐스트 → 지정 레이어·태그면 금속 (전장 담당과 규칙 합의) |
| C. PhysicMaterial 이름 | 콜라이더의 물리 재질 이름으로 판별 |

**부착** R1 과 같은 자동 부착(플레이어 찾으면 `AddComponent`) 또는 `Player.prefab` 에 직접 부착
**결정할 것** ① 바닥 판별 A / B / C ② 부착 자동 / 프리팹

### R6. 엔티티 소리는 테이블 컬럼으로
기존 `WeaponTable.FireSfxKey` → `DataManager.LinkSounds` 패턴을 확장한다.

| 대상 | 추가 컬럼 | 코드 연결 | 다른 파트 수정 |
|---|---|---|---|
| 무기 교체 | `WeaponTable.SwitchSfxKey` | `GunSlot.GunChanged` 구독 (Audio 쪽) | 없음 |
| 빈 탄창 | `WeaponTable.DryFireSfxKey` | `Gun.TryFire` 의 `Ammo <= 0` 분기에서 재생 → 이어서 자동 장전음 | 무기 1줄 |
| 로켓·유탄 폭발 | `WeaponTable.ExplodeSfxKey` | `RocketLauncher`·`GrenadeLauncher` 의 하드코딩 `sfx_grenade_explode` 제거, 프리팹 값도 정리 | 무기 2파일 + 프리팹 |
| 보급 | `SupplyTable.DropSfxKey`, `CollectSfxKey` | `SupplyDropper` 하드코딩 제거, 획득은 Router | 보급 1줄 |
| 독가스 | 아래 선택지 | `PoisonProjectile` 착탄 / `PoisonZone` 루프 | 몬스터 (선택지에 따라) |
| 몬스터 기절 (PR #27) | `MonsterTable.StunSfxKey` | R1 의 `Stunned` 알림 | 없음 (R1 에 포함) |
| 돌진 (PR #27) | `MonsterAttackTable.ActiveLoopSfxKey`(달리기 루프), `ImpactSfxKey`(벽 충돌) | R1 의 `AttackPhaseChanged(Active)` 에서 루프 시작, Recover/Idle 에서 정지 / `Stunned` 에서 충돌음 | 없음 (R1 에 포함) — 에셋이 오면 추가 |

**독가스 키 위치 선택지** (클립: `PoisonGas_Escaped` 0.78초 = 터짐, `PoisonGas_inside` 3.05초 = 장판 루프)

| 선택지 | 내용 | 다른 파트 수정 |
|---|---|---|
| **A. 테이블 (권장)** | `MonsterAttackTable.ImpactSfxKey`(착탄), `StatusEffectTable.LoopSfxKey`(장판 루프) | 몬스터: 착탄·장판 시작 시 재생 호출 2~3줄 |
| B. 컴포넌트 필드 | `PoisonAreaSettings` 에 키 필드 추가 | 몬스터: 같은 2~3줄 + 프리팹 값 입력 |

장판 루프는 `PlayLoop(def, zone.transform)` — 장판이 풀로 돌아가 꺼지면 자동으로 멈추고, 장판이 여러 개면 가까운 3개만 들린다 (현재 `SoundService` 기능 그대로).

**결정할 것** ① 독가스 키 A / B ② 로켓·유탄 폭발음을 테이블로 옮길지 (무기 담당)

### R7. `ISoundService` 확장 + 중복 조회 제거
```csharp
float GetVolume(SoundCategory category);
void SetVolume(SoundCategory category, float volume);   // 옵션 화면
void Duck(SoundCategory category, float level, float holdTime);
SoundHandle PlayLoop(string key, Transform follow);     // 키만 아는 쪽 (독가스 장판 등)
```
- `NullSoundService` 도 같이 갱신. 기존 호출 코드는 영향 없음 (메서드 추가만).
- `Gun.PlaySfx` 가 `SoundDef` 를 받아 `sound.Key` 로 다시 조회하는 것을 `PlaySfx(sound, position)` 로 수정 (무기 1줄, 동작 동일).
- **결정할 것** 없음 (코어 담당 확인만)

### R8. AudioMixer 도입
**v1 대비 확인된 점** 믹서 에셋은 스크립트로 만들 수 없다 (Unity 공개 API 없음) → **에디터에서 직접 생성**해야 한다 (5분).
- 구성: Master / Bgm / Sfx / Monster / Ui 그룹 + 그룹별 볼륨 Exposed 파라미터(dB)
- 스냅샷: `Default`, `Reward`(강화 화면 — Sfx·Monster 로우패스), `BossIntro`(Monster 감쇠)
- 코드: 믹서가 연결되면 `SetVolume` 이 Exposed 파라미터를 바꾸고, 없으면 지금처럼 코드 볼륨 곱셈 (현재의 "비워도 동작" 원칙 유지)

| 선택지 | 내용 |
|---|---|
| **A. 지금 도입 (권장)** | 옵션 화면이 생기기 전에 구조를 맞춰 둠 |
| B. 옵션 UI 작업 때 | 그때까지 코드 볼륨만 사용 |

**결정할 것** 도입 시기 A / B

### R9. `SoundService` 내부 정리
**v1 대비 확인된 점** 사운드 로직을 EditMode 테스트하려면 Audio 폴더에 asmdef 가 필요한데, `PlayerSoundEmitter`·Router 가 `PlayerController`(asmdef 없음)를 참조하면 Audio 를 asmdef 로 분리할 수 없다.

| 선택지 | 내용 | 비용 |
|---|---|---|
| **A. 가벼운 정리 (권장)** | 카테고리 배열 크기를 enum 길이로, 카메라 전환(`CameraModeChangedEvent`) 시 리스너 다시 찾기, BGM 처리를 `BgmPlayer` 로 분리 | 작음 |
| B. 테스트 가능 구조 | A + `VoiceAllocator` 순수 클래스 분리 + Audio asmdef + EditMode 테스트. 플레이어 의존 코드는 `Audio/Player/` 등 asmdef 밖으로 분리 | 큼 |

**결정할 것** A / B

### R10. 독장판 틱 피격음
**v1 대비 변경** 기침 클립(`Voice_Cough`)이 들어와서 "독 피해 = 기침" 으로 구분할 수 있게 됐다.

| 선택지 | 내용 | 다른 파트 수정 | 정확도 |
|---|---|---|---|
| **A. `DamageKind.Status` (권장)** | Core 의 `DamageKind` 에 지속 피해 값 추가, `PoisonZone` 이 그 값을 사용. Router: Status → `sfx_player_cough`(쿨다운 1.5초), 그 외 → `sfx_player_hit` | 코어 enum 1개 / 몬스터 1줄 | 정확 |
| B. Router 추정 | 같은 피해량이 일정 간격으로 반복되면 틱으로 간주 | 없음 | 낮음 (연타 공격 오판 가능) |
| C. 보류 | 쿨다운만 늘림 (`sfx_player_hit` 0.15 → 0.5초) | 없음 | 문제 남음 |

**결정할 것** A / B / C (코어·몬스터 담당 합의)

---

## 5. 작업 순서 (결정 후)

| 단계 | 내용 | 다른 파트 |
|---|---|---|
| 1. Audio 단독 | R3, R4 분할 + 새 이벤트 소리, R5, R9-A, 독가스 에셋 이름 정리 + SoundTable 키 추가 | 없음 |
| 2. 계약 | R7 (`ISoundService` 확장), R1 계약, R10 enum | 코어 |
| 3. 연결 | R1 (`MonsterController` 구현), R6 (무기·보급·독가스), R10 (`PoisonZone`) | 몬스터·무기·보급 |
| 4. 에디터 | R8 믹서 생성·연결, `SoundSystemTest` 씬·`SoundTester` 에 새 이벤트 추가 | 없음 |

1단계는 결정 없이도 바로 진행 가능 (R4 의 아나운서 부분만 결정 대기).

## 6. 담당자 확인 체크리스트
- [ ] **몬스터**: R1 `IMonsterSoundSource` 구현 추가 허용 (공격 단계 + 기절 알림) / R6 독가스 착탄·장판 재생 호출 / R10 `PoisonZone` 의 DamageKind 변경 / 헤드샷 판정 가능 여부 / 침투형 은신(Vanish) 구현 일정 / 기절이 벽 충돌 외에도 생길 예정인지
- [ ] **무기**: R6 빈 탄창 1줄, 로켓·유탄 폭발음 테이블 이전 / R7 `Gun.PlaySfx` 수정
- [ ] **보급**: R6 `SupplyDropper` 키 하드코딩 제거
- [ ] **코어**: R7 계약 확장 / R10 `DamageKind.Status` / R1 계약 위치
- [ ] **보스**: 보스가 `MonsterAttack` 재사용 여부, `BossSkillPhaseChangedEvent` 발행 위치
- [ ] **전장**: R5-B 를 고르면 금속 바닥 레이어·태그 규칙
- [ ] **팀 전체**: R4 아나운서 사용 범위, R8 도입 시기, R9 테스트 범위
