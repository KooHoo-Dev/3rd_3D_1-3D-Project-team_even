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

        // 개체별 난수 (선회 여부, 정면 우회 시 측면 선택용)
        private readonly System.Random rng;

        // ── 대기(Wait) 관련 ──
        private DirectiveKind lastKind; // 직전 지시 종류. Wait 진입 순간 감지용
        private bool orbiting;          // 대기 중 선회 여부 (진입 시 1회 결정)

        // ── 측면 우회 관련 ──
        private int side;                  // 우회 중 유지하는 측면 (+1/-1), 0 = 우회 아님
        private bool committedToApproach;  // 우회를 끝내고 돌입을 확정했나 (가까워질 때까지 우회로 되돌아가지 않기 위함)

        public FlankPolicy(int seed)
        {
            rng = new System.Random(seed);
        }

        /// <summary>
        /// 목표 무효 → 정지. 슬롯 대기 → 제자리 대기. Wait 지시 → 근접형과 같은 대기/선회.
        /// 그 외 → 목표 시야 안이고 멀면 시야 밖 측면 지점으로 우회, 아니면 배율 속도로 돌입
        /// </summary>
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
                // 1) 목표 시야 안(원뿔)에 있는지 판정
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

                // 2) 우회 여부: 시야 안 + 멀리 있음 + 돌입 확정 전이면 우회, 그 외는 돌입
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

        /// <summary>y 를 0 으로 만든 수평 벡터</summary>
        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
