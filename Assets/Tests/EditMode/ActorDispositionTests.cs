using System;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorDispositionTests
    {
        [Test]
        public void FearRisesQuicklyAndSubsidesSlowlyWithoutErasingInjury()
        {
            var actor = new ActorDisposition(.5d);
            actor.Advance(1d, 1d, 4d, true);
            Assert.AreEqual(.2d, actor.Confidence, 1e-9);
            Assert.AreEqual(.5d, actor.Fear, 1e-9);
            actor.ReportHarm(.4d);
            actor.Advance(1d, 1d, 0d, false);
            Assert.Greater(actor.Fear, .85d);
            actor.Advance(30d, 1d, 0d, false);
            Assert.AreEqual(0d, actor.Fear);
            Assert.AreEqual(1d, actor.Confidence);
        }

        [Test]
        public void StrengthChangesConfidenceAndInputsAreValidated()
        {
            var actor = new ActorDisposition(.5d, .2d);
            actor.Advance(0d, 4d, 1d, false);
            Assert.AreEqual(.8d, actor.Confidence, 1e-9);
            Assert.AreEqual(.2d, actor.Fear);
            actor.Advance(0d, double.MaxValue, double.MaxValue, false);
            Assert.AreEqual(.5d, actor.Confidence);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActorDisposition(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.Advance(1d, 0d, 1d, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.Advance(double.NaN, 1d, 1d, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.ReportHarm(2d));
        }

        static CreatureSenses Senses() => new() { Up = Vector3.up, Forward = Vector3.forward,
            HasThreat = true, ThreatId = new(EntityId.HostOwner, 8), ThreatPosition = Vector3.forward * 2f,
            ThreatDistance = 2f, DeltaTime = .1f, HasDisposition = true, CanDefend = true,
            Confidence = .8f, Courage = .8f, Health = 2, MaxHealth = 2, AttackDuration = 1f, AttackHitTime = .5f };
        static void Tick(CreatureBrain brain, CreatureSenses senses, int count)
        { for (uint i = 0; i < count; i++) { brain.Observe(senses); brain.Sample(i); } }

        [Test]
        public void ConfidentActorDefendsButImmediatePanicOverridesCommitment()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander); var senses = Senses();
            Tick(brain, senses, 1); Assert.AreEqual(CreatureBehaviour.Defend, brain.Behaviour);
            senses.Fear = 1f;
            Tick(brain, senses, 1); Assert.AreEqual(CreatureBehaviour.Flee, brain.Behaviour);
        }

        [Test]
        public void CorneredFearfulActorCanCounterattackOncePerAttackAndCannotHuntItsThreat()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander); var senses = Senses();
            senses.EscapeBlocked = true; senses.Fear = 1f; senses.Confidence = .1f;
            senses.ThreatPosition = Vector3.forward; senses.ThreatDistance = 1f;
            int hits = 0;
            for (uint i = 0; i < 12; i++) { brain.Observe(senses); brain.Sample(i); if (brain.HitRequested) hits++; }
            Assert.AreEqual(1, hits); Assert.AreEqual(CreatureObjective.Defend, brain.Objective);
            senses.HasThreat = false;
            Tick(brain, senses, 30);
            Assert.AreEqual(CreatureObjective.Roam, brain.Objective);
            Assert.AreNotEqual(CreatureBehaviour.Chase, brain.Behaviour);
        }

        [Test]
        public void SmallConfidenceChangesDoNotOscillateFightAndFlight()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander); var senses = Senses();
            senses.Courage = .4f; senses.Confidence = .7f;
            Tick(brain, senses, 1);
            for (int i = 0; i < 100; i++)
            {
                senses.Confidence = i % 2 == 0 ? .38f : .42f;
                Tick(brain, senses, 1);
                Assert.AreEqual(CreatureObjective.Defend, brain.Objective);
            }
        }

        [Test]
        public void ConfidentHungryHunterCanContinueAgainstDefendingPrey()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander); var senses = Senses();
            senses.HasPrey = true; senses.PreyId = senses.ThreatId; senses.PreyPosition = senses.ThreatPosition;
            senses.Needs = new(1d, 0d);
            Tick(brain, senses, 1); Assert.AreEqual(CreatureObjective.Hunt, brain.Objective);
            senses.Confidence = .1f; senses.Fear = .95f;
            Tick(brain, senses, 1); Assert.AreEqual(CreatureObjective.Escape, brain.Objective);
            senses.Confidence = .8f; senses.Fear = .2f;
            Tick(brain, senses, 30); Assert.AreNotEqual(CreatureObjective.Hunt, brain.Objective);
        }

        [Test]
        public void CriticalThirstSelectsRememberedWaterOverFruitlessFoodSearch()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander); var senses = Senses();
            senses.HasThreat = false; senses.Needs = new(1d, 1d);
            Tick(brain, senses, 30);
            senses.Water = new() { Available = true, Id = new(EntityId.HostOwner, 42), Position = Vector3.forward * 100f };
            Tick(brain, senses, 30); Assert.AreEqual(CreatureObjective.FindWater, brain.Objective);
        }

        [Test]
        public void DesperateHunterStillRejectsOverwhelmingOpponent()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander); var senses = Senses();
            senses.HasThreat = false; senses.HasPrey = true; senses.PreyPosition = Vector3.forward * 8f;
            senses.PreyId = senses.ThreatId; senses.Needs = new(1d, 0d);
            senses.Confidence = .05f; senses.Courage = .5f; senses.Fear = .5f;
            Tick(brain, senses, 30); Assert.AreNotEqual(CreatureObjective.Hunt, brain.Objective);
            senses.Confidence = .7f; senses.Fear = .1f;
            Tick(brain, senses, 30); Assert.AreEqual(CreatureObjective.Hunt, brain.Objective);
        }
    }
}
