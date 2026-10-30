using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HybridShooter.EditorTools
{
    /// <summary>
    /// 마그마 투사체용 고해상도 아이코스피어 메시를 만들고 MagmaProjectile 프리팹의 Visual 에 연결한다.
    /// MagmaRock 셰이더가 버텍스를 밀어내 바위 모양을 만들기 때문에 기본 Sphere 보다 촘촘한 메시가 필요하다.
    /// 에디터가 스크립트를 다시 불러올 때 한 번 확인하고, 이미 연결돼 있으면 아무것도 하지 않는다.
    /// </summary>
    [InitializeOnLoad]
    internal static class MagmaRockMeshSetup
    {
        private const string MeshFolder = "Assets/Prefabs/Effects/Meshes";
        private const string MeshPath = MeshFolder + "/MagmaRockSphere.asset";
        private const string PrefabPath = "Assets/Resources/Projectile/MagmaProjectile.prefab";
        private const string VisualName = "Visual";
        private const int Subdivisions = 5; // 10242 정점, 20480 삼각형
        private const float Radius = 0.5f;

        static MagmaRockMeshSetup()
        {
            EditorApplication.delayCall += () => Run(false);
        }

        [MenuItem("Tools/HybridShooter/Rebuild Magma Rock Mesh")]
        private static void RebuildFromMenu()
        {
            Run(true);
        }

        private static void Run(bool forceRebuild)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (mesh == null)
            {
                if (!AssetDatabase.IsValidFolder(MeshFolder))
                {
                    AssetDatabase.CreateFolder("Assets/Prefabs/Effects", "Meshes");
                }

                mesh = new Mesh { name = "MagmaRockSphere" };
                BuildIcosphere(mesh);
                AssetDatabase.CreateAsset(mesh, MeshPath);
                AssetDatabase.SaveAssets();
            }
            else if (forceRebuild)
            {
                BuildIcosphere(mesh);
                EditorUtility.SetDirty(mesh);
                AssetDatabase.SaveAssets();
            }

            AssignToPrefab(mesh);
        }

        private static void AssignToPrefab(Mesh mesh)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                return;
            }

            Transform visual = prefab.transform.Find(VisualName);
            MeshFilter filter = visual != null ? visual.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == mesh)
            {
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                root.transform.Find(VisualName).GetComponent<MeshFilter>().sharedMesh = mesh;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[MagmaRockMeshSetup] {PrefabPath} 의 {VisualName} 메시를 {MeshPath} 로 교체했습니다.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void BuildIcosphere(Mesh mesh)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var vertices = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i] = vertices[i].normalized;
            }

            var triangles = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            for (int s = 0; s < Subdivisions; s++)
            {
                var midpointCache = new Dictionary<long, int>();
                var next = new List<int>(triangles.Count * 4);
                for (int i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];
                    int ab = Midpoint(a, b, vertices, midpointCache);
                    int bc = Midpoint(b, c, vertices, midpointCache);
                    int ca = Midpoint(c, a, vertices, midpointCache);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                triangles = next;
            }

            var normals = new Vector3[vertices.Count];
            var positions = new Vector3[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                normals[i] = vertices[i];
                positions[i] = vertices[i] * Radius;
            }

            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.triangles = triangles.ToArray();
            // 셰이더가 표면을 바깥으로 밀어내므로 컬링 범위를 넉넉하게 잡는다
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (Radius * 3f));
        }

        private static int Midpoint(int a, int b, List<Vector3> vertices, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int index))
            {
                return index;
            }

            index = vertices.Count;
            vertices.Add(((vertices[a] + vertices[b]) * 0.5f).normalized);
            cache[key] = index;
            return index;
        }
    }
}
