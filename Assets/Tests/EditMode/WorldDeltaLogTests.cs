using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // WorldDeltaLog is the one write path for persistent world change: an append-only file of fixed-size
    // records that compacts into a base file. It replaces three JSON stores that rewrote the whole file on
    // every chop, so the risks worth testing are the ones that lose a save rather than one action - the
    // binary round-trip, a torn tail from a crash mid-append, and the compaction rename ordering.
    public sealed class WorldDeltaLogTests
    {
        const string WorldKey = "test-world";
        string _dir;

        string LogPath => Path.Combine(_dir, WorldKey + ".log");
        string BasePath => Path.Combine(_dir, WorldKey + ".sav");

        [SetUp]
        public void MakeCleanDirectory()
        {
            _dir = Path.Combine(Application.temporaryCachePath, "WorldDeltaLogTests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void RemoveDirectory()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        static WorldDelta Chop(ulong key, Vector3 position, int proto, byte state = 0) =>
            new(0, DeltaKind.ScatterState, key, position, Quaternion.Euler(10f, 20f, 30f), proto, state);

        [Test]
        public void EveryField_SurvivesTheBinaryRoundTrip()
        {
            // > int.MaxValue and a negative TypeIndex: the byte packing must not be sign- or width-lossy.
            ulong big = (1UL << 63) | 987654321UL;
            var written = new WorldDelta(7, DeltaKind.EntitySpawned, big,
                new Vector3(-1234.5f, 6789.25f, 0.125f), new Quaternion(0.1f, -0.2f, 0.3f, -0.9f), -42, 200);

            using (var log = new WorldDeltaLog())
            {
                log.Open(_dir, WorldKey);
                log.Append(written);
            }

            using var reloaded = new WorldDeltaLog();
            reloaded.Open(_dir, WorldKey);
            Assert.IsTrue(reloaded.TryGet(big, out WorldDelta read));
            Assert.AreEqual(DeltaKind.EntitySpawned, read.Kind);
            Assert.AreEqual(big, read.Key);
            Assert.AreEqual(written.Position, read.Position);
            Assert.AreEqual(written.Rotation, read.Rotation);
            Assert.AreEqual(-42, read.TypeIndex);
            Assert.AreEqual(200, read.State);
            Assert.AreEqual(0, reloaded.TornRecordsDropped);
        }

        [Test]
        public void Log_AssignsSequences_AndIgnoresTheCallersValue()
        {
            using var log = new WorldDeltaLog();
            log.Open(_dir, WorldKey);

            Assert.AreEqual(1u, log.Append(new WorldDelta(999, DeltaKind.ScatterRemoved, 10UL)).Sequence);
            Assert.AreEqual(2u, log.Append(new WorldDelta(999, DeltaKind.ScatterRemoved, 11UL)).Sequence);

            // The host is the ordering authority, so sequences must keep climbing across a reload rather
            // than restarting and colliding with records already on disk.
            log.Close();
            using var reopened = new WorldDeltaLog();
            reopened.Open(_dir, WorldKey);
            Assert.AreEqual(3u, reopened.Append(new WorldDelta(0, DeltaKind.ScatterRemoved, 12UL)).Sequence);
        }

        [Test]
        public void LastWritePerKeyWins_SoTheLiveSetTracksThingsNotChanges()
        {
            using var log = new WorldDeltaLog();
            log.Open(_dir, WorldKey);

            log.Append(Chop(500UL, Vector3.one, 3, state: 0));      // stump
            log.Append(Chop(500UL, Vector3.one, 3, state: 1));      // later dug
            log.Append(Chop(501UL, Vector3.zero, 4));

            Assert.AreEqual(2, log.Count, "two keys touched, so two live records");
            Assert.IsTrue(log.TryGet(500UL, out WorldDelta d));
            Assert.AreEqual(1, d.State);
            Assert.IsFalse(log.TryGet(9999UL, out _));
        }

        [Test]
        public void TornTail_LosesOnlyTheIncompleteRecord()
        {
            using (var log = new WorldDeltaLog())
            {
                log.Open(_dir, WorldKey);
                log.Append(Chop(1UL, Vector3.right, 1));
                log.Append(Chop(2UL, Vector3.up, 2));
                log.Append(Chop(3UL, Vector3.forward, 3));
            }

            // A crash mid-append: the last record reached disk only in part.
            using (var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Write))
                fs.SetLength(fs.Length - 20);

            using var reloaded = new WorldDeltaLog();
            reloaded.Open(_dir, WorldKey);
            Assert.IsTrue(reloaded.TryGet(1UL, out _), "records before the tear survive");
            Assert.IsTrue(reloaded.TryGet(2UL, out _));
            Assert.IsFalse(reloaded.TryGet(3UL, out _), "the torn record is dropped, not half-read");
            Assert.AreEqual(1, reloaded.TornRecordsDropped);
        }

        [Test]
        public void CorruptedRecord_IsRejectedByItsChecksum()
        {
            using (var log = new WorldDeltaLog())
            {
                log.Open(_dir, WorldKey);
                log.Append(Chop(1UL, Vector3.right, 1));
                log.Append(Chop(2UL, Vector3.up, 2));
            }

            byte[] bytes = File.ReadAllBytes(LogPath);
            bytes[^10] ^= 0xFF;                                     // bit rot inside the last record
            File.WriteAllBytes(LogPath, bytes);

            using var reloaded = new WorldDeltaLog();
            reloaded.Open(_dir, WorldKey);
            Assert.IsTrue(reloaded.TryGet(1UL, out _));
            Assert.IsFalse(reloaded.TryGet(2UL, out _));
            Assert.AreEqual(1, reloaded.TornRecordsDropped);
        }

        [Test]
        public void Compaction_FoldsTheLogIntoTheBase_AndKeepsEveryLiveRecord()
        {
            // A threshold of a few records so compaction fires during the loop rather than needing 21k appends.
            using (var log = new WorldDeltaLog(compactThreshold: (WorldDelta.FixedBytes + WorldDelta.ChecksumBytes) * 4))
            {
                log.Open(_dir, WorldKey);
                for (ulong i = 0; i < 20; i++) log.Append(Chop(i, new Vector3(i, 0, 0), (int)i));
                Assert.AreEqual(20, log.Count);
            }

            Assert.IsTrue(File.Exists(BasePath), "compaction produced a base file");
            Assert.IsFalse(File.Exists(BasePath + ".tmp"), "the temp file was renamed, not left behind");

            using var reloaded = new WorldDeltaLog();
            reloaded.Open(_dir, WorldKey);
            Assert.AreEqual(20, reloaded.Count);
            Assert.AreEqual(0, reloaded.TornRecordsDropped);
            for (ulong i = 0; i < 20; i++)
            {
                Assert.IsTrue(reloaded.TryGet(i, out WorldDelta d), $"key {i} survived compaction");
                Assert.AreEqual(new Vector3(i, 0, 0), d.Position);
            }
        }

        [Test]
        public void RecordsAppendedAfterCompaction_MergeWithTheBaseOnReload()
        {
            using (var log = new WorldDeltaLog(compactThreshold: (WorldDelta.FixedBytes + WorldDelta.ChecksumBytes) * 4))
            {
                log.Open(_dir, WorldKey);
                for (ulong i = 0; i < 10; i++) log.Append(Chop(i, Vector3.zero, 1));
                log.Compact();
                log.Append(Chop(100UL, Vector3.one, 7));            // lands in the fresh log, not the base
            }

            Assert.IsTrue(File.Exists(LogPath));
            using var reloaded = new WorldDeltaLog();
            reloaded.Open(_dir, WorldKey);
            Assert.AreEqual(11, reloaded.Count, "base plus log, with neither dropped");
            Assert.IsTrue(reloaded.TryGet(100UL, out WorldDelta d));
            Assert.AreEqual(7, d.TypeIndex);
        }

        [Test]
        public void Payload_SurvivesTheRoundTrip_AndIsFramedByItsLength()
        {
            // A surface stamp carries nine fields, which is why the record grew a payload rather than staying
            // fixed. Mixing payload and payload-free records also proves the length prefix frames correctly:
            // get it wrong and the reader walks into the middle of the next record.
            var stamp = new byte[137];
            for (int i = 0; i < stamp.Length; i++) stamp[i] = (byte)(i * 7);

            using (var log = new WorldDeltaLog())
            {
                log.Open(_dir, WorldKey);
                log.Append(new WorldDelta(0, DeltaKind.ScatterRemoved, 1UL));                     // no payload
                log.Append(new WorldDelta(0, DeltaKind.SurfaceStamp, 2UL, payload: stamp));       // payload
                log.Append(new WorldDelta(0, DeltaKind.ScatterState, 3UL, Vector3.one, typeIndex: 5));
            }

            using var reloaded = new WorldDeltaLog();
            reloaded.Open(_dir, WorldKey);
            Assert.AreEqual(3, reloaded.Count);
            Assert.AreEqual(0, reloaded.TornRecordsDropped);

            Assert.IsTrue(reloaded.TryGet(2UL, out WorldDelta withPayload));
            CollectionAssert.AreEqual(stamp, withPayload.Payload);

            Assert.IsTrue(reloaded.TryGet(1UL, out WorldDelta without));
            Assert.IsNull(without.Payload, "a payload-free record stays payload-free");

            Assert.IsTrue(reloaded.TryGet(3UL, out WorldDelta after));
            Assert.AreEqual(5, after.TypeIndex, "the record after a payload is still framed correctly");
            Assert.AreEqual(Vector3.one, after.Position);
        }

        [Test]
        public void EmptyPayload_IsTheSameAsNoPayload()
        {
            var d = new WorldDelta(0, DeltaKind.EntityState, 1UL, payload: new byte[0]);
            Assert.IsNull(d.Payload);
            Assert.AreEqual(0, d.PayloadLength);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new WorldDelta(0, DeltaKind.EntityState, 1UL, payload: new byte[WorldDelta.MaxPayloadBytes + 1]));
        }

        [Test]
        public void MissingWorld_OpensEmpty_RatherThanThrowing()
        {
            using var log = new WorldDeltaLog();
            log.Open(Path.Combine(_dir, "nested", "never-saved"), "fresh");
            Assert.AreEqual(0, log.Count);
            Assert.AreEqual(1u, log.NextSequence);
            Assert.IsTrue(log.IsOpen);
        }
    }
}
