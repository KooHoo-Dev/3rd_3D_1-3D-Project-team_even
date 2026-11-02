# 라운드 종료 강화 시스템 보완 — 기획안 (v3.2: 등급 카드 상점)

> 상태: **기획 확정 · 구현 완료(에디터 확인 전)**  ·  작성일 2026-11-02  ·  브랜치 `chore/UpgradeSystem-Improvement`
> 근거: 2차 빌드 팀원 피드백 · 팀장 의견(2026-11-02) · 「이벤트 버스와 외부 데이터 적용 기준」 v0.1
> 관련: [UpgradeSystem_Handoff.md](UpgradeSystem_Handoff.md) · [UpgradeUI_Mockup.html](UpgradeUI_Mockup.html) · 팀 공유 표 [UpgradeList_v3.png](UpgradeList_v3.png) · BL-030/031/032
> 이전 버전: v2(특화 · 슬롯 보장 · 리롤 +5 증가)는 git `1659dca2`
> **담당 범위: 강화 시스템만.** 라운드 포맷 · 랜덤 기믹은 라운드 담당 (6장은 의존 사항만 기록)

## 0. 결정 사항

| 항목 | 결정 |
|---|---|
| 레벨 | 모든 강화 **최대 2레벨** |
| 종류 | **8종** (4장). 특화 · 슬롯 보장 삭제. 회피 기동 · 연사력은 팀장 의견으로 삭제 (밸런스 · 효용성) |
| 비용 | 레벨로만: **Lv1 60 / Lv2 100** |
| 등급 | **3등급** 하급(회색) · 일반(흰색) · 에픽(보라) — 증가량만 차등. **에픽 25%** 로 잘 뜨게 (팀장 의견) |
| 개수 강화 | 하급 · 일반 +1, 에픽 +2 |
| 리롤 | **항상 5**, 횟수 무제한으로 시작 (테스트 후 상한 검토) |
| 러키걸 | 에픽 확률만 조금 올림. **행운은 엄격하게** |
| 등급 색 | `UpgradeGradeTable.Color` (테이블) |
| 상점 횟수 | R1 · R2 종료 후 **2번**. R2 섬멸전(2:30 **시간 내 생존**) 종료 후에도 상점 열림 + 클리어 보상 150 (라운드 담당에 요청) |

### 변경 이력
| 버전 | 내용 |
|---|---|
| v3 | 2차 빌드 피드백: 일반 10종 · 6등급 · 리롤 5 고정 |
| v3.1 | 강화 10종 정정(스태미나 · 구르기 무적 · 연사력 · 러키걸 추가), R1~R4 확정, 러키걸 하향 |
| **v3.2** | 팀장 의견: 회피 기동 · 연사력 삭제(8종), 등급 3단계(하급 · 일반 · 에픽), 에픽 25% |

---

## 1. 게임 흐름 전제

```
R1 현행(군중전→클리페오사우루스) ─▶ [상점 1] ─▶ R2 섬멸전 2:30 생존 (+랜덤 기믹) ─▶ [상점 2] ─▶ R3 현 2라운드(티라노포드, +랜덤 기믹) ─▶ 클리어
   획득 ~300                                   획득 ? + 클리어 보상 150                         티라노포드 처치 연출의 GameClearedEvent (현행)
```

---

## 2. 상점 규칙 — 무작위 3장 + 고정 리롤

1. 상점이 열리면 **카드 3장**. 카드 한 장 = **(강화 종류, 등급)** = 그 강화의 다음 1레벨.
   - 종류: 최대 레벨이 아닌 강화 중 `Weight` 가중치로, 한 화면에 같은 종류는 한 장만
   - 등급: 등급 가중치(3장, `LuckyGirl` 반영)로 카드마다 따로
2. 구매하면 **SOLD**. 재화가 되면 3장 다 사도 된다. 남은 재화는 다음 상점으로 (현행).
3. **리롤 5:** SOLD 가 아닌 카드만 종류 · 등급을 다시 뽑는다.
4. 후보가 3개보다 적으면 있는 만큼, 없으면 "모든 강화 완료". "다음 라운드 ▶" 는 언제든 (현행).

예) 상점 1 (300): Lv1 ×3 = 180 → 리롤 ×2 = 10 → Lv1 60 → 남음 50 · 상점 2 (50 + R2 획득): Lv2 100 ×2 + Lv1 … → 한 판 약 **6~7장** / 전체 16레벨

---

## 3. 등급 (3단계)

| 등급 | 이름 | 색 | 기본 확률 |
|---|---|---|---|
| `Poor` | 하급 | `#9E9E9E` 회색 | 40% |
| `Common` | 일반 | `#FFFFFF` 흰색 | 35% |
| `Epic` | 에픽 | `#B65BD0` 보라 | **25%** |

- 카드 3장 중 에픽이 1장 이상 나올 확률 ≈ **58%** → 상점을 열 때마다 절반 이상은 에픽을 보고, 리롤 한두 번이면 대부분 만난다 ("띄우는 맛").
- 에픽 증가량은 일반의 약 2~2.5배로 크게 잡아 차이가 확실히 느껴지게 한다.
- 증가량은 배율을 곱하지 않고 **강화마다 등급별 값을 표에 직접** 적는다 (`GradeValues`).
- 레벨마다 산 등급이 따로 기록된다. 예) 재장전 Lv1 에픽(−15%) + Lv2 하급(−6%) = −21%.
- 에픽 색은 어두운 카드 배경에서 잘 보이도록 밝은 보라(`#B65BD0`)로 했다.

---

## 4. 강화 목록 (8종 · 최대 2레벨 · 60 / 100)

> 등급별 값 = 한 레벨 살 때 증가량 (하급 / 일반 / 에픽)

| # | 강화 (`UpgradeType`) | 이름 | 등급별 값 | 적용 위치 |
|---|---|---|---|---|
| 1 | `ReloadSpeed` (기존) | 빠른 재장전 | 재장전 시간 −6 / 8 / 15 % | `Gun.RefreshStats` (기존) |
| 2 | `MagazineSize` (기존) | 확장 탄창 | 탄창 크기 +4 / 6 / 12 발 | `Gun.RefreshStats` (기존, `MagazineUpgradeRate`) |
| 3 | `GrenadeRadius` | 광역 장약 | 수류탄 폭발 반경 +8 / 10 / 25 % | `PlayerItems.RefreshStats` |
| 4 | `MaxHealth` | 강화 슈트 | 최대 체력 +8 / 10 / 25 (늘어난 만큼 현재 체력도) | `PlayerController.Upgrade` |
| 5 | `MaxStamina` | 지구력 강화 | 최대 스태미나 +10 / 15 / 30 | `PlayerController.Upgrade` |
| 6 | `StaminaRegen` | 호흡 조절 | 스태미나 회복 속도 +10 / 15 / 30 % | `PlayerController.Upgrade` |
| 7 | `HealCapacity` | 의료 키트 | 회복 팩 최대 보유 +1 / 1 / 2 | `PlayerItems.RefreshStats` |
| 8 | `LuckyGirl` | 러키걸 | 에픽 확률 ×(1 + 10 / 15 / 25 %) | `UpgradeManager` 등급 뽑기 |

- 삭제: 수류탄 파우치(`GrenadeCapacity`, v3) · 회피 기동 · 연사력(v3.2).
  `GrenadeCapacity` 는 이미 커밋된 enum · 코드 참조가 있어 **enum 값은 남기고 테이블 행만 삭제**. 회피 기동 · 연사력은 커밋 전이라 enum · 코드에서 모두 뺐다.
- 러키걸: 에픽 가중치에만 배율. Lv1 일반(×1.15) → 에픽 약 27.7%, 최대 Lv2 에픽 둘(×1.5) → 약 33%. 상점 1 에서 사면 그 상점 리롤 · 상점 2 전체에 적용.

---

## 5. 데이터 · 통신 설계 (기준 v0.1)

### 5-1. 데이터

| 항목 | 방식 | 위치 | 이유 |
|---|---|---|---|
| 강화별 레벨 · 비용 · 등급별 값 · 이름 · 가중치 | 외부 데이터 | `UpgradeTable.tsv` → `UpgradeDto` → `UpgradeDef` | 종류별 변형 · 반복 튜닝 (현행 확장) |
| 등급 이름 · 색 · 기본 가중치 | 외부 데이터 (신규) | `UpgradeGradeTable.tsv` → `UpgradeGradeDef` | 등급별 비교 · 튜닝, enum 검증. 색은 `SupplyTable.BeaconColor` 선례 |
| 카드 수 3 · 리롤 비용 5 | 외부 데이터 | `ConfigTable` `UpgradeOfferCount` · `UpgradeRerollCost` | 전역 튜닝 값 |
| 체력 · 스태미나 **기본값** | Inspector (현행 유지) | `PlayerController` | 기준값 소유 위치는 그대로, 강화는 "기본값 + 강화 합계"로만 더한다 |
| 카드 UI 프레임 · 아이콘 | Inspector | `UpgradeUiSkin` | 에셋 참조 |
| 강화 아이콘 (enum 순서 9칸) | Inspector | `UpgradeUiSkin.upgradeIcons` — SCI-FI UI Pack Pro `White Icons` (재장전 Loop · 탄창 Bullet · 수류탄 파우치/광역 장약 Bomb · 강화 슈트 Shield(Icons2) · 지구력 Run2 · 호흡 Lightning · 의료 키트 Health · 러키걸 Dice), 미리보기 [UpgradeIcons_v3.png](UpgradeIcons_v3.png) | 에셋 참조 |
| 제시 카드 · SOLD / 레벨별 구매 등급 | 런타임 | `UpgradeManager` / `UpgradeLedger` | 현재 값, 재시작 시 초기화 |

`UpgradeTable.tsv` — `Value` 열을 `GradeValues`(하급;일반;에픽)로 바꾸고 `Weight` 열 추가, Lv3 행 삭제

```
Id	UpgradeType	Level	Cost	GradeValues	Weight	Name	Description
6001	ReloadSpeed	1	60	0.06;0.08;0.15	10	빠른 재장전 I	재장전 시간 {0}
6101	LuckyGirl	1	60	0.10;0.15;0.25	10	러키걸 I	에픽 등급 확률 {0}
```

- `GradeValues` 는 등급 수(3)만큼 · 0 초과가 아니면 로드 오류.
- 같은 `UpgradeType` 의 모든 행은 `Weight` 가 같아야 함 (`DataManager.GroupUpgrades` 검증).
- 합계 검증: `ReloadSpeed` 를 모두 에픽으로 사도 1 미만. `UpgradeGradeTable` 은 등급 3행이 모두 있어야 함.

`UpgradeGradeTable.tsv` (신규): `Grade	Name	Color	Weight` 3행 (3장 표)

### 5-2. 통신

| 통신 | 방식 | 이유 |
|---|---|---|
| 상점 열기 | 기존 `GameStateChangedEvent(Reward)` 구독 | 현행 유지 |
| 카드 구매 | 직접 호출 `UpgradeManager.TryPurchase(카드 번호)` → `IUpgradeState.TryPurchase(type, grade, wallet)` | 결과가 필요한 요청 |
| 리롤 | 직접 호출 `UpgradeManager.TryReroll()` → `ICurrencyWallet.TrySpend` | 결과가 필요한 요청. 새 전역 이벤트 없음 |
| 화면 갱신 | 객체 C# event `UpgradeManager.Changed` | 가까운 객체 관찰 (현행) |
| 능력치 반영 | 기존 `UpgradePurchasedEvent` 구독 → `GetTotalValue` 재계산 (`Gun` · `PlayerItems` 현행 + **`PlayerController` 추가**) | 독립 모듈이 확정 사실 관찰 (현행 패턴). 이벤트 필드 변경 없음 |
| 강화 초기화 반영 | **신규 `UpgradesResetEvent`** (`UpgradeLedger.ResetAll` 이 발행, `Gun` · `PlayerItems` · `PlayerController` 구독) | 강화 상태 소유자가 확정한 사실. 재시작 정리 이벤트(`RoundCleanupEvent`) 구독 순서에 기대지 않게 |
| 상점 종료 | 기존 `RewardSelectionFinishedEvent` | 현행 |

### 5-3. 계약 변경 (코어)

```csharp
public interface IUpgradeState
{
    int GetLevel(UpgradeType type);
    float GetTotalValue(UpgradeType type);               // 레벨별 (산 등급의 값) 합
    UpgradeGrade? GetGrade(UpgradeType type, int level); // 신규 — 보유 강화 표시용
    UpgradeDef GetNext(UpgradeType type);
    bool TryPurchase(UpgradeType type, UpgradeGrade grade, ICurrencyWallet wallet); // grade 추가
    void ResetAll();                                     // UpgradesResetEvent 발행
}
```

---

## 6. 라운드 담당에 요청 (의존 사항)

| 요청 | 이유 |
|---|---|
| R2 섬멸전(2:30 생존) 종료 시 `GameState.Reward` 로 전환 | 지금은 보스 처치 후에만 상점이 열림 → 상점 2 |
| 섬멸전 클리어 보상 150 지급 | 보스 보상이 없으면 상점 2 재화 부족 |
| 기믹이 플레이어 스태미나 · 체력 등을 바꾸면 알려 주기 | 강화 합계와 겹치는 방식 맞추기 |

---

## 7. 화면 (UpgradeScreen)

| 변경 | 내용 |
|---|---|
| 카드 연결 | 강화 종류 고정 3장 → 상점 카드 번호(`GetOption(i)`) |
| 등급 | 카드 위 등급 이름 · 강화 이름 · 테두리 = 등급 색 |
| 증가량 | "재장전 시간 -15%" (등급 값) / "현재 → 구매 후" 누적 |
| 레벨 | "Lv0 → Lv1" / "Lv1 → Lv2", 별은 산 레벨의 등급 색 |
| SOLD · 리롤 | 산 카드 SOLD, "리롤 5" 재화 부족 시 비활성 |
| 보유 강화 | 좌측 상단 산 강화 목록 (레벨마다 등급 색 점) |

---

## 8. 변경 범위

| 파일 | 변경 | 담당 |
|---|---|---|
| `Core/Data/GameEnums.cs` | `UpgradeType` 끝에 6개 추가, `UpgradeGrade` 신규 | 강화 |
| `Core/Data` Dto · Def · `DataManager` · `GameConfig` | 열 변경, `UpgradeGradeTable` 로드 · 검증, Config 키 | 강화 |
| `StreamingAssets/Tables` · `Docs/Tables/README.md` | `UpgradeTable` 재작성, `UpgradeGradeTable` 신규, Config 키 | 강화 |
| `Core/Contracts/ServiceContracts.cs` · `UpgradeLedger` · `GameEvents` | 5-3, `UpgradesResetEvent` | 코어 (강화 작업자가 수정) |
| `Reward/UpgradeManager.cs` · `UI/UpgradeScreen.cs` · `UpgradeUiSkin` | 카드 · 등급 · 리롤 · SOLD | 강화 |
| `Weapon/Gun.cs` | `UpgradesResetEvent` 구독만 | **무기 담당 공유** |
| `Player/Item/PlayerItems.cs` | 수류탄 반경 · 회복 팩 최대 | **플레이어 담당 공유** |
| `Player/Controller/PlayerController.*` | 최대 체력 · 스태미나 · 회복 (신규 partial `PlayerController.Upgrade.cs`, 필드 → 프로퍼티) | **플레이어 담당 공유** |

**바꾸지 않는 것:** 라운드 흐름, `UpgradePurchasedEvent` 구조, 보급 규칙, 로드아웃 화면, 구르기 · 연사.

---

## 9. 보완 질문 (2026-11-02 확정)

| R | 내용 | 결정 |
|---|---|---|
| R1 | "최대 탄창 수 증가"의 의미 | **탄창 크기(+발, 현행 `MagazineSize`)** |
| R2 | 회피 기동 단점 | v3.2 에서 회피 기동 삭제로 해당 없음 |
| R3 | 러키걸 = 등급 확률 배율 (강화 종류는 영향 없음) | 그대로 (v3.2: 에픽에만) |
| R4 | 플레이어 · 무기 담당 코드(`PlayerController` · `PlayerItems` · `Gun`) | 강화 작업에서 직접 수정 (구독 + 신규 partial 파일로 최소 수정) → PR 에 담당자 리뷰 요청 |
