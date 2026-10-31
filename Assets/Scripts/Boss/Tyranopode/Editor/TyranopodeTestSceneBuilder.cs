using System.Linq;
using HybridShooter.Boss.Tyranopode;
using HybridShooter.Core.Contracts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HybridShooter.Boss.TyranopodeEditor
{
    /// <summary>
    /// Test_Tyranopode 씬을 만든다: 본 게임 씬(맵·NavMesh·플레이어·잡몹 풀)을 복사하고 라운드 흐름(RunFlow)을 끈 뒤
    /// 보스 출현 지점에 Tyranopode 와 디버그 HUD 를 놓는다. 원본 씬은 건드리지 않는다
    /// </summary>
    public static class TyranopodeTestSceneBuilder
    {
        private const string SourceScene = "Assets/Scenes/TestScenes/Monster Player Integration.unity";
        private const string TargetScene = "Assets/Scenes/MS/Scenes/Test_Tyranopode.unity";
        private const string PrefabPath = "Assets/Resources/Boss/Tyranopode/Tyranopode.prefab";
        private const string RoundFlowObject = "RunFlow";
        private const string BossSpawnPoint = "BossSpawnPoint";

        [MenuItem("Tools/HybridShooter/Boss/Tyranopode 테스트 씬 만들기")]
        public static void Create()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScene) != null
                && EditorUtility.DisplayDialog("Tyranopode", $"{TargetScene} 를 다시 만들까요? (씬에서 바꾼 내용은 사라집니다)", "다시 만들기", "취소") == false)
                return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) TyranopodePrefabBuilder.Build();

            AssetDatabase.DeleteAsset(TargetScene);
            if (AssetDatabase.CopyAsset(SourceScene, TargetScene) == false)
            {
                Debug.LogError($"[TyranopodeTestScene] 씬 복사 실패: {SourceScene}");
                return;
            }

            var scene = EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();

            // 라운드 흐름을 끈다: 웨이브·라운드 보스 없이 Tyranopode 와만 싸운다 (잡몹 풀은 그대로 → 보스 소환에 쓴다)
            var roundFlow = roots.FirstOrDefault(r => r.name == RoundFlowObject);
            if (roundFlow != null) roundFlow.SetActive(false);
            else Debug.LogWarning($"[TyranopodeTestScene] '{RoundFlowObject}' 를 찾지 못했습니다. 라운드가 같이 돌 수 있습니다.");

            var spawn = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == BossSpawnPoint);
            var boss = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            boss.transform.position = spawn != null ? spawn.position : Vector3.zero;

            var player = roots.SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).FirstOrDefault(m => m is IPlayerTarget);
            var controller = new SerializedObject(boss.GetComponent<TyranopodeController>());
            controller.FindProperty("playerSource").objectReferenceValue = player;
            controller.ApplyModifiedPropertiesWithoutUndo();

            var hud = new GameObject("TyranopodeDebugHud").AddComponent<TyranopodeDebugHud>();
            var hudSo = new SerializedObject(hud);
            hudSo.FindProperty("boss").objectReferenceValue = boss.GetComponent<TyranopodeController>();
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = boss;
            Debug.Log($"[TyranopodeTestScene] 완료: {TargetScene} (Play → F1 디버그 창)", boss);
        }
    }
}
