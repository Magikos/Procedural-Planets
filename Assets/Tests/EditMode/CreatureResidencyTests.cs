using System;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // The residency spine's pure logic. What is worth pinning here is exactly what a play-test cannot see:
    // whether a slot's identity survives packing, whether a death's expiry arithmetic releases the slot at the
    // right moment and with a NEW generation, and whether the derived key space can collide with the minted
    // one. Wandering, drawing and feel are verified in play, not here.
    public sealed class CreatureResidencyTests
    {
        const int TestSeed = 20260826;

        static EntityId Slot(int face = 2, int x = 5, int y = 9, int slot = 1) =>
            CreatureKey.Slot(face, CreatureTerritory.Level, x, y, slot);

        // --- identity ------------------------------------------------------

        [Test]
        public void Address_SurvivesThePacking()
        {
            EntityId id = CreatureKey.Individual(face: 5, level: 31, x: 4095, y: 4094, slot: 63, generation: 511);
            CreatureKey.Unpack(id, out int face, out int level, out int x, out int y, out int slot, out int gen);

            Assert.AreEqual(5, face);
            Assert.AreEqual(31, level);
            Assert.AreEqual(4095, x);
            Assert.AreEqual(4094, y);
            Assert.AreEqual(63, slot);
            Assert.AreEqual(511, gen);
        }

        [Test]
        public void OtherDerivedIds_AreNotMistakenForCreatures()
        {
            // The derived owner tag is a NAMESPACE, not a creature marker: well-known ids such as the local
            // player's threat-registry id live in it too. Testing the tag alone reads those as creatures and
            // unpacks them into a territory that does not exist.
            var wellKnown = new EntityId(EntityId.DerivedOwner, 1);
            Assert.IsFalse(CreatureKey.IsCreature(wellKnown));
            Assert.IsTrue(CreatureKey.IsCreature(CreatureKey.Individual(0, 4, 0, 0, 0, 0)));
        }

        [Test]
        public void TheAllZeroTerritory_IsAValidIdRatherThanNone()
        {
            // face 0, level 0, cell 0,0, slot 0, generation 0 is a real address. Without the marker bit it
            // packs to zero, which EntityId reserves for "no entity".
            EntityId id = CreatureKey.Individual(0, 0, 0, 0, 0, 0);
            Assert.IsFalse(id.IsNone);
            Assert.IsTrue(CreatureKey.IsCreature(id));
        }

        [Test]
        public void OutOfRangeAddresses_Throw_RatherThanMaskIntoAnotherTerritory()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatureKey.Individual(6, 4, 0, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatureKey.Individual(0, 32, 0, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatureKey.Individual(0, 4, 4096, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatureKey.Individual(0, 4, 0, 0, 64, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatureKey.Individual(0, 4, 0, 0, 0, 512));
        }

        [Test]
        public void DerivedKeys_CanNeverBeAMintedEntityId()
        {
            // Both live in the delta log's entity space, and nothing but the owner tag separates them. If a
            // host-minted id could land on a creature key, a dropped log would overwrite a creature's death.
            var host = new EntityIdAllocator(EntityId.HostOwner);
            EntityId creature = CreatureKey.Individual(1, CreatureTerritory.Level, 3, 4, 2, 7);

            for (int i = 0; i < 1000; i++)
                Assert.AreNotEqual(creature.Value, host.Next().Value);

            Assert.AreEqual(EntityId.DerivedOwner, creature.Owner);
            Assert.Throws<ArgumentOutOfRangeException>(() => new EntityIdAllocator(EntityId.DerivedOwner));
        }

        [Test]
        public void ObservingACreatureKey_DoesNotMoveTheHostCounter()
        {
            var host = new EntityIdAllocator(EntityId.HostOwner);
            host.Observe(CreatureKey.Individual(3, CreatureTerritory.Level, 11, 12, 5, 3));
            Assert.AreEqual(1UL, host.NextCounter, "a derived id belongs to another owner and must be ignored");
        }

        [Test]
        public void SlotAndIndividual_AreTheSameAddressWithAndWithoutTheGeneration()
        {
            EntityId slot = Slot();
            EntityId third = CreatureKey.AtGeneration(slot, 3);

            Assert.AreEqual(0, CreatureKey.GenerationOf(slot));
            Assert.AreEqual(3, CreatureKey.GenerationOf(third));
            Assert.AreEqual(slot.Value, CreatureKey.SlotOf(third).Value);
            Assert.AreNotEqual(slot.Value, third.Value, "a generation must change the individual's id");
        }

        // --- death, expiry and repopulation ---------------------------------

        static CreatureRecord Death(long diedAt, float respawnSeconds, int generation = 0) =>
            CreatureRecord.Death(Slot(), speciesIndex: 0, generation, new Vector3(1f, 2f, 3f),
                diedAt, respawnSeconds);

        [Test]
        public void ADeath_SuppressesItsSlotUntilTheExpiryLapses()
        {
            CreatureRecord death = Death(diedAt: 1000, respawnSeconds: 60f);

            Assert.IsTrue(death.Suppresses(1000), "the instant of death");
            Assert.IsTrue(death.Suppresses(1059), "one second short of the expiry");
            Assert.IsFalse(death.Suppresses(1060), "the expiry is the moment the slot is released");
            Assert.IsFalse(death.Suppresses(9999));

            Assert.AreEqual(60f, death.SecondsUntilRespawn(1000), 1e-4f);
            Assert.AreEqual(0f, death.SecondsUntilRespawn(2000), 1e-4f);
        }

        [Test]
        public void TheCountdown_KeepsWholeSecondsAtRealUnixTimestamps()
        {
            // A real timestamp needs 31 bits and a float carries 24. Doing the subtraction in float rounds the
            // epoch to the nearest ~128 s, and the countdown then sits still while the clock moves - which is
            // exactly what a test written against a toy timestamp like 1000 fails to catch.
            const long now = 1787756079;
            CreatureRecord death = Death(diedAt: now - 247, respawnSeconds: 300f);

            Assert.AreEqual(53f, death.SecondsUntilRespawn(now), 0.5f);
            Assert.AreEqual(52f, death.SecondsUntilRespawn(now + 1), 0.5f);
            Assert.IsTrue(death.Suppresses(now + 52));
            Assert.IsFalse(death.Suppresses(now + 53));
        }

        [Test]
        public void ZeroOrNegativeExpiry_NeverLapses_WhichIsTheBossCase()
        {
            foreach (float never in new[] { 0f, -1f, float.NaN })
            {
                CreatureRecord death = Death(diedAt: 1000, respawnSeconds: never);
                Assert.IsTrue(death.NeverLapses, $"respawn {never} must be permanent");
                Assert.IsTrue(death.Suppresses(long.MaxValue / 2), $"respawn {never} must still suppress");
                Assert.AreEqual(float.PositiveInfinity, death.SecondsUntilRespawn(1_000_000));
            }
        }

        [Test]
        public void AnExpiredDeath_RepopulatesTheSlotWithANewGeneration()
        {
            CreatureRecord death = Death(diedAt: 1000, respawnSeconds: 60f, generation: 4);

            Assert.IsFalse(CreatureTerritory.TryResolveOccupant(true, death, 1030, out _),
                "the slot stays empty while the death suppresses it");

            Assert.IsTrue(CreatureTerritory.TryResolveOccupant(true, death, 1060, out int generation));
            Assert.AreEqual(5, generation,
                "returning to find the same generation alive is worse than finding a fresh animal");
        }

        [Test]
        public void ASlotWithNoDeath_HoldsItsOriginalOccupant()
        {
            Assert.IsTrue(CreatureTerritory.TryResolveOccupant(false, default, 0, out int generation));
            Assert.AreEqual(0, generation);
        }

        [Test]
        public void ANeverRespawningDeath_KeepsTheSlotEmptyForever()
        {
            CreatureRecord death = Death(diedAt: 1000, respawnSeconds: 0f, generation: 2);
            Assert.IsFalse(CreatureTerritory.TryResolveOccupant(true, death, 1000, out _));
            Assert.IsFalse(CreatureTerritory.TryResolveOccupant(true, death, long.MaxValue / 2, out _));
        }

        [Test]
        public void TheGeneration_WrapsRatherThanOverflowingTheKey()
        {
            CreatureRecord last = Death(diedAt: 0, respawnSeconds: 1f, generation: CreatureKey.GenerationWrap - 1);
            Assert.AreEqual(0, last.NextGeneration);
            Assert.DoesNotThrow(() => CreatureKey.AtGeneration(Slot(), last.NextGeneration));
        }

        // --- persistence ----------------------------------------------------

        [Test]
        public void ADeathRecord_SurvivesTheRoundTripThroughTheDeltaLog()
        {
            CreatureRecord source = Death(diedAt: 1735689600, respawnSeconds: 604800f, generation: 9);
            WorldDelta delta = CreatureRecordCodec.Encode(source);

            Assert.AreEqual(DeltaKind.EntityRemoved, delta.Kind,
                "creature deaths reuse the entity kinds so the log collapses them to one live record per slot");

            Assert.IsTrue(CreatureRecordCodec.TryDecode(delta, out CreatureRecord read));
            Assert.AreEqual(source.Slot.Value, read.Slot.Value);
            Assert.AreEqual(source.SpeciesIndex, read.SpeciesIndex);
            Assert.AreEqual(source.Generation, read.Generation);
            Assert.AreEqual(source.UnixSeconds, read.UnixSeconds);
            Assert.AreEqual(source.RespawnSeconds, read.RespawnSeconds, 1e-3f);
            Assert.AreEqual(source.Position, read.Position);
            Assert.IsTrue(read.IsDead);
        }

        [Test]
        public void ADisplacementRecord_SurvivesTheRoundTripAndIsNotADeath()
        {
            CreatureRecord source = CreatureRecord.Displacement(Slot(), speciesIndex: 1, generation: 3,
                new Vector3(10f, -20f, 30f), 1787760000, CreatureBehaviour.Flee);
            WorldDelta delta = CreatureRecordCodec.Encode(source);

            Assert.AreEqual(DeltaKind.EntityMoved, delta.Kind);
            Assert.IsTrue(CreatureRecordCodec.TryDecode(delta, out CreatureRecord read));

            Assert.IsFalse(read.IsDead);
            Assert.AreEqual(3, read.Generation);
            Assert.AreEqual(CreatureBehaviour.Flee, read.Behaviour,
                "what it was doing has to survive, or a fleeing animal comes back calm");
            Assert.AreEqual(source.Position, read.Position);
            Assert.AreEqual(source.UnixSeconds, read.UnixSeconds);
            Assert.IsFalse(read.Suppresses(long.MaxValue / 2), "a displaced creature is alive");
        }

        [Test]
        public void ADisplacementCarriesTheGeneration_SoItCanReplaceALapsedDeath()
        {
            // Both share a key AND a delta space, so the later write replaces the earlier. If a displacement
            // did not carry the generation, overwriting a lapsed death would silently reset the slot to
            // generation 0 - the one thing section 5 says must never happen.
            CreatureRecord displaced = CreatureRecord.Displacement(Slot(), 0, generation: 7,
                Vector3.one, 1787760000, CreatureBehaviour.Wander);

            Assert.IsTrue(CreatureTerritory.TryResolveOccupant(true, displaced, 1787760000, out int generation));
            Assert.AreEqual(7, generation, "a displacement describes the CURRENT occupant, so its generation is live");
        }

        [Test]
        public void ARecordIsFiledUnderItsSlot_EvenWhenHandedTheIndividual()
        {
            EntityId slot = Slot();
            CreatureRecord record = CreatureRecord.Death(
                CreatureKey.AtGeneration(slot, 6), 0, 6, Vector3.zero, 0, 1f);

            Assert.AreEqual(slot.Value, record.Slot.Value,
                "a key carrying generation bits would file the record where no lookup goes looking");
        }

        // --- when a creature is worth a byte -----------------------------------

        const float HomeRange = 120f;

        static CreatureRecordAction Decide(float fromHome, CreatureBehaviour behaviour = CreatureBehaviour.Wander,
            int generation = 0, bool written = false, float movedSinceWritten = 0f,
            CreatureBehaviour writtenBehaviour = CreatureBehaviour.Wander) =>
            CreatureRecordPolicy.Decide(fromHome, HomeRange, behaviour, generation, written,
                movedSinceWritten, writtenBehaviour);

        [Test]
        public void ACalmAnimalNearItsHome_CostsNothing()
        {
            // Section 5's promise. It is where the seed would have put it, so there is nothing to write down.
            Assert.AreEqual(CreatureRecordAction.Keep, Decide(fromHome: 10f));
            Assert.AreEqual(CreatureRecordAction.Keep, Decide(fromHome: HomeRange * 0.5f));
        }

        [Test]
        public void AnAnimalLeftFarFromHome_IsWrittenDown()
        {
            Assert.AreEqual(CreatureRecordAction.Write, Decide(fromHome: HomeRange));
        }

        [Test]
        public void AnAgitatedAnimal_IsWrittenDownEvenStandingOnItsHome()
        {
            // What it was DOING is the exceptional part. A deer that was fleeing when you walked away has to
            // still be fleeing when you come back, wherever it happened to be standing.
            Assert.AreEqual(CreatureRecordAction.Write,
                Decide(fromHome: 0f, behaviour: CreatureBehaviour.Flee));
        }

        [Test]
        public void AnAnimalThatDriftedHome_HasItsRecordDropped()
        {
            // Section 6: the log shrinks back toward the seed. Leaving the record behind is how a save file
            // grows forever instead.
            Assert.AreEqual(CreatureRecordAction.Forget, Decide(fromHome: 5f, written: true));
        }

        [Test]
        public void ASlotWhoseGenerationMovedOn_KeepsARecordEvenWithNothingElseToSay()
        {
            // The generation counter lives nowhere else. Forgetting the record would put the slot back to
            // generation 0 and resurrect an animal the player already killed.
            Assert.AreEqual(CreatureRecordAction.Write,
                Decide(fromHome: 0f, generation: 3));
            Assert.AreNotEqual(CreatureRecordAction.Forget,
                Decide(fromHome: 0f, generation: 3, written: true));
        }

        [Test]
        public void RewritingTheSameThing_IsSkipped()
        {
            // A creature crossing the bubble boundary demotes repeatedly. Without this the log gets an append
            // every time it steps out.
            Assert.AreEqual(CreatureRecordAction.Keep,
                Decide(fromHome: HomeRange, written: true, movedSinceWritten: 2f));
            Assert.AreEqual(CreatureRecordAction.Write,
                Decide(fromHome: HomeRange, written: true, movedSinceWritten: 50f));
        }

        [Test]
        public void AChangeOfBehaviour_IsAlwaysWorthARewrite()
        {
            // It has not moved, but it started running. That is the whole point of saving the behaviour.
            Assert.AreEqual(CreatureRecordAction.Write,
                Decide(fromHome: HomeRange, behaviour: CreatureBehaviour.Flee, written: true,
                    movedSinceWritten: 0f, writtenBehaviour: CreatureBehaviour.Wander));
        }

        [Test]
        public void AFormatOneDeathRecord_StillReadsAsADeath()
        {
            // Format 1 predates displacement records and is already in Bryan's save. It carried no state byte
            // and no behaviour, so everything it describes is a death - reading it as a displacement would
            // resurrect creatures that were killed.
            var payload = new byte[17];
            payload[0] = 1;                                                   // format
            BitConverter.GetBytes(4).CopyTo(payload, 1);                      // generation
            BitConverter.GetBytes(1735689600L).CopyTo(payload, 5);            // died
            BitConverter.GetBytes(600f).CopyTo(payload, 13);                  // respawn

            var legacy = new WorldDelta(0, DeltaKind.EntityRemoved, Slot().Value,
                new Vector3(1f, 2f, 3f), typeIndex: 0, payload: payload);

            Assert.IsTrue(CreatureRecordCodec.TryDecode(legacy, out CreatureRecord read));
            Assert.IsTrue(read.IsDead);
            Assert.AreEqual(4, read.Generation);
            Assert.AreEqual(1735689600L, read.UnixSeconds);
            Assert.AreEqual(600f, read.RespawnSeconds, 1e-3f);
            Assert.IsTrue(read.Suppresses(1735689600L + 599));
            Assert.IsFalse(read.Suppresses(1735689600L + 600));
        }

        [Test]
        public void AForgetRecord_PutsTheSlotBackToWhatTheSeedSays()
        {
            // The payload-free record is how a creature that drifted home stops costing storage. It must fail
            // to decode, because the ABSENCE of a readable record is what means "exactly what the seed says".
            Assert.IsFalse(CreatureRecordCodec.TryDecode(CreatureRecordCodec.Forget(Slot()), out _));
        }

        [Test]
        public void ARemovedDroppedObject_IsNotReadAsACreatureDeath()
        {
            // A dropped log's removal is the same DeltaKind. Only the owner tag tells the two apart, and
            // decoding one as the other would suppress a territory slot that nothing ever killed.
            var log = new WorldDelta(0, DeltaKind.EntityRemoved, new EntityId(EntityId.HostOwner, 42).Value);
            Assert.IsFalse(CreatureRecordCodec.TryDecode(log, out _));
        }

        [Test]
        public void ATombstonedDeath_ReleasesItsSlot()
        {
            // clear-deaths writes a payload-free record over the death. It must fail to decode, because the
            // absence of a readable death is what puts the slot back to its seed default.
            var tombstone = new WorldDelta(0, DeltaKind.EntityRemoved, Slot().Value);
            Assert.IsFalse(CreatureRecordCodec.TryDecode(tombstone, out _));
        }

        // --- birth ----------------------------------------------------------

        [Test]
        public void TheSameSeed_PutsTheSameCreaturesInTheSamePlaces()
        {
            var a = new SeedProvider(TestSeed);
            var b = new SeedProvider(TestSeed);
            var other = new SeedProvider(TestSeed + 1);

            for (int slot = 0; slot < 3; slot++)
            {
                EntityId key = Slot(face: 1, x: 7, y: 3, slot: slot);
                Vector3 first = CreatureTerritory.HomeCandidate(a, key);
                Assert.AreEqual(first, CreatureTerritory.HomeCandidate(b, key),
                    "two runs of one seed must agree without talking");
                Assert.AreNotEqual(first, CreatureTerritory.HomeCandidate(other, key),
                    "a different world must not reuse the same homes");
            }
        }

        [Test]
        public void EverySlotsHome_LandsInsideItsOwnTerritory()
        {
            var seeds = new SeedProvider(TestSeed);
            for (int face = 0; face < 6; face++)
            for (int cell = 0; cell < CreatureTerritory.CellsPerFace; cell += 5)
            for (int slot = 0; slot < 3; slot++)
            {
                EntityId key = CreatureKey.Slot(face, CreatureTerritory.Level, cell, cell, slot);
                Vector3 dir = CreatureTerritory.HomeCandidate(seeds, key);

                FaceSpaceCellRangeBuilder.DirectionToFaceUv(dir, out int gotFace, out Vector2 uv);
                int gotX = Mathf.FloorToInt(uv.x / CreatureTerritory.CellUvWidth);
                int gotY = Mathf.FloorToInt(uv.y / CreatureTerritory.CellUvWidth);

                Assert.AreEqual(face, gotFace, $"home for {face}:{cell},{cell}#{slot} left its face");
                Assert.AreEqual(cell, gotX, "home left its territory in u");
                Assert.AreEqual(cell, gotY, "home left its territory in v");
            }
        }

        // --- suitability: altitude and biome ---------------------------------

        static CreatureSpeciesDto Species(float minAlt, float maxAlt, params BiomeType[] biomes) =>
            new("Test", 3, 120f, 2.5f, 0.8f, 300f, minAlt, maxAlt, 1.7f, Color.white, biomes,
                CreatureFaction.Wildlife, 35f, 3, "Hide", 1);

        [Test]
        public void AnEmptyBiomeList_MeansAnyBiome()
        {
            // The default must be permissive: a species is limited by its altitude band alone until someone
            // says otherwise, or adding a species stops being a one-line change.
            CreatureSpeciesDto anywhere = Species(2f, 3000f);
            foreach (BiomeType b in Enum.GetValues(typeof(BiomeType)))
                Assert.IsTrue(anywhere.LivesIn(b), $"{b} should be allowed when no list is authored");
        }

        [Test]
        public void ASpecies_SettlesOnlyInItsOwnBiomes()
        {
            CreatureSpeciesDto deer = Species(2f, 3000f, BiomeType.Forest, BiomeType.Taiga);

            Assert.IsTrue(deer.LivesIn(BiomeType.Forest));
            Assert.IsTrue(deer.LivesIn(BiomeType.Taiga));
            Assert.IsFalse(deer.LivesIn(BiomeType.Desert));
            Assert.IsFalse(deer.LivesIn(BiomeType.Ocean));
        }

        [Test]
        public void BothGatesMustPass_NotEitherOne()
        {
            CreatureSpeciesDto deer = Species(2f, 3000f, BiomeType.Forest);

            Assert.IsTrue(deer.Suits(100f, BiomeType.Forest));
            Assert.IsFalse(deer.Suits(100f, BiomeType.Desert), "right altitude, wrong biome");
            Assert.IsFalse(deer.Suits(-5f, BiomeType.Forest), "right biome, underwater");
            Assert.IsFalse(deer.Suits(9000f, BiomeType.Forest), "right biome, above the band");
        }

        [Test]
        public void ANegativeBand_IsHowAnAquaticSpeciesIsExpressed()
        {
            // Altitude is signed, so "underwater" needs no new axis - the same gate that keeps a deer out of
            // the sea is what keeps a fish in it.
            CreatureSpeciesDto fish = Species(-30f, -2f, BiomeType.Ocean, BiomeType.Lake);

            Assert.IsTrue(fish.Suits(-12f, BiomeType.Ocean));
            Assert.IsFalse(fish.Suits(5f, BiomeType.Ocean), "above the waterline");
            Assert.IsFalse(fish.Suits(-12f, BiomeType.Forest));
        }

        [Test]
        public void TheDtoDoesNotAliasTheAssetsBiomeArray()
        {
            // A DTO is a snapshot. Sharing the authored array would let an inspector edit reach code that has
            // already read the settings, which is the whole failure the SO/DTO split exists to prevent.
            var authored = new CreatureSpecies { Biomes = new[] { BiomeType.Forest } };
            CreatureSpeciesDto dto = CreatureSpeciesDto.From(authored);

            authored.Biomes[0] = BiomeType.Desert;

            Assert.IsTrue(dto.LivesIn(BiomeType.Forest), "the snapshot must not see the later edit");
            Assert.IsFalse(dto.LivesIn(BiomeType.Desert));
        }

        [Test]
        public void RetryingAHome_DrawsSomewhereElseInTheSameTerritory()
        {
            var seeds = new SeedProvider(TestSeed);
            EntityId key = Slot(face: 3, x: 6, y: 10, slot: 2);

            var seen = new System.Collections.Generic.HashSet<Vector3>();
            for (int attempt = 0; attempt < CreatureTerritory.HomeAttempts; attempt++)
            {
                Vector3 dir = CreatureTerritory.HomeCandidate(seeds, key, attempt);
                Assert.IsTrue(seen.Add(dir), $"attempt {attempt} repeated an earlier candidate");

                // Every retry must stay inside the SAME territory, or a rejected slot would wander into a
                // neighbour's cell and two territories would fight over one animal.
                FaceSpaceCellRangeBuilder.DirectionToFaceUv(dir, out int face, out Vector2 uv);
                Assert.AreEqual(3, face);
                Assert.AreEqual(6, Mathf.FloorToInt(uv.x / CreatureTerritory.CellUvWidth));
                Assert.AreEqual(10, Mathf.FloorToInt(uv.y / CreatureTerritory.CellUvWidth));
            }
        }

        [Test]
        public void AttemptZero_IsStillTheSlotsPreferredSpot()
        {
            // The retry must not move where a slot WANTS to be, only where it settles for. Homes stay as
            // clustered as suitability allows rather than being scattered by the existence of the retry.
            var seeds = new SeedProvider(TestSeed);
            EntityId key = Slot();
            Assert.AreEqual(CreatureTerritory.HomeCandidate(seeds, key),
                CreatureTerritory.HomeCandidate(seeds, key, 0));
        }

        [Test]
        public void SlotsInOneTerritory_DoNotAllStandOnTheSamePoint()
        {
            var seeds = new SeedProvider(TestSeed);
            Vector3 a = CreatureTerritory.HomeCandidate(seeds, Slot(slot: 0));
            Vector3 b = CreatureTerritory.HomeCandidate(seeds, Slot(slot: 1));
            Vector3 c = CreatureTerritory.HomeCandidate(seeds, Slot(slot: 2));

            Assert.AreNotEqual(a, b);
            Assert.AreNotEqual(b, c);
            Assert.AreNotEqual(a, c);
        }

        // --- drifting home ---------------------------------------------------

        [Test]
        public void FastForward_MovesAlongTheSurfaceAndArrivesExactlyOnce()
        {
            Vector3 center = Vector3.zero;
            const float radius = 5000f;
            Vector3 home = new Vector3(1f, 0f, 0f) * radius;
            Vector3 away = Quaternion.AngleAxis(20f, Vector3.up) * home;   // ~1745 m of arc

            float arc = CreatureTerritory.SurfaceDistance(center, away, home);
            Assert.AreEqual(20f * Mathf.Deg2Rad * radius, arc, 1f);

            Vector3 part = CreatureTerritory.DriftToward(center, away, home, arc * 0.25f);
            Assert.AreEqual(arc * 0.75f, CreatureTerritory.SurfaceDistance(center, part, home), 1f,
                "a partial drift closes exactly the distance it was given");
            Assert.AreEqual(radius, part.magnitude, 0.5f, "drifting must not leave the surface");

            Vector3 arrived = CreatureTerritory.DriftToward(center, away, home, arc * 5f);
            Assert.AreEqual(0f, CreatureTerritory.SurfaceDistance(center, arrived, home), 0.5f,
                "an overshoot settles at home rather than sailing past it");
        }

        [Test]
        public void FastForward_IsAStepFunction_SoAnHourAwayCostsWhatAFrameCosts()
        {
            Vector3 center = Vector3.zero;
            Vector3 home = new Vector3(0f, 0f, 5000f);
            Vector3 away = Quaternion.AngleAxis(8f, Vector3.right) * home;

            // One 600-second step must land where 600 one-second steps land: that equivalence is what lets an
            // unobserved creature be skipped entirely instead of simulated.
            Vector3 stepped = away;
            for (int i = 0; i < 600; i++)
                stepped = CreatureTerritory.DriftToward(center, stepped, home, 0.8f);
            Vector3 jumped = CreatureTerritory.DriftToward(center, away, home, 0.8f * 600f);

            Assert.AreEqual(0f, CreatureTerritory.SurfaceDistance(center, stepped, jumped), 1f);
        }

        [Test]
        public void AnObserverInTheAir_IsStillNextToWhatIsBeneathIt()
        {
            // The bug this pins: promotion used straight-line distance while the planner collected territories
            // by direction from the planet centre. An observer 300 m up was a full bubble radius from a
            // creature DIRECTLY BENEATH them, so flying over a herd showed an empty world.
            Vector3 center = Vector3.zero;
            const float ground = 5000f;
            const float bubble = 300f;

            Vector3 creature = new Vector3(0f, 0f, 1f) * ground;
            Vector3 overhead = creature.normalized * (ground + 400f);   // 400 m up, well past the bubble

            Assert.Greater(Vector3.Distance(overhead, creature), bubble,
                "straight-line distance alone would reject it, which is exactly what went wrong");
            Assert.AreEqual(0f, CreatureTerritory.SurfaceDistance(center, creature, overhead), 1f,
                "along the surface it is directly underfoot, and that is what the bubble must measure");
        }

        [Test]
        public void AltitudeDoesNotEatTheBubble()
        {
            Vector3 center = Vector3.zero;
            const float ground = 5000f;

            // 200 m along the surface, seen from 500 m up: still 200 m of ground between them.
            Vector3 creature = new Vector3(0f, 0f, 1f) * ground;
            Vector3 observerGround = Quaternion.AngleAxis(200f / ground * Mathf.Rad2Deg, Vector3.up) * creature;
            Vector3 observerHigh = observerGround.normalized * (ground + 500f);

            Assert.AreEqual(200f, CreatureTerritory.SurfaceDistance(center, creature, observerHigh), 15f);
        }

        [Test]
        public void Drifting_IsSafeAtHomeAndAtThePlanetCentre()
        {
            Vector3 home = new Vector3(0f, 5000f, 0f);
            Assert.AreEqual(0f,
                Vector3.Distance(home, CreatureTerritory.DriftToward(Vector3.zero, home, home, 100f)), 1e-2f);
            Assert.AreEqual(Vector3.zero, CreatureTerritory.DriftToward(Vector3.zero, Vector3.zero, home, 100f));
            Assert.AreEqual(home, CreatureTerritory.DriftToward(Vector3.zero, home, home, 0f));
            Assert.AreEqual(0f, CreatureTerritory.SurfaceDistance(Vector3.zero, Vector3.zero, home));
        }
    }
}
