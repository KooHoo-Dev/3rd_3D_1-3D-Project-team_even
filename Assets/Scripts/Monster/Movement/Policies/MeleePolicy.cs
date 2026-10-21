using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>기본 근접형: Engage 면 목표(또는 슬롯)로, Wait 면 대기 링에서 정지/선회</summary>
    public sealed class MeleePolicy : IMovementPolicy
    {
        private readonly System.Random rng;
        private DirectiveKind lastKind;
        private bool orbiting;

        public MeleePolicy(int seed)
        {
            rng = new System.Random(seed);
        }

        public MoveIntent Evaluate(in MovementContext ctx)
        {
            var dir = ctx.Directive;

            if (!ctx.TargetValid)
            {
                lastKind = dir.Kind;
                return MoveIntent.Stop(MovementState.Idle);
            }

            var t = ctx.Tuning;
            if (dir.RequiresSlot && !dir.HasSlotPosition)
                return MoveIntent.Stop(MovementState.Waiting);
            MoveIntent intent;

            if (dir.Kind == DirectiveKind.Wait)
            {
                if (lastKind != DirectiveKind.Wait)
                    orbiting = rng.NextDouble() < t.OrbitChance; // 대기 진입 시 1회만 선택
                intent = WaitOrbit.Evaluate(in ctx, orbiting);
            }
            else
            {
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
                bool inRange = dir.HasSlotPosition
                    ? WaitOrbit.HorizontalDistance(ctx.SelfPosition, dest) <= dir.SlotArrivalDistance
                    : WaitOrbit.HorizontalDistance(ctx.SelfPosition, ctx.TargetPosition) <= t.AttackRange;
                intent = new MoveIntent(inRange ? MovementState.Engaged : MovementState.Approach, dest, t.MoveSpeed, stop);
            }

            lastKind = dir.Kind;
            return intent;
        }
    }
}
