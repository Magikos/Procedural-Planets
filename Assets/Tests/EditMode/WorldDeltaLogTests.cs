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
                new Vector3(-1234.5f, 6789.25f, 0.125f), new Quaternion(0.1f, -0.2f, 0.3f, -0.9f), -42, 200,
                scale: 3.4375f);

            using (var log = new WorldDeltaLog())
            {
                log.Open(_dir, WorldKey);
                log.Append(written);
            }

            using var reloaded = new WorldDeltaLog();
            reloaded.Open(_dir, WorldKey);
            Assert.IsTrue(reloaded.TryGet(DeltaKind.EntitySpawned, big, out WorldDelta read));
            Assert.AreEqual(DeltaKind.EntitySpawned, read.Kind);
            Assert.AreEqual(big, read.Key);
            Assert.AreEqual(written.Position, read.Position);
            Assert.AreEqual(written.Rotation, read.Rotation);
            Assert.AreEqual(-42, read.TypeIndex);
            Assert.AreEqual(200, read.State);
            Assert.AreEqual(3.4375f, read.Scale, "scale survives, so a felled tree's stump is its own size");
            Assert.AreEqual(0, reloaded.TornRecordsDropped);
        }

        [Test]
        public void Scale_DefaultsToOne_NotZero()
        {
            // Every kind that has no size still writes a scale, and a consumer multiplies by it. Defaulting
            // to 0 would collapse every stump and log to nothing.
            Assert.AreEqual(1f, new WorldDelta(0, DeltaKind.ScatterRemoved, 1UL).Scale);

            using var log = new WorldDeltaLog();
            log.Open(_dir, WorldKey);
            Assert.AreEqual(1f, log.Append(new WorldDelta(0, DeltaKind.ScatterRemoved, 1UL)).Scale);
        }

        [Test]
        public void V1File_IsReadAndUpgraded_RatherThanRefused()
        {
            // Bryan's live world is already a v1 log. Refusing it would drop every chop in it, so the reader
            // has to understand the old fixed part - which ends at state, before scale was appended - and the
            // open has to leave the file in the current schema so the next append is not written behind a
            // header promising the old framing.
            WriteV1Log(BasePath,
                (1UL, new Vector3(1, 2, 3), 4, (byte)0),
                (2UL, new Vector3(-5, 6, 7), 9, (byte)1));

            using (var upgraded = new WorldDeltaLog())
            {
                upgraded.Open(_dir, WorldKey);
                Assert.AreEqual(0, upgraded.TornRecordsDropped, "a v1 record is not a torn record");
                Assert.AreEqual(2, upgraded.Count);
                Assert.IsTrue(upgraded.TryGet(DeltaKind.ScatterState, 1UL, out WorldDelta d));
                Assert.AreEqual(new Vector3(1, 2, 3), d.Position);
                Assert.AreEqual(4, d.TypeIndex);
                Assert.AreEqual(1f, d.Scale, "a record written before the field reads as unscaled");

                upgraded.Append(Chop(3UL, Vector3.one, 7));
            }

            // The append after the upgrade has to be readable, which it is not if the header still says v1.
            using var reloaded = new WorldDeltaLog();
            reloaded.Open(_dir, WorldKey);
            Assert.AreEqual(3, reloaded.Count);
            Assert.AreEqual(0, reloaded.TornRecordsDropped);
            Assert.IsTrue(reloaded.TryGet(DeltaKind.ScatterState, 3UL, out WorldDelta appended));
            Assert.AreEqual(7, appended.TypeIndex);
        }

        [Test]
        public void UnknownSchema_IsRefused_NotHalfRead()
        {
            // A file from a future build cannot be interpreted, and half-reading it is worse than ignoring it.
            byte[] header = new byte[8];
            BitConverter.GetBytes(0x504C4457u).CopyTo(header, 0);
            BitConverter.GetBytes(WorldDeltaLog.SchemaVersion + 1).CopyTo(header, 4);
            File.WriteAllBytes(BasePath, header);

            using var log = new WorldDeltaLog();
            log.Open(_dir, WorldKey);
            Assert.AreEqual(0, log.Count);
        }

        // Written byte by byte rather than through the current writer: the point is to read a file an OLD
        // build produced, so a change to the current framing must not quietly change the fixture.
        static void WriteV1Log(string path, params (ulong key, Vector3 pos, int proto, byte state)[] records)
        {
            const int v1Fixed = 48;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);

            var header = new byte[8];
            BitConverter.GetBytes(0x504C4457u).CopyTo(header, 0);
            BitConverter.GetBytes(1).CopyTo(header, 4);
            fs.Write(header, 0, header.Length);

            uint sequence = 1;
            foreach ((ulong key, Vector3 pos, int proto, byte state) in records)
            {
                var b = new byte[v1Fixed];
                BitConverter.GetBytes(sequence++).CopyTo(b, 0);
                b[4] = (byte)DeltaKind.ScatterState;
                BitConverter.GetBytes(key).CopyTo(b, 5);
                BitConverter.GetBytes((ushort)0).CopyTo(b, 13);
                BitConverter.GetBytes(pos.x).CopyTo(b, 15);
                BitConverter.GetBytes(pos.y).CopyTo(b, 19);
                BitConverter.GetBytes(pos.z).CopyTo(b, 23);
                BitConverter.GetBytes(0f).CopyTo(b, 27);
                BitConverter.GetBytes(0f).CopyTo(b, 31);
                BitConverter.GetBytes(0f).CopyTo(b, 35);
                BitConverter.GetBytes(1f).CopyTo(b, 39);
                BitConverter.GetBytes(proto).CopyTo(b, 43);
                b[47] = state;

                uint hash = 2166136261u;
                for (int i = 0; i < b.Length; i++) { hash ^= b[i]; hash *= 16777619u; }
                fs.Write(b, 0, b.Length);
                fs.Write(BitConverter.GetBytes(hash), 0, 4);
            }
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
            Assert.IsTrue(log.TryGet(DeltaKind.ScatterState, 500UL, out WorldDelta d));
            Assert.AreEqual(1, d.State);
            Assert.IsFalse(log.TryGet(DeltaKind.ScatterState, 9999UL, out _));
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
            Assert.IsTrue(reloaded.TryGet(DeltaKind.ScatterState, 1UL, out _), "records before the tear survive");
            Assert.IsTrue(reloaded.TryGet(DeltaKind.ScatterState, 2UL, out _));
            Assert.IsFalse(reloaded.TryGet(DeltaKind.ScatterState, 3UL, out _), "the torn record is dropped, not half-read");
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
            Assert.IsTrue(reloaded.TryGet(DeltaKind.ScatterState, 1UL, out _));
            Assert.IsFalse(reloaded.TryGet(DeltaKind.ScatterState, 2UL, out _));
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
                Assert.IsTrue(reloaded.TryGet(DeltaKind.ScatterState, i, out WorldDelta d), $"key {i} survived compaction");
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
            Assert.IsTrue(reloaded.TryGet(DeltaKind.ScatterState, 100UL, out WorldDelta d));
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

            Assert.IsTrue(reloaded.TryGet(DeltaKind.SurfaceStamp, 2UL, out WorldDelta withPayload));
            CollectionAssert.AreEqual(stamp, withPayload.Payload);

            Assert.IsTrue(reloaded.TryGet(DeltaKind.ScatterRemoved, 1UL, out WorldDelta without));
            Assert.IsNull(without.Payload, "a payload-free record stays payload-free");

            Assert.IsTrue(reloaded.TryGet(DeltaKind.ScatterState, 3UL, out WorldDelta after));
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
        public void SameNumber_InTwoIdSpaces_StaysTwoRecords()
        {
            // A ScatterId packs a cell address that can be small, and EntityId counters start at 1, so the
            // same number naming a tree and a dropped log is ordinary rather than exotic. Keyed on the bare
            // ulong, the log would let one overwrite the other and a chopped tree would come back.
            const ulong shared = 42UL;

            using var log = new WorldDeltaLog();
            log.Open(_dir, WorldKey);
            log.Append(new WorldDelta(0, DeltaKind.ScatterState, shared, Vector3.up, typeIndex: 1, state: 1));
            log.Append(new WorldDelta(0, DeltaKind.EntitySpawned, shared, Vector3.down, typeIndex: 2));
            log.Append(new WorldDelta(0, DeltaKind.SurfaceStamp, shared, payload: new byte[] { 9 }));

            Assert.AreEqual(3, log.Count, "three id spaces, so three live records");
            Assert.IsTrue(log.TryGet(DeltaKind.ScatterState, shared, out WorldDelta tree));
            Assert.AreEqual(Vector3.up, tree.Position);
            Assert.IsTrue(log.TryGet(DeltaKind.EntitySpawned, shared, out WorldDelta dropped));
            Assert.AreEqual(Vector3.down, dropped.Position);
            Assert.IsTrue(log.TryGet(DeltaKind.SurfaceStamp, shared, out WorldDelta stamp));
            Assert.AreEqual(1, stamp.PayloadLength);
        }

        [Test]
        public void KindsDescribingOneThing_ShareASpace_SoTheyCollapse()
        {
            using var log = new WorldDeltaLog();
            log.Open(_dir, WorldKey);

            // An entity's whole life is one live record: spawned, moved, then removed leaves a tombstone.
            log.Append(new WorldDelta(0, DeltaKind.EntitySpawned, 1UL, Vector3.zero, typeIndex: 4));
            log.Append(new WorldDelta(0, DeltaKind.EntityMoved, 1UL, Vector3.one));
            log.Append(new WorldDelta(0, DeltaKind.EntityRemoved, 1UL));
            Assert.AreEqual(1, log.Count);

            // The lookup takes any kind in the space, so asking with Spawned still finds the tombstone.
            Assert.IsTrue(log.TryGet(DeltaKind.EntitySpawned, 1UL, out WorldDelta d));
            Assert.AreEqual(DeltaKind.EntityRemoved, d.Kind);
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
