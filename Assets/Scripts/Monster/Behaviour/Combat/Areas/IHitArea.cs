using System;
using HybridShooter.Core.Data;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 공격 판정 범위. 모든 판정은 수평(XZ) 기준이며 높이는 무시한다.
    /// 예고 표시도 같은 객체를 써야 "표시된 범위 밖이면 맞지 않는다"가 보장된다.
    /// </summary>
    public interface IHitArea
    {
        /// <param name="origin">공격자 위치</param>
        /// <param name="forward">공격자 정면 (수평 성분만 사용)</param>
        /// <param name="point">판정할 위치</param>
        bool Contains(Vector3 origin, Vector3 forward, Vector3 point);
    }

    public static class HitAreas
    {
        /// <summary>AttackSpec.Shape 에 맞는 범위. Projectile 은 범위가 아니라 전달 방식이라 지원하지 않는다</summary>
        public static IHitArea From(in AttackSpec spec) => spec.Shape switch
        {
            AttackShape.Melee => new MeleeArea(spec.Range),
            AttackShape.Circle => new CircleArea(spec.Range, spec.Radius),
            AttackShape.Cone => new ConeArea(spec.Range, spec.Angle),
            AttackShape.Line => new LineArea(spec.Range, spec.Width),
            _ => throw new NotSupportedException($"{spec.Shape} 는 판정 범위로 만들 수 없다 (SourceDefId {spec.SourceDefId})"),
        };

        /// <summary>수평 성분만 남긴 벡터</summary>
        internal static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>수평 정면 단위 벡터. 수평 성분이 0 이면 예외</summary>
        internal static Vector3 FlatDirection(Vector3 forward)
        {
            Vector3 flat = Flat(forward);
            if (flat.sqrMagnitude < 1e-8f) throw new ArgumentException("forward 의 수평 성분이 0 이다", nameof(forward));
            return flat.normalized;
        }
    }
}
