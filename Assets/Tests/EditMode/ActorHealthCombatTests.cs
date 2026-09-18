using System;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorHealthCombatTests
    {
        static readonly ActorRecoveryProfile Recovery = new(10d, 100d, 100d, 100d, 1000d);
        static readonly ActorAttackDefinition Attack = new(18d, 1.4f, 40f, .4f, .3f, 1f, .12d, .2d, .15d);

        [Test]
        public void WoundedEscapeEventuallyRestoresHealthWoundsAndStopsBleeding()
        {
            var health = new ActorHealth(100);
            health.Damage(25d, .3d, .15d);
            for (int i = 0; i < 5; i++) health.Advance(1d, default, ActorExertion.Run, true, Recovery);
            Assert.Less(health.Current, 75d); Assert.AreEqual(.3d, health.Wounds);
            for (int i = 0; i < 150; i++) health.Advance(1d, default, ActorExertion.Rest, false, Recovery);
            Assert.AreEqual(100d, health.Current); Assert.AreEqual(0d, health.Wounds); Assert.AreEqual(0d, health.Bleeding);
        }

        [Test]
        public void DamageRestartsDelayAndDeathNeverHeals()
        {
            var health = new ActorHealth(100, 80d);
            health.Advance(9d, default, ActorExertion.Rest, false, Recovery);
            health.Damage(1d);
            health.Advance(9d, default, ActorExertion.Sleep, false, Recovery);
            Assert.AreEqual(79d, health.Current);
            health.Damage(1000d);
            health.Advance(1000d, default, ActorExertion.Sleep, false, Recovery);
            Assert.IsFalse(health.Alive); Assert.AreEqual(0d, health.Current);
        }

        [Test]
        public void DeprivationBlocksHealingAndSleepIsFasterThanWalking()
        {
            var starving = new ActorHealth(100, 50d, .5d);
            starving.Advance(200d, new(1d, 0d), ActorExertion.Sleep, false, Recovery);
            Assert.AreEqual(50d, starving.Current); Assert.AreEqual(.5d, starving.Wounds);
            var walking = new ActorHealth(100, 50d, .5d); var sleeping = new ActorHealth(100, 50d, .5d);
            walking.Advance(20d, default, ActorExertion.Walk, false, Recovery);
            sleeping.Advance(20d, default, ActorExertion.Sleep, false, Recovery);
            Assert.Greater(sleeping.Current, walking.Current); Assert.Less(sleeping.Wounds, walking.Wounds);
        }

        [Test]
        public void HealthRestoreAndLargeStepsMatchSmallSteps()
        {
            var large = new ActorHealth(100, 75d, .3d, .15d); var small = new ActorHealth(100, 75d, .3d, .15d);
            large.Advance(40d, default, ActorExertion.Rest, false, Recovery);
            for (int i = 0; i < 400; i++) small.Advance(.1d, default, ActorExertion.Rest, false, Recovery);
            Assert.AreEqual(large.Current, small.Current, 1e-8); Assert.AreEqual(large.Wounds, small.Wounds, 1e-8);
            var restored = new ActorHealth(small.Maximum, small.Current, small.Wounds, small.Bleeding, small.SafeSeconds);
            Assert.AreEqual(small.Current, restored.Current);
            Assert.Throws<ArgumentOutOfRangeException>(() => restored.Damage(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActorHealth(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActorRecoveryProfile(0, 1, 1, 1, 1));
        }

        [Test]
        public void ExhaustedAttackSpendsRemainingReserveAndCannotDealFullDamage()
        {
            var endurance = new ActorEndurance(.03d);
            double power = endurance.SpendUpTo(Attack.StaminaCost);
            Assert.AreEqual(.25d, power, 1e-9); Assert.AreEqual(0d, endurance.Stamina);
            Assert.AreEqual(4.5d, Attack.Damage * power, 1e-9);
            Assert.Throws<ArgumentOutOfRangeException>(() => endurance.SpendUpTo(double.NaN));
        }

        static CreatureSenses Senses() => new() { Up = Vector3.up, Forward = Vector3.forward, DeltaTime = .05f,
            HasPrey = true, PreyPosition = Vector3.forward, PreyId = new(EntityId.HostOwner, 2), PreyAlert = true,
            Needs = new(1d, 0d), Health = 100d, MaxHealth = 100, Attack = Attack, CanAttack = true };

        [Test]
        public void ActiveWindowRetriesAMissThenStopsAfterConfirmedHit()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander); var senses = Senses();
            int requests = 0;
            for (uint i = 0; i < 25; i++)
            {
                brain.Observe(senses); brain.Sample(i);
                if (brain.HitRequested)
                {
                    requests++;
                    Assert.GreaterOrEqual(brain.AttackTime, Attack.Windup);
                    Assert.Less(brain.AttackTime, Attack.Duration);
                    if (requests == 2) brain.ConfirmHit();
                }
            }
            Assert.AreEqual(2, requests); Assert.AreEqual(1u, brain.AttackSequence);
        }

        [Test]
        public void AttackRequiresStaminaAndContactAndRespectsRecovery()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander); var senses = Senses();
            senses.CanAttack = false;
            for (uint i = 0; i < 20; i++) { brain.Observe(senses); brain.Sample(i); }
            Assert.AreEqual(0u, brain.AttackSequence);
            senses.CanAttack = true;
            for (uint i = 0; i < 20; i++) { brain.Observe(senses); brain.Sample(i); }
            Assert.AreEqual(1u, brain.AttackSequence);
            Assert.IsTrue(Attack.InContact(1f, 30f)); Assert.IsFalse(Attack.InContact(1f, 50f));
            Assert.IsFalse(Attack.InContact(2f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActorAttackDefinition(1, 1, 40, 1, 1, 1, 2, 0, 0));
        }

        [Test]
        public void HungryWoundedActorDoesNotRestForeverInsteadOfSeekingFood()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander); var senses = Senses();
            senses.HasPrey = false; senses.NeedsHealing = true; senses.CanRest = true;
            for (uint i = 0; i < 30; i++) { brain.Observe(senses); brain.Sample(i); }
            Assert.AreEqual(CreatureObjective.FindFood, brain.Objective);
        }
    }
}
