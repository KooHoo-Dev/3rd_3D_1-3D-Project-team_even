using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>정면 Offset 거리에 중심을 둔 원 (Offset 0 = 공격자 중심)</summary>
    public sealed class CircleArea : IHitArea
    {
        public float Offset { get; }
        public float Radius { get; }
        /// <summary>HitAreas.From 이 Circle 모양 공격에 만든다</summary>
        /// <param name="offset">공격자에서 원 중심까지 정면 거리 (m). 0 = 공격자 중심</param>
        /// <param name="radius">원 반지름 (m)</param>
        public CircleArea(float offset, float radius) { Offset = offset; Radius = radius; }

        /// <summary>정면 Offset 지점을 중심으로 수평 거리 ≤ Radius 면 안이다</summary>
        public bool Contains(Vector3 origin, Vector3 forward, Vector3 point)
        {
            Vector3 center = origin + HitAreas.FlatDirection(forward) * Offset;
            return HitAreas.Flat(point - center).magnitude <= Radius;
        }
    }
}
