using HybridShooter.Monster.MovementLab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HybridShooter.Monster.MovementLab.Editor
{
    public static class CreateMovementLab
    {
        [MenuItem("Tools/Monster Movement/Create Laboratory")]
        private static void Create()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (!string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path)) continue;
                Debug.LogWarning("이름 없는 기존 씬을 먼저 저장한 뒤 실행하세요. Unity는 미저장 씬 옆에 additive 씬 생성을 허용하지 않습니다.");
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("Monster Movement Lab");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<MovementLabExample>().BuildLaboratory();
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log("새 additive scene에 Monster Movement Laboratory를 만들었습니다. 기존 scene은 저장하거나 교체하지 않았습니다.");
        }
    }
}
