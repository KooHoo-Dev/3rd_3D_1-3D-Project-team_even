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
        /// <summary>
        /// point 가 이 범위 안인지 판정한다. 공격 전략(StrikeAttack 등)이 판정 순간에 대상 위치로 호출한다.
        /// </summary>
        /// <param name="origin">공격자 위치</param>
        /// <param name="forward">공격자 정면 (수평 성분만 사용)</param>
        /// <param name="point">판정할 위치</param>
        /// <returns>범위 안이면 true (높이 무시)</returns>
        bool Contains(Vector3 origin, Vector3 forward, Vector3 point);
    }

    /// <summary>
    /// 판정 범위 생성과 범위 계산 공용 도우미. 범위 클래스들이 수평(XZ) 계산을 같은 방식으로 하게 한다.
    /// </summary>
    public static class HitAreas
    {
        /// <summary>
        /// AttackSpec.Shape 에 맞는 범위. 공격 전략이 생성될 때 1번 만든다.
        /// Projectile 은 범위가 아니라 전달 방식이라 지원하지 않는다.
        /// </summary>
        /// <param name="spec">공격 값 묶음. Shape 와 Range / Radius / Angle / Width 를 읽는다</param>
        /// <returns>Shape 에 맞는 판정 범위</returns>
        /// <exception cref="NotSupportedException">Melee / Circle / Cone / Line 이 아닌 Shape 일 때</exception>
        public static IHitArea From(in AttackSpec spec) => spec.Shape switch
        {
            AttackShape.Melee => new MeleeArea(spec.Range),
            AttackShape.Circle => new CircleArea(spec.Range, spec.Radius),
            AttackShape.Cone => new ConeArea(spec.Range, spec.Angle),
            AttackShape.Line => new LineArea(spec.Range, spec.Width),
            _ => throw new NotSupportedException($"{spec.Shape} 는 판정 범위로 만들 수 없다 (SourceDefId {spec.SourceDefId})"),
        };

        /// <summary>높이(y)를 버리고 수평 성분만 남긴 벡터를 돌려준다</summary>
        /// <param name="v">원본 벡터</param>
        /// <returns>y = 0 인 벡터</returns>
        internal static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>공격자 정면을 수평 단위 벡터로 만든다. 방향이 필요한 범위(Circle / Cone / Line)가 쓴다</summary>
        /// <param name="forward">공격자 정면</param>
        /// <returns>수평 정면 단위 벡터</returns>
        /// <exception cref="ArgumentException">forward 의 수평 성분이 0 일 때</exception>
        internal static Vector3 FlatDirection(Vector3 forward)
        {
            Vector3 flat = Flat(forward);
            if (flat.sqrMagnitude < 1e-8f) throw new ArgumentException("forward 의 수평 성분이 0 이다", nameof(forward));
            return flat.normalized;
        }
    }
}
