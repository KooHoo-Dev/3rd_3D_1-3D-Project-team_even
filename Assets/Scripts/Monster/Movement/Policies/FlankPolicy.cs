using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 측면 침투형: 멀리 있으면(원거리 모드) 배율 속도(FlankSpeedMultiplier)로 플레이어 카메라 시야 밖의 측면 링 지점으로 접근하고,
    /// 전환 거리 안이면(근거리 모드) 기본 이동 속도로 목표(또는 슬롯)로 돌입(rush)한다. Wait 는 근접형과 동일.
    /// 시야는 카메라 위치·정면·FOV 수평 반각으로만 판정하고 가림은 보지 않는다.
    /// </summary>
    public sealed class FlankPolicy : IMovementPolicy
    {
        // 목적지를 고를 때 시야 반각 바깥으로 더 벌리는 여유 (도). 다시 고를지는 반각만으로 판정한다
        private const float BearingMarginDeg = 30f;
        // 측면 링 위 후보 지점 간격 (도)
        private const float CandidateStepDeg = 10f;

        /// <summary>거리 모드. Far = 시야 밖 목적지로 접근, Near = 슬롯·토큰 흐름으로 돌입</summary>
        private enum FlankMode { Far, Near }

        // 개체별 난수 (대기 중 선회 여부 선택용)
        private readonly System.Random rng;

        // ── 대기(Wait) 관련 ──
        private DirectiveKind lastKind; // 직전 지시 종류. Wait 진입 순간 감지용
        private bool orbiting;          // 대기 중 선회 여부 (진입 시 1회 결정)

        // ── 거리 모드와 시야 밖 목적지 ──
        private FlankMode mode;              // 전환 거리 이하에서 Near, 전환 거리 + 폭 초과에서 Far
        private bool hasFlankDestination;    // false = 다음 원거리 판단에서 목적지를 새로 고른다
        private float flankAngleDeg;         // 도, 목표 중심 측면 링 위 목적지의 방위각. 지점은 매 판단 현재 목표 위치로 다시 계산
        private bool isApproachingOutOfView; // 마지막 판단이 시야 밖 목적지로 접근하는 의도였는가

        // 마지막 판단이 시야 밖 목적지로 접근하는 의도였는가. MonsterController 가 원거리 이동 클립 배율을 고를 때 읽는다
        public bool IsApproachingOutOfView => isApproachingOutOfView;

        /// <summary>개체별 난수를 준비한다. MovementPolicyFactory 가 개체마다 하나씩 만든다</summary>
        /// <param name="seed">난수 시드 (개체 InstanceID)</param>
        public FlankPolicy(int seed)
        {
            rng = new System.Random(seed);
        }

        /// <summary>
        /// 목표 무효 → 정지. 슬롯 대기 → 제자리 대기. Wait 지시 → 근접형과 같은 대기/선회.
        /// 그 외 → 거리 모드를 갱신하고, 원거리면 배율 속도로 시야 밖 목적지로 접근, 근거리면 기본 속도로 돌입
        /// </summary>
        /// <param name="ctx">자기·목표 위치, 카메라 시야, 군중 지시, 유형별 수치를 담은 판단 입력</param>
        /// <returns>이번 판단의 이동 의도</returns>
        public MoveIntent Evaluate(in MovementContext ctx)
        {
            var dir = ctx.Directive;
            isApproachingOutOfView = false; // ApproachOutOfView 가 시야 밖 목적지 의도를 낼 때만 true

            if (ctx.TargetValid == false)
            {
                ClearFlankDestination();
                lastKind = dir.Kind;
                return MoveIntent.Stop(MovementState.Idle);
            }

            var t = ctx.Tuning;
            if (dir.RequiresSlot && dir.HasSlotPosition == false)
                return MoveIntent.Stop(MovementState.Waiting);
            MoveIntent intent;

            if (dir.Kind == DirectiveKind.Wait)
            {
                ClearFlankDestination();
                if (lastKind != DirectiveKind.Wait)
                    orbiting = rng.NextDouble() < t.OrbitChance; // 대기 진입 시 1회만 선택
                intent = WaitOrbit.Evaluate(in ctx, orbiting);
            }
            else
            {
                float d = WaitOrbit.HorizontalDistance(ctx.SelfPosition, ctx.TargetPosition);
                bool reachedSwitchDistance = d <= t.FlankSwitchDistance;
                bool leftSwitchBand = d > t.FlankSwitchDistance + t.FlankSwitchBand;
                if (reachedSwitchDistance) mode = FlankMode.Near;
                if (leftSwitchBand) mode = FlankMode.Far;

                if (mode == FlankMode.Far)
                    intent = ApproachOutOfView(in ctx, d);
                else
                    intent = RushToTarget(in ctx, d);
            }

            lastKind = dir.Kind;
            return intent;
        }

        /// <summary>
        /// 원거리 접근: 저장한 링 각도의 시야 밖 목적지로 이동하는 의도를 만든다. Evaluate 가 원거리 모드일 때 호출한다.
        /// 목적지는 매 판단 현재 목표 위치 기준으로 다시 계산해 목표가 움직여도 따라간다.
        /// 목적지가 없거나 카메라 시야에 들어왔으면 각도를 새로 고르고, 시야 밖에 있는 동안은 유지한다.
        /// </summary>
        /// <param name="ctx">판단 입력. 목표 위치, 카메라 시야, 유형별 수치를 쓴다</param>
        /// <param name="distanceToTarget">목표까지 수평 거리 (m). 고를 목적지가 없어 돌입할 때 넘긴다</param>
        /// <returns>시야 밖 목적지로 배율 속도(MoveSpeed × FlankSpeedMultiplier)로 향하는 Approach 의도. 고를 목적지가 없으면 RushToTarget 의 의도</returns>
        private MoveIntent ApproachOutOfView(in MovementContext ctx, float distanceToTarget)
        {
            Vector3 flankDestination = WaitOrbit.PointOnRing(ctx.TargetPosition, flankAngleDeg, ctx.Tuning.FlankRadius);
            bool destinationInView = hasFlankDestination && IsInView(in ctx, flankDestination, ctx.ViewHalfAngleDeg);
            bool needsNewDestination = hasFlankDestination == false || destinationInView;
            if (needsNewDestination)
            {
                hasFlankDestination = ChooseOutOfViewAngle(in ctx, out flankAngleDeg);
                flankDestination = WaitOrbit.PointOnRing(ctx.TargetPosition, flankAngleDeg, ctx.Tuning.FlankRadius);
            }
            if (hasFlankDestination == false)
                return RushToTarget(in ctx, distanceToTarget);

            isApproachingOutOfView = true;
            float approachSpeed = ctx.Tuning.MoveSpeed * ctx.Tuning.FlankSpeedMultiplier;
            return new MoveIntent(MovementState.Approach, flankDestination, approachSpeed, 0.5f);
        }

        /// <summary>
        /// 지점이 플레이어 카메라 시야 안인지 수평 각도로 판정한다. 가림은 보지 않는다.
        /// ApproachOutOfView(유지 판정, 반각)와 ChooseOutOfViewAngle(후보 판정, 반각 + 여유)이 호출한다.
        /// </summary>
        /// <param name="ctx">판단 입력. 카메라 위치(ViewPosition)와 정면(TargetForward)을 쓴다</param>
        /// <param name="point">판정할 지점 (월드)</param>
        /// <param name="halfAngleDeg">시야로 볼 수평 반각 (도)</param>
        /// <returns>카메라 정면과 지점 방향의 수평 각이 반각 이하면 true. 지점이 카메라 위치와 같으면 true</returns>
        private static bool IsInView(in MovementContext ctx, Vector3 point, float halfAngleDeg)
        {
            Vector3 viewForward = Flat(ctx.TargetForward);
            Vector3 toPoint = Flat(point - ctx.ViewPosition);
            return Vector3.Angle(viewForward, toPoint) <= halfAngleDeg;
        }

        /// <summary>
        /// 목표 중심 FlankRadius 링 위 후보(CandidateStepDeg 간격) 중, 시야 반각 + 여유 밖이면서 몬스터에게 가장 가까운 지점의 방위각을 고른다.
        /// ApproachOutOfView 가 목적지를 새로 고를 때 호출한다.
        /// </summary>
        /// <param name="ctx">판단 입력. 자기·목표 위치, 카메라 시야, FlankRadius 를 쓴다</param>
        /// <param name="angleDeg">고른 지점의 방위각 (도, WaitOrbit.PointOnRing 기준). 못 찾으면 0</param>
        /// <returns>시야 밖 후보를 찾았으면 true</returns>
        private static bool ChooseOutOfViewAngle(in MovementContext ctx, out float angleDeg)
        {
            float chooseHalfAngleDeg = ctx.ViewHalfAngleDeg + BearingMarginDeg;
            int candidateCount = Mathf.RoundToInt(360f / CandidateStepDeg);
            bool found = false;
            float closestDistance = float.MaxValue;
            angleDeg = 0f;

            for (int i = 0; i < candidateCount; i++)
            {
                float candidateAngleDeg = i * CandidateStepDeg;
                Vector3 candidate = WaitOrbit.PointOnRing(ctx.TargetPosition, candidateAngleDeg, ctx.Tuning.FlankRadius);
                float distanceToSelf = WaitOrbit.HorizontalDistance(ctx.SelfPosition, candidate);
                if (IsInView(in ctx, candidate, chooseHalfAngleDeg)) continue;
                if (distanceToSelf >= closestDistance) continue;

                closestDistance = distanceToSelf;
                angleDeg = candidateAngleDeg;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// 돌입: 기본 이동 속도로 슬롯(또는 목표)으로 이동하는 의도를 만든다.
        /// Evaluate 가 근거리 모드일 때, ApproachOutOfView 가 시야 밖 목적지를 못 찾았을 때 호출한다.
        /// 저장한 시야 밖 목적지는 지워서 다시 원거리가 되면 새로 고르게 한다.
        /// 슬롯이 없으면 정지 거리 0 으로 목표 자체를 향한다. 사거리 앞에서 미리 감속하지 않고 전속으로 다가가 Attack 진입 때 선다.
        /// </summary>
        /// <param name="ctx">판단 입력. 군중 지시와 유형별 수치를 쓴다</param>
        /// <param name="distanceToTarget">목표까지 수평 거리 (m). 슬롯이 없을 때 도착 판정에 쓴다</param>
        /// <returns>도착했으면 Engaged, 아니면 Approach 의도</returns>
        private MoveIntent RushToTarget(in MovementContext ctx, float distanceToTarget)
        {
            var dir = ctx.Directive;
            var t = ctx.Tuning;
            ClearFlankDestination();
            Vector3 dest;
            float stop;
            if (dir.Kind == DirectiveKind.Engage && dir.HasSlot)
            {
                dest = dir.HasSlotPosition ? dir.SlotPosition
                    : WaitOrbit.PointOnRing(ctx.TargetPosition, dir.SlotAngleDeg, t.AttackRange * 0.8f);
                stop = dir.HasSlotPosition ? Mathf.Min(0.2f, dir.SlotArrivalDistance * 0.5f) : 0.2f;
            }
            else
            {
                dest = ctx.TargetPosition;
                stop = 0f;
            }
            bool arrived = dir.HasSlotPosition
                ? WaitOrbit.HorizontalDistance(ctx.SelfPosition, dest) <= dir.SlotArrivalDistance : distanceToTarget <= t.AttackRange;
            return new MoveIntent(arrived ? MovementState.Engaged : MovementState.Approach,
                                  dest, t.MoveSpeed, stop);
        }

        /// <summary>저장한 시야 밖 목적지를 지운다. 목표 상실·Wait 지시·돌입 때 호출해 다음 원거리 판단에서 새로 고르게 한다</summary>
        private void ClearFlankDestination() => hasFlankDestination = false;

        /// <summary>y 를 0 으로 만든 수평 벡터</summary>
        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
