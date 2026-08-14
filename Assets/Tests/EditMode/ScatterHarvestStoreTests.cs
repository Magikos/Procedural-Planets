using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // ScatterHarvestStore persists the set of harvested scatter ids per planet seed, so a chopped tree stays
    // gone across reload/regen. The risk is the JSON round-trip: JsonUtility cannot serialize ulong, so ids
    // are stored as strings — an id above int range must survive. These tests touch persistentDataPath under a
    // seed unlikely to collide with a real world, and clean the file up.
    public sealed class ScatterHarvestStoreTests
    {
        const int TestSeed = 0x5CA77E5;

        static string FilePath(int seed) =>
            Path.Combine(Application.persistentDataPath, "ProceduralPlanets", $"scatter-harvest-{seed}.json");

        [SetUp, TearDown]
        public void CleanFiles()
        {
            foreach (int s in new[] { TestSeed, TestSeed + 1 })
                if (File.Exists(FilePath(s))) File.Delete(FilePath(s));
        }

        [Test]
        public void Add_Persists_AcrossReload_IncludingIdsAboveIntRange()
        {
            var a = new ScatterHarvestStore();
            a.Configure(TestSeed);
            ulong big = (1UL << 63) | 987654321UL; // > int.MaxValue: exercises the string round-trip
            Assert.IsTrue(a.Add(100UL));
            Assert.IsTrue(a.Add(big));
            Assert.IsFalse(a.Add(100UL), "duplicate add returns false");
            Assert.AreEqual(2, a.Count);

            var reloaded = new ScatterHarvestStore();
            reloaded.Configure(TestSeed);
            Assert.IsTrue(reloaded.Contains(100UL));
            Assert.IsTrue(reloaded.Contains(big));
            Assert.IsFalse(reloaded.Contains(999UL));
            Assert.AreEqual(2, reloaded.Count);
        }

        [Test]
        public void Configure_WrongSeed_LoadsEmpty()
        {
            var a = new ScatterHarvestStore();
            a.Configure(TestSeed);
            a.Add(100UL);

            var other = new ScatterHarvestStore();
            other.Configure(TestSeed + 1);
            Assert.IsFalse(other.Contains(100UL));
            Assert.AreEqual(0, other.Count);
        }
    }
}
