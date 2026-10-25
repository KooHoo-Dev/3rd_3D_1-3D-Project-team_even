using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 측면 침투형: 목표 시야 안에서 멀리 있으면 시야 원뿔 밖 측면 지점으로 우회(detour)하고,
    /// 시야 밖이거나 가까우면 배율 속도로 목표(또는 슬롯)로 돌입(rush)한다. Wait 는 근접형과 동일.
    /// </summary>
    public sealed class FlankPolicy : IMovementPolicy
    {
        // 우회 지점을 시야 반각 바깥으로 더 벌려 잡는 여유 (도)
        private const float BearingMarginDeg = 30f;

        // 개체별 난수 (선회 여부, 정면 우회 시 측면 선택용)
        private readonly System.Random rng;

        // ── 대기(Wait) 관련 ──
        private DirectiveKind lastKind; // 직전 지시 종류. Wait 진입 순간 감지용
        private bool orbiting;          // 대기 중 선회 여부 (진입 시 1회 결정)

        // ── 측면 우회 관련 ──
        private int side;                  // 우회 중 유지하는 측면 (+1/-1), 0 = 우회 아님
        private bool committedToApproach;  // 우회를 끝내고 돌입을 확정했나 (가까워질 때까지 우회로 되돌아가지 않기 위함)

        /// <summary>개체별 난수를 준비한다. MovementPolicyFactory 가 개체마다 하나씩 만든다</summary>
        /// <param name="seed">난수 시드 (개체 InstanceID)</param>
        public FlankPolicy(int seed)
        {
            rng = new System.Random(seed);
        }

        /// <summary>
        /// 목표 무효 → 정지. 슬롯 대기 → 제자리 대기. Wait 지시 → 근접형과 같은 대기/선회.
        /// 그 외 → 목표 시야 안이고 멀면(공격 사거리 2배 초과) 시야 밖 측면 지점으로 우회, 아니면 배율 속도로 돌입
        /// </summary>
        /// <param name="ctx">자기·목표 위치, 군중 지시, 유형별 수치를 담은 판단 입력</param>
        /// <returns>이번 판단의 이동 의도</returns>
        public MoveIntent Evaluate(in MovementContext ctx)
        {
            var dir = ctx.Directive;

            if (ctx.TargetValid == false)
            {
                side = 0;
                committedToApproach = false;
                lastKind = dir.Kind;
                return MoveIntent.Stop(MovementState.Idle);
            }

            var t = ctx.Tuning;
            if (dir.RequiresSlot && dir.HasSlotPosition == false)
                return MoveIntent.Stop(MovementState.Waiting);
            MoveIntent intent;

            if (dir.Kind == DirectiveKind.Wait)
            {
                side = 0;
                committedToApproach = false;
                if (lastKind != DirectiveKind.Wait)
                    orbiting = rng.NextDouble() < t.OrbitChance; // 대기 진입 시 1회만 선택
                intent = WaitOrbit.Evaluate(in ctx, orbiting);
            }
            else
            {
                // 1) 목표 시야 안(원뿔)에 있는지 판정
                float d = WaitOrbit.HorizontalDistance(ctx.SelfPosition, ctx.TargetPosition);

                Vector3 fwd = Flat(ctx.TargetForward);
                if (fwd.magnitude < 1e-4f) fwd = Vector3.forward;
                fwd.Normalize();
                Vector3 toSelf = Flat(ctx.SelfPosition - ctx.TargetPosition);
                bool inView = false;
                if (d > 1e-4f)
                {
                    toSelf.Normalize();
                    inView = Vector3.Angle(fwd, toSelf) <= t.FlankViewHalfAngleDeg;
                }

                // 2) 우회 여부: 시야 안 + 멀리 있음 + 돌입 확정 전이면 우회, 그 외는 돌입
                if (d <= t.AttackRange * 2f) committedToApproach = false;
                bool isFar = d > t.AttackRange * 2f;
                bool approachNotCommitted = committedToApproach == false;
                if (inView && isFar && approachNotCommitted)
                    intent = DetourToFlank(in ctx, fwd, toSelf);
                else
                    intent = RushToTarget(in ctx, d);
            }

            lastKind = dir.Kind;
            return intent;
        }

        /// <summary>
        /// 우회: 목표 시야 원뿔 밖 측면 링 위 지점으로 이동하는 의도를 만든다. Evaluate 가 시야 안 + 멀리 있음 + 돌입 확정 전일 때 호출한다.
        /// 측면은 우회 시작 시 한 번 정하고 우회가 끝날 때까지 유지한다.
        /// </summary>
        /// <param name="ctx">판단 입력. 목표 위치와 유형별 수치를 쓴다</param>
        /// <param name="targetForward">목표의 수평 정면 방향 (정규화됨)</param>
        /// <param name="toSelf">목표에서 자신으로 향하는 수평 방향 (정규화됨)</param>
        /// <returns>측면 지점으로 향하는 Approach 의도</returns>
        private MoveIntent DetourToFlank(in MovementContext ctx, Vector3 targetForward, Vector3 toSelf)
        {
            var t = ctx.Tuning;
            if (side == 0) side = ChooseDetourSide(targetForward, toSelf);
            float fwdBearing = WaitOrbit.BearingDeg(ctx.TargetPosition, ctx.TargetPosition + targetForward);
            float bearing = fwdBearing + side * (t.FlankViewHalfAngleDeg + BearingMarginDeg);
            Vector3 dest = WaitOrbit.PointOnRing(ctx.TargetPosition, bearing, t.FlankRadius);
            return new MoveIntent(MovementState.Approach, dest, t.MoveSpeed, 0.5f);
        }

        /// <summary>
        /// 우회 측면을 정한다. 자신이 목표 정면의 어느 쪽에 있는지(외적 y 부호)로 고르고, 거의 정면이면 난수로 고른다. DetourToFlank 가 우회 시작 시 호출한다.
        /// </summary>
        /// <param name="targetForward">목표의 수평 정면 방향 (정규화됨)</param>
        /// <param name="toSelf">목표에서 자신으로 향하는 수평 방향 (정규화됨)</param>
        /// <returns>+1 또는 -1</returns>
        private int ChooseDetourSide(Vector3 targetForward, Vector3 toSelf)
        {
            float cy = targetForward.z * toSelf.x - targetForward.x * toSelf.z; // (fwd × toSelf).y
            bool isStraightAhead = Mathf.Abs(cy) < 1e-4f;
            if (isStraightAhead) return rng.NextDouble() < 0.5 ? 1 : -1;
            if (cy > 0f) return 1;
            return -1;
        }

        /// <summary>
        /// 돌입: 배율 속도로 슬롯(또는 목표)으로 이동하는 의도를 만든다. Evaluate 가 우회 조건이 아닐 때 호출한다.
        /// 우회 중이었다면 돌입을 확정해 가까워질 때까지 우회로 되돌아가지 않게 한다.
        /// </summary>
        /// <param name="ctx">판단 입력. 군중 지시와 유형별 수치를 쓴다</param>
        /// <param name="distanceToTarget">목표까지 수평 거리 (m). 슬롯이 없을 때 도착 판정에 쓴다</param>
        /// <returns>도착했으면 Engaged, 아니면 Approach 의도</returns>
        private MoveIntent RushToTarget(in MovementContext ctx, float distanceToTarget)
        {
            var dir = ctx.Directive;
            var t = ctx.Tuning;
            if (side != 0) committedToApproach = true;
            side = 0;
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
                stop = t.AttackRange * 0.9f;
            }
            bool arrived = dir.HasSlotPosition
                ? WaitOrbit.HorizontalDistance(ctx.SelfPosition, dest) <= dir.SlotArrivalDistance : distanceToTarget <= t.AttackRange;
            return new MoveIntent(arrived ? MovementState.Engaged : MovementState.Approach,
                                  dest, t.MoveSpeed * t.FlankSpeedMultiplier, stop);
        }

        /// <summary>y 를 0 으로 만든 수평 벡터</summary>
        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
