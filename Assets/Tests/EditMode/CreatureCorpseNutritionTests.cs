using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class CreatureCorpseNutritionTests
    {
        [Test]
        public void ThreeWolvesShareOneCarcassAndHideLootDoesNotRemoveFood()
        {
            var store = new CreatureCorpseStore();
            EntityId id = store.Record(0, Vector3.zero, Quaternion.identity, 100);
            Assert.That(store.MarkLooted(id), Is.True);
            for (int i = 0; i < 3; i++) Assert.That(store.ConsumeMeat(id, 100, 4, 1), Is.EqualTo(1));
            Assert.That(store.RemainingMeat(id, 100, 4), Is.EqualTo(1));
            Assert.That(store.ConsumeMeat(id, 100, 4, 20), Is.EqualTo(1));
            Assert.That(store.ConsumeMeat(id, 100, 4, 1), Is.Zero);
            Assert.That(store.TryGet(id, out CreatureCorpse corpse), Is.True);
            Assert.That(corpse.Looted, Is.True);
            Assert.That(store.StageOf(corpse, 100), Is.EqualTo(CorpseStage.Fresh));
        }

        [Test]
        public void DecayAndConsumptionSurviveDiskReloadWithoutRefilling()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CreatureNutrition-" + Guid.NewGuid().ToString("N"));
            EntityId id;
            long halfway = (long)((CorpseDecay.Default.RottingAfterSeconds + CorpseDecay.Default.BonesAfterSeconds) / 2);
            try
            {
                using (var log = new WorldDeltaLog())
                {
                    log.Open(directory, "world");
                    var store = new CreatureCorpseStore();
                    store.Configure(log);
                    id = store.Record(0, Vector3.zero, Quaternion.identity, 0);
                    Assert.That(store.RemainingMeat(id, halfway, 4), Is.EqualTo(2).Within(.00001));
                    Assert.That(store.ConsumeMeat(id, halfway, 4, 1), Is.EqualTo(1));
                    store.MarkLooted(id);
                    log.Flush();
                }
                using (var log = new WorldDeltaLog())
                {
                    log.Open(directory, "world");
                    var restored = new CreatureCorpseStore();
                    restored.Configure(log);
                    Assert.That(restored.RemainingMeat(id, halfway, 4), Is.EqualTo(1).Within(.00001));
                    Assert.That(restored.RemainingMeat(id, 0, 4), Is.EqualTo(1).Within(.00001), "A clock correction cannot restore persisted loss.");
                    Assert.That(restored.MarkLooted(id), Is.False);
                    Assert.That(restored.RemainingMeat(id, (long)CorpseDecay.Default.BonesAfterSeconds, 4), Is.Zero);
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void LegacyPayloadRetainsLootAndStartsWithDecayLimitedMeat()
        {
            using var log = new WorldDeltaLog();
            var payload = new byte[9];
            payload[0] = 10;
            BitConverter.GetBytes(100L).CopyTo(payload, 1);
            var allocator = new EntityIdAllocator(EntityId.CorpseOwner);
            EntityId id = allocator.Next();
            log.Append(new WorldDelta(0, DeltaKind.EntitySpawned, id.Value, Vector3.zero,
                Quaternion.identity, typeIndex: 0, state: 1, payload: payload));
            var store = new CreatureCorpseStore();
            store.Configure(log);
            Assert.That(store.RemainingMeat(id, 100, 4), Is.EqualTo(4));
            Assert.That(store.MarkLooted(id), Is.False);
            Assert.That(store.ConsumeMeat(id, 100, 4, 1), Is.EqualTo(1));
            Assert.That(log.TryGet(DeltaKind.EntitySpawned, id.Value, out WorldDelta saved), Is.True);
            Assert.That(saved.Payload[0], Is.EqualTo(12));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.ConsumeMeat(id, 100, 4, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.RemainingMeat(id, 100, -1));
        }
    }
}
