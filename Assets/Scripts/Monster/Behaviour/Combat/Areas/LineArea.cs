using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>정면으로 뻗은 직사각형 (길이 Length, 폭 Width)</summary>
    public sealed class LineArea : IHitArea
    {
        public float Length { get; }
        public float Width { get; }
        public LineArea(float length, float width) { Length = length; Width = width; }

        public bool Contains(Vector3 origin, Vector3 forward, Vector3 point)
        {
            Vector3 dir = HitAreas.FlatDirection(forward);
            Vector3 toPoint = HitAreas.Flat(point - origin);
            float along = Vector3.Dot(toPoint, dir);
            if (along < 0f || along > Length) return false;
            Vector3 lateral = toPoint - dir * along;
            return lateral.sqrMagnitude <= Width * Width * 0.25f;
        }
    }
}
