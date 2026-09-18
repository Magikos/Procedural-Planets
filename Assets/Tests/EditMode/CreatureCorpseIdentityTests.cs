using System;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class CreatureCorpseIdentityTests
    {
        [Test]
        public void FemaleAppearanceSurvivesDeathFeedingLootAndReload()
        {
            ulong female = 1;
            while (CreatureAnimationView.IsMale(female)) female++;
            using var log = new WorldDeltaLog();
            var store = new CreatureCorpseStore(); store.Configure(log);
            EntityId id = store.Record(0, Vector3.up, Quaternion.identity, 100, female);
            store.ConsumeMeat(id, 100, 4d, 1d);
            store.MarkLooted(id);
            var restored = new CreatureCorpseStore(); restored.Configure(log);
            Assert.That(restored.TryGet(id, out var corpse), Is.True);
            Assert.That(corpse.SourceActorId, Is.EqualTo(female));
            Assert.That(corpse.AppearanceIdentity, Is.EqualTo(female));
            Assert.That(CreatureAnimationView.IsMale(corpse.AppearanceIdentity), Is.False);
            Assert.That(corpse.MeatRemainingFraction, Is.EqualTo(.75d));
            Assert.That(corpse.Looted, Is.True);
        }

        [TestCase(10)]
        [TestCase(11)]
        public void LegacyCorpsePreservesItsExistingAppearanceIdentity(int format)
        {
            using var log = new WorldDeltaLog();
            var payload = new byte[format == 10 ? 9 : 17];
            payload[0] = (byte)format;
            BitConverter.GetBytes(100L).CopyTo(payload, 1);
            if (format == 11) BitConverter.GetBytes(.75d).CopyTo(payload, 9);
            var allocator = new EntityIdAllocator(EntityId.CorpseOwner);
            EntityId id = allocator.Next();
            log.Append(new WorldDelta(0, DeltaKind.EntitySpawned, id.Value, Vector3.zero, Quaternion.identity,
                typeIndex: 0, payload: payload));
            var store = new CreatureCorpseStore(); store.Configure(log);
            Assert.That(store.TryGet(id, out var corpse), Is.True);
            Assert.That(corpse.SourceActorId, Is.Zero);
            Assert.That(corpse.AppearanceIdentity, Is.EqualTo(id.Value));
            store.MarkLooted(id);
            var restored = new CreatureCorpseStore(); restored.Configure(log);
            Assert.That(restored.TryGet(id, out corpse), Is.True);
            Assert.That(corpse.AppearanceIdentity, Is.EqualTo(id.Value));
        }
    }
}
