using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // Surface stamps are the durable world state - path wear and scorch are caches rebuilt from them - and
    // they now live in the world delta log rather than in surface-edits-{seed}.json. A stamp has nine fields
    // and no transform, so it rides the record's opaque payload; the risks worth testing are the ones that
    // lose edits rather than one brush stroke: the payload encoding, the fold from records back to stamps,
    // and the one-shot import of the old JSON, which must not run twice and must not eat the player's file.
    public sealed class SurfaceEditControllerTests
    {
        const int TestSeed = 0x5EDC70;

        string _dir;

        static string LegacyPath(int seed) =>
            Path.Combine(Application.persistentDataPath, "ProceduralPlanets", $"surface-edits-{seed}.json");

        [SetUp]
        public void MakeCleanDirectory()
        {
            _dir = Path.Combine(Application.temporaryCachePath, "SurfaceEditControllerTests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            Directory.CreateDirectory(_dir);
            CleanLegacyFile();
        }

        [TearDown]
        public void Cleanup()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            CleanLegacyFile();
        }

        static void CleanLegacyFile()
        {
            string path = LegacyPath(TestSeed);
            if (File.Exists(path)) File.Delete(path);
        }

        WorldDeltaLog OpenLog()
        {
            var log = new WorldDeltaLog();
            log.Open(_dir, "world-" + TestSeed);
            return log;
        }

        // No provider and no material: painting needs a generated chunked planet, but the stamp list, its
        // encoding and its persistence do not, and those are what is under test.
        static SurfaceEditController Controller(WorldDeltaLog log)
        {
            var controller = new SurfaceEditController(null, new UnityLogger(), null);
            controller.Configure(null, null, TestSeed, log);
            return controller;
        }

        static SurfaceEditStamp Sample() => new()
        {
            kind = SurfaceEditStampCodec.ScorchKind,
            shape = "hard-square",
            operation = "erase",
            strokeId = 12345,
            direction = new Vector3(0.6f, -0.8f, 0f),
            radiusMeters = 7.25f,
            strength = 0.375f,
            createdUnixSeconds = 1767000000L,
            regrowSeconds = 42.5f,
        };

        static void AssertSameStamp(SurfaceEditStamp expected, SurfaceEditStamp actual)
        {
            Assert.AreEqual(expected.kind, actual.kind);
            Assert.AreEqual(expected.shape, actual.shape);
            Assert.AreEqual(expected.operation, actual.operation);
            Assert.AreEqual(expected.strokeId, actual.strokeId);
            Assert.AreEqual(expected.direction, actual.direction);
            Assert.AreEqual(expected.radiusMeters, actual.radiusMeters);
            Assert.AreEqual(expected.strength, actual.strength);
            Assert.AreEqual(expected.createdUnixSeconds, actual.createdUnixSeconds);
            Assert.AreEqual(expected.regrowSeconds, actual.regrowSeconds);
        }

        [Test]
        public void EveryStampField_RoundTripsThroughTheRecord()
        {
            SurfaceEditStamp stamp = Sample();
            WorldDelta record = SurfaceEditStampCodec.Encode(77UL, stamp);

            Assert.AreEqual(DeltaKind.SurfaceStamp, record.Kind);
            Assert.AreEqual(77UL, record.Key);
            Assert.AreEqual(SurfaceEditStampCodec.StatePresent, record.State);
            Assert.AreEqual(stamp.direction, record.Position,
                "the direction is hoisted so spatial code can place a stamp without decoding the payload");

            Assert.IsTrue(SurfaceEditStampCodec.TryDecode(record, out SurfaceEditStamp read));
            Assert.AreEqual(77UL, read.recordId);
            AssertSameStamp(stamp, read);
        }

        [Test]
        public void StampRecord_SurvivesTheBinaryRoundTrip()
        {
            // The payload is opaque to the log, so nothing but this checks that a stamp written to disk is the
            // stamp that comes back - length framing, checksum and all.
            SurfaceEditStamp stamp = Sample();
            using (WorldDeltaLog log = OpenLog())
                log.Append(SurfaceEditStampCodec.Encode(9UL, stamp));

            using WorldDeltaLog reloaded = OpenLog();
            Assert.AreEqual(0, reloaded.TornRecordsDropped);
            Assert.IsTrue(reloaded.TryGet(DeltaKind.SurfaceStamp, 9UL, out WorldDelta record));
            Assert.IsTrue(SurfaceEditStampCodec.TryDecode(record, out SurfaceEditStamp read));
            AssertSameStamp(stamp, read);
        }

        [Test]
        public void Tombstone_CarriesNoPayload_AndDecodesAsNothing()
        {
            // Removal has to be a record of its own: the stamp it erases is still in the log, and a client
            // that replayed only the additions would paint it straight back.
            WorldDelta tombstone = SurfaceEditStampCodec.Tombstone(5UL);
            Assert.AreEqual(SurfaceEditStampCodec.StateRemoved, tombstone.State);
            Assert.AreEqual(0, tombstone.PayloadLength);
            Assert.IsFalse(SurfaceEditStampCodec.TryDecode(tombstone, out _));
        }

        [Test]
        public void LegacyJson_IsImportedOnce_AndTheFileIsLeftAlone()
        {
            WriteLegacyJson(TestSeed);

            using (WorldDeltaLog log = OpenLog())
            {
                IReadOnlyList<SurfaceEditStamp> stamps = Controller(log).SavedStamps;
                Assert.AreEqual(2, stamps.Count, "both stamps carried over");
                AssertSameStamp(FirstFixtureStamp(), stamps[0]);
                Assert.AreEqual(SurfaceEditStampCodec.ScorchKind, stamps[1].kind);
                Assert.AreEqual(2, stamps[1].strokeId);
            }

            Assert.IsTrue(File.Exists(LegacyPath(TestSeed)),
                "the old save is the player's data; the import reads it and leaves it where it is");

            // Re-importing after the player edited more would paint back what they cleared, so the guard is
            // the log itself: it already holds surface-stamp records, so there is nothing to import.
            using WorldDeltaLog reopened = OpenLog();
            Assert.AreEqual(2, Controller(reopened).SavedStamps.Count, "the import did not run twice");
        }

        [Test]
        public void ImportedStamps_ComeBackIdentical_FromTheLogAlone()
        {
            WriteLegacyJson(TestSeed);

            var imported = new List<SurfaceEditStamp>();
            using (WorldDeltaLog log = OpenLog())
                imported.AddRange(Controller(log).SavedStamps);

            using WorldDeltaLog reopened = OpenLog();
            IReadOnlyList<SurfaceEditStamp> reloaded = Controller(reopened).SavedStamps;
            Assert.AreEqual(imported.Count, reloaded.Count);
            for (int i = 0; i < imported.Count; i++)
                AssertSameStamp(imported[i], reloaded[i]);
        }

        [Test]
        public void ClearedStamps_StayCleared_AcrossAReload()
        {
            WriteLegacyJson(TestSeed);

            using (WorldDeltaLog log = OpenLog())
            {
                SurfaceEditController a = Controller(log);
                Assert.AreEqual(2, a.SavedStamps.Count);
                a.ClearSavedStamps();
                Assert.AreEqual(0, a.SavedStamps.Count);
            }

            using WorldDeltaLog reopened = OpenLog();
            Assert.AreEqual(0, Controller(reopened).SavedStamps.Count,
                "the tombstones survived, and they also stop the JSON being imported a second time");
        }

        [Test]
        public void ClearingScorch_LeavesThePathStamps()
        {
            WriteLegacyJson(TestSeed);

            using WorldDeltaLog log = OpenLog();
            SurfaceEditController a = Controller(log);
            a.ClearSavedScorchStamps();

            Assert.AreEqual(1, a.SavedStamps.Count);
            Assert.AreEqual(SurfaceEditStampCodec.PathKind, a.SavedStamps[0].kind);
        }

        [Test]
        public void DifferentWorld_DoesNotSeeAnotherWorldsStamps()
        {
            WriteLegacyJson(TestSeed);
            using (WorldDeltaLog log = OpenLog())
                Assert.AreEqual(2, Controller(log).SavedStamps.Count);

            var other = new WorldDeltaLog();
            other.Open(_dir, "world-other");
            using (other)
            {
                var controller = new SurfaceEditController(null, new UnityLogger(), null);
                controller.Configure(null, null, TestSeed + 1, other);
                Assert.AreEqual(0, controller.SavedStamps.Count,
                    "a seed with no log and no JSON of its own starts empty");
            }
        }

        static SurfaceEditStamp FirstFixtureStamp() => new()
        {
            kind = SurfaceEditStampCodec.PathKind,
            shape = "disc",
            operation = "paint",
            strokeId = 1,
            direction = new Vector3(0f, 1f, 0f),
            radiusMeters = 3.5f,
            strength = 0.8f,
            createdUnixSeconds = 1700000000L,
            regrowSeconds = 0f,
        };

        // Written as literal JSON rather than through the current types: the point is to read a file the OLD
        // build produced, so a change to the current types must not quietly change the fixture.
        static void WriteLegacyJson(int seed)
        {
            string path = LegacyPath(seed);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,
                "{\"version\":1,\"planetSeed\":" + seed + ",\"stamps\":[" +
                "{\"kind\":\"path\",\"shape\":\"disc\",\"operation\":\"paint\",\"strokeId\":1," +
                "\"direction\":{\"x\":0,\"y\":1,\"z\":0},\"radiusMeters\":3.5,\"strength\":0.8," +
                "\"createdUnixSeconds\":1700000000,\"regrowSeconds\":0}," +
                "{\"kind\":\"scorch\",\"shape\":\"hard-disc\",\"operation\":\"erase\",\"strokeId\":2," +
                "\"direction\":{\"x\":1,\"y\":0,\"z\":0},\"radiusMeters\":12,\"strength\":1," +
                "\"createdUnixSeconds\":1700000001,\"regrowSeconds\":0}]}");
        }
    }
}
