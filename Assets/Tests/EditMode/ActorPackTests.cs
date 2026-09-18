using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorPackTests
    {
        [Test]
        public void IndependentActorsAreNotAlliesAndUnavailableMembersGiveNoSupport()
        {
            Assert.IsFalse(ActorGroup.Allied(0, 0)); Assert.IsFalse(ActorGroup.Allied(1, 0));
            Assert.IsTrue(ActorGroup.Allied(1, 1));
            Assert.AreEqual(0, ActorGroup.Support(1, 1, 2, 12, 12, true));
            Assert.AreEqual(0, ActorGroup.Support(1, 1, 2, 2, 12, false));
            Assert.AreEqual(0, ActorGroup.Support(0, 1, 2, 2, 12, true));
            Assert.AreEqual(1, ActorGroup.Support(1, 1, 2, 6, 12, true));
        }
        [Test]
        public void OutnumberedWolfRetreatsWhileEqualRivalHoldsGround()
        {
            foreach (int opponents in new[] { 1, 2, 3 })
            {
                var disposition = new ActorDisposition(.65);
                var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
                for (uint tick = 0; tick < 100; tick++)
                {
                    disposition.Advance(.1, 1.5, 1.5 * opponents, true);
                    brain.Observe(new CreatureSenses { Position = Vector3.zero, Forward = Vector3.forward, Up = Vector3.up,
                        DeltaTime = .1f, HasDisposition = true, HasThreat = true, ThreatDistance = 4,
                        ThreatPosition = Vector3.forward * 4, ThreatId = new EntityId(EntityId.HostOwner, 10),
                        CanDefend = true, Confidence = (float)disposition.Confidence, Fear = (float)disposition.Fear,
                        Courage = (float)disposition.EffectiveCourage, Health = 90, MaxHealth = 90 });
                    brain.Sample(tick);
                }
                Assert.AreEqual(opponents == 1 ? CreatureObjective.Defend : CreatureObjective.Escape, brain.Objective);
                if (opponents > 1) Assert.Less(disposition.EffectiveCourage, disposition.Courage);
            }
        }
        [Test]
        public void NearbySupportImprovesConfidenceAndLosingItRemovesTheBonus()
        {
            var actor = new ActorDisposition(.65);
            actor.Advance(1, 1.5, 3, true, 2);
            double supported = actor.Confidence, courage = actor.EffectiveCourage, fear = actor.Fear;
            actor.Advance(2, 1.5, 3, true);
            Assert.Less(actor.Confidence, supported); Assert.Less(actor.EffectiveCourage, courage);
            Assert.Greater(actor.Fear, fear);
        }
        [Test]
        public void EqualUnprovokedRivalGetsAWarningInsteadOfAnAttack()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
            var senses = new CreatureSenses { Up = Vector3.up, Forward = Vector3.forward, DeltaTime = .1f,
                HasThreat = true, ThreatDistance = 4, ThreatPosition = Vector3.forward * 4,
                HasDisposition = true, Confidence = .5f, Courage = .65f, CanDefend = true, CanThreaten = true,
                Health = 90, MaxHealth = 90, ThreatId = new EntityId(EntityId.HostOwner, 10) };
            for (uint tick = 0; tick < 45; tick++) { brain.Observe(senses); brain.Sample(tick); }
            Assert.AreEqual(CreatureBehaviour.Threaten, brain.Behaviour); Assert.AreEqual(0u, brain.AttackSequence);
            senses.ThreatProvoked = true; brain.Observe(senses); brain.Sample(101);
            Assert.AreEqual(CreatureBehaviour.Defend, brain.Behaviour);
        }
        [Test]
        public void CoordinationExcludesIndependentDistantAndUnwillingMembers()
        {
            var candidates = new[] { new ActorGroup.Candidate(1, 0, 0, 4, true),
                new ActorGroup.Candidate(2, 1, 20, 4, true), new ActorGroup.Candidate(3, 1, 2, 4, false),
                new ActorGroup.Candidate(4, 1, 2, 4, true), new ActorGroup.Candidate(5, 1, 1, 4, true) };
            Assert.AreEqual(3, ActorGroup.SelectLeader(1, candidates, 12, 20));
            Assert.AreEqual(-1, ActorGroup.SelectLeader(0, candidates, 12, 20));
        }
        [Test]
        public void GroupCommitmentKeepsItsTargetAndExpires()
        {
            var hunt = new ActorGroup.Hunt(); hunt.Commit(5, 10, 1, 20);
            hunt.Commit(6, 11, 2, 20);
            Assert.AreEqual(5ul, hunt.Leader); Assert.AreEqual(10ul, hunt.Target);
            Assert.IsFalse(hunt.Active(21)); hunt.Commit(6, 11, 21, 20);
            Assert.AreEqual(11ul, hunt.Target); hunt.Clear(); Assert.IsFalse(hunt.Active(22));
        }
        [Test]
        public void WillingMemberJoinsHuntButUrgentWaterAndThreatStillWin()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
            var senses = new CreatureSenses { Up = Vector3.up, Forward = Vector3.forward, DeltaTime = 3,
                HasPrey = true, PreyId = new EntityId(EntityId.HostOwner, 10), PreyPosition = Vector3.forward * 8,
                CanRest = true, Needs = new ActorNeeds(.4, .2), GroupHuntCommitted = true };
            brain.Observe(senses); brain.Sample(0); Assert.AreEqual(CreatureObjective.Hunt, brain.Objective);
            senses.Needs = new ActorNeeds(.4, .95);
            senses.Water = new CreatureResourceTarget { Available = true, Id = new EntityId(EntityId.HostOwner, 11), Position = Vector3.right };
            brain.Observe(senses); brain.Sample(1); Assert.AreEqual(CreatureObjective.FindWater, brain.Objective);
            senses.HasThreat = true; senses.ThreatPosition = Vector3.back; senses.ThreatDistance = 1;
            brain.Observe(senses); brain.Sample(2); Assert.AreEqual(CreatureObjective.Escape, brain.Objective);
        }
        [Test]
        public void LongStalkDoesNotExhaustChaseBudget()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
            var senses = new CreatureSenses { Up = Vector3.up, Forward = Vector3.forward, DeltaTime = 1,
                HasPrey = true, PreyId = new EntityId(EntityId.HostOwner, 10), PreyPosition = Vector3.forward * 8,
                Needs = new ActorNeeds(.9, 0) };
            for (uint tick = 0; tick < 25; tick++) { brain.Observe(senses); brain.Sample(tick); }
            Assert.AreEqual(CreatureBehaviour.Stalk, brain.Behaviour);
            senses.PreyAlert = true;
            for (uint tick = 25; tick < 28; tick++) { brain.Observe(senses); brain.Sample(tick); }
            Assert.AreEqual(CreatureBehaviour.Chase, brain.Behaviour);
        }
    }
}
