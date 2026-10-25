using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>정면으로 뻗은 직사각형 (길이 Length, 폭 Width)</summary>
    public sealed class LineArea : IHitArea
    {
        public float Length { get; }
        public float Width { get; }
        /// <summary>HitAreas.From 이 Line 모양 공격에 만든다</summary>
        /// <param name="length">정면으로 뻗은 길이 (m)</param>
        /// <param name="width">직사각형 전체 폭 (m). 정면 축 양쪽으로 절반씩</param>
        public LineArea(float length, float width) { Length = length; Width = width; }

        /// <summary>정면 축 위 거리가 0~Length 이고 축에서 옆으로 Width 절반 이내면 안이다</summary>
        public bool Contains(Vector3 origin, Vector3 forward, Vector3 point)
        {
            Vector3 dir = HitAreas.FlatDirection(forward);
            Vector3 toPoint = HitAreas.Flat(point - origin);
            float along = Vector3.Dot(toPoint, dir);
            if (along < 0f || along > Length) return false;
            Vector3 lateral = toPoint - dir * along;
            return lateral.magnitude <= Width * 0.5f;
        }
    }
}
