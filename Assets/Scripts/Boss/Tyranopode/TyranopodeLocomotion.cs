using HybridShooter.Core.Contracts;
using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// Tyranopode 이동. NavMeshAgent 를 직접 쓴다 (BossMovement 는 초기화 직후 멈춰 둔다).
    ///  - Circle: 공격 사이에 플레이어를 바라본 채 일정 반경으로 빠르게 맴돈다. 막히거나 일정 시간이 지나면 방향을 바꾼다
    ///  - Manual: 공격이 직접 움직인다 (MoveFree·Face)
    ///  - 경기장 경계: 맴돌기는 몸 중심이 실제 벽(NavMesh 가장자리)에서 wallMargin 안쪽에 머문다 (NavMesh 는 사람 크기 기준이라 그대로 두면 촉수가 벽을 뚫는다).
    ///    경기장 원은 가장 먼 벽까지의 바깥 상한이고, 벽 모양(사각형·울퉁불퉁)은 KeepAwayFromWalls 가 따른다.
    ///    경기장은 원으로 어림한 값이라 실제 벽이 아니다 → 돌진·도약은 이 원에 막히지 않는다 (실제 벽은 몸통 벽 감지·FindLanding 이 맡는다)
    /// 회전은 항상 여기서 한다 (agent.updateRotation 끔)
    ///  - MoveFree: 돌진·도약용. NavMesh 의 작은 틈·돌멩이 구멍에 멈추지 않도록 transform 을 직접 옮기고 높이는 바닥에 맞춘다.
    ///    돌진(땅)은 몸통으로 정면 충돌한 가파른 벽(Wall)·그 자리 바닥 근처에 NavMesh 가 없는 낭떠러지(Edge)에서만 멈추고
    ///    비스듬히 스치면 미끄러진다. 도약(공중)은 착지점을 미리 검사해 정하고(FindLanding) 날아가는 동안은 막지 않는다.
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
        /// <summary>벽·경기장 끝과 진행 방향이 이 각도(도) 안으로 마주 보면 정면 충돌, 넘으면 스침</summary>
        private const float HeadOnAngle = 60f;
        /// <summary>도약 착지점을 찾을 때 당기는 간격(m)</summary>
        private const float LandingStep = 2f;
        /// <summary>착지 NavMesh 와 재어 둔 바닥 높이가 이만큼(m) 넘게 다르면 다른 층</summary>
        private const float LevelTolerance = 1.5f;
        /// <summary>착지점까지 걷는 길이 직선의 이 배 + LandingDetourSlack 을 넘으면 벽 너머(크게 돌아야 함)</summary>
        private const float MaxLandingDetour = 2f;
        private const float LandingDetourSlack = 6f;
        /// <summary>벽 거리 두기: 목표 근처 NavMesh 를 찾는 반경(m)·밀어내기 되풀이 횟수 (밀린 자리가 다른 벽에 가까워질 수 있다)</summary>
        private const float WallSample = 4f;
        private const int WallPushIterations = 3;

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
        private readonly Collider[] roomHits = new Collider[16];
        // NavMeshPath 는 필드 초기화에서 만들 수 없다 (Unity 가 막음) → Awake 에서 만든다
        private NavMeshPath landingPath;

        /// <summary>MoveFree 가 멈춘 이유: 몸통이 벽에 정면 충돌 / 그 자리 바닥에 NavMesh 가 없음 (절벽·맵 끝)</summary>
        public enum MoveStop { None, Wall, Edge }

        /// <summary>마지막 벽 감지 (기즈모)</summary>
        public Vector3 LastProbeOrigin { get; private set; }
        public Vector3 LastProbeEnd { get; private set; }
        public Vector3? LastWallPoint { get; private set; }
        public WallContact LastContact { get; private set; }
        /// <summary>마지막 도약 착지점 고르기 (기즈모·로그): 목표 자리, 고른 자리, 목표 자리를 버린 이유</summary>
        public Vector3 LastLandingDesired { get; private set; }
        public Vector3 LastLandingChosen { get; private set; }
        public LandingIssue LastLandingIssue { get; private set; }
        /// <summary>마지막으로 벽으로 감지한 콜라이더 (돌진 멈춤 로그)</summary>
        public Collider LastWallCollider { get; private set; }

        public Transform Target => target;
        /// <summary>몸 중심이 머물 수 있는 반지름 (경기장 반지름 - wallMargin). 경계가 없으면 0</summary>
        public float InnerRadius => hasBounds ? Mathf.Max(1f, boundsRadius - wallMargin) : 0f;
        /// <summary>벽에서 몸 중심까지 둘 거리(m)</summary>
        public float WallMargin => wallMargin;
        public Vector3 Velocity => agent != null && agent.isActiveAndEnabled ? agent.velocity : Vector3.zero;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            view = GetComponent<TyranopodeView>();
            landingPath = new NavMeshPath();
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

        /// <summary>
        /// point 를 실제 벽(걸을 수 있는 바닥의 끝)에서 wallMargin 안쪽으로 민다. 경기장이 원이 아니어도(울퉁불퉁한 사각형 등) 벽마다 같은 거리를 둔다.
        /// 가장 가까운 NavMesh 가장자리의 반대쪽으로 밀고, 밀린 자리가 다른 벽에 가까워질 수 있어 몇 번 되풀이한다.
        /// 경기장 안 바위·기둥 둘레도 가장자리라 같은 거리만큼 피한다. 근처에 NavMesh 가 없으면 그대로
        /// </summary>
        public Vector3 KeepAwayFromWalls(Vector3 point)
        {
            int mask = agent != null ? agent.areaMask : NavMesh.AllAreas;
            if (NavMesh.SamplePosition(point, out var onMesh, WallSample, mask) == false) return point;

            Vector3 result = onMesh.position;
            for (int i = 0; i < WallPushIterations; i++)
            {
                if (NavMesh.FindClosestEdge(result, out var edge, mask) == false) break;
                float push = wallMargin - edge.distance;
                if (push <= 0.05f) break;

                // 가장자리에서 지금 자리 쪽이 안쪽. 가장자리 위에 있으면 방향을 알 수 없어 가장자리 법선을 쓴다
                Vector3 away = result - edge.position;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f)
                {
                    away = edge.normal;
                    away.y = 0f;
                }
                if (away.sqrMagnitude < 0.0001f) break;

                if (NavMesh.SamplePosition(result + away.normalized * push, out var moved, WallSample, mask) == false) break;
                result = moved.position;
            }
            return result;
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
        ///  - 땅 (airborne false, 돌진): 몸통 크기 벽 감지. 정면으로 박으면 Wall, 비스듬히 스치면 벽을 따라 미끄러진다.
        ///    경기장 원(어림값)에는 막히지 않는다. 그 자리 바닥 근처에 NavMesh 가 없으면 Edge (절벽·맵 끝, 부딪힌 게 아님)
        ///  - 공중 (airborne true, 도약): 벽·낭떠러지를 보지 않는다 (가파른 경사처럼 NavMesh 가 없는 곳 위도 날아간다). 착지점은 FindLanding 으로
        /// 반환 = 멈춘 이유. 멈추면 그 자리에 둔다
        /// </summary>
        public MoveStop MoveFree(Vector3 delta, bool airborne, out float moved)
        {
            if (free == false)
            {
                Stop();
                agent.updatePosition = false;
                free = true;
            }

            Vector3 before = transform.position;
            delta.y = 0f;
            moved = 0f;
            if (delta.sqrMagnitude < 0.00000001f) return MoveStop.None;

            Vector3 inside;
            float ground;
            if (airborne)
            {
                inside = before + delta;
                ground = TyranopodeGround.Height(inside, before.y);
            }
            else
            {
                float distance = delta.magnitude;
                var contact = ProbeWall(delta / distance, distance, out Vector3 wallNormal);
                if (contact == WallContact.HeadOn) return MoveStop.Wall;
                // 스침: 벽 쪽 성분을 빼고 벽을 따라 미끄러진다
                if (contact == WallContact.Graze) delta = Vector3.ProjectOnPlane(delta, wallNormal);

                inside = before + delta;

                // NavMesh 는 지금 높이가 아니라 그 자리 바닥 높이에서 찾는다 (구덩이 가장자리처럼 높이 차가 커도 이어져 있으면 지나간다)
                ground = TyranopodeGround.Height(inside, before.y);
                if (NavMesh.SamplePosition(new Vector3(inside.x, ground, inside.z), out _, gapTolerance, agent.areaMask) == false) return MoveStop.Edge;
            }

            // 높이는 바닥을 부드럽게 따라가되(바위 위로 툭 튀지 않게), 가파른 오르막에서도 지형 속으로 파고들지 않는다
            float follow = 1f - Mathf.Exp(-heightFollow * Time.deltaTime);
            inside.y = Mathf.Max(Mathf.Lerp(before.y, ground, follow), ground - MaxSink);
            transform.position = inside;
            agent.nextPosition = inside;
            moved = new Vector2(inside.x - before.x, inside.z - before.z).magnitude;
            return MoveStop.None;
        }

        /// <summary>도약 착지 후보를 버린 이유</summary>
        public enum LandingIssue { None, NoNavMesh, OtherLevel, Disconnected, WallInPath, NoRoom }

        /// <summary>
        /// 도약 착지점: desired 부터 from 쪽으로 LandingStep 씩 당기며, 처음으로 다음을 모두 만족하는 자리. 끝내 없으면 from.
        ///  - 그 자리 바닥에 NavMesh 가 있다 (바위 구멍·가파른 경사·맵 밖 X)
        ///  - 그 NavMesh 가 재어 둔 바닥과 같은 층이다 (지붕·다리 아래·다른 층 X)
        ///  - 보스 발밑에서 걸어서 이어지고 크게 돌지 않는다 (벽 너머·끊긴 구덩이 X)
        ///  - 보스에서 거기까지 몸 높이에 가파른 벽이 없다 (벽을 뚫고 날아가지 않게)
        ///  - 착지한 몸통 자리에 벽이 겹치지 않는다
        /// 경기장 원(어림값)으로는 당기지 않는다. 결과는 기즈모·로그로 (LastLanding*)
        /// </summary>
        public Vector3 FindLanding(Vector3 from, Vector3 desired)
        {
            LastLandingDesired = desired;
            LastLandingIssue = LandingIssue.None;
            Vector3 fromGround = new Vector3(from.x, TyranopodeGround.Height(from, from.y), from.z);
            bool hasStart = NavMesh.SamplePosition(fromGround, out var start, gapTolerance, agent.areaMask);

            Vector3 back = from - desired;
            back.y = 0f;
            float length = back.magnitude;
            Vector3 dir = length > 0.01f ? back / length : Vector3.zero;
            for (float pulled = 0f; pulled <= length; pulled += LandingStep)
            {
                var issue = CheckLanding(from, hasStart, start.position, desired + dir * pulled, out Vector3 point);
                if (issue == LandingIssue.None)
                {
                    LastLandingChosen = point;
                    if (pulled >= LandingStep)
                        Debug.Log($"[Tyranopode] 도약 착지점을 {pulled:0.#}m 당김: 목표 자리 {LastLandingIssue}", this);
                    return point;
                }
                if (pulled <= 0f) LastLandingIssue = issue;
            }
            LastLandingChosen = from;
            Debug.Log($"[Tyranopode] 도약 착지점을 찾지 못해 제자리: 목표 자리 {LastLandingIssue}", this);
            return from;
        }

        private LandingIssue CheckLanding(Vector3 from, bool hasStart, Vector3 start, Vector3 candidate, out Vector3 point)
        {
            point = candidate;
            float ground = TyranopodeGround.Height(candidate, candidate.y);
            if (NavMesh.SamplePosition(new Vector3(candidate.x, ground, candidate.z), out var hit, gapTolerance, agent.areaMask) == false)
                return LandingIssue.NoNavMesh;
            if (Mathf.Abs(hit.position.y - ground) > LevelTolerance) return LandingIssue.OtherLevel;
            point = hit.position;

            if (hasStart)
            {
                if (NavMesh.CalculatePath(start, point, agent.areaMask, landingPath) == false || landingPath.status != NavMeshPathStatus.PathComplete)
                    return LandingIssue.Disconnected;
                float walk = 0f;
                var corners = landingPath.corners;
                for (int i = 1; i < corners.Length; i++) walk += Vector3.Distance(corners[i - 1], corners[i]);
                Vector3 flat = point - start;
                flat.y = 0f;
                if (walk > flat.magnitude * MaxLandingDetour + LandingDetourSlack) return LandingIssue.Disconnected;
            }

            if (IsJumpBlocked(from, point)) return LandingIssue.WallInPath;
            if (HasRoom(point) == false) return LandingIssue.NoRoom;
            return LandingIssue.None;
        }

        /// <summary>from → to 사이 몸 높이에 가파른 벽이 있나 (몸통 절반 굵기로 훑는다. 낮은 바위·완만한 언덕은 넘는다)</summary>
        private bool IsJumpBlocked(Vector3 from, Vector3 to)
        {
            Vector3 lift = Vector3.up * (wallProbeHeight + wallProbeRadius);
            Vector3 origin = from + lift;
            Vector3 delta = to + lift - origin;
            float distance = delta.magnitude;
            if (distance < 0.01f) return false;
            int count = Physics.SphereCastNonAlloc(origin, wallProbeRadius * 0.5f, delta / distance, wallHits, distance, ~0, QueryTriggerInteraction.Ignore);
            float slopeLimit = Mathf.Cos(wallMinSteepness * Mathf.Deg2Rad);
            for (int i = 0; i < count; i++)
            {
                var hit = wallHits[i];
                if (hit.distance <= 0f || hit.normal.y >= slopeLimit) continue;
                if (IsWallCollider(hit.collider)) return true;
            }
            return false;
        }

        /// <summary>point 에 착지한 몸통 자리(몸 높이 구)에 벽이 겹치지 않는가. 지형(터레인)은 바닥이라 뺀다</summary>
        private bool HasRoom(Vector3 point)
        {
            Vector3 center = point + Vector3.up * (wallProbeHeight + wallProbeRadius);
            int count = Physics.OverlapSphereNonAlloc(center, wallProbeRadius, roomHits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (roomHits[i] is TerrainCollider == false && IsWallCollider(roomHits[i])) return false;
            return true;
        }

        /// <summary>벽 감지에서 벽으로 칠 콜라이더: 보스 자신·플레이어·몬스터(IDamageable)가 아닌 것</summary>
        private bool IsWallCollider(Collider collider)
            => collider.transform.IsChildOf(transform) == false && collider.GetComponentInParent<IDamageable>() == null;

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

        /// <summary>벽 감지 결과: 없음 / 비스듬히 스침 / 정면 충돌</summary>
        public enum WallContact { None, Graze, HeadOn }

        /// <summary>
        /// 몸통 크기 구(wallProbeRadius, 바닥에서 wallProbeHeight 위)를 direction 으로 bodyReach + distance 만큼 훑어 벽을 찾는다.
        /// 낮은 돌멩이·바닥은 이 높이에 걸리지 않고, wallMinSteepness 보다 완만한 면은 오를 수 있는 경사라 벽이 아니다.
        /// 맞은 벽이 진행 방향과 HeadOnAngle 이내로 마주 보면 정면 충돌, 아니면 스침 (wallNormal = 수평 법선).
        /// 보스 자신·플레이어·몬스터(IDamageable)·트리거·처음부터 겹친 것은 건너뛴다
        /// </summary>
        public WallContact ProbeWall(Vector3 direction, float distance, out Vector3 wallNormal)
        {
            Vector3 origin = transform.position + Vector3.up * (wallProbeHeight + wallProbeRadius);
            float length = bodyReach + distance;
            LastProbeOrigin = origin;
            LastProbeEnd = origin + direction * length;
            LastWallPoint = null;
            LastContact = WallContact.None;
            LastWallCollider = null;
            wallNormal = Vector3.zero;
            int count = Physics.SphereCastNonAlloc(origin, wallProbeRadius, direction, wallHits, length, ~0, QueryTriggerInteraction.Ignore);
            float slopeLimit = Mathf.Cos(wallMinSteepness * Mathf.Deg2Rad);
            float headOn = Mathf.Cos(HeadOnAngle * Mathf.Deg2Rad);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = wallHits[i];
                if (hit.distance <= 0f || hit.distance >= nearest) continue;
                if (hit.normal.y >= slopeLimit) continue; // 완만한 경사
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.collider.GetComponentInParent<IDamageable>() != null) continue;
                Vector3 normal = hit.normal;
                normal.y = 0f;
                if (normal.sqrMagnitude < 0.0001f) continue;
                nearest = hit.distance;
                wallNormal = normal.normalized;
                LastWallPoint = hit.point;
                LastWallCollider = hit.collider;
                LastContact = Vector3.Dot(-wallNormal, direction) >= headOn ? WallContact.HeadOn : WallContact.Graze;
            }
            return LastContact;
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
        /// <summary>선택하면: 마지막 벽 감지 (초록 = 비었음, 노랑 = 스침(미끄러짐), 빨강 = 정면 충돌)</summary>
        private void OnDrawGizmosSelected()
        {
            if (Application.isPlaying == false || LastProbeOrigin == Vector3.zero) return;
            Gizmos.color = LastContact == WallContact.HeadOn ? Color.red : LastContact == WallContact.Graze ? Color.yellow : Color.green;
            Gizmos.DrawWireSphere(LastProbeOrigin, wallProbeRadius);
            Gizmos.DrawLine(LastProbeOrigin, LastProbeEnd);
            Gizmos.DrawWireSphere(LastProbeEnd, wallProbeRadius);
            if (LastWallPoint.HasValue) Gizmos.DrawSphere(LastWallPoint.Value, 0.4f);

            // 마지막 도약: 흰색 = 목표 자리, 하늘색 = 고른 착지점 (다르면 당겨진 것, 이유는 로그)
            if (LastLandingDesired == Vector3.zero) return;
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(LastLandingDesired, 1f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(LastLandingChosen, 1.2f);
            Gizmos.DrawLine(LastLandingDesired, LastLandingChosen);
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
            // 경기장 원(바깥 상한) 안으로 당긴 뒤 실제 벽에서 거리를 둔다
            Vector3 destination = KeepAwayFromWalls(ClampToBounds(target.position + around * circleRadius));

            agent.speed = circleSpeed;
            agent.acceleration = circleSpeed * 4f;
            agent.isStopped = false;
            if (NavMesh.SamplePosition(destination, out var hit, 4f, agent.areaMask)) agent.SetDestination(hit.position);
            else circleSign = -circleSign;
        }
    }
}
