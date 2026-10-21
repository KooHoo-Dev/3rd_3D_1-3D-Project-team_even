using System;
using System.Collections.Generic;
using HybridShooter.Monster.Movement;
using NUnit.Framework;
using UnityEngine;

namespace HybridShooter.Monster.Movement.Lab.Tests
{
    public class SlotRegistryTests
    {
        private static readonly SlotObstacle[] NoObstacles = Array.Empty<SlotObstacle>();

        private sealed class FakeNavigation : ISlotNavigation
        {
            public Func<Vector3, bool> Projectable = _ => true;
            public Func<Vector3, Vector3> Projected = p => p;
            public Func<Vector3, Vector3, bool> Reachable = (_, __) => true;

            public bool TryProject(Vector3 candidate, out Vector3 position)
            {
                position = Projected(candidate);
                return Projectable(candidate);
            }

            public bool CanReach(Vector3 origin, Vector3 destination) => Reachable(origin, destination);
        }

        [Test]
        public void EmptyPopulationStillHasTwentyFourStableWorldAxisCandidates()
        {
            var registry = new SlotRegistry(new SlotSettings(), new FakeNavigation());
            registry.Refresh(Vector3.zero, 0f, NoObstacles);
            Assert.That(registry.Count, Is.EqualTo(24));
            SlotSnapshot first = registry.GetSnapshot(0);
            Assert.That(first.Kind, Is.EqualTo(SlotKind.Approach));
            Assert.That(first.CandidatePosition.z, Is.EqualTo(2.8f).Within(0.001f));
            Assert.That(registry.GetSnapshot(8).Kind, Is.EqualTo(SlotKind.Waiting));
            registry.Refresh(new Vector3(5f, 0f, -3f), 1f, NoObstacles);
            Assert.That(registry.GetSnapshot(0).CandidatePosition - first.CandidatePosition,
                Is.EqualTo(new Vector3(5f, 0f, -3f)));
        }

        [Test]
        public void RegistryKeepsItsCapturedIdsWhenSettingsCountChanges()
        {
            var settings = new SlotSettings { ApproachCount = 2, WaitingCount = 3 };
            var registry = new SlotRegistry(settings, new FakeNavigation());
            settings.ApproachCount = 8;
            settings.WaitingCount = 16;
            registry.Refresh(Vector3.zero, 1f, NoObstacles);
            Assert.That(registry.Count, Is.EqualTo(5));
            Assert.That(registry.GetSnapshot(2).Kind, Is.EqualTo(SlotKind.Waiting));
        }

        [Test]
        public void RequestIsStableExclusiveAndReleasePromotesWaitingOwner()
        {
            var settings = new SlotSettings { ApproachCount = 1, WaitingCount = 1 };
            var registry = new SlotRegistry(settings, new FakeNavigation());
            registry.Refresh(Vector3.zero, 0f, NoObstacles);
            Assert.That(registry.TryRequest(11, Vector3.zero, 0f, true, out SlotSnapshot first), Is.True);
            Assert.That(registry.TryRequest(11, Vector3.zero, 0.1f, true, out SlotSnapshot repeat), Is.True);
            Assert.That(repeat.Id, Is.EqualTo(first.Id));
            Assert.That(registry.TryRequest(22, Vector3.zero, 0f, true, out SlotSnapshot waiting), Is.True);
            Assert.That(waiting.Kind, Is.EqualTo(SlotKind.Waiting));
            Assert.That(waiting.Id, Is.Not.EqualTo(first.Id));
            registry.Release(11);
            Assert.That(registry.TryRequest(22, Vector3.zero, 0.2f, true, out SlotSnapshot promoted), Is.True);
            Assert.That(promoted.Kind, Is.EqualTo(SlotKind.Approach));
            Assert.That(registry.GetSnapshot(waiting.Id).Use, Is.EqualTo(SlotUse.Free));
            Assert.That(registry.ApproachReservations, Is.EqualTo(1));
        }

        [Test]
        public void ProjectionFailureAndCollapsedProjectedPointsAreInvalid()
        {
            var settings = new SlotSettings { ApproachCount = 2, WaitingCount = 1 };
            var nav = new FakeNavigation
            {
                Projectable = p => p.z >= 0f,
                Projected = p => p.z > 0f ? Vector3.zero : p
            };
            var registry = new SlotRegistry(settings, nav);
            registry.Refresh(Vector3.zero, 0f, NoObstacles);
            Assert.That(registry.GetSnapshot(0).Validity, Is.EqualTo(SlotValidity.Valid));
            Assert.That(registry.GetSnapshot(1).Validity, Is.EqualTo(SlotValidity.OffNavMesh));
            Assert.That(registry.GetSnapshot(2).Validity, Is.EqualTo(SlotValidity.TooClose));
        }

        [Test]
        public void CongestedOrUnreachableCandidateIsSkipped()
        {
            var settings = new SlotSettings { ApproachCount = 4, WaitingCount = 1 };
            var nav = new FakeNavigation { Reachable = (_, destination) => destination.x <= 0.1f };
            var registry = new SlotRegistry(settings, nav);
            var obstacles = new[] { new SlotObstacle(99, new Vector3(0f, 0f, 2.8f), 0.5f) };
            registry.Refresh(Vector3.zero, 0f, obstacles);
            Assert.That(registry.GetSnapshot(0).Congested, Is.True);
            Assert.That(registry.TryRequest(11, Vector3.zero, 0f, true, out SlotSnapshot assigned), Is.True);
            Assert.That(assigned.Id, Is.EqualTo(2).Or.EqualTo(3));
        }

        [Test]
        public void TransientCrowdDoesNotStealExistingValidReservation()
        {
            var registry = new SlotRegistry(new SlotSettings { ApproachCount = 2, WaitingCount = 1 }, new FakeNavigation());
            registry.Refresh(Vector3.zero, 0f, NoObstacles);
            Assert.That(registry.TryRequest(11, Vector3.zero, 0f, true, out SlotSnapshot original), Is.True);
            registry.Refresh(Vector3.zero, 1f, new[]
            {
                new SlotObstacle(11, Vector3.zero, 0.4f),
                new SlotObstacle(22, original.Position, 0.4f)
            });
            Assert.That(registry.GetSnapshot(original.Id).Congested, Is.True);
            Assert.That(registry.TryRequest(11, Vector3.zero, 1f, true, out SlotSnapshot retained), Is.True);
            Assert.That(retained.Id, Is.EqualTo(original.Id));
        }

        [Test]
        public void StalledReservationRejectsOnlyThatOwnersSlotUntilCooldownExpires()
        {
            var settings = new SlotSettings { ApproachCount = 2, WaitingCount = 1 };
            var registry = new SlotRegistry(settings, new FakeNavigation());
            registry.Refresh(Vector3.zero, 0f, NoObstacles);
            Assert.That(registry.TryRequest(11, Vector3.zero, 0f, true, out SlotSnapshot first), Is.True);
            Assert.That(registry.TryRequest(11, Vector3.zero, 6.1f, true, out SlotSnapshot reassigned), Is.True);
            Assert.That(reassigned.Id, Is.Not.EqualTo(first.Id));
            registry.Release(11);
            Assert.That(registry.TryRequest(22, Vector3.zero, 6.2f, true, out SlotSnapshot other), Is.True);
            Assert.That(other.Id, Is.EqualTo(first.Id));
            registry.Release(22);
            registry.Release(11);
            Assert.That(registry.TryRequest(11, Vector3.zero, 8.2f, true, out SlotSnapshot retried), Is.True);
            Assert.That(retried.Id, Is.EqualTo(first.Id));
        }

        [Test]
        public void MovingTargetRequiresActualArrivalAtNewPosition()
        {
            var settings = new SlotSettings { ApproachCount = 1, WaitingCount = 1 };
            var registry = new SlotRegistry(settings, new FakeNavigation());
            Vector3 original = new Vector3(0f, 0f, 2.8f);
            registry.Refresh(Vector3.zero, 0f, NoObstacles);
            Assert.That(registry.TryRequest(11, original, 0f, true, out SlotSnapshot occupied), Is.True);
            Assert.That(occupied.Use, Is.EqualTo(SlotUse.Occupied));
            registry.Refresh(new Vector3(5f, 0f, 0f), 1f,
                new List<SlotObstacle> { new SlotObstacle(11, original, 0.4f) });
            Assert.That(registry.TryRequest(11, original, 1f, true, out SlotSnapshot moved), Is.True);
            Assert.That(moved.Use, Is.EqualTo(SlotUse.Reserved));
            Assert.That(moved.Position.x, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void UnreachableCandidatesLeaveAgentUnassignedAndApproachCanBeDisabled()
        {
            var settings = new SlotSettings { ApproachCount = 1, WaitingCount = 1 };
            var nav = new FakeNavigation { Reachable = (_, __) => false };
            var registry = new SlotRegistry(settings, nav);
            registry.Refresh(Vector3.zero, 0f, NoObstacles);
            Assert.That(registry.TryRequest(11, Vector3.zero, 0f, true, out _), Is.False);
            nav.Reachable = (_, __) => true;
            Assert.That(registry.TryRequest(11, Vector3.zero, 0f, false, out SlotSnapshot waiting), Is.True);
            Assert.That(waiting.Kind, Is.EqualTo(SlotKind.Waiting));
        }
    }
}
