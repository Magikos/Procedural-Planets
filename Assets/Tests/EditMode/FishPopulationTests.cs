using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class FishPopulationTests
    {
        sealed class Water : IWaterQueryService
        {
            public bool Ocean, Available = true, SplitKind;
            public float Depth = 30f;
            public bool TryGetWaterSurface(Vector3 p, out WaterSample sample)
            {
                sample = new WaterSample(new Vector3(p.x, 0f, p.z), Vector3.up, -p.y, Depth, 1,
                    SplitKind ? p.x < 0f : Ocean);
                return Available;
            }
            public bool IsUnderwater(Vector3 p) => TryGetWaterSurface(p, out var s) && s.IsSubmerged;
        }

        [Test]
        public void SharksRejectLakesAndShallowOceanAtSpawnAndDuringTravel()
        {
            var water = new Water();
            Vector3 start = new(-3f, -5f, 0f);
            Assert.IsFalse(FishMovement.IsHabitat(water, start, 1, 1.8f, FishSpecies.Shark));
            Assert.Throws<ArgumentException>(() => new FishSchool(water, new[] { start }, Vector3.forward, FishSpecies.Shark));
            water.Ocean = true;
            water.Depth = 7f;
            Assert.IsFalse(FishMovement.IsHabitat(water, start, 1, 1.8f, FishSpecies.Shark));
            water.Depth = 30f;
            Assert.IsTrue(FishMovement.IsHabitat(water, start, 1, 1.8f, FishSpecies.Shark));
            water.SplitKind = true;
            Assert.IsFalse(FishMovement.TryMove(water, start, new Vector3(3f, -5f, 0f), 1, 1.8f, out var result, FishSpecies.Shark));
            Assert.AreEqual(start, result);
            Assert.IsFalse(FishMovement.IsHabitat(water, new Vector3(-0.5f, -5f, 0f), 1, 1.8f, FishSpecies.Shark),
                "A valid centre cannot put the shark's body across the habitat boundary.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PopulationSpawnsMatchingSpeciesAndClearsWhenWaterDisappears(bool ocean)
        {
            var water = new Water { Ocean = ocean };
            var threats = new ThreatRegistry();
            using var population = new FishPopulation(water, threats);
            population.Configure(123, Vector3.zero);
            Vector3 observer = new(0f, 2f, 0f);
            for (int i = 0; i < 20; i++) population.Tick(observer, 0.1f, 100);
            Assert.Greater(population.Groups.Count, 0);
            Assert.LessOrEqual(population.Groups.Count, 7);
            Assert.AreEqual(ocean ? 1 : 0, population.Groups.Count(g => g.Species == FishSpecies.Shark));
            foreach (var group in population.Groups)
            {
                Assert.AreEqual(ocean ? FishHabitat.Ocean : FishHabitat.Freshwater, group.Species.Habitat);
                foreach (Vector3 position in group.School.Positions)
                    Assert.IsTrue(FishMovement.IsHabitat(water, position, 1, group.Species.Clearance, group.Species));
            }
            water.Available = false;
            population.Tick(observer, 0.1f, 100);
            Assert.AreEqual(0, population.Groups.Count);
            Assert.AreEqual(0, threats.Sources.Count);
        }

        [Test]
        public void RegenerationAndObserverDepartureReleaseSharkThreats()
        {
            var water = new Water { Ocean = true };
            var threats = new ThreatRegistry();
            using var population = new FishPopulation(water, threats);
            population.Configure(123, Vector3.zero);
            population.Tick(Vector3.up * 2f, 0.1f, 100);
            population.Tick(Vector3.up * 2f, 0.1f, 100);
            Assert.Greater(threats.Sources.Count, 0);
            population.Configure(456, Vector3.zero);
            Assert.AreEqual(0, population.Groups.Count);
            Assert.AreEqual(0, threats.Sources.Count);
            population.Tick(Vector3.up * 2f, 0.1f, 100);
            population.Tick(Vector3.up * 1000f, 0.1f, 100);
            Assert.AreEqual(0, population.Groups.Count);
            Assert.AreEqual(0, threats.Sources.Count);
        }

        [Test]
        public void SchoolTurnsAndSwimsAndPlayerDisguiseChangesEscape()
        {
            var water = new Water();
            Vector3 start = new(0f, -5f, 0f);
            var calm = new FishSchool(water, new[] { start }, Vector3.forward, FishSpecies.Freshwater);
            var fleeing = new FishSchool(water, new[] { start }, Vector3.forward, FishSpecies.Freshwater);
            var friendly = new FishSchool(water, new[] { start }, Vector3.forward, FishSpecies.Freshwater);
            var threats = new ThreatRegistry();
            threats.Report(ThreatRegistry.LocalPlayer, start + Vector3.forward, CreatureFaction.Player);
            for (int i = 0; i < 60; i++) { calm.Tick(1f / 30f, null, 100); fleeing.Tick(1f / 30f, threats, 100); }
            Assert.Greater(Vector3.Distance(start, calm.Positions[0]), 1f);
            Assert.Greater(Vector3.Distance(calm.Positions[0], fleeing.Positions[0]), 1f);
            // The existing registry tests cover disguise expiry. This checks the swimming consumer.
            threats.SetDisguise(ThreatRegistry.LocalPlayer, CreatureFaction.Wildlife, 10f, 100);
            for (int i = 0; i < 60; i++) friendly.Tick(1f / 30f, threats, 100);
            Assert.Less(Vector3.Distance(calm.Positions[0], friendly.Positions[0]), 0.001f);
        }

        [TestCase(1f)]
        [TestCase(2f)]
        public void FishModelsAnimateAndReleaseGraphsOnClear(float parentScale)
        {
            var water = new Water { Ocean = true };
            using var population = new FishPopulation(water, new ThreatRegistry());
            population.Configure(123, Vector3.zero);
            population.Tick(Vector3.up * 2f, 0.1f, 100);
            var root = new GameObject("Fish view test");
            root.transform.position = new Vector3(100f, 200f, 300f);
            root.transform.localScale = Vector3.one * parentScale;
            try
            {
                using var view = new FishView(root.transform);
                view.Sync(population.Groups, 0.1f);
                Assert.Greater(view.BodyCount, 0);
                var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>();
                Assert.AreEqual(view.BodyCount, skins.Length);
                foreach (var skin in skins)
                {
                    Assert.Greater(skin.bounds.size.magnitude, 0.1f, "Imported scale must produce a visible fish.");
                    var group = population.Groups.First(g => skin.transform.root == root.transform &&
                        skin.transform.parent.parent.name.Contains(g.Id.Value.ToString()));
                    // World AABBs grow when rotated. Check actual oriented bounds against the body sphere.
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 offset = Vector3.Scale(skin.localBounds.extents, new Vector3(
                            (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                        Vector3 point = skin.rootBone.TransformPoint(skin.localBounds.center + offset);
                        Assert.LessOrEqual(Vector3.Distance(point, skin.transform.parent.parent.position), group.Species.Clearance);
                    }
                }
                var graphs = root.GetComponentsInChildren<Animator>().Select(a => a.playableGraph).ToArray();
                var first = new Mesh();
                var second = new Mesh();
                try
                {
                    skins[0].BakeMesh(first);
                    view.Sync(population.Groups, 0.35f);
                    skins[0].BakeMesh(second);
                    var after = second.vertices;
                    Assert.IsTrue(first.vertices.Where((v, i) => Vector3.Distance(v, after[i]) > 0.0001f).Any());
                }
                finally { UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second); }
                view.Clear();
                Assert.AreEqual(0, view.BodyCount);
                foreach (var graph in graphs) Assert.IsFalse(graph.IsValid());
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
