# 사운드 시스템 리팩토링 설계 (v2 — 팀 결정용)

> 브랜치: `feat/SoundSystem` · 기준: develop `b097ea3f`(PR #27 돌진형 몬스터) 머지 `2dbcc206` + 사운드 에셋 커밋 `53277ae7`, `fab5592b`
> v1 대비 바뀐 점: 새로 들어온 사운드(플레이어·무기·UI·아나운서·독가스)를 반영하고, 코드를 다시 확인해 **팀원 코드 수정이 필요 없어진 항목**을 정리했다. R1~R10 은 항목마다 **선택지 + 권장안 + 결정할 것**으로 다시 썼다.
> v2.1: PR #27(중간체 돌진·기절) 반영 → **2-1장**, R1(기절 알림 추가), R6(돌진 소리), 에셋 요청 목록 보완.
> v2.2: PR #29(보스 클리페오사우루스) 머지 `72e7dbde` 반영 → **0장 머지 점검**, **2-2장**, 새 항목 **R11 보스 사운드 경로 재설계**.

---

## ⚠️ 0. 머지 점검 결과 (`72e7dbde` 기준) — 회의 전 먼저 처리

| # | 문제 | 원인 | 영향 | 조치 |
|---|---|---|---|---|
| M1 | **몬스터 사운드 키가 전부 비었다.** `MonsterTable` 의 Idle/Loop/Hurt/DeathSfxKey 4열, `MonsterAttackTable` 의 SfxKey 열이 헤더만 남고 값이 없다 | 사운드 키 열은 `feat/SoundSystem` 에만 있었다. 머지 충돌 해결 때 **헤더는 이 브랜치, 행은 develop 쪽**을 받아서 값이 빠졌다 | 일반·중간체 몬스터의 울음·루프·사망·공격음이 데이터에서 끊김 (`DataManager.LinkSounds` 가 빈 키를 "소리 없음"으로 처리 → 경고도 안 뜸) | ✅ **복구 완료** — develop 수치(1~15열 / 1~24열)는 그대로 두고 사운드 키 값만 `2dbcc206` 시점 값으로 되살림 (일반·중간체 7행 + 주석 보관된 보스 행). `MonsterAttackTable` 주석의 SfxKey 설명도 복원 |
| M2 | 돌진 공격(200101) 수치가 앞선 머지 `2dbcc206` 에서 옛 값(Cooldown 10, Recovery 1, Range 15)으로 되돌아갔었다 | 같은 원인 (그때는 이 브랜치 쪽 행을 받음) | 이번 머지에서 develop 값(3 / 0 / 30)으로 **다시 맞춰져 현재는 정상** | 없음. M1 복구 시 develop 수치를 기준으로 할 것 |
| M3 | `Core/Data/Dtos/BossDTO.cs.meta` 와 `BossDto.cs` 가 대소문자가 다르게 둘 다 Git 에 올라가 있다 (`BossDTO.cs` + `BossDto.cs`) | 보스 브랜치에서 파일명 대소문자만 바꿈 | Windows 에서는 파일 하나로 보이지만, .meta 와 이름이 어긋나 Unity 가 GUID 를 새로 만들 수 있고 macOS/Linux 에서는 클래스 중복 | **보스 담당**에게 전달 (`git mv` 로 한쪽 정리) |
| M4 | 보스 데이터가 `MonsterTable`/`MonsterAttackTable`/`MonsterPartTable`/`BossPhaseTable` 에서 주석 처리되고 `BossTable`·`ClypeosaurusTable` 로 분리됐다 | PR #29 설계 변경 | `mon_clypeosaurus_*` 6개 키를 쓰는 곳이 사라짐. 기존 보스 사운드 경로(Router 의 `BossSkillPhaseChanged`·`BossDamageStageChanged`·`HurtSfx`·`DeathSfx`)가 데이터상 동작 불가 | **R11** 에서 재설계 |
| M5 | `SoundTester` 의 보스 버튼(경고·등장·공격·페이즈)이 `MonsterTable` 의 보스(`IsBoss`)를 찾는데 이제 없다 | M4 | 테스트 씬에서 보스 소리 확인 불가 | R11 확정 후 테스터 갱신 |

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
| **R11** | **보스 사운드 경로 재설계 (PR #29)** | **A** `BossSoundEmitter` + 테이블 키 | 보스: `BossController` 에 이벤트 추가 / 코어: 보스 이벤트 페이로드 | 이벤트 계약, 키 위치, 패턴별 소리 |

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
| 보스 | PR #29 로 `BossController`·`BossHealth`·`BossMovement` 추가 (→ **2-2장**). 보스 EventBus 이벤트는 여전히 테스터만 발행 | 기존 보스 사운드 경로가 데이터 분리로 끊김 → R11 |

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

### 2-2. 최신 머지 — PR #29 보스 클리페오사우루스 (`feat/boss-Clypeosaurus`)

| 바뀐 점 | 내용 | 사운드 영향 |
|---|---|---|
| **보스가 일반 몬스터 구조에서 분리** | `Scripts/Boss/BossController`(IDamageable), `BossHealth`, `BossMovement`(NavMesh 추적). 일반 몬스터의 `MonsterController`·`MonsterAttack`·상태 머신을 **쓰지 않는다** | R1 의 "보스도 `MonsterAttack` 이면 이미터로 통일" 가정이 깨짐 → 보스 전용 경로 필요 (R11) |
| **데이터 분리** | `BossTable`(Id·MaxHp·MoveSpeed) + `ClypeosaurusTable`(1행: 짓밟기·마그마 포격·안테나 전격 수치, 안테나 개방 HP 비율 70%/40%). 옛 보스 행·공격·부위·페이즈는 주석 보관 | 보스 사운드 키를 둘 곳이 없다 (`mon_clypeosaurus_*` 6개 미사용) |
| **보스 C# 이벤트** | `BossController.Damaged(DamageInfo)`, `Died` 만 있음 | 공격·패턴·안테나 개방·등장 알림이 아직 없다 → 보스 담당과 함께 추가해야 함 |
| **패턴 (데이터만, 동작 코드 아직 없음)** | 짓밟기(예고 1.8초, 반경 10m), 마그마 포격(예고 1.5초, 0.3초 간격 연사), 안테나 전격(충전 1.5초, 직선 25m), HP 70%·40% 에서 안테나 4개·6개 개방 | 각 패턴의 **예고 시작**이 플레이어 회피 신호 → 예고음이 가장 중요 |
| **애니메이션** | `Clypeosaurus_Controller`: SmashAttack(전·좌·우), SpittersAttack, RoarAntennaElectricAttack, GetHit(전·후·좌·우), Walk·Turn, IdleBreathe/LookAround, Death | 패턴 ↔ 클립: 짓밟기=SmashAttack, 포격=SpittersAttack, 전격=RoarAntennaElectric |
| **EventBus 보스 이벤트와 타입 불일치** | `BossAppearedEvent(MonsterDef)`, `BossSkillPhaseChangedEvent(MonsterAttackDef)`, `BossDefeatedEvent(MonsterDef)` 인데 새 보스는 `BossDef` | Router 의 보스 처리(BGM 전환·경고·덕킹·사망음)를 새 보스가 발행할 수 없음. 보상(`KillRewardCollector`)도 같은 문제 → **코어 결정 필요** |
| 프리팹·씬 | `Resources/Boss/Clypeosaurus/Prefab/Clypeosaurus.prefab`, `Scenes/CY/TestMap_V1` | 보스 프리팹에 사운드 컴포넌트 없음 |
| 어셈블리 | `Scripts/Boss` 는 asmdef 없음 (Assembly-CSharp) | Audio 가 `BossController` 를 **직접 참조 가능** → 보스는 Core 인터페이스 없이도 연결 가능 |

**보스 사운드 연결점 (현재 에셋 기준)**

| 순간 | 필요한 보스 쪽 알림 | 소리 (현재 키) | 비고 |
|---|---|---|---|
| 등장 | 등장 이벤트 | `mon_clypeosaurus_shout` + BGM `bgm_boss` + 군중 덕킹 | 옛 SkyFall 등장 공격은 주석 처리됨 — 등장 연출 유무 확인 필요 |
| 짓밟기 예고 | 패턴 단계(Windup) | `mon_clypeosaurus_attack` / `attack2` | 착지 충격음은 `sfx_explosion_large` 대여 가능 |
| 마그마 포격 예고·발사 | 패턴 단계 + 발사 1발마다 | `mon_clypeosaurus_skill_attack` / 착탄 `sfx_explosion_small` | 0.3초 연사 → MaxInstances·Cooldown 으로 제한 |
| 안테나 전격 충전·발사 | 패턴 단계(충전 시작, 발사) | 충전 `mon_clypeosaurus_shout`(Shout_2 변형) / 발사 (에셋 없음) | 전기 충전음·방전음 에셋 요청 |
| 안테나 개방 (HP 70%, 40%) | 개방 이벤트(개수) | `mon_clypeosaurus_hurt` + 포효 | 옛 `BossDamageStageChanged` 역할 |
| 피격 | `Damaged` (이미 있음) | 매번 재생하지 않음 (총알 타격음은 Router 의 `DamageDealt` 경로) | ※ `BossController` 는 `DamageDealtEvent` 를 발행하지 않음 → 히트마커·타격음도 안 남 |
| 걷기 | `BossMovement.IsMoving` (이미 있음) | 무거운 발소리 (에셋 없음) | 이미터가 폴링 가능 |
| 사망 | `Died` (이미 있음) | 사망음 (에셋 없음 — `mon_clypeosaurus_hurt` 대여) + BGM 정지 + `ui_stage_clear` | |

---

## 3. 현재 문제 (보완할 부분)

| 상태 | 문제 | 해결 항목 |
|---|---|---|
| 🔴 | 몬스터 울음·이동 루프·공격음이 실게임에서 안 남 (`MonsterSoundEmitter` 가 어떤 프리팹에도 없음, 애니메이션 이벤트도 없음) | R1·R2 |
| 🔴 | 사망 연출 중에도 울음·루프 계속 (애니메이션 이벤트 의존) | R3 |
| 🔴 | 돌진 몬스터 벽 충돌·기절 소리 경로 없음 (`mon_letalobrach_groggy` 미사용), 기절 중에도 울음 계속 | R1 (기절 알림) |
| 🔴 | 머지로 몬스터 사운드 키 값이 전부 빠짐 | 0장 M1 |
| 🔴 | 보스 사운드 경로 단절 (데이터 분리 + 이벤트 타입 불일치 + 보스 알림 부족) | R11 |
| 🔴 | 보스 피격 시 히트마커·타격음 없음 (`BossController` 가 `DamageDealtEvent` 미발행) | R11 |
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

- 보스: ~~보스도 `MonsterAttack` 을 쓰면 이미터로 통일~~ → PR #29 에서 보스는 `MonsterAttack` 을 쓰지 않는 별도 구조로 확정. 보스는 **R11** 로 분리한다. R1 은 일반·중간체 몬스터 전용.
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

### R11. 보스 사운드 경로 재설계 (PR #29) ★신규
**문제** 보스가 일반 몬스터 구조에서 분리되면서 ① 보스 사운드 키를 둘 데이터가 없고 ② EventBus 보스 이벤트가 옛 타입(`MonsterDef`/`MonsterAttackDef`)이라 새 보스가 발행할 수 없고 ③ `BossController` 에 패턴·등장·안테나 알림이 없다. 보스 공격 동작 코드는 아직 없으므로 **지금 계약을 정하면 보스 담당이 구현하면서 같이 넣을 수 있다.**

**① 소리를 내는 쪽 — 역할 분담**

| 선택지 | 내용 | 장점 | 단점 |
|---|---|---|---|
| **A. 전역은 Router, 보스 몸 소리는 `BossSoundEmitter` (권장)** | BGM 전환·경고·덕킹·스테이지 클리어 = EventBus 이벤트 → Router. 패턴 예고·발사·안테나·발소리·사망 = 보스 프리팹의 `BossSoundEmitter` 가 `BossController` C# 이벤트 구독 (3D, 보스 위치) | 일반 몬스터(R1 이미터)와 같은 구조. EventBus 에 패턴마다 이벤트를 늘리지 않아도 됨. Audio 가 `BossController` 를 직접 참조 가능(같은 어셈블리) | 보스 프리팹에 컴포넌트 부착 (또는 자동 부착) |
| B. 전부 EventBus | `BossSkillPhaseChangedEvent` 등을 새 타입으로 바꿔 Router 가 모두 처리 | 기존 Router 코드 재사용 | 이벤트 페이로드가 보스 패턴 구조에 묶임. 보스가 바뀔 때마다 Core 수정 |

**② `BossController` 에 필요한 알림 (보스 담당 — 패턴 구현 시 함께)**
```csharp
public enum BossPattern { Stomp, Barrage, Electric }
public event Action<BossPattern, SkillPhase> PatternPhaseChanged; // Windup(예고)·Active(발동)·Recover·Idle
public event Action<Vector3> BarrageShotFired;                    // 포격 1발 발사 (착탄 위치)
public event Action<int> AntennaOpened;                           // 개방된 안테나 수 4 / 6
public event Action Appeared;                                     // 등장 연출 시작
// 이미 있음: Damaged(DamageInfo), Died
```
- `SkillPhase` 는 기존 Core enum 재사용 (Windup/Active/Recover/Idle) → 일반 몬스터와 단계 개념이 같다.

**③ EventBus 보스 이벤트 페이로드 (코어 결정)**

| 선택지 | 내용 |
|---|---|
| **a. `BossDef` 로 교체 (권장)** | `BossWarningEvent(BossDef)`, `BossAppearedEvent(BossDef, GameObject)`, `BossDefeatedEvent(BossDef, Vector3)`. `BossSkillPhaseChangedEvent`·`BossDamageStageChangedEvent` 는 R11-A 를 고르면 **삭제**(이미터가 대신함) |
| b. 둘 다 유지 | 옛 `MonsterDef` 이벤트를 두고 새 이벤트 추가 → 중복 경로 위험 |

보상(`KillRewardCollector`)·UI 도 같은 이벤트를 쓰므로 사운드만의 결정이 아니다.

**④ 보스 사운드 키 위치**

| 선택지 | 내용 |
|---|---|
| **a. `BossTable` 공통 + `ClypeosaurusTable` 패턴 (권장)** | `BossTable`: `AppearSfxKey`, `StepSfxKey`, `HurtSfxKey`(안테나 개방), `DeathSfxKey` / `ClypeosaurusTable`: `StompSfxKey`, `StompImpactSfxKey`, `BarrageSfxKey`, `BarrageImpactSfxKey`, `ElectricChargeSfxKey`, `ElectricFireSfxKey` — 기존 "엔티티 소리는 테이블" 규칙(R6)과 같음 |
| b. `BossSoundEmitter` 직렬화 필드 | 보스가 1종이라 테이블 없이 프리팹에서 지정. 데이터 담당 작업 없음 |

**⑤ 보스 피격 피드백** `BossController.TakeDamage` 가 `DamageDealtEvent` 를 발행하지 않아 보스를 쏴도 히트마커·타격음·처치 확인음이 없다. `MonsterController` 와 같이 1줄 발행 추가 필요 (보스 담당). 보스 처치 시 `IsKill = true` 로 처치 확인음까지 해결.

**⑥ 기존 키 정리** `mon_clypeosaurus_attack/attack2/skill_attack/shout/hurt` + 미커밋 `Clypeosaurus_Shout_2` 를 패턴에 재배정 (2-2장 표). 에셋 요청: 보스 발소리, 전격 충전·방전, 마그마 착탄, 보스 사망.

- **수정 범위** 보스: `BossController` 이벤트 선언·발행, `DamageDealtEvent` 1줄 / 코어: 보스 이벤트 타입 / 데이터: 보스 테이블 사운드 열(④-a 선택 시) / Audio: `BossSoundEmitter` 신규, Router 보스 partial 수정, `SoundTester` 보스 버튼 갱신
- **결정할 것** ① A / B ② 알림 목록 확정 (보스 담당) ③ 이벤트 페이로드 a / b (코어) ④ 키 위치 a / b ⑤ 등장 연출(옛 SkyFall)을 유지하는지

---

## 5. 작업 순서 (결정 후)

| 단계 | 내용 | 다른 파트 |
|---|---|---|
| 0. 머지 복구 | 0장 M1 (몬스터 사운드 키 값 복구) | 없음 |
| 1. Audio 단독 | R3, R4 분할 + 새 이벤트 소리, R5, R9-A, 독가스 에셋 이름 정리 + SoundTable 키 추가 | 없음 |
| 2. 계약 | R7 (`ISoundService` 확장), R1 계약, R10 enum | 코어 |
| 3. 연결 | R1 (`MonsterController` 구현), R6 (무기·보급·독가스), R10 (`PoisonZone`) | 몬스터·무기·보급 |
| 3-B. 보스 | R11 — 보스 패턴 구현과 같은 일정으로 (`BossController` 알림 → `BossSoundEmitter`) | 보스·코어 |
| 4. 에디터 | R8 믹서 생성·연결, `SoundSystemTest` 씬·`SoundTester` 에 새 이벤트 추가 | 없음 |

1단계는 결정 없이도 바로 진행 가능 (R4 의 아나운서 부분만 결정 대기).

## 6. 담당자 확인 체크리스트
- [ ] **몬스터**: R1 `IMonsterSoundSource` 구현 추가 허용 (공격 단계 + 기절 알림) / R6 독가스 착탄·장판 재생 호출 / R10 `PoisonZone` 의 DamageKind 변경 / 헤드샷 판정 가능 여부 / 침투형 은신(Vanish) 구현 일정 / 기절이 벽 충돌 외에도 생길 예정인지
- [ ] **무기**: R6 빈 탄창 1줄, 로켓·유탄 폭발음 테이블 이전 / R7 `Gun.PlaySfx` 수정
- [ ] **보급**: R6 `SupplyDropper` 키 하드코딩 제거
- [ ] **코어**: R7 계약 확장 / R10 `DamageKind.Status` / R1 계약 위치
- [ ] **보스**: R11 `BossController` 알림 목록(패턴 단계·포격 발사·안테나 개방·등장) / `DamageDealtEvent` 발행 추가 / 등장 연출 유지 여부 / 0장 M3 `BossDTO.cs` 대소문자 정리
- [ ] **코어**: R11 보스 EventBus 이벤트를 `BossDef` 기준으로 바꿀지 (보상·UI 담당과 함께)
- [ ] **전장**: R5-B 를 고르면 금속 바닥 레이어·태그 규칙
- [ ] **팀 전체**: R4 아나운서 사용 범위, R8 도입 시기, R9 테스트 범위
