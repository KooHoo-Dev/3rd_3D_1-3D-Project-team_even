using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HybridShooter.Monster.MovementLab.Editor
{
    /// <summary>새 샘플 씬만 저장한다. 기존 씬/에셋을 덮어쓰지 않는다.</summary>
    public static class MovementLabSceneExporter
    {
        public static void ExportNewScene()
        {
            const string folder = "Assets/MonsterMovementLab/Scenes";
            const string scenePath = folder + "/MonsterMovementLaboratory.unity";
            const string meshPath = folder + "/LaboratoryNavMesh.asset";
            if (File.Exists(scenePath) || File.Exists(meshPath))
                throw new IOException("생성 대상이 이미 존재한다. 샘플 씬 덮어쓰기를 거부한다.");
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var previousScene = SceneManager.GetActiveScene();
            // 배치 프로세스가 시작하며 만든 미저장 초기 씬만 대체한다. 사용자 작업 씬에는 적용하지 않는다.
            bool disposableInitialScene = Application.isBatchMode && string.IsNullOrEmpty(previousScene.path)
                && !previousScene.isDirty;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                disposableInitialScene ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("Monster Movement Lab");
            var example = root.AddComponent<MovementLabExample>();
            example.BuildLaboratory();
            var surface = root.GetComponentInChildren<NavMeshSurface>();
            AssetDatabase.CreateAsset(surface.navMeshData, meshPath);
            EditorUtility.SetDirty(surface);
            EditorUtility.SetDirty(example);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, scenePath))
                throw new IOException("실험실 씬 저장 실패");
            AssetDatabase.SaveAssets();
            if (!disposableInitialScene) EditorSceneManager.CloseScene(scene, true);
            if (previousScene.IsValid()) SceneManager.SetActiveScene(previousScene);
            Debug.Log("Movement laboratory exported: " + scenePath);
        }
    }
}
