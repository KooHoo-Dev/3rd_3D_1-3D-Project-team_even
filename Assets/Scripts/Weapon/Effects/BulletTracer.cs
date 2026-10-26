using System.Collections.Generic;
using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 히트스캔 총알의 탄 궤적. 밝은 총알 머리가 꼬리를 끌며 총구에서 맞은 자리까지 날아가고 끝에 닿으면 사라진다.
    ///  - 판정은 이미 레이캐스트로 끝났고 이건 보이기만 한다 (피해와 무관)
    ///  - 탄착 이펙트는 Gun 이 넘겨주면 총알 머리가 그 자리를 지나는 순간 생성한다 (쏘자마자 터지지 않게)
    ///  - 연사가 빨라 매번 만들고 지우지 않도록 프리팹별로 재사용한다
    ///  - 선 두 점은 꼬리 → 머리 순서. 색 그라데이션(꼬리 투명 → 머리 밝음)은 프리팹의 LineRenderer 에서 정한다
    ///  - 총알 머리는 같은 재질로 짧고 굵은 선을 하나 더 그린다 (프리팹에 따로 만들 필요 없음)
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class BulletTracer : MonoBehaviour
    {
        [Tooltip("총알 속도 (m/s). 실제 탄속보다 느려야 날아가는 게 보인다")]
        [SerializeField] private float speed = 180f;
        [Tooltip("꼬리 길이 (m)")]
        [SerializeField] private float length = 4f;
        [Tooltip("총구와 맞은 자리가 이보다 가까우면 그리지 않는다 (탄착 이펙트는 바로 생성)")]
        [SerializeField] private float minDistance = 1.5f;

        [Header("총알 머리")]
        [Tooltip("밝은 머리 길이 (m). 0 이면 그리지 않는다")]
        [SerializeField] private float headLength = 0.35f;
        [Tooltip("머리 굵기 = 꼬리 굵기 × 이 값")]
        [SerializeField] private float headWidthScale = 2.5f;
        [SerializeField] private Color headColor = Color.white;

        [Header("탄착")]
        [Tooltip("생성한 탄착 이펙트를 지우는 시간 (초)")]
        [SerializeField] private float impactLifetime = 2f;

        private static readonly Dictionary<BulletTracer, Stack<BulletTracer>> pools = new Dictionary<BulletTracer, Stack<BulletTracer>>();

        private BulletTracer source;
        private LineRenderer line;
        private LineRenderer headLine;
        private Vector3 start;
        private Vector3 direction;
        private float distance;
        private float head;

        private GameObject impactPrefab;
        private readonly List<Pose> impacts = new List<Pose>();

        /// <summary>
        /// from(총구) 에서 to(맞은 자리) 까지 총알 하나. impacts 는 총알 머리가 지날 때 impactPrefab 을 생성할 자리.
        /// 너무 가까워 그리지 않으면 false (이때 탄착은 부른 쪽이 바로 생성한다)
        /// </summary>
        public static bool Fire(BulletTracer prefab, Vector3 from, Vector3 to, GameObject impactPrefab = null, List<Pose> impacts = null)
        {
            float distance = Vector3.Distance(from, to);
            if (distance < prefab.minDistance) return false;
            Take(prefab).Begin(from, to, distance, impactPrefab, impacts);
            return true;
        }

        private static BulletTracer Take(BulletTracer prefab)
        {
            if (pools.TryGetValue(prefab, out var pool))
            {
                // 씬이 바뀌면 쌓아 둔 사본이 지워져 있을 수 있다
                while (pool.Count > 0)
                {
                    var pooled = pool.Pop();
                    if (pooled != null) return pooled;
                }
            }
            var tracer = Instantiate(prefab);
            tracer.source = prefab;
            tracer.line = tracer.GetComponent<LineRenderer>();
            tracer.line.useWorldSpace = true;
            tracer.line.positionCount = 2;
            if (prefab.headLength > 0f) tracer.headLine = tracer.CreateHead();
            return tracer;
        }

        /// <summary>꼬리 선의 재질·정렬을 그대로 쓰는 짧고 굵은 선</summary>
        private LineRenderer CreateHead()
        {
            var go = new GameObject("Head");
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            var head = go.AddComponent<LineRenderer>();
            head.sharedMaterial = line.sharedMaterial;
            head.useWorldSpace = true;
            head.positionCount = 2;
            head.alignment = line.alignment;
            head.textureMode = LineTextureMode.Stretch;
            head.numCapVertices = 0;
            head.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            head.receiveShadows = false;
            head.widthMultiplier = line.widthMultiplier * headWidthScale;
            head.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            head.startColor = head.endColor = headColor;
            head.sortingOrder = line.sortingOrder + 1;
            return head;
        }

        private void Begin(Vector3 from, Vector3 to, float totalDistance, GameObject impactPrefab, List<Pose> impacts)
        {
            start = from;
            direction = (to - from) / totalDistance;
            distance = totalDistance;
            this.impactPrefab = impactPrefab;
            this.impacts.Clear();
            if (impactPrefab != null && impacts != null) this.impacts.AddRange(impacts);
            // 첫 프레임부터 보이도록 한 프레임만큼 나간 상태에서 시작
            head = Mathf.Min(speed * Time.deltaTime, totalDistance);
            gameObject.SetActive(true);
            Draw();
            SpawnPassedImpacts();
        }

        private void Update()
        {
            head += speed * Time.deltaTime;
            SpawnPassedImpacts();
            if (head - length >= distance)
            {
                Release();
                return;
            }
            Draw();
        }

        private void Draw()
        {
            float front = Mathf.Min(head, distance);
            float tail = Mathf.Clamp(head - length, 0f, distance);
            line.SetPosition(0, start + direction * tail);
            line.SetPosition(1, start + direction * front);

            if (headLine == null) return;
            // 머리는 맞은 자리에 닿으면 사라진다 (꼬리만 따라 들어간다)
            bool flying = head < distance;
            headLine.enabled = flying;
            if (flying == false) return;
            headLine.SetPosition(0, start + direction * Mathf.Max(0f, front - headLength));
            headLine.SetPosition(1, start + direction * front);
        }

        /// <summary>총알 머리가 지나간 탄착 자리에 이펙트 생성</summary>
        private void SpawnPassedImpacts()
        {
            for (int i = impacts.Count - 1; i >= 0; i--)
            {
                Pose pose = impacts[i];
                if (Vector3.Dot(pose.position - start, direction) > head) continue;
                Destroy(Instantiate(impactPrefab, pose.position, pose.rotation), impactLifetime);
                impacts.RemoveAt(i);
            }
        }

        private void Release()
        {
            gameObject.SetActive(false);
            impacts.Clear();
            impactPrefab = null;
            if (pools.TryGetValue(source, out var pool) == false)
            {
                pool = new Stack<BulletTracer>();
                pools[source] = pool;
            }
            pool.Push(this);
        }
    }
}
