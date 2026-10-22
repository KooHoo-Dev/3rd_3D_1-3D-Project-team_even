using HybridShooter.Monster.Movement;
using NUnit.Framework;
using UnityEngine;

namespace HybridShooter.Monster.Movement.Lab.Tests
{
    public class PolicyContractTests
    {
        private static MovementTuning Tuning => new MovementTuning(3.5f, 1.8f, 0.5f, 25f, 12f, 18f, 50f, 10f, 1.6f);
        private static MovementContext Context(CrowdDirective directive, bool valid = true) =>
            new MovementContext(Vector3.forward * -10, Vector3.zero, Vector3.forward, valid, directive, Tuning, 1, 0.2f);

        [Test]
        public void MeleeUsesActualValidatedSlotRatherThanRebuildingRing()
        {
            var slot = new SlotSnapshot(5, 0, Vector3.right * 3, new Vector3(2.7f, 0.1f, 0.2f),
                SlotValidity.Valid, 1, SlotUse.Reserved, false);
            var context = Context(new CrowdDirective(DirectiveKind.Engage, slot));
            var intent = new MeleePolicy(1).Evaluate(context);
            Assert.That(intent.Destination, Is.EqualTo(slot.Position));
        }

        [Test]
        public void NoSlotStopsInsteadOfEnteringCongestedSpace()
        {
            var context = Context(CrowdDirective.WaitingForSlot(Vector3.forward * -10));
            Assert.That(new MeleePolicy(1).Evaluate(context).HasDestination, Is.False);
            Assert.That(new FlankPolicy(1).Evaluate(context).State, Is.EqualTo(MovementState.Waiting));
        }

        [Test]
        public void WaitingUsesItsReservedPosition()
        {
            var slot = new SlotSnapshot(15, 1, Vector3.right * 8, Vector3.right * 8,
                SlotValidity.Valid, 1, SlotUse.Reserved, false);
            var context = Context(new CrowdDirective(DirectiveKind.Wait, slot));
            Assert.That(new MeleePolicy(1).Evaluate(context).Destination, Is.EqualTo(slot.Position));
        }

        [Test]
        public void TokenWaitingAndOrbitUseTheConfiguredWaitingRing()
        {
            var settings = new SlotSettings { WaitingRadius = 8f };
            var parameters = new AllocationParams
            {
                TargetPosition = Vector3.zero,
                Capacity = 0,
                NearRadius = 6f,
                Slots = settings,
                ReleaseRadius = 10.5f,
                UseSlots = false
            };
            var agents = new[] { new AllocationAgent { Position = Vector3.right * 12f, UsesToken = true } };
            var results = new AllocationResult[1];
            EngageAllocator.Allocate(parameters, agents, 1, results);
            Assert.That(results[0].Directive.WaitPoint.x, Is.EqualTo(8f).Within(0.001f));

            var tuning = new MovementTuning(3.5f, 1.8f, 1f, 25f, 12f, 18f, 50f, 10f, 1.6f);
            var context = new MovementContext(Vector3.right * 8f, Vector3.zero, Vector3.forward, true,
                results[0].Directive, tuning, 1f, 0.2f);
            var intent = new MeleePolicy(1).Evaluate(context);
            Assert.That(intent.State, Is.EqualTo(MovementState.Orbiting));
            Assert.That(intent.Destination.magnitude, Is.EqualTo(8f).Within(0.001f));
        }

        [Test]
        public void InvalidTargetStopsBothPolicies()
        {
            var context = Context(CrowdDirective.None, false);
            Assert.That(new MeleePolicy(1).Evaluate(context).State, Is.EqualTo(MovementState.Idle));
            Assert.That(new FlankPolicy(1).Evaluate(context).HasDestination, Is.False);
        }

        [Test]
        public void FlankSelectsLateralWaypointFromFront()
        {
            var context = new MovementContext(Vector3.forward * 15, Vector3.zero, Vector3.forward, true,
                new CrowdDirective(DirectiveKind.Unrestricted, default, 0, false, 0), Tuning, 1, 0.2f);
            var intent = new FlankPolicy(1).Evaluate(context);
            Assert.That(Mathf.Abs(intent.Destination.x), Is.GreaterThan(5f));
            Assert.That(Vector3.Angle(Vector3.forward, intent.Destination), Is.GreaterThan(50f));
        }

        [Test]
        public void FlankCommitsAfterDetourInsteadOfOscillatingBackToWaypoint()
        {
            var slot = new SlotSnapshot(0, 0, Vector3.forward * 2.8f, Vector3.forward * 2.8f,
                SlotValidity.Valid, 1, SlotUse.Reserved, false);
            var directive = new CrowdDirective(DirectiveKind.Engage, slot);
            var policy = new FlankPolicy(1);
            var front = new MovementContext(Vector3.forward * 15, Vector3.zero, Vector3.forward, true,
                directive, Tuning, 0, 0.2f);
            policy.Evaluate(front);
            var side = new MovementContext(new Vector3(10, 0, 1), Vector3.zero, Vector3.forward, true,
                directive, Tuning, 1, 0.2f);
            Assert.That(policy.Evaluate(side).Destination, Is.EqualTo(slot.Position));
            var reenterView = new MovementContext(new Vector3(2, 0, 4), Vector3.zero, Vector3.forward, true,
                directive, Tuning, 2, 0.2f);
            Assert.That(policy.Evaluate(reenterView).Destination, Is.EqualTo(slot.Position));
        }
    }
}
