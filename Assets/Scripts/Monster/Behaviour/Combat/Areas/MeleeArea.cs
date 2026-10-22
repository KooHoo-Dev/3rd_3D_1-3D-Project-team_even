using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>공격자 중심 반경 (근접)</summary>
    public sealed class MeleeArea : IHitArea
    {
        public float Range { get; }
        public MeleeArea(float range) { Range = range; }

        public bool Contains(Vector3 origin, Vector3 forward, Vector3 point)
            => HitAreas.Flat(point - origin).sqrMagnitude <= Range * Range;
    }
}
