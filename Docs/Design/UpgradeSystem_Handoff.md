# 강화(업그레이드) 시스템 개편 인수인계 (chore/UpgradeSystem-Improvement)

> 작성 2026-11-02 · 다른 PC에서 이어서 작업하기 위한 메모
> 기획안 본문: [UpgradeSystemExpansion_Proposal.md](UpgradeSystemExpansion_Proposal.md) (v2: 랜덤 3장 상점)

## 0. 지금 상태

- **기획 단계.** 코드 · 데이터(TSV)는 하나도 바꾸지 않았다.
- 기획안 v2 를 팀 피드백 공유 자료에 요약해 넣음.
- 2차 빌드 피드백을 반영해 **기획안 v3** 작성 (2026-11-02). 아래 3 · 4 · 5장은 v2 기준 기록이며, 최신 결정 · 질문은 기획안 v3 0장 · 9장을 본다.
- **구현 완료 (2026-11-02, 커밋 전):** 데이터(`UpgradeTable` v3 · `UpgradeGradeTable` · Config 2키) · `IUpgradeState` 등급 기록 + `UpgradesResetEvent` · 상점 카드/리롤/러키걸 · 강화 화면 등급색/SOLD/리롤/보유 강화 · 무기(초기화 구독만) · 수류탄 범위 · 회복 팩 보유 · 플레이어 체력/스태미나(`PlayerController.Upgrade.cs`). v3.2 팀장 의견: 회피 기동 · 연사력 삭제(8종), 등급 3단계(하급 · 일반 · 에픽 25%).
  dotnet 컴파일만 확인. **남은 것:** 에디터 플레이 확인, 플레이모드 테스트 실행, PR 에 플레이어·무기 담당 리뷰 요청.

## 1. 다른 PC에서 시작하기

1. 이 PC에서 커밋 후 **푸시**되어 있어야 한다.
2. 다른 PC에서 `git fetch` → `chore/UpgradeSystem-Improvement` 체크아웃.
3. 이 문서 → 기획안 v2 순서로 읽고, 아래 3장(결정 사항) · 5장(열린 질문)부터 이어 간다.

## 2. 진행 경과

| 버전 | 내용 | 바뀐 이유 |
|---|---|---|
| v1 | 강화 3종 → 9~12종, 전체 나열 상점, Lv3 까지, 1단계 총 비용 4,940 | 첫 제안 |
| v1.1 | 문제(P1~P5) → 보완안 구조로 재구성, 헬다이버즈2 차용(보급 강화 · 특화 한쪽만 · 부위 파괴 · 투하 준비) 추가 | "지금 시스템의 문제를 보완하는 기획안으로" |
| v2 | **무작위 3장 + 리롤**, 14종 모두 최대 2레벨, 일반 60/100 · 특화 90/140, 상점 2번 기준 | 1스테이지 재화 최대 ~300 → v1 비용이 비쌈 · 랜덤 3장으로 선택 제한 · 리롤 5부터 · 3스테이지로 마무리 |
| v3 | 일반 10종만 · **6등급(하급~전설) 증가량 차등** · 리롤 5 고정 · 특화/슬롯 보장 삭제 · 라운드 포맷(R2 섬멸전) 전제 | 2차 빌드 팀원 피드백 |
| **v3.1 (현재)** | 강화 10종 정정 (스태미나 · 구르기 무적 · 연사력 · 러키걸 추가, 수류탄 파우치 · 보급 계열 삭제), Q1~Q8 확정, 팀 공유 표 `UpgradeList_v3.png` | 작성자 확인 2026-11-02. 남은 질문 = 기획안 9장 R1~R4. 강화 시스템만 담당 (라운드는 라운드 담당) |

## 3. 팀(작성자)이 정한 조건 — 바꾸지 말 것

| 조건 | 값 |
|---|---|
| 1스테이지 획득 재화 | 많아야 **~300** |
| 리롤 비용 | **5 부터** 시작 (20 은 비쌈). v2 에서는 +5씩 증가, 상점마다 초기화 |
| 게임 길이 | **3스테이지**로 마무리 → 상점은 스테이지 1 · 2 클리어 후 **2번** |
| 상점 방식 | 전체 나열이 아니라 **무작위 3장** 으로 제한 |
| 범위 | 게임 시스템에 큰 변화를 주지 않는다 (라운드 흐름 · 구매 처리 · 재시작 초기화 · 보급 규칙 유지) |

## 4. v2 핵심 (요약)

- 카드 한 장 = 어떤 강화의 다음 1레벨, 재화로 구매 → SOLD. 리롤은 SOLD 아닌 카드만.
- 슬롯 보장: 상점 1 = 특화 카드 1장 / 상점 2 = 가진 강화의 다음 레벨 1장.
- 특화는 군중전 / 보스전 중 **한쪽만**.
- 일반 10종: 피해량 · 연사력 · 재장전★ · 탄창★ · 수류탄 보유★ · 수류탄 위력 · 최대 체력 · 회복 팩 보유 · 보급 간격 · 투하 준비(스테이지 시작 지급)
- 특화 4종: 군중전 = 탄약 회수 · 처치 회복 / 보스전 = 약점 분석 · 부위 파괴
- 한 판 재화 ~700 → 8~9장 구매 (실제 구매 가능 최대 2,060 의 약 1/3)

## 5. 열린 질문 (2차 빌드 피드백 때 확인)

| # | 질문 | 담당 / 방법 |
|---|---|---|
| 1 | 스테이지 2 · 3 실제 획득 재화 (v2 는 400 가정) | 플레이 로그 `강화 선택 열림: 이번 라운드 +N` |
| 2 | 강화 14종 구성 · 레벨당 수치에 대한 팀 의견 | 팀 공유 후 |
| 3 | 슬롯 보장 규칙 · 특화 한쪽만 규칙 도입 여부 | 팀 공유 후 |
| 4 | 클리페오사우루스 발사관 부위 파괴 처리가 있는지 (`PartHp` 처리는 지금 티라노포드에만 있음) | 보스 담당 |
| 5 | 지금은 티라노포드(2스테이지 보스) 처치 연출에서 `GameClearedEvent` 발행 → 3스테이지 마무리로 조정 | 라운드 · 보스 담당 (강화 범위 밖) |
| 6 | 스테이지 3 에서 번 재화는 쓸 곳이 없음 → 결과 화면 표시 여부 | 팀 결정 (강화 범위 밖) |
| 7 | 화면 목업을 먼저 만들지 (`UpgradeUI_Mockup.html` 에 리롤 · SOLD · 분류 태그 추가) | 권장: 목업 먼저 |

## 6. 구현할 때 볼 코드 (조사해 둔 위치)

| 대상 | 위치 | 메모 |
|---|---|---|
| 강화 상점 로직 | `Assets/Scripts/Reward/UpgradeManager.cs` | `GetOption` · `TryPurchase` · `Open`. 여기에 `Offers` · 뽑기 · `TryReroll` 추가 |
| 강화 화면 | `Assets/Scripts/UI/UpgradeScreen.cs` | 카드가 `manager.Types[i]` 에 묶여 있음 (147 · 364줄) → `Offers[i]` 로 |
| 강화 상태 · 구매 | `Core/Contracts/ServiceContracts.cs` `IUpgradeState` (구현 `UpgradeLedger`) | 그대로 사용 |
| 재화 | `ICurrencyWallet.TrySpend` | 리롤 차감에 사용 |
| 데이터 | `StreamingAssets/Tables/UpgradeTable.tsv`, `Core/Data/Dtos/UpgradeDto.cs`, `Defs/UpgradeDef.cs`, `DataManager.GroupUpgrades` | 열 `Category` · `Weight` 추가 |
| 강화 종류 | `Core/Data/GameEnums.cs` `UpgradeType` | 끝에 추가 규칙 |
| 총 능력치 | `Weapon/Gun.cs` `RefreshStats` (+ `MagazineUpgradeRate` 같은 virtual 훅) | 산탄 `ShotGun.MagazineUpgradeRate = 0.2` |
| 수류탄 · 회복 팩 | `Player/Item/PlayerItems.cs` `RefreshStats` | "강화가 생기면 여기서 더한다" 주석 자리 |
| 스테이지 시작 지급 | `Supply/RoundStartSupply.cs` + `ConfigTable` `RoundStart*` | 투하 준비 강화 |
| 보급 간격 | `Round/RoundSequencer.cs` `StartAutoDropCycle(roundDef.SupplyInterval)` (198 · 248줄) | 보급 우선순위 강화 |
| 약점 | `Boss/BossWeakPoint.cs`, `MonsterWeakPoint.ScaleDamage` | 약점 분석 강화 |
| 부위 체력 | `Boss/Tyranopode/TyranopodeController.cs` `PartMaxHp` | 부위 파괴 강화 |
| 처치 보상 | `Reward/KillRewardCollector.cs` (`MonsterDiedEvent`) | 탄약 회수 · 처치 회복도 같은 이벤트 구독 |

## 7. 참고 — 같은 폴더의 관련 문서

- `2차 빌드 피드백 정리.md` — v2 요약이 붙어 있음 (작성자 로컬 파일, 이 커밋에는 포함하지 않음)
- [SupplyUpgradeRework_Proposal.md](SupplyUpgradeRework_Proposal.md) — 다회 구매 · 강화 화면 스킨 이전 작업
- [UpgradeUI_Mockup.html](UpgradeUI_Mockup.html) — 기존 강화 화면 목업
