using System;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorHomeTests
    {
        static ActorHomeSite Site(uint group = 1, ulong actor = 0, int capacity = 3) =>
            new(2001ul, Vector3.zero, Vector3.up, 3, capacity, group, actor, true);
        [Test]
        public void OwnershipCapacityAndRestPositionsRemainStable()
        {
            var site = Site(capacity: 2);
            Assert.IsFalse(site.TryReserve(40, 0));
            Assert.IsTrue(site.TryReserve(5, 1)); Assert.IsTrue(site.TryReserve(20, 1));
            Assert.IsFalse(site.TryReserve(21, 1));
            site.TryRestPosition(5, out var first); site.TryRestPosition(20, out var second);
            Assert.Greater(Vector3.Distance(first, second), 1.1f);
            site.Release(20); Assert.IsTrue(site.TryReserve(21, 1));
            site.TryRestPosition(5, out var stable); Assert.AreEqual(first, stable);
            var solo = Site(0, 40, 1);
            Assert.IsTrue(solo.TryReserve(40, 0)); Assert.IsFalse(solo.TryReserve(41, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Site(0, 0));
        }
        [Test]
        public void GatheringLeavesMissingMembersAndDoesNotImmediatelyRestart()
        {
            var site = Site();
            site.UpdateGather(0, true, 3, 1, true, 8); Assert.IsTrue(site.Gathering);
            site.UpdateGather(7, true, 3, 1, true, 8); Assert.IsTrue(site.Gathering);
            site.UpdateGather(8, true, 3, 1, true, 8); Assert.IsFalse(site.Gathering);
            site.UpdateGather(9, true, 3, 1, true, 8); Assert.IsFalse(site.Gathering);
            site.UpdateGather(40, true, 3, 3, true, 8); Assert.IsTrue(site.Gathering);
            site.UpdateGather(42, true, 3, 3, true, 8); Assert.IsFalse(site.Gathering);
            site.UpdateGather(80, true, 3, 3, false, 8); Assert.IsFalse(site.Gathering);
        }
        [Test]
        public void SharedObservationsExpireWithoutRumourRefresh()
        {
            var first = new ActorKnowledge(); var second = new ActorKnowledge();
            const ulong id = 101;
            first.Remember(id, ActorObservationKind.Water, Vector3.one, 0, 10);
            first.ShareWith(second, 5); second.ShareWith(first, 9);
            Assert.IsTrue(second.Knows(id, ActorObservationKind.Water, 9));
            Assert.IsFalse(first.Knows(id, ActorObservationKind.Water, 10));
            Assert.IsFalse(second.Knows(id, ActorObservationKind.Water, 10));
            first.Remember(40, ActorObservationKind.Threat, Vector3.one, 10, 5);
            Assert.IsTrue(first.ThreatNear(Vector3.zero, 3, 11)); Assert.IsFalse(first.ThreatNear(Vector3.zero, 3, 15));
            for (ulong i = 1; i <= 40; i++) first.Remember(i, ActorObservationKind.Food, Vector3.zero, 20, i);
            Assert.AreEqual(32, first.Count);
        }
        static CreatureSenses Senses() => new() { Up = Vector3.up, Forward = Vector3.forward, DeltaTime = .1f,
            Needs = new ActorNeeds(.1, .1), CanRest = true, HasHomeSite = true, HomeSafe = true,
            HomeSheltered = true, HomeId = new EntityId(2001), RestPosition = Vector3.forward * 6 };
        [Test]
        public void FedActorReturnsThenRestsAndGatheringCanBeInterrupted()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander); var senses = Senses();
            brain.Observe(senses); brain.Sample(0); Assert.AreEqual(CreatureObjective.ReturnHome, brain.Objective);
            senses.Needs = new ActorNeeds(.6, .1); senses.DeltaTime = 3;
            brain.Observe(senses); brain.Sample(1); Assert.AreEqual(CreatureObjective.ReturnHome, brain.Objective);
            senses.AtHome = true; senses.Needs = new ActorNeeds(.1, .1); senses.DeltaTime = .1f;
            for (uint i = 1; i < 4; i++) { brain.Observe(senses); brain.Sample(i); }
            Assert.AreEqual(CreatureBehaviour.Rest, brain.Behaviour);
            senses.GatherAtHome = true; senses.Needs = new ActorNeeds(.7, .1);
            for (uint i = 4; i < 35; i++) { brain.Observe(senses); brain.Sample(i); }
            Assert.AreEqual(CreatureBehaviour.Gather, brain.Behaviour);
            senses.HasThreat = true; senses.ThreatDistance = 2; senses.ThreatPosition = Vector3.forward * 2;
            brain.Observe(senses); brain.Sample(36); Assert.AreEqual(CreatureObjective.Escape, brain.Objective);
        }
        [Test]
        public void ExhaustionUnsafeHomeAndFailedRouteAllowLocalRest()
        {
            foreach (int condition in new[] { 0, 1, 2 })
            {
                var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander); var senses = Senses();
                if (condition == 0) senses.NeedsRecovery = true;
                if (condition == 1) senses.HomeSafe = false;
                brain.Observe(senses); brain.Sample(0);
                if (condition == 2) { brain.ReportNavigationFailure(); senses.HasHomeSite = false; }
                for (uint tick = 1; tick < 5; tick++) { brain.Observe(senses); brain.Sample(tick); }
                Assert.AreEqual(CreatureObjective.Rest, brain.Objective);
            }
        }
    }
}
