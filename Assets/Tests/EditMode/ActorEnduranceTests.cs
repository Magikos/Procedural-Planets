using System;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorEnduranceTests
    {
        static readonly ActorEnduranceProfile Profile = new(8d, 360d, 60d);

        [Test]
        public void ResourceTravelRecoversSlowerThanStationaryRest()
        {
            var walking = new ActorEndurance(.5d);
            var resting = new ActorEndurance(.5d);
            var needs = new ActorNeeds(.2d, .2d);
            walking.Advance(1d, needs, ActorEndurance.ClassifyExertion(1d, false, false), Profile);
            resting.Advance(1d, needs, ActorEndurance.ClassifyExertion(0d, false, false), Profile);
            Assert.Greater(resting.Stamina, walking.Stamina);
            Assert.AreEqual(ActorExertion.Run, ActorEndurance.ClassifyExertion(1d, true, false));
            Assert.AreEqual(ActorExertion.Sleep, ActorEndurance.ClassifyExertion(0d, false, true));
            Assert.AreEqual(ActorExertion.Walk, ActorEndurance.ClassifyExertion(1d, false, true),
                "A stale sleep state cannot grant sleep recovery while moving.");
            Assert.Throws<ArgumentOutOfRangeException>(() => ActorEndurance.ClassifyExertion(double.NaN, false, false));
        }

        [Test]
        public void OrdinaryHungerKeepsCapacityAndSeverePenaltiesHaveAFloor()
        {
            Assert.AreEqual(1d, ActorEndurance.TargetCapacity(new(.8d, .7d), 0d));
            Assert.AreEqual(.55d, ActorEndurance.TargetCapacity(new(1d, 1d), 1d));
            var actor = new ActorEndurance();
            actor.Advance(.1d, new(1d, 1d), ActorExertion.Rest, Profile);
            Assert.Greater(actor.Capacity, .99d);
        }

        [Test]
        public void HungryExhaustedActorCanRecoverAndRunAgain()
        {
            var actor = new ActorEndurance(.05d);
            Assert.IsTrue(actor.Recovering);
            for (int i = 0; i < 200; i++) actor.Advance(.1d, new(1d, .2d), ActorExertion.Rest, Profile);
            Assert.IsFalse(actor.Recovering);
            Assert.Greater(actor.Fraction, .65d);
            double before = actor.Stamina;
            actor.Advance(1d, new(1d, .2d), ActorExertion.Run, Profile);
            Assert.Less(actor.Stamina, before);
        }

        [Test]
        public void WolfProfileLastsLongerWithoutInfiniteEndurance()
        {
            var deer = new ActorEndurance(); var wolf = new ActorEndurance();
            var wolfProfile = new ActorEnduranceProfile(22d, 360d, 60d);
            for (int i = 0; i < 80; i++)
            { deer.Advance(.1d, default, ActorExertion.Run, Profile); wolf.Advance(.1d, default, ActorExertion.Run, wolfProfile); }
            Assert.IsTrue(deer.Recovering); Assert.IsFalse(wolf.Recovering);
            Assert.Greater(wolf.RunSpeedScale, deer.RunSpeedScale);
            wolf.Advance(22d, default, ActorExertion.Run, wolfProfile);
            Assert.IsTrue(wolf.Recovering);
        }

        [Test]
        public void SleepRepairsFatigueButFoodDoesNotRefillStaminaInstantly()
        {
            var actor = new ActorEndurance(.2d, .9d, .6d, true, true);
            actor.Advance(0d, default, ActorExertion.Rest, Profile);
            Assert.AreEqual(.2d, actor.Stamina); Assert.AreEqual(.6d, actor.Capacity);
            actor.Advance(40d, default, ActorExertion.Sleep, Profile);
            Assert.Less(actor.Fatigue, .25d); Assert.IsFalse(actor.NeedsSleep);
            var restored = new ActorEndurance(actor.Stamina, actor.Fatigue, actor.Capacity, actor.Recovering, actor.NeedsSleep);
            Assert.AreEqual(actor.Fraction, restored.Fraction);
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.Advance(double.NaN, default, ActorExertion.Rest, Profile));
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.Advance(1d, default, ActorExertion.Rest, default));
        }

        static CreatureSenses Senses() => new() { Position = Vector3.zero, Forward = Vector3.forward, Up = Vector3.up,
            Needs = new(1d, .1d), Health = 2, MaxHealth = 2, DeltaTime = .1f, HasPrey = true,
            PreyId = new(EntityId.HostOwner, 1), PreyPosition = Vector3.forward * 8f, CanRest = true };
        static void Tick(CreatureBrain brain, CreatureSenses senses, int count)
        { for (uint i = 0; i < count; i++) { brain.Observe(senses); brain.Sample(i); } }

        [Test]
        public void MaximumHungerHuntYieldsToNewlyDetectedCarrion()
        {
            var brain = new CreatureBrain(5, null, CreatureBehaviour.Wander); var senses = Senses();
            Tick(brain, senses, 25); Assert.AreEqual(CreatureObjective.Hunt, brain.Objective);
            senses.Food = new() { Available = true, Id = new(EntityId.HostOwner, 42), Position = Vector3.forward * 100f };
            Tick(brain, senses, 25); Assert.AreEqual(CreatureObjective.Hunt, brain.Objective);
            senses.Food = new() { Available = true, Id = new(EntityId.HostOwner, 42), Position = Vector3.forward * 3f };
            Tick(brain, senses, 10);
            Assert.AreEqual(CreatureObjective.FindFood, brain.Objective);
            Assert.AreEqual(CreatureBehaviour.Feed, brain.Behaviour);
            Assert.AreEqual(senses.Food.Id, brain.ObjectiveTarget);
        }

        [Test]
        public void RecoveryBlocksHuntingButNeverBlocksEmergencyFlight()
        {
            var brain = new CreatureBrain(5, null, CreatureBehaviour.Wander); var senses = Senses();
            senses.NeedsRecovery = true;
            Tick(brain, senses, 30); Assert.AreEqual(CreatureBehaviour.Rest, brain.Behaviour);
            senses.NeedsRecovery = false;
            Tick(brain, senses, 30); Assert.AreEqual(CreatureObjective.Hunt, brain.Objective);
            senses.NeedsRecovery = true; senses.HasThreat = true;
            Tick(brain, senses, 1); Assert.AreEqual(CreatureBehaviour.Flee, brain.Behaviour);
        }

        [Test]
        public void HungrySleepyActorCanChooseSafeNearbyFoodBeforeSleep()
        {
            var brain = new CreatureBrain(5, null, CreatureBehaviour.Wander); var senses = Senses();
            senses.NeedsSleep = senses.CanSleep = true;
            Tick(brain, senses, 70); Assert.AreEqual(CreatureBehaviour.Sleep, brain.Behaviour);
            senses.Food = new() { Available = true, Position = Vector3.forward, Id = new(EntityId.HostOwner, 42) };
            Tick(brain, senses, 30); Assert.AreEqual(CreatureBehaviour.Feed, brain.Behaviour);
        }
    }
}
