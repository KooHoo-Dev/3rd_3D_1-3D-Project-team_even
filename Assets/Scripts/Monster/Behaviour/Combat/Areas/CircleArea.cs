using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>정면 Offset 거리에 중심을 둔 원 (Offset 0 = 공격자 중심)</summary>
    public sealed class CircleArea : IHitArea
    {
        public float Offset { get; }
        public float Radius { get; }
        public CircleArea(float offset, float radius) { Offset = offset; Radius = radius; }

        public bool Contains(Vector3 origin, Vector3 forward, Vector3 point)
        {
            Vector3 center = origin + HitAreas.FlatDirection(forward) * Offset;
            return HitAreas.Flat(point - center).sqrMagnitude <= Radius * Radius;
        }
    }
}
