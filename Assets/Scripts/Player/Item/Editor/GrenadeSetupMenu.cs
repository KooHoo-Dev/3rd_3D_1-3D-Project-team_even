using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HybridShooter.Player.EditorTools
{
    /// <summary>
    /// 수류탄 프리팹 만들기 + PlayerTest 씬의 Player 에 연결 (메뉴 한 번).
    ///
    ///  1. PlayerTest 씬을 연다
    ///  2. SF 무기 팩의 SciFiGrenade01_1 을 씬에 놓고 프리팹 연결을 완전히 푼다 (Static 도 끔)
    ///  3. Rigidbody + Grenade 컴포넌트를 붙인다 (충돌은 원본의 UCX 볼록 메시 콜라이더)
    ///  4. Assets/Prefabs/Weapons/Grenade.prefab 으로 저장하고 씬의 사본은 지운다
    ///  5. Player 에 PlayerItems 를 붙이고 grenadePrefab 에 연결한 뒤 씬 저장
    /// </summary>
    public static class GrenadeSetupMenu
    {
        private const string ScenePath = "Assets/Scenes/MS/PlayerTest.unity";
        private const string SourcePrefabPath = "Assets/ExternalAssets/Sci-fi-Weapons/SciFiWorld/Weapons/Prefabs/SciFiGrenade01_1.prefab";
        private const string PrefabFolder = "Assets/Prefabs/Weapons";
        private const string PrefabPath = PrefabFolder + "/Grenade.prefab";

        [MenuItem("Tools/HybridShooter/Setup Grenade (PlayerTest)")]
        private static void Setup()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("수류탄 설정", "플레이 모드를 끄고 실행하세요.", "확인");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            Grenade prefab = AssetDatabase.LoadAssetAtPath<Grenade>(PrefabPath);
            if (prefab == null || EditorUtility.DisplayDialog("수류탄 설정",
                    $"{PrefabPath} 가 이미 있습니다. 원본에서 다시 만들까요?\n(아니오 = 있는 프리팹을 Player 에 연결만)", "다시 만들기", "연결만"))
            {
                prefab = CreatePrefab();
                if (prefab == null) return;
            }

            var player = Object.FindFirstObjectByType<PlayerController>();
            if (player == null)
            {
                EditorUtility.DisplayDialog("수류탄 설정", "씬에서 PlayerController 를 찾지 못했습니다.", "확인");
                return;
            }

            var items = player.GetComponent<PlayerItems>();
            if (items == null) items = Undo.AddComponent<PlayerItems>(player.gameObject);

            var so = new SerializedObject(items);
            so.FindProperty("grenadePrefab").objectReferenceValue = prefab;
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeObject = items;
            EditorGUIUtility.PingObject(prefab);
            Debug.Log($"[GrenadeSetup] {PrefabPath} 생성 및 {player.name} 의 PlayerItems 에 연결 완료", items);
        }

        private static Grenade CreatePrefab()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (source == null)
            {
                EditorUtility.DisplayDialog("수류탄 설정", $"원본 프리팹이 없습니다:\n{SourcePrefabPath}", "확인");
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "Grenade";
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // SF 팩 프리팹은 Static 이라 그대로 두면 움직이지 않는다
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);

            // 충돌용 UCX 메시 콜라이더: Rigidbody 와 쓰려면 볼록(convex) 이어야 한다
            foreach (var mesh in go.GetComponentsInChildren<MeshCollider>(true))
            {
                mesh.convex = true;
                mesh.enabled = true;
            }
            if (go.GetComponentInChildren<Collider>(true) == null) go.AddComponent<SphereCollider>().radius = 0.05f;

            var body = go.AddComponent<Rigidbody>();
            body.mass = 0.4f;
            body.linearDamping = 0.1f;
            body.angularDamping = 0.5f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.isKinematic = true;
            go.AddComponent<Grenade>();

            if (AssetDatabase.IsValidFolder("Assets/Prefabs") == false) AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (AssetDatabase.IsValidFolder(PrefabFolder) == false) AssetDatabase.CreateFolder("Assets/Prefabs", Path.GetFileName(PrefabFolder));

            var asset = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath, out bool success);
            Object.DestroyImmediate(go);
            if (success == false)
            {
                EditorUtility.DisplayDialog("수류탄 설정", $"프리팹 저장 실패: {PrefabPath}", "확인");
                return null;
            }
            return asset.GetComponent<Grenade>();
        }
    }
}
