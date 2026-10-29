# 보급 시스템 재구성 — 확인 체크리스트

> 브랜치: `chore/SupplySystemImprovement` (origin/develop 9f68ac0e 포함)
> 기준 문서: 「이벤트 버스와 외부 데이터 적용 기준」 v0.1
> 체크 방법: 각 항목의 **확인 방법**대로 해 보고 `[ ]` → `[x]`

## 0. 먼저 할 일 (이 두 가지가 끝나야 플레이 확인 가능)

- [ ] **컴파일 오류 해결 — 복구 완료, 플레이 확인 필요**
  `feat/Run-Director` 머지(f20e4406) 때 Gun.cs 변경(커밋 9fd4b808)이 빠져 `PlayerController.Input.cs:136` 의 `Gun.CancelActions()` 가 없었다.
  develop 쪽 라운드 정리 규칙과 합쳐 복구:
  - `CancelActions()` (방아쇠·조준·장전·점사 취소) 복구, 모든 `RoundCleanupEvent` 에서 호출
  - FullReset: 정리 시점에 탄약을 시작값으로(develop 규칙) + 다음 `RoundStartedEvent` 에서 능력치 재계산 후 타이머·탄 퍼짐·반동·탄약 초기화(RunDirector 규칙)
  - 차이: RunDirector 원본은 재시작 때 `RefillAmmo()`(예비 탄창 최대)였지만, develop 의 유한 예비 탄창 규칙에 맞춰 `ResetAmmo()`(시작값)로 통일
  - 다른 파일에는 같은 머지로 빠진 줄 없음 (9fd4b808 의 나머지 .cs 전부 대조)
  - 확인: Unity Console 에 error CS 없음 / 게임오버 → 재시작 시 탄약이 StartMagazines, 강화 해제된 탄창 크기 / 라운드 전환 때 조준·장전이 풀림
- [ ] **보급 안내음 wav 만들기**
  1. Unity 메뉴 `Tools > Audio > Announcer Pack > 1. Split Into Candidates`
  2. `Assets/Audio_Work/AnnouncerPack/` 의 `Seg_번호_시작-끝.wav` 를 들어 보고 보급 대사 하나를 선택
  3. 선택한 채 `Tools > Audio > Announcer Pack > 2. Use Selected As Supply Announce`
     → `Resources/Audio/Announce/Announce_SupplyDrop.wav` 생성
  4. `Assets/Audio_Work` 폴더 삭제 (커밋하지 않음)
  - 대사 두 개가 한 파일로 붙어 나오면 `AnnouncerPackSplitter` 의 `MergeGapSeconds`(0.35)를 줄여 다시 분할
  - 확인: 플레이 중 Console 에 `sfx_supply_announce ... AudioClip 을 찾을 수 없습니다` 경고 없음

## 1. 요구사항

| 확인 | 요구사항 | 구현 위치 | 확인 방법 |
|---|---|---|---|
| [ ] | Terrain Height 30 이상 지역에는 투하 금지 | `ConfigTable.SupplyMaxTerrainHeight=30`, `SupplyDropper.GetTerrainHeight` | 산이 많은 맵에서 여러 번 투하 → 착지 지점 높이가 모두 30 미만 (`SupplyTester` 1 키 로그) |
| [ ] | 첫 보급 20초, 이후 25초 간격 (요청 15초 → 플레이 체감상 잦아 25초로 조정) | `RoundTable.SupplyStartTime=20`, `SupplyInterval=25` | 라운드 시작 후 20초·45초·70초에 투하 알림 |
| [ ] | 투하 시 안내 방송 | `SupplyTable.AnnounceSfxKey` → `sfx_supply_announce` (Ui, 2D), `GameSoundRouter.OnSupplyDropped` | 투하될 때마다 무전 안내음 (0번 항목 완료 후) |
| [ ] | 획득 시 랜덤 장전음 | `SupplyTable.CollectSfxKey` → `sfx_supply_reload` (장전음 6개 중 랜덤) | 여러 번 획득 → 소리가 바뀜 |
| [ ] | 탄약은 수류탄 팩·회복 팩에 모두 포함 | `SupplyCrate.TryCollect` → `IPlayerAmmo.AddSupplyMagazines` | 어느 상자를 먹어도 HUD 에 `예비 탄창 +N` |
| [ ] | 탄창 충전 = 사용 가능한 슬롯의 모든 총, 최대 예비 탄창의 1/2 | `WeaponTable.SupplyMagazineRatio=0.5`, `GunSlot.AddSupplyMagazines` (예비로 꺼 둔 총 제외) | 라이플 0/6 → 3/6, 들지 않은 슬롯 총도 함께 증가 |
| [ ] | 스플래시 무기는 엄격하게 (0.25) | `WeaponTable` 2007 로켓 / 2008 유탄 = 0.25 (내림, 최소 1) | 로켓 최대 8 → +2, 유탄 최대 4 → +1 |
| [ ] | 힐팩·수류탄 팩 1~3개 랜덤 | `SupplyTable.AmountMin=1`, `AmountMax=3`, `SupplyDef.RollAmount` | 여러 번 획득 → +1/+2/+3 이 섞여 나옴 |
| [ ] | 설계 기준 문서 준수 | 아래 2번 | — |

## 2. 기준 문서 적용 (통신·데이터 위치)

| 확인 | 항목 | 결정 |
|---|---|---|
| [ ] | RoundSequencer → SupplyDropper 시작·중지 | 직접 호출 유지 (라운드가 순서를 소유) |
| [ ] | 상자 → 플레이어 지급 | 직접 호출 유지 (지급 결과에 따라 상자 유지/제거) |
| [ ] | 투하 사실 → HUD·투하음/안내음·빛기둥 | `SupplyDroppedEvent` (독립 구독자 3개). 투하음을 `SupplyDropper` 직접 재생 → `GameSoundRouter` 로 이동 |
| [ ] | 획득 사실 → HUD·장전음·분석 | `SupplyCollectedEvent` + 실제 지급량(`ItemAmount`, `MagazinesAdded`) |
| [ ] | 튜닝 값 | 기존 TSV(Round/Supply/Weapon/Config/Sound)에 열·행만 추가. 새 테이블·새 파이프라인 없음 |

## 3. 보완 항목 (제안한 방법으로 반영)

| 확인 | # | 문제 | 반영한 방법 | 확인 방법 |
|---|---|---|---|---|
| [ ] | 1 | 같은 값이 여러 곳에 있음: `SupplyDropper.dropInterval`(Inspector 25) ↔ `RoundDef.SupplyInterval`, RoundSequencer Inspector 타임라인이 `SupplyStartTime` 을 조용히 무시, 미사용 `GameConfig.SupplyLifetime` | `dropInterval` 직렬화 필드 삭제(값은 `StartAutoDropCycle(interval)` 인자로만), RoundSequencer 타임라인 삭제, `SupplyLifetime` 상수·문서 삭제 | 시작·간격을 RoundTable 에서만 바꿀 수 있음 |
| [ ] | 2 | 투하음은 SupplyDropper, 획득음은 Router 로 경로가 둘 | 둘 다 `GameSoundRouter.Items` 에서 이벤트로 재생 | 투하음·획득음 정상 |
| [ ] | 3 | 획득 이벤트에 Def 만 있어 HUD 가 `Def.Amount` 를 표시 → 랜덤이면 틀림 | `SupplyCollectedEvent.ItemAmount / MagazinesAdded / Position` 추가, HUD·분석기가 실제 지급량 사용 | 가득 찬 상태에서 먹으면 늘어난 만큼만 표시 |
| [ ] | 4 | 지점을 못 찾으면 그 회차가 사라짐 (높이 제한으로 더 자주 발생) | 후보 재시도 10 → 20회, 실패하면 1초 뒤 재시도(최대 5번) 후 다음 주기 | 산악 지역에서도 15초마다 꾸준히 투하. ※ 씬에 저장된 `maxPositionTries` 값이 있으면 그 값 우선 |
| [ ] | 5 | Terrain Height 정의 | Terrain 원점 기준 `SampleHeight` (Inspector Terrain Height 와 같은 단위). 해당 XZ 에 Terrain 이 없으면 제한 없음 | 바위 등 장애물 콜라이더 위에 착지하는 경우 허용할지 결정 → 막으려면 추가 작업 필요 |
| [ ] | 6 | 종류별 하드코딩 (색·이름 `? :` 조건 4곳) | `SupplyTable.DisplayName`, `BeaconColor` 열 추가. SupplyCrate·SupplyBeaconOverride·SupplyHud·PlaytestAnalyzer 가 데이터 사용 | 테이블 색·이름 변경이 게임에 반영 |
| [ ] | 7 | `SupplyBeaconOverride` 가 `FindAnyObjectByType<SupplyDropper>` 로 마지막 상자를 추정 | `SupplyDroppedEvent.Crate` 로 상자를 직접 받음. 재질은 보급 Id 별 캐시 (색 = BeaconColor × `hdrIntensity` 3.8) | 안개 속 빛기둥 색: 수류탄 주황, 회복 청록 (이전 HDR 색과 비슷한지 확인) |
| [ ] | 8 | `EventBus` 의 순회 복사본이 타입별 static 하나 → 핸들러 안에서 같은 이벤트를 재발행하면 바깥 순회가 깨짐 | 호출마다 풀에서 꺼낸 복사본 사용 (할당 없음 유지) | 기존 이벤트 동작 이상 없음 |
| [ ] | 9 | 동시 보급 상한 10 (15초 간격이면 약 150초에 도달, 보스 165~195초) | 상한을 `ConfigTable.SupplyMaxActive` 로 이동, 값 10 → 5 (안 주운 상자가 5개 쌓이면 투하 중지) | 상자 5개를 줍지 않고 두면 다음 투하가 건너뛰어짐 |
| [ ] | 10 | 테스트·도구 영향 | `SupplyTester` 로그 수정. `TesterPlayer` 는 IItemUser 만 구현 → 테스트 씬에서는 탄약 충전 없이 아이템만 지급(의도대로 동작) | SupplyPackTestscene 에서 1 키 투하·획득 |

## 4. 동작 규칙 변경 (알아 둘 것)

- [ ] 아이템·탄약 중 **하나라도** 실제로 늘면 상자를 획득(제거)한다. 둘 다 가득이면 상자가 남는다.
  (예: 수류탄이 가득이어도 탄약이 부족하면 먹어지고, 수류탄은 0개 지급)
- [ ] `Scenes/CY/Round Test.unity` 는 타임라인(5초 보급 시작)을 쓰고 있었다 → 이제 RoundTable 값(20초)을 따른다. 빠른 확인이 필요하면 RoundTable 을 임시로 바꿀 것
- [ ] 씬·프리팹에 남은 옛 직렬화 값(`dropInterval`, `maxActiveSupplies`, `grenadeColor`, `healColor`, `timeline`)은 무시되며, 다음 저장 때 Unity 가 정리한다

## 5. 변경 파일

- 데이터: `RoundTable`, `SupplyTable`(열 재구성), `WeaponTable`(+SupplyMagazineRatio), `ConfigTable`(+SupplyMaxTerrainHeight, +SupplyMaxActive), `SoundTable`(+sfx_supply_announce, sfx_supply_collect → sfx_supply_reload)
- DTO·Def: `SupplyDto/Def`, `WeaponDto/Def`, `GameConfig`, `DataManager.LinkSounds`
- 계약·이벤트: `IPlayerAmmo.AddSupplyMagazines`, `SupplyDroppedEvent`, `SupplyCollectedEvent`, `EventBus`
- 로직: `SupplyDropper`, `SupplyCrate`, `Gun`, `GunSlot`, `RocketLauncher`, `GrenadeLauncher`, `RoundSequencer`
- 구독자: `GameSoundRouter(.Items)`, `SupplyHud`, `SupplyBeaconOverride`, `PlaytestAnalyzer`, `SupplyTester`
- 도구: `Audio/Editor/AnnouncerPackSplitter` (신규)
- 문서: `Docs/Tables/README.md`, `Guides/02-API-사용법.md`, `Guides/03-라운드-진행.md`, `Architecture/02-SystemInterfaces.md`
