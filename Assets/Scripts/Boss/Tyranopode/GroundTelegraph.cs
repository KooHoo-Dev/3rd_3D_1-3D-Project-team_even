using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 바닥 공격 예고 (원·부채꼴·직사각형). 옅은 바탕 위에 진한 채움이 duration 동안 바깥으로 차오른다 → 다 차면 맞는다.
    /// TyranopodeController.RentTelegraph 로 빌려 쓰고 Hide 로 돌려준다 (비활성 = 반납).
    /// 맞는 순간 Detonate: 같은 모양·크기가 밝게 번쩍이며 사라진다 → 실제 피해 범위가 그대로 보인다
    /// 경사진 지형을 따라 휜다: 약 1m 간격 그물 메시의 꼭짓점마다 바닥 높이(TyranopodeGround)를 재서 표면 위 Lift 에 놓는다.
    /// 채움은 바탕을 만들 때 잰 높이를 보간해 매 프레임 다시 놓는다 (다시 재지 않는다)
    /// </summary>
    public sealed class GroundTelegraph : MonoBehaviour
    {
        private const int Segments = 40;
        private const float Spacing = 1f;          // 그물 간격(m)
        private const int MaxRings = 40;
        private const int MaxSpokes = 96;
        private const int MaxRows = 80;
        private const float Lift = 0.1f;           // 바닥 위로 띄우는 높이(m)
        private const float FollowDistance = 0.3f; // 보스를 따라갈 때 이만큼 움직여야 다시 잰다
        private const float FollowAngle = 2f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

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

        // 그물: 부채꼴 = 중심 + rings 고리 × (spokes + 1) 점, 직사각형 = (rows + 1) 줄 × (columns + 1) 점
        private int rings;
        private int spokes;
        private int rows;
        private int columns;
        private Vector3[] flat;      // 높이 없는 바탕 꼭짓점 (로컬)
        private float[] heights;     // 꼭짓점별 로컬 높이 (바닥 + Lift)
        private Vector3[] backVertices;
        private Vector3[] fillVertices;
        private float fillProgress = -1f;

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
            backMesh.MarkDynamic();
            fillMesh.MarkDynamic();
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
            BuildSector();
            Move(center, forward);
            Begin(time, color);
        }

        /// <summary>직사각형 (start 에서 forward 로 length, 폭 width)</summary>
        public void ShowRect(Vector3 start, Vector3 forward, float width, float length, float time, Color color)
        {
            shape = Shape.Rect;
            this.width = width;
            this.length = length;
            BuildRect();
            Move(start, forward);
            Begin(time, color);
        }

        /// <summary>
        /// 보스를 따라 위치·방향만 옮긴다 (예고 중 보스가 돌 때). 매 프레임 불러도 된다:
        /// FollowDistance·FollowAngle 보다 적게 움직였으면 그대로 두고, 넘으면 옮겨서 바닥 높이를 다시 잰다
        /// </summary>
        public void Place(Vector3 origin, Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            Vector3 moved = origin - transform.position;
            moved.y = 0f;
            if (moved.sqrMagnitude < FollowDistance * FollowDistance && Vector3.Angle(transform.forward, forward) < FollowAngle) return;
            Move(origin, forward);
        }

        private void Move(Vector3 origin, Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            origin.y = TyranopodeGround.Height(origin, origin.y);
            transform.SetPositionAndRotation(origin, Quaternion.LookRotation(forward));
            Drape();
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>맞는 순간: 같은 모양이 color 로 번쩍였다가 사라지고 스스로 반납된다</summary>
        public void Detonate(Color color)
        {
            detonating = true;
            detonateStart = Time.time;
            detonateColor = color;
            SetFill(1f);
            backRenderer.enabled = false;
        }

        /// <summary>모양 안의 무작위 지점 (먼지·파편을 범위 전체에 뿌릴 때). 높이는 그 자리 바닥</summary>
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
            Vector3 point = transform.TransformPoint(local);
            point.y = TyranopodeGround.Height(point, point.y);
            return point;
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

        private void UpdateFill() => SetFill(Mathf.Clamp01((Time.time - startTime) / duration));

        private void SetColor(Renderer target, Color color)
        {
            target.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            target.SetPropertyBlock(block);
        }

        // ───────────────────────── 그물 메시 ─────────────────────────

        private int SectorIndex(int ring, int spoke) => ring == 0 ? 0 : 1 + (ring - 1) * (spokes + 1) + spoke;

        private int RectIndex(int row, int column) => row * (columns + 1) + column;

        private void BuildSector()
        {
            // 큰 원은 바깥 둘레가 너무 성기지 않게 (호 길이 약 2m 마다 바퀴살)
            float arc = radius * angle * Mathf.Deg2Rad / (Spacing * 2f);
            spokes = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(Segments * angle / 360f, arc)), 3, MaxSpokes);
            rings = Mathf.Clamp(Mathf.CeilToInt(radius / Spacing), 2, MaxRings);
            Allocate(1 + rings * (spokes + 1));

            flat[0] = Vector3.zero;
            float start = -angle * 0.5f;
            for (int ring = 1; ring <= rings; ring++)
            {
                float r = radius * ring / rings;
                for (int spoke = 0; spoke <= spokes; spoke++)
                {
                    float a = (start + angle * spoke / spokes) * Mathf.Deg2Rad;
                    flat[SectorIndex(ring, spoke)] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r;
                }
            }

            var triangles = new int[(spokes + (rings - 1) * spokes * 2) * 3];
            int n = 0;
            for (int spoke = 0; spoke < spokes; spoke++)
            {
                triangles[n++] = 0;
                triangles[n++] = SectorIndex(1, spoke);
                triangles[n++] = SectorIndex(1, spoke + 1);
            }
            for (int ring = 2; ring <= rings; ring++)
            for (int spoke = 0; spoke < spokes; spoke++)
                n = Quad(triangles, n, SectorIndex(ring - 1, spoke), SectorIndex(ring, spoke), SectorIndex(ring, spoke + 1), SectorIndex(ring - 1, spoke + 1));
            SetTopology(triangles);
        }

        private void BuildRect()
        {
            columns = Mathf.Clamp(Mathf.CeilToInt(width / Spacing), 1, MaxRings);
            rows = Mathf.Clamp(Mathf.CeilToInt(length / Spacing), 1, MaxRows);
            Allocate((rows + 1) * (columns + 1));

            for (int row = 0; row <= rows; row++)
            for (int column = 0; column <= columns; column++)
                flat[RectIndex(row, column)] = new Vector3(width * ((float)column / columns - 0.5f), 0f, length * row / rows);

            var triangles = new int[rows * columns * 6];
            int n = 0;
            for (int row = 1; row <= rows; row++)
            for (int column = 0; column < columns; column++)
                n = Quad(triangles, n, RectIndex(row - 1, column), RectIndex(row, column), RectIndex(row, column + 1), RectIndex(row - 1, column + 1));
            SetTopology(triangles);
        }

        private static int Quad(int[] triangles, int n, int a, int b, int c, int d)
        {
            triangles[n++] = a;
            triangles[n++] = b;
            triangles[n++] = c;
            triangles[n++] = a;
            triangles[n++] = c;
            triangles[n++] = d;
            return n;
        }

        private void Allocate(int count)
        {
            if (flat != null && flat.Length == count) return;
            flat = new Vector3[count];
            heights = new float[count];
            backVertices = new Vector3[count];
            fillVertices = new Vector3[count];
        }

        private void SetTopology(int[] triangles)
        {
            // 꼭짓점 수가 바뀌면 삼각형부터 지우고 넣는다
            backMesh.Clear();
            fillMesh.Clear();
            backMesh.vertices = flat;
            fillMesh.vertices = flat;
            backMesh.triangles = triangles;
            fillMesh.triangles = triangles;
            fillProgress = -1f;
        }

        /// <summary>꼭짓점마다 바닥 높이를 재서 바탕을 지형에 덮는다</summary>
        private void Drape()
        {
            float baseY = transform.position.y;
            for (int i = 0; i < flat.Length; i++)
            {
                Vector3 world = transform.TransformPoint(flat[i]);
                heights[i] = TyranopodeGround.Height(world, baseY) - baseY + Lift;
                backVertices[i] = new Vector3(flat[i].x, heights[i], flat[i].z);
            }
            backMesh.vertices = backVertices;
            backMesh.RecalculateBounds();
            fillProgress = -1f;
        }

        /// <summary>채움 진행도 t: 반지름(직사각형은 길이)을 t 배로 줄인 자리에, 바탕 높이를 보간해 놓는다</summary>
        private void SetFill(float t)
        {
            if (Mathf.Approximately(t, fillProgress)) return;
            fillProgress = t;

            if (shape == Shape.Sector)
            {
                fillVertices[0] = backVertices[0];
                for (int ring = 1; ring <= rings; ring++)
                for (int spoke = 0; spoke <= spokes; spoke++)
                {
                    int i = SectorIndex(ring, spoke);
                    float along = ring * t; // 바퀴살을 따라 몇 번째 고리 자리인가
                    int inner = Mathf.Min(Mathf.FloorToInt(along), rings - 1);
                    float h = Mathf.Lerp(heights[SectorIndex(inner, spoke)], heights[SectorIndex(inner + 1, spoke)], along - inner);
                    fillVertices[i] = new Vector3(flat[i].x * t, h, flat[i].z * t);
                }
            }
            else
            {
                for (int row = 0; row <= rows; row++)
                for (int column = 0; column <= columns; column++)
                {
                    int i = RectIndex(row, column);
                    float along = row * t;
                    int near = Mathf.Min(Mathf.FloorToInt(along), rows - 1);
                    float h = Mathf.Lerp(heights[RectIndex(near, column)], heights[RectIndex(near + 1, column)], along - near);
                    fillVertices[i] = new Vector3(flat[i].x, h, flat[i].z * t);
                }
            }
            fillMesh.vertices = fillVertices;
            fillMesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            // EditMode 테스트에서도 지운다
            if (Application.isPlaying)
            {
                Destroy(backMesh);
                Destroy(fillMesh);
                return;
            }
            DestroyImmediate(backMesh);
            DestroyImmediate(fillMesh);
        }
    }
}
