using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // ScatterHarvestStore persists harvest override records per planet seed: a felled tree becomes a Stump
    // (with position + prototype so it can be rendered), then Dug. Both keep the standing tree out of the draw
    // (Contains == true); only Stump renders. Risk is the JSON round-trip (JsonUtility can't do ulong → ids are
    // strings) and the state machine. Tests touch persistentDataPath under an unlikely seed and clean up.
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

        static ScatterHarvestStore.HarvestNode Find(ScatterHarvestStore store, ulong id)
        {
            var list = new List<ScatterHarvestStore.HarvestNode>();
            store.CollectStumps(list);
            return list.Find(n => n.Id == id);
        }

        [Test]
        public void RecordStump_Persists_WithPositionAndProto_AcrossReload()
        {
            var a = new ScatterHarvestStore();
            a.Configure(TestSeed);
            ulong big = (1UL << 63) | 987654321UL; // > int.MaxValue: exercises the string round-trip
            Assert.IsTrue(a.RecordStump(100UL, new Vector3(1, 2, 3), 4));
            Assert.IsTrue(a.RecordStump(big, new Vector3(-5, 6, 7), 9));
            Assert.IsFalse(a.RecordStump(100UL, Vector3.zero, 0), "duplicate record returns false");
            Assert.AreEqual(2, a.Count);

            var reloaded = new ScatterHarvestStore();
            reloaded.Configure(TestSeed);
            Assert.IsTrue(reloaded.Contains(100UL));
            Assert.IsTrue(reloaded.Contains(big));
            Assert.IsFalse(reloaded.Contains(999UL));

            ScatterHarvestStore.HarvestNode n = Find(reloaded, big);
            Assert.AreEqual(big, n.Id);
            Assert.AreEqual(9, n.ProtoIndex);
            Assert.AreEqual(new Vector3(-5, 6, 7), n.Position);
            Assert.AreEqual(ScatterHarvestStore.HarvestState.Stump, n.State);
        }

        [Test]
        public void RecordDug_RemovesStump_ButStillBlocksStandingTree_AndPersists()
        {
            var a = new ScatterHarvestStore();
            a.Configure(TestSeed);
            a.RecordStump(100UL, new Vector3(1, 2, 3), 4);
            Assert.IsTrue(a.RecordDug(100UL));
            Assert.IsFalse(a.RecordDug(100UL), "digging an already-dug node returns false");

            var stumps = new List<ScatterHarvestStore.HarvestNode>();
            a.CollectStumps(stumps);
            Assert.AreEqual(0, stumps.Count, "a dug node is no longer a stump");
            Assert.IsTrue(a.Contains(100UL), "but the standing tree stays gone");

            var reloaded = new ScatterHarvestStore();
            reloaded.Configure(TestSeed);
            reloaded.CollectStumps(stumps);
            Assert.AreEqual(0, stumps.Count);
            Assert.IsTrue(reloaded.Contains(100UL));
        }

        [Test]
        public void Configure_WrongSeed_LoadsEmpty()
        {
            var a = new ScatterHarvestStore();
            a.Configure(TestSeed);
            a.RecordStump(100UL, Vector3.one, 0);

            var other = new ScatterHarvestStore();
            other.Configure(TestSeed + 1);
            Assert.IsFalse(other.Contains(100UL));
            Assert.AreEqual(0, other.Count);
        }
    }
}
