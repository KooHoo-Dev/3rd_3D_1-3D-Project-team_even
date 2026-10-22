using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>정면 기준 부채꼴. Angle = 전체 각도 (도)</summary>
    public sealed class ConeArea : IHitArea
    {
        public float Range { get; }
        public float Angle { get; }
        public ConeArea(float range, float angle) { Range = range; Angle = angle; }

        public bool Contains(Vector3 origin, Vector3 forward, Vector3 point)
        {
            Vector3 toPoint = HitAreas.Flat(point - origin);
            float sqr = toPoint.sqrMagnitude;
            if (sqr > Range * Range) return false;
            if (sqr < 1e-8f) return true;
            return Vector3.Angle(HitAreas.FlatDirection(forward), toPoint) <= Angle * 0.5f;
        }
    }
}
