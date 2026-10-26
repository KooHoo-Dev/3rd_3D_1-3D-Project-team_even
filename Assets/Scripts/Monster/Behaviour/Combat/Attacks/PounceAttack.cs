using System;
using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>실제 이륙 때 목표를 고정하고 Active 전체 동안 도약한다. 착지 때 현재 대상에게 한 번만 피해를 준다.</summary>
    public sealed class PounceAttack : MonsterAttack
    {
        private const float Skin = 0.05f;
        private readonly PounceSettings settings;
        private readonly CircleArea landingArea;
        private Vector3 startRoot;
        private Vector3 rootOffset;
        private Vector3 landingGround;
        private float launchTime;
        private float flightSeconds;
        private float progress;
        private int arcSegments;
        private bool launched;
        private Transform playerRoot;

        public PounceAttack(in AttackSpec spec, MonsterBody body, Func<IPlayerTarget, IDamageable> findDamageable,
                            PounceSettings settings) : base(spec, body, findDamageable)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings), "추격 몬스터에 PounceSettings 가 없다");
            this.settings = settings;
            landingArea = new CircleArea(0f, spec.Radius);
        }

        /// <summary>예고 중 목표는 고정하지 않는다. 예고 회전은 기존 MonsterAttack 이 맡는다.</summary>
        protected override void OnWindup()
        {
            launched = false;
            progress = 0f;
            playerRoot = Target.Root;
        }

        protected override bool OnActive(float now, float deltaTime)
        {
            if (launched == false) return Launch(now);
            if (Body.HasAttackMotion == false) return true; // 실행기 비활성화로 이미 정리됨

            float next = Mathf.Clamp01((now - launchTime) / flightSeconds);
            if (IsArcClear(progress, next) == false)
            {
                Body.EndAttackMotion(ResolveInterruptionGround());
                return true; // 무피해. WallHit 기절 없이 기존 Active/회복 주기를 마친다.
            }
            Body.MoveForAttack(PositionAt(next));
            progress = next;
            if (progress < 1f) return false;

            // 비행 중 NavMesh 가 변경되었으면 TerrainGround 가 목표점에 가장 가까운 유효 좌표를 찾는다.
            Vector3 ground = ResolveGround(landingGround);
            if (IsBodyClear(ground + rootOffset) == false)
            {
                Body.EndAttackMotion(ResolveInterruptionGround());
                return true;
            }
            Body.EndAttackMotion(ground);
            Vector3 player = Target.PlayerTrackingInfo.Position;
            if (landingArea.Contains(ground, Body.Forward, player) &&
                Mathf.Abs(player.y - ground.y) <= settings.HitHeightTolerance)
                Hit(Target, ground);
            return true;
        }

        /// <summary>이륙 순간 위치를 한 번 읽고 최대 사거리까지 제한한다. 유효 지면/궤적이 없으면 도약하지 않는다.</summary>
        private bool Launch(float now)
        {
            startRoot = Body.Position;
            Vector3 target = Target.PlayerTrackingInfo.Position;
            Vector3 destination = ClampDestination(startRoot, target, Spec.Range);
            if (TerrainGround.TryNavMeshPoint(Body.AttackGroundOrigin, settings.NavMeshTolerance, Body.AttackNavigationFilter,
                                              out Vector3 startGround) == false)
                throw new InvalidOperationException("도약 출발점의 NavMesh 를 찾을 수 없다");
            rootOffset = Vector3.up * (startRoot.y - startGround.y);
            if (TryLandingGround(destination, settings.GroundProbeHeight, out landingGround) == false) return true;
            flightSeconds = ActiveDuration;
            if (flightSeconds <= 0f) throw new InvalidOperationException("도약에는 양수인 Active 시간이 필요하다");
            launchTime = now;
            // 작은 프레임에서도 곡선과 큰 프레임의 이동 구간을 같은 캡슐 구간으로 검사한다.
            float horizontalSteps = Vector3.Distance(startRoot, landingGround + rootOffset) / (Body.AttackRadius * 0.5f);
            float curveSteps = Mathf.Sqrt(4f * settings.ArcHeight / Skin);
            arcSegments = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(horizontalSteps, curveSteps)));
            if (IsBodyClear(startRoot) == false || IsArcClear(0f, 1f) == false) return true;
            Body.BeginAttackMotion(startGround, ResolveInterruptionGround);
            launched = true;
            return false;
        }

        protected override void OnCancel()
        {
            if (launched && Body.HasAttackMotion) Body.EndAttackMotion(ResolveInterruptionGround());
            launched = false;
            playerRoot = null;
        }

        /// <summary>취소 위치 바로 아래로 내려놓는다. 그 지면이 NavMesh 밖이면 목표점에 가장 가까운 NavMesh 로 보정한다.</summary>
        private Vector3 ResolveInterruptionGround()
        {
            Vector3 desired = Body.Position - rootOffset;
            if (TerrainGround.TryGroundBelow(desired + Vector3.up * Skin, settings.GroundProbeDepth + Skin,
                                             settings.GroundMask, Body.Root, playerRoot, out Vector3 below))
                desired = below;
            Vector3 ground = ResolveGround(desired);
            if (IsBodyClear(ground + rootOffset) == false)
                throw new InvalidOperationException($"도약 취소 복구 지점에 몸체가 들어갈 수 없다: {ground}");
            return ground;
        }

        private Vector3 ResolveGround(Vector3 desired)
        {
            if (TerrainGround.TryNavMeshPoint(desired, settings.NavMeshTolerance, Body.AttackNavigationFilter,
                                              out Vector3 ground)) return ground;
            return TerrainGround.ClosestNavMeshPoint(desired, Body.AttackNavigationFilter);
        }

        private bool TryLandingGround(Vector3 desired, float probeHeight, out Vector3 ground)
        {
            ground = default;
            if (TerrainGround.TryGroundBelow(desired + Vector3.up * probeHeight,
                                             probeHeight + settings.GroundProbeDepth, settings.GroundMask,
                                             Body.Root, playerRoot, out Vector3 physicalGround) == false) return false;
            if (TerrainGround.TryNavMeshPoint(physicalGround, settings.NavMeshTolerance, Body.AttackNavigationFilter,
                                              out ground) == false) return false;
            // 가까운 별층/절벽 옆 좌표를 같은 착지 지면으로 취급하지 않는다.
            if (HitAreas.Flat(ground - physicalGround).magnitude > Skin) return false;
            return IsBodyClear(ground + rootOffset);
        }

        private Vector3 PositionAt(float t) => ArcPosition(startRoot, landingGround + rootOffset, settings.ArcHeight, t);

        /// <summary>큰 Tick 도 포물선의 작은 구간들로 나누어 벽/천장을 통과하지 못하게 한다.</summary>
        private bool IsArcClear(float from, float to)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt((to - from) * arcSegments));
            Vector3 previous = PositionAt(from);
            for (int i = 1; i <= steps; i++)
            {
                Vector3 next = PositionAt(Mathf.Lerp(from, to, (float)i / steps));
                if (IsSegmentClear(previous, next) == false) return false;
                previous = next;
            }
            return true;
        }

        private bool IsSegmentClear(Vector3 from, Vector3 to)
        {
            if (IsBodyClear(to) == false) return false;
            CapsuleAt(from, out Vector3 bottom, out Vector3 top, out float radius);
            Vector3 delta = to - from;
            if (delta.magnitude < 1e-5f) return true;
            foreach (RaycastHit hit in Physics.CapsuleCastAll(bottom, top, radius, delta.normalized,
                                                             delta.magnitude, settings.ObstacleMask, QueryTriggerInteraction.Ignore))
                if (IsObstacle(hit.collider)) return false;
            return true;
        }

        private bool IsBodyClear(Vector3 root)
        {
            CapsuleAt(root, out Vector3 bottom, out Vector3 top, out float radius);
            foreach (Collider collider in Physics.OverlapCapsule(bottom, top, radius, settings.ObstacleMask,
                                                                 QueryTriggerInteraction.Ignore))
                if (IsObstacle(collider)) return false;
            return true;
        }

        private void CapsuleAt(Vector3 root, out Vector3 bottom, out Vector3 top, out float radius)
        {
            Vector3 foot = root - rootOffset;
            radius = Mathf.Max(0.01f, Body.AttackRadius - Skin);
            bottom = foot + Vector3.up * Body.AttackRadius;
            top = foot + Vector3.up * Mathf.Max(Body.AttackRadius, Body.AttackHeight - Body.AttackRadius);
        }

        private bool IsObstacle(Collider collider)
        {
            if (Body.OwnsCollider(collider)) return false;
            if (playerRoot != null && collider.transform.IsChildOf(playerRoot)) return false;
            return true;
        }

        public static Vector3 ClampDestination(Vector3 start, Vector3 target, float range)
        {
            Vector3 horizontal = HitAreas.Flat(target - start);
            if (horizontal.magnitude <= range) return target;
            Vector3 clamped = start + horizontal.normalized * range;
            clamped.y = target.y;
            return clamped;
        }

        public static Vector3 ArcPosition(Vector3 start, Vector3 end, float height, float progress)
        {
            float t = Mathf.Clamp01(progress);
            return Vector3.Lerp(start, end, t) + Vector3.up * (4f * height * t * (1f - t));
        }
    }
}
