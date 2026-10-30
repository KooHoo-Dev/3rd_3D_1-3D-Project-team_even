using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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
    private static readonly string[] SiteNames = { "LandingPad", "CrashSite", "DefenseLine", "Refinery", "Crater", "Wasteland" };
    private static readonly Vector2[] DefaultSiteCenters =
    {
        new Vector2(35f, 9f), new Vector2(-49.43f, -90.27f), new Vector2(125f, 10f),
        new Vector2(110f, 95f), new Vector2(-40f, 90f), new Vector2(-60f, 10f),
    };
    private const int SitePad = 0, SiteCrash = 1, SiteDefense = 2, SiteRefinery = 3, SiteCrater = 4, SiteWasteland = 5;
    private static readonly Vector2[] SiteCenters = new Vector2[SiteNames.Length];

    private static Vector2 Center => SiteCenters[SitePad];
    private static Vector2 CrashCenter => SiteCenters[SiteCrash];
    private static Vector2 DefenseCenter => SiteCenters[SiteDefense];
    private static Vector2 RefineryCenter => SiteCenters[SiteRefinery];
    private static Vector2 CraterCenter => SiteCenters[SiteCrater];
    private static Vector2 WastelandCenter => SiteCenters[SiteWasteland];

    // 수동 편집 기록 (Save Sci-Fi Map Edits 메뉴가 씀)
    private const string EditsPath = "Assets/Scenes/GU/Map/SciFiMapEdits.json";

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
    private static readonly string[] LayerNames = { "Sand1", "Sand2", "Sand3", "SandRock", "Sand6" };
    private const int LayerBase = 0, LayerBaseAlt = 1, LayerRoad = 2, LayerRock = 3, LayerCrater = 4;

    // 착륙장 + 엄폐물 링
    private const float PadRadius = 17f;          // 바닥판을 까는 반경 (안쪽 15m는 비워 둠)
    private const float PadTopOffset = 0.1f;      // 바닥판 윗면이 지면보다 올라오는 높이
    private const int PadLampCount = 12;
    private const float CoverRingMin = 21f;
    private const float CoverRingMax = 29f;
    private const float CoverGateHalfAngle = 18f; // 동서남북 진입 통로 반각(도)
    private const float CoverMaxHeight = 1.5f;    // 컨테이너를 뺀 엄폐물 최대 높이

    // 추락선: MapDesign 씬에서 손으로 맞춘 자세를 그대로 쓴다. (오일러 약 17.87, -208.1, -13.8)
    // 손으로 맞출 당시 지형이 (73.6, 23.8)만큼 밀려 있었으므로, 지형 기준 같은 자리가 되도록 그만큼 되돌린 값이다.
    // CrashSite 그룹이 옮겨지면 같은 만큼 따라간다.
    private static readonly Vector3 ShipPosition = new Vector3(-49.43f, -0.19f, -90.27f);
    private static readonly Quaternion ShipRotation = new Quaternion(0.07769645f, -0.95590156f, 0.17838787f, -0.21998493f);

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
    private const float CraterMaxSlope = 32f; // 분화구 사발 경사(최대 약 27°)에도 바위·엄폐물을 놓을 수 있게

    // 지점별 전용 엄폐물(보스 장판·돌진 회피용): 지점마다 2~5개, 몸 전체를 가리는 높이
    private const int CoverMinCount = 2;
    private const int CoverMaxCount = 5;
    private const float CoverTallMinHeight = 2.4f;
    private const float CoverTallMaxHeight = 3.2f;
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

    // 분위기: 붉은 갈색 먼지 안개 + 어두운 하늘 + 낮게 깔린 햇빛 + 붉은 포인트 조명 (MapDesign 씬 설정을 바꾼다)
    private static readonly Color FogColor = new Color(0.42f, 0.27f, 0.21f);
    private const float FogDensity = 0.008f;
    private static readonly Color AmbientSky = new Color(0.30f, 0.24f, 0.24f);
    private static readonly Color AmbientEquator = new Color(0.28f, 0.20f, 0.17f);
    private static readonly Color AmbientGround = new Color(0.12f, 0.09f, 0.08f);
    private static readonly Color SunColor = new Color(1f, 0.72f, 0.55f);
    private const float SunIntensity = 1.1f;
    private static readonly Vector3 SunEuler = new Vector3(28f, -35f, 0f);
    private static readonly Color GuideLightColor = new Color(1f, 0.15f, 0.1f);
    private static readonly Color CraterGlowColor = new Color(1f, 0.25f, 0.1f);

    // 소품 배치 공통
    private const float MaxPropSlope = 20f;  // 이보다 가파른 곳엔 배치하지 않음(도)
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

        // 3) 프리펩 저장 후 씬에 배치
        PrefabUtility.SaveAsPrefabAsset(root, SavePath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();

        ReplaceInActiveScene();
        ApplyAtmosphere();
        Debug.Log($"[SciFiMapBuilder] 생성 완료: {SavePath}");
    }

    // ───────────────────────── 지형 ─────────────────────────

    private static Terrain FindSourceTerrain()
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

        for (int j = 0; j < res; j++)
        {
            for (int i = 0; i < res; i++)
            {
                float wx = minX + i / (float)(res - 1) * data.size.x;
                float wz = minZ + j / (float)(res - 1) * data.size.z;
                Vector2 p = new Vector2(wx, wz);
                float h = heights[j, i] * scaleY + Lift;

                // 광장·순환로: 기준면으로 평탄화
                float d = Vector2.Distance(p, Center);
                float flat = Mathf.Max(
                    1f - Smooth01((d - PlazaRadius) / PlazaBlend),
                    1f - Smooth01((Mathf.Abs(d - RingRadius) - RingHalfWidth) / RingBlend));
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
                h = Mathf.Max(h, baseH + (CliffHeight + n2 * CliffHeightVar + rough) * cliffT);

                heights[j, i] = Mathf.Clamp01(h / scaleY);
            }
        }

        data.SetHeights(0, 0, heights);
    }

    private static void PaintTerrain(TerrainData data, Vector3 terrainPos)
    {
        TerrainLayer[] layers = new TerrainLayer[LayerNames.Length];
        for (int k = 0; k < LayerNames.Length; k++)
            layers[k] = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerDir + LayerNames[k] + ".terrainlayer");
        data.terrainLayers = layers;

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

                // 우선순위: 급경사 바위 > 분화구 바닥 > 길 > 바탕(두 가지 모래를 얼룩지게)
                float rest = 1f - rock;
                float wCrater = rest * crater;
                rest -= wCrater;
                float wRoad = rest * road * 0.85f;
                rest -= wRoad;

                alpha[j, i, LayerRock] = rock;
                alpha[j, i, LayerCrater] = wCrater;
                alpha[j, i, LayerRoad] = wRoad;
                alpha[j, i, LayerBaseAlt] = rest * patch;
                alpha[j, i, LayerBase] = rest * (1f - patch);
            }
        }

        data.SetAlphamaps(0, 0, alpha);
        EditorUtility.SetDirty(data);
    }

    private static Terrain CreateTerrainObject(TerrainData data, Terrain source, Vector3 position, Transform parent)
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
    private static readonly List<Vector3> Occupied = new List<Vector3>(); // (x, z, 반경)

    private static void BuildProps(Transform root, Terrain terrain)
    {
        _terrain = terrain;
        Occupied.Clear();
        Transform props = new GameObject("Props").transform;
        props.SetParent(root, false);

        Random.State prev = Random.state;
        Random.InitState(7101); BuildLandingPad(SiteGroup(props, SitePad));
        Random.InitState(7102); BuildCrashSite(SiteGroup(props, SiteCrash));
        Random.InitState(7103); BuildDefenseLine(SiteGroup(props, SiteDefense));
        Random.InitState(7104); BuildRefinery(SiteGroup(props, SiteRefinery));
        Random.InitState(7105); BuildCrater(SiteGroup(props, SiteCrater));
        Random.InitState(7106); BuildWasteland(SiteGroup(props, SiteWasteland));
        Random.InitState(7107); BuildBoundary(Group(props, "Boundary")); // 지점 소품을 피해서 놓도록 마지막에
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
                Bounds b = GetBounds(go);
                go.transform.position += new Vector3(xz.x - b.center.x, GroundY(xz) + PadTopOffset - b.max.y, xz.y - b.center.z);
            }
        }
        Occupy(Center, PadRadius);

        // 유도등
        for (int k = 0; k < PadLampCount; k++)
        {
            float a = k * Mathf.PI * 2f / PadLampCount;
            GameObject lamp = Spawn("Lamp_Position", parent);
            if (lamp == null) continue;
            SetOnGround(lamp, Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (PadRadius + 0.6f), 0f);
            Bounds lb = GetBounds(lamp);
            AddPointLight(lamp.transform, "GuideLight", new Vector3(lb.center.x, lb.max.y + 0.3f, lb.center.z), GuideLightColor, 6f, 2f);
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
        if (ship != null)
        {
            Vector2 baseXZ = DefaultSiteCenters[SiteCrash];
            Vector3 shift = new Vector3(CrashCenter.x - baseXZ.x, GroundY(CrashCenter) - GroundY(baseXZ), CrashCenter.y - baseXZ.y);
            ship.transform.SetPositionAndRotation(ShipPosition + shift, ShipRotation);
            shipRadius = Footprint(ship);
            Bounds sb = GetBounds(ship);
            Occupy(new Vector2(sb.center.x, sb.center.z), shipRadius);
        }

        PlaceCover(parent, new[] { "Container_01", "Concrete_wall2_Shield", "Stone_Big1", "Rock_01" },
            CrashCenter, shipRadius + 4f, shipRadius + 18f, MaxPropSlope, null);

        Scatter(parent, new[] { "Rock_01", "Stone_med1", "Stone_med2", "Stone_med3", "Rock_Shales_01", "Rock_Shales_03" },
            10, CrashCenter, shipRadius + 3f, shipRadius + 20f, 2f, 0.8f, 1.3f, 0.2f, 0f);
        Scatter(parent, new[] { "Prop_Barrel1 gray", "Prop3", "Prop5", "Prop7", "Cover Panel2D" },
            6, CrashCenter, shipRadius + 2f, shipRadius + 14f, 1.5f, 1f, 1f, 0.1f, 25f);
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
        Scatter(parent, new[] { "Antenna_01", "Antenna_03", "Antenna_05", "Antenna_Big", "Solar_Panel1", "Solar_Panel2", "Solar_Panel3", "Solar_Panel4", "Radar_Sphere" },
            8, DefenseCenter, 6f, 45f, 3f, 1f, 1f, 0f, 0f);
        Scatter(parent, new[] { "Barrier_2m", "Barrier_4m", "Barrier_01_A", "Barrier_Edge_2m", "Concrete_wall2_Barrier3", "Cover Panel6A" },
            12, DefenseCenter + new Vector2(-12f, 0f), 4f, 32f, 3f, 1f, 1f, 0.05f, 8f);
    }

    /// <summary>정제 플랜트(북동): 크레인 + 탱크·발전기 군집(사이 통로 4m 이상) + 작은 설비</summary>
    private static void BuildRefinery(Transform parent)
    {
        Scatter(parent, new[] { "Crane_01" }, 1, RefineryCenter, 0f, 4f, 4f, 1f, 1f, 0f, 0f);
        PlaceCover(parent, new[] { "Container_01", "Barrier_Solid_01", "Concrete_wall2_Barrier1" },
            RefineryCenter, 6f, 30f, MaxPropSlope, null);
        Scatter(parent, new[] { "Tank_01", "Tank_02", "Tank_03", "Tank_04", "Tank_Stack_01", "Tank_Stack_02", "Tank_Stack_03",
                "Generator_Set_01", "Generator_Set_02", "Generator_Set_03", "Fuel_Tank_01", "Radiator_Big" },
            14, RefineryCenter, 6f, 30f, 4f, 1f, 1f, 0f, 0f);
        Scatter(parent, new[] { "Powerbox_01", "Powerbox_05", "Powerbox_09", "Pipe3_A", "Pipe2_A", "Prop_Barrel1", "Prop_Cylinder1", "Prop_vents_03" },
            10, RefineryCenter, 4f, 34f, 2f, 1f, 1f, 0f, 0f);
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
            Occupy(drillXZ, Mathf.Max(baseB.extents.x, baseB.extents.z));
        }

        // 드릴이 찌른 행성의 '살'이 비치는 붉은 빛
        AddPointLight(parent, "CraterGlow", new Vector3(CraterCenter.x, GroundY(CraterCenter) + 2f, CraterCenter.y), CraterGlowColor, CraterRadius + 4f, 3f);

        PlaceCover(parent, new[] { "Stone_Big1", "Rock_01", "Concrete_wall2_Shield", "Concrete_wall2_Barrier1", "Barrier_Solid_01" },
            CraterCenter, 6f, CraterRadius - 2f, CraterMaxSlope, null);
        Scatter(parent, new[] { "Rock_Shales_02", "Rock_Shales_05", "Rock_Shales_07", "Stone_med4", "Stone_med5", "Stone_Big1" },
            12, CraterCenter, CraterRadius - 4f, CraterRadius + 10f, 2f, 0.8f, 1.5f, 0.2f, 0f, CraterMaxSlope);
    }

    /// <summary>암석 황무지(서): 큰 바위 사이로 우회로가 생기게 간격을 넉넉히 둔 바위 지대</summary>
    private static void BuildWasteland(Transform parent)
    {
        PlaceCover(parent, new[] { "Stone_Big1", "Rock_01", "Rock_Group1", "Barrier_Solid_01" }, WastelandCenter, 0f, 30f, MaxPropSlope, null);
        Scatter(parent, new[] { "Rock_Group1" }, 3, WastelandCenter, 0f, 30f, 5f, 1f, 1.6f, 0.15f, 0f);
        Scatter(parent, new[] { "Stone_Big1" }, 4, WastelandCenter, 0f, 32f, 4f, 1f, 1.5f, 0.15f, 0f);
        Scatter(parent, new[] { "Rock_Shales_01", "Rock_Shales_04", "Rock_Shales_06", "Rock_Shales_08", "Rock_Shales_09", "Rock_Shales_10" },
            10, WastelandCenter, 0f, 34f, 3f, 0.8f, 1.4f, 0.2f, 0f);
        Scatter(parent, new[] { "Stone_med1", "Stone_med2", "Stone_med3", "Stone_med6", "Stone_small1", "Stone_small3" },
            10, WastelandCenter, 0f, 36f, 2f, 0.8f, 1.3f, 0.2f, 0f);
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

        Vector2 xz = start + dir * (len * 0.5f);
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
            if (Overlaps(p, Footprint(go)))
            {
                Object.DestroyImmediate(go);
                continue;
            }
            SetOnGround(go, p, 0.25f);
        }
        return BoundaryRockStep;
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

    private static void AddPointLight(Transform parent, string name, Vector3 position, Color color, float range, float intensity)
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
    private static void Scatter(Transform parent, string[] names, int count, Vector2 center, float rMin, float rMax,
        float clearance, float scaleMin, float scaleMax, float sinkRatio, float maxTilt, float maxSlope = MaxPropSlope)
    {
        for (int k = 0; k < count; k++)
        {
            GameObject go = Spawn(names[Random.Range(0, names.Length)], parent);
            if (go == null) continue;
            go.transform.localScale *= Random.Range(scaleMin, scaleMax);
            go.transform.rotation = Quaternion.Euler(Random.Range(-maxTilt, maxTilt), Random.Range(0f, 360f), Random.Range(-maxTilt, maxTilt)) * go.transform.rotation;
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
    private static void PlaceCover(Transform site, string[] names, Vector2 center, float rMin, float rMax, float maxSlope,
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

        float d = Vector2.Distance(xz, Center);
        if (d < PadRadius + 1f + r) return false;
        if (Mathf.Abs(d - RingRadius) < RingHalfWidth + r) return false;

        foreach (Vector3 o in Occupied)
            if (Vector2.Distance(xz, new Vector2(o.x, o.y)) < r + o.z + clearance) return false;
        return true;
    }

    private static void Occupy(Vector2 xz, float r) => Occupied.Add(new Vector3(xz.x, xz.y, r));

    private static Transform Group(Transform parent, string name)
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

    private static GameObject Spawn(string name, Transform parent)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + name + ".prefab");
        if (prefab == null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RockDir + name + ".prefab");
        if (prefab == null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GUPrefabDir + name + ".prefab");
        if (prefab == null)
        {
            Debug.LogWarning($"[SciFiMapBuilder] 프리펩을 찾지 못함: {name}");
            return null;
        }
        return (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
    }

    private static Bounds GetBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.5f);
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    private static float Footprint(GameObject go)
    {
        Bounds b = GetBounds(go);
        return Mathf.Max(b.extents.x, b.extents.z);
    }

    private static void ClampHeight(GameObject go, float maxHeight)
    {
        float h = GetBounds(go).size.y;
        if (h > maxHeight) go.transform.localScale *= maxHeight / h;
    }

    private static float GroundY(Vector2 xz) => _terrain.SampleHeight(new Vector3(xz.x, 0f, xz.y)) + _terrain.transform.position.y;

    /// <summary>Bounds 중심을 xz에 맞추고, 바닥 네 모서리·중심 중 가장 낮은 지면에 닿게(sinkRatio만큼 파묻어) 놓는다.</summary>
    private static void SetOnGround(GameObject go, Vector2 xz, float sinkRatio)
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

    /// <summary>활성 씬의 안개·환경광·방향광을 붉은 먼지 행성 분위기로 맞춘다. (씬 설정이라 프리펩에는 들어가지 않음)</summary>
    private static void ApplyAtmosphere()
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
                GetRootPose(asset.transform, b, out Vector3 bPos, out Quaternion bRot, out Vector3 bScale);
                if ((pos - bPos).sqrMagnitude < 1e-6f && Quaternion.Angle(rot, bRot) < 0.01f && (scale - bScale).sqrMagnitude < 1e-8f) continue;
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
            SetRootPose(root, t, e.position, e.rotation, e.scale);
        }
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
