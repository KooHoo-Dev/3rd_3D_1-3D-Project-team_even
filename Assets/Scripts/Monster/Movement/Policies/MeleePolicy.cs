using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>기본 근접형: Engage 면 목표(또는 슬롯)로, Wait 면 대기 링에서 정지/선회</summary>
    public sealed class MeleePolicy : IMovementPolicy
    {
        // 개체별 난수 (선회 여부 선택용, 시드는 개체 InstanceID)
        private readonly System.Random rng;
        // 직전 판단의 지시 종류. Wait 진입 순간을 감지해 선회 여부를 1회만 정하는 데 쓴다
        private DirectiveKind lastKind;
        // 현재 대기 중 선회할지 여부
        private bool orbiting;

        /// <summary>개체별 난수를 준비한다. MovementPolicyFactory 가 개체마다 하나씩 만든다</summary>
        /// <param name="seed">난수 시드 (개체 InstanceID)</param>
        public MeleePolicy(int seed)
        {
            rng = new System.Random(seed);
        }

        /// <summary>
        /// 목표 무효 → 정지. 슬롯 대기 → 제자리 대기. Wait 지시 → 대기 위치에서 정지/선회.
        /// 그 외(Engage/Unrestricted) → 슬롯 또는 목표로 이동하고 도착하면 Engaged
        /// </summary>
        /// <param name="ctx">자기·목표 위치, 군중 지시, 유형별 수치를 담은 판단 입력</param>
        /// <returns>이번 판단의 이동 의도</returns>
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
                // 목적지 결정: 슬롯 위치 > 슬롯 방위 지점 > 목표 본인 순
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
                // 도착 판정: 슬롯이 있으면 슬롯 도착 거리, 없으면 공격 사거리
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
