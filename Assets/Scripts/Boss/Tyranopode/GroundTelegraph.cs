using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 바닥 공격 예고 (원·부채꼴·직사각형). 옅은 바탕 위에 진한 채움이 duration 동안 바깥으로 차오른다 → 다 차면 맞는다.
    /// TyranopodeController.RentTelegraph 로 빌려 쓰고 Hide 로 돌려준다 (비활성 = 반납).
    /// 맞는 순간 Detonate: 같은 모양·크기가 밝게 번쩍이며 사라진다 → 실제 피해 범위가 그대로 보인다
    /// </summary>
    public sealed class GroundTelegraph : MonoBehaviour
    {
        private const int Segments = 40;
        private const float GroundOffset = 0.06f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly RaycastHit[] GroundHits = new RaycastHit[8];

        private enum Shape { Sector, Rect }

        private MeshRenderer backRenderer;
        private MeshRenderer fillRenderer;
        private Transform fill;
        private Mesh backMesh;
        private Mesh fillMesh;
        private MaterialPropertyBlock block;
        private Shape shape;
        private float startTime;
        private float duration;
        private float radius;
        private float angle;
        private float width;
        private float length;
        private bool detonating;
        private float detonateStart;
        private Color detonateColor;

        [Tooltip("맞는 순간 번쩍임이 사라지는 시간(초)")]
        [SerializeField] private float detonateTime = 0.45f;

        public static GroundTelegraph Create(Material material)
        {
            var go = new GameObject("Tyranopode Telegraph");
            var telegraph = go.AddComponent<GroundTelegraph>();
            telegraph.Build(material);
            go.SetActive(false);
            return telegraph;
        }

        private void Build(Material material)
        {
            block = new MaterialPropertyBlock();
            backMesh = new Mesh { name = "TelegraphBack" };
            fillMesh = new Mesh { name = "TelegraphFill" };
            backRenderer = AddLayer(transform, backMesh, material);
            fill = new GameObject("Fill").transform;
            fill.SetParent(transform, false);
            fill.localPosition = Vector3.up * 0.01f;
            fillRenderer = AddLayer(fill, fillMesh, material);
        }

        private static MeshRenderer AddLayer(Transform parent, Mesh mesh, Material material)
        {
            var filter = parent.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = parent.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        /// <summary>원 (center 중심 radius)</summary>
        public void ShowCircle(Vector3 center, float radius, float time, Color color)
            => ShowSector(center, Vector3.forward, radius, 360f, time, color);

        /// <summary>부채꼴 (center 에서 forward 방향, 전체 각도 angle)</summary>
        public void ShowSector(Vector3 center, Vector3 forward, float radius, float angle, float time, Color color)
        {
            shape = Shape.Sector;
            this.radius = radius;
            this.angle = angle;
            BuildSector(backMesh, radius, angle);
            BuildSector(fillMesh, radius, angle);
            Place(center, forward);
            Begin(time, color);
        }

        /// <summary>직사각형 (start 에서 forward 로 length, 폭 width)</summary>
        public void ShowRect(Vector3 start, Vector3 forward, float width, float length, float time, Color color)
        {
            shape = Shape.Rect;
            this.width = width;
            this.length = length;
            BuildRect(backMesh, width, length);
            BuildRect(fillMesh, width, length);
            Place(start, forward);
            Begin(time, color);
        }

        /// <summary>보스를 따라 위치·방향만 옮긴다 (예고 중 보스가 돌 때)</summary>
        public void Place(Vector3 origin, Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            origin.y = GroundHeight(origin) + GroundOffset;
            transform.SetPositionAndRotation(origin, Quaternion.LookRotation(forward));
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>맞는 순간: 같은 모양이 color 로 번쩍였다가 사라지고 스스로 반납된다</summary>
        public void Detonate(Color color)
        {
            detonating = true;
            detonateStart = Time.time;
            detonateColor = color;
            fill.localScale = Vector3.one;
            backRenderer.enabled = false;
        }

        /// <summary>모양 안의 무작위 지점 (먼지·파편을 범위 전체에 뿌릴 때)</summary>
        public Vector3 RandomPointInside()
        {
            Vector3 local;
            if (shape == Shape.Rect)
            {
                local = new Vector3(Random.Range(-width * 0.5f, width * 0.5f), 0f, Random.Range(0f, length));
            }
            else
            {
                float a = Random.Range(-angle * 0.5f, angle * 0.5f) * Mathf.Deg2Rad;
                float r = radius * Mathf.Sqrt(Random.value);
                local = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r;
            }
            return transform.TransformPoint(local);
        }

        /// <summary>모양 넓이 (m²)</summary>
        public float Area => shape == Shape.Rect ? width * length : Mathf.PI * radius * radius * angle / 360f;

        private void Begin(float time, Color color)
        {
            detonating = false;
            backRenderer.enabled = true;
            startTime = Time.time;
            duration = Mathf.Max(0.01f, time);
            SetColor(backRenderer, new Color(color.r, color.g, color.b, color.a * 0.35f));
            SetColor(fillRenderer, color);
            gameObject.SetActive(true);
            UpdateFill();
        }

        private void Update()
        {
            if (detonating == false)
            {
                UpdateFill();
                return;
            }
            float t = (Time.time - detonateStart) / detonateTime;
            if (t >= 1f)
            {
                detonating = false;
                Hide();
                return;
            }
            Color color = detonateColor;
            color.a *= 1f - t * t;
            SetColor(fillRenderer, color);
        }

        private void UpdateFill()
        {
            float t = Mathf.Clamp01((Time.time - startTime) / duration);
            fill.localScale = shape == Shape.Sector ? new Vector3(t, 1f, t) : new Vector3(1f, 1f, t);
        }

        private void SetColor(Renderer target, Color color)
        {
            target.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            target.SetPropertyBlock(block);
        }

        private static float GroundHeight(Vector3 point)
        {
            // 보스·플레이어 콜라이더 위에 그리지 않도록 가장 낮은 맞은 면을 쓴다
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 6f, Vector3.down, GroundHits, 20f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (GroundHits[i].collider.GetComponentInParent<Core.Contracts.IDamageable>() != null) continue;
                if (GroundHits[i].point.y < best) best = GroundHits[i].point.y;
            }
            return best < float.MaxValue ? best : point.y;
        }

        private static void BuildSector(Mesh mesh, float radius, float angle)
        {
            int segments = Mathf.Max(3, Mathf.CeilToInt(Segments * angle / 360f));
            var vertices = new Vector3[segments + 2];
            var triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            float start = -angle * 0.5f;
            for (int i = 0; i <= segments; i++)
            {
                float a = (start + angle * i / segments) * Mathf.Deg2Rad;
                vertices[i + 1] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius;
            }
            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
            Apply(mesh, vertices, triangles);
        }

        private static void BuildRect(Mesh mesh, float width, float length)
        {
            float w = width * 0.5f;
            var vertices = new[]
            {
                new Vector3(-w, 0f, 0f), new Vector3(-w, 0f, length),
                new Vector3(w, 0f, length), new Vector3(w, 0f, 0f),
            };
            Apply(mesh, vertices, new[] { 0, 1, 2, 0, 2, 3 });
        }

        private static void Apply(Mesh mesh, Vector3[] vertices, int[] triangles)
        {
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            Destroy(backMesh);
            Destroy(fillMesh);
        }
    }
}
