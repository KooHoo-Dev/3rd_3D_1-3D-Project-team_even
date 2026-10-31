using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 튜토리얼 맵(TutorialMap.unity)을 조립한다. 설계: Docs/Design/MapDesign_SciFiBase.md 5장
/// 메뉴: Tools/Map/Build Tutorial Map
/// - 남(z=0)에서 북(+Z)으로 한 방향으로만 걷는 약 60m × 200m 선형 협곡. 폭은 단계마다 넓어지고 좁아진다.
/// - 단계: ① 추락 함선 옆 기상 → ② 잔해 협곡 → ③ 첫 항체 조우 → ④ 보급 포드 공터 → ⑤ 전초기지 게이트
/// - 튜토리얼 진행 로직(트리거·안내 문구)은 범위 밖이다. 단계 위치에는 빈 마커(Markers 아래)만 둔다.
/// - 소품 배치·콜라이더 정리·NavMesh·분위기는 SciFiMapBuilder의 도우미를 같이 쓴다.
/// - 씬이 이미 있으면 열어서 TutorialMap 루트만 다시 만든다. (씬의 다른 오브젝트는 건드리지 않음)
/// </summary>
public static class TutorialMapBuilder
{
    private const string ScenePath = "Assets/Scenes/GU/TutorialMap.unity";
    private const string TerrainDataPath = "Assets/Scenes/GU/Map/TutorialTerrain.asset";
    private const string NavMeshDataPath = "Assets/Scenes/GU/Map/TutorialMap_NavMesh.asset";
    private const string RootName = "TutorialMap";

    // 지형: 가로(X) 100m × 세로(Z) 240m, 협곡 바닥 높이 FloorHeight
    private const int HeightmapResolution = 257;
    private const int AlphamapResolution = 256;
    private static readonly Vector3 TerrainSize = new Vector3(100f, 80f, 240f);
    private const float FloorHeight = 5f;
    private const float WallHeight = 28f;       // + 0 ~ WallHeightVar
    private const float WallHeightVar = 8f;
    private const float WallRamp = 8f;          // 바닥 가장자리에서 벽 꼭대기까지 수평 거리
    private const float StartCapZ = 6f;         // 이보다 남쪽은 막힌 벽
    private const float EndCapZ = 222f;         // 이보다 북쪽은 막힌 벽 (게이트 뒤)

    // 협곡 중심선: x = CorridorX + Sway·sin(z·SwayFreq) (살짝 굽이침)
    private const float CorridorX = 50f;
    private const float CorridorSway = 8f;
    private const float CorridorSwayFreq = 0.025f;
    private const float LaneHalf = 2.5f;        // 중심선 양쪽으로 이만큼은 소품을 두지 않는 통로

    // 협곡 반폭(중심선 기준): z 지점별 값 사이를 부드럽게 잇는다. ①넓음 → ②좁은 협곡 → ③조우 공간 → ④공터 → ⑤게이트 앞
    private static readonly float[] HalfWidthZ = { 0f, 35f, 45f, 85f, 95f, 125f, 135f, 170f, 180f, 215f, 225f };
    private static readonly float[] HalfWidthW = { 16f, 18f, 10f, 10f, 16f, 16f, 20f, 20f, 11f, 11f, 8f };

    // 단계 위치 (z)
    private const float PlayerStartZ = 10f;
    private const float ShipZ = 28f;
    private const float WeaponZ = 60f;
    private const float EncounterZ = 110f;
    private const float SupplyZ = 152f;
    private const float GateZ = 212f;

    private static readonly string[] LayerNames = { "Sand1", "Sand3", "SandRock" };
    private const int LayerBase = 0, LayerLane = 1, LayerRock = 2;

    [MenuItem("Tools/Map/Build Tutorial Map")]
    private static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[TutorialMapBuilder] 플레이 모드에서는 실행할 수 없습니다.");
            return;
        }
        Terrain source = SciFiMapBuilder.FindSourceTerrain();
        if (source == null)
        {
            Debug.LogError("[TutorialMapBuilder] Map.prefab의 지형을 찾지 못했습니다. (지형 레이어·머티리얼·NavMesh 설정을 가져오는 데 씀)");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Scene scene = OpenOrCreateScene();
        foreach (GameObject go in scene.GetRootGameObjects())
            if (go.name == RootName) Object.DestroyImmediate(go);

        GameObject root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);

        // 1) 지형
        TerrainData data = GetOrCreateTerrainData();
        SculptTerrain(data);
        PaintTerrain(data);
        Terrain terrain = CreateTerrainObject(data, source, root.transform);

        // 2) 단계별 소품 + 마커
        SciFiMapBuilder.BeginPlacement(terrain, false, InsideCorridorOffLane);
        Transform props = SciFiMapBuilder.Group(root.transform, "Props");
        Transform markers = SciFiMapBuilder.Group(root.transform, "Markers");
        Random.State prev = Random.state;
        Random.InitState(8101); BuildWake(SciFiMapBuilder.Group(props, "1_Wake"), markers);
        Random.InitState(8102); BuildDebrisCanyon(SciFiMapBuilder.Group(props, "2_DebrisCanyon"), markers);
        Random.InitState(8103); BuildFirstContact(SciFiMapBuilder.Group(props, "3_FirstContact"), markers);
        Random.InitState(8104); BuildSupplyClearing(SciFiMapBuilder.Group(props, "4_SupplyClearing"), markers);
        Random.InitState(8105); BuildGate(SciFiMapBuilder.Group(props, "5_OutpostGate"), markers);
        Random.InitState(8106); BuildWallFoot(SciFiMapBuilder.Group(props, "WallFoot"));
        Random.state = prev;

        // 3) 콜라이더 정리 → NavMesh → 분위기 → 미리보기 카메라
        SciFiMapBuilder.CleanupColliders(root.transform);
        SciFiMapBuilder.BakeNavMesh(root, source, NavMeshDataPath);
        SciFiMapBuilder.ApplyAtmosphere();
        AimCamera(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        Debug.Log($"[TutorialMapBuilder] 생성 완료: {ScenePath}");
    }

    private static Scene OpenOrCreateScene()
    {
        if (File.Exists(ScenePath)) return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);
        return scene;
    }

    // ───────────────────────── 협곡 모양 ─────────────────────────

    private static float CenterX(float z) => CorridorX + CorridorSway * Mathf.Sin(z * CorridorSwayFreq);

    private static float HalfWidth(float z)
    {
        if (z <= HalfWidthZ[0]) return HalfWidthW[0];
        for (int k = 1; k < HalfWidthZ.Length; k++)
        {
            if (z > HalfWidthZ[k]) continue;
            float t = (z - HalfWidthZ[k - 1]) / (HalfWidthZ[k] - HalfWidthZ[k - 1]);
            return Mathf.SmoothStep(HalfWidthW[k - 1], HalfWidthW[k], t);
        }
        return HalfWidthW[HalfWidthW.Length - 1];
    }

    /// <summary>협곡 바닥 밖으로 나간 거리(m). 0 이하면 바닥 안쪽.</summary>
    private static float Outside(float x, float z)
    {
        float side = Mathf.Abs(x - CenterX(z)) - HalfWidth(z);
        return Mathf.Max(side, Mathf.Max(StartCapZ - z, z - EndCapZ));
    }

    /// <summary>소품 자리 조건: 협곡 바닥 안쪽(벽에서 1m 이상)이고 중심 통로(LaneHalf)에 걸리지 않음</summary>
    private static bool InsideCorridorOffLane(Vector2 xz, float r)
    {
        if (Outside(xz.x, xz.y) > -(r + 1f)) return false;
        return Mathf.Abs(xz.x - CenterX(xz.y)) - r > LaneHalf;
    }

    private static Vector2 OnCenter(float z) => new Vector2(CenterX(z), z);

    // ───────────────────────── 지형 ─────────────────────────

    private static TerrainData GetOrCreateTerrainData()
    {
        TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
        if (data == null)
        {
            data = new TerrainData();
            AssetDatabase.CreateAsset(data, TerrainDataPath);
        }
        data.heightmapResolution = HeightmapResolution;
        data.alphamapResolution = AlphamapResolution;
        data.size = TerrainSize;
        return data;
    }

    private static void SculptTerrain(TerrainData data)
    {
        int res = data.heightmapResolution;
        float[,] heights = new float[res, res];
        for (int j = 0; j < res; j++)
        {
            for (int i = 0; i < res; i++)
            {
                float x = i / (float)(res - 1) * TerrainSize.x;
                float z = j / (float)(res - 1) * TerrainSize.z;
                float h = FloorHeight + (Mathf.PerlinNoise(x * 0.08f, z * 0.08f) - 0.5f) * 0.6f; // 바닥 잔굴곡
                float outside = Outside(x, z);
                if (outside > 0f)
                {
                    float wallT = Mathf.SmoothStep(0f, 1f, outside / WallRamp);
                    float top = WallHeight + Mathf.PerlinNoise(x * 0.04f + 200f, z * 0.04f) * WallHeightVar;
                    h += top * wallT + Mathf.PerlinNoise(x * 0.25f, z * 0.25f) * 2.5f * wallT;
                }
                heights[j, i] = Mathf.Clamp01(h / TerrainSize.y);
            }
        }
        data.SetHeights(0, 0, heights);
    }

    private static void PaintTerrain(TerrainData data)
    {
        TerrainLayer[] layers = new TerrainLayer[LayerNames.Length];
        for (int k = 0; k < LayerNames.Length; k++)
            layers[k] = AssetDatabase.LoadAssetAtPath<TerrainLayer>(SciFiMapBuilder.LayerDir + LayerNames[k] + ".terrainlayer");
        data.terrainLayers = layers;

        int res = data.alphamapResolution;
        float[,,] alpha = new float[res, res, layers.Length];
        for (int j = 0; j < res; j++)
        {
            for (int i = 0; i < res; i++)
            {
                float nx = i / (float)(res - 1), nz = j / (float)(res - 1);
                float x = nx * TerrainSize.x, z = nz * TerrainSize.z;
                float rock = Mathf.SmoothStep(0f, 1f, (data.GetSteepness(nx, nz) - 25f) / 15f);
                float laneDist = Mathf.Abs(x - CenterX(z)) - (LaneHalf + 2f + Mathf.PerlinNoise(x * 0.1f, z * 0.1f) * 3f);
                float lane = 1f - Mathf.SmoothStep(0f, 1f, laneDist / 3f); // 많이 밟힌 길
                float rest = 1f - rock;
                alpha[j, i, LayerRock] = rock;
                alpha[j, i, LayerLane] = rest * lane * 0.8f;
                alpha[j, i, LayerBase] = rest * (1f - lane * 0.8f);
            }
        }
        data.SetAlphamaps(0, 0, alpha);
        EditorUtility.SetDirty(data);
    }

    private static Terrain CreateTerrainObject(TerrainData data, Terrain source, Transform parent)
    {
        GameObject go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.layer = source.gameObject.layer;
        go.transform.SetParent(parent, false);
        Terrain terrain = go.GetComponent<Terrain>();
        terrain.materialTemplate = source.materialTemplate;
        terrain.heightmapPixelError = source.heightmapPixelError;
        return terrain;
    }

    // ───────────────────────── 단계 ─────────────────────────

    /// <summary>① 추락 함선 옆 기상: 협곡 벽에 처박힌 추락선 + 주변 잔해. 이동·시점을 익힌다.</summary>
    private static void BuildWake(Transform parent, Transform markers)
    {
        Marker(markers, "PlayerStart", OnCenter(PlayerStartZ), 0f);
        Marker(markers, "Stage1_Wake (이동·시점)", OnCenter(PlayerStartZ + 6f), 0f);

        GameObject ship = SciFiMapBuilder.Spawn("Spaceship_Broken", parent);
        if (ship != null)
        {
            ship.transform.localScale *= 0.7f;
            ship.transform.rotation = Quaternion.Euler(8f, 160f, -10f) * ship.transform.rotation;
            Vector2 at = OnCenter(ShipZ);
            SciFiMapBuilder.SetOnGround(ship, at, 0.15f);
            // 선체 안쪽 끝이 통로에서 4m 떨어지도록 동쪽 벽 쪽으로 밀어 넣는다. (벽에 처박힌 모습)
            Bounds b = SciFiMapBuilder.GetBounds(ship);
            ship.transform.position += Vector3.right * (at.x + LaneHalf + 4f - b.min.x);
            b = SciFiMapBuilder.GetBounds(ship);
            SciFiMapBuilder.Occupy(new Vector2(b.center.x, b.center.z), Mathf.Max(b.extents.x, b.extents.z));
        }

        for (float z = 14f; z <= 40f; z += 13f)
            SciFiMapBuilder.Scatter(parent, new[] { "Rock_01", "Stone_med1", "Stone_med2", "Prop_Barrel1 gray", "Prop3", "Prop5" },
                4, OnCenter(z), 0f, 16f, 1.5f, 0.8f, 1.2f, 0.15f, 0f);
    }

    /// <summary>② 잔해 협곡: 좁은 길 양쪽 벽 아래 잔해와 바위. 첫 무기("동력 공급 재개")와 사격을 익힌다.</summary>
    private static void BuildDebrisCanyon(Transform parent, Transform markers)
    {
        Marker(markers, "Stage2_DebrisCanyon (사격)", OnCenter(50f), 0f);
        Marker(markers, "WeaponPickup (동력 공급 재개)", OnCenter(WeaponZ), 0f);

        for (float z = 48f; z <= 88f; z += 10f)
            SciFiMapBuilder.Scatter(parent, new[] { "Rock_Shales_02", "Rock_Shales_05", "Stone_med3", "Stone_med4", "Prop7", "Cover Panel2D", "Barrier_01_Corner" },
                4, OnCenter(z), 0f, 12f, 1f, 0.8f, 1.3f, 0.15f, 0f);

        // 사격 표적 삼아 길가에 세워 둔 통 몇 개
        SciFiMapBuilder.Scatter(parent, new[] { "Prop_Barrel1", "Prop_Barrel1 yellow" }, 4, OnCenter(WeaponZ + 12f), 0f, 9f, 1f, 1f, 1f, 0f, 0f);
    }

    /// <summary>③ 첫 항체 조우: 넓어진 공간에 회피용 엄폐물 2~5개. 소수 몬스터와 전투, 바이오매스 회수를 익힌다.</summary>
    private static void BuildFirstContact(Transform parent, Transform markers)
    {
        Marker(markers, "Stage3_FirstContact (항체 조우·바이오매스)", OnCenter(EncounterZ - 10f), 0f);
        float hw = HalfWidth(EncounterZ);
        Marker(markers, "MonsterSpawn_A", OnCenter(EncounterZ + 10f) + new Vector2(-hw * 0.6f, 0f), 180f);
        Marker(markers, "MonsterSpawn_B", OnCenter(EncounterZ + 10f) + new Vector2(hw * 0.6f, 0f), 180f);

        SciFiMapBuilder.PlaceCover(parent, new[] { "Container_01", "Barrier_Solid_01", "Concrete_wall2_Barrier1", "Stone_Big1" },
            OnCenter(EncounterZ), 4f, hw, SciFiMapBuilder.MaxPropSlope, null);
        SciFiMapBuilder.Scatter(parent, new[] { "Rock_Shales_06", "Rock_Shales_09", "Stone_med5", "Stone_small1" },
            6, OnCenter(EncounterZ), 0f, hw, 1.5f, 0.8f, 1.3f, 0.2f, 0f);
    }

    /// <summary>④ 보급 포드 공터: 넓은 공터 가운데 착륙판과 붉은 유도등. 궤도 보급 포드와 업그레이드를 익힌다.</summary>
    private static void BuildSupplyClearing(Transform parent, Transform markers)
    {
        Vector2 c = OnCenter(SupplyZ);
        Marker(markers, "Stage4_SupplyClearing (보급 포드·업그레이드)", OnCenter(SupplyZ - 14f), 0f);
        Marker(markers, "SupplyPodDrop", c, 0f);

        // 2×2 착륙판 (밟는 바닥이라 콜라이더 정리에서 장애물로 치지 않음)
        GameObject probe = SciFiMapBuilder.Spawn("Hangar_Ground_Plate1", parent);
        if (probe != null)
        {
            Vector3 tile = SciFiMapBuilder.GetBounds(probe).size;
            Object.DestroyImmediate(probe);
            for (int ix = 0; ix < 2; ix++)
            {
                for (int iz = 0; iz < 2; iz++)
                {
                    GameObject plate = SciFiMapBuilder.Spawn("Hangar_Ground_Plate1", parent);
                    Vector2 xz = c + new Vector2((ix - 0.5f) * tile.x, (iz - 0.5f) * tile.z);
                    Bounds b = SciFiMapBuilder.GetBounds(plate);
                    plate.transform.position += new Vector3(xz.x - b.center.x, SciFiMapBuilder.GroundY(xz) + 0.1f - b.max.y, xz.y - b.center.z);
                }
            }
            float padR = Mathf.Max(tile.x, tile.z);
            SciFiMapBuilder.Occupy(c, padR);
            for (int k = 0; k < 4; k++)
            {
                float a = (45f + k * 90f) * Mathf.Deg2Rad;
                GameObject lamp = SciFiMapBuilder.Spawn("Lamp_Position", parent);
                if (lamp == null) continue;
                SciFiMapBuilder.SetOnGround(lamp, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (padR + 0.6f), 0f);
                Bounds lb = SciFiMapBuilder.GetBounds(lamp);
                SciFiMapBuilder.AddPointLight(lamp.transform, "GuideLight", new Vector3(lb.center.x, lb.max.y + 0.3f, lb.center.z), SciFiMapBuilder.GuideLightColor, 6f, 2f);
            }
        }

        SciFiMapBuilder.Scatter(parent, new[] { "Container_01", "Prop_Cylinder1", "Powerbox_03", "Prop_Barrel1 gray" },
            5, c, 8f, HalfWidth(SupplyZ), 2f, 1f, 1f, 0f, 0f);
    }

    /// <summary>⑤ 전초기지 게이트: 협곡을 가로막는 기지 외벽과 가운데 게이트. 통과하면 인게임 씬으로 넘어간다.</summary>
    private static void BuildGate(Transform parent, Transform markers)
    {
        Marker(markers, "Stage5_OutpostGate", OnCenter(GateZ - 12f), 0f);
        Marker(markers, "GateTrigger (인게임 전환)", OnCenter(GateZ + 1f), 0f);

        Vector2 c = OnCenter(GateZ);
        GameObject gate = SciFiMapBuilder.Spawn("Wall1_Gate", parent);
        if (gate == null) return;
        Bounds gb = SciFiMapBuilder.GetBounds(gate);
        if (gb.size.z > gb.size.x) gate.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * gate.transform.rotation; // 긴 변이 협곡을 가로지르게
        SciFiMapBuilder.SetOnGround(gate, c, 0f);
        gb = SciFiMapBuilder.GetBounds(gate);

        // 게이트 양옆을 벽 조각으로 협곡 벽까지 막는다.
        float reach = HalfWidth(GateZ) + WallRamp * 0.5f;
        foreach (int side in new[] { -1, 1 })
        {
            float edge = side < 0 ? gb.min.x : gb.max.x;
            for (int guard = 0; guard < 12 && Mathf.Abs(edge - c.x) < reach; guard++)
            {
                GameObject wall = SciFiMapBuilder.Spawn(guard % 2 == 0 ? "Wall1A" : "Wall1B", parent);
                if (wall == null) break;
                Bounds wb = SciFiMapBuilder.GetBounds(wall);
                if (wb.size.z > wb.size.x) wall.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * wall.transform.rotation;
                wb = SciFiMapBuilder.GetBounds(wall);
                float len = Mathf.Max(wb.size.x, 1f);
                SciFiMapBuilder.SetOnGround(wall, new Vector2(edge + side * len * 0.5f, c.y), 0.05f);
                edge += side * len;
            }
            Vector3 lampPos = new Vector3(side < 0 ? gb.min.x : gb.max.x, gb.max.y + 0.5f, gb.center.z - gb.extents.z - 0.5f);
            SciFiMapBuilder.AddPointLight(parent, side < 0 ? "GateLight_L" : "GateLight_R", lampPos, SciFiMapBuilder.GuideLightColor, 8f, 3f);
        }
        SciFiMapBuilder.Occupy(c, Mathf.Abs(gb.extents.x) + 2f);
    }

    /// <summary>협곡 벽 아래를 따라 바위를 흩어 벽과 바닥의 경계를 자연스럽게 만든다.</summary>
    private static void BuildWallFoot(Transform parent)
    {
        string[] rocks = { "Rock_Shales_01", "Rock_Shales_03", "Rock_Shales_08", "Stone_Big1", "Stone_med6" };
        for (float z = 12f; z < GateZ - 6f; z += 9f)
        {
            foreach (int side in new[] { -1, 1 })
            {
                Vector2 xz = new Vector2(CenterX(z) + side * (HalfWidth(z) - 1.5f), z + Random.Range(-3f, 3f));
                GameObject go = SciFiMapBuilder.Spawn(rocks[Random.Range(0, rocks.Length)], parent);
                if (go == null) continue;
                go.transform.localScale *= Random.Range(1f, 1.8f);
                go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * go.transform.rotation;
                SciFiMapBuilder.SetOnGround(go, xz, 0.3f);
            }
        }
    }

    // ───────────────────────── 마커·카메라 ─────────────────────────

    /// <summary>튜토리얼 단계 위치를 표시하는 빈 오브젝트. 진행 로직 담당이 이 위치에 트리거·안내를 붙인다.</summary>
    private static void Marker(Transform parent, string name, Vector2 xz, float yaw)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(new Vector3(xz.x, SciFiMapBuilder.GroundY(xz), xz.y), Quaternion.Euler(0f, yaw, 0f));
    }

    /// <summary>씬 기본 카메라를 시작 지점 눈높이에서 협곡 안쪽(+Z)을 보게 둔다. (미리보기용)</summary>
    private static void AimCamera(Scene scene)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (!go.TryGetComponent(out Camera cam)) continue;
            Vector2 start = OnCenter(PlayerStartZ);
            cam.transform.SetPositionAndRotation(new Vector3(start.x, SciFiMapBuilder.GroundY(start) + 1.7f, start.y), Quaternion.LookRotation(Vector3.forward));
            break;
        }
    }
}
