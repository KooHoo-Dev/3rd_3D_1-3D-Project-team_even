# 외계 행성 대기 (청회색 포자 안개) 적용 가이드

> 목표: 벌레 괴물이 연기 속에서 걸어 나오는 느낌. 약 85m 부터 수평선이 안 보이고, 보급 빛기둥만 안개를 뚫고 보인다.
> 하늘: 별과 성운이 보이는 몽환적인 밤하늘에 거대한 달이 떠서 플레이어를 내려다본다 (가운데 어두운 구덩이 + 붉은 테두리 = 눈동자).
> 플레이가 불편하지 않도록 하늘만 어둡고, 달빛·환경광은 밝게 둔다.
> 테스트 씬: `Assets/Scenes/MS/Scenes/TestMap_V1.unity` → 확정 후 메인 씬에 같은 메뉴로 적용.

## 적용 방법

1. 대상 씬을 연다 (저장된 씬이어야 함)
2. `Tools > HybridShooter > Atmosphere > 현재 씬에 외계 대기 적용 (청회색 안개)`
3. Generate Lighting 이 자동으로 돈다 (라이트맵 없음, 환경광·반사만 → 몇 초) → 씬 저장

다시 실행해도 안전하다. 이미 있는 Volume Profile·프리팹은 덮어쓰지 않고, 프리셋 값만 다시 씬에 반영한다.

## 무엇이 바뀌나

| 대상 | 내용 | 범위 |
|---|---|---|
| RenderSettings | Exponential² 안개, 밤하늘(별·성운·거대한 달), Gradient 환경광 | 그 씬만 |
| Directional Light | 차가운 달빛. 하늘의 달이 이 빛의 방향에 그려진다 → 그림자 방향과 일치 | 그 씬만 |
| `AtmosphereRig` 프리팹 배치 | Global Volume(포스트 프로세싱) · 카메라 포스트 프로세싱 켜기 · 보급 빛기둥 교체 · 주변 연기 | 그 씬만 |
| Lighting Settings | `Assets/Prefabs/Atmosphere/Lighting/{씬이름}_Atmosphere.lighting` 를 새로 만들어 연결 | 그 씬만 |

### 건드리지 않는 공용 설정 (다른 작업자 영향 없음)

- URP 에셋 / 렌더러 (`Assets/Settings/*`): 그대로
- `Player.prefab` 카메라: 프리팹 수정 없음. `CameraPostProcessSwitch` 가 런타임에만 켜고, 비활성화 시 되돌린다
- `SupplyCrate.cs`: 수정 없음. `SupplyBeaconOverride` 가 `SupplyDroppedEvent` 를 받아 빛기둥 재질·크기만 바꾼다
- 기존 Lighting Settings / Lighting Data: 지금 MS·CY·GU 의 TestMap_V1 등 여러 씬이 Creepy_Cat 예제(`Example_01Settings.lighting`)와 그 Lighting Data 를 같이 쓰고 있다. 그대로 구우면 다른 씬에 영향이 가므로, 씬 전용 설정을 새로 만들어 연결한 뒤 굽는다

## 수치 조정

| 무엇 | 어디서 | 반영 |
|---|---|---|
| 안개 색·밀도, 별·성운·달, 환경광, 달빛, 연기 색 | `Assets/Prefabs/Atmosphere/AlienAtmospherePreset.asset` | 메뉴 다시 실행 |
| 달 위치 | 프리셋 `moonLightRotation` (x = 고도, y = 방위). 플레이어가 주로 바라보는 방향 위로 두면 '지켜보는' 느낌이 강하다 | 메뉴 다시 실행 |
| 눈동자 느낌 세기 | 프리셋 `moonEyeIntensity` (0 = 평범한 달, 1 = 노골적인 눈) | 메뉴 다시 실행 |
| 성운 띠 방향·폭, 별 반짝임 속도, 눈동자 위치·크기 | `Materials/AtmosphereSky.mat` | 즉시 |
| 톤매핑·색감·Bloom·비네트·그레인 | `Assets/Prefabs/Atmosphere/AlienAtmosphereVolume.asset` | 즉시 |
| 보급 빛기둥 색·높이·굵기 | `AtmosphereRig.prefab` 의 `SupplyBeaconOverride` | 다음 투하부터 |
| 빛기둥 맥동·윗부분 사라짐·원거리 최소 굵기 | `Materials/SupplyBeaconNoFog.mat` | 즉시 |
| 주변 연기 반경·양·크기 | `AmbientSmoke.prefab` 파티클 | 즉시 |

안개 밀도 기준 (Exponential², 보이는 비율 = exp(-(밀도×거리)²)):

| 밀도 | 40m | 60m | 80m | 사실상 안 보임 |
|---|---|---|---|---|
| 0.018 | 60% | 31% | 13% | 약 105m |
| **0.022 (기본)** | 46% | 18% | 5% | 약 85m |
| 0.028 | 28% | 6% | 1% | 약 65m |

## 연출 팁

- 몬스터 스폰 지점은 가시거리 경계(약 60~90m) 바로 바깥에 두면 실제로 연기 속에서 걸어 나온다
- 스폰 지역이나 골목에 더 짙은 연기가 필요하면 씬 뷰 중심을 그곳에 맞추고
  `Tools > HybridShooter > Atmosphere > 씬 뷰 중심에 연기 덩어리 배치` (24×24m `SmokeBank`)
- 보급 색: 수류탄 = 주황, 회복 = 청록-흰색 (청회색 안개와 겹치는 초록 대신)

## 메인 씬으로 옮길 때 체크리스트

- [ ] 메인 씬 담당자와 합의 (씬 RenderSettings·태양·Lighting Settings 가 바뀐다)
- [ ] 메인 씬을 열고 메뉴 실행 → 씬 저장
- [ ] 메인 씬에 다른 Global Volume 이 있으면 우선순위 확인 (`AtmosphereRig/GlobalVolume` priority = 10)
- [ ] 메인 씬 카메라가 `MainCamera` 태그인지 확인 (포스트 프로세싱·연기 추적이 `Camera.main` 기준)
- [ ] 외부 에셋 머티리얼 중 안개를 안 받는 셰이더가 있으면 그 오브젝트만 안개 속에서 튄다 → 먼 곳에서 확인
- [ ] (선택, 팀 합의 필요) URP 에셋 공용 설정: Color Grading Mode → HDR(톤매핑 품질↑), Shadow Distance 50 → 80(안개 경계까지 그림자)
