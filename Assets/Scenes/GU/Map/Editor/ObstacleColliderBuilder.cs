using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using Unity.AI.Navigation.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// 활성 씬의 Obstacles 하위 장애물마다 외곽 정점을 간소화한 볼록 콜라이더(SimplifiedCollider 자식)를 붙인다.
/// 메뉴: Tools/Map/Build Obstacle Colliders, Tools/Map/Clear Obstacle Colliders
/// - 장애물 = Obstacles 자식 중 '_' 로 시작하는 그룹(_Rocks, _Buildings)의 자식, 그 외에는 Obstacles 의 직접 자식
/// - 장애물의 모든 메시 정점(월드)에서 고르게 퍼진 방향마다 가장 바깥 정점(지지점)만 남겨 볼록 껍질을 만든다.
/// - 기존 콜라이더는 끄고(씬 오버라이드, Clear 로 복구), 생성 메시는 "씬 폴더/씬 이름/ObstacleColliders" 에 에셋으로 저장한다.
/// - 몬스터(NavMeshAgent)는 콜라이더가 아니라 NavMesh 를 따르므로 NavMeshModifier(Not Walkable)를 달고,
///   씬의 NavMeshSurface 를 "물리 콜라이더 기준 + 장애물 레이어 포함" 으로 바꿔 다시 굽는다.
/// </summary>
public static class ObstacleColliderBuilder
{
    private const string ObstaclesRootName = "Obstacles";
    private const string ColliderName = "SimplifiedCollider";
    private const string MeshFolderName = "ObstacleColliders";

    private const int SupportDirections = 64;  // 간소화 정도 = 껍질 정점 최대 개수 (PhysX 볼록 콜라이더 한도 255면 이내)
    private const float MergeDistance = 0.15f; // 이보다 가까운 지지점은 하나로 합침 (m)
    private const float Skin = 0.05f;          // 원본 외곽보다 살짝 키워 메시가 콜라이더 밖으로 삐져나오지 않게 (m)
    private const float SliceStep = 1f;        // NavMesh 용 내부 단면 간격 (m). 에이전트 높이(2m)보다 작아야 장애물 속에 NavMesh 섬이 안 생긴다

    [MenuItem("Tools/Map/Build Obstacle Colliders")]
    private static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[ObstacleColliderBuilder] 플레이 모드에서는 실행할 수 없다");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
        {
            Debug.LogWarning("[ObstacleColliderBuilder] 씬을 먼저 저장해야 메시 에셋 경로를 정할 수 있다");
            return;
        }

        List<Transform> obstacles = FindObstacles(scene);
        if (obstacles.Count == 0)
        {
            Debug.LogWarning($"[ObstacleColliderBuilder] '{ObstaclesRootName}' 하위 장애물이 없다: {scene.path}");
            return;
        }

        Undo.SetCurrentGroupName("Build Obstacle Colliders");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (Transform obstacle in obstacles) RemoveGenerated(obstacle);
        string folder = RecreateMeshFolder(scene);

        int obstacleLayers = 0;
        for (int i = 0; i < obstacles.Count; i++)
        {
            Transform obstacle = obstacles[i];
            List<Vector3> points = CollectSupportPoints(obstacle, out int sourceVertexCount);
            List<int> hull = points.Count >= 4 ? BuildHull(points) : null;
            if (hull == null)
            {
                Debug.LogWarning($"[ObstacleColliderBuilder] 볼록 껍질을 만들 수 없어 건너뜀: {obstacle.name}", obstacle);
                continue;
            }

            Mesh mesh = CreateColliderMesh(obstacle, points, hull, out int hullVertexCount);
            mesh.name = $"{obstacle.name}_{ColliderName}";
            AssetDatabase.CreateAsset(mesh, $"{folder}/{i:00}_{SanitizeFileName(obstacle.name)}.asset");

            DisableOriginalColliders(obstacle);
            GameObject go = CreateColliderObject(obstacle, mesh);
            obstacleLayers |= 1 << go.layer;
            Debug.Log($"[ObstacleColliderBuilder] {obstacle.name}: 메시 정점 {sourceVertexCount} → 콜라이더 정점 {hullVertexCount}", go);
        }
        AssetDatabase.SaveAssets();

        RebakeNavMesh(scene, obstacleLayers);
        EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"[ObstacleColliderBuilder] 장애물 {obstacles.Count}개 처리 완료. NavMesh 베이크가 끝나면 씬을 저장할 것");
    }

    [MenuItem("Tools/Map/Clear Obstacle Colliders")]
    private static void Clear()
    {
        Scene scene = SceneManager.GetActiveScene();
        Undo.SetCurrentGroupName("Clear Obstacle Colliders");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (Transform obstacle in FindObstacles(scene))
        {
            RemoveGenerated(obstacle);
            RestoreOriginalColliders(obstacle);
        }

        string folder = GetMeshFolder(scene);
        if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);

        EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log("[ObstacleColliderBuilder] 간소화 콜라이더 제거 완료. NavMeshSurface 설정은 그대로이므로 필요하면 다시 Bake 할 것");
    }

    private static List<Transform> FindObstacles(Scene scene)
    {
        var result = new List<Transform>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>())
            {
                if (t.name != ObstaclesRootName) continue;
                foreach (Transform child in t)
                {
                    if (child.gameObject.activeInHierarchy == false) continue;
                    if (child.name.StartsWith("_") == false)
                    {
                        result.Add(child);
                        continue;
                    }
                    foreach (Transform obstacle in child)
                        if (obstacle.gameObject.activeInHierarchy) result.Add(obstacle);
                }
            }
        }
        return result;
    }

    private static string GetMeshFolder(Scene scene)
    {
        string sceneDir = Path.GetDirectoryName(scene.path).Replace('\\', '/');
        return $"{sceneDir}/{scene.name}/{MeshFolderName}";
    }

    /// <summary>이전 실행에서 만든 메시 에셋을 지우고 빈 폴더를 다시 만든다</summary>
    private static string RecreateMeshFolder(Scene scene)
    {
        string folder = GetMeshFolder(scene);
        if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);

        string sceneDir = Path.GetDirectoryName(scene.path).Replace('\\', '/');
        string sceneFolder = $"{sceneDir}/{scene.name}";
        if (AssetDatabase.IsValidFolder(sceneFolder) == false) AssetDatabase.CreateFolder(sceneDir, scene.name);
        AssetDatabase.CreateFolder(sceneFolder, MeshFolderName);
        return folder;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    /// <summary>
    /// 장애물 메시 정점(월드) 중 고르게 퍼진 방향마다 가장 바깥 정점만 골라, 가까운 점은 합치고 Skin 만큼 바깥으로 민다.
    /// 지지점은 모두 원본 볼록 껍질 위의 점이므로 결과 껍질은 원본 외곽을 따라가되 면 수만 줄어든다.
    /// </summary>
    private static List<Vector3> CollectSupportPoints(Transform obstacle, out int sourceVertexCount)
    {
        List<Vector3> dirs = SampleDirections(SupportDirections);
        var best = new Vector3[dirs.Count];
        var bestDot = new float[dirs.Count];
        for (int d = 0; d < dirs.Count; d++) bestDot[d] = float.NegativeInfinity;

        sourceVertexCount = 0;
        foreach (MeshFilter filter in obstacle.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            if (filter.TryGetComponent(out MeshRenderer renderer) == false || renderer.enabled == false) continue;

            Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
            Vector3[] vertices = filter.sharedMesh.vertices;
            sourceVertexCount += vertices.Length;
            foreach (Vector3 v in vertices)
            {
                Vector3 w = toWorld.MultiplyPoint3x4(v);
                for (int d = 0; d < dirs.Count; d++)
                {
                    float dot = Vector3.Dot(w, dirs[d]);
                    if (dot <= bestDot[d]) continue;
                    bestDot[d] = dot;
                    best[d] = w;
                }
            }
        }

        var points = new List<Vector3>();
        if (sourceVertexCount == 0) return points;

        foreach (Vector3 p in best)
        {
            bool merged = false;
            foreach (Vector3 q in points)
            {
                if ((p - q).sqrMagnitude >= MergeDistance * MergeDistance) continue;
                merged = true;
                break;
            }
            if (merged == false) points.Add(p);
        }

        Vector3 center = Vector3.zero;
        foreach (Vector3 p in points) center += p;
        center /= points.Count;
        for (int i = 0; i < points.Count; i++)
            points[i] += (points[i] - center).normalized * Skin;
        return points;
    }

    /// <summary>축 6방향 + 피보나치 구면 n방향 (단위 벡터)</summary>
    private static List<Vector3> SampleDirections(int n)
    {
        var dirs = new List<Vector3> { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int i = 0; i < n; i++)
        {
            float y = 1f - 2f * (i + 0.5f) / n;
            float r = Mathf.Sqrt(1f - y * y);
            float a = golden * i;
            dirs.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
        }
        return dirs;
    }

    /// <summary>
    /// 점들의 볼록 껍질 삼각형 인덱스(3개씩, Cross(b-a, c-a) 가 바깥 방향). 점이 수십 개라는 전제의 단순 증분 구현.
    /// 점들이 한 평면 위에 있으면 null.
    /// </summary>
    private static List<int> BuildHull(List<Vector3> p)
    {
        // 초기 사면체: 서로 먼 두 점 → 그 선에서 가장 먼 점 → 그 평면에서 가장 먼 점
        int a = 0;
        int b = Farthest(p, i => (p[i] - p[a]).sqrMagnitude);
        a = Farthest(p, i => (p[i] - p[b]).sqrMagnitude);
        Vector3 ab = (p[b] - p[a]).normalized;
        int c = Farthest(p, i => Vector3.Cross(p[i] - p[a], ab).sqrMagnitude);
        Vector3 normal = Vector3.Cross(p[b] - p[a], p[c] - p[a]).normalized;
        int d = Farthest(p, i => Mathf.Abs(Vector3.Dot(p[i] - p[a], normal)));

        float size = (p[b] - p[a]).magnitude;
        float eps = size * 1e-5f;
        if (size < 1e-4f || Mathf.Abs(Vector3.Dot(p[d] - p[a], normal)) < size * 1e-3f) return null;

        var faces = new List<(int a, int b, int c)>();
        AddOutward(faces, p, a, b, c, d);
        AddOutward(faces, p, a, b, d, c);
        AddOutward(faces, p, a, c, d, b);
        AddOutward(faces, p, b, c, d, a);

        var edges = new List<(int, int)>();
        var edgeSet = new HashSet<(int, int)>();
        for (int i = 0; i < p.Count; i++)
        {
            if (i == a || i == b || i == c || i == d) continue;

            // i 에서 보이는 면을 지우고, 지운 영역의 경계(지평선) 모서리와 i 를 이어 새 면을 만든다
            edges.Clear();
            for (int f = faces.Count - 1; f >= 0; f--)
            {
                var face = faces[f];
                if (SignedDistance(p, face, p[i]) <= eps) continue;
                edges.Add((face.a, face.b));
                edges.Add((face.b, face.c));
                edges.Add((face.c, face.a));
                faces.RemoveAt(f);
            }
            edgeSet.Clear();
            edgeSet.UnionWith(edges);
            foreach ((int u, int v) in edges)
                if (edgeSet.Contains((v, u)) == false) faces.Add((u, v, i));
        }

        var triangles = new List<int>(faces.Count * 3);
        foreach (var face in faces)
        {
            triangles.Add(face.a);
            triangles.Add(face.b);
            triangles.Add(face.c);
        }
        return triangles;
    }

    private static int Farthest(List<Vector3> p, System.Func<int, float> score)
    {
        int best = 0;
        for (int i = 1; i < p.Count; i++)
            if (score(i) > score(best)) best = i;
        return best;
    }

    private static float SignedDistance(List<Vector3> p, (int a, int b, int c) face, Vector3 x)
    {
        Vector3 n = Vector3.Cross(p[face.b] - p[face.a], p[face.c] - p[face.a]).normalized;
        return Vector3.Dot(n, x - p[face.a]);
    }

    /// <summary>inside 점이 면 뒤쪽에 오도록 감김 방향을 맞춰 추가</summary>
    private static void AddOutward(List<(int a, int b, int c)> faces, List<Vector3> p, int a, int b, int c, int inside)
    {
        if (SignedDistance(p, (a, b, c), p[inside]) > 0f) faces.Add((a, c, b));
        else faces.Add((a, b, c));
    }

    /// <summary>
    /// 볼록 껍질 + 내부 수평 단면을 장애물 로컬 공간 메시로 만든다.
    /// 물리(볼록 콜라이더)는 정점의 볼록 껍질만 쓰므로 내부 단면은 무시되지만, NavMesh 는 삼각형을 그대로 복셀화하므로
    /// 단면이 없으면 땅에 박힌 장애물 속 지면이 걸을 수 있는 섬으로 남는다.
    /// </summary>
    private static Mesh CreateColliderMesh(Transform obstacle, List<Vector3> points, List<int> hull, out int hullVertexCount)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var remap = new Dictionary<int, int>();
        foreach (int index in hull)
        {
            if (remap.TryGetValue(index, out int mapped) == false)
            {
                mapped = vertices.Count;
                remap.Add(index, mapped);
                vertices.Add(points[index]);
            }
            triangles.Add(mapped);
        }
        hullVertexCount = vertices.Count;

        var hullEdges = new HashSet<(int, int)>();
        for (int t = 0; t < triangles.Count; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int u = triangles[t + k], v = triangles[t + (k + 1) % 3];
                hullEdges.Add(u < v ? (u, v) : (v, u));
            }
        }

        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        for (int i = 0; i < hullVertexCount; i++)
        {
            minY = Mathf.Min(minY, vertices[i].y);
            maxY = Mathf.Max(maxY, vertices[i].y);
        }
        for (float h = minY + SliceStep * 0.5f; h < maxY; h += SliceStep)
            AddSlice(vertices, triangles, hullEdges, h);

        Matrix4x4 toLocal = obstacle.worldToLocalMatrix;
        for (int i = 0; i < vertices.Count; i++) vertices[i] = toLocal.MultiplyPoint3x4(vertices[i]);

        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>높이 h 의 수평 평면으로 자른 껍질 단면(볼록 다각형)을 위를 향하는 부채꼴 삼각형으로 추가</summary>
    private static void AddSlice(List<Vector3> vertices, List<int> triangles, HashSet<(int, int)> hullEdges, float h)
    {
        var ring = new List<Vector3>();
        foreach ((int u, int v) in hullEdges)
        {
            Vector3 p = vertices[u], q = vertices[v];
            if ((p.y - h) * (q.y - h) >= 0f) continue;
            ring.Add(Vector3.Lerp(p, q, (h - p.y) / (q.y - p.y)));
        }
        if (ring.Count < 3) return;

        Vector3 center = Vector3.zero;
        foreach (Vector3 r in ring) center += r;
        center /= ring.Count;
        ring.Sort((x, y) => Mathf.Atan2(x.z - center.z, x.x - center.x).CompareTo(Mathf.Atan2(y.z - center.z, y.x - center.x)));
        // 껍질 모서리 위의 점이면 볼록 콜라이더 쿠킹이 정점으로 남길 수 있으므로 살짝 안쪽으로 넣는다
        for (int i = 0; i < ring.Count; i++) ring[i] = Vector3.MoveTowards(ring[i], center, Skin);

        int start = vertices.Count;
        vertices.AddRange(ring);
        for (int i = 1; i < ring.Count - 1; i++)
        {
            int a = start, b = start + i, c = start + i + 1;
            if (Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).y < 0f) (b, c) = (c, b);
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }
    }

    /// <summary>장애물 자식으로 볼록 MeshCollider + NavMeshModifier(Not Walkable) 오브젝트를 만든다</summary>
    private static GameObject CreateColliderObject(Transform obstacle, Mesh mesh)
    {
        var go = new GameObject(ColliderName);
        Undo.RegisterCreatedObjectUndo(go, "Build Obstacle Colliders");
        go.transform.SetParent(obstacle, false);

        // 레이어는 장애물 메시 레이어(씬에서 Buildings / Trees 로 지정됨)를 따른다
        MeshRenderer renderer = obstacle.GetComponentInChildren<MeshRenderer>();
        go.layer = renderer != null ? renderer.gameObject.layer : obstacle.gameObject.layer;

        var col = go.AddComponent<MeshCollider>();
        col.sharedMesh = mesh;
        col.convex = true;

        var modifier = go.AddComponent<NavMeshModifier>();
        modifier.overrideArea = true;
        modifier.area = NavMesh.GetAreaFromName("Not Walkable");
        return go;
    }

    private static void RemoveGenerated(Transform obstacle)
    {
        for (int i = obstacle.childCount - 1; i >= 0; i--)
        {
            Transform child = obstacle.GetChild(i);
            if (child.name == ColliderName) Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    /// <summary>원본 콜라이더(트리거 제외)를 씬 오버라이드로 끈다. 프리펩 원본은 건드리지 않는다</summary>
    private static void DisableOriginalColliders(Transform obstacle)
    {
        foreach (Collider col in obstacle.GetComponentsInChildren<Collider>(true))
        {
            if (col.isTrigger || col.enabled == false || col.name == ColliderName) continue;
            Undo.RecordObject(col, "Build Obstacle Colliders");
            col.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(col);
        }
    }

    /// <summary>DisableOriginalColliders 가 만든 m_Enabled 오버라이드를 되돌린다</summary>
    private static void RestoreOriginalColliders(Transform obstacle)
    {
        foreach (Collider col in obstacle.GetComponentsInChildren<Collider>(true))
        {
            if (col.enabled || PrefabUtility.IsPartOfPrefabInstance(col) == false) continue;
            SerializedProperty enabled = new SerializedObject(col).FindProperty("m_Enabled");
            if (enabled != null && enabled.prefabOverride)
                PrefabUtility.RevertPropertyOverride(enabled, InteractionMode.UserAction);
        }
    }

    /// <summary>
    /// 씬의 NavMeshSurface 가 장애물 콜라이더를 수집하도록 바꾸고 다시 굽는다. (Inspector 의 Bake 버튼과 같은 비동기 베이크)
    /// 렌더 메시 기준이면 원본의 오목한 모양(창문·내부)으로 구워져 플레이어 충돌 경계와 어긋나므로 물리 콜라이더 기준으로 통일한다.
    /// </summary>
    private static void RebakeNavMesh(Scene scene, int obstacleLayers)
    {
        var surfaces = new List<NavMeshSurface>();
        foreach (GameObject root in scene.GetRootGameObjects())
            surfaces.AddRange(root.GetComponentsInChildren<NavMeshSurface>());
        if (surfaces.Count == 0)
        {
            Debug.LogWarning("[ObstacleColliderBuilder] NavMeshSurface 가 없어 NavMesh 를 굽지 않았다. 몬스터는 장애물을 통과할 수 있다");
            return;
        }

        foreach (NavMeshSurface surface in surfaces)
        {
            Undo.RecordObject(surface, "Build Obstacle Colliders");
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = surface.layerMask | obstacleLayers;
            PrefabUtility.RecordPrefabInstancePropertyModifications(surface);
        }
        NavMeshAssetManager.instance.StartBakingSurfaces(surfaces.ToArray());
    }
}
