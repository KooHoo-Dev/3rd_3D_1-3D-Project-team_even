using System.Collections.Generic;
using System.Linq;
using HybridShooter.Core.Battlefield;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace HybridShooter.Weapon.EditorTools
{
    /// <summary>
    /// SciFiMap 연료 셀(ExplosiveProp) 만들기 · MS 테스트 씬 · 통합 씬 적용 (메뉴: Tools/HybridShooter/Map/연료 셀).
    ///
    ///  0. 폭발 이펙트(Explosion_FuelCell)는 WeaponVfxSetupMenu 가 만든다 (메뉴 0, 없으면 1 이 대신 부른다)
    ///  1. 연료 셀 프리팹: Sci-Fi Base 노란 드럼통에 붉은 발광 띠·경고등·누출 연기를 달고, 높이를 1.7m(올라설 수 없는 높이 단계)로 맞춘다.
    ///     ExplosiveProp 은 새로 붙이므로 코드 기본값(내구도·소리 키 등)이 그대로 들어간다 → 값을 바꾸면 이 메뉴를 다시 실행
    ///  2. 테스트 씬: Monster Player Integration 을 MS/Scenes/Test_FuelCell 로 복사해 배치 프리팹(FuelCellLayout)을 놓는다.
    ///     배치 프리팹이 없으면 SciFiMap 지점 그룹(Props/Refinery 등) 둘레에 자동으로 만든다
    ///  3. 배치 다듬기: 테스트 씬에서 셀을 옮기고 FuelCellLayout 인스턴스의 Overrides → Apply All 로 프리팹에 저장
    ///  4. 통합 씬 적용: Monster Player Integration 에 FuelCellLayout 인스턴스 하나만 놓는다 (이미 있으면 위치만 다시 맞춤)
    ///
    /// 배치는 SciFiMap.prefab 이 아닌 별도 프리팹에 담는다 → 맵 빌더·SciFiMapEdits·NavMesh 에셋과 SciFiMap 을 쓰는 다른 씬을 건드리지 않는다.
    /// 배치 프리팹 루트는 SciFiMap 루트와 같은 자세에 놓는다 → 맵 위치가 다른 씬에서도 같은 자리에 선다.
    /// 공유 설정(SpawnPointFinder·총의 레이어)은 바꾸지 않고, 셀 레이어가 빠져 있으면 경고만 한다
    /// </summary>
    public static class FuelCellSceneTool
    {
        private const string Menu = "Tools/HybridShooter/Map/연료 셀/";
        private const string Tag = "[FuelCellSceneTool]";

        private const string CellSourcePath = "Assets/Sci-Fi Base/Prefabs/Prop_Barrel1 yellow.prefab";
        private const string SmokePath = "Assets/Scenes/GU/Prefabs/EngineSmoke.prefab";
        private const string CrushEffectPath = "Assets/Prefabs/Effects/Impact_Explosion.prefab";

        private const string OutputFolder = "Assets/Scenes/MS/Prefabs/Hazards";
        private const string CellPrefabPath = OutputFolder + "/ExplosiveFuelCell.prefab";
        private const string LayoutPrefabPath = OutputFolder + "/FuelCellLayout.prefab";
        private const string GlowMaterialPath = OutputFolder + "/M_FuelCell_Glow.mat";

        private const string IntegrationScenePath = "Assets/Scenes/TestScenes/Monster Player Integration.unity";
        private const string TestScenePath = "Assets/Scenes/MS/Scenes/Test_FuelCell.unity";
        private const string MapRootName = "SciFiMap";
        private const string LayoutName = "FuelCellLayout";
        private const string TesterName = "FuelCellTester";

        /// <summary>SpawnPointFinder.obstacleLayers 에 이미 들어 있는 레이어 → 보스·몬스터가 셀 위에 생성되지 않는다</summary>
        private const string CellLayerName = "Pillars";
        /// <summary>맵 높이 단계 ② (1.6~1.8m): 밟고 점프해도 높은 단계에 오르지 못한다</summary>
        private const float CellHeight = 1.7f;
        private const int LayoutSeed = 20261103;

        private static readonly Color GlowColor = new Color(4f, 0.25f, 0.15f);
        private static readonly Color LightColor = new Color(1f, 0.15f, 0.1f);

        // 자동 배치: 지점 그룹마다 개수·중심에서의 거리(m). Paired = 두 번째 셀을 첫 셀 옆에 붙여 연쇄 폭발 자리로
        private struct Spot
        {
            public string Site;
            public int Count;
            public float MinRadius, MaxRadius;
            public bool Paired;
            public Spot(string site, int count, float minRadius, float maxRadius, bool paired = false)
            {
                Site = site; Count = count; MinRadius = minRadius; MaxRadius = maxRadius; Paired = paired;
            }
        }

        private static readonly Spot[] Spots =
        {
            new Spot("Refinery", 2, 6f, 20f, paired: true),   // 정제 플랜트: 연료 시설
            new Spot("Camp", 2, 8f, 18f, paired: true),       // 간이 공항 격납고
            new Spot("Lab", 1, 8f, 16f),                      // 연구소 발전 장비
            new Spot("DefenseLine", 2, 6f, 20f),              // 외곽 방어선
            new Spot("LandingPad", 2, 20f, 28f),              // 착륙장 엄폐 링 (안쪽 15m 는 비움)
            new Spot("RuinsSE", 1, 8f, 16f),
            new Spot("RuinsS", 1, 8f, 16f),
            new Spot("RuinsN", 1, 8f, 16f),
            new Spot("RuinsE", 1, 8f, 16f),
            new Spot("RuinsSW", 1, 8f, 16f),
            new Spot("Wasteland", 1, 6f, 18f),
        };

        // Props/<지점> 을 못 찾을 때 쓰는 기본 중심 (SciFiMapBuilder.DefaultSiteCenters, 맵 루트 기준 XZ)
        private static readonly Dictionary<string, Vector2> FallbackCenters = new Dictionary<string, Vector2>
        {
            ["LandingPad"] = new Vector2(35f, 9f), ["CrashSite"] = new Vector2(-49.43f, -90.27f),
            ["DefenseLine"] = new Vector2(125f, 10f), ["Refinery"] = new Vector2(110f, 95f),
            ["Wasteland"] = new Vector2(-60f, 10f), ["RuinsSE"] = new Vector2(99f, -55f),
            ["RuinsS"] = new Vector2(19f, -82f), ["RuinsN"] = new Vector2(51f, 98f),
            ["RuinsSW"] = new Vector2(-34f, -50f), ["RuinsE"] = new Vector2(113f, 54f),
            ["Camp"] = new Vector2(63f, -19f), ["Lab"] = new Vector2(2f, 108f),
        };

        private const float PadClearRadius = 15f;    // 구조선 착륙 자리
        private const float CrashClearRadius = 25f;  // 플레이어 시작 위치
        private const float MinSpacing = 12f;        // 묶음이 아닌 셀끼리 (한 번에 다 연쇄되지 않게)
        private const float PairMin = 2.5f, PairMax = 3.5f;
        private const float ClearanceRadius = 1f;
        private const int TriesPerCell = 80;

        // ───────────────────────── 메뉴 ─────────────────────────

        [MenuItem(Menu + "1. 연료 셀 프리팹 만들기")]
        public static void BuildCellPrefabMenu()
        {
            BuildCellPrefab();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(CellPrefabPath);
        }

        [MenuItem(Menu + "2. MS 테스트 씬 만들기 (Test_FuelCell)")]
        public static void CreateTestScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) != null
                && EditorUtility.DisplayDialog("연료 셀", $"{TestScenePath} 를 다시 만들까요? (씬에서 바꾼 내용은 사라집니다. 프리팹에 Apply 한 배치는 남습니다)", "다시 만들기", "취소") == false)
                return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CellPrefabPath) == null) BuildCellPrefab();

            AssetDatabase.DeleteAsset(TestScenePath);
            if (AssetDatabase.CopyAsset(IntegrationScenePath, TestScenePath) == false)
            {
                Debug.LogError($"{Tag} 씬 복사 실패: {IntegrationScenePath}");
                return;
            }

            var scene = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
            if (TryFindMap(scene, out Transform map) == false) return;

            GameObject layout = AssetDatabase.LoadAssetAtPath<GameObject>(LayoutPrefabPath) == null
                ? GenerateLayout(scene, map)
                : PlaceLayout(scene, map);

            var tester = new GameObject(TesterName);
            SceneManager.MoveGameObjectToScene(tester, scene);
            tester.AddComponent<ExplosivePropTester>();

            CheckScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = layout;
            Debug.Log($"{Tag} 테스트 씬 완성: {TestScenePath} (F7 = 셀 모두 되살리기, F12 = 보스로 건너뛰기)");
        }

        [MenuItem(Menu + "3. 열린 씬에서 배치 다시 자동 생성")]
        public static void RegenerateLayoutMenu()
        {
            var scene = SceneManager.GetActiveScene();
            if (TryFindMap(scene, out Transform map) == false) return;
            if (EditorUtility.DisplayDialog("연료 셀", $"{LayoutPrefabPath} 의 배치를 자동 배치로 덮어쓸까요? (손으로 다듬은 위치는 사라집니다)", "덮어쓰기", "취소") == false)
                return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CellPrefabPath) == null) BuildCellPrefab();

            foreach (var old in FindLayoutInstances(scene)) Object.DestroyImmediate(old);
            Selection.activeGameObject = GenerateLayout(scene, map);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        [MenuItem(Menu + "4. Monster Player Integration 에 적용")]
        public static void ApplyToIntegrationSceneMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;
            if (EditorUtility.DisplayDialog("연료 셀",
                    "팀 공유 씬(Monster Player Integration)에 FuelCellLayout 인스턴스 하나를 놓고 저장합니다.\n다른 사람이 이 씬을 고치는 중이 아닐 때 실행하세요.",
                    "적용", "취소") == false)
                return;
            ApplyToIntegrationScene();
        }

        /// <summary>배치 모드: Unity.exe -batchmode -quit -projectPath . -executeMethod HybridShooter.Weapon.EditorTools.FuelCellSceneTool.ApplyToIntegrationScene</summary>
        public static void ApplyToIntegrationScene()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(LayoutPrefabPath) == null)
            {
                Debug.LogError($"{Tag} {LayoutPrefabPath} 가 없습니다. 먼저 '2. MS 테스트 씬 만들기' 로 배치를 만드세요.");
                return;
            }

            var scene = EditorSceneManager.OpenScene(IntegrationScenePath, OpenSceneMode.Single);
            if (TryFindMap(scene, out Transform map) == false) return;
            PlaceLayout(scene, map);
            CheckScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"{Tag} {IntegrationScenePath} 에 연료 셀 배치를 적용했습니다.");
        }

        // ───────────────────────── 연료 셀 프리팹 ─────────────────────────

        private static void BuildCellPrefab()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(CellSourcePath);
            if (source == null) throw new System.InvalidOperationException($"{Tag} 원본 드럼통을 찾지 못했습니다: {CellSourcePath}");
            EnsureFolder(OutputFolder);
            Material glow = BuildGlowMaterial();

            var root = new GameObject("ExplosiveFuelCell");
            try
            {
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);

                // 원본 드럼통: 바닥 중심이 루트 원점에 오게, 높이 CellHeight 로
                var body = (GameObject)PrefabUtility.InstantiatePrefab(source);
                PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                body.name = "Body";
                body.transform.SetParent(visual.transform, false);
                body.transform.localPosition = Vector3.zero;
                foreach (var col in body.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);

                Bounds bounds = MeshBounds(body);
                body.transform.localScale *= CellHeight / Mathf.Max(0.01f, bounds.size.y);
                bounds = MeshBounds(body);
                body.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                bounds = MeshBounds(body);

                float diameter = Mathf.Max(bounds.size.x, bounds.size.z);
                float height = bounds.size.y;

                // 붉은 발광 띠 두 줄 (어두운 맵에서도 셀임을 알아보게)
                var bands = new List<Renderer>();
                foreach (float t in new[] { 0.3f, 0.72f })
                {
                    var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Object.DestroyImmediate(band.GetComponent<Collider>());
                    band.name = $"GlowBand_{Mathf.RoundToInt(t * 100)}";
                    band.transform.SetParent(visual.transform, false);
                    band.transform.localPosition = new Vector3(0f, height * t, 0f);
                    band.transform.localScale = new Vector3(diameter * 1.04f, 0.04f, diameter * 1.04f);
                    var renderer = band.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = glow;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    bands.Add(renderer);
                }

                // 경고등: 그림자 없는 작은 점광원 (셀마다 하나라 그림자는 끈다)
                var lightGo = new GameObject("WarningLight");
                lightGo.transform.SetParent(root.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, height + 0.3f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = LightColor;
                light.range = 4f;
                light.intensity = 3f;
                light.shadows = LightShadows.None;

                // 누출 연기 (위험 단계에서 켠다). EngineSmoke 는 Scaling Mode = Local 이라 자기 크기로 줄인다
                GameObject leak = null;
                var smoke = AssetDatabase.LoadAssetAtPath<GameObject>(SmokePath);
                if (smoke != null)
                {
                    leak = (GameObject)PrefabUtility.InstantiatePrefab(smoke);
                    PrefabUtility.UnpackPrefabInstance(leak, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    leak.name = "Leak";
                    leak.transform.SetParent(root.transform, false);
                    leak.transform.localPosition = new Vector3(0f, height, 0f);
                    leak.transform.localScale = Vector3.one * 0.35f;
                    leak.SetActive(false);
                }
                else Debug.LogWarning($"{Tag} 누출 연기 프리팹이 없어 연기 없이 만듭니다: {SmokePath}");

                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, height * 0.5f, 0f);
                capsule.height = height;
                capsule.radius = diameter * 0.5f;

                // NavMesh: 다시 구워도 셀은 빼고(부서지면 길이 열려야 하므로), 대신 장애물로 길을 판다
                var obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Capsule;
                obstacle.center = capsule.center;
                obstacle.height = height;
                obstacle.radius = capsule.radius;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
                root.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

                var prop = root.AddComponent<ExplosiveProp>();
                var so = new SerializedObject(prop);
                so.FindProperty("visualRoot").objectReferenceValue = visual;
                so.FindProperty("warningLight").objectReferenceValue = light;
                so.FindProperty("leakEffect").objectReferenceValue = leak;
                so.FindProperty("explosion.effectPrefab").objectReferenceValue = LoadOrBuildExplosion();
                so.FindProperty("crushEffectPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(CrushEffectPath);
                var glowRenderers = so.FindProperty("glowRenderers");
                glowRenderers.arraySize = bands.Count;
                for (int i = 0; i < bands.Count; i++) glowRenderers.GetArrayElementAtIndex(i).objectReferenceValue = bands[i];
                so.ApplyModifiedPropertiesWithoutUndo();

                SetLayerRecursive(root, LayerMask.NameToLayer(CellLayerName));
                PrefabUtility.SaveAsPrefabAsset(root, CellPrefabPath);
                Debug.Log($"{Tag} 연료 셀 프리팹 저장: {CellPrefabPath} (높이 {height:0.00}m, 지름 {diameter:0.00}m)");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>연료 셀 폭발 이펙트 (WeaponVfxSetupMenu 가 만든다). 없으면 그 자리에서 만든다</summary>
        private static GameObject LoadOrBuildExplosion()
        {
            var explosion = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponVfxSetupMenu.FuelCellExplosionPath)
                            ?? WeaponVfxSetupMenu.BuildFuelCellExplosion();
            if (explosion == null) Debug.LogError($"{Tag} 연료 셀 폭발 이펙트를 만들지 못했습니다: {WeaponVfxSetupMenu.FuelCellExplosionPath}");
            return explosion;
        }

        /// <summary>URP Lit + HDR Emission (Bloom 에서 빛난다). 다시 만들면 같은 에셋을 고친다 (GUID 유지)</summary>
        private static Material BuildGlowMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, GlowMaterialPath);
            }
            material.SetColor("_BaseColor", new Color(0.35f, 0.02f, 0.02f));
            material.SetFloat("_Smoothness", 0.6f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", GlowColor);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ───────────────────────── 배치 ─────────────────────────

        /// <summary>지점 그룹 둘레에서 평평하고 비어 있는 땅을 골라 셀을 놓고, FuelCellLayout 프리팹으로 저장해 연결한다</summary>
        private static GameObject GenerateLayout(Scene scene, Transform map)
        {
            var cellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CellPrefabPath);
            var terrain = map.GetComponentInChildren<Terrain>();
            if (terrain == null)
            {
                Debug.LogError($"{Tag} {map.name} 에 Terrain 이 없어 자동 배치를 할 수 없습니다.", map);
                return null;
            }

            var layout = new GameObject(LayoutName);
            SceneManager.MoveGameObjectToScene(layout, scene);
            layout.transform.SetPositionAndRotation(map.position, map.rotation);

            Physics.SyncTransforms();
            bool hasNavMesh = NavMesh.CalculateTriangulation().vertices.Length > 0;
            if (hasNavMesh == false) Debug.LogWarning($"{Tag} NavMesh 가 없어 걸을 수 있는 자리인지 확인하지 않고 배치합니다.");

            Vector3 pad = SiteCenter(map, "LandingPad");
            Vector3 crash = SiteCenter(map, "CrashSite");
            var rng = new System.Random(LayoutSeed);
            var placed = new List<Vector3>();
            int failed = 0;

            foreach (var spot in Spots)
            {
                Vector3 center = SiteCenter(map, spot.Site);
                Vector3? partner = null;
                for (int n = 0; n < spot.Count; n++)
                {
                    bool pairing = spot.Paired && partner.HasValue;
                    bool found = false;
                    for (int attempt = 0; attempt < TriesPerCell && found == false; attempt++)
                    {
                        Vector3 origin = pairing ? partner.Value : center;
                        float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
                        float radius = pairing
                            ? Mathf.Lerp(PairMin, PairMax, (float)rng.NextDouble())
                            : Mathf.Lerp(spot.MinRadius, spot.MaxRadius, (float)rng.NextDouble());
                        Vector3 candidate = origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

                        if (Flat(candidate - pad).magnitude < PadClearRadius + 1f) continue;
                        if (Flat(candidate - crash).magnitude < CrashClearRadius) continue;
                        if (TooClose(candidate, placed, pairing ? partner.Value : (Vector3?)null)) continue;
                        if (TryGround(terrain, candidate, hasNavMesh, out Vector3 ground) == false) continue;

                        var cell = (GameObject)PrefabUtility.InstantiatePrefab(cellPrefab, scene);
                        cell.transform.SetParent(layout.transform, true);
                        cell.transform.SetPositionAndRotation(ground, Quaternion.Euler(0f, (float)(rng.NextDouble() * 360f), 0f));
                        cell.name = $"FuelCell_{spot.Site}_{n + 1}";
                        Physics.SyncTransforms(); // 다음 셀의 빈자리 검사에 방금 놓은 셀이 잡히게
                        placed.Add(ground);
                        partner = ground;
                        found = true;
                    }
                    if (found == false)
                    {
                        failed++;
                        Debug.LogWarning($"{Tag} {spot.Site} 둘레에서 셀 자리를 찾지 못했습니다 ({n + 1}/{spot.Count}).");
                    }
                }
            }

            PrefabUtility.SaveAsPrefabAssetAndConnect(layout, LayoutPrefabPath, InteractionMode.AutomatedAction);
            Debug.Log($"{Tag} 자동 배치 {placed.Count}개 (실패 {failed}) → {LayoutPrefabPath}", layout);
            return layout;
        }

        /// <summary>FuelCellLayout 인스턴스를 SciFiMap 루트 자세에 놓는다. 이미 있으면 하나만 남기고 위치만 맞춘다</summary>
        private static GameObject PlaceLayout(Scene scene, Transform map)
        {
            var instances = FindLayoutInstances(scene);
            GameObject layout;
            if (instances.Count > 0)
            {
                layout = instances[0];
                for (int i = 1; i < instances.Count; i++) Object.DestroyImmediate(instances[i]);
            }
            else
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LayoutPrefabPath);
                layout = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            }
            layout.transform.SetParent(null, false);
            layout.transform.SetPositionAndRotation(map.position, map.rotation);
            if (Vector3.Distance(map.lossyScale, Vector3.one) > 0.001f)
                Debug.LogWarning($"{Tag} {map.name} 크기가 1 이 아닙니다 ({map.lossyScale}). 셀 위치가 맵과 어긋날 수 있습니다.", map);
            return layout;
        }

        private static List<GameObject> FindLayoutInstances(Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LayoutPrefabPath);
            return scene.GetRootGameObjects()
                .Where(go => go.name == LayoutName
                             || (prefab != null && PrefabUtility.GetCorrespondingObjectFromSource(go) == prefab))
                .ToList();
        }

        private static bool TooClose(Vector3 candidate, List<Vector3> placed, Vector3? partner)
        {
            foreach (var p in placed)
            {
                if (partner.HasValue && p == partner.Value) continue;
                if (Flat(candidate - p).magnitude < MinSpacing) return true;
            }
            return false;
        }

        /// <summary>
        /// 셀을 세울 수 있는 땅인가: 위에서 처음 닿는 면이 지면(지형 또는 깔린 바닥판)이고 평평하며,
        /// 셀 둘레에 소품·벽이 없고, (NavMesh 가 있으면) 몬스터·보스가 다니는 곳
        /// </summary>
        private static bool TryGround(Terrain terrain, Vector3 xz, bool checkNavMesh, out Vector3 ground)
        {
            ground = default;
            float terrainY = terrain.SampleHeight(xz) + terrain.transform.position.y;
            Vector3 top = new Vector3(xz.x, terrainY + 60f, xz.z);
            if (Physics.Raycast(top, Vector3.down, out RaycastHit hit, 120f, ~0, QueryTriggerInteraction.Ignore) == false) return false;
            if (hit.normal.y < 0.94f) return false;
            if (hit.point.y - terrainY > 0.35f) return false; // 소품 윗면 (바닥판 두께까지만 허용)
            ground = hit.point;

            Vector3 bottom = ground + Vector3.up * (ClearanceRadius + 0.4f);
            Vector3 upper = ground + Vector3.up * (CellHeight + 0.3f);
            foreach (var col in Physics.OverlapCapsule(bottom, upper, ClearanceRadius, ~0, QueryTriggerInteraction.Ignore))
                if (col is TerrainCollider == false) return false;

            if (checkNavMesh && NavMesh.SamplePosition(ground, out NavMeshHit navHit, 1f, NavMesh.AllAreas) == false) return false;
            return true;
        }

        private static Vector3 SiteCenter(Transform map, string site)
        {
            Transform group = map.Find("Props/" + site);
            if (group != null) return group.position;
            Vector2 xz = FallbackCenters[site];
            Debug.LogWarning($"{Tag} {map.name}/Props/{site} 를 찾지 못해 기본 중심 ({xz.x}, {xz.y}) 을 씁니다.", map);
            return map.TransformPoint(new Vector3(xz.x, 0f, xz.y));
        }

        // ───────────────────────── 점검 ─────────────────────────

        private static bool TryFindMap(Scene scene, out Transform map)
        {
            map = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t => t.name == MapRootName);
            if (map == null) Debug.LogError($"{Tag} {scene.path} 에서 '{MapRootName}' 을 찾지 못했습니다.");
            return map != null;
        }

        /// <summary>공유 설정은 고치지 않고 확인만 한다: 보스 스폰 장애물·총이 셀을 맞히는지</summary>
        private static void CheckScene(Scene scene)
        {
            int layer = LayerMask.NameToLayer(CellLayerName);
            int bit = 1 << layer;
            var behaviours = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).ToList();

            var finders = behaviours.OfType<SpawnPointFinder>().ToList();
            if (finders.Count == 0) Debug.LogWarning($"{Tag} {scene.name}: SpawnPointFinder 가 없습니다.");
            foreach (var finder in finders)
            {
                int mask = new SerializedObject(finder).FindProperty("obstacleLayers").intValue;
                if ((mask & bit) == 0)
                    Debug.LogWarning($"{Tag} {scene.name}: SpawnPointFinder.obstacleLayers 에 '{CellLayerName}' 가 없어 보스·몬스터가 셀 위에 생성될 수 있습니다.", finder);
            }

            foreach (var gun in behaviours.OfType<Gun>())
            {
                int mask = new SerializedObject(gun).FindProperty("hitMask").intValue;
                if ((mask & bit) == 0)
                    Debug.LogWarning($"{Tag} {scene.name}: {gun.name} 의 hitMask 에 '{CellLayerName}' 가 없어 셀을 쏠 수 없습니다.", gun);
            }

            int cells = behaviours.OfType<ExplosiveProp>().Count();
            Debug.Log($"{Tag} {scene.name}: 연료 셀 {cells}개, SpawnPointFinder {finders.Count}개 확인");
        }

        // ───────────────────────── 도우미 ─────────────────────────

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>메시 Renderer 들의 Bounds (파티클 제외)</summary>
        private static Bounds MeshBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>().Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToList();
            if (renderers.Count == 0) return new Bounds(go.transform.position, Vector3.one);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Count; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
