using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sci-Fi Base 에셋으로 인게임 맵(SciFiMap)을 조립한다. 설계: Docs/Design/MapDesign_SciFiBase.md
/// 메뉴: Tools/Map/Build Sci-Fi Map
/// - 기존 Map.prefab의 ReductedTerrain(New Terrain.asset)을 복사한 새 TerrainData를 깎고 칠한다. (원본·다른 씬은 건드리지 않음)
/// - 복사본은 Lift만큼 높이를 올리고 Terrain을 그만큼 내려서, 지면 높이는 그대로 두고 분화구를 파낼 여유를 만든다.
/// - 소품은 지점별 고정 seed로 배치한다. 프리펩 크기는 Renderer Bounds로 재서 맞추고, 순환로·광장 중심·서로 겹치는 자리는 피한다.
/// - 조립 결과는 프리펩(SciFiMap.prefab)으로 저장하고, 활성 씬의 기존 SciFiMap을 교체한다. 씬에 Map 인스턴스가 있으면 비활성화한다.
/// 메뉴: Tools/Map/Save Sci-Fi Map Edits
/// - 씬에서 손으로 옮기거나 지우거나 추가한 내용을 SciFiMapEdits.json에 기록한다. 이후 빌드는 이 기록을 다시 적용한다.
/// - 지점 그룹(Props/LandingPad 등)을 옮기면 그 지점의 중심이 바뀌고, 지형 조각과 자동 배치도 새 중심을 따른다.
/// </summary>
public static class SciFiMapBuilder
{
    private const string SourceMapPath = "Assets/Resources/Map/Map.prefab";
    private const string SourceTerrainName = "ReductedTerrain";
    private const string TerrainDataPath = "Assets/Scenes/GU/Map/SciFiTerrain.asset";
    private const string SavePath = "Assets/Scenes/GU/Map/SciFiMap.prefab";
    private const string LayerDir = "Assets/Sci-Fi Base/Terrain/Layers/";
    private const string PrefabDir = "Assets/Sci-Fi Base/Prefabs/";
    private const string RockDir = "Assets/Sci-Fi Base/Rocks/";
    private const string GUPrefabDir = "Assets/Scenes/GU/Prefabs/";

    private const float Lift = 15f; // 분화구를 파기 위해 높이를 올려두는 양(m)

    // 지점 그룹 이름과 기본 중심 좌표 (월드 XZ). 지형 가장자리는 높이 20~60m 산이고, 걸을 수 있는 평지는 대략 X -78~147, Z -94~122 이다.
    // 씬에서 지점 그룹(Props/<이름>)을 옮기고 Save Sci-Fi Map Edits로 저장하면, 다음 빌드부터 그 위치가 중심이 된다. (지형 조각도 따라감)
    // RuinsSE/S/N/SW: 들판의 기지 폐허 구역 (순환로 바깥 띠, 다른 지점·분화구→착륙장 통로를 피한 자리)
    private static readonly string[] SiteNames =
        { "LandingPad", "CrashSite", "DefenseLine", "Refinery", "Crater", "Wasteland", "RuinsSE", "RuinsS", "RuinsN", "RuinsSW", "RuinsE", "Camp", "Lab" };
    private static readonly Vector2[] DefaultSiteCenters =
    {
        new Vector2(35f, 9f), new Vector2(-49.43f, -90.27f), new Vector2(125f, 10f),
        new Vector2(110f, 95f), new Vector2(-40f, 90f), new Vector2(-60f, 10f),
        new Vector2(99f, -55f), new Vector2(19f, -82f), new Vector2(51f, 98f), new Vector2(-34f, -50f), new Vector2(113f, 54f),
        new Vector2(63f, -19f), new Vector2(2f, 108f),
    };
    private const int SitePad = 0, SiteCrash = 1, SiteDefense = 2, SiteRefinery = 3, SiteCrater = 4, SiteWasteland = 5;
    private const int SiteRuinsSE = 6, SiteRuinsS = 7, SiteRuinsN = 8, SiteRuinsSW = 9, SiteRuinsE = 10;
    private const int SiteCamp = 11, SiteLab = 12; // 간이 기지(착륙장 남동쪽 구석), 연구소 기지(분화구 북동쪽, 분화구를 내려다봄)
    private static readonly Vector2[] SiteCenters = new Vector2[SiteNames.Length];

    private static Vector2 Center => SiteCenters[SitePad];
    private static Vector2 CrashCenter => SiteCenters[SiteCrash];
    private static Vector2 DefenseCenter => SiteCenters[SiteDefense];
    private static Vector2 RefineryCenter => SiteCenters[SiteRefinery];
    private static Vector2 CraterCenter => SiteCenters[SiteCrater];
    private static Vector2 WastelandCenter => SiteCenters[SiteWasteland];

    // 수동 편집 기록 (Save Sci-Fi Map Edits 메뉴가 씀)
    private const string EditsPath = "Assets/Scenes/GU/Map/SciFiMapEdits.json";
    private static readonly string[] KeepTiltPrefabs = { "Spaceship_Broken" }; // 수동 편집을 적용할 때 똑바로 세우지 않을 프리펩
    private const string NavMeshDataPath = "Assets/Scenes/GU/Map/SciFiMap_NavMesh.asset";

    // 스폰 지점 (Tools/Map/Place Sci-Fi Spawn Points 메뉴가 씬의 Player·BossSpawnPoint를 옮김)
    private const float PlayerFromShip = 6f;                     // 추락선 가장자리에서 맵 중앙 쪽으로 떨어진 거리
    private static readonly Vector2 BossOffset = new Vector2(0f, -10f); // 분화구 중심 기준. 드릴(북쪽)을 피해 남쪽 바닥

    // 중앙 광장(착륙장)
    private const float PlazaRadius = 45f;
    private const float PlazaBlend = 15f;

    // 순환로
    private const float RingRadius = 65f;
    private const float RingHalfWidth = 9f;
    private const float RingBlend = 8f;

    // 굴착 분화구
    private const float CraterRadius = 28f;
    private const float CraterDepth = 7f;
    private const float CraterRimHeight = 2.5f;
    private const float CraterRimWidth = 6f;
    private const float CraterBlend = 15f;

    // 외곽 절벽
    private const float CliffWidth = 14f;      // ± CliffWidthVar
    private const float CliffWidthVar = 6f;
    private const float CliffHeight = 24f;     // + 0 ~ CliffHeightVar
    private const float CliffHeightVar = 10f;
    private const float CliffNoiseScale = 0.03f;

    // 지형 레이어 인덱스 (LayerNames 순서)
    private static readonly string[] LayerNames = { "Sand1", "Sand2", "Sand3", "SandRock", "Sand6", "Sand4", "Sand7" };
    private const int LayerBase = 0, LayerBaseAlt = 1, LayerRoad = 2, LayerRock = 3, LayerCrater = 4, LayerDark = 5, LayerStreak = 6;

    // 들판 모래 언덕 (평평한 들판을 깨는 완만한 기복)
    private const float DuneAmplitude = 2.4f;
    private const float DuneScale = 0.022f;
    private const float DuneKeepRadius = 20f;   // 추락선 주변은 원래 지형 그대로 둔다 (손으로 맞춘 추락선 높이가 원래 지형 기준이라)
    private const float DuneKeepBlend = 10f;
    // 기준면으로 평탄화하는 지점 (폐허 구역 바닥판이 뜨거나 묻히지 않게)
    private static readonly int[] FlatSites = { SiteRuinsSE, SiteRuinsS, SiteRuinsN, SiteRuinsSW, SiteRuinsE, SiteCamp, SiteLab };
    private const float SiteFlatRadius = 16f;
    private const float SiteFlatRadiusWide = 26f;
    private const float SiteFlatBlend = 8f;

    // 착륙장 + 엄폐물 링
    private const float PadRadius = 17f;          // 바닥판을 까는 반경 (안쪽 15m는 비워 둠)
    private const float PadTopOffset = 0.1f;      // 바닥판 윗면이 지면보다 올라오는 높이
    private const int PadLampCount = 12;
    private const float CoverRingMin = 21f;
    private const float CoverRingMax = 29f;
    private const float CoverGateHalfAngle = 18f; // 동서남북 진입 통로 반각(도)
    private const float CoverMaxHeight = 1.7f;    // 낮은 엄폐물 최대 높이 (높이 단계 규칙상 1.6~1.8m 띠)

    // 추락선: MapDesign 씬에서 손으로 맞춘 자세를 그대로 쓴다. (오일러 약 17.87, -208.1, -13.8)
    // 손으로 맞출 당시 지형이 (73.6, 23.8)만큼 밀려 있었으므로, 지형 기준 같은 자리가 되도록 그만큼 되돌린 값이다.
    // CrashSite 그룹이 옮겨지면 같은 만큼 따라간다.
    private static readonly Vector3 ShipPosition = new Vector3(-49.43f, -0.19f, -90.27f);
    private static readonly Quaternion ShipRotation = new Quaternion(0.07769645f, -0.95590156f, 0.17838787f, -0.21998493f);
    private const string ShipEditPath = "Props/CrashSite/Spaceship_Broken"; // 손으로 맞춘 추락선 자세의 편집 기록 경로

    // 추락 지점 소품: 선체 가장자리(Bounds 반경)에서 떨어진 거리 범위(m)
    private const float CrashCoverGapMin = 10f, CrashCoverGapMax = 26f;   // 회피용 엄폐물 (생존자가 끌어다 놓은 느낌, 선체와 떨어짐)
    private const float CrashRockGapMin = 5f, CrashRockGapMax = 24f;      // 바위
    private const float CrashDebrisGapMin = 6f, CrashDebrisGapMax = 18f;  // 통·잔해

    // 외곽 방어선 울타리 (방어선 중심 기준 동쪽으로 FenceOffsetX)
    private const float FenceOffsetX = 15f;
    private const float FenceHalfLength = 55f;
    private const float FenceGapChance = 0.3f;
    private const float FenceTiltChance = 0.25f;

    // 굴착 분화구 드릴: Sci-Fi Base/Scenes/Scene_Standard의 드릴 탑 조립(피벗 기준 로컬 좌표, 부품 스케일 1.5)을 그대로 옮김.
    // Drill_Pillar는 튜브를 감싸는 고리, Drill_Connector는 꼭대기 가로 빔이다. 빔은 한쪽만 남겨 부서진 느낌을 낸다.
    private static readonly string[] DrillPartNames =
        { "Drill_Bot", "Drill_Pillar", "Drill_Pillar", "Drill_Tube", "Drill_Pillar", "Drill_Pillar", "Drill_Top", "Drill_Tube", "Drill_Connector", "Drill_Connector", "Drill_Connector" };
    private static readonly Vector3[] DrillPartPositions =
    {
        new Vector3(0f, -1.35f, 0f), new Vector3(0f, -0.6f, 0f), new Vector3(0f, 5.4f, 0f), new Vector3(0f, 6.9f, 0f),
        new Vector3(0f, 11.4f, 0f), new Vector3(0f, 17.4f, 0f), new Vector3(0f, 17.9f, 0f), new Vector3(0f, 30.9f, 0f),
        new Vector3(0f, 47.9f, 0f), new Vector3(24f, 47.9f, 0f), new Vector3(0f, 55.9f, 0f),
    };
    private static readonly Vector3[] DrillPartEulers =
    {
        Vector3.zero, new Vector3(0f, 90f, 0f), new Vector3(0f, 90f, 0f), Vector3.zero,
        new Vector3(0f, 90f, 0f), new Vector3(0f, 90f, 0f), new Vector3(0f, 90f, 0f), Vector3.zero,
        new Vector3(180f, 0f, 0f), new Vector3(180f, 0f, 0f), Vector3.zero,
    };
    private const float DrillPartScale = 1.5f;
    private const float DrillHeight = 32f;
    private static readonly Vector2 DrillOffset = new Vector2(0f, 8f); // 분화구 남쪽 바닥은 보스용으로 비워 둠
    private const float CraterCoverRimMin = 3f, CraterCoverRimMax = 12f; // 분화구 회피용 엄폐물: 테두리 바깥 띠 (사발 반경 기준)
    private const float CraterMaxSlope = 32f; // 분화구 사발 경사(최대 약 27°)에도 바위·엄폐물을 놓을 수 있게

    // 지점별 전용 엄폐물(보스 장판·돌진 회피용): 지점마다 2~5개, 몸 전체를 가리는 높이
    private const int CoverMinCount = 2;
    private const int CoverMaxCount = 5;
    private const float CoverTallMinHeight = 3.1f; // 높이 단계 규칙상 3.1m 이상 (1.8m 엄폐물을 밟고도 못 올라감)
    private const float CoverTallMaxHeight = 3.6f;
    private const float CoverMinWidth = 2.5f;   // 가로 폭이 이보다 좁으면 폭 기준으로 키움
    private const float CoverHardMaxHeight = 5f; // 폭 기준으로 키운 결과가 이보다 높으면 쓰지 않음
    private const float CoverSpacing = 6f;      // 엄폐물 사이 최소 간격(이동 통로)

    // 외곽 경계: 평지를 둘러싼 산기슭을 찾아 그 안쪽을 따라 무너진 기지 외벽과 바위 무더기를 번갈아 놓는다.
    private const int BoundarySamples = 180;          // 둘레를 찾는 방향 수(2도 간격)
    private const float BoundaryRise = 9f;            // 기준면보다 이만큼 높아지면 산기슭으로 본다 (평지의 5m 단차는 무시)
    private const float BoundaryInset = 1.5f;         // 산기슭에서 안쪽으로 물린 거리
    private const float BoundaryNoiseScale = 0.02f;
    private const float BoundaryWallThreshold = 0.5f; // 노이즈가 이보다 크면 외벽 구간, 작으면 바위 구간
    private const float BoundaryWallGapChance = 0.15f;
    private const float BoundaryRockStep = 7f;
    private static readonly string[] BoundaryWallNames =
        { "Wall_Large_01", "Wall_Large_02", "Wall_Large_03", "Wall_Large_05", "Concrete_wall1", "Concrete_wall2", "Wall1A", "Wall1B" };
    private static readonly string[] BoundaryRockNames =
        { "Rock_Shales_01", "Rock_Shales_03", "Rock_Shales_06", "Rock_Shales_09", "Stone_Big1", "Rock_01" };

    // 기지 폐허 구역: Sci-Fi Base/Scenes/Scene_2_Standard의 구성(포장 바닥 + 콘크리트 벽·탑 + 파이프 + 방벽 + 조명·안테나)을
    // 작은 단위로 조립한다. 바닥판 위에 2~3면 벽(가운데 입구), 모서리 탑, 바깥 파이프 줄, 입구 앞 방벽, 안쪽 소품.
    private const float RuinWidth = 24f, RuinDepth = 16f, RuinSizeVar = 2f;
    private const float RuinEntranceHalf = 3.5f;      // 벽마다 비워 두는 입구 반폭 (7m)
    private const float RuinBrokenChance = 0.15f;     // 벽 조각이 무너져 비어 있을 확률
    private const float RuinMissingTileChance = 0.12f; // 바닥판이 깨져 비어 있을 확률
    private const float RuinPipeOffset = 2f;          // 벽 바깥으로 파이프 줄이 떨어진 거리
    private static readonly string[] RuinFloorNames = { "Concrete_Ground1", "Concrete_Ground2", "Concrete_Ground3", "Concrete_Ground4" };
    private static readonly string[] RuinWallNames = { "Concrete_wall1", "Concrete_wall1", "Concrete_wall1 Stripes Variant", "Concrete_wall2" };
    private static readonly string[] RuinPipeNames = { "Pipe2_A", "Pipe2_Connector1", "Pipe2_A", "Pipe2_B" };
    private static readonly string[] RuinInsideNames = { "Prop1", "Prop2", "Prop4", "Prop6", "Prop8", "Prop9", "Prop10", "Powerbox_01", "Prop_Barrel1", "Prop_Cylinder2" };
    private static readonly string[] RuinAccentNames = { "Antenna_04", "Antenna_05", "Solar_Panel3", "Billboard2_no_holdfast", "Prop_Went_big2" };

    private static readonly string[] LabPipelineNames = { "Pipe2_A", "Pipe2_Connector1", "Pipe2_A", "Pipe2_Support1" };
    private const float LabPipelineGapChance = 0.12f;

    // 필드 대형 엄폐물: 넓은 평지 곳곳의 큰 바위 지형·기지 잔해 (지점 사이 빈 들판을 채움)
    // 보스(Clypeosaurus) 몸통 폭이 약 15m인데 NavMesh 에이전트 반경은 0.5m라 좁은 틈도 길로 잡힌다. 그래서 간격을 배치 규칙으로 지킨다.
    private const int FieldCoverCount = 22;
    private const float FieldCoverSpacing = 15f;               // 큰 물체(주변 바위 포함) 사이 최소 간격 = 보스 통로 (보스 몸통 폭 약 15m)
    private const float FieldCoverClearance = 3f;              // 작은 소품과의 간격
    private const float FieldCoverCompanionReach = 4f;         // 큰 엄폐물 둘레에 붙이는 작은 바위가 차지하는 폭
    private const float BossCorridorHalf = 14f;                // 분화구(보스 등장)→착륙장 직선 통로 반폭: 큰 엄폐물 금지
    private const float FieldCoverPadKeep = CoverRingMax + 4f; // 착륙장 낮은 엄폐물 링 안쪽은 큰 엄폐물 금지
    private const float PlayableRise = 8f;                     // 기준면보다 이만큼 높은 곳은 산비탈로 보고 쓰지 않음 (평지 5m 단차 + 언덕 기복은 허용, 산은 20m 이상)
    private const float FieldRockMinHeight = 4f, FieldRockMaxHeight = 8f;
    private const float FieldWreckChance = 0.4f;
    // 순환로 안쪽 띠(착륙장 엄폐물 링과 순환로 사이): 군중전 공간이라 낮은 바위만 드문드문
    private const int InnerCoverCount = 6;
    private const float InnerRockMinHeight = 3f, InnerRockMaxHeight = 5f;
    private const float InnerCoverReach = 1.5f;
    private static readonly string[] InnerRockNames = { "Stone_Big1", "Rock_01", "Rock_Shales_04", "Rock_Shales_10" }; // 옆으로 넓은 Rock_Group1은 좁은 띠에 안 맞아 뺌
    private static readonly string[] FieldRockNames = { "Rock_Group1", "Stone_Big1", "Rock_01", "Rock_Shales_04", "Rock_Shales_10" };
    private static readonly string[] FieldWreckNames = { "Tank_02", "Tank_03", "Container_01", "Generator_Set_02", "Wall_Large_06", "Concrete_wall2_Barrier3" };
    private static readonly string[] FieldToppledNames = { "Tank_02", "Tank_03" }; // 옆으로 쓰러뜨려 놓을 것
    private static readonly string[] FieldCompanionNames = { "Stone_med1", "Stone_med3", "Stone_med5", "Rock_Shales_02", "Rock_Shales_07" };

    // 환경 디테일: 들판 곳곳의 중간 돌(장애물) + 작은 돌·자갈 무더기(콜라이더 끔, NavMesh·이동과 무관)
    private const int MediumRockCount = 70;
    private const int PebbleClusterCount = 70;
    private const int PebblesPerClusterMin = 3, PebblesPerClusterMax = 8;
    private const float PebbleClusterRadius = 4f;
    private const float PebbleMaxRise = 10f; // 산기슭 조금 위까지는 자갈을 흩뿌린다
    private static readonly string[] MediumRockNames =
        { "Stone_med1", "Stone_med2", "Stone_med4", "Stone_med6", "Rock_Shales_01", "Rock_Shales_03", "Rock_Shales_05", "Rock_Shales_08" };
    private static readonly string[] PebbleNames =
        { "Stone_small1", "Stone_small2", "Stone_small3", "Stone_small4", "Stone_small5", "Stone_Tiny1", "Stone_Tiny2", "Stone_Tiny3" };

    // 분위기: 붉은 갈색 먼지 안개 + 어두운 하늘 + 낮게 깔린 햇빛 + 붉은 포인트 조명 (MapDesign 씬 설정을 바꾼다)
    private static readonly Color FogColor = new Color(0.42f, 0.27f, 0.21f);
    private const float FogDensity = 0.008f;
    private static readonly Color AmbientSky = new Color(0.30f, 0.24f, 0.24f);
    private static readonly Color AmbientEquator = new Color(0.28f, 0.20f, 0.17f);
    private static readonly Color AmbientGround = new Color(0.12f, 0.09f, 0.08f);
    private static readonly Color SunColor = new Color(1f, 0.72f, 0.55f);
    private const float SunIntensity = 1.1f;
    private static readonly Vector3 SunEuler = new Vector3(28f, -35f, 0f);
    internal static readonly Color GuideLightColor = new Color(1f, 0.15f, 0.1f);
    private static readonly Color CraterGlowColor = new Color(1f, 0.25f, 0.1f);

    // 높이 단계 규칙 (맵 핵심 설계: 플레이어가 몬스터가 못 오는 높은 곳에서 사격하는 이점을 없앤다)
    // 플레이어 점프 높이 1.2m(PlayerController.jumpHeight). 장애물 윗면 높이(지면 기준)를 세 단계로만 허용한다:
    //   ① 0.35m 이하: 밟아도 의미 없는 턱  ② 1.6~1.8m: 맨땅에서 점프로 못 올라감  ③ 3.1m 이상: ②를 밟고 점프해도 못 올라감
    // 그 사이 높이는 가까운 단계로 크기를 맞춘다. 계단·캣워크·플랫폼처럼 올라설 수 있는 구조물은 아예 쓰지 않는다.
    private const float PerchLowMax = 0.35f;
    private const float PerchMidMin = 1.6f, PerchMidMax = 1.8f;
    private const float PerchTallMin = 3.1f;
    private const float PerchScaleMin = 0.8f, PerchScaleMax = 1.3f; // 이 배율 범위로 맞출 수 없으면 그대로 둔다
    private const float PerchAirborne = 0.5f; // 바닥이 지면보다 이만큼 위에 뜬 부품(드릴 빔 등)은 규칙에서 뺀다

    // 소품 배치 공통
    internal const float MaxPropSlope = 20f;  // 이보다 가파른 곳엔 배치하지 않음(도)
    private const float EdgeMargin = 2f;
    private const int PlaceTries = 30;

    [MenuItem("Tools/Map/Build Sci-Fi Map")]
    private static void Build()
    {
        Terrain source = FindSourceTerrain();
        if (source == null)
        {
            Debug.LogError($"[SciFiMapBuilder] {SourceMapPath} 에서 {SourceTerrainName} 을 찾지 못했습니다.");
            return;
        }

        // 씬의 SciFiMap에 저장 안 한 수동 편집이 있으면 먼저 물어본다.
        GameObject current = FindSceneInstance();
        if (current != null && PrefabUtility.HasPrefabInstanceAnyOverrides(current, false))
        {
            int choice = EditorUtility.DisplayDialogComplex("Build Sci-Fi Map",
                "씬의 SciFiMap에 저장하지 않은 수동 편집이 있습니다.\n빌드하면 씬의 SciFiMap이 교체되어 이 편집은 사라집니다.",
                "저장 후 빌드", "취소", "버리고 빌드");
            if (choice == 1) return;
            if (choice == 0) SaveEdits();
        }

        EditData edits = LoadEdits();
        _edits = edits;
        for (int k = 0; k < SiteNames.Length; k++)
        {
            MovedEdit e = edits.moved.Find(m => m.path == "Props/" + SiteNames[k]);
            SiteCenters[k] = e != null ? new Vector2(e.position.x, e.position.z) : DefaultSiteCenters[k];
        }

        GameObject root = new GameObject("SciFiMap");

        // 1) 지형: 복사 → 조각 → 칠하기
        TerrainData data = GetOrCopyTerrainData(source.terrainData);
        // Map.prefab 루트는 원본에서 (73.6, 0, 23.8)에 있지만 MapDesign 씬에서는 (0, 0, 0)에 놓여 있다.
        // 씬 오브젝트(Player·보급·보스 스폰) 좌표와 맞추려면 루트 기준 위치를 써야 한다.
        Vector3 terrainPos = source.transform.position - source.transform.root.position - Vector3.up * Lift;
        SculptTerrain(data, source.terrainData, terrainPos);
        PaintTerrain(data, terrainPos);
        Terrain terrain = CreateTerrainObject(data, source, terrainPos, root.transform);

        // 2) 지점별 소품 배치 → 이름 고유화(편집 기록의 경로 키) → 저장된 수동 편집 재적용
        BuildProps(root.transform, terrain);
        MakeNamesUnique(root.transform);
        ApplyEdits(root.transform, edits);

        // 3) 콜라이더 정리 → NavMesh 굽기 (수동 편집까지 반영된 상태에서)
        CleanupColliders(root.transform);
        BakeNavMesh(root, source, NavMeshDataPath);

        // 4) 프리펩 저장 후 씬에 배치
        PrefabUtility.SaveAsPrefabAsset(root, SavePath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();

        ReplaceInActiveScene();
        ApplyAtmosphere();
        Debug.Log($"[SciFiMapBuilder] 생성 완료: {SavePath}");
    }

    // ───────────────────────── 지형 ─────────────────────────

    internal static Terrain FindSourceTerrain()
    {
        GameObject map = AssetDatabase.LoadAssetAtPath<GameObject>(SourceMapPath);
        if (map == null) return null;
        foreach (Terrain t in map.GetComponentsInChildren<Terrain>(true))
            if (t.name == SourceTerrainName) return t;
        return null;
    }

    /// <summary>처음엔 원본을 복사하고, 이후 재빌드 때는 같은 에셋(GUID 유지)을 재사용한다. 높이는 매번 원본에서 다시 가져온다.</summary>
    private static TerrainData GetOrCopyTerrainData(TerrainData sourceData)
    {
        TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
        if (data != null) return data;

        AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(sourceData), TerrainDataPath);
        return AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
    }

    private static void SculptTerrain(TerrainData data, TerrainData sourceData, Vector3 terrainPos)
    {
        int res = sourceData.heightmapResolution;
        float[,] heights = sourceData.GetHeights(0, 0, res, res);
        float scaleY = data.size.y;

        // 광장 중심 지면 높이를 기준면으로 삼는다(복사본 기준, Lift 포함).
        float baseH = sourceData.GetInterpolatedHeight(
            (Center.x - terrainPos.x) / data.size.x, (Center.y - terrainPos.z) / data.size.z) + Lift;

        float minX = terrainPos.x, maxX = terrainPos.x + data.size.x;
        float minZ = terrainPos.z, maxZ = terrainPos.z + data.size.z;

        // 언덕을 넣지 않을 추락선 자리: 손으로 맞춘 자세가 있으면 그 위치, 없으면 추락 지점 중심
        MovedEdit ship = _edits?.moved.Find(m => m.path == ShipEditPath);
        Vector2 shipKeep = ship != null ? new Vector2(ship.position.x, ship.position.z) : CrashCenter;

        for (int j = 0; j < res; j++)
        {
            for (int i = 0; i < res; i++)
            {
                float wx = minX + i / (float)(res - 1) * data.size.x;
                float wz = minZ + j / (float)(res - 1) * data.size.z;
                Vector2 p = new Vector2(wx, wz);
                float h = heights[j, i] * scaleY + Lift;

                // 들판 모래 언덕: 완만한 큰 기복 + 잔물결 (아래 평탄화 구역에서는 지워진다)
                float duneW = Smooth01((Vector2.Distance(p, shipKeep) - DuneKeepRadius) / DuneKeepBlend);
                h += duneW * ((Mathf.PerlinNoise(wx * DuneScale + 500f, wz * DuneScale + 500f) - 0.5f) * 2f * DuneAmplitude
                            + (Mathf.PerlinNoise(wx * DuneScale * 4f, wz * DuneScale * 4f) - 0.5f) * DuneAmplitude * 0.5f);

                // 광장·순환로·폐허 구역·추락 지점: 기준면으로 평탄화
                float d = Vector2.Distance(p, Center);
                float flat = Mathf.Max(
                    1f - Smooth01((d - PlazaRadius) / PlazaBlend),
                    1f - Smooth01((Mathf.Abs(d - RingRadius) - RingHalfWidth) / RingBlend));
                foreach (int site in FlatSites)
                {
                    float r = site == SiteCamp || site == SiteLab ? SiteFlatRadiusWide : SiteFlatRadius; // 옆에 건물이 붙는 구역은 넓게
                    flat = Mathf.Max(flat, 1f - Smooth01((Vector2.Distance(p, SiteCenters[site]) - r) / SiteFlatBlend));
                }
                h = Mathf.Lerp(h, baseH, flat);

                // 굴착 분화구: 사발 모양 + 둥근 테두리
                float dc = Vector2.Distance(p, CraterCenter);
                float bowl = dc < CraterRadius ? -CraterDepth * (1f - (dc / CraterRadius) * (dc / CraterRadius)) : 0f;
                float rimT = (dc - CraterRadius) / CraterRimWidth;
                float rim = CraterRimHeight * Mathf.Exp(-rimT * rimT);
                float craterW = 1f - Smooth01((dc - CraterRadius) / CraterBlend);
                h = Mathf.Lerp(h, baseH + bowl + rim, craterW);

                // 외곽 절벽: 가장자리에서 안쪽으로 폭·높이가 들쭉날쭉한 벽
                float edge = Mathf.Min(Mathf.Min(wx - minX, maxX - wx), Mathf.Min(wz - minZ, maxZ - wz));
                float n1 = Mathf.PerlinNoise(wx * CliffNoiseScale, wz * CliffNoiseScale);
                float n2 = Mathf.PerlinNoise(wx * CliffNoiseScale + 100f, wz * CliffNoiseScale + 100f);
                float width = CliffWidth + (n1 * 2f - 1f) * CliffWidthVar;
                float cliffT = Smooth01(1f - edge / width);
                float rough = Mathf.PerlinNoise(wx * 0.2f, wz * 0.2f) * 3f;
                // cliffT가 0인 안쪽까지 max를 걸면 기준면보다 낮은 곳(분화구·낮은 평지)이 전부 기준면으로 메워진다.
                if (cliffT > 0f) h = Mathf.Max(h, baseH + (CliffHeight + n2 * CliffHeightVar + rough) * cliffT);

                heights[j, i] = Mathf.Clamp01(h / scaleY);
            }
        }

        data.SetHeights(0, 0, heights);
    }

    private static void PaintTerrain(TerrainData data, Vector3 terrainPos)
    {
        TerrainLayer[] layers = LoadTerrainLayers(data, LayerNames);

        int res = data.alphamapResolution;
        float[,,] alpha = new float[res, res, layers.Length];

        for (int j = 0; j < res; j++)
        {
            for (int i = 0; i < res; i++)
            {
                float nx = i / (float)(res - 1);
                float nz = j / (float)(res - 1);
                Vector2 p = new Vector2(terrainPos.x + nx * data.size.x, terrainPos.z + nz * data.size.z);

                float d = Vector2.Distance(p, Center);
                float road = Mathf.Max(
                    1f - Smooth01((d - (PlazaRadius - 5f)) / 10f),
                    1f - Smooth01((Mathf.Abs(d - RingRadius) - RingHalfWidth * 0.8f) / 4f));
                float dc = Vector2.Distance(p, CraterCenter);
                float crater = 1f - Smooth01((dc - CraterRadius * 0.8f) / (CraterRadius * 0.4f));
                float rock = Smooth01((data.GetSteepness(nx, nz) - 25f) / 15f);
                float patch = Smooth01((Mathf.PerlinNoise(p.x * 0.05f + 50f, p.y * 0.05f + 50f) - 0.4f) / 0.3f);
                float patch2 = Smooth01((Mathf.PerlinNoise(p.x * 0.018f + 900f, p.y * 0.018f + 900f) - 0.45f) / 0.25f);
                float streak = Smooth01((Mathf.PerlinNoise(p.x * 0.11f + 300f, p.y * 0.03f + 300f) - 0.55f) / 0.2f); // 바람에 쓸린 줄무늬

                // 우선순위: 급경사 바위 > 분화구 바닥 > 길 > 바탕(참고 씬처럼 모래 네 가지를 큰 얼룩·작은 얼룩·줄무늬로 섞음)
                float rest = 1f - rock;
                float wCrater = rest * crater;
                rest -= wCrater;
                float wRoad = rest * road * 0.85f;
                rest -= wRoad;
                float wStreak = rest * streak * 0.6f;
                rest -= wStreak;
                float wDark = rest * patch2;
                rest -= wDark;

                alpha[j, i, LayerRock] = rock;
                alpha[j, i, LayerCrater] = wCrater;
                alpha[j, i, LayerRoad] = wRoad;
                alpha[j, i, LayerStreak] = wStreak;
                alpha[j, i, LayerDark] = wDark;
                alpha[j, i, LayerBaseAlt] = rest * patch;
                alpha[j, i, LayerBase] = rest * (1f - patch);
            }
        }

        data.SetAlphamaps(0, 0, alpha);
        EditorUtility.SetDirty(data);
    }

    /// <summary>Sci-Fi Base 지형 레이어(LayerDir 안의 이름들)를 불러와 data에 지정하고 돌려준다.</summary>
    internal static TerrainLayer[] LoadTerrainLayers(TerrainData data, string[] names)
    {
        TerrainLayer[] layers = new TerrainLayer[names.Length];
        for (int k = 0; k < names.Length; k++)
            layers[k] = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerDir + names[k] + ".terrainlayer");
        data.terrainLayers = layers;
        return layers;
    }

    /// <summary>data로 지형 오브젝트를 만들고, 레이어·머티리얼·그림자 같은 렌더 설정은 원본 지형(source)을 따른다.</summary>
    internal static Terrain CreateTerrainObject(TerrainData data, Terrain source, Vector3 position, Transform parent)
    {
        GameObject go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.layer = source.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.transform.position = position;

        Terrain terrain = go.GetComponent<Terrain>();
        terrain.materialTemplate = source.materialTemplate;
        terrain.heightmapPixelError = source.heightmapPixelError;
        terrain.shadowCastingMode = source.shadowCastingMode;
        return terrain;
    }

    // ───────────────────────── 소품 ─────────────────────────

    private static Terrain _terrain;
    private static EditData _edits; // 이번 빌드의 수동 편집 기록 (배치 중에 미리 참고할 것이 있을 때)
    private static bool _useSiteRules = true;                   // IsFree에서 광장·순환로를 피할지 (인게임 맵 전용)
    private static System.Func<Vector2, float, bool> _extraFilter; // IsFree에 더할 금지 구역 (다른 빌더용)
    private static readonly List<Vector3> Occupied = new List<Vector3>(); // (x, z, 반경)
    private static readonly List<Vector3> BigOccupied = new List<Vector3>(); // 보스 통로 간격을 따지는 큰 물체만

    private static void BuildProps(Transform root, Terrain terrain)
    {
        BeginPlacement(terrain, true);
        Transform props = new GameObject("Props").transform;
        props.SetParent(root, false);

        Random.State prev = Random.state;
        // 간이 공항(착륙장 구석)은 착륙장 엄폐물 링보다 먼저 놓아, 링 엄폐물이 유도로를 피해 가게 한다.
        Transform camp = SiteGroup(props, SiteCamp);
        Random.InitState(7130);
        CompoundFrame campFrame = BuildCompound(camp, SiteCenters[SiteCamp], CampSpec, Center); // 앞면(격납고 입구)이 착륙장을 봄
        // 격납고 양옆: 관제·사무동, 창고동 (뒤쪽은 순환로와 가까워 비움)
        AttachBuilding(camp, "ControlOffice", campFrame, 1, 2, 2);
        AttachBuilding(camp, "Storage", campFrame, -1, 2, 1);
        Random.InitState(7133); BuildTaxiway(camp);
        Random.InitState(7101); BuildLandingPad(SiteGroup(props, SitePad));
        for (int k = SiteRuinsSE; k <= SiteRuinsE; k++) // 폐허는 자리가 고정이라 먼저 놓고, 뒤의 지점 소품이 피해 가게 한다
        {
            Random.InitState(7111 + k);
            BuildCompound(SiteGroup(props, k), SiteCenters[k], RuinSpec);
        }
        Transform lab = SiteGroup(props, SiteLab);
        Random.InitState(7131);
        CompoundFrame labFrame = BuildCompound(lab, SiteCenters[SiteLab], LabSpec, CraterCenter); // 앞면이 분화구를 봄
        // 연구동(오른쪽 = 분화구를 볼 때 남쪽, 북쪽 산비탈과 왼쪽 탱크 더미를 피함) + 장비 창고(뒤, 파이프 줄 너머)
        AttachBuilding(lab, "ResearchHall", labFrame, 1, 3, 2);
        AttachBuilding(lab, "EquipmentShed", labFrame, 0, 2, 1);
        Random.InitState(7132); BuildLabPipeline(lab);
        Random.InitState(7102); BuildCrashSite(SiteGroup(props, SiteCrash));
        Random.InitState(7103); BuildDefenseLine(SiteGroup(props, SiteDefense));
        Random.InitState(7104); BuildRefinery(SiteGroup(props, SiteRefinery));
        Random.InitState(7105); BuildCrater(SiteGroup(props, SiteCrater));
        Random.InitState(7106); BuildWasteland(SiteGroup(props, SiteWasteland));
        Transform fieldCover = Group(props, "FieldCover");
        Random.InitState(7108);
        BuildFieldCover(fieldCover, "들판 대형", FieldCoverCount, FieldWreckChance, FieldRockMinHeight, FieldRockMaxHeight,
            FieldCoverCompanionReach, FieldCoverPadKeep, float.MaxValue);
        Random.InitState(7112);
        BuildFieldCover(fieldCover, "순환로 안쪽", InnerCoverCount, 0f, InnerRockMinHeight, InnerRockMaxHeight,
            InnerCoverReach, FieldCoverPadKeep, RingRadius - RingHalfWidth - 2f);
        Random.InitState(7109); BuildMediumRocks(Group(props, "Rocks"));
        Random.InitState(7107); BuildBoundary(Group(props, "Boundary")); // 지점 소품을 피해서 놓도록 소품 중 마지막에
        Random.InitState(7110); BuildPebbles(Group(root, "Details"));   // 장애물이 아니라 Props 밖에 둔다 (콜라이더 정리·NavMesh 제외)
        Random.state = prev;
    }

    /// <summary>착륙장(중앙): 원형으로 깐 바닥판 + 가장자리 유도등 + 진입 통로를 남긴 끊긴 원호 엄폐물</summary>
    private static void BuildLandingPad(Transform parent)
    {
        // 바닥판: 같은 크기의 Hangar_Ground_Plate 변형만 섞어서 격자로 깐다.
        Vector3 tile = Vector3.zero;
        List<string> plates = new List<string>();
        for (int k = 1; k <= 5; k++)
        {
            string name = "Hangar_Ground_Plate" + k;
            GameObject go = Spawn(name, parent);
            if (go == null) continue;
            Vector3 s = GetBounds(go).size;
            if (plates.Count == 0) tile = s;
            if (Mathf.Abs(s.x - tile.x) < 0.1f && Mathf.Abs(s.z - tile.z) < 0.1f) plates.Add(name);
            Object.DestroyImmediate(go);
        }
        if (plates.Count == 0 || tile.x < 0.5f || tile.z < 0.5f)
        {
            Debug.LogWarning("[SciFiMapBuilder] Hangar_Ground_Plate 크기를 재지 못해 착륙장 바닥판을 건너뜁니다.");
            plates.Clear();
        }

        int n = plates.Count > 0 ? Mathf.CeilToInt(PadRadius / tile.x) : -1;
        int m = plates.Count > 0 ? Mathf.CeilToInt(PadRadius / tile.z) : -1;
        for (int ix = -n; ix <= n; ix++)
        {
            for (int iz = -m; iz <= m; iz++)
            {
                Vector2 xz = Center + new Vector2(ix * tile.x, iz * tile.z);
                if (Vector2.Distance(xz, Center) > PadRadius) continue;
                GameObject go = Spawn(plates[Random.Range(0, plates.Count)], parent);
                // 정사각형 타일일 때만 90도 단위로 돌려 반복 무늬를 줄인다.
                if (Mathf.Abs(tile.x - tile.z) < 0.1f) go.transform.rotation = Quaternion.Euler(0f, 90f * Random.Range(0, 4), 0f) * go.transform.rotation;
                PlaceTile(go, xz, GroundY(xz) + PadTopOffset);
            }
        }
        Occupy(Center, PadRadius);

        // 유도등
        for (int k = 0; k < PadLampCount; k++)
        {
            float a = k * Mathf.PI * 2f / PadLampCount;
            PlaceGuideLamp(parent, Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (PadRadius + 0.6f), 6f, 2f);
        }

        // 회피용 엄폐물: 엄폐물 링 띠 안, 동서남북 진입 통로 밖
        PlaceCover(parent, new[] { "Container_01", "Barrier_Solid_01", "Concrete_wall2_Barrier1", "Cover Frame Panel1" },
            Center, CoverRingMin, CoverRingMax, MaxPropSlope, xz => !InPadGate(xz));

        // 낮은 엄폐물 링: 4개 원호에 2~3개씩 (회피용 엄폐물과 겹치는 자리는 건너뜀)
        string[] cover =
        {
            "Cover Panel1A", "Cover Panel2B", "Cover Panel3C", "Cover Panel4A", "Cover Panel5B",
            "Barrier_01_A", "Barrier_01_B", "Barrier_Solid_01",
            "Concrete_wall2_Barrier1", "Concrete_wall2_Barrier2", "Concrete_wall2_Barrier3",
            "Prop_Barrel1", "Prop_Barrel1 yellow",
        };
        for (int g = 0; g < 4; g++)
        {
            float start = g * 90f + CoverGateHalfAngle;
            float end = (g + 1) * 90f - CoverGateHalfAngle;
            int count = Random.Range(2, 4);
            for (int k = 0; k < count; k++)
            {
                GameObject go = Spawn(cover[Random.Range(0, cover.Length)], parent);
                if (go == null) continue;
                ClampHeight(go, CoverMaxHeight);

                float angle = Mathf.Lerp(start, end, (k + 0.5f) / count) + Random.Range(-5f, 5f);
                float rad = angle * Mathf.Deg2Rad;
                Vector2 xz = Center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * Random.Range(CoverRingMin, CoverRingMax);
                // 로컬 +Z가 바깥을 보게 → 긴 변(X)이 원호를 따라감. 정렬 안 된 느낌으로 살짝 비튼다.
                go.transform.rotation = Quaternion.Euler(0f, 90f - angle + Random.Range(-25f, 25f), 0f) * go.transform.rotation;
                float r = Footprint(go);
                if (!IsFree(xz, r, 1f, MaxPropSlope))
                {
                    Object.DestroyImmediate(go);
                    continue;
                }
                SetOnGround(go, xz, 0.02f);
                Occupy(xz, r);
            }
        }
    }

    /// <summary>추락 지점(남): 추락선 잔해(연기 포함) + 주변 바위·잔해</summary>
    private static void BuildCrashSite(Transform parent)
    {
        GameObject ship = Spawn("Spaceship_Broken", parent);
        float shipRadius = 10f;
        Vector2 shipXZ = CrashCenter;
        if (ship != null)
        {
            Vector2 baseXZ = DefaultSiteCenters[SiteCrash];
            Vector3 shift = new Vector3(CrashCenter.x - baseXZ.x, GroundY(CrashCenter) - GroundY(baseXZ), CrashCenter.y - baseXZ.y);
            ship.transform.SetPositionAndRotation(ShipPosition + shift, ShipRotation);

            // 손으로 맞춘 추락선 자세가 저장돼 있으면 주변 소품을 배치하기 전에 먼저 적용한다.
            // (나중에 적용하면 소품은 옛 자리 기준으로 흩어지고 추락선만 옮겨진다)
            MovedEdit manual = _edits?.moved.Find(m => m.path == ShipEditPath);
            if (manual != null) SetRootPose(parent.root, ship.transform, manual.position, manual.rotation, manual.scale);

            shipRadius = Footprint(ship);
            Bounds sb = GetBounds(ship);
            shipXZ = new Vector2(sb.center.x, sb.center.z);
            OccupyBig(shipXZ, shipRadius);
        }

        // 추락선에 바짝 붙지 않게 선체 가장자리에서 일정 거리 밖에 놓는다.
        PlaceCover(parent, new[] { "Container_01", "Concrete_wall2_Shield", "Stone_Big1", "Rock_01" },
            shipXZ, shipRadius + CrashCoverGapMin, shipRadius + CrashCoverGapMax, MaxPropSlope, null);
        Scatter(parent, new[] { "Rock_01", "Stone_med1", "Stone_med2", "Stone_med3", "Rock_Shales_01", "Rock_Shales_03" },
            10, shipXZ, shipRadius + CrashRockGapMin, shipRadius + CrashRockGapMax, 2f, 0.8f, 1.3f, 0.2f);
        Scatter(parent, new[] { "Prop_Barrel1 gray", "Prop3", "Prop5", "Prop7", "Cover Panel2D" },
            6, shipXZ, shipRadius + CrashDebrisGapMin, shipRadius + CrashDebrisGapMax, 1.5f, 1f, 1f, 0.1f);
    }

    /// <summary>외곽 방어선(동): 군데군데 무너진 남북 방향 울타리 + 안테나·태양광 패널 + 낮은 방벽</summary>
    private static void BuildDefenseLine(Transform parent)
    {
        float z = DefenseCenter.y - FenceHalfLength;
        while (z < DefenseCenter.y + FenceHalfLength)
        {
            GameObject go = Spawn("Fence1", parent);
            if (go == null) break;
            Bounds b = GetBounds(go);
            if (b.size.x > b.size.z) go.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * go.transform.rotation;
            float len = Mathf.Max(Mathf.Max(b.size.x, b.size.z), 1f);

            if (Random.value < FenceGapChance)
            {
                Object.DestroyImmediate(go); // 끊긴 구간
            }
            else
            {
                if (Random.value < FenceTiltChance)
                    go.transform.rotation = Quaternion.Euler(0f, Random.Range(-10f, 10f), Random.Range(15f, 35f) * (Random.value < 0.5f ? 1f : -1f)) * go.transform.rotation;
                Vector2 xz = new Vector2(DefenseCenter.x + FenceOffsetX, z + len * 0.5f);
                SetOnGround(go, xz, 0.05f);
                Occupy(xz, len * 0.5f);
            }
            z += len;
        }

        PlaceCover(parent, new[] { "Concrete_wall2_Barrier2", "Wall3_Barrier1", "Barrier_Solid_01", "Container_01" },
            DefenseCenter + new Vector2(-12f, 0f), 4f, 30f, MaxPropSlope, null);
        Scatter(parent, new[] { "Antenna_01", "Antenna_03", "Antenna_05", "Solar_Panel1", "Solar_Panel2", "Solar_Panel3", "Solar_Panel4" },
            5, DefenseCenter, 6f, 45f, 3f, 1f, 1f, 0f);
        Scatter(parent, new[] { "Barrier_2m", "Barrier_4m", "Barrier_01_A", "Barrier_Edge_2m", "Concrete_wall2_Barrier3", "Cover Panel6A" },
            8, DefenseCenter + new Vector2(-12f, 0f), 4f, 32f, 3f, 1f, 1f, 0.05f);
    }

    /// <summary>정제 플랜트(북동): 크레인 + 탱크·발전기 군집(사이 통로 4m 이상) + 작은 설비</summary>
    private static void BuildRefinery(Transform parent)
    {
        Scatter(parent, new[] { "Crane_01" }, 1, RefineryCenter, 0f, 4f, 4f, 1f, 1f, 0f);
        PlaceCover(parent, new[] { "Container_01", "Barrier_Solid_01", "Concrete_wall2_Barrier1" },
            RefineryCenter, 6f, 30f, MaxPropSlope, null);
        Scatter(parent, new[] { "Tank_01", "Tank_02", "Tank_03", "Tank_04", "Tank_Stack_01", "Tank_Stack_02",
                "Generator_Set_01", "Generator_Set_02", "Generator_Set_03", "Radiator_Big" },
            10, RefineryCenter, 6f, 30f, 4f, 1f, 1f, 0f);
        Scatter(parent, new[] { "Powerbox_01", "Powerbox_05", "Prop_Barrel1", "Prop_Cylinder1", "Prop_vents_03" }, // 작은 잡동사니는 적게
            4, RefineryCenter, 4f, 34f, 2f, 1f, 1f, 0f);
    }

    /// <summary>굴착 분화구(북서): 비스듬히 박힌 거대 드릴 탑(참고 씬 조립) + 회피용 엄폐물 + 테두리 바위</summary>
    private static void BuildCrater(Transform parent)
    {
        Transform drill = new GameObject("Drill").transform;
        drill.SetParent(parent, false);
        for (int k = 0; k < DrillPartNames.Length; k++)
        {
            GameObject go = Spawn(DrillPartNames[k], drill);
            if (go == null) continue;
            go.transform.localPosition = DrillPartPositions[k];
            go.transform.localRotation = Quaternion.Euler(DrillPartEulers[k]);
            go.transform.localScale = Vector3.one * DrillPartScale;
        }

        if (drill.childCount > 0)
        {
            drill.localScale = Vector3.one * Mathf.Clamp(DrillHeight / GetBounds(drill.gameObject).size.y, 0.2f, 2f);
            drill.rotation = Quaternion.Euler(8f, 30f, -5f);
            // 기울어진 탑의 Bounds 중심은 밑동에서 벗어나므로, 밑동(Drill_Bot)이 목표 지점에 오도록 맞춘다.
            Vector2 drillXZ = CraterCenter + DrillOffset;
            Bounds baseB = GetBounds(drill.GetChild(0).gameObject);
            drill.position += new Vector3(drillXZ.x - baseB.center.x, 0f, drillXZ.y - baseB.center.z);
            drill.position += Vector3.up * (GroundY(drillXZ) - GetBounds(drill.GetChild(0).gameObject).min.y - baseB.size.y * 0.3f);
            OccupyBig(drillXZ, Mathf.Max(baseB.extents.x, baseB.extents.z));
        }

        // 드릴이 찌른 행성의 '살'이 비치는 붉은 빛
        AddPointLight(parent, "CraterGlow", new Vector3(CraterCenter.x, GroundY(CraterCenter) + 2f, CraterCenter.y), CraterGlowColor, CraterRadius + 4f, 3f);

        // 회피용 엄폐물은 분화구 사발 안(보스 등장 공간)이 아니라 테두리 바깥 띠에 둔다.
        PlaceCover(parent, new[] { "Stone_Big1", "Rock_01", "Concrete_wall2_Shield", "Barrier_Solid_01" },
            CraterCenter, CraterRadius + CraterCoverRimMin, CraterRadius + CraterCoverRimMax, CraterMaxSlope,
            xz => DistanceToSegment(xz, CraterCenter, Center) > BossCorridorHalf);
        Scatter(parent, new[] { "Rock_Shales_02", "Rock_Shales_05", "Rock_Shales_07", "Stone_med4", "Stone_med5", "Stone_Big1" },
            12, CraterCenter, CraterRadius - 4f, CraterRadius + 10f, 2f, 0.8f, 1.5f, 0.2f, CraterMaxSlope);
    }

    /// <summary>암석 황무지(서): 큰 바위 사이로 우회로가 생기게 간격을 넉넉히 둔 바위 지대</summary>
    private static void BuildWasteland(Transform parent)
    {
        PlaceCover(parent, new[] { "Stone_Big1", "Rock_01", "Rock_Group1", "Barrier_Solid_01" }, WastelandCenter, 0f, 30f, MaxPropSlope, null);
        Scatter(parent, new[] { "Rock_Group1" }, 3, WastelandCenter, 0f, 30f, 5f, 1f, 1.6f, 0.15f);
        Scatter(parent, new[] { "Stone_Big1" }, 4, WastelandCenter, 0f, 32f, 4f, 1f, 1.5f, 0.15f);
        Scatter(parent, new[] { "Rock_Shales_01", "Rock_Shales_04", "Rock_Shales_06", "Rock_Shales_08", "Rock_Shales_09", "Rock_Shales_10" },
            10, WastelandCenter, 0f, 34f, 3f, 0.8f, 1.4f, 0.2f);
        Scatter(parent, new[] { "Stone_med1", "Stone_med2", "Stone_med3", "Stone_med6", "Stone_small1", "Stone_small3" },
            10, WastelandCenter, 0f, 36f, 2f, 0.8f, 1.3f, 0.2f);
    }

    /// <summary>
    /// 사각 구역(폐허·간이 기지·연구소)의 구성 설정. BuildCompound가 이 설정대로 조립한다.
    /// 면 번호: 0=뒤(+v) 1=오른쪽(+u) 2=앞(-v, 늘 열림) 3=왼쪽(-u)
    /// </summary>
    private sealed class CompoundSpec
    {
        public float Width, Depth, SizeVar;
        public string[] FloorNames;
        public float MissingTileChance;
        public string[] WallNames;
        public float BrokenChance;                 // 벽 조각이 무너져 비어 있을 확률
        public float ExtraOpenChance;              // 옆면 하나를 더 열어 L자로 만들 확률
        public bool SideEntrances = true;          // 옆벽 가운데 입구 (조각이 긴 벽은 입구에 걸려 통째로 빠지므로 끔)
        public bool Divider;                       // 뒷벽 가운데에서 앞으로 나온 칸막이
        public int Towers;                         // 0: 없음, 2: 뒷면 양 모서리, 3: 앞 모서리 하나 더(반반)
        public bool ContainerStack;
        public string[] TankNames;                 // 바깥 옆면 탱크 더미 후보 (null이면 없음)
        public float TankChance;
        public string[] PipeNames;                 // 뒷벽 바깥 파이프 줄 (null이면 없음)
        public int FrontBarriersMin, FrontBarriersMax;
        public string[] InsideNames;
        public int InsideMin, InsideMax;
        public string[] AccentNames;               // 바깥 모서리 세로 포인트
        public int AccentCount;
        public string LampName;
        public Color LightColor;
        public float LightRange;
    }

    // 기지 폐허: 무너진 콘크리트 벽·탑 (Scene_2_Standard 구성 축소)
    private static readonly CompoundSpec RuinSpec = new CompoundSpec
    {
        Width = RuinWidth, Depth = RuinDepth, SizeVar = RuinSizeVar,
        FloorNames = RuinFloorNames, MissingTileChance = RuinMissingTileChance,
        WallNames = RuinWallNames, BrokenChance = RuinBrokenChance, ExtraOpenChance = 0.2f,
        Divider = true, Towers = 3, ContainerStack = true,
        TankNames = new[] { "Tank_Stack_01", "Tank_Stack_03" }, TankChance = 0.6f,
        PipeNames = RuinPipeNames, FrontBarriersMin = 2, FrontBarriersMax = 3,
        InsideNames = RuinInsideNames, InsideMin = 3, InsideMax = 5,
        AccentNames = RuinAccentNames, AccentCount = 1,
        LampName = "Lamp_03_B", LightColor = new Color(1f, 0.55f, 0.3f), LightRange = 0f,
    };

    // 간이 공항(착륙장 한 구석): 지붕 없는 격납고 벽(ㄷ자, 입구가 착륙장을 봄) + 연료 탱크 + 화물 컨테이너 + 창고 표지판. 안테나는 두지 않는다.
    // 착륙장 가장자리에서 격납고 입구까지는 BuildTaxiway가 유도로와 유도등을 깐다. 지붕·2층·계단은 두지 않는다.
    private static readonly CompoundSpec CampSpec = new CompoundSpec
    {
        Width = 18f, Depth = 12f, SizeVar = 0f,
        FloorNames = new[] { "Hangar_Ground_Plate1" }, MissingTileChance = 0f,
        WallNames = new[] { "Wall_Hangar1", "Wall_Hangar1_Window", "Wall_Hangar1" }, BrokenChance = 0.05f,
        ExtraOpenChance = 0f, SideEntrances = false, // 격납고: 앞면만 열린 ㄷ자
        Divider = false, Towers = 0, ContainerStack = true,
        TankNames = null, TankChance = 0f, // 옆면은 관제·사무동과 창고동이 차지 (연료 탱크 대신)
        PipeNames = null, FrontBarriersMin = 0, FrontBarriersMax = 0,
        InsideNames = new[] { "Generator_01", "Powerbox_03", "Prop_Barrel1", "Prop_Barrel1 yellow", "Prop5", "Prop_Cylinder1", "Prop_Went_big1" },
        InsideMin = 3, InsideMax = 5,
        AccentNames = new[] { "Billboard_Warehouse", "Prop_Went_big2" }, AccentCount = 2, // 안테나 없이 창고 표지판·대형 환기구
        LampName = "Lamp_03_B", LightColor = new Color(1f, 0.78f, 0.5f), LightRange = 12f,
    };

    // 블록 건물 (간이 공항·연구소 옆): 콘크리트 탑 블록(Concrete_wall2_Tower_UP1B)을 붙여 세운 속이 꽉 찬 덩어리. 내부·지붕·계단 없음
    private const float BuildingGap = 4f;          // 구역과 건물 사이 통로

    // 간이 공항 유도로: 착륙장 가장자리 → 격납고 입구. 가운데 줄무늬 도로판, 양옆 붉은 유도등
    private static readonly string[] TaxiwayNames = { "Concrete_Road1_stripes", "Concrete_Road1" };
    private const float TaxiwayLampSpacing = 6f;
    private const float TaxiwayLampSide = 4f;   // 유도로 중심선에서 유도등까지

    // 연구소 기지(분화구 옆): 드릴이 찌른 행성을 관측하던 선발대 연구소. 온전한 파란 패널 벽, 칸막이, 발전·측정 장비, 차가운 조명
    private static readonly CompoundSpec LabSpec = new CompoundSpec
    {
        Width = 22f, Depth = 14f, SizeVar = 0f,
        FloorNames = new[] { "Ground_Plate1" }, MissingTileChance = 0.03f,
        WallNames = new[] { "Concrete_wall2", "Concrete_wall1 Stripes Variant", "Concrete_wall2" }, BrokenChance = 0.05f, // 콘크리트 기반 간이 시설
        ExtraOpenChance = 0f,
        Divider = true, Towers = 0, ContainerStack = false,
        TankNames = new[] { "Tank_01", "Tank_02" }, TankChance = 1f,
        PipeNames = new[] { "Pipe3_A", "Pipe3_Connector1" }, FrontBarriersMin = 0, FrontBarriersMax = 0, // 앞은 파이프라인이 분화구로 나감
        InsideNames = new[] { "Generator_Set_01", "Powerbox_10", "Powerbox_12", "Prop_vents_05", "Radiator_Big", "Prop_Cylinder2", "Billboard_Reactor1" },
        InsideMin = 4, InsideMax = 6,
        AccentNames = new[] { "Antenna_05" }, AccentCount = 1, // 장식은 안테나 하나만 (Antenna_Big은 계단 달린 발판이라 뺌)
        LampName = "Lamp_03_B", LightColor = new Color(0.6f, 0.8f, 1f), LightRange = 14f,
    };

    /// <summary>
    /// 사각 구역 하나를 spec대로 조립한다. 지형은 SculptTerrain에서 이 자리를 기준면으로 평탄화해 둔다.
    /// ① 바닥판 격자 ② 2~3면 벽(옆벽 가운데 7m 입구, 뒷벽은 막음) + 칸막이 ③ 모서리 탑 ④ 컨테이너·탱크
    /// ⑤ 뒷벽 바깥 파이프 ⑥ 앞 방벽 ⑦ 안쪽 소품·조명 ⑧ 바깥 세로 포인트.
    /// faceToward가 있으면 열린 앞면이 그쪽을 보게 돌리고, 없으면 무작위 방향.
    /// 다 놓은 뒤 구역 전체를 큰 물체로 기록해 필드 엄폐물이 보스 통로 간격을 지키게 한다.
    /// </summary>
    private static CompoundFrame BuildCompound(Transform parent, Vector2 center, CompoundSpec spec, Vector2? faceToward = null)
    {
        float width = spec.Width + Random.Range(-spec.SizeVar, spec.SizeVar);
        float depth = spec.Depth + Random.Range(-spec.SizeVar, spec.SizeVar);
        float yawDeg = Random.Range(0f, 180f);
        if (faceToward.HasValue)
        {
            Vector2 back = (center - faceToward.Value).normalized; // 앞면(-v)이 목표를 보려면 +v는 반대쪽
            yawDeg = Mathf.Atan2(back.x, back.y) * Mathf.Rad2Deg;
        }
        Quaternion yaw = Quaternion.Euler(0f, yawDeg, 0f);
        Vector3 u3 = yaw * Vector3.right, v3 = yaw * Vector3.forward;
        Vector2 u = new Vector2(u3.x, u3.z), v = new Vector2(v3.x, v3.z);
        float groundY = GroundY(center);

        // ① 바닥판: 회전 전 크기로 격자를 정하고, 윗면을 지면보다 살짝 위에 맞춘다.
        GameObject probe = Spawn(spec.FloorNames[0], parent);
        if (probe == null) return new CompoundFrame { Center = center, U = u, V = v, Width = width, Depth = depth };
        Vector3 tile = GetBounds(probe).size;
        Object.DestroyImmediate(probe);
        int nx = Mathf.Max(1, Mathf.RoundToInt(width / Mathf.Max(tile.x, 0.5f)));
        int nz = Mathf.Max(1, Mathf.RoundToInt(depth / Mathf.Max(tile.z, 0.5f)));
        width = nx * tile.x;
        depth = nz * tile.z;
        for (int ix = 0; ix < nx; ix++)
        {
            for (int iz = 0; iz < nz; iz++)
            {
                if (Random.value < spec.MissingTileChance) continue;
                GameObject go = Spawn(spec.FloorNames[Random.Range(0, spec.FloorNames.Length)], parent);
                if (go == null) continue;
                go.transform.rotation = yaw * go.transform.rotation;
                Vector2 xz = center + u * ((ix + 0.5f) * tile.x - width * 0.5f) + v * ((iz + 0.5f) * tile.z - depth * 0.5f);
                PlaceTile(go, xz, groundY + 0.05f);
            }
        }

        // ② 벽: 앞면은 늘 열고, ExtraOpenChance로 옆면 하나를 더 연다(L자). 뒷벽은 입구 없이, 옆벽은 가운데 입구.
        Vector2[] corners =
        {
            center - u * (width * 0.5f) + v * (depth * 0.5f), center + u * (width * 0.5f) + v * (depth * 0.5f),
            center + u * (width * 0.5f) - v * (depth * 0.5f), center - u * (width * 0.5f) - v * (depth * 0.5f),
        };
        int extraOpen = Random.value < spec.ExtraOpenChance ? (Random.value < 0.5f ? 1 : 3) : -1;
        for (int side = 0; side < 4; side++)
        {
            if (side == 2 || side == extraOpen) continue;
            BuildWallLine(parent, corners[side], corners[(side + 1) % 4], spec.WallNames,
                side == 0 || !spec.SideEntrances ? -1f : Random.Range(0.35f, 0.65f), spec.BrokenChance);
        }
        if (spec.Divider)
        {
            // 뒷벽 가운데에서 앞쪽으로 절반 길이: 안을 두 칸으로 나눠 엄폐 각을 만든다
            Vector2 backMid = (corners[0] + corners[1]) * 0.5f;
            BuildWallLine(parent, backMid, backMid - v * (depth * 0.5f), spec.WallNames, -1f, 0f);
            Occupy(backMid - v * (depth * 0.25f), depth * 0.25f + 1f); // 안쪽 소품이 칸막이와 겹치지 않게
        }

        // ③ 탑: 뒷면 양 모서리, Towers=3이면 앞 모서리 하나 더(반반)
        if (spec.Towers >= 2)
        {
            BuildTower(parent, corners[0], yaw);
            BuildTower(parent, corners[1], yaw);
            if (spec.Towers >= 3 && Random.value < 0.5f) BuildTower(parent, corners[Random.value < 0.5f ? 2 : 3], yaw);
        }

        // ④ 쌓인 컨테이너 (안쪽 한 칸) + 바깥 옆면 탱크 더미
        if (spec.ContainerStack)
        {
            Vector2 stackXZ = center + u * (width * 0.25f * (Random.value < 0.5f ? -1f : 1f)) + v * (depth * 0.2f);
            GameObject lower = Spawn("Container_01", parent);
            if (lower != null)
            {
                AlignLongAxis(lower, u);
                SetOnGround(lower, stackXZ, 0f);
                ApplyPerchRule(lower.transform); // 위에 쌓기 전에 높이 단계를 맞춰 둔다 (나중에 맞추면 위 컨테이너와 겹침)
                Occupy(stackXZ, Footprint(lower));
                GameObject upper = Spawn("Container_01", parent);
                if (upper != null && Random.value < 0.7f)
                {
                    AlignLongAxis(upper, u);
                    upper.transform.rotation = Quaternion.Euler(0f, Random.Range(-12f, 12f), 0f) * upper.transform.rotation;
                    SetOnGround(upper, stackXZ, 0f);
                    upper.transform.position += Vector3.up * (GetBounds(lower).max.y - GetBounds(upper).min.y);
                }
                else if (upper != null) Object.DestroyImmediate(upper);
            }
        }
        if (spec.TankNames != null && Random.value < spec.TankChance)
        {
            GameObject tanks = Spawn(spec.TankNames[Random.Range(0, spec.TankNames.Length)], parent);
            if (tanks != null)
            {
                tanks.transform.rotation = yaw * tanks.transform.rotation;
                SetOnGround(tanks, corners[3] - u * 5f + v * (depth * 0.3f), 0f);
            }
        }

        // ⑤ 뒷벽 바깥 파이프 줄
        if (spec.PipeNames != null)
            BuildWallLine(parent, corners[0] + v * RuinPipeOffset, corners[1] + v * RuinPipeOffset, spec.PipeNames, -1f, 0f);

        // ⑥ 열린 앞면 바깥에 방벽 (가운데는 비움)
        Vector2 frontMid = (corners[2] + corners[3]) * 0.5f - v * 5f;
        int barriers = Random.Range(spec.FrontBarriersMin, spec.FrontBarriersMax + 1);
        for (int k = 0; k < barriers; k++)
        {
            float off = (k - (barriers - 1) * 0.5f) * 6f;
            if (Mathf.Abs(off) < 2f) off += 4f;
            GameObject bar = Spawn("Barrier_4m", parent);
            if (bar == null) continue;
            AlignLongAxis(bar, u);
            bar.transform.rotation = Quaternion.Euler(0f, Random.Range(-15f, 15f), 0f) * bar.transform.rotation;
            SetOnGround(bar, frontMid + u * off, 0.02f);
        }

        // ⑦ 안쪽 소품 + 모서리 조명
        Scatter(parent, spec.InsideNames, Random.Range(spec.InsideMin, spec.InsideMax + 1), center, 0f, Mathf.Min(width, depth) * 0.5f - 1.5f, 1f, 1f, 1f, 0f);
        GameObject lamp = Spawn(spec.LampName, parent);
        if (lamp != null)
        {
            lamp.transform.rotation = yaw * lamp.transform.rotation;
            SetOnGround(lamp, corners[3] + u * 1.5f + v * 1.5f, 0f);
            if (spec.LightRange > 0f)
            {
                Bounds lb = GetBounds(lamp);
                AddPointLight(parent, "CompoundLight", new Vector3(center.x, lb.max.y, center.y), spec.LightColor, spec.LightRange, 2f);
            }
        }

        // ⑧ 바깥 세로 포인트 (먼 데서도 보이는 실루엣): 뒷면 바깥 모서리부터 차례로
        Vector2[] accentSpots = { corners[1] + u * 4f + v * 2f, corners[0] - u * 4f + v * 2f, corners[1] + u * 4f - v * (depth * 0.5f) };
        for (int k = 0; k < spec.AccentCount && k < accentSpots.Length; k++)
        {
            GameObject accent = Spawn(spec.AccentNames[(k + Random.Range(0, spec.AccentNames.Length)) % spec.AccentNames.Length], parent);
            if (accent == null) continue;
            accent.transform.rotation = yaw * accent.transform.rotation;
            SetOnGround(accent, accentSpots[k], 0f);
        }

        OccupyBig(center, Mathf.Sqrt(width * width + depth * depth) * 0.5f + RuinPipeOffset + 6f); // 바깥 탱크·포인트까지
        return new CompoundFrame { Center = center, U = u, V = v, Width = width, Depth = depth };
    }

    /// <summary>BuildCompound가 실제로 놓은 구역의 위치·방향·크기 (옆에 건물을 붙일 때 씀)</summary>
    private struct CompoundFrame
    {
        public Vector2 Center, U, V;
        public float Width, Depth;
    }

    /// <summary>
    /// a에서 b까지 names 조각을 긴 변이 선을 따르게 이어 놓는다. entranceT(0~1)가 0 이상이면 그 위치에 입구를 비운다.
    /// brokenChance 확률로 조각을 빼 무너진 틈을 만든다.
    /// </summary>
    private static void BuildWallLine(Transform parent, Vector2 a, Vector2 b, string[] names, float entranceT, float brokenChance)
    {
        float len = Vector2.Distance(a, b);
        Vector2 dir = (b - a) / Mathf.Max(len, 0.001f);
        float entrance = entranceT * len;
        float pos = 0f;
        for (int k = 0; pos < len - 0.5f && k < 64; k++)
        {
            GameObject go = Spawn(names[k % names.Length], parent);
            if (go == null) break;
            float piece = AlignLongAxis(go, dir);
            bool inEntrance = entranceT >= 0f && pos < entrance + RuinEntranceHalf && pos + piece > entrance - RuinEntranceHalf;
            if (inEntrance || pos + piece > len + piece * 0.5f || Random.value < brokenChance)
                Object.DestroyImmediate(go);
            else
                SetOnGround(go, a + dir * (pos + piece * 0.5f), 0.05f);
            pos += piece;
        }
    }

    /// <summary>
    /// 콘크리트 블록 건물 하나: 받침(Concrete_wall2_Base1) 위에 탑 블록(Concrete_wall2_Tower_UP1B)을 얹은 덩어리를 격자로 붙여 세운다.
    /// 속이 꽉 찬 블록이라 내부·지붕·문·계단이 없고, 윗면이 3.1m 이상이라 올라설 수도 없다.
    /// (얇고 높은 벽 위에 지붕을 얹은 건물은 쓰지 않는다) 칸이 3개 이상이면 한 모서리 칸을 비워 ㄱ자 덩어리로 만든다.
    /// </summary>
    private static void BuildBlockBuilding(Transform parent, string label, Vector2 center, Vector2 u, Vector2 v, int cols, int rows)
    {
        Transform bld = Group(parent, "Building_" + label);
        Quaternion yaw = Quaternion.LookRotation(new Vector3(v.x, 0f, v.y), Vector3.up); // 로컬 +X = u, +Z = v
        Vector2 cell = TowerBlockSize(bld);
        int skipCorner = cols * rows >= 3 ? Random.Range(0, 4) : -1;
        for (int ix = 0; ix < cols; ix++)
        {
            for (int iz = 0; iz < rows; iz++)
            {
                bool corner = (ix == 0 || ix == cols - 1) && (iz == 0 || iz == rows - 1);
                int cornerId = (ix == 0 ? 0 : 1) + (iz == 0 ? 0 : 2);
                if (corner && cornerId == skipCorner) continue;
                Vector2 xz = center + u * ((ix + 0.5f - cols * 0.5f) * cell.x) + v * ((iz + 0.5f - rows * 0.5f) * cell.y);
                BuildTower(bld, xz, yaw);
            }
        }
        float w = cols * cell.x, d = rows * cell.y;
        OccupyBig(center, Mathf.Sqrt(w * w + d * d) * 0.5f + 1f);
    }

    /// <summary>받침·탑 블록 한 칸의 바닥 크기 (회전 전 X, Z 중 큰 쪽). 받침이 탑보다 넓으면 받침 기준.</summary>
    private static Vector2 TowerBlockSize(Transform parent)
    {
        Vector2 size = Vector2.zero;
        foreach (string name in new[] { "Concrete_wall2_Base1", "Concrete_wall2_Tower_UP1B" })
        {
            GameObject probe = Spawn(name, parent);
            if (probe == null) continue;
            Bounds b = GetBounds(probe);
            size = Vector2.Max(size, new Vector2(b.size.x, b.size.z));
            Object.DestroyImmediate(probe);
        }
        return size.x > 0.5f && size.y > 0.5f ? size : new Vector2(3f, 3f);
    }

    /// <summary>
    /// 구역 옆에 콘크리트 블록 건물을 붙인다. side=±1은 구역의 오른쪽(+u)/왼쪽(-u), 0은 뒤쪽(+v).
    /// </summary>
    private static void AttachBuilding(Transform parent, string label, CompoundFrame frame, int side, int cols, int rows)
    {
        Vector2 cell = TowerBlockSize(parent);
        float width = cols * cell.x, depth = rows * cell.y;
        Vector2 spot = side == 0
            ? frame.Center + frame.V * (frame.Depth * 0.5f + BuildingGap + depth * 0.5f)
            : frame.Center + frame.U * (side * (frame.Width * 0.5f + BuildingGap + width * 0.5f));
        BuildBlockBuilding(parent, label, spot, frame.U, frame.V, cols, rows);
    }

    /// <summary>콘크리트 탑: 받침(Concrete_wall2_Base1) 위에 탑(Concrete_wall2_Tower_UP1B)을 Bounds로 얹는다.</summary>
    private static void BuildTower(Transform parent, Vector2 xz, Quaternion yaw)
    {
        GameObject foot = Spawn("Concrete_wall2_Base1", parent);
        if (foot == null) return;
        foot.transform.rotation = yaw * foot.transform.rotation;
        SetOnGround(foot, xz, 0.05f);
        ApplyPerchRule(foot.transform, true); // 탑을 얹기 전에 받침 높이 단계를 맞춰 둔다 (받침이 탑보다 넓으면 그 턱에 올라서지 못하게)
        GameObject tower = Spawn("Concrete_wall2_Tower_UP1B", parent);
        if (tower == null) return;
        tower.transform.rotation = yaw * tower.transform.rotation;
        SetOnGround(tower, xz, 0f);
        tower.transform.position += Vector3.up * (GetBounds(foot).max.y - GetBounds(tower).min.y);
    }

    /// <summary>회전 전 Bounds의 긴 변(X 또는 Z)이 dir을 향하도록 돌리고 그 길이를 돌려준다.</summary>
    private static float AlignLongAxis(GameObject go, Vector2 dir)
    {
        Bounds b = GetBounds(go);
        bool alongX = b.size.x >= b.size.z;
        float phi = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        go.transform.rotation = Quaternion.Euler(0f, alongX ? -phi : 90f - phi, 0f) * go.transform.rotation;
        return Mathf.Max(Mathf.Max(b.size.x, b.size.z), 0.5f);
    }

    /// <summary>
    /// 필드 대형 엄폐물: 들판 곳곳에 높이 4~8m 바위 지형이나 기지 잔해(쓰러진 탱크·컨테이너·벽 조각)를 놓고 둘레에 작은 바위를 붙인다.
    /// 보스 이동을 막지 않도록 서로 FieldCoverSpacing 이상 띄우고, 순환로·착륙장 안쪽·분화구→착륙장 통로·산비탈은 피한다.
    /// </summary>
    /// <param name="minCenterDist">착륙장 중심에서 이보다 가까운 곳은 쓰지 않음</param>
    /// <param name="maxCenterDist">착륙장 중심에서 이보다 먼 곳은 쓰지 않음 (순환로 안쪽 띠만 채울 때)</param>
    private static void BuildFieldCover(Transform parent, string label, int count, float wreckChance, float minHeight, float maxHeight,
        float reach, float minCenterDist, float maxCenterDist)
    {
        float baseY = GroundY(Center);
        _extraFilter = (xz, r) =>
        {
            float d = Vector2.Distance(xz, Center);
            return GroundY(xz) < baseY + PlayableRise && d > minCenterDist + r && d < maxCenterDist - r
                   && DistanceToSegment(xz, CraterCenter, Center) > BossCorridorHalf + r;
        };

        Vector3 tp = _terrain.transform.position;
        Vector3 size = _terrain.terrainData.size;
        int placed = 0;
        for (int attempt = 0; attempt < count * 4 && placed < count; attempt++)
        {
            bool wreck = Random.value < wreckChance;
            string[] rocks = maxCenterDist < 1000f ? InnerRockNames : FieldRockNames;
            string name = wreck ? FieldWreckNames[Random.Range(0, FieldWreckNames.Length)] : rocks[Random.Range(0, rocks.Length)];
            GameObject go = Spawn(name, parent);
            if (go == null) continue;

            Quaternion yaw = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            if (wreck && System.Array.IndexOf(FieldToppledNames, name) >= 0)
                go.transform.rotation = yaw * Quaternion.Euler(0f, 0f, 90f) * go.transform.rotation; // 옆으로 쓰러진 탱크
            else
                go.transform.rotation = yaw * go.transform.rotation;
            if (!wreck)
            {
                float h = Mathf.Max(GetBounds(go).size.y, 0.01f);
                go.transform.localScale *= Mathf.Clamp(Random.Range(minHeight, maxHeight) / h, 0.5f, 6f);
            }

            float r = Footprint(go);
            bool ok = false;
            for (int t = 0; t < PlaceTries && !ok; t++)
            {
                // 띠(maxCenterDist가 유한)는 띠 안에서, 아니면 지형 전체에서 뽑는다. (좁은 띠를 지형 전체에서 뽑으면 거의 안 걸림)
                Vector2 xz;
                if (maxCenterDist < 1000f)
                {
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    xz = Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(minCenterDist + r, maxCenterDist - r);
                }
                else xz = new Vector2(Random.Range(tp.x + 10f, tp.x + size.x - 10f), Random.Range(tp.z + 10f, tp.z + size.z - 10f));
                if (!IsFree(xz, r + reach, FieldCoverClearance, MaxPropSlope) || !FarFromBig(xz, r + reach, FieldCoverSpacing)) continue;
                SetOnGround(go, xz, wreck ? 0.12f : 0.2f);
                Occupy(xz, r);
                ok = true;

                // 둘레에 작은 바위를 붙여 땅에서 솟은 느낌을 낸다. 그다음 둘레까지 차지한 것으로 기록해 다음 엄폐물과 간격을 지킨다.
                if (reach >= 1f)
                    Scatter(parent, FieldCompanionNames, Random.Range(1, 4), xz, r + 0.5f, r + reach - 0.5f, 0.3f, 0.8f, 1.5f, 0.25f);
                OccupyBig(xz, r + reach);
            }
            if (ok) placed++;
            else Object.DestroyImmediate(go);
        }
        _extraFilter = null;
        Debug.Log($"[SciFiMapBuilder] {label} 엄폐물 {placed}/{count}개 배치");
    }

    /// <summary>들판 곳곳의 중간 크기 돌(1~2m). 걸려 넘지 못하는 장애물이라 Props 아래에 두고 순환로·착륙장은 피한다.</summary>
    private static void BuildMediumRocks(Transform parent)
    {
        float baseY = GroundY(Center);
        _extraFilter = (xz, r) => GroundY(xz) < baseY + PlayableRise;
        Vector3 tp = _terrain.transform.position;
        Vector3 size = _terrain.terrainData.size;
        for (int k = 0; k < MediumRockCount; k++)
        {
            Vector2 c = new Vector2(Random.Range(tp.x + 10f, tp.x + size.x - 10f), Random.Range(tp.z + 10f, tp.z + size.z - 10f));
            Scatter(parent, MediumRockNames, 1, c, 0f, 6f, 2f, 0.7f, 1.4f, 0.25f);
        }
        _extraFilter = null;
    }

    /// <summary>
    /// 작은 돌·자갈 무더기. 발에 걸리지 않게 콜라이더를 끄고, Props 밖(Details)에 두어 NavMesh·콜라이더 정리에서 빠진다.
    /// 순환로 위에도 흩어 길이 너무 깨끗하지 않게 하되, 착륙장 바닥판과 다른 소품 위는 피한다.
    /// </summary>
    private static void BuildPebbles(Transform parent)
    {
        float baseY = GroundY(Center);
        Vector3 tp = _terrain.transform.position;
        Vector3 size = _terrain.terrainData.size;
        int made = 0;
        for (int attempt = 0; attempt < PebbleClusterCount * 20 && made < PebbleClusterCount; attempt++)
        {
            Vector2 c = new Vector2(Random.Range(tp.x + 10f, tp.x + size.x - 10f), Random.Range(tp.z + 10f, tp.z + size.z - 10f));
            if (GroundY(c) > baseY + PebbleMaxRise || Vector2.Distance(c, Center) < PadRadius + 2f) continue; // 산 위·착륙장이면 다시 뽑음
            made++;
            int n = Random.Range(PebblesPerClusterMin, PebblesPerClusterMax + 1);
            for (int i = 0; i < n; i++)
            {
                Vector2 xz = c + Random.insideUnitCircle * PebbleClusterRadius;
                GameObject go = Spawn(PebbleNames[Random.Range(0, PebbleNames.Length)], parent);
                if (go == null) continue;
                go.transform.localScale *= Random.Range(0.6f, 1.6f);
                go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * go.transform.rotation;
                if (Overlaps(xz, Footprint(go)))
                {
                    Object.DestroyImmediate(go);
                    continue;
                }
                foreach (Collider col in go.GetComponentsInChildren<Collider>()) col.enabled = false;
                SetOnGround(go, xz, 0.3f);
            }
        }
    }

    /// <summary>
    /// 간이 공항 유도로: 착륙장 바닥판 가장자리에서 격납고 입구까지 도로판을 잇고 양옆에 붉은 유도등을 세운다.
    /// 도로판은 밟는 바닥(장애물 아님)이고, 유도로 띠는 점유로 기록해 착륙장 엄폐물 링이 그 위에 놓이지 않게 한다.
    /// </summary>
    private static void BuildTaxiway(Transform parent)
    {
        Vector2 camp = SiteCenters[SiteCamp];
        Vector2 dir = (camp - Center).normalized;
        Vector2 start = Center + dir * (PadRadius + 0.5f);
        Vector2 end = camp - dir * (CampSpec.Depth * 0.5f);
        float len = Vector2.Distance(start, end);
        if (len < 2f) return;

        BuildWallLine(parent, start, end, TaxiwayNames, -1f, 0f);
        for (float t = 3f; t < len; t += 3f) Occupy(start + dir * t, TaxiwayLampSide + 1f);

        Vector2 side = new Vector2(-dir.y, dir.x);
        for (float t = TaxiwayLampSpacing * 0.5f; t < len; t += TaxiwayLampSpacing)
        {
            foreach (float s in new[] { -1f, 1f })
                PlaceGuideLamp(parent, start + dir * t + side * (s * TaxiwayLampSide), 4f, 1.5f);
        }
    }

    /// <summary>
    /// 연구소 앞에서 분화구 테두리까지 이어지는 시료 파이프라인 (연구소가 드릴·분화구를 관측하던 흔적).
    /// 사이사이 끊어 두어 사람·몬스터가 넘나들 틈을 남기고, 중간에 조명 하나를 세운다.
    /// </summary>
    private static void BuildLabPipeline(Transform parent)
    {
        Vector2 lab = SiteCenters[SiteLab];
        Vector2 dir = (CraterCenter - lab).normalized;
        Vector2 start = lab + dir * (LabSpec.Depth * 0.5f + 7f);             // 앞 방벽 바깥부터
        Vector2 end = CraterCenter - dir * (CraterRadius + CraterRimWidth);  // 분화구 테두리까지
        if (Vector2.Dot(end - start, dir) <= 2f) return;
        BuildWallLine(parent, start, end, LabPipelineNames, -1f, LabPipelineGapChance);
        GameObject lamp = Spawn("Lamp_05", parent);
        if (lamp != null) SetOnGround(lamp, (start + end) * 0.5f + new Vector2(-dir.y, dir.x) * 2f, 0f);
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>외곽 경계: 중심에서 방향마다 바깥으로 걸어가 산기슭(또는 지형 끝)을 찾고, 그 둘레를 따라 외벽 구간과 바위 구간을 번갈아 놓는다.</summary>
    private static void BuildBoundary(Transform parent)
    {
        float baseY = GroundY(Center);
        List<Vector2> pts = new List<Vector2>();
        for (int k = 0; k < BoundarySamples; k++)
        {
            float a = k * Mathf.PI * 2f / BoundarySamples;
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            float d = RingRadius + RingHalfWidth;
            while (InsideTerrain(Center + dir * (d + 1f), EdgeMargin) && GroundY(Center + dir * (d + 1f)) < baseY + BoundaryRise) d += 1f;
            pts.Add(Center + dir * (d - BoundaryInset));
        }

        float carry = 0f; // 앞 구간에서 넘어온 거리
        for (int k = 0; k < pts.Count; k++)
        {
            Vector2 p0 = pts[k], p1 = pts[(k + 1) % pts.Count];
            float seg = Vector2.Distance(p0, p1);
            Vector2 dir = (p1 - p0) / Mathf.Max(seg, 0.001f);
            float t = carry;
            while (t < seg)
            {
                Vector2 xz = p0 + dir * t;
                bool wall = Mathf.PerlinNoise(xz.x * BoundaryNoiseScale + 300f, xz.y * BoundaryNoiseScale + 300f) > BoundaryWallThreshold;
                t += wall ? PlaceBoundaryWall(parent, xz, dir) : PlaceBoundaryRocks(parent, xz);
            }
            carry = t - seg;
        }
    }

    /// <summary>start에서 dir 방향으로 외벽 한 칸을 놓고 그 길이를 돌려준다. 가끔 무너진 틈(빈칸)이나 기울어진 벽을 섞는다.</summary>
    private static float PlaceBoundaryWall(Transform parent, Vector2 start, Vector2 dir)
    {
        GameObject go = Spawn(BoundaryWallNames[Random.Range(0, BoundaryWallNames.Length)], parent);
        if (go == null) return BoundaryRockStep;
        Bounds b = GetBounds(go);
        bool alongX = b.size.x >= b.size.z;
        float len = Mathf.Max(Mathf.Max(b.size.x, b.size.z), 2f);
        if (Random.value < BoundaryWallGapChance)
        {
            Object.DestroyImmediate(go);
            return len;
        }

        // 긴 변이 둘레 방향을 따르도록 돌린다. (Unity yaw θ에서 로컬 +X는 (cosθ, -sinθ), 로컬 +Z는 (sinθ, cosθ))
        float phi = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float yaw = alongX ? -phi : 90f - phi;
        float lean = Random.value < 0.2f ? Random.Range(-12f, 12f) : 0f;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(lean, 0f, 0f) * go.transform.rotation;

        Vector2 xz = PullInsideTerrain(start + dir * (len * 0.5f), len * 0.5f);
        if (Overlaps(xz, len * 0.5f))
        {
            Object.DestroyImmediate(go);
            return len;
        }
        SetOnGround(go, xz, 0.1f);
        return len;
    }

    /// <summary>xz 근처에 바위 2~3개 무더기를 놓고 다음 칸까지 거리를 돌려준다.</summary>
    private static float PlaceBoundaryRocks(Transform parent, Vector2 xz)
    {
        int count = Random.Range(2, 4);
        for (int k = 0; k < count; k++)
        {
            Vector2 p = xz + Random.insideUnitCircle * 3f;
            GameObject go = Spawn(BoundaryRockNames[Random.Range(0, BoundaryRockNames.Length)], parent);
            if (go == null) continue;
            go.transform.localScale *= Random.Range(1.2f, 2.2f);
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * go.transform.rotation;
            p = PullInsideTerrain(p, Footprint(go));
            if (Overlaps(p, Footprint(go)))
            {
                Object.DestroyImmediate(go);
                continue;
            }
            SetOnGround(go, p, 0.25f);
        }
        return BoundaryRockStep;
    }

    /// <summary>
    /// 반경 r 소품이 지형 경계(EdgeMargin 포함) 밖으로 삐져나오면 맵 중심 쪽으로 끌어들인다.
    /// 경계 지점이 지형 끝에 닿은 방향(산기슭이 없는 쪽)에서 크게 키운 바위가 지형 밖에 떠 있던 문제를 막는다.
    /// </summary>
    private static Vector2 PullInsideTerrain(Vector2 xz, float r)
    {
        for (int step = 0; step < 100 && !InsideTerrain(xz, r + EdgeMargin); step++)
            xz = Vector2.MoveTowards(xz, Center, 0.5f);
        return xz;
    }

    private static bool InsideTerrain(Vector2 xz, float margin)
    {
        Vector3 tp = _terrain.transform.position;
        Vector3 size = _terrain.terrainData.size;
        return xz.x > tp.x + margin && xz.x < tp.x + size.x - margin && xz.y > tp.z + margin && xz.y < tp.z + size.z - margin;
    }

    private static bool Overlaps(Vector2 xz, float r)
    {
        foreach (Vector3 o in Occupied)
            if (Vector2.Distance(xz, new Vector2(o.x, o.y)) < r + o.z) return true;
        return false;
    }

    /// <summary>바닥 유도등(Lamp_Position)을 xz에 세우고 꼭대기에 붉은 점광원을 단다.</summary>
    internal static void PlaceGuideLamp(Transform parent, Vector2 xz, float range, float intensity)
    {
        GameObject lamp = Spawn("Lamp_Position", parent);
        if (lamp == null) return;
        SetOnGround(lamp, xz, 0f);
        Bounds lb = GetBounds(lamp);
        AddPointLight(lamp.transform, "GuideLight", new Vector3(lb.center.x, lb.max.y + 0.3f, lb.center.z), GuideLightColor, range, intensity);
    }

    /// <summary>바닥판의 Bounds 중심을 xz에 맞추고 윗면을 topY 높이에 둔다.</summary>
    internal static void PlaceTile(GameObject go, Vector2 xz, float topY)
    {
        Bounds b = GetBounds(go);
        go.transform.position += new Vector3(xz.x - b.center.x, topY - b.max.y, xz.y - b.center.z);
    }

    internal static void AddPointLight(Transform parent, string name, Vector3 position, Color color, float range, float intensity)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
    }

    /// <summary>
    /// center 주변 반경 [rMin, rMax] 안에 names 중 무작위 프리펩을 count개 배치한다.
    /// 자리마다 최대 PlaceTries번 시도하며, IsFree를 통과하지 못하면 그 개체는 버린다.
    /// </summary>
    internal static void Scatter(Transform parent, string[] names, int count, Vector2 center, float rMin, float rMax,
        float clearance, float scaleMin, float scaleMax, float sinkRatio, float maxSlope = MaxPropSlope)
    {
        for (int k = 0; k < count; k++)
        {
            GameObject go = Spawn(names[Random.Range(0, names.Length)], parent);
            if (go == null) continue;
            go.transform.localScale *= Random.Range(scaleMin, scaleMax);
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * go.transform.rotation;
            float r = Footprint(go);

            bool placed = false;
            for (int t = 0; t < PlaceTries && !placed; t++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float d = Mathf.Sqrt(Random.Range(rMin * rMin, rMax * rMax)); // 면적 기준 고르게
                Vector2 xz = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                if (!IsFree(xz, r, clearance, maxSlope)) continue;
                SetOnGround(go, xz, sinkRatio);
                Occupy(xz, r);
                placed = true;
            }
            if (!placed) Object.DestroyImmediate(go);
        }
    }

    /// <summary>
    /// 보스 장판·몬스터 돌진을 피할 전용 엄폐물을 지점마다 CoverMinCount~CoverMaxCount개 배치한다. (지점 그룹 아래 "Cover")
    /// 높이를 CoverTallMinHeight~MaxHeight로 맞추고, 그래도 폭이 CoverMinWidth보다 좁은 프리펩은 쓰지 않는다.
    /// 엄폐물끼리는 CoverSpacing 이상 띄워 사이로 지나갈 수 있게 한다. filter가 false인 자리는 쓰지 않는다.
    /// </summary>
    internal static void PlaceCover(Transform site, string[] names, Vector2 center, float rMin, float rMax, float maxSlope,
        System.Func<Vector2, bool> filter)
    {
        Transform parent = Group(site, "Cover");
        int target = Random.Range(CoverMinCount, CoverMaxCount + 1);
        int placed = 0;
        for (int attempt = 0; attempt < target * 4 && placed < target; attempt++)
        {
            GameObject go = Spawn(names[Random.Range(0, names.Length)], parent);
            if (go == null) continue;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * go.transform.rotation;
            // 목표 높이로 맞추되, 폭이 모자라면(좁고 높은 바위 등) 폭 기준으로 더 키운다. 너무 높아지면 쓰지 않는다.
            Bounds b = GetBounds(go);
            float byHeight = Random.Range(CoverTallMinHeight, CoverTallMaxHeight) / Mathf.Max(b.size.y, 0.01f);
            float byWidth = CoverMinWidth / Mathf.Max(Mathf.Max(b.size.x, b.size.z), 0.01f);
            go.transform.localScale *= Mathf.Max(byHeight, byWidth);
            if (GetBounds(go).size.y > CoverHardMaxHeight)
            {
                Object.DestroyImmediate(go);
                continue;
            }

            float r = Footprint(go);
            bool ok = false;
            for (int t = 0; t < PlaceTries && !ok; t++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                Vector2 xz = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Mathf.Sqrt(Random.Range(rMin * rMin, rMax * rMax));
                if ((filter != null && !filter(xz)) || !IsFree(xz, r, CoverSpacing, maxSlope)) continue;
                SetOnGround(go, xz, 0.05f);
                Occupy(xz, r);
                ok = true;
            }
            if (ok) placed++;
            else Object.DestroyImmediate(go);
        }

        if (placed < CoverMinCount)
            Debug.LogWarning($"[SciFiMapBuilder] {site.name}: 회피용 엄폐물을 {placed}개만 배치했습니다. (최소 {CoverMinCount}개) 반경·후보 프리펩을 조정하세요.");
    }

    /// <summary>착륙장 동서남북 진입 통로(각도 ±CoverGateHalfAngle) 안인지</summary>
    private static bool InPadGate(Vector2 xz)
    {
        float angle = Mathf.Atan2(xz.y - Center.y, xz.x - Center.x) * Mathf.Rad2Deg;
        float fromGate = Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 90f) * 90f));
        return fromGate < CoverGateHalfAngle;
    }

    /// <summary>맵 안쪽·완만한 경사이고, 광장 중심·순환로·다른 소품과 겹치지 않는 자리인지</summary>
    private static bool IsFree(Vector2 xz, float r, float clearance, float maxSlope)
    {
        Vector3 tp = _terrain.transform.position;
        Vector3 size = _terrain.terrainData.size;
        if (xz.x - r < tp.x + EdgeMargin || xz.x + r > tp.x + size.x - EdgeMargin) return false;
        if (xz.y - r < tp.z + EdgeMargin || xz.y + r > tp.z + size.z - EdgeMargin) return false;
        if (_terrain.terrainData.GetSteepness((xz.x - tp.x) / size.x, (xz.y - tp.z) / size.z) > maxSlope) return false;

        if (_useSiteRules)
        {
            float d = Vector2.Distance(xz, Center);
            if (d < PadRadius + 1f + r) return false;
            if (Mathf.Abs(d - RingRadius) < RingHalfWidth + r) return false;
        }
        if (_extraFilter != null && !_extraFilter(xz, r)) return false;

        foreach (Vector3 o in Occupied)
            if (Vector2.Distance(xz, new Vector2(o.x, o.y)) < r + o.z + clearance) return false;
        return true;
    }

    internal static void Occupy(Vector2 xz, float r) => Occupied.Add(new Vector3(xz.x, xz.y, r));

    /// <summary>
    /// 큰 물체(구역·추락선·드릴·필드 대형 엄폐물)로 기록한다. 보스 통로 간격(FieldCoverSpacing)은 큰 물체끼리만 따진다.
    /// (작은 통·돌까지 18m씩 비우면 들판에 큰 엄폐물 놓을 자리가 거의 남지 않는다)
    /// </summary>
    private static void OccupyBig(Vector2 xz, float r)
    {
        Occupy(xz, r);
        BigOccupied.Add(new Vector3(xz.x, xz.y, r));
    }

    private static bool FarFromBig(Vector2 xz, float r, float spacing)
    {
        foreach (Vector3 o in BigOccupied)
            if (Vector2.Distance(xz, new Vector2(o.x, o.y)) < r + o.z + spacing) return false;
        return true;
    }

    /// <summary>
    /// 다른 빌더(TutorialMapBuilder)가 이 파일의 배치 도우미를 쓰기 전에 부른다.
    /// siteRules=false면 인게임 맵의 광장·순환로 금지 구역을 끄고, extraFilter(xz, 반경)로 자기 금지 구역을 넣을 수 있다.
    /// </summary>
    internal static void BeginPlacement(Terrain terrain, bool siteRules, System.Func<Vector2, float, bool> extraFilter = null)
    {
        _terrain = terrain;
        Occupied.Clear();
        BigOccupied.Clear();
        _useSiteRules = siteRules;
        _extraFilter = extraFilter;
    }

    internal static Transform Group(Transform parent, string name)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    /// <summary>지점 그룹은 지점 중심(XZ)에 둔다. 씬에서 이 그룹을 옮기면 저장 시 새 중심으로 기록된다.</summary>
    private static Transform SiteGroup(Transform parent, int site)
    {
        Transform t = Group(parent, SiteNames[site]);
        t.position = new Vector3(SiteCenters[site].x, 0f, SiteCenters[site].y);
        return t;
    }

    internal static GameObject Spawn(string name, Transform parent)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + name + ".prefab");
        if (prefab == null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RockDir + name + ".prefab");
        if (prefab == null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GUPrefabDir + name + ".prefab");
        if (prefab == null)
        {
            Debug.LogWarning($"[SciFiMapBuilder] 프리펩을 찾지 못함: {name}");
            return null;
        }
        if (System.Array.IndexOf(ExcludedPrefabNames, name) >= 0)
        {
            Debug.LogWarning($"[SciFiMapBuilder] 맵 컨셉에 맞지 않아 배치하지 않음: {name}");
            return null;
        }
        if (HasClimbablePart(prefab))
        {
            Debug.LogWarning($"[SciFiMapBuilder] 올라설 수 있는 부품(계단·사다리·발판)이 있어 배치하지 않음: {name}");
            return null;
        }
        return (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
    }

    /// <summary>
    /// 맵 핵심 설계(플레이어가 몬스터가 못 오는 높은 곳에서 사격하지 못하게) 때문에 쓰지 않는 프리펩인지.
    /// 자식 이름에 계단·사다리·발판·캣워크·경사로·엘리베이터가 있거나, 프리펩 이름 자체가 그런 구조물이면 true.
    /// </summary>
    private static bool HasClimbablePart(GameObject prefab)
    {
        if (ClimbableCache.TryGetValue(prefab, out bool cached)) return cached;
        bool found = ClimbablePattern.IsMatch(prefab.name);
        foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
            if (!found && ClimbablePattern.IsMatch(t.name)) found = true;
        ClimbableCache[prefab] = found;
        return found;
    }

    private static readonly string[] ExcludedPrefabNames = { "Radar_Sphere" }; // 맵 컨셉(추락한 굴착 전초기지)에 맞지 않음
    private static readonly Regex ClimbablePattern = new Regex("stair|ladder|catwalk|platform|ramp|elevator", RegexOptions.IgnoreCase);
    private static readonly Dictionary<GameObject, bool> ClimbableCache = new Dictionary<GameObject, bool>();

    /// <summary>메시 Renderer들의 Bounds. 파티클(연기 등)은 수십 m로 부풀어 있어 뺀다. 메시가 없으면 피벗의 0.5m 상자.</summary>
    internal static Bounds GetBounds(GameObject go) =>
        TryGetBounds(go, out Bounds b) ? b : new Bounds(go.transform.position, Vector3.one * 0.5f);

    internal static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (found) bounds.Encapsulate(r.bounds);
            else { bounds = r.bounds; found = true; }
        }
        return found;
    }

    internal static float Footprint(GameObject go)
    {
        Bounds b = GetBounds(go);
        return Mathf.Max(b.extents.x, b.extents.z);
    }

    private static void ClampHeight(GameObject go, float maxHeight)
    {
        float h = GetBounds(go).size.y;
        if (h > maxHeight) go.transform.localScale *= maxHeight / h;
    }

    internal static float GroundY(Vector2 xz) => _terrain.SampleHeight(new Vector3(xz.x, 0f, xz.y)) + _terrain.transform.position.y;

    /// <summary>Bounds 중심을 xz에 맞추고, 바닥 네 모서리·중심 중 가장 낮은 지면에 닿게(sinkRatio만큼 파묻어) 놓는다.</summary>
    internal static void SetOnGround(GameObject go, Vector2 xz, float sinkRatio)
    {
        Bounds b = GetBounds(go);
        go.transform.position += new Vector3(xz.x - b.center.x, 0f, xz.y - b.center.z);
        b = GetBounds(go);
        float ground = GroundY(xz);
        ground = Mathf.Min(ground, GroundY(new Vector2(b.min.x, b.min.z)));
        ground = Mathf.Min(ground, GroundY(new Vector2(b.min.x, b.max.z)));
        ground = Mathf.Min(ground, GroundY(new Vector2(b.max.x, b.min.z)));
        ground = Mathf.Min(ground, GroundY(new Vector2(b.max.x, b.max.z)));
        go.transform.position += Vector3.up * (ground - b.min.y - b.size.y * sinkRatio);
    }

    // ───────────────────────── 씬 ─────────────────────────

    /// <summary>활성 씬의 기존 SciFiMap을 지우고 새 인스턴스를 배치한다. 겹치지 않도록 Map.prefab 인스턴스는 비활성화한다.</summary>
    private static void ReplaceInActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == "SciFiMap")
            {
                Undo.DestroyObjectImmediate(go);
            }
            else if (go.activeSelf && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == SourceMapPath)
            {
                Undo.RecordObject(go, "Disable Map");
                go.SetActive(false);
            }
        }

        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(SavePath);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
        Undo.RegisterCreatedObjectUndo(instance, "Build Sci-Fi Map");
        Selection.activeGameObject = instance;
    }

    /// <summary>
    /// Props 아래 장애물 소품(프리펩 인스턴스)마다, 소품 방향에 맞춘 로컬 Bounds 상자로
    /// - 콜라이더가 하나도 없으면 BoxCollider를 붙인다. (Rock_01 등은 원본에 콜라이더가 없어 몬스터·플레이어가 통과함)
    /// - NavMeshModifierVolume(Not Walkable)을 붙인다. 소품 윗면이나 땅에 박힌 소품 속 지면이 NavMesh 섬으로 남아
    ///   몬스터가 그 위로 소환(SnapToNavMesh)되는 것을 막는다.
    /// 착륙장 바닥판·유도등은 밟고 다니는 바닥이라 뺀다. 소품마다 볼록 메시 콜라이더로 감싸지 않는 이유는
    /// 크레인·기울어진 드릴 탑 같은 소품이 큰 덩어리가 되어 통로를 막고, 소품 수백 개마다 메시 에셋이 생기기 때문이다.
    /// </summary>
    internal static void CleanupColliders(Transform root)
    {
        Transform props = root.Find("Props");
        if (props == null) return;
        List<Transform> obstacles = new List<Transform>();
        CollectObstacles(props, obstacles);

        int notWalkable = UnityEngine.AI.NavMesh.GetAreaFromName("Not Walkable");
        int boxes = 0, volumes = 0, resized = 0;
        foreach (Transform prop in obstacles)
        {
            if (ApplyPerchRule(prop)) resized++;
            if (!TryGetLocalBounds(prop, out Bounds local)) continue;

            bool hasCollider = false;
            foreach (Collider c in prop.GetComponentsInChildren<Collider>())
                if (c.enabled && !c.isTrigger) { hasCollider = true; break; }
            if (!hasCollider)
            {
                BoxCollider box = prop.gameObject.AddComponent<BoxCollider>();
                box.center = local.center;
                box.size = local.size;
                boxes++;
            }

            NavMeshModifierVolume volume = prop.gameObject.AddComponent<NavMeshModifierVolume>();
            volume.center = local.center;
            volume.size = local.size;
            volume.area = notWalkable;
            volumes++;
        }
        Debug.Log($"[SciFiMapBuilder] 콜라이더 정리: 장애물 {obstacles.Count}개, 높이 단계 맞춤 {resized}, BoxCollider 추가 {boxes}, NavMesh 제외 볼륨 {volumes}");
    }

    /// <summary>
    /// 높이 단계 규칙: 장애물 윗면 높이(지면 기준)가 올라설 수 있는 높이면 가까운 단계(PerchMid 띠 또는 PerchTallMin)로
    /// 바닥 중심을 고정한 채 크기를 맞춘다. 공중에 뜬 부품은 건드리지 않는다. 크기를 바꿨으면 true.
    /// </summary>
    private static bool ApplyPerchRule(Transform prop, bool force = false)
    {
        // 드릴 탑·블록 건물은 부품을 맞물려 쌓은 것이라 나중에 크기를 따로 바꾸면 어긋난다 (블록 받침은 쌓기 전에 force로 맞춤)
        if (!force && prop.parent != null && (prop.parent.name == "Drill" || prop.parent.name.StartsWith("Building_"))) return false;
        Bounds b = GetBounds(prop.gameObject);
        float ground = GroundY(new Vector2(b.center.x, b.center.z));
        if (b.min.y - ground > PerchAirborne || b.size.y < 0.01f) return false;

        float top = b.max.y - ground;
        float target;
        if (top <= PerchLowMax || (top >= PerchMidMin && top <= PerchMidMax) || top >= PerchTallMin) return false;
        else if (top < PerchMidMin) target = PerchMidMin;
        else target = top < (PerchMidMax + PerchTallMin) * 0.5f ? PerchMidMax : PerchTallMin;

        // 바닥(b.min.y)은 그대로 두고 전체 높이를 늘이거나 줄여 윗면이 target에 오게 한다.
        float scale = (target + ground - b.min.y) / b.size.y;
        // 배율이 크면(납작한 태양광 패널이 4배로 커지는 등) 오히려 어색하다. 범위를 넘으면 크기를 바꾸지 않는다.
        if (scale < PerchScaleMin || scale > PerchScaleMax) return false;
        Vector3 bottom = new Vector3(b.center.x, b.min.y, b.center.z);
        prop.localScale *= scale;
        Bounds after = GetBounds(prop.gameObject);
        prop.position += bottom - new Vector3(after.center.x, after.min.y, after.center.z);
        return true;
    }

    /// <summary>프리펩 인스턴스 루트를 장애물 하나로 모은다. (그룹은 내려가고, 바닥판·유도등은 뺌)</summary>
    private static void CollectObstacles(Transform parent, List<Transform> result)
    {
        foreach (Transform t in parent)
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
            {
                CollectObstacles(t, result);
                continue;
            }
            if (t.parent != null && t.parent.name.StartsWith("Building_"))
            {
                result.Add(t); // 건물 부품은 바닥판 프리펩이라도(콘크리트 슬래브 지붕) 장애물: 지붕 위 NavMesh·몬스터 소환을 막는다
                continue;
            }
            if (IsWalkFloor(t)) continue;
            result.Add(t);
        }
    }

    /// <summary>땅에 깔린 밟는 바닥판·가는 조명인지 본다. 건물 부품(슬래브 지붕)은 호출자가 먼저 걸러야 한다.</summary>
    private static bool IsWalkFloor(Transform prefabRoot)
    {
        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prefabRoot.gameObject);
        string sourceName = source != null ? source.name : prefabRoot.name;
        return sourceName.StartsWith("Hangar_Ground_Plate") || sourceName.StartsWith("Concrete_Ground") || sourceName.StartsWith("Concrete_Road")
            || sourceName.StartsWith("Ground_Plate") || sourceName.StartsWith("Lamp_");
    }

    /// <summary>Building_ 밖의 밟는 바닥판 프리펩 인스턴스를 모은다. (CollectObstacles가 빼는 것과 같은 기준)</summary>
    private static void CollectWalkFloors(Transform parent, List<Transform> result)
    {
        foreach (Transform t in parent)
        {
            if (t.name.StartsWith("Building_")) continue;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) CollectWalkFloors(t, result);
            else if (IsWalkFloor(t) && !t.name.StartsWith("Lamp_")) result.Add(t);
        }
    }

    /// <summary>prop의 모든 메시 정점 범위를 prop 로컬 공간 상자로 구한다. (메시가 없으면 false)</summary>
    private static bool TryGetLocalBounds(Transform prop, out Bounds local)
    {
        local = default;
        bool found = false;
        foreach (MeshFilter filter in prop.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            if (!filter.TryGetComponent(out MeshRenderer renderer) || !renderer.enabled) continue;
            Bounds mb = filter.sharedMesh.bounds;
            for (int k = 0; k < 8; k++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                Vector3 p = prop.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (found) local.Encapsulate(p);
                else { local = new Bounds(p, Vector3.zero); found = true; }
            }
        }
        return found;
    }

    /// <summary>
    /// 기존 Map의 NavMeshSurface 설정(에이전트·레이어·콜라이더 기준)을 그대로 써서 SciFiMap 자식만 대상으로 굽는다.
    /// 구운 데이터는 에셋으로 저장해 프리펩이 참조하게 한다. 씬에 인스턴스가 켜질 때 NavMesh가 등록된다.
    /// 걷는 면은 지형(과 땅에 깔린 바닥판)뿐이다. 나머지는 NavMeshModifier(Not Walkable)로 장애물로만 남겨,
    /// 소품·건물·바위 윗면에 NavMesh 섬이 생기고 몬스터가 그 위로 소환(SnapToNavMesh)되는 것을 막는다.
    /// </summary>
    internal static void BakeNavMesh(GameObject root, Terrain source, string dataPath)
    {
        ApplyWalkableRule(root);
        NavMeshSurface surface = root.GetComponent<NavMeshSurface>();
        if (surface == null) surface = root.AddComponent<NavMeshSurface>();
        NavMeshSurface template = source != null ? source.GetComponent<NavMeshSurface>() : surface;
        if (template != null)
        {
            surface.agentTypeID = template.agentTypeID;
            surface.layerMask = template.layerMask;
            surface.useGeometry = template.useGeometry;
            surface.defaultArea = template.defaultArea;
        }
        else
        {
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        }
        surface.collectObjects = CollectObjects.Children; // 씬의 Player 등 다른 오브젝트는 빼고 맵만
        surface.BuildNavMesh();

        AssetDatabase.DeleteAsset(dataPath);
        AssetDatabase.CreateAsset(surface.navMeshData, dataPath);
    }

    /// <summary>
    /// 루트에 Not Walkable 모디파이어(자식까지)를 달고, 지형과 밟는 바닥판에는 Walkable 모디파이어를 달아 덮어쓴다.
    /// (자식의 모디파이어가 부모 것보다 우선한다)
    /// </summary>
    private static void ApplyWalkableRule(GameObject root)
    {
        int walkable = UnityEngine.AI.NavMesh.GetAreaFromName("Walkable");
        SetAreaModifier(root, UnityEngine.AI.NavMesh.GetAreaFromName("Not Walkable"));

        List<Transform> floors = new List<Transform>();
        foreach (Terrain t in root.GetComponentsInChildren<Terrain>(true)) floors.Add(t.transform);
        int terrains = floors.Count;
        Transform props = root.transform.Find("Props");
        if (props != null) CollectWalkFloors(props, floors);
        foreach (Transform floor in floors) SetAreaModifier(floor.gameObject, walkable);
        Debug.Log($"[SciFiMapBuilder] NavMesh 걷는 면: 지형 {terrains}개, 바닥판 {floors.Count - terrains}개. 나머지는 장애물(Not Walkable)");
    }

    private static void SetAreaModifier(GameObject go, int area)
    {
        NavMeshModifier modifier = go.GetComponent<NavMeshModifier>();
        if (modifier == null) modifier = go.AddComponent<NavMeshModifier>();
        modifier.overrideArea = true;
        modifier.area = area;
        modifier.applyToChildren = true;
    }

    /// <summary>
    /// 맵 전체를 다시 빌드하지 않고 SciFiMap.prefab의 NavMesh만 현재 규칙으로 다시 굽는다.
    /// 프리펩을 열어 고치므로 이 프리펩을 쓰는 모든 씬에 반영된다.
    /// </summary>
    [MenuItem("Tools/Map/Rebake Sci-Fi Map NavMesh")]
    private static void RebakeNavMesh()
    {
        Terrain source = FindSourceTerrain();
        GameObject root = PrefabUtility.LoadPrefabContents(SavePath);
        try
        {
            BakeNavMesh(root, source, NavMeshDataPath);
            PrefabUtility.SaveAsPrefabAsset(root, SavePath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[SciFiMapBuilder] NavMesh 다시 굽기 완료: {NavMeshDataPath}");
    }

    /// <summary>
    /// 씬의 Player를 추락선 옆(맵 중앙 쪽)에, BossSpawnPoint를 분화구 남쪽 바닥에 옮긴다.
    /// 빌드 때마다 덮어쓰지 않도록 별도 메뉴로 둔다. 옮긴 뒤 손으로 다듬어도 빌드가 건드리지 않는다.
    /// </summary>
    [MenuItem("Tools/Map/Place Sci-Fi Spawn Points")]
    private static void PlaceSpawnPoints()
    {
        GameObject map = FindSceneInstance();
        Terrain terrain = map != null ? map.GetComponentInChildren<Terrain>() : null;
        if (terrain == null)
        {
            Debug.LogError("[SciFiMapBuilder] 활성 씬에서 SciFiMap을 찾지 못했습니다. 먼저 Build Sci-Fi Map을 실행하세요.");
            return;
        }
        _terrain = terrain;

        // 지점 그룹 위치가 곧 지점 중심이다. (수동으로 옮긴 경우 포함)
        Transform pad = map.transform.Find("Props/" + SiteNames[SitePad]);
        Transform crater = map.transform.Find("Props/" + SiteNames[SiteCrater]);
        Transform crash = map.transform.Find("Props/" + SiteNames[SiteCrash]);
        Vector2 padXZ = pad != null ? new Vector2(pad.position.x, pad.position.z) : DefaultSiteCenters[SitePad];
        Vector2 craterXZ = crater != null ? new Vector2(crater.position.x, crater.position.z) : DefaultSiteCenters[SiteCrater];

        Transform player = FindInScene("Player");
        if (player != null)
        {
            Transform ship = crash != null ? crash.Find("Spaceship_Broken") : null;
            Vector2 shipXZ = crash != null ? new Vector2(crash.position.x, crash.position.z) : DefaultSiteCenters[SiteCrash];
            float shipRadius = 10f;
            if (ship != null)
            {
                Bounds sb = GetBounds(ship.gameObject);
                shipXZ = new Vector2(sb.center.x, sb.center.z);
                shipRadius = Mathf.Max(sb.extents.x, sb.extents.z);
            }
            Vector2 toPad = (padXZ - shipXZ).normalized;
            Vector2 xz = shipXZ + toPad * (shipRadius + PlayerFromShip);
            Undo.RecordObject(player, "Place Sci-Fi Spawn Points");
            player.SetPositionAndRotation(new Vector3(xz.x, GroundY(xz), xz.y), Quaternion.LookRotation(new Vector3(toPad.x, 0f, toPad.y)));
        }
        else Debug.LogWarning("[SciFiMapBuilder] 씬에서 Player를 찾지 못했습니다.");

        Transform boss = FindInScene("BossSpawnPoint");
        if (boss != null)
        {
            Vector2 xz = craterXZ + BossOffset;
            Undo.RecordObject(boss, "Place Sci-Fi Spawn Points");
            boss.position = new Vector3(xz.x, GroundY(xz), xz.y);
        }
        else Debug.LogWarning("[SciFiMapBuilder] 씬에서 BossSpawnPoint를 찾지 못했습니다.");

        EditorSceneManager.MarkSceneDirty(map.scene);
        Debug.Log($"[SciFiMapBuilder] 스폰 지점 이동: Player {(player != null ? player.position.ToString() : "-")}, BossSpawnPoint {(boss != null ? boss.position.ToString() : "-")}");
    }

    /// <summary>활성 씬에서 이름이 같은 오브젝트를 찾는다. (SciFiMap 내부 제외, 비활성 포함)</summary>
    private static Transform FindInScene(string name)
    {
        foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (go.name == "SciFiMap") continue;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
        }
        return null;
    }

    /// <summary>활성 씬의 안개·환경광·방향광을 붉은 먼지 행성 분위기로 맞춘다. (씬 설정이라 프리펩에는 들어가지 않음)</summary>
    internal static void ApplyAtmosphere()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = FogColor;
        RenderSettings.fogDensity = FogDensity;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = AmbientSky;
        RenderSettings.ambientEquatorColor = AmbientEquator;
        RenderSettings.ambientGroundColor = AmbientGround;

        Scene scene = SceneManager.GetActiveScene();
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional || light.gameObject.scene != scene) continue;
            Undo.RecordObjects(new Object[] { light, light.transform }, "Sci-Fi Map Atmosphere");
            light.color = SunColor;
            light.intensity = SunIntensity;
            light.transform.rotation = Quaternion.Euler(SunEuler);
            break;
        }
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static GameObject FindSceneInstance()
    {
        foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == "SciFiMap" && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == SavePath)
                return go;
        return null;
    }

    // ───────────────────────── 수동 편집 저장·재적용 ─────────────────────────
    // 씬의 SciFiMap 인스턴스를 SciFiMap.prefab과 비교해 옮김·삭제·추가를 SciFiMapEdits.json에 기록하고, 빌드 때마다 다시 적용한다.
    // 오브젝트는 루트 기준 이름 경로로 찾는다. 빌더가 만든 계층은 이름을 고유하게 바꿔 두고(MakeNamesUnique),
    // 프리펩 내부처럼 같은 이름이 겹치는 곳은 "이름[순번]"으로 구분한다.
    // 자세는 SciFiMap 루트 기준으로 저장한다. 부모 기준으로 저장하면 지점 그룹 위치가 바뀔 때 그만큼 함께 밀려 버린다.

    [System.Serializable]
    private class EditData
    {
        public List<MovedEdit> moved = new List<MovedEdit>();
        public List<string> removed = new List<string>();
        public List<AddedEdit> added = new List<AddedEdit>();
    }

    [System.Serializable]
    private class MovedEdit
    {
        public string path;
        public Vector3 position;    // SciFiMap 루트 기준
        public Quaternion rotation;
        public Vector3 scale;
    }

    [System.Serializable]
    private class AddedEdit
    {
        public string parentPath;
        public string name;
        public string prefabPath;
        public Vector3 position;    // SciFiMap 루트 기준
        public Quaternion rotation;
        public Vector3 scale;
    }

    [MenuItem("Tools/Map/Save Sci-Fi Map Edits")]
    private static void SaveEdits()
    {
        GameObject instance = FindSceneInstance();
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(SavePath);
        if (instance == null || asset == null)
        {
            Debug.LogError("[SciFiMapBuilder] 활성 씬에서 SciFiMap 프리펩 인스턴스를 찾지 못했습니다. 먼저 Build Sci-Fi Map을 실행하세요.");
            return;
        }

        Dictionary<string, Transform> before = MapPaths(asset.transform);
        Dictionary<string, Transform> after = MapPaths(instance.transform);
        EditData data = LoadEdits();
        int moved = 0, removed = 0, added = 0;

        foreach (KeyValuePair<string, Transform> kv in after)
        {
            Transform t = kv.Value;
            GetRootPose(instance.transform, t, out Vector3 pos, out Quaternion rot, out Vector3 scale);
            if (before.TryGetValue(kv.Key, out Transform b))
            {
                // 바뀌었는지는 부모 기준(local)으로 따진다. 루트 기준으로 따지면 부모를 따라 움직인 LOD 자식까지 전부 기록된다.
                // (기록하는 값은 루트 기준이라, 지점 그룹 위치가 바뀌어도 놓은 자리가 유지된다)
                if ((t.localPosition - b.localPosition).sqrMagnitude < 1e-6f && Quaternion.Angle(t.localRotation, b.localRotation) < 0.01f
                    && (t.localScale - b.localScale).sqrMagnitude < 1e-8f) continue;
                AddedEdit a = data.added.Find(e => JoinPath(e.parentPath, e.name) == kv.Key);
                if (a != null)
                {
                    a.position = pos; a.rotation = rot; a.scale = scale;
                }
                else
                {
                    data.moved.RemoveAll(e => e.path == kv.Key);
                    data.moved.Add(new MovedEdit { path = kv.Key, position = pos, rotation = rot, scale = scale });
                }
                moved++;
            }
            else if (ParentPath(kv.Key) == "" || before.ContainsKey(ParentPath(kv.Key)))
            {
                // 새로 추가된 오브젝트 중 가장 위쪽만 기록한다. (그 아래는 프리펩에 포함됨)
                GameObject source = PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)
                    ? PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) : null;
                if (source == null)
                {
                    Debug.LogWarning($"[SciFiMapBuilder] 프리펩이 아닌 오브젝트는 추가로 저장하지 않습니다: {kv.Key}");
                    continue;
                }
                data.added.Add(new AddedEdit
                {
                    parentPath = ParentPath(kv.Key), name = t.name, prefabPath = AssetDatabase.GetAssetPath(source),
                    position = pos, rotation = rot, scale = scale,
                });
                added++;
            }
        }

        foreach (string path in before.Keys)
        {
            if (after.ContainsKey(path)) continue;
            string parent = ParentPath(path);
            if (parent != "" && !after.ContainsKey(parent)) continue; // 부모째 지워진 경우 부모만 기록
            int addedIndex = data.added.FindIndex(e => JoinPath(e.parentPath, e.name) == path);
            if (addedIndex >= 0) data.added.RemoveAt(addedIndex);
            else if (!data.removed.Contains(path)) data.removed.Add(path);
            data.moved.RemoveAll(e => e.path == path || e.path.StartsWith(path + "/"));
            removed++;
        }

        File.WriteAllText(EditsPath, JsonUtility.ToJson(data, true));
        AssetDatabase.ImportAsset(EditsPath);
        Debug.Log($"[SciFiMapBuilder] 수동 편집 저장: 이동 {moved} · 삭제 {removed} · 추가 {added} → {EditsPath}");
    }

    private static EditData LoadEdits()
    {
        if (!File.Exists(EditsPath)) return new EditData();
        return JsonUtility.FromJson<EditData>(File.ReadAllText(EditsPath)) ?? new EditData();
    }

    /// <summary>삭제 → 추가 → 이동 순서로 적용한다. 지점 그룹의 이동은 빌드 시작 때 중심 좌표로 이미 반영했으므로 건너뛴다.</summary>
    private static void ApplyEdits(Transform root, EditData data)
    {
        foreach (string path in data.removed)
        {
            Transform t = FindPath(root, path);
            if (t == null) { Debug.LogWarning($"[SciFiMapBuilder] 삭제할 오브젝트를 찾지 못함: {path}"); continue; }
            try { Object.DestroyImmediate(t.gameObject); }
            catch (System.InvalidOperationException) { t.gameObject.SetActive(false); } // 중첩 프리펩 내부라 지울 수 없으면 숨김
        }

        foreach (AddedEdit e in data.added)
        {
            Transform parent = e.parentPath == "" ? root : FindPath(root, e.parentPath);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(e.prefabPath);
            if (parent == null || prefab == null) { Debug.LogWarning($"[SciFiMapBuilder] 추가할 오브젝트를 만들지 못함: {JoinPath(e.parentPath, e.name)}"); continue; }
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = e.name;
            SetRootPose(root, go.transform, e.position, e.rotation, e.scale);
        }

        foreach (MovedEdit e in data.moved)
        {
            if (System.Array.Exists(SiteNames, s => e.path == "Props/" + s)) continue;
            Transform t = FindPath(root, e.path);
            if (t == null) { Debug.LogWarning($"[SciFiMapBuilder] 옮길 오브젝트를 찾지 못함: {e.path}"); continue; }

            // 씬에서 메시를 클릭해 옮기면 소품 안의 자식(LOD 메시 등)만 움직인다. 그러면 나머지 LOD·콜라이더는 제자리에 남으므로,
            // 그 자식이 저장된 자세에 오도록 소품(프리펩 인스턴스 루트) 전체를 옮긴다.
            GameObject propRoot = PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
            if (propRoot == null || propRoot.transform == t)
            {
                SetRootPose(root, t, e.position, e.rotation, e.scale);
                Upright(t);
                continue;
            }
            Transform prop = propRoot.transform;
            Vector3 childLocalPos = prop.InverseTransformPoint(t.position);
            Quaternion childLocalRot = Quaternion.Inverse(prop.rotation) * t.rotation;
            Quaternion propRot = root.rotation * e.rotation * Quaternion.Inverse(childLocalRot);
            Vector3 propPos = root.TransformPoint(e.position) - propRot * Vector3.Scale(childLocalPos, prop.lossyScale);
            prop.SetPositionAndRotation(propPos, propRot);
            Upright(prop);
        }
    }

    /// <summary>
    /// 기울어진 소품을 똑바로 세운다. 프리펩 원래 자세 기준으로 좌우·앞뒤 기울기를 없애고 수평 방향(yaw)만 남긴다.
    /// 일부러 기울여 둔 추락선 등(KeepTiltPrefabs)은 그대로 둔다. 회전축은 소품 Bounds 바닥 중심이라 세운 뒤에도 놓인 자리가 유지된다.
    /// </summary>
    private static void Upright(Transform prop)
    {
        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prop.gameObject);
        if (source == null || System.Array.IndexOf(KeepTiltPrefabs, source.name) >= 0) return;

        Quaternion baseRot = source.transform.localRotation;
        Quaternion tilt = prop.rotation * Quaternion.Inverse(baseRot);
        Vector3 forward = tilt * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) forward = tilt * Vector3.up; // 앞이 수직을 보면 위 방향으로 수평 방향을 정함
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) return;

        Bounds before = GetBounds(prop.gameObject);
        Vector3 pivot = new Vector3(before.center.x, before.min.y, before.center.z);
        prop.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up) * baseRot;
        Bounds after = GetBounds(prop.gameObject);
        prop.position += pivot - new Vector3(after.center.x, after.min.y, after.center.z);
    }

    /// <summary>t의 자세를 root 기준으로 구한다. 스케일은 root 기준 lossyScale(회전 없는 축 비율 근사).</summary>
    private static void GetRootPose(Transform root, Transform t, out Vector3 position, out Quaternion rotation, out Vector3 scale)
    {
        position = root.InverseTransformPoint(t.position);
        rotation = Quaternion.Inverse(root.rotation) * t.rotation;
        Vector3 rs = root.lossyScale;
        scale = new Vector3(t.lossyScale.x / rs.x, t.lossyScale.y / rs.y, t.lossyScale.z / rs.z);
    }

    private static void SetRootPose(Transform root, Transform t, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        t.SetPositionAndRotation(root.TransformPoint(position), root.rotation * rotation);
        Vector3 ps = t.parent.lossyScale;
        Vector3 rs = root.lossyScale;
        t.localScale = new Vector3(scale.x * rs.x / ps.x, scale.y * rs.y / ps.y, scale.z * rs.z / ps.z);
    }

    /// <summary>빌더가 만든 계층(중첩 프리펩 내부 제외)에서 같은 부모 아래 이름이 겹치면 "이름 (n)"으로 바꾼다.</summary>
    private static void MakeNamesUnique(Transform parent)
    {
        Dictionary<string, int> total = new Dictionary<string, int>();
        foreach (Transform c in parent) total[c.name] = total.TryGetValue(c.name, out int n) ? n + 1 : 1;
        Dictionary<string, int> seen = new Dictionary<string, int>();
        foreach (Transform c in parent)
        {
            string name = c.name;
            if (total[name] > 1)
            {
                seen[name] = seen.TryGetValue(name, out int k) ? k + 1 : 1;
                c.name = $"{name} ({seen[name]})";
            }
            if (!PrefabUtility.IsPartOfPrefabInstance(c.gameObject)) MakeNamesUnique(c);
        }
    }

    private static Dictionary<string, Transform> MapPaths(Transform root)
    {
        Dictionary<string, Transform> map = new Dictionary<string, Transform>();
        CollectPaths(root, "", map);
        return map;
    }

    private static void CollectPaths(Transform parent, string prefix, Dictionary<string, Transform> map)
    {
        foreach (Transform c in parent)
        {
            string path = JoinPath(prefix, Segment(c));
            map[path] = c;
            CollectPaths(c, path, map);
        }
    }

    /// <summary>형제 중 이름이 유일하면 이름, 아니면 "이름[같은 이름 중 순번]"</summary>
    private static string Segment(Transform t)
    {
        int index = 0, count = 0;
        foreach (Transform s in t.parent)
        {
            if (s.name != t.name) continue;
            if (s == t) index = count;
            count++;
        }
        return count > 1 ? $"{t.name}[{index}]" : t.name;
    }

    private static Transform FindPath(Transform root, string path)
    {
        Transform cur = root;
        foreach (string seg in path.Split('/'))
        {
            Match m = Regex.Match(seg, @"^(.*)\[(\d+)\]$");
            string name = m.Success ? m.Groups[1].Value : seg;
            int index = m.Success ? int.Parse(m.Groups[2].Value) : 0;
            Transform next = null;
            foreach (Transform c in cur)
            {
                if (c.name != name) continue;
                if (index-- == 0) { next = c; break; }
            }
            if (next == null) return null;
            cur = next;
        }
        return cur;
    }

    private static string JoinPath(string parent, string child) => parent == "" ? child : parent + "/" + child;

    private static string ParentPath(string path)
    {
        int i = path.LastIndexOf('/');
        return i < 0 ? "" : path.Substring(0, i);
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
