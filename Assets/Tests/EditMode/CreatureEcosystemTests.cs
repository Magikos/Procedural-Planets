using System;
using NUnit.Framework;

namespace ProceduralPlanets.Tests
{
    public sealed class CreatureEcosystemTests
    {
        [Test]
        public void AuthoredDeerFeedsThreeEmptyWolvesWithAReserve()
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureCombatSettings>(
                "Assets/Art/Creatures/Deer/DeerCombat.asset");
            Assert.IsNotNull(settings);
            var corpse = new CreatureCorpseResource(settings.Snapshot().MeatYield, CorpseDecay.GameDays);
            var wolves = new[] { new ActorNeeds(1, 0), new ActorNeeds(1, 0), new ActorNeeds(1, 0) };
            for (int tick = 0; tick < 200; tick++)
            {
                for (int i = 0; i < wolves.Length; i++)
                {
                    wolves[i] = wolves[i].Advance(.05, 80, 160);
                    corpse.Meat.Consume(ref wolves[i], ResourceKind.Meat, .12 * .05);
                }
                corpse.Advance(.05 * 86400 / 120);
            }
            foreach (var wolf in wolves) Assert.Less(wolf.Hunger, .01);
            Assert.Greater(corpse.Meat.Remaining, 1d);
        }
        [Test]
        public void CorpseReloadPreservesConsumptionAndAgeWithoutRefilling()
        {
            var corpse = new CreatureCorpseResource(1.5d, CorpseDecay.GameDays);
            var needs = new ActorNeeds(1d, 0d);
            corpse.Meat.Consume(ref needs, ResourceKind.Meat, .8d);
            corpse.Advance(86400d);
            var restored = new CreatureCorpseResource(corpse.InitialMeat, corpse.Decay, corpse.AgeSeconds, corpse.Meat.Remaining);
            Assert.AreEqual(.7d, restored.Meat.Remaining, 1e-9);
            Assert.IsTrue(restored.HasFlies);
            restored.Advance(20d * 86400d);
            Assert.AreEqual(0d, restored.Meat.Remaining);
            Assert.IsFalse(restored.HasFlies);
            Assert.AreEqual(CorpseStage.Bones, restored.Stage);
        }

        [Test]
        public void BloodStampRetainsGameTimeAndSurfaceNormalAndRejectsUnknownKinds()
        {
            var stamp = new SurfaceEditStamp { kind = SurfaceEditStampCodec.BloodKind,
                direction = new UnityEngine.Vector3(2, 3, 4), surfaceNormal = UnityEngine.Vector3.up,
                createdGameSeconds = 123.5d, radiusMeters = .2f, strength = 1, regrowSeconds = 100 };
            var encoded = SurfaceEditStampCodec.Encode(42, stamp);
            Assert.IsTrue(SurfaceEditStampCodec.TryDecode(encoded, out var restored));
            Assert.AreEqual(stamp.kind, restored.kind);
            Assert.AreEqual(stamp.direction, restored.direction);
            Assert.AreEqual(stamp.surfaceNormal, restored.surfaceNormal);
            Assert.AreEqual(stamp.createdGameSeconds, restored.createdGameSeconds);
            encoded.Payload[1] = 200;
            Assert.IsFalse(SurfaceEditStampCodec.TryDecode(encoded, out _));
            stamp.kind = "unknown";
            Assert.Throws<ArgumentException>(() => SurfaceEditStampCodec.Encode(43, stamp));
        }

        [Test]
        public void StarvationHasGraceAndDamageIsStableAcrossTickSizes()
        {
            var needs = new ActorNeeds(1d, 0d);
            var single = new ActorDeprivation();
            var split = new ActorDeprivation();
            Assert.AreEqual(0, single.Advance(needs, 10d, 10, 10d, 20d, 2d, 5d));
            int damage = single.Advance(needs, 10d, 10, 10d, 20d, 2d, 5d);
            int splitDamage = 0;
            for (int i = 0; i < 200; i++) splitDamage += split.Advance(needs, .1d, 10, 10d, 20d, 2d, 5d);
            Assert.AreEqual(5, damage);
            Assert.AreEqual(damage, splitDamage);
        }

        [Test]
        public void DrinkingStopsDehydrationWithoutRestoringHealth()
        {
            var exposure = new ActorDeprivation();
            Assert.AreEqual(1, exposure.Advance(new ActorNeeds(0d, 1d), 3d, 10, 10d, 20d, 2d, 10d));
            var restored = new ActorDeprivation(exposure.StarvingSeconds, exposure.DehydratedSeconds, exposure.DamageRemainder);
            Assert.AreEqual(0, restored.Advance(new ActorNeeds(0d, .1d), 20d, 10, 10d, 20d, 2d, 10d));
            Assert.AreEqual(0d, restored.DehydratedSeconds);
            Assert.Throws<ArgumentOutOfRangeException>(() => restored.Advance(default, double.NaN, 10, 0d, 20d, 0d, 10d));
        }

        [Test]
        public void EitherUnsatisfiedNeedCanEventuallyKill()
        {
            foreach (var needs in new[] { new ActorNeeds(1d, 0d), new ActorNeeds(0d, 1d) })
                Assert.AreEqual(10, new ActorDeprivation().Advance(needs, 100d, 10, 10d, 20d, 2d, 10d));
        }

        [Test]
        public void MultipleConsumersShareOneStockAndDecayCannotRefillIt()
        {
            var source = new ActorResourceSource(ResourceKind.Meat, 1d, 1d, 0d);
            var first = new ActorNeeds(1d, 0d); var second = first;
            Assert.AreEqual(.7d, source.Consume(ref first, ResourceKind.Meat, .7d));
            Assert.AreEqual(.3d, source.Consume(ref second, ResourceKind.Meat, .7d), 1e-9);
            source.LimitRemaining(1d);
            Assert.AreEqual(0d, source.Remaining);
        }

        [Test]
        public void GameDayDecayKeepsBonesForMonthsAndFliesNeedMeat()
        {
            var decay = CorpseDecay.GameDays;
            const double day = 86400d;
            Assert.AreEqual(CorpseStage.Fresh, decay.StageAtAge(day));
            Assert.AreEqual(CorpseStage.Bloated, decay.StageAtAge(2d * day));
            Assert.AreEqual(CorpseStage.Rotting, decay.StageAtAge(5d * day));
            Assert.AreEqual(CorpseStage.Bones, decay.StageAtAge(60d * day));
            Assert.AreEqual(CorpseStage.Gone, decay.StageAtAge(90d * day));
            Assert.IsFalse(decay.HasFliesAtAge(.1d * day, 1d));
            Assert.IsTrue(decay.HasFliesAtAge(.25d * day, .5d));
            Assert.IsFalse(decay.HasFliesAtAge(.25d * day, 0d));
            Assert.AreEqual(.5d, decay.NaturalMeatFraction(13d * day), 1e-9);
            Assert.AreEqual(0d, decay.NaturalMeatFraction(21d * day));
        }
    }
}
