using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // ScatterHarvestStore is a view over the world delta log: a felled tree becomes a Stump (with position +
    // prototype so it can be rendered), then Dug. Both keep the standing tree out of the draw (Contains ==
    // true); only Stump renders. The risks are the fold from records back to state - a reloaded world must
    // match a live one - and the import of the old per-seed JSON save, which must not run twice.
    public sealed class ScatterHarvestStoreTests
    {
        const int TestSeed = 0x5CA77E5;

        string _dir;

        static string LegacyPath(int seed) =>
            Path.Combine(Application.persistentDataPath, "ProceduralPlanets", $"scatter-harvest-{seed}.json");

        [SetUp]
        public void MakeCleanDirectory()
        {
            _dir = Path.Combine(Application.temporaryCachePath, "ScatterHarvestStoreTests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            Directory.CreateDirectory(_dir);
            CleanLegacyFiles();
        }

        [TearDown]
        public void Cleanup()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            CleanLegacyFiles();
        }

        static void CleanLegacyFiles()
        {
            foreach (int s in new[] { TestSeed, TestSeed + 1 })
                foreach (string p in new[] { LegacyPath(s), LegacyPath(s) + ".migrated" })
                    if (File.Exists(p)) File.Delete(p);
        }

        WorldDeltaLog OpenLog(int seed)
        {
            var log = new WorldDeltaLog();
            log.Open(_dir, "world-" + seed);
            return log;
        }

        ScatterHarvestStore Store(WorldDeltaLog log, int seed = TestSeed)
        {
            var store = new ScatterHarvestStore();
            store.Configure(seed, log);
            return store;
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
            ulong big = (1UL << 63) | 987654321UL;      // > int.MaxValue: the id must survive the round trip
            using (WorldDeltaLog log = OpenLog(TestSeed))
            {
                ScatterHarvestStore a = Store(log);
                Assert.IsTrue(a.RecordStump(100UL, new Vector3(1, 2, 3), 4));
                Assert.IsTrue(a.RecordStump(big, new Vector3(-5, 6, 7), 9));
                Assert.IsFalse(a.RecordStump(100UL, Vector3.zero, 0), "duplicate record returns false");
                Assert.AreEqual(2, a.Count);
            }

            using WorldDeltaLog reopened = OpenLog(TestSeed);
            ScatterHarvestStore reloaded = Store(reopened);
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
            var stumps = new List<ScatterHarvestStore.HarvestNode>();

            using (WorldDeltaLog log = OpenLog(TestSeed))
            {
                ScatterHarvestStore a = Store(log);
                a.RecordStump(100UL, new Vector3(1, 2, 3), 4);
                Assert.IsTrue(a.RecordDug(100UL));
                Assert.IsFalse(a.RecordDug(100UL), "digging an already-dug node returns false");

                a.CollectStumps(stumps);
                Assert.AreEqual(0, stumps.Count, "a dug node is no longer a stump");
                Assert.IsTrue(a.Contains(100UL), "but the standing tree stays gone");
            }

            using WorldDeltaLog reopened = OpenLog(TestSeed);
            ScatterHarvestStore reloaded = Store(reopened);
            reloaded.CollectStumps(stumps);
            Assert.AreEqual(0, stumps.Count);
            Assert.IsTrue(reloaded.Contains(100UL));
        }

        [Test]
        public void DiggingAStump_LeavesOneRecord_NotTwo()
        {
            // Stump then Dug is a state machine on one tree, so the log must collapse it rather than grow
            // per chop - that is what keeps the save proportional to changed things.
            using WorldDeltaLog log = OpenLog(TestSeed);
            ScatterHarvestStore a = Store(log);
            a.RecordStump(100UL, Vector3.one, 4);
            a.RecordDug(100UL);
            Assert.AreEqual(1, log.Count);
        }

        [Test]
        public void RecordLog_Persists_PositionRotationProto_AcrossReload()
        {
            var rot = Quaternion.Euler(12f, 34f, 56f);
            ulong logId;

            using (WorldDeltaLog log = OpenLog(TestSeed))
            {
                ScatterHarvestStore a = Store(log);
                logId = a.RecordLog(new Vector3(3, 4, 5), rot, 7);
                Assert.AreNotEqual(0UL, logId);
            }

            using WorldDeltaLog reopened = OpenLog(TestSeed);
            ScatterHarvestStore reloaded = Store(reopened);
            var logs = new List<ScatterHarvestStore.LogRecord>();
            reloaded.CollectLogs(logs);
            Assert.AreEqual(1, logs.Count);
            Assert.AreEqual(new Vector3(3, 4, 5), logs[0].Position);
            Assert.AreEqual(7, logs[0].ProtoIndex);
            Assert.Less(Quaternion.Angle(rot, logs[0].Rotation), 0.5f, "rotation round-trips");

            Assert.IsTrue(reloaded.RemoveLog(logId));
            reloaded.CollectLogs(logs);
            Assert.AreEqual(0, logs.Count, "removed log is gone");
            Assert.IsFalse(reloaded.RemoveLog(logId), "removing twice returns false");
        }

        [Test]
        public void LogIds_DoNotRepeat_AfterAReload()
        {
            // The allocator restarts at 1 each session, so without replaying the saved ids a second chop
            // would mint an id a live log already holds and overwrite it.
            ulong first;
            using (WorldDeltaLog log = OpenLog(TestSeed))
                first = Store(log).RecordLog(Vector3.zero, Quaternion.identity, 1);

            using WorldDeltaLog reopened = OpenLog(TestSeed);
            ScatterHarvestStore reloaded = Store(reopened);
            ulong second = reloaded.RecordLog(Vector3.one, Quaternion.identity, 2);

            Assert.AreNotEqual(first, second);
            var logs = new List<ScatterHarvestStore.LogRecord>();
            reloaded.CollectLogs(logs);
            Assert.AreEqual(2, logs.Count, "the second log did not overwrite the first");
        }

        [Test]
        public void RemovedLog_StaysRemoved_AcrossReload()
        {
            ulong logId;
            using (WorldDeltaLog log = OpenLog(TestSeed))
            {
                ScatterHarvestStore a = Store(log);
                logId = a.RecordLog(Vector3.zero, Quaternion.identity, 1);
                Assert.IsTrue(a.RemoveLog(logId));
            }

            using WorldDeltaLog reopened = OpenLog(TestSeed);
            var logs = new List<ScatterHarvestStore.LogRecord>();
            Store(reopened).CollectLogs(logs);
            Assert.AreEqual(0, logs.Count, "the tombstone survived, so the log did not come back");
        }

        [Test]
        public void Configure_DifferentWorld_LoadsEmpty()
        {
            using (WorldDeltaLog log = OpenLog(TestSeed))
                Store(log).RecordStump(100UL, Vector3.one, 0);

            using WorldDeltaLog other = OpenLog(TestSeed + 1);
            ScatterHarvestStore store = Store(other, TestSeed + 1);
            Assert.IsFalse(store.Contains(100UL));
            Assert.AreEqual(0, store.Count);
        }

        [Test]
        public void LegacyJsonSave_IsImportedOnce_ThenMovedAside()
        {
            WriteLegacySave(TestSeed);

            using (WorldDeltaLog log = OpenLog(TestSeed))
            {
                ScatterHarvestStore a = Store(log);
                Assert.IsTrue(a.Contains(100UL), "the old save's stump carried over");
                ScatterHarvestStore.HarvestNode n = Find(a, 100UL);
                Assert.AreEqual(new Vector3(1, 2, 3), n.Position);
                Assert.AreEqual(4, n.ProtoIndex);

                var logs = new List<ScatterHarvestStore.LogRecord>();
                a.CollectLogs(logs);
                Assert.AreEqual(1, logs.Count, "the old save's fallen log carried over");
            }

            Assert.IsFalse(File.Exists(LegacyPath(TestSeed)), "the imported file was moved aside");
            Assert.IsTrue(File.Exists(LegacyPath(TestSeed) + ".migrated"), "and kept, not deleted");

            // Re-importing after the player chopped more would resurrect what they removed, so the second
            // open must find nothing to import.
            using WorldDeltaLog reopened = OpenLog(TestSeed);
            ScatterHarvestStore reloaded = Store(reopened);
            Assert.AreEqual(1, reloaded.Count, "the import did not run twice");
            var logsAfter = new List<ScatterHarvestStore.LogRecord>();
            reloaded.CollectLogs(logsAfter);
            Assert.AreEqual(1, logsAfter.Count);
        }

        static void WriteLegacySave(int seed)
        {
            string path = LegacyPath(seed);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // Written as literal JSON rather than through the store's own types: the point is to read a file
            // the OLD build produced, so a change to the current types must not quietly change the fixture.
            File.WriteAllText(path,
                "{\"version\":3,\"planetSeed\":" + seed + ",\"nextLogId\":2," +
                "\"nodes\":[{\"id\":\"100\",\"position\":{\"x\":1,\"y\":2,\"z\":3},\"proto\":4,\"state\":0}]," +
                "\"logs\":[{\"id\":\"1\",\"position\":{\"x\":9,\"y\":8,\"z\":7}," +
                "\"rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"proto\":5}]}");
        }
    }
}
