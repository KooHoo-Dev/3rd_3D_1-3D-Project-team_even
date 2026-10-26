using HybridShooter.Audio.Tester;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace HybridShooter.Audio.Editor
{
    /// <summary>
    /// Tools/Audio/Create Sound Test Scene.
    /// 플레이어·무기·몬스터 소리를 한 씬에서 확인하는 사운드 작업용 씬을 GU 폴더에 만든다.
    ///   - 플레이어: MS 의 Player 프리팹을 원본으로 PlayerSoundEmitter 만 더한 Prefab Variant (+ 테스트용 IPlayerTarget 등록)
    ///   - 바닥: 평평한 Terrain (독장판·포격 목표가 Terrain 높이를 쓰므로 Plane 이 아니라 Terrain), 점프 상자, 돌진 충돌용 벽
    ///   - NavMesh: NavMeshSurface 로 구워 씬 폴더에 에셋으로 저장 (몬스터 이동용)
    ///   - 몬스터: 돌진(레탈로브라크)·도약(파르다트록스)·독 포격(자이언트슬러그)·근접(무스코모르프) — HS 프리팹을 배치만 한다.
    ///     프리팹에 Collider 가 없어 씬 인스턴스에만 임시 CapsuleCollider 를 붙인다 (프리팹 수정 없음)
    ///   - 0 키 리스폰: SoundTestMonsterRespawner 가 처음 배치대로 몬스터를 다시 만든다
    ///   - SoundService + GameMixer. 몬스터 이미터는 실행 시 GameSoundRouter 가 자동으로 붙인다
    /// 이미 있으면 다시 만들지 물어본다 (다시 만들면 씬을 새로 덮어쓴다)
    /// </summary>
    internal static class SoundTestSceneMenu
    {
        private const string BasePlayerPath = "Assets/Scenes/MS/Prefabs/Player.prefab";
        private const string VariantFolder = "Assets/Scenes/GU/Prefabs";
        private const string VariantPath = VariantFolder + "/Player_Sound.prefab";
        private const string ScenePath = "Assets/Scenes/GU/SoundIntegrationTest.unity";
        private const string SceneDataFolder = "Assets/Scenes/GU/SoundIntegrationTest";
        private const string MonsterPrefabFolder = "Assets/Scenes/HS/MonsterIntegrationLab/Prefabs/Monsters/";

        private const float GroundSize = 120f; // m. 플레이어가 가운데

        /// <summary>몬스터 프리팹 이름과 배치 위치 (플레이어 = 원점, +Z 앞쪽)</summary>
        private static readonly (string Prefab, Vector3 Position)[] Monsters =
        {
            ("Letalobrach", new Vector3(0f, 0f, 28f)),        // 돌진 → 플레이어 뒤 벽(-Z)에 충돌·기절
            ("Pardathrox", new Vector3(22f, 0f, 18f)),        // 시야 밖 도약
            ("GiantSlug", new Vector3(-22f, 0f, 22f)),        // 독 포격 (플레이어가 4초 멈추면) → 착탄·장판·기침
            ("Muscomorph_Ground", new Vector3(10f, 0f, 35f)), // 일반 근접 (울음·이동 루프·사망)
        };

        [MenuItem("Tools/Audio/Create Sound Test Scene")]
        private static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
                EditorUtility.DisplayDialog("Sound Test Scene", $"{ScenePath} 가 이미 있습니다.\n다시 만들까요? (씬을 새로 덮어씁니다)", "다시 만들기", "열기만") == false)
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
                return;
            }

            var variant = CreatePlayerVariant();
            if (variant == null) return;
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;
            if (AssetDatabase.IsValidFolder(SceneDataFolder) == false) AssetDatabase.CreateFolder("Assets/Scenes/GU", "SoundIntegrationTest");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var mainCamera = GameObject.Find("Main Camera");
            if (mainCamera != null) Object.DestroyImmediate(mainCamera); // 플레이어 프리팹이 카메라·리스너를 가짐

            CreateGround();
            CreateBox("JumpBox (1m)", new Vector3(0f, 0.5f, 6f), new Vector3(3f, 1f, 3f));
            CreateBox("ChargeWall", new Vector3(0f, 2f, -5f), new Vector3(24f, 4f, 1f)); // 플레이어 바로 뒤. 돌진을 옆으로 피하면 레탈로브라크가 박힌다
            BakeNavMesh(); // 플레이어·몬스터를 놓기 전에 구워 바닥·상자·벽만 들어가게

            var soundService = new GameObject("SoundService", typeof(SoundService), typeof(GameSoundRouter));
            SoundServiceMenu.AssignMixer(soundService.GetComponent<SoundService>());

            var player = (GameObject)PrefabUtility.InstantiatePrefab(variant, scene);
            player.transform.position = new Vector3(0f, 0.1f, 0f);
            player.AddComponent<SoundTestPlayerTarget>(); // 씬 인스턴스에만 (Variant 는 그대로)

            var monsterRoot = new GameObject("Monsters").transform;
            var entries = new System.Collections.Generic.List<SoundTestMonsterRespawner.Entry>();
            foreach (var (prefabName, position) in Monsters)
            {
                var prefab = PlaceMonster(prefabName, position, monsterRoot);
                if (prefab != null) entries.Add(new SoundTestMonsterRespawner.Entry { Prefab = prefab, Position = position });
            }
            var respawner = new GameObject("MonsterRespawner (0 키)").AddComponent<SoundTestMonsterRespawner>();
            respawner.Setup(entries.ToArray(), monsterRoot);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Audio] {ScenePath} 생성 (플레이어 = {VariantPath}, 몬스터 {monsterRoot.childCount}마리)");
        }

        /// <summary>평평한 Terrain. TerrainData 는 씬 폴더에 에셋으로 저장</summary>
        private static void CreateGround()
        {
            string dataPath = SceneDataFolder + "/Ground_TerrainData.asset";
            AssetDatabase.DeleteAsset(dataPath);
            var data = new TerrainData { heightmapResolution = 33, size = new Vector3(GroundSize, 10f, GroundSize) };
            AssetDatabase.CreateAsset(data, dataPath);

            var ground = Terrain.CreateTerrainGameObject(data);
            ground.name = "Ground (Terrain)";
            ground.transform.position = new Vector3(-GroundSize / 2f, 0f, -GroundSize / 2f);
            var terrainMaterial = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial : null;
            if (terrainMaterial != null) ground.GetComponent<Terrain>().materialTemplate = terrainMaterial;
        }

        private static void CreateBox(string name, Vector3 position, Vector3 scale)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.position = position;
            box.transform.localScale = scale;
        }

        /// <summary>바닥·상자·벽 콜라이더로 NavMesh 를 굽고, 씬 폴더에 에셋으로 저장해 씬과 함께 남게 한다</summary>
        private static void BakeNavMesh()
        {
            var surface = new GameObject("NavMesh").AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            string dataPath = SceneDataFolder + "/NavMesh.asset";
            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(surface.navMeshData, dataPath);
        }

        /// <summary>몬스터 프리팹을 배치하고 임시 히트박스를 붙인다. 리스폰 목록에 넣을 프리팹을 돌려준다 (없으면 null)</summary>
        private static GameObject PlaceMonster(string prefabName, Vector3 position, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrefabFolder + prefabName + ".prefab");
            if (prefab == null)
            {
                Debug.LogWarning($"[Audio] 몬스터 프리팹이 없습니다: {MonsterPrefabFolder}{prefabName}.prefab");
                return null;
            }
            var monster = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            monster.transform.SetPositionAndRotation(position, Quaternion.LookRotation(-position.normalized)); // 플레이어 쪽을 본다
            // [임시] HS 프리팹에 Collider 가 없어 씬 인스턴스에만 히트박스 (프리팹 수정 없음)
            if (SoundTestMonsterRespawner.AddTemporaryHitbox(monster)) Debug.Log($"[Audio] {monster.name} 에 임시 히트박스 — 프리팹에 Collider 없음");
            return prefab;
        }

        /// <summary>Variant 가 없으면 원본 인스턴스에 PlayerSoundEmitter 를 더해 Variant 로 저장. 있으면 그대로 사용</summary>
        private static GameObject CreatePlayerVariant()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
            if (existing != null) return existing;

            var basePlayer = AssetDatabase.LoadAssetAtPath<GameObject>(BasePlayerPath);
            if (basePlayer == null)
            {
                Debug.LogError($"[Audio] 원본 플레이어 프리팹이 없습니다: {BasePlayerPath}");
                return null;
            }
            if (AssetDatabase.IsValidFolder(VariantFolder) == false) AssetDatabase.CreateFolder("Assets/Scenes/GU", "Prefabs");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePlayer);
            try
            {
                if (instance.GetComponent<PlayerSoundEmitter>() == null) instance.AddComponent<PlayerSoundEmitter>();
                // 프리팹 인스턴스를 저장하면 원본을 따라가는 Variant 가 된다
                return PrefabUtility.SaveAsPrefabAsset(instance, VariantPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
