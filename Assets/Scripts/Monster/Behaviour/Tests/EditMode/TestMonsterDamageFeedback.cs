using NUnit.Framework;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// 피격 모션 방향 고르기(MonsterDamageFeedback.ToHitSide)와 약점 배율(MonsterWeakPoint.ScaleDamage) 순수 계산을 확인한다.
    /// 방향은 "피해가 들어온 방향(공격자 → 몬스터)"의 몬스터 로컬 좌표다.
    /// 약점 발광 셰이더가 Resources 에서 읽히고 에러 없이 컴파일되는지도 확인한다.
    /// </summary>
    public sealed class TestMonsterDamageFeedback
    {
        [Test]
        public void ToHitSide_BulletTravelingBackward_IsFront()
        {
            Assert.AreEqual(HitSide.Front, MonsterDamageFeedback.ToHitSide(Vector3.back));
        }

        [Test]
        public void ToHitSide_BulletTravelingForward_IsBack()
        {
            Assert.AreEqual(HitSide.Back, MonsterDamageFeedback.ToHitSide(Vector3.forward));
        }

        [Test]
        public void ToHitSide_BulletTravelingRight_IsLeft()
        {
            Assert.AreEqual(HitSide.Left, MonsterDamageFeedback.ToHitSide(Vector3.right));
        }

        [Test]
        public void ToHitSide_BulletTravelingLeft_IsRight()
        {
            Assert.AreEqual(HitSide.Right, MonsterDamageFeedback.ToHitSide(Vector3.left));
        }

        [Test]
        public void ToHitSide_DiagonalPicksLargerComponent()
        {
            Assert.AreEqual(HitSide.Left, MonsterDamageFeedback.ToHitSide(new Vector3(0.8f, 0f, -0.6f)));
            Assert.AreEqual(HitSide.Front, MonsterDamageFeedback.ToHitSide(new Vector3(0.6f, 0f, -0.8f)));
        }

        [Test]
        public void ToHitSide_ZeroOrVertical_IsFront()
        {
            Assert.AreEqual(HitSide.Front, MonsterDamageFeedback.ToHitSide(Vector3.zero));
            Assert.AreEqual(HitSide.Front, MonsterDamageFeedback.ToHitSide(Vector3.down));
        }

        [Test]
        public void ScaleDamage_MultipliesAndRounds()
        {
            Assert.AreEqual(20, MonsterWeakPoint.ScaleDamage(10, 2f));
            Assert.AreEqual(15, MonsterWeakPoint.ScaleDamage(10, 1.5f));
            Assert.AreEqual(11, MonsterWeakPoint.ScaleDamage(7, 1.5f)); // 10.5 → .5 올림
        }

        [Test]
        public void WeakPointGlowShader_LoadsFromResourcesAndCompiles()
        {
            var shader = Resources.Load<Shader>("Shaders/MonsterWeakPointGlow");
            Assert.IsNotNull(shader);
            Assert.AreEqual("HybridShooter/Monster/WeakPointGlow", shader.name);
            Assert.IsFalse(UnityEditor.ShaderUtil.ShaderHasError(shader));
            Assert.IsTrue(shader.isSupported);
        }
    }
}
