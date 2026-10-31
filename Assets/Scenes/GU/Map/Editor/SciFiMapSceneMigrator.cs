using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 테스트 씬들의 옛 맵(Creepy_Cat 기반 Map 프리펩·Terrain·Base·Ground 평면)을 SciFiMap 프리펩으로 바꾼다.
/// 메뉴: Tools/Map/Replace Test Scene Maps With Sci-Fi Map  (배치 모드: -executeMethod SciFiMapSceneMigrator.MigrateAll)
/// - 옛 Map.prefab 인스턴스가 있던 씬: 같은 자리(위치·회전)에 SciFiMap을 둔다. SciFiMap 지형은 Map의 지형을 복사해 깎은 것이라 지면이 맞는다.
/// - 그 밖의 씬: 기준 오브젝트(플레이어, 없으면 원점)가 MapDesign 씬의 플레이어 자리에 오도록 SciFiMap을 옮기고,
///   그 지점 지면 높이를 기준 오브젝트 발 높이에 맞춘다. 씬의 다른 오브젝트는 그대로 두므로 기준 오브젝트와의 상대 배치가 유지된다.
/// - 씬 컴포넌트가 지울 맵 안의 Terrain을 참조하고 있었으면 SciFiMap의 Terrain으로 다시 연결한다. 나머지 끊기는 참조는 로그로 남긴다.
/// </summary>
public static class SciFiMapSceneMigrator
{
    private const string MapPrefabPath = "Assets/Scenes/GU/Map/SciFiMap.prefab";
    private const string OldMapPrefabPath = "Assets/Resources/Map/Map.prefab";
    private const string ReferenceScenePath = "Assets/Scenes/GU/MapDesign.unity";
    private const string Tag = "[SciFiMapSceneMigrator]";

    // 기준 오브젝트 후보 (앞에 있는 이름부터 찾는다)
    private static readonly string[] AnchorNames = { "Player", "TestPlayer", "Player_Sound", "Player (1)" };

    // 씬별로 걷어낼 옛 맵 오브젝트 (루트 이름 또는 계층 경로)
    private static readonly (string scene, string[] mapPaths)[] Targets =
    {
        ("Assets/Scenes/TestScenes/Monster Player Integration.unity", new[] { "Map" }),
        ("Assets/Scenes/GU/SupplyImprovement.unity", new[] { "Map" }),
        ("Assets/Scenes/GU/Map/TestMap_V2.unity", new[] { "Map" }),
        ("Assets/Scenes/GU/RewardAndUpgradeSystem.unity", new[] { "Map" }),
        ("Assets/Scenes/GU/Map/TestMap_V1.unity", new[] { "Base", "Terrain" }),
        ("Assets/Scenes/MS/Scenes/TestMap_V1.unity", new[] { "Base", "Terrain", "Navigation" }),
        ("Assets/Scenes/MS/Scenes/PlayerTest.unity", new[] { "Base", "Terrain" }),
        ("Assets/Scenes/CY/TestMap_V1.unity", new[] { "Terrain", "Navigation" }),
        ("Assets/Scenes/CY/Round Test.unity", new[] { "Ground" }),
        ("Assets/Scenes/GU/SoundSystemTest.unity", new[] { "Ground" }),
        ("Assets/Scenes/GU/SupplyPackTestscene.unity", new[] { "Ground" }),
        ("Assets/Scenes/HS/MonsterIntegrationLab/HS Monster Playground.unity", new[] { "Tester/Monster Movement Laboratory (Owned)/Terrain" }),
        ("Assets/Scenes/HS/MonsterSpawnPointFinderLab/Monster Spawn Point Finder Lab.unity", new[] { "Tester/Monster Movement Laboratory (Owned)/Terrain" }),
    };

    [MenuItem("Tools/Map/Replace Test Scene Maps With Sci-Fi Map")]
    public static void MigrateAll()
    {
        // 씬을 차례로 열어 바꾸므로, 열려 있는 씬의 저장 안 한 변경을 먼저 묻고 끝나면 원래 씬으로 돌아온다.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string previousScene = SceneManager.GetActiveScene().path;

        // MapDesign 씬에서 SciFiMap과 플레이어의 상대 위치를 읽는다.
        Scene reference = EditorSceneManager.OpenScene(ReferenceScenePath, OpenSceneMode.Single);
        GameObject refMap = FindRoot(reference, "SciFiMap");
        GameObject refPlayer = FindRoot(reference, "Player");
        if (refMap == null || refPlayer == null)
        {
            Debug.LogError($"{Tag} {ReferenceScenePath} 에서 SciFiMap/Player를 찾지 못했습니다.");
            return;
        }
        Quaternion mapRotation = refMap.transform.rotation;
        Vector3 playerOffset = refPlayer.transform.position - refMap.transform.position;

        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefabPath);
        foreach ((string scene, string[] mapPaths) in Targets)
            Migrate(scene, mapPaths, asset, mapRotation, playerOffset);

        Debug.Log($"{Tag} 완료");
        if (!string.IsNullOrEmpty(previousScene)) EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
    }

    private static void Migrate(string scenePath, string[] mapPaths, GameObject asset, Quaternion mapRotation, Vector3 playerOffset)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        if (FindRoot(scene, "SciFiMap") != null)
        {
            Debug.Log($"{Tag} {scenePath}: 이미 SciFiMap이 있어 건너뜀");
            return;
        }

        List<GameObject> oldMaps = new List<GameObject>();
        foreach (string path in mapPaths)
        {
            GameObject go = FindPath(scene, path);
            if (go != null) oldMaps.Add(go);
            else Debug.LogWarning($"{Tag} {scenePath}: '{path}' 를 찾지 못함");
        }

        GameObject map = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
        Terrain terrain = map.GetComponentInChildren<Terrain>();

        GameObject oldMapInstance = oldMaps.FirstOrDefault(go => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == OldMapPrefabPath);
        if (oldMapInstance != null)
        {
            map.transform.SetPositionAndRotation(oldMapInstance.transform.position, oldMapInstance.transform.rotation);
            Debug.Log($"{Tag} {scenePath}: 옛 Map 자리 {oldMapInstance.transform.position} 에 배치");
        }
        else
        {
            GameObject anchor = AnchorNames.Select(n => FindRoot(scene, n)).FirstOrDefault(go => go != null);
            Vector3 anchorPos = anchor != null ? anchor.transform.position : Vector3.zero;
            float footY = anchor != null ? FootHeight(anchor, oldMaps) : 0f;

            map.transform.SetPositionAndRotation(anchorPos - playerOffset, mapRotation);
            float ground = terrain.SampleHeight(anchorPos) + terrain.GetPosition().y;
            map.transform.position += Vector3.up * (footY - ground);
            Debug.Log($"{Tag} {scenePath}: 기준 '{(anchor != null ? anchor.name : "원점")}' {anchorPos} (발 높이 {footY:0.00}) → SciFiMap {map.transform.position}");
        }

        RewireReferences(scene, scenePath, oldMaps, map, terrain);

        foreach (GameObject go in oldMaps)
        {
            Debug.Log($"{Tag} {scenePath}: '{go.name}' 삭제");
            Object.DestroyImmediate(go);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>지울 맵 안의 오브젝트를 가리키는 씬 컴포넌트 참조를 찾아, Terrain 계열은 SciFiMap 것으로 바꾸고 나머지는 로그로 남긴다.</summary>
    private static void RewireReferences(Scene scene, string scenePath, List<GameObject> oldMaps, GameObject map, Terrain terrain)
    {
        HashSet<Object> removed = new HashSet<Object>();
        foreach (GameObject go in oldMaps)
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                removed.Add(t.gameObject);
                foreach (Component c in t.GetComponents<Component>())
                    if (c != null) removed.Add(c);
            }

        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Component c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null || removed.Contains(c) || c.transform.IsChildOf(map.transform)) continue;
                SerializedObject so = new SerializedObject(c);
                SerializedProperty it = so.GetIterator();
                bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    if (!removed.Contains(it.objectReferenceValue)) continue;

                    Object old = it.objectReferenceValue;
                    Object replacement = old switch
                    {
                        Terrain => (Object)terrain,
                        TerrainCollider => terrain.GetComponent<TerrainCollider>(),
                        _ => null,
                    };
                    if (replacement == null && old is GameObject oldGo && oldGo.GetComponent<Terrain>() != null) replacement = terrain.gameObject;
                    if (replacement == null && old is Transform oldTr && oldTr.GetComponent<Terrain>() != null) replacement = terrain.transform;

                    it.objectReferenceValue = replacement;
                    changed = true;
                    string from = $"{c.gameObject.name}.{c.GetType().Name}.{it.propertyPath}";
                    if (replacement != null) Debug.Log($"{Tag} {scenePath}: {from} → SciFiMap {replacement.GetType().Name}");
                    else Debug.LogWarning($"{Tag} {scenePath}: {from} 가 지운 맵의 '{old.name}' 을 가리켜 비움 (수동 확인 필요)");
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
    }

    /// <summary>기준 오브젝트의 발 높이. 콜라이더 → 렌더러 → 피벗 순으로 쓴다. 지울 맵 안의 것은 뺀다.</summary>
    private static float FootHeight(GameObject anchor, List<GameObject> oldMaps)
    {
        Physics.SyncTransforms();
        float? min = null;
        foreach (Collider c in anchor.GetComponentsInChildren<Collider>())
            if (c.enabled && !c.isTrigger && !oldMaps.Any(m => c.transform.IsChildOf(m.transform)))
                min = Mathf.Min(min ?? float.MaxValue, c.bounds.min.y);
        if (min == null)
            foreach (Renderer r in anchor.GetComponentsInChildren<Renderer>())
                min = Mathf.Min(min ?? float.MaxValue, r.bounds.min.y);
        return min ?? anchor.transform.position.y;
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
            if (go.name == name || go.name.StartsWith(name + " (Missing Prefab")) return go;
        return null;
    }

    private static GameObject FindPath(Scene scene, string path)
    {
        string[] parts = path.Split('/');
        GameObject root = FindRoot(scene, parts[0]);
        if (root == null || parts.Length == 1) return root;
        Transform child = root.transform.Find(string.Join("/", parts.Skip(1)));
        return child != null ? child.gameObject : null;
    }
}
