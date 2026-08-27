using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // What a creature is afraid of. The parts worth pinning are the ones a play-test cannot show reliably:
    // that relations are directed, that the free camera can never become a threat, that a friendly effect
    // changes one caster rather than species data, and that the effect expires on a real clock.
    public sealed class CreatureThreatTests
    {
        const long Now = 1787760000;   // a real unix timestamp, not a toy one - float epoch math rounds to ~128 s

        static ThreatRegistry Registry()
        {
            var r = new ThreatRegistry();
            r.SetRelations(FactionRelationsDto.Default);
            return r;
        }

        static EntityId Wolf => new(EntityId.HostOwner, 100);
        static EntityId OtherDeer => CreatureKey.Individual(1, CreatureTerritory.Level, 2, 3, 1, 0);

        // --- the table ------------------------------------------------------

        [Test]
        public void WildlifeIgnoresWildlife_WhichIsWhyDeerDoNotFleeRabbits()
        {
            FactionRelationsDto t = FactionRelationsDto.Default;
            Assert.AreEqual(FactionRelation.Neutral,
                t.Of(CreatureFaction.Wildlife, CreatureFaction.Wildlife));
            Assert.IsFalse(t.IsThreat(CreatureFaction.Wildlife, CreatureFaction.Wildlife),
                "one cell has to cover deer, rabbits and birds, or it becomes a rule written per species");
        }

        [Test]
        public void ThePlayerIsAThreatToWildlifeByDefault()
        {
            // Bryan, 2026-08-26: deer bolt on sight, armed or not.
            Assert.IsTrue(FactionRelationsDto.Default.IsThreat(CreatureFaction.Wildlife, CreatureFaction.Player));
        }

        [Test]
        public void RelationsAreDirected_NotSymmetric()
        {
            FactionRelationsDto t = FactionRelationsDto.Default;

            // A wolf HUNTS a deer; a deer FEARS a wolf. A symmetric "are we friends" flag cannot say this.
            Assert.AreEqual(FactionRelation.Hostile, t.Of(CreatureFaction.Predator, CreatureFaction.Wildlife));
            Assert.AreEqual(FactionRelation.Afraid, t.Of(CreatureFaction.Wildlife, CreatureFaction.Predator));
            Assert.AreNotEqual(t.Of(CreatureFaction.Predator, CreatureFaction.Wildlife),
                t.Of(CreatureFaction.Wildlife, CreatureFaction.Predator));
        }

        [Test]
        public void AnUnauthoredTable_LeavesEverythingNeutral()
        {
            // Neutral is the right default: a new faction ignores everything until someone says otherwise.
            var empty = new FactionRelationsDto(new FactionRelation[0]);
            Assert.AreEqual(FactionRelation.Neutral, empty.Of(CreatureFaction.Wildlife, CreatureFaction.Player));
        }

        // --- presence is not threat ------------------------------------------

        [Test]
        public void AnEmptyRegistry_FrightensNobody()
        {
            // The residency bubble is a set of POSITIONS and never appears here. A free camera flying through
            // a herd contributes nothing to this list, so nothing runs - no special case for cameras needed.
            ThreatRegistry r = Registry();
            Assert.IsFalse(r.TryFindThreat(Vector3.zero, OtherDeer, CreatureFaction.Wildlife, 1000f, Now, out _));
        }

        [Test]
        public void ACreatureDoesNotFrightenItself()
        {
            ThreatRegistry r = Registry();
            r.Report(OtherDeer, Vector3.zero, CreatureFaction.Wildlife);
            r.Report(Wolf, new Vector3(5f, 0f, 0f), CreatureFaction.Predator);

            Assert.IsTrue(r.TryFindThreat(Vector3.zero, OtherDeer, CreatureFaction.Wildlife, 50f, Now,
                out ThreatSource found));
            Assert.AreEqual(Wolf.Value, found.Id.Value, "it must find the wolf, not itself");
        }

        [Test]
        public void OnlyThreatsInsideTheAwarenessRadiusAreNoticed()
        {
            ThreatRegistry r = Registry();
            r.Report(Wolf, new Vector3(40f, 0f, 0f), CreatureFaction.Predator);

            Assert.IsFalse(r.TryFindThreat(Vector3.zero, OtherDeer, CreatureFaction.Wildlife, 35f, Now, out _),
                "beyond the radius");
            Assert.IsTrue(r.TryFindThreat(Vector3.zero, OtherDeer, CreatureFaction.Wildlife, 45f, Now, out _),
                "inside the radius");
        }

        [Test]
        public void TheNearestThreatWins()
        {
            ThreatRegistry r = Registry();
            var far = new EntityId(EntityId.HostOwner, 101);
            r.Report(far, new Vector3(30f, 0f, 0f), CreatureFaction.Player);
            r.Report(Wolf, new Vector3(10f, 0f, 0f), CreatureFaction.Predator);

            Assert.IsTrue(r.TryFindThreat(Vector3.zero, OtherDeer, CreatureFaction.Wildlife, 100f, Now,
                out ThreatSource found));
            Assert.AreEqual(Wolf.Value, found.Id.Value);
        }

        [Test]
        public void WithdrawingAThreat_StopsTheFear()
        {
            ThreatRegistry r = Registry();
            r.Report(Wolf, Vector3.zero, CreatureFaction.Predator);
            Assert.IsTrue(r.TryFindThreat(Vector3.zero, OtherDeer, CreatureFaction.Wildlife, 50f, Now, out _));

            r.Withdraw(Wolf);   // this is what despawning the character does
            Assert.IsFalse(r.TryFindThreat(Vector3.zero, OtherDeer, CreatureFaction.Wildlife, 50f, Now, out _));
        }

        [Test]
        public void ReportingAgain_MovesTheThreatRatherThanDuplicatingIt()
        {
            ThreatRegistry r = Registry();
            r.Report(Wolf, Vector3.zero, CreatureFaction.Predator);
            r.Report(Wolf, new Vector3(500f, 0f, 0f), CreatureFaction.Predator);

            Assert.AreEqual(1, r.Sources.Count, "a per-frame report must not grow the list every frame");
            Assert.IsFalse(r.TryFindThreat(Vector3.zero, OtherDeer, CreatureFaction.Wildlife, 50f, Now, out _),
                "it moved away, so it is no longer near");
        }

        // --- the spell --------------------------------------------------------

        [Test]
        public void ADisguise_MakesOneCasterSafeWithoutTouchingSpeciesData()
        {
            ThreatRegistry r = Registry();
            var otherPlayer = new EntityId(EntityId.HostOwner, 200);
            r.Report(ThreatRegistry.LocalPlayer, Vector3.zero, CreatureFaction.Player);
            r.Report(otherPlayer, new Vector3(1f, 0f, 0f), CreatureFaction.Player);

            r.SetDisguise(ThreatRegistry.LocalPlayer, CreatureFaction.Wildlife, 30f, Now);

            Assert.IsTrue(r.TryFindThreat(Vector3.zero, OtherDeer, CreatureFaction.Wildlife, 50f, Now,
                out ThreatSource found));
            Assert.AreEqual(otherPlayer.Value, found.Id.Value,
                "the effect is on ONE caster; everyone else is still a player to the wildlife");

            // And the table itself is untouched - nothing global changed.
            Assert.IsTrue(FactionRelationsDto.Default.IsThreat(CreatureFaction.Wildlife, CreatureFaction.Player));
        }

        [Test]
        public void ADisguiseExpiresOnARealClock()
        {
            ThreatRegistry r = Registry();
            r.Report(ThreatRegistry.LocalPlayer, Vector3.zero, CreatureFaction.Player);
            r.SetDisguise(ThreatRegistry.LocalPlayer, CreatureFaction.Wildlife, 30f, Now);

            Assert.IsFalse(r.TryFindThreat(Vector3.zero, OtherDeer, CreatureFaction.Wildlife, 50f, Now + 29, out _),
                "still friendly one second before it lapses");
            Assert.IsTrue(r.TryFindThreat(Vector3.zero, OtherDeer, CreatureFaction.Wildlife, 50f, Now + 30, out _),
                "the moment it lapses, the player is a threat again");

            // Whole-second precision at a real timestamp: doing this arithmetic in float rounds the epoch to
            // the nearest ~128 s and the effect would appear to last minutes longer than it does.
            Assert.AreEqual(30f, r.DisguiseSecondsLeft(ThreatRegistry.LocalPlayer, Now), 0.5f);
            Assert.AreEqual(1f, r.DisguiseSecondsLeft(ThreatRegistry.LocalPlayer, Now + 29), 0.5f);
        }

        [Test]
        public void ALapsedDisguiseIsPruned_RatherThanAccumulating()
        {
            ThreatRegistry r = Registry();
            r.SetDisguise(ThreatRegistry.LocalPlayer, CreatureFaction.Wildlife, 10f, Now);
            Assert.AreEqual(1, r.DisguiseCount);

            r.PruneExpired(Now + 5);
            Assert.AreEqual(1, r.DisguiseCount, "still running");

            r.PruneExpired(Now + 11);
            Assert.AreEqual(0, r.DisguiseCount);
        }

        [Test]
        public void AZeroDurationDisguise_ClearsRatherThanLingeringForever()
        {
            ThreatRegistry r = Registry();
            r.SetDisguise(ThreatRegistry.LocalPlayer, CreatureFaction.Wildlife, 30f, Now);
            r.SetDisguise(ThreatRegistry.LocalPlayer, CreatureFaction.Wildlife, 0f, Now);
            Assert.AreEqual(0, r.DisguiseCount);
        }

        // --- behaviour survives demotion ---------------------------------------

        static CreatureSpeciesDto Deer => new("Deer", 3, 120f, 2.5f, 0.8f, 300f, 2f, 3000f, 1.7f,
            Color.white, new[] { BiomeType.Forest }, CreatureFaction.Wildlife, 45f, 3, "Hide", 2, 0f);

        [Test]
        public void ABrainRebuiltFromARememberedBehaviour_ResumesIt()
        {
            // The machine is a promotion-time artefact and dies at demotion; the BEHAVIOUR is a value that
            // survives it. Rebuilding from that value is what stops a creature that was fleeing when you
            // walked away from being calm when you come back - the red-car problem one layer up.
            var resumed = new CreatureBrain(1234, Deer, CreatureBehaviour.Flee);
            Assert.AreEqual(CreatureBehaviour.Flee, resumed.Behaviour);

            var fresh = new CreatureBrain(1234, Deer, CreatureBehaviour.Wander);
            Assert.AreEqual(CreatureBehaviour.Wander, fresh.Behaviour);
        }

        [Test]
        public void AResumedFlee_EndsItselfOnceNothingIsChasing()
        {
            var brain = new CreatureBrain(1234, Deer, CreatureBehaviour.Flee);
            brain.Observe(new CreatureSenses
            {
                Position = Vector3.zero,
                Up = Vector3.up,
                Forward = Vector3.forward,
                Home = Vector3.zero,
                Species = Deer,
                HasThreat = false,      // the threat is gone or outrun
            });
            brain.Sample(0);

            Assert.AreEqual(CreatureBehaviour.Wander, brain.Behaviour,
                "flee ends ITSELF when the threat is gone, rather than a table polling for it");
        }

        [Test]
        public void AThreatOverridesWhateverTheAnimalWasDoing()
        {
            var brain = new CreatureBrain(1234, Deer, CreatureBehaviour.Wander);
            brain.Observe(new CreatureSenses
            {
                Position = Vector3.zero,
                Up = Vector3.up,
                Forward = Vector3.forward,
                Home = Vector3.zero,
                Species = Deer,
                HasThreat = true,
                ThreatPosition = new Vector3(5f, 0f, 0f),
                ThreatDistance = 5f,
            });
            brain.Sample(0);

            Assert.AreEqual(CreatureBehaviour.Flee, brain.Behaviour);
            Assert.Greater(brain.SpeedScale, 1f, "fleeing is faster than grazing");
        }

        // --- identity ---------------------------------------------------------

        [Test]
        public void ThePlayersRegistryId_CollidesWithNeitherMintedIdsNorCreatures()
        {
            // It shares a keyspace with both, and a collision would make a dropped log or a deer BE the player.
            var host = new EntityIdAllocator(EntityId.HostOwner);
            for (int i = 0; i < 1000; i++)
                Assert.AreNotEqual(ThreatRegistry.LocalPlayer.Value, host.Next().Value);

            Assert.IsFalse(CreatureKey.IsCreature(ThreatRegistry.LocalPlayer),
                "a creature address always sets bit 47 of its counter; this one must not");
            Assert.IsFalse(ThreatRegistry.LocalPlayer.IsNone);
        }

        // --- perching: only fliers, and only for a while --------------------

        static CreatureSpeciesDto Flier => new("Bird", 4, 200f, 6f, 2.5f, 180f, 2f, 4000f, 0.35f,
            Color.white, System.Array.Empty<BiomeType>(), CreatureFaction.Wildlife, 70f, 1, "Feathers", 2, 9f);

        static CreatureSpeciesDto Walker => new("Deer", 3, 120f, 2.5f, 0.8f, 300f, 2f, 3000f, 1.7f,
            Color.white, System.Array.Empty<BiomeType>(), CreatureFaction.Wildlife, 45f, 3, "Hide", 2, 0f);

        static CreatureSenses Calm(CreatureSpeciesDto species, uint tick) => new()
        {
            Position = new Vector3(0f, 1000f, 0f),
            Up = Vector3.up,
            Forward = Vector3.forward,
            Home = new Vector3(0f, 1000f, 0f),
            DeltaTime = 0.02f,
            Tick = tick,
            Species = species,
        };

        static CreatureBehaviour RunFor(CreatureSpeciesDto species, int seed, uint ticks)
        {
            var brain = new CreatureBrain(seed, species, CreatureBehaviour.Wander);
            for (uint t = 0; t < ticks; t++)
            {
                brain.Observe(Calm(species, t));
                brain.Sample(t);
            }
            return brain.Behaviour;
        }

        [Test]
        public void AWalkerNeverPerches_HoweverLongItWanders()
        {
            // Perching is the one behaviour gated on being a flier. A deer reaching it would mean the gate is
            // reading something other than the cruise altitude.
            for (int seed = 1; seed <= 12; seed++)
                for (uint ticks = 100; ticks <= 3000; ticks += 700)
                    Assert.AreNotEqual(CreatureBehaviour.Perch, RunFor(Walker, seed, ticks),
                        $"deer perched at seed {seed} after {ticks} ticks");
        }

        [Test]
        public void AFlierPerchesEventually_AndTakesOffAgain()
        {
            bool everPerched = false, everBackUp = false;
            for (int seed = 1; seed <= 12 && !(everPerched && everBackUp); seed++)
            {
                var species = Flier;
                var brain = new CreatureBrain(seed, species, CreatureBehaviour.Wander);
                bool sawPerch = false;
                for (uint t = 0; t < 4000; t++)
                {
                    brain.Observe(Calm(species, t));
                    brain.Sample(t);
                    if (brain.Behaviour == CreatureBehaviour.Perch) { sawPerch = true; everPerched = true; }
                    else if (sawPerch) everBackUp = true;
                }
            }
            Assert.IsTrue(everPerched, "no bird ever landed across twelve seeds and 80 s of ticks");
            Assert.IsTrue(everBackUp, "a bird landed and never took off again");
        }

        [Test]
        public void AThreatBeatsAPerch()
        {
            // Fear overrides from ANY state. A bird that sits on the ground while something walks up to it is
            // the transition table having gained a hole.
            var species = Flier;
            var brain = new CreatureBrain(7, species, CreatureBehaviour.Perch);

            CreatureSenses scared = Calm(species, 10);
            scared.HasThreat = true;
            scared.ThreatPosition = new Vector3(5f, 1000f, 0f);
            scared.ThreatDistance = 5f;

            brain.Observe(scared);
            brain.Sample(10);
            Assert.AreEqual(CreatureBehaviour.Flee, brain.Behaviour);
        }

        [Test]
        public void FlightGroundingAddsItsAltitudeOnTop()
        {
            var flat = new FlatGrounding();
            var flight = new FlightGrounding(flat) { AltitudeMeters = 9f };

            Assert.IsTrue(flight.TryGround(Vector3.zero, Vector3.down, 0.2f, out _));
            Assert.AreEqual(9.2f, flat.LastFootOffset, 1e-4f, "the wrapper adds, it does not replace");

            // Perched is zero, and a negative altitude must not pull a body underground.
            flight.AltitudeMeters = 0f;
            flight.TryGround(Vector3.zero, Vector3.down, 0.2f, out _);
            Assert.AreEqual(0.2f, flat.LastFootOffset, 1e-4f);

            flight.AltitudeMeters = -50f;
            flight.TryGround(Vector3.zero, Vector3.down, 0.2f, out _);
            Assert.AreEqual(0.2f, flat.LastFootOffset, 1e-4f, "a negative altitude is clamped away");
        }

        sealed class FlatGrounding : IGroundingProvider
        {
            public float LastFootOffset;

            public bool TryGround(Vector3 worldPos, Vector3 downDir, float footOffset, out GroundResult result)
            {
                LastFootOffset = footOffset;
                result = new GroundResult(worldPos + Vector3.up * footOffset, Vector3.up);
                return true;
            }
        }
    }
}
