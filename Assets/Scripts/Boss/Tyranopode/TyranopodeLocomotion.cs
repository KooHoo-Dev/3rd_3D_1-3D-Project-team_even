using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// Tyranopode 이동. NavMeshAgent 를 직접 쓴다 (BossMovement 는 초기화 직후 멈춰 둔다).
    ///  - Circle: 공격 사이에 플레이어를 바라본 채 일정 반경으로 빠르게 맴돈다. 막히거나 일정 시간이 지나면 방향을 바꾼다
    ///  - Manual: 공격이 직접 움직인다 (MoveDelta·Face)
    ///  - 경기장 경계: 몸 중심이 벽에서 wallMargin 안쪽에 머문다 (NavMesh 는 사람 크기 기준이라 그대로 두면 촉수가 벽을 뚫는다).
    ///    경계에 막히면 MoveDelta 가 덜 움직여서 돌진은 벽에 부딪힌 것으로 판정된다
    /// 회전은 항상 여기서 한다 (agent.updateRotation 끔)
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class TyranopodeLocomotion : MonoBehaviour
    {
        private const float RepathInterval = 0.2f;

        [SerializeField, Min(1f)] private float turnSpeed = 240f;
        [Tooltip("맴도는 방향을 바꾸는 간격(초) 범위")]
        [SerializeField] private Vector2 flipInterval = new Vector2(2f, 4f);
        [Tooltip("한 번에 앞쪽으로 잡는 목표 각도(도). 클수록 빠르게 돈다")]
        [SerializeField, Range(5f, 90f)] private float circleStepAngle = 35f;
        [Tooltip("경기장 벽에서 몸 중심까지 둘 거리(m). 촉수 길이 정도 (빌더가 촉수 길이로 채운다)")]
        [SerializeField, Min(0f)] private float wallMargin = 5f;

        private NavMeshAgent agent;
        private TyranopodeView view;
        private Transform target;
        private bool circling;
        private float circleRadius;
        private float circleSpeed;
        private int circleSign = 1;
        private float nextRepathTime;
        private float nextFlipTime;
        private float stuckTime;
        private bool hasBounds;
        private Vector3 boundsCenter;
        private float boundsRadius;

        public Transform Target => target;
        /// <summary>몸 중심이 머물 수 있는 반지름 (경기장 반지름 - wallMargin). 경계가 없으면 0</summary>
        public float InnerRadius => hasBounds ? Mathf.Max(1f, boundsRadius - wallMargin) : 0f;
        public Vector3 Velocity => agent != null && agent.isActiveAndEnabled ? agent.velocity : Vector3.zero;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            view = GetComponent<TyranopodeView>();
        }

        public void Initialize(Transform player)
        {
            target = player;
            agent.updateRotation = false;
            agent.autoBraking = false;
            Stop();
        }

        /// <summary>경기장 경계 (중심·벽까지 반지름). radius 0 이하면 제한 없음</summary>
        public void SetBounds(Vector3 center, float radius)
        {
            hasBounds = radius > 0f;
            boundsCenter = center;
            boundsRadius = radius;
        }

        /// <summary>point 를 경계 안쪽(벽에서 wallMargin)으로 당긴다. 높이는 그대로</summary>
        public Vector3 ClampToBounds(Vector3 point)
        {
            if (hasBounds == false) return point;
            Vector3 offset = point - boundsCenter;
            offset.y = 0f;
            float inner = InnerRadius;
            if (offset.sqrMagnitude <= inner * inner) return point;
            Vector3 clamped = boundsCenter + offset.normalized * inner;
            clamped.y = point.y;
            return clamped;
        }

        /// <summary>플레이어 주위를 맴돈다. 매 프레임 불러도 된다 (값만 갱신)</summary>
        public void Circle(float radius, float speed)
        {
            if (circling == false)
            {
                circling = true;
                nextRepathTime = 0f;
                circleSign = Random.value < 0.5f ? 1 : -1;
                nextFlipTime = Time.time + Random.Range(flipInterval.x, flipInterval.y);
            }
            circleRadius = radius;
            circleSpeed = speed;
        }

        /// <summary>제자리에 멈춘다 (공격·그로기). 이후 이동은 MoveDelta 로만</summary>
        public void Stop()
        {
            circling = false;
            if (IsAgentReady == false) return;
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            agent.isStopped = true;
        }

        /// <summary>NavMesh 위에서 delta 만큼 옮긴다. 반환 = 실제로 움직인 수평 거리 (벽·가장자리에 막히면 작다)</summary>
        public float MoveDelta(Vector3 delta)
        {
            if (IsAgentReady == false) return 0f;
            Vector3 before = transform.position;
            Vector3 limited = ClampToBounds(before + delta) - before;
            limited.y = delta.y;
            agent.Move(limited);
            Vector3 moved = transform.position - before;
            moved.y = 0f;
            return moved.magnitude;
        }

        /// <summary>point 쪽으로 돈다. 다 돌았으면 true</summary>
        public bool Face(Vector3 point, float speedMultiplier = 1f)
        {
            Vector3 dir = point - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return true;
            Quaternion look = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * speedMultiplier * Time.deltaTime);
            return Quaternion.Angle(transform.rotation, look) < 3f;
        }

        /// <summary>
        /// 공중(등장 연출로 벽 너머에서 날아올 때): NavMeshAgent 를 꺼서 transform 을 직접 옮긴다.
        /// false 로 되돌리면 지금 자리에서 NavMesh 에 다시 올린다
        /// </summary>
        public void SetAirborne(bool airborne)
        {
            if (airborne)
            {
                Stop();
                agent.enabled = false;
                return;
            }
            agent.enabled = true;
            agent.Warp(transform.position);
            Stop();
        }

        public void FaceInstant(Vector3 point)
        {
            Vector3 dir = point - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir);
        }

        private bool IsAgentReady => agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;

        private void Update()
        {
            if (circling == false || target == null || IsAgentReady == false) return;

            Face(target.position);
            view.SetLocomotion(transform.InverseTransformDirection(agent.velocity));

            // 막혀서 거의 못 움직이면 방향을 바꾼다
            if (agent.velocity.sqrMagnitude < 1f) stuckTime += Time.deltaTime;
            else stuckTime = 0f;
            if (Time.time >= nextFlipTime || stuckTime > 0.5f)
            {
                circleSign = -circleSign;
                stuckTime = 0f;
                nextFlipTime = Time.time + Random.Range(flipInterval.x, flipInterval.y);
            }

            if (Time.time < nextRepathTime) return;
            nextRepathTime = Time.time + RepathInterval;

            Vector3 offset = transform.position - target.position;
            offset.y = 0f;
            if (offset.sqrMagnitude < 0.01f) offset = -transform.forward;
            Vector3 around = Quaternion.Euler(0f, circleStepAngle * circleSign, 0f) * offset.normalized;
            Vector3 destination = ClampToBounds(target.position + around * circleRadius);

            agent.speed = circleSpeed;
            agent.acceleration = circleSpeed * 4f;
            agent.isStopped = false;
            if (NavMesh.SamplePosition(destination, out var hit, 4f, agent.areaMask)) agent.SetDestination(hit.position);
            else circleSign = -circleSign;
        }
    }
}
