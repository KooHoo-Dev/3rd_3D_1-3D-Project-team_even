using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>대기 링 정지/선회 공용 계산. Melee·Flank 등 여러 정책이 재사용한다</summary>
    internal static class WaitOrbit
    {
        // 링 밖 허용 오차 (m). 이보다 멀면 WaitPoint 로 먼저 이동
        private const float RingTolerance = 1.5f;
        // 선회 목표점을 현재 방위보다 앞서 잡는 시간 (s)
        private const float OrbitLookaheadSeconds = 0.6f;

        /// <summary>목표 기준 self 의 방위각을 구한다. 선회·측면 우회 지점 계산에 쓴다</summary>
        /// <param name="target">기준점 (목표 위치)</param>
        /// <param name="self">방위를 잴 위치</param>
        /// <returns>방위각 (도, atan2(x, z) 기준, 0~360)</returns>
        public static float BearingDeg(Vector3 target, Vector3 self)
        {
            float deg = Mathf.Atan2(self.x - target.x, self.z - target.z) * Mathf.Rad2Deg;
            if (deg < 0f) deg += 360f;
            return deg;
        }

        /// <summary>목표 중심 링 위의 지점을 구한다. 선회 목적지·측면 우회 지점·슬롯 방위 지점에 쓴다</summary>
        /// <param name="target">링 중심 (목표 위치)</param>
        /// <param name="angleDeg">방위각 (도, atan2(x, z) 기준)</param>
        /// <param name="radius">링 반경 (m)</param>
        /// <returns>링 위 지점 (월드, y = target.y)</returns>
        public static Vector3 PointOnRing(Vector3 target, float angleDeg, float radius)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(target.x + Mathf.Sin(rad) * radius, target.y, target.z + Mathf.Cos(rad) * radius);
        }

        /// <summary>높이를 무시한 수평(XZ) 거리. 도착·사거리·링 판정에 쓴다</summary>
        /// <param name="a">첫 위치 (월드)</param>
        /// <param name="b">둘째 위치 (월드)</param>
        /// <returns>수평 거리 (m)</returns>
        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Wait 지시를 받은 개체의 이동 의도.
        /// 슬롯 모드면 배정된 슬롯으로 가서 도착 시 Waiting. 토큰 모드면 대기 링에서 정지(Waiting) 또는 선회(Orbiting).
        /// MeleePolicy·FlankPolicy 가 Wait 지시를 받았을 때 호출한다.
        /// </summary>
        /// <param name="ctx">판단 입력. Directive 의 WaitPoint·OrbitSign·슬롯 정보를 쓴다</param>
        /// <param name="orbiting">true = 선회, false = 정지 (정책이 대기 진입 시 1회 정한 값)</param>
        /// <returns>대기 이동 의도. 링에서 RingTolerance 이상 멀면 먼저 WaitPoint 로 Approach</returns>
        public static MoveIntent Evaluate(in MovementContext ctx, bool orbiting)
        {
            var t = ctx.Tuning;

            // 슬롯 모드: 슬롯이 없으면 제자리 대기, 있으면 슬롯으로 이동
            if (ctx.Directive.RequiresSlot)
            {
                if (ctx.Directive.HasSlotPosition == false) return MoveIntent.Stop(MovementState.Waiting);
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
