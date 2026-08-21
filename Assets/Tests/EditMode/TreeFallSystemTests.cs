using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // The topple direction of a felled tree has to be a pure function of the tree, because the resting log it
    // produces is saved and, later, replicated. It used to read Random.onUnitSphere, so the same tree fell a
    // different way in every session. These tests pin the three properties that makes it safe to rely on:
    // it is deterministic, it varies between trees, and it is a direction a tree can actually fall.
    public sealed class TreeFallSystemTests
    {
        static readonly Vector3 Up = new Vector3(0.3f, 0.8f, -0.5f).normalized;
        static readonly SeedProvider Seeds = new SeedProvider(12345);

        [Test]
        public void SameTree_AlwaysFallsTheSameWay()
        {
            Vector3 first = TreeFallSystem.ToppleDirection(unchecked((int)0xDEADBEEF), Up);
            for (int i = 0; i < 10; i++)
                Assert.AreEqual(first, TreeFallSystem.ToppleDirection(unchecked((int)0xDEADBEEF), Up));
        }

        [Test]
        public void DifferentTrees_FallDifferentWays()
        {
            // Not a distribution test - just proof the id reaches the result at all. A hash that ignored the
            // id would still be deterministic, and would still be the bug.
            var seen = new System.Collections.Generic.HashSet<Vector3>();
            for (int id = 1; id <= 64; id++) seen.Add(TreeFallSystem.ToppleDirection(Seeds.GetSeedForEntity((ulong)id), Up));
            Assert.Greater(seen.Count, 50, "ids should spread across directions, not collapse onto a few");
        }

        [Test]
        public void ToppleDirection_IsHorizontal_AndUnitLength()
        {
            for (int id = 1; id <= 32; id++)
            {
                Vector3 d = TreeFallSystem.ToppleDirection(Seeds.GetSeedForEntity((ulong)id), Up);
                Assert.AreEqual(1f, d.magnitude, 1e-4f, "a direction, not a scale");
                Assert.AreEqual(0f, Vector3.Dot(d, Up), 1e-4f, "a tree topples sideways, not into the ground");
            }
        }

        [Test]
        public void SameTree_InDifferentWorlds_FallsDifferently()
        {
            // The seed provider folds the world seed in, so two worlds do not share a felling animation just
            // because the tree occupies the same cell. Same world, same tree is still identical.
            var worldA = new SeedProvider(12345);
            var worldB = new SeedProvider(54321);

            Assert.AreEqual(worldA.GetSeedForEntity(99UL), new SeedProvider(12345).GetSeedForEntity(99UL));
            Assert.AreNotEqual(worldA.GetSeedForEntity(99UL), worldB.GetSeedForEntity(99UL));
        }

        [Test]
        public void EntitySeed_UsesTheWholeKey_NotJustTheLowBits()
        {
            // ScatterIds pack face, level, x, y and slot across the full 64 bits, so an overload that dropped
            // the high half would give thousands of trees the same fall.
            var seeds = new SeedProvider(1);
            Assert.AreNotEqual(seeds.GetSeedForEntity(1UL), seeds.GetSeedForEntity(1UL << 32));
            Assert.AreNotEqual(seeds.GetSeedForEntity(1UL << 40), seeds.GetSeedForEntity(1UL << 48));
        }

        [Test]
        public void DegenerateUp_DoesNotProduceNaN()
        {
            // A zero radial cannot happen from a real surface point, but a NaN direction would silently
            // corrupt the saved log rotation rather than throwing where it could be seen.
            Vector3 d = TreeFallSystem.ToppleDirection(7, Vector3.zero);
            Assert.IsFalse(float.IsNaN(d.x) || float.IsNaN(d.y) || float.IsNaN(d.z));
            Assert.AreEqual(1f, d.magnitude, 1e-4f);

            // Up parallel to the world Y reference is the branch the basis has to sidestep.
            Vector3 polar = TreeFallSystem.ToppleDirection(7, Vector3.up);
            Assert.AreEqual(1f, polar.magnitude, 1e-4f);
            Assert.AreEqual(0f, Vector3.Dot(polar, Vector3.up), 1e-4f);
        }
    }
}
