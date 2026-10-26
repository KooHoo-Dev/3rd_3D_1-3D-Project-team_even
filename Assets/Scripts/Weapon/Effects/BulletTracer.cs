using System.Collections.Generic;
using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 히트스캔 총알의 탄 궤적. 짧은 빛줄기가 총구에서 맞은 자리까지 날아가고 끝에 닿으면 사라진다.
    ///  - 판정은 이미 레이캐스트로 끝났고 이건 보이기만 한다 (피해와 무관)
    ///  - 연사가 빨라 매번 만들고 지우지 않도록 프리팹별로 재사용한다
    ///  - 선 두 점은 꼬리 → 머리 순서. 색 그라데이션(꼬리 투명 → 머리 밝음)은 프리팹의 LineRenderer 에서 정한다
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class BulletTracer : MonoBehaviour
    {
        [Tooltip("빛줄기 속도 (m/s). 실제 탄속보다 느려야 눈에 보인다")]
        [SerializeField] private float speed = 350f;
        [Tooltip("빛줄기 길이 (m)")]
        [SerializeField] private float length = 5f;
        [Tooltip("총구와 맞은 자리가 이보다 가까우면 그리지 않는다")]
        [SerializeField] private float minDistance = 1.5f;

        private static readonly Dictionary<BulletTracer, Stack<BulletTracer>> pools = new Dictionary<BulletTracer, Stack<BulletTracer>>();

        private BulletTracer source;
        private LineRenderer line;
        private Vector3 start;
        private Vector3 direction;
        private float distance;
        private float head;

        /// <summary>from(총구) 에서 to(맞은 자리) 까지 빛줄기 하나</summary>
        public static void Fire(BulletTracer prefab, Vector3 from, Vector3 to)
        {
            float distance = Vector3.Distance(from, to);
            if (distance < prefab.minDistance) return;
            Take(prefab).Begin(from, to, distance);
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
            return tracer;
        }

        private void Begin(Vector3 from, Vector3 to, float totalDistance)
        {
            start = from;
            direction = (to - from) / totalDistance;
            distance = totalDistance;
            // 첫 프레임부터 보이도록 한 프레임만큼 나간 상태에서 시작
            head = Mathf.Min(speed * Time.deltaTime, totalDistance);
            gameObject.SetActive(true);
            Draw();
        }

        private void Update()
        {
            head += speed * Time.deltaTime;
            if (head - length >= distance)
            {
                Release();
                return;
            }
            Draw();
        }

        private void Draw()
        {
            float tail = Mathf.Clamp(head - length, 0f, distance);
            line.SetPosition(0, start + direction * tail);
            line.SetPosition(1, start + direction * Mathf.Min(head, distance));
        }

        private void Release()
        {
            gameObject.SetActive(false);
            if (pools.TryGetValue(source, out var pool) == false)
            {
                pool = new Stack<BulletTracer>();
                pools[source] = pool;
            }
            pool.Push(this);
        }
    }
}
