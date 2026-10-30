# MapDesign 재설계 — Sci-Fi Base 기반 (작업 인계 문서)

2026-10-30 · 브랜치 `chore/MapDetailsAdding`

> **목적**: 회사 PC에서 진행한 조사·컨셉 설계를 집 PC에서 이어서 작업하기 위한 기록.
> **현재 상태**: 컨셉 설계만 끝났고, **파일 수정·구현은 아직 하나도 하지 않았다.** (이 문서만 추가됨)

---

## 1. 요청 배경

- 기존 맵(`Assets/Scenes/GU/MapDesign.unity`)이 **"너무 심심하다"** 는 평가를 받았다.
- `Assets/Sci-Fi Base` 에셋으로 맵을 새로 만든다. 참고 씬은 `Assets/Sci-Fi Base/Scenes/Scene_Standard.unity`이고, 도심 부분은 설계하지 않는다.
- 배경은 **불시착한 행성의 환경**이며, 네러티브 문서(`게임 네러티브 설정 문서.md`, @CSD)를 따른다.
- 맵 규모는 현재 MapDesign 크기를 유지한다.

### 설계 변경 이력

| 차수 | 내용 | 결과 |
|---|---|---|
| 1차 | 튜토리얼 → 스테이지 1 → 2 → 3, 구역 4개를 게이트로 연결 (사분면 U자 경로) | **폐기** |
| 2차 (팀장 요청) | ① 튜토리얼과 인게임 맵을 **분리** ② 인게임은 기존 맵처럼 **구역 구분 없이 한 지역**에서 플레이 | **채택** |
| 2차 보완 (본인) | **착륙장 주변에 엄폐물을 조금** 배치 | 채택 (4-3 참고) |

---

## 2. 조사 결과 (사실 확인된 내용)

### 2-1. 현재 MapDesign 씬 구성

- 씬 안에 `Map` 프리팹 인스턴스가 있다. 원본은 `Assets/Resources/Map/Map.prefab`(Creepy_Cat 키트)이고, 씬에서는 루트 위치가 (0, 0, 0)이다.
  - 이 프리팹은 **다른 씬에서도 쓴다**: `SupplyImprovement.unity`, `Scenes/TestScenes/Monster Player Integration.unity`.
  - 그래서 **Map.prefab은 수정하지 않는다.** 새 맵은 별도 프리팹으로 만든다.
  - MapDesign 씬에서는 이 프리팹의 건물 16개가 `m_RemovedGameObjects`로 제거된 상태다. 즉 지형과 바위 위주만 남아 있다.
- 씬의 게임플레이 오브젝트 (월드 좌표):

| 오브젝트 | 위치 | 컴포넌트 |
|---|---|---|
| Player | (69.6, -5.0, 24.0) | 플레이어 프리팹 |
| BossSpawnPoint | (-95, 0, 37) | `RoundBossEncounter`의 spawnPoint |
| RunFlow | — | `RunDirector`, `RoundSequencer` |
| MonsterWaves | — | `MonsterWaveDirector`, `MonsterPool` |
| Supply | (-69.2, 2.7, 14.3) | `SupplyDropper` |
| Reward | — | `KillRewardCollector`, `UpgradeManager` |
| Crowd Coordinator, SoundService, PlaytestAnalyzer, Lab Camera, Lab Directional Light | — | — |

### 2-2. 지형 (Terrain)

- 오브젝트: `Map/ReductedTerrain`. TerrainData는 `Assets/New Terrain.asset`이고 바이너리다.
- 히트맵 해상도 513, 스케일 0.5859375 → **크기 300m × 300m**, 높이 스케일 600.
- 월드 범위는 **X −115.5 ~ 184.5, Z −140.6 ~ 159.4**이고, 지면 높이는 대략 **y ≈ −5**다.
- 맵 중앙은 대략 **(35, −5, 9)**.
- **(구현 중 확인)** 지형 가장자리는 높이 20~60m의 산이다. 실제로 걸을 수 있는 평지는 대략 **X −78 ~ 147, Z −94 ~ 122**(약 225m × 215m)다. 아래 4장 좌표는 이 범위에 맞춰 다시 잡았다.
- **(구현 중 확인)** `Map.prefab` 원본 루트는 (73.6, 0, 23.8)에 있다. MapDesign 씬은 이 루트를 (0, 0, 0)으로 덮어쓴다. 그래서 지형 위치를 계산할 때는 루트를 기준으로 삼아야 한다.
- Map 프리팹 안에 NavMeshSurface가 있다. layerMask bits 649, NavMesh 데이터는 `NavMesh-ReductedTerrain*.asset`.

### 2-3. Sci-Fi Base 에셋

- **머티리얼은 이미 URP Lit**으로 되어 있다. shader guid `933532a4...`은 URP Lit.shader이고 117개 전부 해당된다. 프로젝트는 URP 17.3, Unity **6000.3.15f1**.
- 프리팹 경로: `Assets/Sci-Fi Base/Prefabs/*.prefab`, `Assets/Sci-Fi Base/Rocks/*.prefab`
- 지형 레이어: `Assets/Sci-Fi Base/Terrain/Layers/` — `Sand1~8`, `SandRock`
- 파티클: `Assets/Sci-Fi Base/Particles/Core_Particle1.prefab`
- 참고 씬 `Scene_Standard`의 분위기: 주황빛 갈색 먼지 안개(FogColor ≈ (0.59, 0.48, 0.40), Exp, density 0.01)에 화성 같은 사막 기지.
- 용도별 주요 프리팹:

| 용도 | 프리팹 |
|---|---|
| 착륙장·바닥 | `Hangar_Ground_Plate1~5`, `Platform_01~08`, `Platform_Edge_*`, `Ground_Plate*`, `Concrete_Ground*` |
| 엄폐물 | `Cover Panel1A~6D`, `Cover Frame Panel*`, `Barrier_01_*`, `Barrier_2m/4m`, `Barrier_Solid_01`, `Concrete_wall2_Barrier1~3`, `Container_01`, `Prop_Barrel1*`, `Prop1~11` |
| 외곽 방어선 | `Fence1/2*`, `Antenna_*`, `Antenna_Big`, `Solar_Panel1~4`, `Radar_Sphere` |
| 정제 플랜트 | `Tank_01~04`, `Tank_Stack_*`, `Pipe2_*`, `Pipe3_*`, `Generator_Set_01~03`, `Catwalk1*`, `Crane_01/02`, `Cables*`, `Fuel_Tank_01` |
| 굴착 분화구 | `Drill_Bot`, `Drill_Pillar`, `Drill_Top`, `Drill_Tube`, `Drill_Connector` |
| 외벽·경계 | `Wall_Large_01~15`, `Concrete_wall1/2*`, `Wall1A~C`, `Wall1_Gate`, `Wall_Hangar1*` |
| 바위 | `Rock_01`, `Rock_Group1`, `Rock_Shales_01~10`, `Stone_Big1`, `Stone_med1~6`, `Stone_small*` |
| 조명 | `Lamp_01~08`, `Lamp_Position`, `Lamp_Wall` |

### 2-4. 게임플레이 제약 (맵 설계에 영향)

- **몬스터 스폰**: 기본 구현은 `RingSpawnZoneProvider`(`Assets/Scripts/Core/Services/DefaultServices.cs`)다.
  - 플레이어 주변 **반경 35m 원 위**의 한 점(`GameConfig.SpawnRingRadius`)을 고른 뒤, 반경 5m 안의 NavMesh로 붙인다.
  - → 맵 전체가 **하나로 이어진 NavMesh**여야 한다. 좁은 막다른 길이 많으면 스폰 실패가 늘어난다.
- **보급 낙하**: `SupplyDropper`는 플레이어 기준 XZ 30~150m 거리에 떨어진다. 위치는 아래로 레이캐스트해 지면을 찾는다(groundMask).
  - → **넓은 지붕 구조물은 최소화**한다. 보급이 지붕 위에 떨어질 수 있기 때문이다.
- **라운드 흐름**: `RunDirector`는 1라운드 데이터를 계속 반복할 뿐이고, 스테이지 개념은 없다. 2차 설계는 구역 구분이 없으므로 이 부분과 충돌하지 않는다.
- 팀의 맵 작업 방식은 **에디터 빌더 스크립트**로 조립하는 것이다. 예시: `Assets/Scenes/GU/Map/Editor/CrashedShipBuilder.cs`(`Tools/Map/Build Crashed Ship`), `ObstacleColliderBuilder.cs`.
- 이미 있는 추락선 프리팹: `Assets/Scenes/GU/Prefabs/Spaceship_Broken.prefab`, `Spaceship_01.prefab`, `EngineSmoke.prefab`

---

## 3. 세계관 컨셉 — "먼저 내려왔다가 전멸한 연합군 굴착 전초기지의 잔해"

Sci-Fi Base는 콘크리트, 파이프, 탱크, 드릴 같은 **인공 시설 에셋**이다. 이것을 외계 행성에 두는 이유를 이렇게 정했다.

> 주인공보다 먼저 이 행성에 내려온 **연합군 선발대의 굴착 전초기지**가 있었다. 이들은 행성 중심부로 드릴을 박아 넣다가 행성이 살아있다는 걸 알게 됐고, 결국 항체에게 전멸했다. 기지 중앙에는 **대형 착륙장**이 남아 있어서, 구조선이 착륙할 수 있는 유일한 장소다.

- **"구조선이 착륙 가능한 지점"**은 중앙 착륙장이다. 엔딩 때 구조선이 이곳에 내려온다.
- **"행성 = 거대한 생명체"**는 굴착 분화구에서 드러난다. 드릴이 찌른 행성의 '살'이 보이는 곳이다.
- 톤은 메인메뉴(어두운 외계 행성, 청회색 + 붉은 포인트)와 맞춘다.

---

## 4. 인게임 맵 설계 (MapDesign.unity, 300m × 300m 유지)

### 4-1. 구조

가운데 넓은 광장이 있고, 그 둘레를 순환로가 감싼다. 바깥쪽에는 성격이 다른 지점 다섯 곳을 둔다.
- 지점 사이에는 **벽이나 게이트가 없다.** 지형과 소품만 자연스럽게 바뀐다.
- 맵 가장자리는 **절벽과 기지 외벽**으로 막는다.

```
            +Z (북)
  ┌──────────────────────────────────┐
  │ [굴착 분화구]        [정제 플랜트] │
  │  보스 등장            탱크·캣워크  │
  │       ╭──── 순환로 ────╮          │
  │[암석] │                │ [외곽    │
  │[황무지]│  (착륙장)      │  방어선]  │
  │       │  + 엄폐물 링    │          │
  │       ╰────────────────╯          │
  │           [추락 지점]              │
  │            시작 위치               │
  └──────────────────────────────────┘
            −Z (남)          외곽: 절벽 + 기지 외벽
```

### 4-2. 지점별 구성 (좌표는 대략적인 월드 XZ 중심)

| 지점 | 중심 좌표 | 연출 / 에셋 | 전투 공간 성격 |
|---|---|---|---|
| **착륙장 (중앙)** | (35, 9) | `Hangar_Ground_Plate*`와 `Platform_*`로 만든 원형 착륙장, 가장자리에 `Lamp_Position` | 가장 넓은 개활지. 대규모 군중전 + **엄폐물 소량** (4-3) |
| **추락 지점 (남서)** | (−49, −90) — 직접 배치 | `Spaceship_Broken` 잔해(연기 포함) + `Rock_*` | **플레이어 시작 위치**. 엄폐물이 적당한 출발 공간 |
| **외곽 방어선 (동)** | (125, 10) | 무너진 `Fence*`(x=140), `Barrier_*`, `Antenna_*`, `Solar_Panel*` | 낮은 엄폐물이 흩어진 중거리 사격 공간 |
| **정제 플랜트 (북동)** | (110, 95) | `Tank_*`, `Pipe2/3_*`, `Generator_Set_*`, `Catwalk1*`, `Crane_*` | 시야가 끊기고 높낮이가 있는 근접 기습 공간 |
| **굴착 분화구 (북서)** | (−40, 90) | 지형을 파서 만든 분화구 + 거대 `Drill_*` 탑 | **보스 등장 지점**. `BossSpawnPoint`를 (-95, 0, 37)에서 이쪽으로 옮긴다 |
| **암석 황무지 (서)** | (−60, 10) | `Rock_Group1`, `Rock_Shales_*`, `Stone_Big1` | 큰 바위 사이로 우회로가 생기는 공간 |

- **회피용 엄폐물**: 지점마다 2~5개를 둔다. 보스 장판이나 몬스터 돌진을 피하는 용도이며, 높이 2.4~3.2m로 몸 전체를 가린다.
- **순환로**: 중앙 (35, 9)을 중심으로 반경 약 65m에 폭 넓은 길을 한 바퀴 둔다. 처음 설계는 90m였지만 평지 범위에 맞춰 줄였다. 군중형 적을 몰고 돌며 싸우는 **카이팅 루프**용이다. 막다른 길과 좁은 병목은 만들지 않는다.

### 4-3. 착륙장 주변 엄폐물 (추가 요청 반영)

- **안쪽 반경 약 15m는 비워 둔다.** 구조선이 착륙할 자리이자 군중전 공간이다.
- **반경 20~30m 띠**에 엄폐물 **8~12개**를 **끊긴 원호** 형태로 배치한다.
  - 순환로 방향(동·서·남·북 4방향)으로는 **진입 통로를 열어 둔다.** 군중 NavMesh 흐름이 막히지 않게 하기 위해서다.
  - 높이는 **허리 정도(1~1.5m)**로 낮게 한다. 시야는 유지하고 몸만 가릴 수 있게 하기 위해서다.
- 사용할 에셋: `Cover Panel*`, `Barrier_01_*`, `Barrier_Solid_01`, `Concrete_wall2_Barrier*`, `Container_01`(1~2개만, 높은 엄폐용), `Prop_Barrel1*`
- 연출: 착륙장 장비가 흩어진 느낌으로, 정렬하지 않고 약간씩 회전시켜 놓는다.

### 4-4. 분위기·조명

- 붉은 갈색 먼지 안개 + 어두운 하늘 + **붉은 포인트 조명**(`Lamp_*`, 착륙장 유도등).
- (선택) 라운드가 올라갈수록 안개를 더 붉게 → "행성 경계 단계 상승" 연출.

---

## 5. 튜토리얼 맵 설계 (새 씬, 약 60m × 200m 선형 협곡)

- 씬 경로(기본안): `Assets/Scenes/GU/TutorialMap.unity`
- 한 방향으로만 걷는 짧은 동선이다. 단계마다 네러티브 용어를 그대로 쓴다.

| 순서 | 구간 | 배우는 것 |
|---|---|---|
| 1 | 추락 함선 옆 기상 | 이동 · 시점 |
| 2 | 잔해 협곡 | 사격, 첫 무기 "동력 공급 재개" |
| 3 | 첫 항체 조우 | 소수 몬스터와 전투, 생체 자원(바이오매스) 회수 |
| 4 | 보급 포드 공터 | 궤도 보급 포드, 업그레이드 |
| 5 | 전초기지 게이트 (`Wall1_Gate`) | 통과하면 인게임 씬으로 전환 |

- 단계별 트리거나 안내 문구 같은 **튜토리얼 진행 로직은 이번 범위에서 뺀다.** 맵에는 각 단계 위치에 **빈 오브젝트 마커**만 배치한다.

---

## 6. 구현 계획

### 6-1. 방식
- 에디터 빌더 스크립트를 새로 만든다. `CrashedShipBuilder.cs`와 같은 스타일이며, 위치는 `Assets/Scenes/GU/Map/Editor/`.
  - 인게임: `Tools/Map/Build Sci-Fi Map` (가칭 `SciFiMapBuilder.cs`)
  - 튜토리얼: `Tools/Map/Build Tutorial Map` (가칭 `TutorialMapBuilder.cs`)
- 프리팹 크기는 미리 알 수 없다. 그래서 `CrashedShipBuilder`처럼 **Renderer Bounds로 크기를 재서** 맞춰 배치한다.
- 배치 랜덤성은 `Random.InitState(고정 seed)`로 재현 가능하게 만든다.
- 결과물:
  - 새 프리팹 (예: `Assets/Scenes/GU/Map/SciFiMap.prefab`)
  - **기존 `New Terrain.asset`을 복사한 새 TerrainData** — 다른 씬에 영향이 없게. 분화구·절벽은 여기서 깎는다.
  - NavMeshSurface 재굽기 (`BuildNavMesh()`)
- MapDesign 씬에서는 기존 `Map` 인스턴스를 비활성화하고 새 맵으로 교체한다.
- 다른 씬(SupplyImprovement 등)은 건드리지 않는다.

### 6-2. 작업 순서 (다음에 할 일)
1. [x] `SciFiMapBuilder.cs` 작성 — 지형 복사·조각(외곽 절벽, 분화구, 순환로 평탄화) + 지형 레이어 칠하기
2. [x] 지점별 소품 배치: 착륙장 + 엄폐물 링 → 추락 지점 → 외곽 방어선 → 정제 플랜트 → 굴착 분화구 → 암석 황무지 (+ 지점별 회피용 엄폐물 2~5개)
3. [ ] 외곽 경계 벽·바위 배치, 조명·안개 설정
4. [ ] NavMesh 굽기 → 플레이 테스트(스폰 실패 여부, 보급 낙하 위치, 군중 흐름) → `BossSpawnPoint`·`Player` 위치 조정
5. [ ] 콜라이더 정리 — 필요하면 `ObstacleColliderBuilder` 재사용
6. [ ] `TutorialMapBuilder.cs` + `TutorialMap.unity` 작성
7. [ ] 스크린샷으로 확인 후 밀도·배치 다듬기

### 6-3. 열려 있는 결정
- 튜토리얼 씬의 이름과 경로: 기본안 `Assets/Scenes/GU/TutorialMap.unity`
- 튜토리얼 → 인게임 씬 전환을 누가 구현할지 (맵 담당 / 게임 흐름 담당)
- 라운드에 따라 안개 색이 바뀌는 연출을 넣을지

---

## 7. 집 PC에서 이어서 작업할 때

- 브랜치 `chore/MapDetailsAdding`을 pull 받는다. 이 문서도 커밋·푸시해 두어야 집에서 받을 수 있다.
- Unity 버전 **6000.3.15f1**
- Claude Code로 이어서 작업한다면 이 문서를 첨부하고 **"6-2 작업 순서 1번부터 진행"**이라고 요청하면 된다.
