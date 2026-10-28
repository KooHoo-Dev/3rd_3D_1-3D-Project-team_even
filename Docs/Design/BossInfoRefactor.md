# 보스 정보 구조 개선 — 보스 이벤트·라운드 연결을 `BossDef` 로 (검토안)

> 기준: `feat/FixedSoundSystem` `cba52f87` (develop `3acdf56b` PR #42 포함)
> 적용 기준: `이벤트 버스와 외부 데이터 적용 기준` v0.1
> 관련: [SoundSystemRefactor_v3.md](SoundSystemRefactor_v3.md) 6장 "범위 밖 — 코어·보상"
> 상태: **검토안**. 4장의 결정을 확인받은 뒤 작업한다.

---

## 1. 문제

보스 데이터는 PR #29·#42 로 `MonsterTable` → `BossTable`(`BossDef`) 로 옮겨졌지만, 보스를 가리키는 **이벤트와 라운드 연결은 옛 `MonsterDef` 를 그대로 쓴다.**

| 위치 | 현재 | 결과 |
|---|---|---|
| `BossWarningEvent(MonsterDef)`, `BossAppearedEvent(MonsterDef, GameObject)`, `BossDefeatedEvent(MonsterDef, Vector3)` | 옛 타입 | `BossController` 는 `BossDef` 만 가지므로 **발행할 수 없다** |
| `KillRewardCollector.OnBossDefeated` | `e.Boss.Reward` (`MonsterDef.Reward`) | `BossDef` 에 `Reward` 없음 |
| `RoundTable.BossId = 3001` → `RoundDef.Boss` (`MonsterDef`) | `MonsterTable` 3001 행은 주석 처리됨 | **지금도 데이터 로드 오류** ("BossId 3001 가 MonsterTable 에 없습니다"), `RoundDef.Boss == null` |
| `GameSoundRouter.Boss` | `e.Boss?.DeathSfx`, `boss.HurtSfx` (`MonsterDef`) | `BossDef` 로 바꾸면 컴파일 불가 → 같이 정리해야 함 |

**실게임 영향**: 보스를 잡아도 보스 보상·강화 화면 열기·BGM 정지·클리어음·승리 아나운서가 없다. 보스 등장 시 보스전 재화 집계·카메라 흔들림·보스 BGM 도 없다 (`BossController` 가 EventBus 를 발행하지 않음).

## 2. 기준 적용

### 2-1. 이벤트 (통신 한 건씩)

| 이벤트 | 사실의 소유자 = 발행자 | 독립 소비자 | 판정 |
|---|---|---|---|
| `BossWarningEvent` | 코어(라운드 진행) — 보스 시각 도달 | 사운드(경고음·덕킹) / 예정: UI·조명 | **EventBus 유지**, 페이로드 `BossDef`. 발행자(RoundDirector) 미구현 — 이번 범위 밖 |
| `BossAppearedEvent` | **보스** — 전투 시작 확정 (`02-SystemInterfaces.md` 시퀀스: `B-->>GF: BossAppeared`) | 보상(UpgradeManager 보스전 집계), 플레이어(카메라 흔들림), 사운드(BGM), 코어(GameState 예정) | **EventBus 유지**, 페이로드 `BossDef` + 인스턴스. 발행: `BossController` |
| `BossDefeatedEvent` | **보스** — 체력 소유자가 사망 확정 | 보상(KillRewardCollector 지급, UpgradeManager 임시 열기), 사운드(BGM 정지·클리어·아나운서), 코어(예정) | **EventBus 유지**, 페이로드 `BossDef`. 발행: `BossController` |
| `DamageDealtEvent` (보스 피격) | 보스 — 피해 적용 확정 | UI(히트마커·데미지 숫자), 사운드(타격음·히트마커) | 일반 몬스터와 같은 이벤트. 발행: `BossController.TakeDamage` |
| `BossSkillPhaseChangedEvent`, `BossDamageStageChangedEvent` | 없음 | 없음 (사운드만 구독, 테스터만 발행) | **삭제** (= 사운드 v3.1 S3). 보스 패턴 소리는 지역 이벤트로 |

세 보스 이벤트는 "소유자가 확정한 사실을 서로 독립된 책임(보상·카메라·사운드·코어)이 각자 관찰"하는 경우라 기준 문서의 EventBus 조건에 맞는다. 문제는 버스 사용이 아니라 **페이로드 타입이 데이터 이전을 따라가지 못한 것**이다.

### 2-2. 데이터 (항목 하나씩)

| 항목 | 현재 | 판정 |
|---|---|---|
| 보스 자체 처치 보상 | 주석 처리된 `MonsterTable` 3001 행 `Reward = 150` | **`BossTable.Reward` 로 이동** (보스 종류별 값, 기존 DTO·Def 에 필드 추가) |
| 라운드 보스 추가 보상 | `RoundTable.BossReward` (100 / 150) | **유지**. BL-029 에서 "보스 Reward 와 별도"로 정의된 다른 의미 (보스 종류 기준 vs 라운드 진행 보너스) → 같은 값을 두 곳에서 관리하는 경우가 아니다 |
| 라운드의 보스 지정 | `RoundTable.BossId` → `MonsterTable.Id` | **`BossTable.Id` 를 가리키도록** (값 3001 → 1). 기존 DTO 주석·DataManager 연결만 수정 |
| 보스 이름·프리팹 키 | 옛 `MonsterTable` 행에만 있음 | **이번에 추가하지 않음** — 지금 읽는 코드가 없다 (보스 생성기·보스 UI 가 생길 때 추가) |

## 3. 개선안

### 3-1. Core
```csharp
// GameEvents.cs
public readonly struct BossWarningEvent   { public readonly BossDef Boss; ... }
public readonly struct BossAppearedEvent  { public readonly BossDef Boss; public readonly GameObject Instance; ... }
public readonly struct BossDefeatedEvent  { public readonly BossDef Boss; public readonly Vector3 Position; ... }
// BossSkillPhaseChangedEvent, BossDamageStageChangedEvent 삭제
```
- `BossDto`/`BossDef`: `Reward` 추가 (`>= 0` 검증)
- `RoundDef.Boss`: `MonsterDef` → `BossDef`, `RoundDto.BossId` 주석을 `BossTable.Id` 로
- `DataManager`: `round.Boss` 를 `bosses` 에서 연결, 실패 시 "BossId 가 BossTable 에 없습니다". `IsBoss` 검사(라운드 쪽) 삭제

### 3-2. 데이터
- `BossTable.tsv`: `Reward` 열 추가, 1 행 = 150 (옛 3001 행 값)
- `RoundTable.tsv`: `BossId` 3001 → 1 (두 행)

### 3-3. 보스 (`BossController`)
```csharp
public void Initialize(BossDef def, IPlayerTarget target)
{
    ... // 기존
    EventBus<BossAppearedEvent>.Publish(new BossAppearedEvent(def, gameObject));   // 전투 시작
}

public void TakeDamage(in DamageInfo info)
{
    ... // 기존 (체력 적용 → died 판정 → Damaged)
    EventBus<DamageDealtEvent>.Publish(new DamageDealtEvent(gameObject, info, died));
    if (died)
    {
        Died?.Invoke();
        EventBus<BossDefeatedEvent>.Publish(new BossDefeatedEvent(Def, transform.position));
    }
}
```
- 지역 이벤트(`Damaged`·`Died`)를 먼저, 전역 사실을 뒤에 → 보스 자신의 처리(이미터 사망음 등)가 끝난 뒤 다른 모듈이 관찰
- `MonsterDiedEvent` 는 발행하지 않는다 (보스는 군중 생존 수·일반 보상 대상이 아님)
- **등장 시점**: 지금은 등장 연출이 없어서 `Initialize` = 전투 시작이다. 등장 연출이 생기면 연출 시작 지점으로 옮긴다 (4장 B)

### 3-4. 보상
- `KillRewardCollector.OnBossDefeated`: `e.Boss.Reward` 는 그대로 (타입만 `BossDef`) — 코드 변경 거의 없음, 주석의 `MonsterDef.Reward` → `BossDef.Reward`
- `KillRewardTester`: `bossMonsterId` → `bossId` (`DataManager.Bosses`)

### 3-5. 사운드 (같이 해야 컴파일됨)
- `GameSoundRouter.Boss`: 페이로드를 읽지 않게 — `boss`·`bossInstance`·`BossPosition()` 삭제, 처치 시 `DeathSfx` 재생 삭제(보스 사망음은 사운드 v3.1 S2 `BossSoundEmitter` 담당), `OnBossSkillPhaseChanged`·`OnBossDamageStageChanged` 삭제
- `SoundTester`: 보스 경고·등장 버튼은 `BossDef` 로, 보스 1~6·S 버튼 삭제
- `MonsterTable.HurtSfxKey`(S9) 는 사운드 리팩토링에서 따로

### 3-6. 테스터·문서
- `CameraShakeTester`: `new BossAppearedEvent(null, null)` 그대로 동작 (변경 없음)
- `02-SystemInterfaces.md` 이벤트 표, `02-API-사용법.md`, `03-BacklogMapping.md`(BL-029 `MonsterDef.Reward` → `BossDef.Reward`), `Docs/Tables/README.md`(BossTable·RoundTable 열)

## 4. 결정할 것

| # | 질문 | 권장 | 다른 안 |
|---|---|---|---|
| A | 보스 자체 보상 위치 | **`BossTable.Reward` 추가** (BL-029 의 "보스 Reward + 라운드 BossReward" 유지) | `RoundTable.BossReward` 하나로 합치고 보스 자체 보상 삭제 (보상 규칙 변경 → 보상 담당 결정) |
| B | `BossAppearedEvent` 발행 시점 | **`BossController.Initialize` 끝** (등장 연출이 생기면 이동) | 별도 `BeginBattle()` 메서드를 만들어 호출 쪽이 정함 (지금 호출할 코드가 테스터뿐) |
| C | 보스 피격 `DamageDealtEvent` 도 같이 | **같이** (히트마커·타격음 — 같은 `TakeDamage` 수정) | 사운드 리팩토링 때 |
| D | 작업 위치 | **develop 기준 별도 브랜치 → 작은 PR** (보상·보스 담당이 바로 받을 수 있고, 사운드 PR 과 분리 리뷰). 사운드 브랜치는 머지 후 develop 을 받아 이어감 | 이 브랜치(`feat/FixedSoundSystem`)에서 같이 → 사운드 PR 이 끝날 때까지 다른 파트가 못 받음 |

### 범위 밖 (후속)
- `BossWarningEvent` 발행 = RoundDirector (코어 미구현)
- 옛 보스 잔재 정리: `MonsterRole.Boss`·`MonsterGrade.Boss`·`MonsterDef.IsBoss` 검증, `BossPhaseTable`·`BossPhaseDef`, 주석 처리된 보스 행·검증 블록
- 보스 이름·프리팹 키 (`BossTable`) — 보스 생성기·UI 가 생길 때
- `BossDef.Id` 만 public 필드(나머지는 프로퍼티) — 보스 담당 정리 사항
