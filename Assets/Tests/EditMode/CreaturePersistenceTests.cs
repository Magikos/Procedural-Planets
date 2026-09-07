using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class CreaturePersistenceTests
    {
        static readonly Vector3 Observer = Vector3.up * 1000f;
        static EntityId Slot => CreatureKey.Slot(2, CreatureTerritory.Level, 8, 8, 0);

        [Test]
        public void NeedsPersistWithoutGrowingDuringAbsenceOrReload()
        {
            using var log = new WorldDeltaLog();
            EntityId id;
            using (var fixture = new Fixture(log))
            {
                fixture.Advance(15f);
                id = fixture.Service.Live[0].Id;
                fixture.Service.FlushState();
                Assert.AreEqual(15d / 1800d, Read(log, id).Needs.Hunger, 1e-9);
                fixture.Service.Tick(-Observer, 0f);
                fixture.Now += 86400;
                fixture.Service.Tick(Observer, 0f);
                fixture.Service.FlushState();
                Assert.AreEqual(15d / 900d, Read(log, id).Needs.Thirst, 1e-9);
            }
            using (var fixture = new Fixture(log))
            {
                fixture.Advance(15f);
                fixture.Service.FlushState();
                Assert.AreEqual(30d / 1800d, Read(log, id).Needs.Hunger, 1e-9);
                Assert.AreEqual(30d / 900d, Read(log, id).Needs.Thirst, 1e-9);
            }
        }

        [Test]
        public void NeedCheckpointAndCleanShutdownPreserveProgress()
        {
            using var log = new WorldDeltaLog();
            EntityId id;
            using (var fixture = new Fixture(log))
            {
                fixture.Advance(30f);
                id = fixture.Service.Live[0].Id;
                Assert.AreEqual(30d / 1800d, Read(log, id).Needs.Hunger, 1e-9);
                fixture.Advance(5f);
            }
            Assert.AreEqual(35d / 1800d, Read(log, id).Needs.Hunger, 1e-9);
        }

        [Test]
        public void HealthOnlyFormatStillLoadsWithSatisfiedNeeds()
        {
            var source = CreatureRecordCodec.Encode(CreatureRecord.Displacement(Slot, 0, 0, Observer, 1234,
                CreatureBehaviour.Wander, 3));
            var payload = new byte[22];
            Array.Copy(source.Payload, payload, payload.Length);
            payload[0] = 3;
            Assert.IsTrue(CreatureRecordCodec.TryDecode(new WorldDelta(0, DeltaKind.EntityMoved,
                Slot.Value, Observer, payload: payload), out var record));
            Assert.AreEqual(3, record.Health);
            Assert.AreEqual(default(ActorNeeds), record.Needs);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(-0.1d)]
        [TestCase(1.1d)]
        public void CorruptNeedLevelsAreRejected(double value)
        {
            var source = CreatureRecordCodec.Encode(CreatureRecord.Displacement(Slot, 0, 0, Observer, 1234,
                CreatureBehaviour.Wander, 3));
            BitConverter.GetBytes(value).CopyTo(source.Payload, 22);
            Assert.IsFalse(CreatureRecordCodec.TryDecode(source, out _));
        }

        [Test]
        public void WoundSurvivesDemotionAndReloadWithoutDuplicateWrites()
        {
            string directory = Path.Combine(Path.GetTempPath(), "creature-persistence-" + Guid.NewGuid().ToString("N"));
            try
            {
                EntityId id;
                using (var log = new WorldDeltaLog())
                {
                    log.Open(directory, "world");
                    using var fixture = new Fixture(log);
                    fixture.Service.Tick(Observer, 0f);
                    Assert.Greater(fixture.Service.LiveCount, 0);
                    id = fixture.Service.Live[0].Id;
                    Assert.AreEqual(3, fixture.Service.Strike(id, 2, Observer).RemainingHealth);
                    var wound = Read(log, id);
                    Assert.AreEqual(3, wound.Health);
                    Assert.AreEqual(fixture.Now, wound.UnixSeconds);
                    uint sequence = log.NextSequence;
                    fixture.Service.Tick(-Observer, 0f);
                    fixture.Service.Tick(Observer, 0f);
                    Assert.AreEqual(sequence, log.NextSequence, "unchanged wounds must not append on every demotion");
                    Assert.AreEqual(2, fixture.Service.Strike(id, 1, Observer).RemainingHealth);
                }
                using (var log = new WorldDeltaLog())
                {
                    log.Open(directory, "world");
                    using var fixture = new Fixture(log);
                    fixture.Service.Tick(Observer, 0f);
                    Assert.AreEqual(1, fixture.Service.Strike(id, 1, Observer).RemainingHealth);
                    Assert.IsTrue(fixture.Service.Strike(id, 1, Observer).Killed);
                    fixture.Now += 11;
                    fixture.Service.Invalidate();
                    fixture.Service.Tick(Observer, 0f);
                    EntityId next = CreatureKey.AtGeneration(CreatureKey.SlotOf(id), CreatureKey.GenerationOf(id) + 1);
                    Assert.AreEqual(4, fixture.Service.Strike(next, 1, Observer).RemainingHealth);
                    Assert.IsFalse(fixture.Service.Strike(id, 1, Observer).Hit, "old-generation requests must not damage the replacement");
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void FailedPersistenceDoesNotChangeLiveHealth()
        {
            using var log = new WorldDeltaLog();
            var failing = new FailingLog(log);
            using var fixture = new Fixture(failing);
            fixture.Service.Tick(Observer, 0f);
            EntityId id = fixture.Service.Live[0].Id;
            Assert.Throws<IOException>(() => fixture.Service.Strike(id, 2, Observer));
            failing.Fail = false;
            Assert.AreEqual(4, fixture.Service.Strike(id, 1, Observer).RemainingHealth);
        }

        [Test]
        public void HealthKeepsAnAtHomeRecordAndOnlyChangesCauseRewrites()
        {
            Assert.AreEqual(CreatureRecordAction.Write, CreatureRecordPolicy.Decide(0f, 100f,
                CreatureBehaviour.Wander, 0, false, 0f, CreatureBehaviour.Wander, true, true));
            Assert.AreEqual(CreatureRecordAction.Keep, CreatureRecordPolicy.Decide(0f, 100f,
                CreatureBehaviour.Wander, 0, true, 0f, CreatureBehaviour.Wander, true, false));
            Assert.AreEqual(CreatureRecordAction.Write, CreatureRecordPolicy.Decide(0f, 100f,
                CreatureBehaviour.Wander, 0, true, 0f, CreatureBehaviour.Wander, true, true));
            Assert.AreEqual(CreatureRecordAction.Forget, CreatureRecordPolicy.Decide(0f, 100f,
                CreatureBehaviour.Wander, 0, true, 0f, CreatureBehaviour.Wander, false, true));
        }

        [Test]
        public void LegacyDisplacementHasUnspecifiedHealth()
        {
            var payload = new byte[18];
            payload[0] = 2;
            BitConverter.GetBytes(7).CopyTo(payload, 1);
            BitConverter.GetBytes(1234L).CopyTo(payload, 5);
            payload[17] = (byte)CreatureBehaviour.Flee;
            var legacy = new WorldDelta(0, DeltaKind.EntityMoved, Slot.Value, Observer, payload: payload);
            Assert.IsTrue(CreatureRecordCodec.TryDecode(legacy, out var record));
            Assert.AreEqual(-1, record.Health);
            Assert.AreEqual(7, record.Generation);
            Assert.AreEqual(CreatureBehaviour.Flee, record.Behaviour);
        }

        [TestCase(0)]
        [TestCase(-2)]
        public void InvalidNewHealthIsRejected(int health)
        {
            var delta = CreatureRecordCodec.Encode(CreatureRecord.Displacement(Slot, 0, 0, Observer, 1234,
                CreatureBehaviour.Wander, 3));
            BitConverter.GetBytes(health).CopyTo(delta.Payload, 18);
            Assert.IsFalse(CreatureRecordCodec.TryDecode(delta, out _));
        }

        [Test]
        public void TruncatedOrInconsistentPayloadIsRejected()
        {
            var source = CreatureRecordCodec.Encode(CreatureRecord.Displacement(Slot, 0, 0, Observer, 1234,
                CreatureBehaviour.Wander, 3));
            var shortPayload = new byte[21];
            Array.Copy(source.Payload, shortPayload, shortPayload.Length);
            Assert.IsFalse(CreatureRecordCodec.TryDecode(new WorldDelta(0, DeltaKind.EntityMoved,
                Slot.Value, Observer, payload: shortPayload), out _));
            Assert.IsFalse(CreatureRecordCodec.TryDecode(new WorldDelta(0, DeltaKind.EntityRemoved,
                Slot.Value, Observer, payload: source.Payload), out _));
        }

        [Test]
        public void PlanningUsesSuppliedElapsedTimeForRespawn()
        {
            using var log = new WorldDeltaLog();
            using var fixture = new Fixture(log);
            fixture.Service.Tick(Observer, 0f);
            var original = fixture.Service.Live[0].Id;
            Assert.IsTrue(fixture.Service.Kill(original));
            fixture.Now += 11;
            fixture.Service.Tick(Observer, 1f);
            var replacement = CreatureKey.AtGeneration(CreatureKey.SlotOf(original), 1);
            Assert.AreEqual(4, fixture.Service.Strike(replacement, 1, Observer).RemainingHealth);
        }

        static CreatureRecord Read(IWorldDeltaLog log, EntityId id)
        {
            Assert.IsTrue(log.TryGet(DeltaKind.EntityMoved, CreatureKey.SlotOf(id).Value, out var delta));
            Assert.IsTrue(CreatureRecordCodec.TryDecode(delta, out var record));
            return record;
        }

        sealed class Fixture : IDisposable
        {
            public long Now = 1788770000;
            public readonly CreatureResidencyService Service;
            readonly GameObject _planet = new("Persistence test planet");
            public Fixture(IWorldDeltaLog log)
            {
                var surface = new Sphere();
                Service = new CreatureResidencyService(_planet.transform, surface, null, () => Now);
                var species = CreatureLibraryDto.Placeholder.At(0) with
                {
                    MaxHealth = 5, PerTerritory = 1, WalkSpeedMps = 0f, DriftHomeSpeedMps = 0f,
                    RespawnSeconds = 10f, MinAltitudeMeters = 0f, MaxAltitudeMeters = 200f,
                    Biomes = Array.Empty<BiomeType>(),
                };
                Set("_library", new CreatureLibraryDto(new[] { species }, 150f));
                Set("_slotBase", new[] { 0 });
                Set("_delta", log);
                Set("_seeds", new SeedProvider(20260826));
                Set("_planetRadius", 1000f);
                Set("_seaLevelRadius", 900f);
                Set("_gravity", new RadialGravityProvider(Vector3.zero));
                Set("_grounding", new PlanetSurfaceGrounding(surface, Vector3.zero));
                Set("_configured", true);
            }
            void Set(string name, object value) => typeof(CreatureResidencyService)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Service, value);
            public void Advance(float seconds)
            {
                int steps = Mathf.CeilToInt(seconds * 10f);
                for (int i = 0; i < steps; i++) Service.Tick(Observer, seconds / steps);
            }
            public void Dispose()
            {
                Service.Dispose();
                UnityEngine.Object.DestroyImmediate(_planet);
            }
        }

        sealed class Sphere : IPlanetSurfaceSampler
        {
            public bool TryGetSurfaceRadius(Vector3 direction, out float radius) { radius = 1000f; return true; }
        }

        sealed class FailingLog : IWorldDeltaLog
        {
            readonly IWorldDeltaLog _inner;
            public bool Fail = true;
            public FailingLog(IWorldDeltaLog inner) => _inner = inner;
            public WorldDelta Append(in WorldDelta delta) => Fail ? throw new IOException("test write failure") : _inner.Append(delta);
            public bool TryGet(DeltaKind kind, ulong key, out WorldDelta delta) => _inner.TryGet(kind, key, out delta);
            public IReadOnlyList<WorldDelta> Snapshot() => _inner.Snapshot();
        }
    }
}
