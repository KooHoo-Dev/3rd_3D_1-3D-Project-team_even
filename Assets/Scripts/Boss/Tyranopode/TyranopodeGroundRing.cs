using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 바닥에 퍼지는 충격파 링 (충격파·포자 파동·궤도 타격·등장 혈관). 평평한 파티클 대신 지형을 따라 휘는 띠 메시라
    /// 경사진 곳에서도 땅에 묻히지 않는다.
    ///  - 수명의 growShare 안에 radius 까지 퍼지고(1 = 수명 내내 일정하게) 그 크기에서 사라진다 → 사라지는 링 = 피해 범위 끝
    ///  - 크기는 부모 VFX 의 크기(lossyScale)를 따른다. TimeScale 로 재생 속도를 바꾼다
    /// 링 텍스처(T_Ring)는 반지름 0.92 에 띠가 있어 메시는 0.82~1.0 구간만 만든다. TyranopodeVfxBuilder 가 만든다
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class TyranopodeGroundRing : MonoBehaviour
    {
        private const int Segments = 96;
        private const int Bands = 3;
        private const float Inner = 0.82f;
        private const float Lift = 0.15f;
        private const float StartSize = 0.05f;

        [SerializeField, Min(0.1f)] private float radius = 10f;
        [SerializeField, Min(0.01f)] private float life = 0.7f;
        [SerializeField, Range(0.05f, 1f)] private float growShare = 0.35f;
        [SerializeField] private Color color = Color.white;

        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private Vector3[] vertices;
        private Color[] colors;
        private float age;

        /// <summary>재생 속도 배율 (등장 혈관을 흡수 시간에 맞출 때)</summary>
        public float TimeScale { get; set; } = 1f;

        /// <summary>빌더용: 프리팹에 저장할 값</summary>
        public void Configure(float ringRadius, float ringLife, float ringGrowShare, Color ringColor)
        {
            radius = ringRadius;
            life = ringLife;
            growShare = ringGrowShare;
            color = ringColor;
        }

        private void Start()
        {
            int count = (Bands + 1) * (Segments + 1);
            vertices = new Vector3[count];
            colors = new Color[count];
            var uvs = new Vector2[count];
            var triangles = new int[Bands * Segments * 6];
            int n = 0;
            for (int band = 0; band <= Bands; band++)
            for (int segment = 0; segment <= Segments; segment++)
            {
                // 텍스처는 중심에서 반지름 비율로 링을 그린다 → 같은 비율 자리에 UV
                float r = Mathf.Lerp(Inner, 1f, (float)band / Bands);
                float a = segment * Mathf.PI * 2f / Segments;
                uvs[Index(band, segment)] = new Vector2(0.5f + 0.5f * r * Mathf.Sin(a), 0.5f + 0.5f * r * Mathf.Cos(a));
                if (band == 0 || segment == Segments) continue;
                int a0 = Index(band - 1, segment), a1 = Index(band - 1, segment + 1), b0 = Index(band, segment), b1 = Index(band, segment + 1);
                triangles[n++] = a0; triangles[n++] = b0; triangles[n++] = b1;
                triangles[n++] = a0; triangles[n++] = b1; triangles[n++] = a1;
            }

            meshRenderer = GetComponent<MeshRenderer>();
            mesh = new Mesh { name = "GroundRing" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = triangles;
            GetComponent<MeshFilter>().sharedMesh = mesh;
            UpdateRing();
        }

        private static int Index(int band, int segment) => band * (Segments + 1) + segment;

        private void Update()
        {
            age += Time.deltaTime * TimeScale;
            UpdateRing();
        }

        private void UpdateRing()
        {
            float k = age / life;
            if (k >= 1f)
            {
                meshRenderer.enabled = false;
                enabled = false;
                return;
            }

            // growShare 1 = 수명 내내 일정한 속도로, 아니면 빠르게 퍼지다 끝에서 느려진다
            float g = Mathf.Clamp01(k / growShare);
            float grow = Mathf.Lerp(StartSize, 1f, growShare >= 1f ? g : 1f - (1f - g) * (1f - g));
            float worldRadius = radius * transform.lossyScale.x * grow;
            Vector3 center = transform.position;
            for (int band = 0; band <= Bands; band++)
            {
                float r = worldRadius * Mathf.Lerp(Inner, 1f, (float)band / Bands);
                for (int segment = 0; segment <= Segments; segment++)
                {
                    float a = segment * Mathf.PI * 2f / Segments;
                    Vector3 world = center + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r;
                    world.y = TyranopodeGround.Height(world, center.y) + Lift;
                    vertices[Index(band, segment)] = transform.InverseTransformPoint(world);
                }
            }

            // 처음 30% 는 그대로, 그 뒤 사라진다
            Color c = color;
            c.a *= k < 0.3f ? 1f : 1f - (k - 0.3f) / 0.7f;
            for (int i = 0; i < colors.Length; i++) colors[i] = c;

            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
