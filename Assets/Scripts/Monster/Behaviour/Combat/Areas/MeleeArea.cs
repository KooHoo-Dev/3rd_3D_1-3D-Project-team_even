using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>공격자 중심 반경 (근접)</summary>
    public sealed class MeleeArea : IHitArea
    {
        public float Range { get; }
        /// <summary>HitAreas.From 이 Melee 모양 공격에 만든다</summary>
        /// <param name="range">공격자 중심 반경 (m)</param>
        public MeleeArea(float range) { Range = range; }

        /// <summary>공격자 중심에서 수평 거리 ≤ Range 면 안이다. 정면 방향은 쓰지 않는다</summary>
        public bool Contains(Vector3 origin, Vector3 forward, Vector3 point)
            => HitAreas.Flat(point - origin).magnitude <= Range;
    }
}
