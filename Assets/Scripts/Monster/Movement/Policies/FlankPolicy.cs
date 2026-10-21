using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 측면 침투형: 목표 시야 안에서 멀리 있으면 시야 원뿔 밖 측면 지점으로 우회(detour)하고,
    /// 시야 밖이거나 가까우면 배율 속도로 목표(또는 슬롯)로 돌입(rush)한다. Wait 는 근접형과 동일.
    /// </summary>
    public sealed class FlankPolicy : IMovementPolicy
    {
        /// <summary>시야 반각 바깥으로 더 벌려 잡는 여유 (도)</summary>
        private const float BearingMarginDeg = 30f;

        private readonly System.Random rng;
        private DirectiveKind lastKind;
        private bool orbiting;
        private int side; // 우회 중 유지하는 측면 (+1/-1), 0 = 우회 아님
        private bool committedToApproach;

        public FlankPolicy(int seed)
        {
            rng = new System.Random(seed);
        }

        public MoveIntent Evaluate(in MovementContext ctx)
        {
            var dir = ctx.Directive;

            if (!ctx.TargetValid)
            {
                side = 0;
                committedToApproach = false;
                lastKind = dir.Kind;
                return MoveIntent.Stop(MovementState.Idle);
            }

            var t = ctx.Tuning;
            if (dir.RequiresSlot && !dir.HasSlotPosition)
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
                float d = WaitOrbit.HorizontalDistance(ctx.SelfPosition, ctx.TargetPosition);

                Vector3 fwd = Flat(ctx.TargetForward);
                if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.forward;
                fwd.Normalize();
                Vector3 toSelf = Flat(ctx.SelfPosition - ctx.TargetPosition);
                bool inView = false;
                if (d > 1e-4f)
                {
                    toSelf.Normalize();
                    inView = Vector3.Angle(fwd, toSelf) <= t.FlankViewHalfAngleDeg;
                }

                if (d <= t.AttackRange * 2f) committedToApproach = false;
                if (inView && d > t.AttackRange * 2f && !committedToApproach)
                {
                    // 우회: 시작 시 측면 결정, 끝날 때까지 유지
                    if (side == 0)
                    {
                        float cy = fwd.z * toSelf.x - fwd.x * toSelf.z; // (fwd × toSelf).y
                        side = Mathf.Abs(cy) < 1e-4f ? (rng.NextDouble() < 0.5 ? 1 : -1) : (cy > 0f ? 1 : -1);
                    }
                    float fwdBearing = WaitOrbit.BearingDeg(ctx.TargetPosition, ctx.TargetPosition + fwd);
                    float bearing = fwdBearing + side * (t.FlankViewHalfAngleDeg + BearingMarginDeg);
                    Vector3 dest = WaitOrbit.PointOnRing(ctx.TargetPosition, bearing, t.FlankRadius);
                    intent = new MoveIntent(MovementState.Approach, dest, t.MoveSpeed, 0.5f);
                }
                else
                {
                    // 돌입
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
                        ? WaitOrbit.HorizontalDistance(ctx.SelfPosition, dest) <= dir.SlotArrivalDistance : d <= t.AttackRange;
                    intent = new MoveIntent(arrived ? MovementState.Engaged : MovementState.Approach,
                                            dest, t.MoveSpeed * t.FlankSpeedMultiplier, stop);
                }
            }

            lastKind = dir.Kind;
            return intent;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
