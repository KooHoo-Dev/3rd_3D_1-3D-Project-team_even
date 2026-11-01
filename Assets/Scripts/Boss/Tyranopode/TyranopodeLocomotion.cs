using HybridShooter.Core.Contracts;
using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// Tyranopode 이동. NavMeshAgent 를 직접 쓴다 (BossMovement 는 초기화 직후 멈춰 둔다).
    ///  - Circle: 공격 사이에 플레이어를 바라본 채 일정 반경으로 빠르게 맴돈다. 막히거나 일정 시간이 지나면 방향을 바꾼다
    ///  - Manual: 공격이 직접 움직인다 (MoveFree·Face)
    ///  - 경기장 경계: 몸 중심이 벽에서 wallMargin 안쪽에 머문다 (NavMesh 는 사람 크기 기준이라 그대로 두면 촉수가 벽을 뚫는다).
    ///    돌진이 경계에 닿으면 벽에 부딪힌 것으로 본다
    /// 회전은 항상 여기서 한다 (agent.updateRotation 끔)
    ///  - MoveFree: 돌진·도약용. NavMesh 의 작은 틈·돌멩이 구멍에 멈추지 않도록 transform 을 직접 옮기고 높이는 바닥에 맞춘다.
    ///    멈추는 것은 몸 높이에 걸리는 가파른 벽(Wall)·경기장 경계(Bounds)·그 자리 바닥 근처에 NavMesh 가 없는 낭떠러지(Edge)뿐.
    ///    높이 차이는 막지 않는다: 이어진 경사(구덩이 안팎)는 오르내린다. EndFree 로 NavMesh 에 되돌린다
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

        [Header("돌진·도약 (NavMesh 밖 이동)")]
        [Tooltip("벽 감지 높이(m, 바닥 기준). 이보다 낮은 돌멩이는 타고 넘는다")]
        [SerializeField, Min(0.1f)] private float wallProbeHeight = 2.5f;
        [Tooltip("벽 감지 구 반지름(m). 몸통 폭 정도")]
        [SerializeField, Min(0.1f)] private float wallProbeRadius = 2f;
        [Tooltip("몸 중심에서 앞쪽 끝까지(m). 이만큼 앞을 더 본다")]
        [SerializeField, Min(0f)] private float bodyReach = 3f;
        [Tooltip("이 거리 안에 NavMesh 가 있으면 틈·구멍으로 보고 지나간다. 없으면 낭떠러지")]
        [SerializeField, Min(0.1f)] private float gapTolerance = 2.5f;
        [Tooltip("이보다 완만한 면(도)은 벽이 아니라 오를 수 있는 경사 (구덩이 벽·언덕)")]
        [SerializeField, Range(10f, 89f)] private float wallMinSteepness = 50f;
        [Tooltip("높이가 바닥을 따라가는 빠르기 (클수록 바로 붙는다)")]
        [SerializeField, Min(1f)] private float heightFollow = 20f;

        /// <summary>바닥보다 이만큼 넘게 파고들지 않는다 (가파른 오르막에서 지형 속으로 들어가지 않게)</summary>
        private const float MaxSink = 0.4f;

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
        private bool free;
        private readonly RaycastHit[] wallHits = new RaycastHit[16];

        /// <summary>MoveFree 가 멈춘 이유</summary>
        public enum MoveStop { None, Wall, Bounds, Edge }

        /// <summary>마지막 벽 감지 (기즈모)</summary>
        public Vector3 LastProbeOrigin { get; private set; }
        public Vector3 LastProbeEnd { get; private set; }
        public Vector3? LastWallPoint { get; private set; }

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

        /// <summary>제자리에 멈춘다 (공격·그로기). 이후 이동은 MoveFree 로만</summary>
        public void Stop()
        {
            circling = false;
            if (IsAgentReady == false) return;
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            agent.isStopped = true;
        }

        /// <summary>
        /// 돌진·도약 이동: delta 의 수평 성분만큼 직접 옮기고 높이는 바닥에 맞춘다 (처음 부르면 Agent 위치 갱신을 끈다).
        /// probeWalls = 몸 높이 벽 감지 (도약은 끈다). 반환 = 멈춘 이유. 멈추면 그 자리에 둔다
        /// </summary>
        public MoveStop MoveFree(Vector3 delta, bool probeWalls, out float moved)
        {
            if (free == false)
            {
                Stop();
                agent.updatePosition = false;
                free = true;
            }

            Vector3 before = transform.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            moved = 0f;
            if (distance < 0.0001f) return MoveStop.None;

            if (probeWalls && ProbeWall(delta / distance, distance)) return MoveStop.Wall;

            Vector3 target = before + delta;
            Vector3 inside = ClampToBounds(target);
            var stop = (inside - target).sqrMagnitude > 0.0001f ? MoveStop.Bounds : MoveStop.None;
            // NavMesh 는 지금 높이가 아니라 그 자리 바닥 높이에서 찾는다 (구덩이 가장자리처럼 높이 차가 커도 이어져 있으면 지나간다)
            float ground = TyranopodeGround.Height(inside, before.y);
            if (NavMesh.SamplePosition(new Vector3(inside.x, ground, inside.z), out _, gapTolerance, agent.areaMask) == false) return MoveStop.Edge;

            // 높이는 바닥을 부드럽게 따라가되(바위 위로 툭 튀지 않게), 가파른 오르막에서도 지형 속으로 파고들지 않는다
            float follow = 1f - Mathf.Exp(-heightFollow * Time.deltaTime);
            inside.y = Mathf.Max(Mathf.Lerp(before.y, ground, follow), ground - MaxSink);
            transform.position = inside;
            agent.nextPosition = inside;
            moved = new Vector2(inside.x - before.x, inside.z - before.z).magnitude;
            return stop;
        }

        /// <summary>MoveFree 를 끝내고 가장 가까운 NavMesh 위로 되돌린다 (돌진·도약이 끝나거나 취소될 때)</summary>
        public void EndFree()
        {
            if (free == false) return;
            free = false;
            // 지금 수평 자리의 바닥에서 가장 가까운 NavMesh (3D 로 가장 가까운 곳을 찾으면 구덩이 바닥으로 되돌아갈 수 있다)
            Vector3 position = transform.position;
            position.y = TyranopodeGround.Height(position, position.y);
            if (NavMesh.SamplePosition(position, out var hit, gapTolerance * 2f, agent.areaMask)) agent.Warp(hit.position);
            else agent.Warp(position);
            agent.updatePosition = true;
            Stop();
        }

        /// <summary>
        /// 몸 높이(wallProbeHeight)에서 direction 으로 bodyReach + distance 안에 벽이 있나. 낮은 돌멩이·바닥은 이 높이에 걸리지 않는다.
        /// wallMinSteepness 보다 완만한 면은 오를 수 있는 경사라 벽이 아니다 (구덩이 안에서 바깥 경사면을 보는 경우).
        /// 보스 자신·플레이어·몬스터(IDamageable)·트리거·처음부터 겹친 것은 건너뛴다
        /// </summary>
        public bool ProbeWall(Vector3 direction, float distance)
        {
            Vector3 origin = transform.position + Vector3.up * (wallProbeHeight + wallProbeRadius);
            float length = bodyReach + distance;
            LastProbeOrigin = origin;
            LastProbeEnd = origin + direction * length;
            LastWallPoint = null;
            int count = Physics.SphereCastNonAlloc(origin, wallProbeRadius, direction, wallHits, length, ~0, QueryTriggerInteraction.Ignore);
            float slopeLimit = Mathf.Cos(wallMinSteepness * Mathf.Deg2Rad);
            for (int i = 0; i < count; i++)
            {
                var hit = wallHits[i];
                if (hit.distance <= 0f) continue;
                if (hit.normal.y >= slopeLimit) continue; // 완만한 경사
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.collider.GetComponentInParent<IDamageable>() != null) continue;
                LastWallPoint = hit.point;
                return true;
            }
            return false;
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

#if UNITY_EDITOR
        /// <summary>선택하면: 마지막 벽 감지 (초록 = 비었음, 빨강 = 벽에 걸림)</summary>
        private void OnDrawGizmosSelected()
        {
            if (Application.isPlaying == false || LastProbeOrigin == Vector3.zero) return;
            Gizmos.color = LastWallPoint.HasValue ? Color.red : Color.green;
            Gizmos.DrawWireSphere(LastProbeOrigin, wallProbeRadius);
            Gizmos.DrawLine(LastProbeOrigin, LastProbeEnd);
            Gizmos.DrawWireSphere(LastProbeEnd, wallProbeRadius);
            if (LastWallPoint.HasValue) Gizmos.DrawSphere(LastWallPoint.Value, 0.4f);
        }
#endif

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
