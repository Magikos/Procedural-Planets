using System;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class AgentDecisionTests
    {
        [Test]
        public void CommitmentAndMarginPreventAlternatingChoices()
        {
            var decision = new UtilityDecision<string>(0.1f, 2d);
            decision.Select(new[] { new UtilityCandidate<string>("food", 0.6f), new("water", 0.5f) }, 0d);
            decision.Select(new[] { new UtilityCandidate<string>("food", 0.6f), new("water", 0.95f) }, 1d);
            Assert.AreEqual("food", decision.Choice, "commitment blocks ordinary interruptions");
            for (int i = 2; i < 20; i++)
            {
                decision.Select(new[] { new UtilityCandidate<string>("food", 0.6f), new("water", i % 2 == 0 ? 0.65f : 0.55f) }, i);
                Assert.AreEqual("food", decision.Choice);
            }
            decision.Select(new[] { new UtilityCandidate<string>("food", 0.6f), new("water", 0.9f) }, 20d);
            Assert.AreEqual("water", decision.Choice);
        }

        [Test]
        public void EmergencyAndLostEligibilityOverrideCommitment()
        {
            var decision = new UtilityDecision<int>(0.1f, 10d);
            decision.Select(new[] { new UtilityCandidate<int>(1, 10f) }, 0d);
            decision.Select(new[] { new UtilityCandidate<int>(1, 10f), new(2, 0.1f, emergency: true) }, 1d);
            Assert.AreEqual(2, decision.Choice);
            decision.Select(new[] { new UtilityCandidate<int>(1, 10f), new(2, 0.1f, eligible: false) }, 2d);
            Assert.AreEqual(1, decision.Choice);
            Assert.IsFalse(decision.Select(Array.Empty<UtilityCandidate<int>>(), 3d));
            Assert.IsFalse(decision.HasChoice);
        }

        [Test]
        public void RejectionIsPerTargetExpiresAndCanBeInvalidated()
        {
            var decision = new UtilityDecision<int>(0f, 0d);
            var candidates = new[] { new UtilityCandidate<int>(10, 0.9f), new(20, 0.6f) };
            decision.Select(candidates, 0d);
            decision.Reject(10, 5d);
            decision.Select(candidates, 1d);
            Assert.AreEqual(20, decision.Choice);
            decision.Select(candidates, 5d);
            Assert.AreEqual(10, decision.Choice);
            decision.Reject(10, 50d);
            decision.ForgetFailure(10);
            decision.Select(candidates, 6d);
            Assert.AreEqual(10, decision.Choice);
        }

        [Test]
        public void EqualScoresKeepCurrentChoiceAfterCandidateReordering()
        {
            var decision = new UtilityDecision<int>(0f, 0d);
            decision.Select(new[] { new UtilityCandidate<int>(1, 1f), new(2, 1f) }, 0d);
            decision.Select(new[] { new UtilityCandidate<int>(2, 1f), new(1, 1f) }, 1d);
            Assert.AreEqual(1, decision.Choice);
            Assert.Throws<ArgumentOutOfRangeException>(() => decision.Select(Array.Empty<UtilityCandidate<int>>(), 0d));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UtilityCandidate<int>(1, float.NaN));
        }

        [Test]
        public void NeedGrowthUsesElapsedTimeAndSaturates()
        {
            var needs = new ActorNeeds();
            for (int i = 0; i < 600; i++) needs = needs.Advance(0.1d, 120d, 60d);
            Assert.AreEqual(0.5d, needs.Hunger, 1e-10);
            Assert.AreEqual(1d, needs.Thirst, 1e-10);
            needs = needs.Advance(10000d, 120d, 0d);
            Assert.AreEqual(1d, needs.Hunger);
            Assert.AreEqual(1d, needs.Thirst);
            Assert.Throws<ArgumentOutOfRangeException>(() => needs.Advance(double.NaN, 10d, 10d));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActorNeeds(-1d, 0d));
        }

        [Test]
        public void CreatureNeedsChooseSearchAndDangerInterruptsImmediately()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
            var senses = new CreatureSenses { Up = Vector3.up, Forward = Vector3.forward, DeltaTime = 0.1f,
                Needs = new ActorNeeds(0.9d, 0.1d) };
            brain.Observe(senses);
            var intent = brain.Sample(0);
            Assert.AreEqual(CreatureObjective.FindFood, brain.Objective);
            Assert.Greater(intent.Move.y, 0f, "searching does not use the idle rest chance");
            senses.HasThreat = true;
            senses.ThreatPosition = Vector3.forward;
            brain.Observe(senses); brain.Sample(1);
            Assert.AreEqual(CreatureObjective.Escape, brain.Objective);
            Assert.AreEqual(CreatureBehaviour.Flee, brain.Behaviour);
        }

        [TestCase(1, CreatureObjective.FindWater)]
        [TestCase(5, CreatureObjective.Hunt)]
        public void InjuryAndThirstCanOutweighHunting(int health, CreatureObjective expected)
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
            brain.Observe(new CreatureSenses { Up = Vector3.up, Forward = Vector3.forward, DeltaTime = 0.1f,
                Needs = new ActorNeeds(0.9d, 0.95d), HasPrey = true, PreyPosition = Vector3.forward * 6,
                Health = health, MaxHealth = 5 });
            brain.Sample(0);
            Assert.AreEqual(expected, brain.Objective);
        }

        [Test]
        public void SatisfiedCreatureDoesNotHuntAnAvailablePrey()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
            brain.Observe(new CreatureSenses { Up = Vector3.up, Forward = Vector3.forward, DeltaTime = 0.1f,
                HasPrey = true, PreyPosition = Vector3.forward * 6 });
            brain.Sample(0);
            Assert.AreEqual(CreatureObjective.Roam, brain.Objective);
            Assert.IsFalse(brain.HitRequested);
        }

        [Test]
        public void ChangingPreyDuringAttackCannotTransferTheHit()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
            var senses = new CreatureSenses { Up = Vector3.up, Forward = Vector3.forward, DeltaTime = 0.1f,
                HasPrey = true, PreyAlert = true, PreyPosition = Vector3.forward,
                Needs = new ActorNeeds(0.8d, 0d),
                PreyId = new EntityId(EntityId.HostOwner, 10), AttackDuration = 1f, AttackHitTime = 0.5f };
            for (uint i = 0; i < 3; i++) { brain.Observe(senses); brain.Sample(i); }
            Assert.AreEqual(CreatureBehaviour.Attack, brain.Behaviour);
            senses.PreyId = new EntityId(EntityId.HostOwner, 20);
            senses.DeltaTime = 1f;
            brain.Observe(senses); brain.Sample(3);
            Assert.IsFalse(brain.HitRequested);
            Assert.AreEqual(CreatureBehaviour.Recover, brain.Behaviour);
        }

        [Test]
        public void FailedHuntCancelsActionAndAllowsAnotherTarget()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
            var senses = new CreatureSenses { Up = Vector3.up, Forward = Vector3.forward, DeltaTime = 0.1f,
                Needs = new ActorNeeds(0.8d, 0d), HasPrey = true, PreyPosition = Vector3.forward * 6,
                PreyId = new EntityId(EntityId.HostOwner, 10) };
            brain.Observe(senses); brain.Sample(0);
            brain.ReportHuntFailure();
            brain.Observe(senses); brain.Sample(1);
            Assert.AreEqual(CreatureObjective.FindFood, brain.Objective);
            Assert.IsFalse(brain.HitRequested);
            senses.PreyId = new EntityId(EntityId.HostOwner, 20);
            senses.DeltaTime = 2.1f;
            brain.Observe(senses); brain.Sample(2);
            Assert.AreEqual(CreatureObjective.Hunt, brain.Objective);
        }
    }
}
