using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HybridShooter.Audio.Editor
{
    /// <summary>
    /// Tools/Audio/Create Sound Test Scene.
    /// MS 의 Player 프리팹을 원본으로 PlayerSoundEmitter 만 더한 Prefab Variant 를 만들고,
    /// 그 플레이어·바닥·SoundService 가 있는 사운드 작업용 씬을 GU 폴더에 만든다. 이미 있으면 다시 만들지 않고 연다
    /// </summary>
    internal static class SoundTestSceneMenu
    {
        private const string BasePlayerPath = "Assets/Scenes/MS/Prefabs/Player.prefab";
        private const string VariantFolder = "Assets/Scenes/GU/Prefabs";
        private const string VariantPath = VariantFolder + "/Player_Sound.prefab";
        private const string ScenePath = "Assets/Scenes/GU/SoundIntegrationTest.unity";

        [MenuItem("Tools/Audio/Create Sound Test Scene")]
        private static void Create()
        {
            var variant = CreatePlayerVariant();
            if (variant == null) return;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
                Debug.Log($"[Audio] {ScenePath} 가 이미 있어 열기만 했습니다");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var mainCamera = GameObject.Find("Main Camera");
            if (mainCamera != null) Object.DestroyImmediate(mainCamera); // 플레이어 프리팹이 카메라·리스너를 가짐

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(10f, 1f, 10f); // 100m × 100m

            var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.name = "JumpBox (1m)";
            step.transform.position = new Vector3(0f, 0.5f, 6f);
            step.transform.localScale = new Vector3(3f, 1f, 3f);

            var soundService = new GameObject("SoundService", typeof(SoundService), typeof(GameSoundRouter));
            SoundServiceMenu.AssignMixer(soundService.GetComponent<SoundService>());

            var player = (GameObject)PrefabUtility.InstantiatePrefab(variant, scene);
            player.transform.position = new Vector3(0f, 0.1f, 0f);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Audio] {ScenePath} 생성 (플레이어 = {VariantPath})");
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
