using NUnit.Framework;
using UnityEngine;

namespace HybridShooter.Monster.Movement.Tests
{
    /// <summary>
    /// 침투형 FlankPolicy.Evaluate 의 순수 판단: 원거리 시야 밖 목적지 선정·유지·재선정, 전환 거리와 복귀 폭, 후보 없음 시 돌입.
    /// 목표는 원점, 카메라도 원점에서 +Z 를 바라본다(별도 지정 시 제외). 지시는 슬롯 없는 Engage.
    /// </summary>
    public sealed class FlankPolicyTests
    {
        private const float MoveSpeed = 7f;
        private const float SpeedMultiplier = 1.6f;
        private const float FlankRadius = 10f;
        private const float SwitchDistance = 15f;
        private const float SwitchBand = 2f;
        private const float HalfAngle = 45f;
        private const float Tolerance = 1e-3f;

        private static readonly MovementTuning Tuning = new MovementTuning(
            MoveSpeed, 1.8f, 0.5f, 25f, 12f, 18f, FlankRadius, SpeedMultiplier, SwitchDistance, SwitchBand);

        private static readonly CrowdDirective EngageWithoutSlot =
            new CrowdDirective(DirectiveKind.Engage, Vector3.zero, 0, false, 0f);

        private static MovementContext Context(Vector3 self, Vector3 viewForward, float halfAngle = HalfAngle) =>
            new MovementContext(self, Vector3.zero, viewForward, Vector3.zero, halfAngle, true,
                                EngageWithoutSlot, Tuning, 0f, 0.1f);

        private static float ViewAngle(Vector3 viewForward, Vector3 point) =>
            Vector3.Angle(new Vector3(viewForward.x, 0f, viewForward.z), new Vector3(point.x, 0f, point.z));

        [Test]
        public void FarInViewChoosesRingPointOutsideViewWithMargin()
        {
            var policy = new FlankPolicy(1);
            MoveIntent intent = policy.Evaluate(Context(new Vector3(0f, 0f, 30f), Vector3.forward));

            Assert.AreEqual(MovementState.Approach, intent.State);
            Assert.AreEqual(FlankRadius, intent.Destination.magnitude, Tolerance);
            Assert.That(ViewAngle(Vector3.forward, intent.Destination), Is.GreaterThanOrEqualTo(HalfAngle + 30f - Tolerance));
            Assert.AreEqual(MoveSpeed * SpeedMultiplier, intent.Speed, Tolerance);
            Assert.IsTrue(policy.IsApproachingOutOfView);
        }

        [Test]
        public void FarChoosesRingPointClosestToSelf()
        {
            var policy = new FlankPolicy(1);
            MoveIntent intent = policy.Evaluate(Context(new Vector3(30f, 0f, 0f), Vector3.forward));

            Assert.That(Vector3.Distance(intent.Destination, new Vector3(FlankRadius, 0f, 0f)), Is.LessThan(Tolerance));
        }

        [Test]
        public void FarKeepsDestinationWhileOutOfView()
        {
            var policy = new FlankPolicy(1);
            Vector3 first = policy.Evaluate(Context(new Vector3(0f, 0f, 30f), Vector3.forward)).Destination;
            Vector3 second = policy.Evaluate(Context(new Vector3(5f, 0f, 28f), Vector3.forward)).Destination;

            Assert.That(Vector3.Distance(first, second), Is.LessThan(Tolerance));
        }

        [Test]
        public void FarRechoosesWhenDestinationEntersView()
        {
            var policy = new FlankPolicy(1);
            Vector3 self = new Vector3(0f, 0f, 30f);
            Vector3 first = policy.Evaluate(Context(self, Vector3.forward)).Destination;

            Vector3 lookAtFirst = first.normalized;
            Vector3 second = policy.Evaluate(Context(self, lookAtFirst)).Destination;

            Assert.That(Vector3.Distance(first, second), Is.GreaterThan(1f));
            Assert.That(ViewAngle(lookAtFirst, second), Is.GreaterThan(HalfAngle));
        }

        [Test]
        public void FarDestinationFollowsMovedTarget()
        {
            // 목적지 도착 후 목표(카메라 포함)가 20m 이동해도, 같은 링 각도로 새 목표 기준 지점을 다시 낸다 (옛 지점에 멈추지 않음)
            var policy = new FlankPolicy(1);
            Vector3 first = policy.Evaluate(Context(new Vector3(0f, 0f, 30f), Vector3.forward)).Destination;

            Vector3 movedTarget = new Vector3(0f, 0f, -20f);
            var context = new MovementContext(first, movedTarget, Vector3.forward, movedTarget, HalfAngle, true,
                                              EngageWithoutSlot, Tuning, 0f, 0.1f);
            MoveIntent intent = policy.Evaluate(context);

            Assert.AreEqual(MovementState.Approach, intent.State);
            Assert.That(Vector3.Distance(intent.Destination, movedTarget + first), Is.LessThan(Tolerance));
        }

        [Test]
        public void WithinSwitchDistanceRushesToTargetAtBaseSpeed()
        {
            var policy = new FlankPolicy(1);
            MoveIntent intent = policy.Evaluate(Context(new Vector3(0f, 0f, SwitchDistance - 1f), Vector3.forward));

            Assert.That(intent.Destination.magnitude, Is.LessThan(Tolerance));
            Assert.AreEqual(MoveSpeed, intent.Speed, Tolerance);
            Assert.IsFalse(policy.IsApproachingOutOfView);
        }

        [Test]
        public void NearRushWithoutSlotStopsAtHalfAttackRange()
        {
            var policy = new FlankPolicy(1);
            MoveIntent intent = policy.Evaluate(Context(new Vector3(0f, 0f, SwitchDistance - 1f), Vector3.forward));

            Assert.AreEqual(Tuning.AttackRange * 0.5f, intent.StopDistance, Tolerance, "플레이어 몸까지 파고들지 않고 사거리 절반에서 멈춘다");
        }

        [Test]
        public void NearStaysUntilDistanceLeavesSwitchBand()
        {
            var policy = new FlankPolicy(1);
            policy.Evaluate(Context(new Vector3(0f, 0f, SwitchDistance - 1f), Vector3.forward));

            MoveIntent insideBand = policy.Evaluate(Context(new Vector3(0f, 0f, SwitchDistance + SwitchBand - 0.5f), Vector3.forward));
            Assert.That(insideBand.Destination.magnitude, Is.LessThan(Tolerance), "복귀 폭 안에서는 계속 돌입");

            MoveIntent outsideBand = policy.Evaluate(Context(new Vector3(0f, 0f, SwitchDistance + SwitchBand + 0.5f), Vector3.forward));
            Assert.AreEqual(FlankRadius, outsideBand.Destination.magnitude, Tolerance, "복귀 폭을 넘으면 시야 밖 링 지점");
        }

        [Test]
        public void FarWithoutOutOfViewCandidateRushes()
        {
            var policy = new FlankPolicy(1);
            MoveIntent intent = policy.Evaluate(Context(new Vector3(0f, 0f, 30f), Vector3.forward, 179f));

            Assert.That(intent.Destination.magnitude, Is.LessThan(Tolerance));
            Assert.AreEqual(MoveSpeed, intent.Speed, Tolerance);
            Assert.IsFalse(policy.IsApproachingOutOfView);
        }

        [Test]
        public void ViewIsJudgedFromCameraPositionNotTarget()
        {
            // 카메라가 목표 뒤 5m 에 있으면, 목표 기준 옆(90°)인 링 지점 (10,0,0) 은 카메라 기준 약 63° 라서 후보가 아니다
            var policy = new FlankPolicy(1);
            Vector3 cameraPosition = new Vector3(0f, 0f, -5f);
            var context = new MovementContext(new Vector3(30f, 0f, 0f), Vector3.zero, Vector3.forward,
                                              cameraPosition, HalfAngle, true,
                                              EngageWithoutSlot, Tuning, 0f, 0.1f);
            Vector3 destination = policy.Evaluate(context).Destination;

            Assert.That(Vector3.Distance(destination, new Vector3(FlankRadius, 0f, 0f)), Is.GreaterThan(1f));
            Assert.That(ViewAngle(Vector3.forward, destination - cameraPosition), Is.GreaterThanOrEqualTo(HalfAngle + 30f - Tolerance));
        }
    }
}
