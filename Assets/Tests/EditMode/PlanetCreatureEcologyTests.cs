using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class PlanetCreatureEcologyTests
    {
        [Test]
        public void ConfiguredMassAllowsLowBodiedPredatorToHuntSuitablePrey()
        {
            using var fixture = new Fixture();
            fixture.Species[0] = fixture.Species[0] with { BodyHeightMeters = .1f, BodyMassKg = 3f, MaximumPreyMassRatio = 1f };
            fixture.Species[1] = fixture.Species[1] with { BodyHeightMeters = .45f, BodyMassKg = 2f };
            object snake = fixture.Add(0, Vector3.zero);
            fixture.Add(1, Vector3.forward * 4f);
            Assert.IsTrue(fixture.Observe(snake).HasPrey);
            fixture.Species[1] = fixture.Species[1] with { BodyMassKg = 10f };
            fixture.AdvanceScan();
            Assert.IsFalse(fixture.Observe(snake).HasPrey);
        }

        [Test]
        public void LargerPredatorNeedsDetectionAndDisguiseCanSuppressAutomaticFear()
        {
            using var fixture = new Fixture();
            fixture.Species[0] = fixture.Species[0] with { BodyMassKg = 40f };
            fixture.Species[1] = fixture.Species[1] with { DisplayName = "Bear", Faction = CreatureFaction.Predator, BodyMassKg = 200f };
            object wolf = fixture.Add(0, Vector3.zero);
            object bear = fixture.Add(1, Vector3.forward * 5f);
            var senses = fixture.Observe(wolf);
            Assert.IsTrue(senses.HasThreat);
            Assert.AreEqual(Fixture.Get<EntityId>(bear, "Id"), senses.ThreatId);
            Assert.IsFalse(senses.HasPrey, "Neutral predator factions must not become mutual prey.");
            fixture.Disguise(bear, CreatureFaction.Wildlife);
            fixture.AdvanceScan();
            Assert.IsFalse(fixture.Observe(wolf).HasThreat);
        }

        [Test]
        public void DistantLargerPredatorDoesNotCauseOmniscientFear()
        {
            using var fixture = new Fixture();
            fixture.Species[0] = fixture.Species[0] with { BodyMassKg = 40f };
            fixture.Species[1] = fixture.Species[1] with { Faction = CreatureFaction.Predator, BodyMassKg = 200f };
            object wolf = fixture.Add(0, Vector3.zero);
            fixture.Add(1, Vector3.forward * 100f);
            Assert.IsFalse(fixture.Observe(wolf).HasThreat);
        }

        [Test]
        public void ConfiguredMassSuppliesFourMealsFromEightyKilogramCarcass()
        {
            using var fixture = new Fixture();
            fixture.Species[1] = fixture.Species[1] with { BodyMassKg = 80f };
            object wolf = fixture.Add(0, Vector3.zero);
            EntityId corpse = fixture.Corpses.Record(1, Fixture.Origin + Vector3.forward * .5f, Quaternion.identity, Fixture.Now);
            var senses = fixture.Observe(wolf);
            Fixture.Set(wolf, "Behaviour", CreatureBehaviour.Feed);
            fixture.Finish(wolf, senses, 1f);
            double consumed = .8d - Fixture.Get<ActorNeeds>(wolf, "Needs").Hunger;
            Assert.AreEqual(4d - consumed, fixture.Corpses.RemainingMeat(corpse, Fixture.Now, 4d), 1e-6);
        }

        [Test]
        public void PredatorSelectsDetectedPreyThenPrefersDiscoveredCarcass()
        {
            using var fixture = new Fixture();
            object wolf = fixture.Add(0, Vector3.zero);
            object deer = fixture.Add(1, Vector3.forward * 6f);
            var senses = fixture.Observe(wolf);
            Assert.IsTrue(senses.HasPrey);
            Assert.AreEqual(Fixture.Get<EntityId>(deer, "Id"), senses.PreyId);
            EntityId corpse = fixture.Corpses.Record(1, Fixture.Origin + Vector3.forward * 3f, Quaternion.identity, Fixture.Now);
            fixture.AdvanceScan();
            senses = fixture.Observe(wolf);
            Assert.IsFalse(senses.HasPrey);
            Assert.AreEqual(corpse, senses.Food.Id);
            Assert.AreEqual(ResourceKind.Meat, senses.Food.Kind);
        }

        [Test]
        public void PredatorCannotIdentifyDistantOrFriendlyResidentsAsPrey()
        {
            using var fixture = new Fixture();
            object wolf = fixture.Add(0, Vector3.zero);
            fixture.Add(0, Vector3.forward * 4f);
            fixture.Add(1, Vector3.forward * 100f);
            Assert.IsFalse(fixture.Observe(wolf).HasPrey);
        }

        [Test]
        public void HerbivoreDetectsPredatorBehindWithoutRequiringAnAttack()
        {
            using var fixture = new Fixture();
            object deer = fixture.Add(1, Vector3.zero);
            object wolf = fixture.Add(0, Vector3.back * 4f);
            Fixture.Set(wolf, "Behaviour", CreatureBehaviour.Rest);
            var senses = fixture.Observe(deer);
            Assert.IsTrue(senses.HasThreat);
            Assert.IsFalse(senses.DirectThreat, "The predator is outside the forward sight cone.");
            Assert.AreEqual(Fixture.Get<EntityId>(wolf, "Id"), senses.ThreatId);
            Assert.IsFalse(senses.HasPrey);
        }

        [Test]
        public void CorpseFeedingSatisfiesHungerAndKeepsConsumedStockAfterReload()
        {
            using var fixture = new Fixture();
            object wolf = fixture.Add(0, Vector3.zero);
            EntityId corpse = fixture.Corpses.Record(1, Fixture.Origin + Vector3.forward * .5f, Quaternion.identity, Fixture.Now);
            var senses = fixture.Observe(wolf);
            Assert.IsTrue(senses.Food.Available);
            Fixture.Set(wolf, "Behaviour", CreatureBehaviour.Feed);
            fixture.Finish(wolf, senses, 1f);
            double consumed = .8d - Fixture.Get<ActorNeeds>(wolf, "Needs").Hunger;
            Assert.Greater(consumed, 0d);
            var restored = new CreatureCorpseStore();
            restored.Configure(fixture.Log);
            double capacity = Math.Pow(fixture.Species[1].BodyHeightMeters, 3);
            Assert.AreEqual(capacity - consumed, restored.RemainingMeat(corpse, Fixture.Now, capacity), 1e-6);
            Fixture.Set(wolf, "Position", Fixture.Origin + Vector3.right * 10f);
            fixture.Finish(wolf, senses, 1f);
            Assert.AreEqual(.8d - consumed, Fixture.Get<ActorNeeds>(wolf, "Needs").Hunger, 1e-9,
                "Remembering a carcass must not allow feeding outside physical reach.");
        }

        sealed class Fixture : IDisposable
        {
            public const long Now = 1788770000;
            public static readonly Vector3 Origin = Vector3.up * 1000f;
            const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
            static readonly Type ResidentType = typeof(CreatureResidencyService).GetNestedType("Resident", BindingFlags.NonPublic);
            readonly GameObject _planet = new("Ecology test planet");
            readonly CreatureResidencyService _service;
            readonly ThreatRegistry _threats = new();
            readonly IDictionary _bySlot;
            int _nextSlot;
            double _time;
            public readonly WorldDeltaLog Log = new();
            public readonly CreatureCorpseStore Corpses = new();
            public readonly CreatureSpeciesDto[] Species =
            {
                CreatureLibraryDto.Placeholder.At(0) with { DisplayName = "Wolf", Faction = CreatureFaction.Predator,
                    Diet = ResourceKind.Meat | ResourceKind.FreshWater, BodyHeightMeters = 1f },
                CreatureLibraryDto.Placeholder.At(0) with { BodyHeightMeters = 1.84f },
            };

            public Fixture()
            {
                _service = new CreatureResidencyService(_planet.transform, null, null, () => Now);
                SetService("_library", new CreatureLibraryDto(Species, 150f));
                SetService("_threats", _threats);
                SetService("_corpses", Corpses);
                _bySlot = (IDictionary)typeof(CreatureResidencyService).GetField("_bySlot", Private).GetValue(_service);
                Corpses.Configure(Log);
            }

            public object Add(int speciesIndex, Vector3 offset)
            {
                object resident = Activator.CreateInstance(ResidentType, true);
                EntityId id = CreatureKey.Slot(2, CreatureTerritory.Level, 8, 8, _nextSlot++);
                Vector3 position = Origin + offset;
                Set(resident, "Id", id); Set(resident, "Slot", id); Set(resident, "SpeciesIndex", speciesIndex);
                Set(resident, "Position", position); Set(resident, "Forward", Vector3.forward);
                Set(resident, "Health", 3); Set(resident, "Needs", new ActorNeeds(.8d, .1d));
                Set(resident, "Behaviour", CreatureBehaviour.Wander);
                Set(resident, "Brain", new CreatureBrain(_nextSlot, Species[speciesIndex], CreatureBehaviour.Wander));
                Set(resident, "Perception", new ActorPerception(Species[speciesIndex].Perception));
                Set(resident, "Driver", new SurfaceCharacterController(null, null, 0f,
                    new CharacterPose(position, Vector3.up, Vector3.forward)));
                _bySlot.Add(id.Value, resident);
                _threats.Report(id, position, Species[speciesIndex].Faction);
                return resident;
            }

            public CreatureSenses Observe(object resident)
            {
                var senses = new CreatureSenses { Position = Get<Vector3>(resident, "Position"), Up = Vector3.up,
                    Forward = Vector3.forward, Species = Species[Get<int>(resident, "SpeciesIndex")] };
                object[] args = { resident, senses.Species, Now, senses };
                typeof(CreatureResidencyService).GetMethod("PrepareEcologySenses", Private).Invoke(_service, args);
                return (CreatureSenses)args[3];
            }

            public void Finish(object resident, CreatureSenses senses, float dt) =>
                typeof(CreatureResidencyService).GetMethod("FinishEcologyStep", Private).Invoke(_service,
                    new object[] { resident, Species[Get<int>(resident, "SpeciesIndex")], senses, dt, Now });
            public void AdvanceScan() { _time += 1d; SetService("_resourceTime", _time); }
            public void Disguise(object resident, CreatureFaction faction) =>
                _threats.SetDisguise(Get<EntityId>(resident, "Id"), faction, 30f, Now);
            void SetService(string name, object value) => typeof(CreatureResidencyService).GetField(name, Private).SetValue(_service, value);
            public static T Get<T>(object target, string name) => (T)target.GetType().GetField(name).GetValue(target);
            public static void Set(object target, string name, object value) => target.GetType().GetField(name).SetValue(target, value);
            public void Dispose() { _service.Dispose(); Log.Dispose(); UnityEngine.Object.DestroyImmediate(_planet); }
        }
    }
}
