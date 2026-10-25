using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>정면 기준 부채꼴. Angle = 전체 각도 (도)</summary>
    public sealed class ConeArea : IHitArea
    {
        public float Range { get; }
        public float Angle { get; }
        /// <summary>HitAreas.From 이 Cone 모양 공격에 만든다</summary>
        /// <param name="range">부채꼴 반지름 (m)</param>
        /// <param name="angle">부채꼴 전체 각도 (도)</param>
        public ConeArea(float range, float angle) { Range = range; Angle = angle; }

        /// <summary>수평 거리 ≤ Range 이고 정면과의 각도 ≤ Angle 절반이면 안이다. 공격자와 같은 위치면 안으로 본다</summary>
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
