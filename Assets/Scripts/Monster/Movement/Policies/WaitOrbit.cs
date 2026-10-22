using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>대기 링 정지/선회 공용 계산. Melee·Flank 등 여러 정책이 재사용한다</summary>
    internal static class WaitOrbit
    {
        /// <summary>링 밖 허용 오차 (m). 이보다 멀면 WaitPoint 로 먼저 이동</summary>
        private const float RingTolerance = 1.5f;
        /// <summary>선회 목표점을 현재 방위보다 앞서 잡는 시간 (s)</summary>
        private const float OrbitLookaheadSeconds = 0.6f;

        /// <summary>목표 기준 self 의 방위각 (도, atan2(x, z), 0~360)</summary>
        public static float BearingDeg(Vector3 target, Vector3 self)
        {
            float deg = Mathf.Atan2(self.x - target.x, self.z - target.z) * Mathf.Rad2Deg;
            if (deg < 0f) deg += 360f;
            return deg;
        }

        /// <summary>목표 중심 반경 radius, 방위 angleDeg 의 지점 (y = target.y)</summary>
        public static Vector3 PointOnRing(Vector3 target, float angleDeg, float radius)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(target.x + Mathf.Sin(rad) * radius, target.y, target.z + Mathf.Cos(rad) * radius);
        }

        /// <summary>수평(XZ) 거리 (m)</summary>
        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Wait 지시를 받은 개체의 이동 의도.
        /// 슬롯 모드면 배정된 슬롯으로 가서 도착 시 Waiting. 토큰 모드면 대기 링에서 정지(Waiting) 또는 선회(Orbiting)
        /// </summary>
        public static MoveIntent Evaluate(in MovementContext ctx, bool orbiting)
        {
            var t = ctx.Tuning;

            // 슬롯 모드: 슬롯이 없으면 제자리 대기, 있으면 슬롯으로 이동
            if (ctx.Directive.RequiresSlot)
            {
                if (!ctx.Directive.HasSlotPosition) return MoveIntent.Stop(MovementState.Waiting);
                bool arrived = HorizontalDistance(ctx.SelfPosition, ctx.Directive.SlotPosition) <= ctx.Directive.SlotArrivalDistance;
                return new MoveIntent(arrived ? MovementState.Waiting : MovementState.Approach,
                    ctx.Directive.SlotPosition, t.MoveSpeed, Mathf.Min(0.2f, ctx.Directive.SlotArrivalDistance * 0.5f));
            }
            float d = HorizontalDistance(ctx.SelfPosition, ctx.TargetPosition);
            float ringRadius = HorizontalDistance(ctx.Directive.WaitPoint, ctx.TargetPosition);

            // 링에서 멀면 먼저 대기 지점으로 이동
            if (d > ringRadius + RingTolerance)
                return new MoveIntent(MovementState.Approach, ctx.Directive.WaitPoint, t.MoveSpeed, 0.3f);

            // 선회: 현재 방위보다 조금 앞선 링 위 지점을 목적지로 잡아 링을 따라 돈다
            if (orbiting)
            {
                int sign = ctx.Directive.OrbitSign != 0 ? ctx.Directive.OrbitSign : 1;
                float angle = BearingDeg(ctx.TargetPosition, ctx.SelfPosition) + sign * t.OrbitAngularSpeedDeg * OrbitLookaheadSeconds;
                Vector3 dest = PointOnRing(ctx.TargetPosition, angle, ringRadius);
                float arcSpeed = t.OrbitAngularSpeedDeg * Mathf.Deg2Rad * ringRadius * 1.2f;
                float speed = Mathf.Max(0.5f, Mathf.Min(t.MoveSpeed, arcSpeed));
                return new MoveIntent(MovementState.Orbiting, dest, speed, 0f);
            }

            return new MoveIntent(MovementState.Waiting, ctx.Directive.WaitPoint, t.MoveSpeed, 0.3f);
        }
    }
}
