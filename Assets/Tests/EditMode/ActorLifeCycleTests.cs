using System;
using NUnit.Framework;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorLifeCycleTests
    {
        readonly ActorLifeProfile _profile = new(2, 20, 30, 40, 3, 5, 2);

        [Test]
        public void RichHabitatCannotCrowdPredatorsOutOfMemory()
        {
            var memory = new ActorKnowledge();
            for (ulong id = 1; id <= 32; id++) memory.Remember(id, ActorObservationKind.Water, UnityEngine.Vector3.zero, 0, 90);
            memory.Remember(100, ActorObservationKind.Actor, UnityEngine.Vector3.one, 1, 12);
            Assert.That(memory.Knows(100, ActorObservationKind.Actor, 1), Is.True);
            memory.Remember(33, ActorObservationKind.Water, UnityEngine.Vector3.zero, 2, 90);
            Assert.That(memory.Knows(100, ActorObservationKind.Actor, 2), Is.True);
            Assert.That(memory.Count, Is.EqualTo(32));
        }

        [Test]
        public void BirthOccursOnceAndCooldownPreventsImmediatePregnancy()
        {
            var life = new ActorLifeCycle(ActorSex.Female, 4, 35);
            Assert.That(life.TryConceive(42, _profile, default, 1, 0, true, true), Is.True);
            Assert.That(life.Advance(2, _profile), Is.Zero);
            Assert.That(life.Advance(1, _profile), Is.EqualTo(2));
            Assert.That(life.Advance(0, _profile), Is.Zero);
            Assert.That(life.TryConceive(42, _profile, default, 1, 0, true, true), Is.False);
            life.Advance(5, _profile);
            Assert.That(life.TryConceive(42, _profile, default, 1, 0, true, true), Is.True);
        }

        [Test]
        public void EligibilityRequiresAdultHealthNeedsSafetyAndCapacity()
        {
            var adult = new ActorLifeCycle(ActorSex.Female, 4, 35);
            Assert.That(adult.CanReproduce(_profile, new ActorNeeds(.8, 0), 1, 0, true, true), Is.False);
            Assert.That(adult.CanReproduce(_profile, default, .7, 0, true, true), Is.False);
            Assert.That(adult.CanReproduce(_profile, default, 1, .8, true, true), Is.False);
            Assert.That(adult.CanReproduce(_profile, default, 1, 0, false, true), Is.False);
            Assert.That(adult.CanReproduce(_profile, default, 1, 0, true, false), Is.False);
            Assert.That(new ActorLifeCycle(ActorSex.Female, 1, 35).CanReproduce(_profile, default, 1, 0, true, true), Is.False);
            Assert.That(new ActorLifeCycle(ActorSex.Female, 21, 35).CanReproduce(_profile, default, 1, 0, true, true), Is.False);
        }

        [Test]
        public void RestoredPregnancyMatchesUninterruptedLifeAndAgeDeath()
        {
            var life = new ActorLifeCycle(ActorSex.Female, 4, 35, 11);
            life.TryConceive(42, _profile, default, 1, 0, true, true);
            life.Advance(1, _profile);
            var restored = new ActorLifeCycle(life.Sex, life.AgeDays, life.DeathAgeDays, life.MotherId,
                life.FatherId, life.Pregnant, life.PregnancyDays, life.CooldownDays, life.MateId);
            Assert.That(restored.Advance(2, _profile), Is.EqualTo(life.Advance(2, _profile)));
            restored.Advance(28, _profile);
            Assert.That(restored.DiedOfAge, Is.True);
            Assert.That(restored.CanReproduce(_profile, default, 1, 0, true, true), Is.False);
        }

        [Test]
        public void RenewableStockCapsAndNeverGrantsNutritionDuringRenewal()
        {
            var needs = new ActorNeeds(1, 0);
            var plants = new ActorResourceSource(ResourceKind.Plants, 2, 1, 0, .5);
            plants.Consume(ref needs, ResourceKind.Plants, 1);
            Assert.That(plants.Advance(1), Is.EqualTo(.5));
            Assert.That(needs.Hunger, Is.Zero);
            plants.Advance(100);
            Assert.That(plants.Remaining, Is.EqualTo(2));
            var meat = new ActorResourceSource(ResourceKind.Meat, 2, 1, 0);
            meat.LimitRemaining(0); meat.Advance(100);
            Assert.That(meat.Remaining, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => plants.Advance(double.NaN));
        }

        [Test]
        public void CapacityUsesLimitingResourceAndKeepsReserve()
        {
            Assert.That(new ActorHabitatCapacity(20, 5).SupportedPopulation(1, 1), Is.EqualTo(4));
            Assert.That(new ActorHabitatCapacity(20, 0).SupportedPopulation(1, 1), Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActorHabitatCapacity(-1, 1));
        }

        [Test]
        public void AdequateRenewalFeedsPopulationForOneHundredDays()
        {
            var plants = new ActorResourceSource(ResourceKind.Plants, 20, 1, 0, 12);
            var water = new ActorResourceSource(ResourceKind.FreshWater, 20, 0, 1, 12);
            var needs = new ActorNeeds[8];
            for (int day = 0; day < 100; day++)
            {
                plants.Advance(1); water.Advance(1);
                for (int i = 0; i < needs.Length; i++)
                {
                    needs[i] = needs[i].Advance(1, 1, 1);
                    plants.Consume(ref needs[i], ResourceKind.Plants, 1);
                    water.Consume(ref needs[i], ResourceKind.FreshWater, 1);
                    Assert.That(needs[i].Hunger + needs[i].Thirst, Is.Zero);
                }
            }
        }
    }
}
