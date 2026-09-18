using System;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class CreatureSurvivalTests
    {
        [Test]
        public void ConsumptionConservesStockAndCannotOverfillOrUseWrongDiet()
        {
            var source = new ActorResourceSource(ResourceKind.Meat, .3d, 1d, 0d);
            var needs = new ActorNeeds(.8d, .6d);
            Assert.AreEqual(0d, source.Consume(ref needs, ResourceKind.Plants, 1d));
            Assert.AreEqual(.3d, source.Consume(ref needs, ResourceKind.Meat, 1d));
            Assert.AreEqual(.5d, needs.Hunger, 1e-9);
            Assert.AreEqual(.6d, needs.Thirst);
            Assert.AreEqual(0d, source.Consume(ref needs, ResourceKind.Meat, 1d));
            source = new ActorResourceSource(ResourceKind.FreshWater, 2d, 0d, 1d);
            Assert.AreEqual(.6d, source.Consume(ref needs, ResourceKind.FreshWater, 10d));
            Assert.AreEqual(1.4d, source.Remaining, 1e-9);
            Assert.AreEqual(0d, needs.Thirst);
            var salt = new ActorResourceSource(ResourceKind.SaltWater, 1d, 0d, 1d);
            Assert.IsFalse(salt.Accepts(ResourceKind.FreshWater | ResourceKind.Meat));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.Consume(ref needs, ResourceKind.FreshWater, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActorResourceSource(ResourceKind.Meat, -1d, 1d, 0d));
        }

        [Test]
        public void HalfFullCreatureRestsSleepsThenWakesToHunt()
        {
            var brain = new CreatureBrain(2, null, CreatureBehaviour.Wander);
            var senses = Senses(.5d, .1d);
            senses.CanRest = senses.CanSleep = true;
            senses.HasPrey = true; senses.PreyPosition = Vector3.forward * 6;
            Tick(brain, senses, 70);
            Assert.AreEqual(CreatureBehaviour.Sleep, brain.Behaviour);
            senses.Needs = new ActorNeeds(.7d, .1d);
            Tick(brain, senses, 3);
            Assert.AreEqual(CreatureBehaviour.Stalk, brain.Behaviour);
        }

        [Test]
        public void DangerImmediatelyInterruptsSleepAndFeeding()
        {
            foreach (var start in new[] { CreatureBehaviour.Sleep, CreatureBehaviour.Feed })
            {
                var brain = new CreatureBrain(2, null, start);
                var senses = Senses(.8d, .1d); senses.HasThreat = true;
                Tick(brain, senses, 1);
                Assert.AreEqual(CreatureBehaviour.Flee, brain.Behaviour);
            }
        }

        [Test]
        public void FoodApproachStopsInReachAndEndsWhenStockDisappears()
        {
            var brain = new CreatureBrain(2, null, CreatureBehaviour.Wander);
            var senses = Senses(.8d, .1d);
            senses.Food = new CreatureResourceTarget { Available = true, Position = Vector3.forward * 6,
                Id = new EntityId(EntityId.HostOwner, 3) };
            brain.Observe(senses);
            Assert.Greater(brain.Sample(0).Move.y, 0f);
            Assert.AreEqual(CreatureBehaviour.Feed, brain.Behaviour);
            senses.Position = Vector3.forward * 5;
            brain.Observe(senses); Assert.AreEqual(0f, brain.Sample(1).Move.y);
            senses.Food.Available = false;
            Tick(brain, senses, 1);
            Assert.AreEqual(CreatureBehaviour.Wander, brain.Behaviour);
        }

        [Test]
        public void NearbyCarcassIsPreferredToAnotherHunt()
        {
            var brain = new CreatureBrain(2, null, CreatureBehaviour.Wander);
            var senses = Senses(.8d, .1d); senses.HasPrey = true; senses.PreyPosition = Vector3.forward * 6;
            senses.Food = new CreatureResourceTarget { Available = true, Position = Vector3.forward };
            Tick(brain, senses, 1);
            Assert.AreEqual(CreatureBehaviour.Feed, brain.Behaviour);
        }

        static CreatureSenses Senses(double hunger, double thirst) => new()
        { Up = Vector3.up, Forward = Vector3.forward, DeltaTime = .1f, Needs = new ActorNeeds(hunger, thirst) };
        static void Tick(CreatureBrain brain, CreatureSenses senses, int count)
        { for (uint i = 0; i < count; i++) { brain.Observe(senses); brain.Sample(i); } }

        [Test]
        public void LostThreatCausesBoundedVigilanceBeforeFeeding()
        {
            var brain = new CreatureBrain(2, null, CreatureBehaviour.Wander);
            var senses = Senses(.8d, .1d);
            senses.VigilanceSeconds = 4f;
            senses.Food = new CreatureResourceTarget { Available = true, Position = Vector3.forward };
            senses.HasThreat = true;
            senses.ThreatPosition = Vector3.forward * 10f;
            Tick(brain, senses, 1);
            Assert.AreEqual(CreatureBehaviour.Flee, brain.Behaviour);
            senses.HasThreat = false;
            Tick(brain, senses, 3);
            Assert.AreEqual(CreatureBehaviour.Alert, brain.Behaviour);
            Assert.AreEqual(CreatureObjective.Vigilance, brain.Objective);
            Assert.AreEqual(senses.ThreatPosition, brain.VigilancePosition);
            brain.Observe(senses);
            Assert.AreEqual(0f, brain.Sample(5).Move.y);
            Tick(brain, senses, 50);
            Assert.AreEqual(CreatureBehaviour.Feed, brain.Behaviour);
        }

        [Test]
        public void RenewedThreatInterruptsVigilanceAndRestartsQuietTime()
        {
            var brain = new CreatureBrain(2, null, CreatureBehaviour.Wander);
            var senses = Senses(.8d, .1d);
            senses.VigilanceSeconds = 4f;
            senses.HasThreat = true;
            Tick(brain, senses, 1);
            senses.HasThreat = false;
            Tick(brain, senses, 30);
            Assert.AreEqual(CreatureBehaviour.Alert, brain.Behaviour);
            senses.HasThreat = true;
            Tick(brain, senses, 1);
            Assert.AreEqual(CreatureBehaviour.Flee, brain.Behaviour);
            senses.HasThreat = false;
            Tick(brain, senses, 20);
            Assert.AreEqual(CreatureBehaviour.Alert, brain.Behaviour);
        }

        [TestCase(0f, .8d)]
        [TestCase(4f, .95d)]
        public void DisabledVigilanceOrCriticalHungerAllowsFeeding(float seconds, double hunger)
        {
            var brain = new CreatureBrain(2, null, CreatureBehaviour.Wander);
            var senses = Senses(hunger, .1d);
            senses.VigilanceSeconds = seconds;
            senses.HasThreat = true;
            senses.Food = new CreatureResourceTarget { Available = true, Position = Vector3.forward };
            Tick(brain, senses, 1);
            senses.HasThreat = false;
            Tick(brain, senses, 3);
            Assert.AreEqual(CreatureBehaviour.Feed, brain.Behaviour);
        }

        [Test]
        public void RabbitUsesSharedRestSleepAndThreatInterruption()
        {
            var rabbit = CreatureLibraryDto.Placeholder.At(1);
            var brain = new CreatureBrain(7, rabbit, CreatureBehaviour.Wander);
            var senses = Senses(.2d, .2d);
            senses.Species = rabbit;
            senses.CanRest = senses.CanSleep = senses.NeedsSleep = true;
            Tick(brain, senses, 70);
            Assert.AreEqual(CreatureBehaviour.Sleep, brain.Behaviour);
            senses.HasThreat = true;
            senses.ThreatPosition = Vector3.back * 4f;
            brain.Observe(senses);
            Assert.Greater(brain.Sample(71).Move.y, 0f);
            Assert.AreEqual(CreatureBehaviour.Flee, brain.Behaviour);
        }

        [Test]
        public void GroundVigilanceDoesNotStopAnAirborneBird()
        {
            var bird = CreatureLibraryDto.Placeholder.At(2);
            var brain = new CreatureBrain(7, bird, CreatureBehaviour.Wander);
            var senses = Senses(.2d, .2d);
            senses.Species = bird;
            senses.VigilanceSeconds = 4f;
            senses.HasThreat = true;
            Tick(brain, senses, 1);
            senses.HasThreat = false;
            Tick(brain, senses, 3);
            Assert.AreEqual(CreatureBehaviour.Wander, brain.Behaviour);
            brain.Observe(senses);
            Assert.Greater(brain.Sample(5).Move.y, 0f);
        }

        [Test]
        public void BiteTracksDuringWindupAndCommitsAtHitTime()
        {
            var brain = new CreatureBrain(2, null, CreatureBehaviour.Attack);
            var senses = Senses(.8d, .1d);
            senses.HasPrey = true; senses.PreyPosition = new Vector3(.3f, 0f, 1f);
            senses.AttackDuration = 1f; senses.AttackHitTime = .4f;
            brain.Observe(senses);
            Assert.Greater(brain.Sample(0).Look.x, 0f);
            senses.DeltaTime = .4f;
            brain.Observe(senses);
            var hit = brain.Sample(1);
            Assert.IsTrue(brain.HitRequested);
            Assert.AreEqual(0f, hit.Look.x);
            Assert.AreEqual(0f, hit.Move.y);
        }
    }
}
