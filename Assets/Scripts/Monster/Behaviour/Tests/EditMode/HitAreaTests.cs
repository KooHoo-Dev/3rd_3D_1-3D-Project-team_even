using System;
using HybridShooter.Core.Data;
using NUnit.Framework;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour.Tests
{
    public sealed class HitAreaTests
    {
        private static readonly Vector3 Origin = Vector3.zero;
        private static readonly Vector3 Fwd = Vector3.forward;

        [Test]
        public void MeleeIsRadiusAroundSelfIgnoringHeight()
        {
            var area = new MeleeArea(1.5f);
            Assert.IsTrue(area.Contains(Origin, Fwd, new Vector3(0f, 3f, 1.5f)));
            Assert.IsTrue(area.Contains(Origin, Fwd, new Vector3(0f, 0f, -1.4f)), "뒤쪽도 근접 반경 안");
            Assert.IsFalse(area.Contains(Origin, Fwd, new Vector3(1.2f, 0f, 1.2f)));
        }

        [Test]
        public void CircleCenterIsOffsetForward()
        {
            var area = new CircleArea(8f, 2f);
            Assert.IsTrue(area.Contains(Origin, Fwd, new Vector3(0f, 0f, 8f)));
            Assert.IsTrue(area.Contains(Origin, Fwd, new Vector3(1.9f, 0f, 8f)));
            Assert.IsFalse(area.Contains(Origin, Fwd, Vector3.zero));
            Assert.IsFalse(area.Contains(Origin, Fwd, new Vector3(0f, 0f, 10.5f)));
        }

        [Test]
        public void CircleWithZeroOffsetIsAroundSelf()
        {
            var area = new CircleArea(0f, 10f);
            Assert.IsTrue(area.Contains(Origin, Fwd, new Vector3(0f, 0f, -9f)));
        }

        [Test]
        public void ConeUsesHalfAngle()
        {
            var area = new ConeArea(5f, 100f);
            Assert.IsTrue(area.Contains(Origin, Fwd, AtYaw(49f) * 4f));
            Assert.IsFalse(area.Contains(Origin, Fwd, AtYaw(51f) * 4f));
            Assert.IsFalse(area.Contains(Origin, Fwd, new Vector3(0f, 0f, 5.1f)));
            Assert.IsTrue(area.Contains(Origin, Fwd, Origin), "자기 위치는 범위 안");
        }

        [Test]
        public void LineIsForwardRectangle()
        {
            var area = new LineArea(15f, 2.5f);
            Assert.IsTrue(area.Contains(Origin, Fwd, new Vector3(1.2f, 0f, 14f)));
            Assert.IsFalse(area.Contains(Origin, Fwd, new Vector3(1.3f, 0f, 5f)), "폭 밖");
            Assert.IsFalse(area.Contains(Origin, Fwd, new Vector3(0f, 0f, -0.5f)), "뒤쪽");
            Assert.IsFalse(area.Contains(Origin, Fwd, new Vector3(0f, 0f, 15.5f)), "길이 밖");
        }

        [Test]
        public void AreasFollowForwardDirection()
        {
            var area = new LineArea(5f, 1f);
            Assert.IsTrue(area.Contains(Origin, Vector3.right, new Vector3(4f, 0f, 0f)));
            Assert.IsFalse(area.Contains(Origin, Vector3.right, new Vector3(0f, 0f, 4f)));
        }

        [Test]
        public void FactoryMapsShapes()
        {
            Assert.IsInstanceOf<MeleeArea>(HitAreas.From(Spec(AttackShape.Melee)));
            Assert.IsInstanceOf<CircleArea>(HitAreas.From(Spec(AttackShape.Circle)));
            Assert.IsInstanceOf<ConeArea>(HitAreas.From(Spec(AttackShape.Cone)));
            Assert.IsInstanceOf<LineArea>(HitAreas.From(Spec(AttackShape.Line)));
            Assert.Throws<NotSupportedException>(() => HitAreas.From(Spec(AttackShape.Projectile)));
        }

        [Test]
        public void VerticalForwardThrows()
        {
            Assert.Throws<ArgumentException>(() => new LineArea(5f, 1f).Contains(Origin, Vector3.up, Fwd));
        }

        /// <summary>정면(+Z)에서 오른쪽으로 deg 만큼 돌린 수평 단위 벡터</summary>
        private static Vector3 AtYaw(float deg)
        {
            float rad = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }

        private static AttackSpec Spec(AttackShape shape) =>
            new AttackSpec(shape, 1, 1, 0f, 0f, 0f, 2f, radius: 1f, angle: 90f, width: 1f);
    }
}
