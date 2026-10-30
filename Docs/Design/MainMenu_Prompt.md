# 메인 메뉴 씬 UI 디자인·구현 프롬프트

> 아래 `---` 사이 전체를 Claude Code(또는 다른 AI 코딩 도구)에 그대로 붙여 넣어 사용한다.
> 결정 사항은 2026-10-28 기획 질의응답 결과를 반영했다.

---

## 역할과 목표

너는 Unity 6000.3(URP) 프로젝트 "3D 하이브리드 슈터 (Team Even)"의 UI/연출 담당 개발자다.
**게임 실행 → 로고(로딩) → 대치 장면 + PRESS ANY KEY → 메인 메뉴(게임 시작 / 옵션 / 게임 종료)** 흐름을 디자인하고 구현하라.
게임 콘셉트: *1인칭으로 몰려오는 군중을 쓸어내고, 3인칭으로 거대한 보스와 맞서는 하이브리드 슈터.*

작업 전에 반드시 읽을 것:
- `README.md`, `Docs/Conventions/TeamRules.md` (폴더·네임스페이스·커밋·씬 규칙)
- `Docs/Architecture/02-SystemInterfaces.md`, `Docs/Guides/02-API-사용법.md` (ServiceLocator / EventBus / DataManager)
- `Docs/Design/SoundSystemGuide.md` (SoundService 배치·볼륨 API)
- `Assets/Scripts/UI/UpgradeScreen.cs` (기존 UI 코드 스타일 참고: 표시 전용, Inspector 연결, 주석 밀도)

## 프로젝트 컨텍스트 (확인된 사실)

| 항목 | 내용 |
|---|---|
| 엔진 | Unity 6000.3, URP 17.3, uGUI 2.0 + TextMeshPro, Input System 1.19, Cinemachine 3.1.7, Timeline, DOTween + UniTask |
| 네임스페이스 | `HybridShooter.<Scripts 아래 폴더 경로>` → 메뉴 코드는 `HybridShooter.UI.MainMenu` (`Assets/Scripts/UI/MainMenu/`) |
| 데이터 | `DataManager.EnsureLoaded()` 가 StreamingAssets/Tables TSV 를 **동기** 로드. `IsLoaded`, `Loaded` 이벤트 제공 |
| 사운드 | `ISoundService` (ServiceLocator 등록, **씬마다 1개**, DontDestroyOnLoad 아님). `SetVolume(SoundCategory, float)`, `MasterVolume`, BGM 크로스페이드. 믹서: `Assets/Resources/Audio/GameMixer.mixer` |
| SoundCategory | `Bgm, Sfx, Monster, Ui` (`Core/Data/GameEnums.cs`) |
| 감도 기본값 | `PlayerDef.LookSensitivity` (테이블 값) |
| 입력 | `Assets/InputSystem_Actions.inputactions` — `Player`, `UI` 액션맵 존재. EventSystem 은 `InputSystemUIInputModule` 사용 |
| 품질 | QualitySettings: `Mobile`, `PC` 2단계 |
| 플레이어 프리팹 | `Assets/Scenes/MS/Prefabs/Player.prefab` |
| 몬스터 프리팹 | `Assets/Resources/Prefabs/Monsters/` — GiantSlug, Letalobrach, Muscomorph_Air, Muscomorph_Ground, Pardathrox (보스: `Assets/Resources/Boss/Clypeosaurus/Prefab/Clypeosaurus.prefab`) |
| 지형 | `Assets/Scenes/HS/MonsterIntegrationLab/Prefabs/Terrain And Obstacles.prefab`, `Assets/Scenes/GU/Map/TestMap_V1.unity` 참고 |
| UI 에셋 | `Assets/ExternalAssets/SCI-FI UI Pack Pro/` (아래 매핑표) |
| 빌드 씬 | 현재 `Assets/Scenes/TestScenes/Monster Player Integration.unity` 1개 → 게임 시작 시 로드할 씬 (Inspector 에서 씬 이름 지정 가능하게) |

## 씬 구성

```
Assets/Scenes/Boot.unity       ← 빌드 인덱스 0. 로고 + 로딩
Assets/Scenes/MainMenu.unity   ← 빌드 인덱스 1. 대치 디오라마 + 메뉴 UI
(게임 씬)                       ← 빌드 인덱스 2. 지금은 Monster Player Integration
```

## 단계별 사양

### 1. Boot — 로고 (로딩 화면 대체)
- 검은 배경(`Camera` Solid Color #000 또는 전체 화면 Image) 정중앙에 로고.
- **로고 이미지는 아직 없음** → TMP 텍스트 로고로 임시 제작 (폰트: `Common/Fonts/Aldrich-Regular SDF`, Cyan 발광 Outline/Glow). `[SerializeField] Sprite logoSprite` 가 할당되면 Sprite 로 대체되도록 만든다.
- 연출: 페이드인(0.6s) → 짧은 SF 글리치/스캔라인 1회 → 유지 → 페이드아웃(0.6s).
- 표시 중 실제 로딩:
  1. `DataManager.EnsureLoaded()` 호출 (`LastReport` 에 오류가 있으면 Debug.LogError, 진행은 계속)
  2. `SceneManager.LoadSceneAsync("MainMenu")` 를 `allowSceneActivation = false` 로 미리 로드
  3. **최소 표시 시간(기본 2.5s)** 과 로딩 완료(progress ≥ 0.9) 둘 다 충족 시 페이드아웃 후 활성화
- 로고 하단 작은 로딩 인디케이터: `Common/Scripts/JustRotate.cs` 로 회전하는 원형 아이콘 (선택).
- 아무 키 입력 시 최소 시간만 단축 (로딩 완료 전엔 넘어가지 않음).

### 2. MainMenu — 대치 장면 (연출용 디오라마)
- 실제 게임 시스템을 구동하지 않는 **연출 전용** 씬. 플레이어·몬스터 프리팹을 배치하되 AI / 입력 / 물리 / 전투 스크립트는 비활성화하거나, 모델+Animator 만 있는 디스플레이용 변형 프리팹(`*_Display`)을 만든다. CrowdCoordinator·DataManager 기반 스폰 로직에 의존하지 않는다.
- 구도: **플레이어 등 뒤 오버숄더(우측 어깨)** 시점. 플레이어는 화면 좌하단~중앙, 몬스터 무리는 화면 **우측~중앙 원경**까지 빽빽하게(전열은 크게, 후열은 많이·작게). 화면 좌측 1/3 은 메뉴를 위해 비교적 비워 둔다.
- 플레이어: 무기를 든 조준 대기 Idle 루프.
- 몬스터: 종류를 섞어 20~40마리. 각 개체 Idle/위협 애니메이션을 **랜덤 시작 오프셋·재생 속도(0.9~1.1)** 로 재생해 동기화된 느낌 제거. 비행형(Muscomorph_Air)은 상공에서 천천히 호버링. 원경에 보스 실루엣(Clypeosaurus)을 안개 속에 두는 것도 고려.
- 카메라: Cinemachine 3 카메라. 느린 좌우 흔들림/돌리(주기 10~15s)와 숨쉬기 수준 Noise.
- 분위기: 어두운 톤, 안개(URP Fog), 몬스터 쪽 역광 림라이트, Post-processing(Bloom, Vignette, 약한 Color Grading).
- 환경 디테일: 바닥 안개/먼지 파티클, 바람에 날리는 재·불씨, 몬스터 눈 발광(Emission), 멀리서 간헐적 섬광.
- BGM: 메뉴 전용 BGM 을 `ISoundService` 로 재생 (사운드 키는 SoundTable 에 추가 필요 시 TODO 로 남김).
- 몬스터 프리팹 모델이 아직 없는 종류는 `TestMonster.prefab` 으로 자리만 잡아 두고 교체 가능하게.

### 3. PRESS ANY KEY 단계
- 메인 메뉴 씬 진입 직후에는 **대치 장면 + 로고(작게, 좌상단) + 하단 중앙 "PRESS ANY KEY"** 만 표시 (텍스트 깜빡임: 알파 0.3↔1, 1.2s 주기).
- 아무 키/마우스 클릭/패드 버튼 → 메뉴 UI 슬라이드인(좌측에서 0.4s, 버튼 순차 딜레이 0.06s), 카메라는 살짝 플레이어 쪽으로 이동.
- 이후 메뉴로 돌아오는 경우(게임 → 메인 메뉴)는 이 단계를 건너뛸 수 있게 static 플래그로 처리.

### 4. 메인 메뉴 UI
레이아웃 (1920×1080 기준, Canvas Scaler: Scale With Screen Size, Match 0.5):
```
┌──────────────────────────────────────────┐
│ [LOGO]                                   │
│                        👹 👹 👹 👹 👹       │
│ ▶ GAME START          👹 👹 👹 👹 👹 👹     │
│   OPTIONS                👹 👹 👹          │
│   QUIT            🧍(등)                  │
│                                          │
│ v0.1.0                                   │
└──────────────────────────────────────────┘
```
- 좌측 세로 정렬, 좌측 여백 ~120px, 버튼 간격 ~24px. 메뉴 뒤에 좌측→우측으로 투명해지는 어두운 그라데이션 패널을 깔아 가독성 확보.
- 컬러: **V2 Cyan 기본 + Yellow 강조**. 기본 버튼은 Cyan, 호버/선택된 버튼과 GAME START 는 Yellow.
- 좌하단 빌드 버전(`Application.version`), 우하단 작은 조작 힌트(`[Enter] 선택  [Esc] 뒤로`).
- 키보드/패드 내비게이션: 첫 선택 = GAME START, Selectable Navigation Vertical, Esc = 팝업 닫기 / 메인에선 종료 팝업.
- 버튼 사운드: 호버/클릭 효과음을 UI 카테고리로 재생.

**호버 연동 연출** (배경이 버튼에 반응):
| 호버 버튼 | 배경 반응 |
|---|---|
| GAME START | 플레이어가 무기를 들어 조준(Aim 포즈), 카메라 살짝 줌인 |
| OPTIONS | 카메라가 약간 옆으로 이동, 장면 채도 살짝 낮춤 |
| QUIT | 몬스터 일부 포효(애니+사운드), 화면 가장자리 붉은 비네트 |
- 호버가 빨리 바뀌어도 튀지 않도록 모든 반응은 보간(DOTween)하고 이전 트윈은 Kill.

### 5. 게임 시작 전환 — 카메라 1인칭 진입
1. 메뉴 UI 슬라이드아웃(0.3s), 입력 잠금
2. 카메라가 플레이어 머리(눈 높이)로 빠르게 파고들며 FOV 변화 (0.8~1.0s) → 1인칭 시점에서 몬스터 무리를 정면으로 봄
3. 순간 암전(또는 Cyan 스캔라인 와이프) → `LoadSceneAsync(gameSceneName)`
4. 게임 씬은 1인칭 군중전으로 시작하므로 시점이 자연스럽게 이어진다.
- Cinemachine 카메라 2대(메뉴용 / 1인칭 진입용) Priority 전환 + Blend 로 구현 권장.

### 6. 옵션 (팝업)
기반 프리팹: `SCI-FI UI Pack_V2 Pro(Cyan)/Prefabs/Prefabs_PanelAndPopup/Popup_Settings.prefab` 를 복제해 수정. 탭은 `Toggle_PageTag` 로 구성.

| 탭 | 항목 | 컨트롤 (V2 Prefabs_Option) | 적용 |
|---|---|---|---|
| 사운드 | 마스터 / BGM / 효과음 / 몬스터 / UI | `Slider` (0~100%) | `ISoundService.MasterVolume`, `SetVolume(SoundCategory, v)` 즉시 적용 |
| 조작 | 마우스 감도(1인칭) / 감도(3인칭) / Y축 반전 | `Slider`, `Switch` | `PlayerDef.LookSensitivity` × 배율(0.1~3.0, 기본 1.0) |
| 조작 | 키 안내 | 읽기 전용 목록 | Input Actions 의 바인딩 표시 문자열을 읽어 자동 생성 (리바인딩은 하지 않음) |
| 그래픽 | 해상도 / 화면 모드 / 품질 / V-Sync / 프레임 제한 | `Dropdown`, `Switch` | `Screen.SetResolution`, `FullScreenMode`, `QualitySettings.SetQualityLevel`(Mobile/PC), `vSyncCount`, `targetFrameRate`(60/120/144/무제한) |

- 하단 버튼: **적용 / 기본값 복원 / 닫기**. 사운드는 슬라이더 이동 즉시 미리듣기, 그래픽은 [적용] 시 반영.
- **저장 구조**: `GameSettings` (순수 C# 데이터 + `PlayerPrefs` 저장/로드, 키 접두사 `Settings.`)를 `Assets/Scripts/Core/Settings/` 에 두고,
  - 씬마다 새로 생기는 `SoundService` 가 시작 시 저장된 볼륨을 적용하도록 연결 (SoundService 수정 최소화: 별도 `SettingsApplier` 컴포넌트나 `GameSettings.Changed` 이벤트 구독 방식 중 택1, 사유를 주석으로)
  - 플레이어 카메라가 감도 배율·Y반전을 읽도록 연결 지점을 TODO 로 명시 (Player 담당과 협의 사항)
- 옵션은 일시정지 메뉴에서도 재사용할 수 있도록 메인 메뉴에 의존하지 않는 독립 프리팹(`Prefabs/UI/OptionsPopup.prefab`)으로 만든다.

### 7. 게임 종료
- `Popup_Quit.prefab`(V2) 기반 확인 팝업: "게임을 종료하시겠습니까?" [종료] [취소]
- 팝업 뒤 배경 Dim + 약한 블러(가능하면). [종료] → `Application.Quit()` (에디터에서는 `EditorApplication.isPlaying = false`).

## UI 에셋 매핑 (SCI-FI UI Pack Pro)

| 용도 | 에셋 |
|---|---|
| GAME START 버튼 | `V2 Cyan/Prefabs/Prefabs_Button/Btn_StartMenuYellow.prefab` (호버 시 강조 애니메이터 `Common/Animation/Btn_Start.controller`) |
| OPTIONS 버튼 | `Btn_SettingMenuCyan.prefab` (호버 시 Yellow 변형 `Btn_SettingMenuYellow`) |
| QUIT 버튼 | `Btn_MainMenuCyan.prefab` 또는 `Btn_Rectangle02Cyan.prefab` (호버 시 Yellow) |
| 옵션 팝업 | `V2 Cyan/Prefabs/Prefabs_PanelAndPopup/Popup_Settings.prefab` |
| 종료 팝업 | `V2 Cyan/Prefabs/Prefabs_PanelAndPopup/Popup_Quit.prefab` |
| 슬라이더·스위치·드롭다운·탭 | `V2 Cyan/Prefabs/Prefabs_Option/` — `Slider`, `Switch`, `Dropdown`, `Toggle_PageTag`, `Checkbox` |
| 슬라이더 값 표시 | `Common/Scripts/TMSliderValuePass.cs` |
| 닫기 버튼 | `Btn_ClosePage.prefab` |
| 로딩 회전 아이콘 | `Common/Scripts/JustRotate.cs` + `Common/White Icons` |
| 장식 라인·타이틀 | `V2 Cyan/Prefabs/Prefabs_Widget/LargeTitle1`, `SceneTopTitle*`, `Textures/DemoScene_Cyan/Decorations` |
| 폰트 | 영문 타이틀 `Aldrich-Regular SDF`, 버튼 `Play-Bold SDF`, 본문 `Play-Regular SDF`. **한글이 필요한 텍스트는 한글 TMP 폰트 에셋을 Fallback 으로 연결** |

- 에셋 원본 프리팹은 수정하지 말고 **Prefab Variant** 또는 복제본을 `Assets/Prefabs/UI/MainMenu/` 에 만든다.
- 데모 씬(`Demo Scene V2(Cyan).unity`)을 열어 원래 쓰임새를 확인한 뒤 고른다. 위 매핑은 이름 기준 추정이므로 실제 모양이 맞지 않으면 더 적합한 프리팹으로 바꾸고 이유를 남긴다.

## 스크립트 구성 (제안)

```
Assets/Scripts/UI/MainMenu/
├─ BootSequence.cs          로고 연출 + 데이터 로드 + MainMenu 비동기 로드
├─ MainMenuController.cs    상태 머신: PressAnyKey → Menu → (Options | QuitConfirm) → StartingGame
├─ MainMenuButtonFx.cs      버튼 호버/선택 시 색·스케일·사운드, 배경 반응 이벤트 발행
├─ MenuBackdropDirector.cs  호버 연동 연출(카메라·비네트·애니메이터 트리거)과 1인칭 진입 연출
├─ DisplayActor.cs          디오라마 캐릭터용: 랜덤 애니 오프셋·속도, 트리거 재생
└─ QuitConfirmPopup.cs
Assets/Scripts/UI/Options/
├─ OptionsPopup.cs          탭 전환, 각 컨트롤 ↔ GameSettings 바인딩
└─ KeyGuideList.cs          InputActionAsset 에서 바인딩 표시 문자열로 목록 생성
Assets/Scripts/Core/Settings/
└─ GameSettings.cs          설정값 + PlayerPrefs 저장/로드 + Changed 이벤트
```

## 구현 규칙
- 기존 코드 스타일을 따른다: `[SerializeField] private`, 한국어 XML 요약 주석, `sealed` 클래스, 필드 Tooltip.
- 연출 수치(시간·거리·FOV·딜레이)는 하드코딩하지 말고 Inspector 노출.
- `Time.timeScale` 에 영향받지 않도록 UI 트윈은 unscaled time 사용.
- 메인 메뉴에서는 커서 표시·잠금 해제, 게임 시작 시 게임 씬이 다시 잠금.
- 다른 담당자 시스템(SoundService, Player, Monster)은 **필요 최소한만** 수정하고, 수정한 경우 이유를 정리한다. 현재 사운드 시스템은 리팩터링 중(`Docs/Design/SoundSystemRefactor_v3.md`)이므로 충돌 가능 지점을 먼저 확인한다.
- 개인 작업 씬 규칙: 완성 전에는 `Test_MainMenu` 로 작업하고, 완료 후 `Boot` / `MainMenu` 로 정리.

## 작업 순서
1. UI 팩 데모 씬 확인 → 사용할 프리팹 확정 (매핑표 갱신)
2. `GameSettings` + 옵션 팝업 (독립적으로 테스트 가능)
3. MainMenu 디오라마 배치(지형·플레이어·몬스터·조명·안개·포스트프로세싱·카메라)
4. 메뉴 UI 레이아웃 + 키보드/패드 내비게이션 + PRESS ANY KEY
5. 호버 연동 연출 + 환경 파티클
6. 게임 시작 1인칭 진입 전환 + 종료 팝업
7. Boot 씬(로고·로딩) + Build Settings 순서 정리 (Boot=0, MainMenu=1, 게임=2)

## 완료 기준
- [ ] 빌드 실행 시 검은 화면 로고 → 메뉴 장면까지 끊김 없이 이어지고, 로고는 최소 2.5s 이상 표시
- [ ] 대치 장면에서 몬스터 애니메이션이 서로 엇갈려 자연스럽고, 메뉴가 장면 주요부를 가리지 않음
- [ ] PRESS ANY KEY → 메뉴 슬라이드인, 마우스·키보드·패드 모두 조작 가능
- [ ] 각 버튼 호버 시 배경 반응이 보간되어 나타나고 빠르게 옮겨 다녀도 튀지 않음
- [ ] 옵션 값이 즉시/적용 시 반영되고 재실행 후에도 유지되며, 게임 씬의 볼륨·감도에도 적용됨
- [ ] 게임 시작 시 1인칭 진입 연출 후 게임 씬 로드, 종료는 확인 팝업을 거침
- [ ] 1920×1080 / 2560×1440 / 1280×720 에서 레이아웃 깨짐 없음
- [ ] Console 에 에러·경고 없음 (UI 팩 자체 경고 제외)

## 구현 전에 확인할 질문 (모르면 기본값으로 진행하고 TODO 기록)
- 게임 시작 시 로드할 최종 게임 씬 이름 (기본: `Monster Player Integration`)
- 메뉴 BGM / 버튼 효과음 / 포효 사운드 키 (없으면 SoundTable 추가 항목 목록만 작성)
- 게임 타이틀 문구 (로고 텍스트)

---

## 구현 진행 (2026-10-28)

### 확정 사항
| 항목 | 결정 |
|---|---|
| 씬 생성 | 에디터 빌더 스크립트 (`Tools > MainMenu > ...`). 다시 실행하면 새로 만든다 |
| 씬 위치 | 작업 중 `Assets/Scenes/GU/Test_MainMenu.unity` → 완료 후 `Assets/Scenes/BuildScenes/Boot`·`MainMenu` |
| 로고 문구 | `TEAM EVEN` |
| 진행 | 단계별 확인 후 커밋 |

### 매핑 변경 (데모 프리팹 확인 결과)
| 용도 | 위 매핑 | 변경 | 이유 |
|---|---|---|---|
| 옵션 탭 | `Toggle_PageTag` | `Btn_SettingsMenuCyan/Yellow` 스프라이트로 만든 Toggle | PageTag 는 40px 점(페이지 표시)이라 글자를 넣을 수 없음 |
| 옵션 팝업 | `Popup_Settings.prefab` 복제 | `Popup_Settings.png` (9-slice) 배경 + 팩 컨트롤 중첩 프리팹 | 프리팹은 데모 내용(Music·Vibration·Language)이 고정 배치 |
| 메뉴 버튼 3개 | StartMenu / SettingMenu / MainMenu 섞어 사용 | 셋 다 `Btn_MainMenuCyan` (선택·GAME START 는 `Btn_MainMenuYellow`) — 4단계에서 확정 | StartMenu(524×264)는 카드형 큰 상자, SettingMenu 는 탭 모양. MainMenu(609×134)가 좌측 세로 목록용 |
| 한글 폰트 | 팩 폰트에 Fallback | 본문은 `NotoSansKR-Regular SDF` 직접 사용, 영문 제목만 `Aldrich` | 팩 폰트 에셋을 고치지 않기 위해. 현재 `Assets/StudySample/` 안에 있어 정식 폴더로 옮길 필요 (TODO) |

### 문서와 실제 코드가 다른 점
- **키 안내**: 플레이어는 `InputSystem_Actions` 가 아니라 `PlayerController.InputPlayerSettings` 의 키를 직접 읽는다 → `KeyGuideList` 도 그 기본값을 읽어 표시 (InputActions 의 Player 맵은 템플릿 그대로라 실제 키와 다름)
- **감도**: 플레이어는 `PlayerDef.LookSensitivity` 가 아니라 `PlayerController.mouseSensitivity`(0.12) × `aimSensitivity` 를 쓰고, 1인칭/3인칭 구분이 없다. `invertY` 는 Inspector 값. → `GameSettings` 에 1인칭·3인칭 배율과 Y 반전만 저장하고, 연결은 Player 담당과 협의 (TODO, `GameSettings.cs` 주석)
- **마스터 볼륨**: `ISoundService` 에 `MasterVolume` 이 없어 계약에 추가 (`SoundService` 는 원래 공개 속성이 있어 그대로, `NullSoundService` 만 1줄)

### 사운드 보완 (사운드 리팩토링 v3.1 재점검)
1. 옵션 볼륨 유지: `GameSettings` 가 `SceneManager.sceneLoaded` (OnEnable 뒤·Start 전)에서 새 씬 SoundService 에 저장값을 넣는다 → SoundService·씬 수정 없음
2. Reward 스냅샷 잔류: `SoundService.Start` 에서 Default 스냅샷으로 즉시 전환
3. 씬 전환 BGM 끊김: `SceneTransition.LoadAsync` — BGM 페이드아웃과 다음 씬 미리 로드를 함께 하고 둘 다 끝나면 활성화 (6·7단계에서 사용)

### 단계 현황
- [x] 1. 프리팹 확정 (위 매핑 변경)
- [x] 2. `GameSettings` + 옵션 팝업 — `Tools > MainMenu > Build Options Popup` → `Assets/Prefabs/UI/OptionsPopup.prefab`, `Create Test Scene` → `Test_MainMenu`
- [x] 3. 디오라마 — `Tools > MainMenu > Rebuild Diorama` (Test_MainMenu 의 Environment·Diorama·CM_Menu 만 다시 만듦). 씬에서 조정한 환경 값을 빌더에 반영
- [x] 4. 메뉴 UI + PRESS ANY KEY + 키보드/패드 내비게이션 — `Tools > MainMenu > Rebuild Menu UI` (Test_MainMenu 의 Canvas 만 다시 만듦, 임시 OPTIONS 버튼 제거)
- [x] 5. 호버 연동 연출 + 환경 파티클 — `Rebuild Diorama` 가 함께 만든다 (Environment 의 Atmosphere·Hover Volume, Diorama 의 `MenuBackdropDirector`)
- [ ] 6~7

### 3단계 디오라마 구성
- **디스플레이 프리팹** `Assets/Prefabs/MainMenu/Display/*_Display.prefab`: 게임 프리팹(`Resources/Prefabs/Monsters`, 보스, `MS/Prefabs/Player`)을 풀어서 Transform·렌더러·Animator·파티클만 남긴다 (게임 스크립트·물리·이미터 제거, RequireComponent 순서대로). 게임 프리팹이 없는 부포모프·덴솝테르는 에셋 팩 프리팹 사용 → `TestMonster` 자리 표시는 필요 없음
- **애니메이션**: 게임 컨트롤러는 파라미터로 움직이므로, 몬스터는 에셋 팩 컨트롤러로 바꾸고 `DisplayActor` 가 상태 이름을 직접 재생 (Idle 랜덤 시작 위치·속도 0.9~1.1, 비행형 호버링, `PlayThreat` 위협·포효). 플레이어는 `Player_AC` 그대로 (Locomotion 정지 = 소총 대기, `Aim` 파라미터로 조준 — 5단계)
- **무리**: 처음엔 고정 시드로 35마리를 부채꼴 고리에 흩뿌렸고, 2026-10-29 씬에서 직접 옮긴 배치를 `MainMenuDioramaBuilder.Placements` 표(종류·위치·방향)로 고정 — 무스코모프 지상 9(4~12m)·파르다트록스 4·레탈로브라크 3·자이언트 슬러그 1, 공중 무스코모프 4·덴솝테르 3 (24마리), 부포모프는 빠짐. 원경 50m 에 클리페오사우루스. 씬에서 다시 옮기면 표를 그 값으로 바꾼다. 오른쪽 원경(15.7, 2.2, 30.6)에 추락한 우주선 `Assets/Scenes/GU/Prefabs/Spaceship_Broken.prefab` (연출 전용 소품이라 디스플레이 프리팹 없이 그대로, 빌더 `ShipPosition`·`ShipRotation`). 바위는 고리 바깥(양옆·원경)
- **환경**: 인게임 맵(건물 단지)은 쓰지 않고, 인게임 씬(`Monster Player Integration`)을 기준으로 만든다. 땅 = 300m 바닥에 인게임 지형 레이어 텍스처(`Creepy_Cat .../Textures/Sand_01.png`, 30m 타일, `Assets/Materials/MainMenu/MainMenuGround.mat`, 콜라이더 없음) + 원경 안개 속 바위 3개(150m 이상, 씬에서 조정한 값). 하늘·조명 = 기본 스카이박스, 스카이박스 주변광, 흰색 Directional 1.2(인게임과 같은 방향, 그림자 Soft). 연출로 더한 것: 같은 방향 보조광 1.0, 몬스터 뒤 약한 역광 0.8, 원경 안개(지수² 0.022, 스카이박스 지평선과 비슷한 회청색 → 땅 끝이 하늘로 녹아들고 50m 보스는 실루엣). 볼륨(`Assets/Settings/MainMenuVolumeProfile`)은 인게임과 같은 Neutral 톤매핑에 Bloom 0.4·Vignette 0.28
- **카메라**: Main Camera 에 CinemachineBrain, `CM_Menu`(우측 어깨 오버숄더, FOV 45) + Noise(Handheld_normal_mild 약하게) + `MenuCameraSway`(12초 좌우 흔들림)
- 구도 수치는 `MainMenuDioramaBuilder` 상단 상수 (CameraPosition·CameraTarget·Crowd 표)
- 메뉴 씬에는 GameSoundRouter 가 없으므로 디오라마에 사운드 이미터가 붙지 않는다

### 4단계 메뉴 UI 구성
- **흐름** `MainMenuController`: PressAnyKey → Menu → (Options) → StartingGame. 게임에서 돌아올 때 `MainMenuController.SkipPressAnyKey = true` 면 바로 메뉴
- **PRESS ANY KEY**: 좌상단 로고(`TEAM EVEN`, Aldrich, 항상 표시) + 하단 중앙 "PRESS ANY KEY" 깜빡임(알파 0.3↔1, 1.2s). `InputSystem.onAnyButtonPress` 한 번 → 메뉴 슬라이드인(왼쪽 700px 밖에서 0.4s, 버튼 0.06s 순차) + 카메라가 플레이어 쪽으로 0.6m (`MenuCameraSway.SetBase`, 흔들림은 계속). 버튼은 다 들어온 뒤에 눌린다 (PRESS ANY KEY 를 누른 클릭·Enter 가 버튼까지 누르지 않게)
- **레이아웃** (1920×1080, Match 0.5): 버튼 487×107(원본 80%), 좌측 여백 120, 간격 24. 뒤에 왼쪽→오른쪽 투명해지는 검은 패널(폭 900, 알파 0.75, 텍스처 없이 `HorizontalFade` 정점 알파). 좌하단 `v{Application.version}`, 우하단 `[Enter] 선택    [Esc] 뒤로`. 수치는 `MainMenuUIBuilder` 상단 상수
- **버튼 색**: GAME START = `Btn_MainMenuYellow`, OPTIONS·QUIT = `Btn_MainMenuCyan` 에 선택(호버·Pressed) 스프라이트만 `Btn_MainMenuYellow_p` 로 → 기본 Cyan, 선택 Yellow. 팩의 `TMTextColorControl` 은 마우스에만 반응해 끄고 `MainMenuButtonFx` 가 선택 시 글자색·크기(1.04)를 보간
- **내비게이션**: 첫 선택 GAME START, 위아래 Explicit(끝에서 처음으로 돈다). 마우스 호버 = 선택. 빈 곳 클릭으로 선택이 비면 마지막 버튼을 다시 선택. 옵션을 닫으면 OPTIONS 선택
- **임시 동작 (6단계에서 교체)**: GAME START → 연출 없이 `SceneTransition.LoadAsync(gameSceneName)`, QUIT → 확인 없이 종료, 메인에서 Esc → 아무 것도 안 함 (종료 팝업 자리)
- 디오라마를 다시 만들면(`Rebuild Diorama`) 새 CM_Menu 를 메뉴 컨트롤러에 다시 연결한다
- 기존 코드 수정: `OptionsPopup.CancelPressed` 공개(메인 메뉴 Esc 판정 공유), `OptionsPopupBuilder` 공용 도우미 internal

### 5단계 호버 연출·분위기 구성
- **흐름**: `MainMenuController` 가 선택된 버튼을 `MenuBackdropDirector.SetHover` 로 넘긴다 (옵션 팝업이 열린 동안은 OPTIONS 반응 유지). 호버가 바뀔 때마다 모든 값을 지금 값에서 새 목표로 0.6s 보간하고 이전 트윈은 Kill → 빠르게 옮겨 다녀도 튀지 않음
- **GAME START**: 플레이어 `Aim` 0→1 (Player_AC 상체 조준 블렌드), FOV −4, 카메라 앞으로 0.25m
- **OPTIONS**: 카메라 오른쪽으로 0.7m, `Hover Volume (Options)` 가중치 1 (채도 −45, 노출 −0.15)
- **QUIT**: `Hover Volume (Quit)` 가중치 1 (붉은 비네트 0.45), 무리 중 4마리가 0.5s 안에 엇갈려 포효(애니+소리, 4초 쿨다운), 몬스터 발광 2.5→5배
- **카메라 이동 분리**: `MenuCameraSway` 에 `Offset` 추가 — 메뉴 진입 이동은 `SetBase`, 호버는 `Offset` 이라 서로 덮어쓰지 않음
- **몬스터 눈 발광**: 대부분의 몬스터 머티리얼은 Emission 맵(눈·발광부)이 이미 켜져 있어 `DisplayActor.emissionBoost`(기본 2.5, 플레이어는 1 — 소총만 발광)로 MaterialPropertyBlock 배율만 올린다. 부포모프·레탈로브라크는 Emission 이 꺼진 머티리얼이라 변화 없음
- **분위기** (Environment/Atmosphere):
  - GroundFog: 팩 `Smoke.png` 연기 덩어리(8~14m, 알파 0.18)가 바닥(z 7~37)에서 천천히 오른쪽으로 흐름 — `Assets/Materials/MainMenu/MainMenuFog.mat`
  - Embers: `T_Glow` 가산 블렌드 불씨(HDR 주황 → Bloom)가 바람 따라 오른쪽 위로 — `MainMenuEmber.mat`
  - DistantFlash: 무리 너머 안개 속 Point Light 가 4~10초마다 번쩍임 (35% 확률로 두 번 연속, 위치 4곳 중 무작위)
- 파티클 재질은 셰이더 GUI 를 거치지 않으므로 블렌드 값·키워드를 빌더가 직접 맞춘다 (`CrashedShipSmoke.mat` 와 같은 투명 설정)
- 볼륨 프로필: `Assets/Settings/MainMenuHoverOptionsProfile.asset`, `MainMenuHoverQuitProfile.asset` (Priority 1, 다시 만들 때마다 새로 씀)
- 수치는 `MenuBackdropDirector`·`DistantFlash` Inspector, 파티클·섬광 위치는 `MainMenuDioramaBuilder` (`FlashPoints`, `CreateAtmosphere`)
