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
    }
}
