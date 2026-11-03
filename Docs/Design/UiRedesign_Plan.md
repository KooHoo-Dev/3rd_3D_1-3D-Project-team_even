# UI 리디자인 구현 설계 — 코드 충돌 방지

브랜치 `feat/UiRedesign` (develop 기준) · 디자인 시안: 디자인 캔버스 "TEAM EVEN UI 리디자인" (보드 00~10)

목표: 메인 메뉴의 "Moonlit Night" 스타일(Moon White · Steel · Moon Red · Ember, Aldrich + Noto Sans KR, 상자 대신 선과 띠)을 게임 전체 UI 에 적용한다.
**다른 사람이 동시에 작업 중인 파일과 같은 줄을 건드리지 않는 것**을 최우선으로 한다.

---

## 1. 충돌 위험 지도 (2026-11-03 기준)

| 파일 | 주 작업자 | 최근 수정 | 진행 중인 다른 브랜치 | 위험 |
|---|---|---|---|---|
| `UI/MainMenu/Editor/MainMenuUIBuilder.cs` + 메인 메뉴 씬 | — | — | 없음 | 보통 (씬 재생성) |
| `UI/Loadout/LoadoutPanel.cs` | MinSeong · pinkshield | 11-02 | **feat/new-round-type** (잠김 문구 수정) | 높음 |
| `UI/Loadout/GameFlowUiBootstrap.cs` | — | — | **feat/new-round-type** (RoundObjectiveHud 등록) | 높음 → 수정 안 함 |
| `UI/RoundObjectiveHud.cs` (신규) | MountainHS | 11-03 | **feat/new-round-type** 에만 있음 | 머지 전 수정 불가 |
| `UI/PlayerHud.cs` | MinSeong | 11-01 | 없음 | 보통 |
| `UI/WeaponSlotHud.cs` | pinkshield · MinSeong | 11-02 | 없음 | 보통 |
| `UI/UpgradeScreen.cs` · `UpgradeUiSkin.cs` | pinkshield | 11-02 | 없음 | 보통 |
| `UI/Options/Editor/OptionsPopupBuilder.cs` + 프리팹 | pinkshield | 11-02 | 없음 | 보통 (프리팹 재생성) |
| `UI/Intro/*` · `UI/Ending/*` + 씬 | MinSeong | 11-02 | 없음 | 높음 (씬 재생성) |
| `Boss/Tyranopode/TyranopodeAnalysisHud.cs` | MinSeong | 11-01 | 없음 (weakpoint-feedback 은 develop 에 머지됨, 다른 파일) | 낮음 |
| `Player/Effects/PlayerReviveEffect.cs` | MinSeong | 10-27 | **feat/MS** (11-03 확인) | 높음 → PR7 은 feat/MS 머지 후 |
| `Core/Events/GameEvents.cs`, `StreamingAssets/Tables/*.tsv` | 공용 | — | new-round-type · weakpoint-feedback | **수정 안 함** |

작업 시작 직전마다 다시 확인한다:
```
git fetch && git diff --name-only origin/develop...origin/<브랜치> -- Assets/Scripts/UI
```

---

## 2. 설계 원칙

1. **스타일은 새 파일에 모은다** (충돌 0)
   - `Scripts/UI/Theme/UiTheme.cs` — 색·글꼴 경로·크기·간격 상수 (static)
   - `Scripts/UI/Theme/UiThemeKit.cs` — 공통 부품 생성기: `Band`(사라지는 띠), `Marker`(붉은 표식), `Line`, `Gauge`(6px 게이지), `KeyCap`, `TextButton`(MainMenuButtonFx 재사용), `SectionHeader`
   - 각 HUD 는 자기 색 상수 대신 `UiTheme` 을 참조. `HudFactory` 는 기존 API 를 그대로 두고 새 부품은 `UiThemeKit` 에만 추가
2. **기존 파일은 "그리는 코드"만 바꾼다**
   - 수정 범위: `Build()` / `Create*()` 같은 레이아웃·색 코드
   - 그대로 둠: `Update`·이벤트 구독·상태 판단 로직, public 메서드·프로퍼티, 클래스·파일 이름, `[SerializeField]` 이름 (프리팹·씬 참조가 깨지지 않게)
   - 표시 문구는 바꾸지 않는다. 예외: 보상 제목 `VICTORY` → `MISSION ACCOMPLISHED` (UpgradeScreen 한 줄)
3. **씬(.unity)은 손으로 고치지 않는다**
   - 런타임에 코드로 만드는 UI(전투 HUD·토스트·무기 선택·강화·게임 오버)는 코드만 바꾸면 된다
   - 빌더로 만드는 것(OptionsPopup 프리팹, IntroCrawl·Ending·MainMenu 씬)은 **빌더 코드만 수정**하고, 프리팹·씬 재생성은 담당자와 시간을 맞춰 **별도 커밋 하나**로
4. **에셋은 지우지 않는다**
   - SCI-FI UI Pack, `GameFlowUiSkin.asset`, `UpgradeUiSkin.asset` 은 그대로 둔다. 새 코드가 스프라이트를 쓰지 않을 뿐
   - 안 쓰게 된 에셋 정리는 모든 PR 이 머지된 뒤 마지막 PR 에서
5. **무기 아이콘은 같은 이름으로 덮어쓴다**
   - `Resources/UI/WeaponIcons/{pistol, assault_rifle, rifle, smg, shotgun, sniper, rocket_launcher, grenade_launcher}.png` 를 흰색 PNG 로 교체
   - `.meta` 는 그대로 (GUID·임포트 설정 유지) → 코드·테이블 수정 없음
6. **공용 핫스팟은 건드리지 않는다**: `GameEvents.cs`, 테이블 TSV, `GameFlowUiBootstrap.cs`
7. **유지하는 것**: 인트로 스크롤(크롤) 문구 폰트는 기존 **Orbit**. 튜토리얼 HUD 는 이미 같은 스타일이라 손대지 않음

---

## 3. 작업 단위 (PR 순서)

PR 은 작게 나누고 develop 에 자주 머지한다. 각 PR 을 시작하기 전에 develop 을 받아 온다.

| # | 범위 | 건드리는 파일 | 선행 조건 |
|---|---|---|---|
| 1 | 테마 기반 + 무기 아이콘 | `UI/Theme/*` (신규), `Resources/UI/WeaponIcons/*.png` | 없음 |
| 2 | 전투 HUD | `PlayerHud`, `WeaponSlotHud`, `AimReticleHud`, `InteractPromptHud`, `DamageVignetteHud`(색만) | PR1 |
| 3 | 알림·카드 | `SupplyInfoCardHud`, `RoundSupplyToastHud`, `UnlockToastHud` | PR1 |
| 4 | 스테이지 사이 화면 | `UpgradeScreen`(보상·강화), `GameOverPanel` | PR1 |
| 5 | 무기 선택 + 라운드 목표 | `LoadoutPanel`, `RoundObjectiveHud` | **feat/new-round-type 머지 후** |
| 6 | 옵션 | `OptionsPopupBuilder` + 프리팹 재생성 | PR1 · pinkshield 와 시간 맞춤 |
| 7 | 보스·부활 | `TyranopodeAnalysisHud`, `PlayerReviveEffect`(글자만) | PR1 |
| 8 | 인트로·엔딩 | `IntroSceneBuilder`·`EndingSceneBuilder` 의 안내·로고 부분 (크롤 폰트 유지) + 씬 재생성 | PR1 · MinSeong 과 시간 맞춤 |
| 9 | 정리 | 안 쓰는 스킨 스프라이트 참조 제거 | 1~8 머지 후 |

PR 2·3·4·7 은 서로 파일이 겹치지 않아 순서와 상관없이 병렬로 진행할 수 있다.

진행 (11-03): PR1 테마·아이콘 ✔ · 메인 메뉴 로고 + 씬 재생성 ✔ · PR2 전투 HUD ✔ · PR3 알림·카드 ✔ (develop #107 받아 옴, UI 파일 겹침 없음) · PR4 보상·강화·게임 오버 ✔ (UpgradeScreen 은 TMP 로 전환, 튜토리얼 강화 체험도 같은 화면) · PR7 중 보스 분석 HUD ✔ (부활 연출은 feat/MS 머지 후)

---

## 4. 시안에서 바뀐 결정

- **라운드 표시**: 시안의 좌상단 "STAGE · ROUND / 남은 적" 은 철회한다. feat/new-round-type 의 `RoundObjectiveHud`(위쪽 가운데)를 새 스타일로 입힌다 (PR5). 라운드 보급 토스트는 그 아래로 내려 겹치지 않게 한다
- **보스 체력 막대**: `RoundObjectiveHud` 주석에 "보스전은 보스 체력 막대가 위쪽을 쓴다" 고 되어 있다. 누가 만드는지 확인한 뒤 시안 08 의 스타일로 맞춘다
- **보상 제목**: `MISSION ACCOMPLISHED`. UpgradeScreen 은 `victoryTitle` 스프라이트가 있으면 그것을 먼저 쓰므로, 스프라이트 대신 텍스트로 그린다
- **인트로 스크롤 문구**: Orbit 유지 (하단 진행·건너뛰기·배속 안내만 새 스타일)
- **라운드 목표 HUD (11-03 갱신)**: feat/new-round-type 의 최신 커밋에서 좌측 상단·0.8배·처치 막대로 바뀌었다. 시안 12 와 전투 HUD(01)를 그 위치에 맞췄고, 라운드 보급 토스트는 다시 위쪽 가운데에 둔다
- **게임 이름 = HIVE PURGE**, `TEAM EVEN` 은 제목 아래 작게 (시안 11·00·07·09). 코드에서 바꿀 곳:

  | 위치 | 지금 | 바꿀 값 | PR |
  |---|---|---|---|
  | `MainMenuUIBuilder.LogoText` | `TEAM EVEN` | `HIVE PURGE` + 붉은 선 옆 작은 `TEAM EVEN` 줄 추가 | 메인 메뉴 PR (메인 메뉴 씬 재생성 포함) |
  | `EndingCreditsText.logo` | `TEAM EVEN` | `HIVE PURGE` + 아래 작은 `TEAM EVEN` | PR8 |
  | `EndingCreditsText` 크레딧 제목 `Heading = "TEAM EVEN"` | 유지 (팀 크레딧) | — | — |
  | `IntroCrawlText.logo` (인트로 3D 로고) | `TEAM EVEN` | 유지 (팀 소개 로고, 11-03 결정) | — |
  | `ProjectSettings.productName` | `3d` | `HIVE PURGE` 제안 (창 제목·빌드 파일 이름) — ProjectSettings 는 충돌이 잦으니 단독 커밋 | 별도 |

---

## 5. PR 마다 확인할 것

- [ ] 컴파일 오류 없음, 기존 EditMode·PlayMode 테스트 통과
- [ ] 해당 UI 가 나오는 테스트 씬에서 직접 확인 (1920×1080, 16:10, 21:9)
- [ ] `[SerializeField]` 이름·public API 변경 없음 (`git diff` 로 확인)
- [ ] `.meta` 파일 변경 없음 (새 파일 추가 제외)
- [ ] 씬·프리팹 변경은 6·8번 PR 의 재생성 커밋에만 있음
