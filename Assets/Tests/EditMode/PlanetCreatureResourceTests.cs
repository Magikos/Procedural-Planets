using NUnit.Framework;
using System.Linq;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class PlanetCreatureResourceTests
    {
        [Test]
        public void HomeWaterGateRejectsDeepLakesButKeepsBanksFlightAndAquaticBands()
        {
            var deer = CreatureLibraryDto.Placeholder.At(0);
            var water = new HomeWater { Depth = 3f };
            Assert.IsFalse(PlanetCreatureResources.CanSettleHome(water, Vector3.up * 100, deer));
            Assert.IsTrue(PlanetCreatureResources.CanSettleHome(water, Vector3.up * 100,
                deer with { CruiseAltitudeMeters = 9f }));
            Assert.IsTrue(PlanetCreatureResources.CanSettleHome(water, Vector3.up * 100,
                deer with { MinAltitudeMeters = -30f, MaxAltitudeMeters = -2f }));
            water.Depth = .1f;
            Assert.IsTrue(PlanetCreatureResources.CanSettleHome(water, Vector3.up * 100, deer));
            water.Present = false;
            Assert.IsTrue(PlanetCreatureResources.CanSettleHome(water, Vector3.up * 100, deer));
        }

        sealed class HomeWater : IWaterQueryService
        {
            public float Depth;
            public bool Present = true;
            public bool TryGetWaterSurface(Vector3 position, out WaterSample sample)
            { sample = new WaterSample(position + position.normalized * Depth, position.normalized, Depth, Depth, 1, false); return Present; }
            public bool IsUnderwater(Vector3 position) => Present && Depth > 0f;
        }

        [Test]
        public void ColdAnimalHomeBiomesHaveAnAuthoredFoodChain()
        {
            var foliage = UnityEditor.AssetDatabase.LoadAssetAtPath<ScatterLibrary>("Assets/Resources/Settings/ScatterLibrary.asset");
            var animals = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureLibrary>("Assets/Resources/Settings/CreatureLibrary.asset");
            var goat = animals.Species.Single(s => s != null && s.DisplayName == "Goat");
            var bear = animals.Species.Single(s => s != null && s.DisplayName == "PolarBear");
            bool HasPlants(BiomeType biome) => foliage.Prototypes.Any(p => p != null && p.Biome == biome && p.Weight > 0f && p.FoodUnits > 0f);
            foreach (var biome in goat.Biomes) Assert.IsTrue(HasPlants(biome), "Goat home has no forage: " + biome);
            foreach (var biome in bear.Biomes)
                Assert.IsTrue(animals.Species.Any(s => s != null && s.Faction == CreatureFaction.Wildlife &&
                    (s.Diet & ResourceKind.Plants) != 0 && s.Biomes.Contains(biome) && HasPlants(biome)),
                    "Polar bear home has no supported herbivore prey: " + biome);
        }

        [TestCase(BiomeType.Mountain)]
        [TestCase(BiomeType.Tundra)]
        [TestCase(BiomeType.Desert)]
        [TestCase(BiomeType.Forest)]
        [TestCase(BiomeType.LakeShore)]
        public void HerbivoreHabitatsHaveRenderedRenewableForage(BiomeType biome)
        {
            var library = UnityEditor.AssetDatabase.LoadAssetAtPath<ScatterLibrary>("Assets/Resources/Settings/ScatterLibrary.asset");
            Assert.IsNotNull(library);
            var food = library.Prototypes.Where(p => p != null && p.Biome == biome && p.FoodUnits > 0f && p.Weight > 0f).ToArray();
            Assert.IsNotEmpty(food, "Run CreatureForageAuthor.Build before validating authored habitat coverage.");
            Assert.IsTrue(food.Any(p => p.FoodRegrowSeconds > 0f));
            Assert.IsTrue(food.Any(p => p.LodMeshes.Any(m => m != null) ||
                p.Parts.Any(part => part != null && part.LodMeshes.Any(m => m != null))), "Food must have real rendered foliage.");
            var ids = library.Prototypes.Where(p => p != null).Select(p => p.SlotId).ToArray();
            Assert.AreEqual(ids.Length, ids.Distinct().Count(), "New forage must not alias an existing scatter identity.");
        }

        [Test]
        public void FoodSurvivesReloadAndRegrowsWithoutOverwritingHarvest()
        {
            var asset = ScriptableObject.CreateInstance<ScatterPrototype>();
            try
            {
                asset.FoodUnits = 2; asset.FoodRegrowSeconds = 100;
                var prototype = ScatterPrototypeDto.From(asset);
                using var log = new WorldDeltaLog();
                log.Append(new WorldDelta(0, DeltaKind.ScatterState, 7, state: 1));
                var store = new FoliageFoodStore(log);
                var needs = new ActorNeeds(1, .5);
                Assert.AreEqual(1, store.Consume(7, prototype, 100, ref needs, ResourceKind.Plants, 2));
                Assert.AreEqual(0, needs.Hunger);
                Assert.AreEqual(.5, needs.Thirst);
                store.Flush();
                var restored = new FoliageFoodStore(log);
                Assert.AreEqual(1, restored.Remaining(7, prototype, 100));
                Assert.AreEqual(1.5, restored.Remaining(7, prototype, 125), .0001);
                Assert.AreEqual(2, restored.Remaining(7, prototype, 1000));
                Assert.IsTrue(log.TryGet(DeltaKind.ScatterState, 7, out var harvest));
                Assert.AreEqual(1, harvest.State);
                Assert.AreEqual(2, log.Count);
                Assert.AreEqual(0, restored.Consume(7, prototype, 100, ref needs, ResourceKind.Meat, 1));
            }
            finally { Object.DestroyImmediate(asset); }
        }

        sealed class Ground : IPlanetSurfaceSampler
        {
            public bool TryGetSurfaceRadius(Vector3 direction, out float radius) { radius = 100; return true; }
        }
        sealed class Water : IWaterQueryService
        {
            public bool Ocean;
            public bool TryGetWaterSurface(Vector3 position, out WaterSample sample)
            {
                bool wet = position.x > 3;
                sample = new WaterSample(position.normalized * 100.1f, position.normalized, .1f, wet ? .1f : 0, 1, Ocean);
                return wet;
            }
            public bool IsUnderwater(Vector3 position) => position.x > 3;
        }

        [Test]
        public void RealWaterQueryFindsBankAndHonorsSaltwaterDiet()
        {
            var water = new Water();
            var resources = new PlanetCreatureResources(null, null, water, new Ground(), Vector3.zero, null);
            var species = CreatureLibraryDto.Placeholder.At(0);
            var perception = new ActorPerception(new ActorPerceptionProfile(ViewAngle: 360));
            var position = Vector3.up * 100;
            resources.Search(2, position, Vector3.right, species, perception, new ActorKnowledge(), 1, 100, out _, out var target);
            Assert.IsTrue(target.View.Available);
            Assert.LessOrEqual(target.View.Position.x, 3);
            Assert.AreEqual(ResourceKind.FreshWater, target.View.Kind);
            var needs = new ActorNeeds(.5, 1);
            Assert.Greater(resources.Consume(target, target.View.Position, species, 100, 1, ref needs), 0);
            water.Ocean = true;
            Assert.AreEqual(0, resources.Consume(target, target.View.Position, species, 100, 1, ref needs));
            resources.Search(2, position, Vector3.right, species, perception, new ActorKnowledge(), 1, 100, out _, out target);
            Assert.IsFalse(target.View.Available);
            species = species with { Diet = ResourceKind.SaltWater };
            resources.Search(2, position, Vector3.right, species, perception, new ActorKnowledge(), 1, 100, out _, out target);
            Assert.IsTrue(target.View.Available);
            Assert.AreEqual(ResourceKind.SaltWater, target.View.Kind);
        }

        [Test]
        public void PerceptionBroadPhaseKeepsStrongSignalsAndRejectsDistantSignals()
        {
            var perception = new ActorPerception(new ActorPerceptionProfile(HearingRange: 10));
            var strong = new ActorStimulus(1, ActorObservationKind.Actor, ActorSense.Hearing, Vector3.right * 15, 4, 0, 1);
            var distant = new ActorStimulus(1, ActorObservationKind.Actor, ActorSense.Hearing, Vector3.right * 21, 4, 0, 1);
            Assert.IsTrue(perception.WithinRange(Vector3.zero, Vector3.forward, Vector3.up, 1, strong));
            Assert.IsFalse(perception.WithinRange(Vector3.zero, Vector3.forward, Vector3.up, 1, distant));
            Assert.IsTrue(perception.Observe(2, Vector3.zero, Vector3.forward, Vector3.up, 1, false, strong, 0, new ActorKnowledge()));
        }
    }
}
