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
            HasLandingTarget = true,
            LandingTarget = new Vector3(0f, 1000f, 0f),
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
        public void AFlierDoesNotUseGroundGrazingPausesWhileWandering()
        {
            int wandering = 0;
            for (int seed = 0; seed < 50; seed++)
            {
                var brain = new CreatureBrain(seed, Flier, CreatureBehaviour.Wander);
                brain.Observe(Calm(Flier, 0));
                ActorIntent intent = brain.Sample(0);
                if (brain.Behaviour != CreatureBehaviour.Wander) continue;
                wandering++;
                Assert.Greater(intent.Move.y, 0f);
            }
            Assert.Greater(wandering, 0);
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

        [TestCase(30)]
        [TestCase(50)]
        [TestCase(120)]
        public void PerchDurationUsesSecondsRatherThanFrameCount(int fps)
        {
            var brain = new CreatureBrain(7, Flier, CreatureBehaviour.Perch);
            int frames = 0;
            while (brain.Behaviour == CreatureBehaviour.Perch && frames <= fps * 13)
            {
                CreatureSenses senses = Calm(Flier, (uint)frames);
                senses.DeltaTime = 1f / fps;
                brain.Observe(senses);
                brain.Sample((uint)frames++);
            }
            Assert.AreEqual(CreatureBehaviour.Wander, brain.Behaviour);
            Assert.That((double)frames / fps, Is.InRange(12d - 0.0001d, 12d + 1d / fps + 0.0001d));
        }

        [Test]
        public void PausedSimulationDoesNotUseIntentTicksAsElapsedTime()
        {
            var brain = new CreatureBrain(7, Flier, CreatureBehaviour.Perch);
            for (uint tick = 0; tick < 10000; tick += 100)
            {
                CreatureSenses senses = Calm(Flier, tick);
                senses.DeltaTime = 0f;
                brain.Observe(senses);
                brain.Sample(tick);
            }
            Assert.AreEqual(CreatureBehaviour.Perch, brain.Behaviour);
        }

        [Test]
        public void DescentDoesNotConsumeTheBirdsRestPeriod()
        {
            var brain = new CreatureBrain(7, Flier, CreatureBehaviour.Perch);
            for (uint frame = 0; frame < 1500; frame++)
            {
                CreatureSenses senses = Calm(Flier, frame);
                senses.AltitudeMeters = 2f;
                brain.Observe(senses);
                brain.Sample(frame);
            }
            Assert.AreEqual(CreatureBehaviour.Perch, brain.Behaviour);
            for (uint frame = 1500; frame < 2050; frame++)
            {
                brain.Observe(Calm(Flier, frame));
                brain.Sample(frame);
            }
            Assert.AreEqual(CreatureBehaviour.Perch, brain.Behaviour, "only eleven seconds have passed on the ground");
        }

        [Test]
        public void WanderDecisionsMatchAtEqualElapsedTimeAcrossFrameRates()
        {
            ActorIntent Run(int fps)
            {
                var brain = new CreatureBrain(123, Walker, CreatureBehaviour.Wander);
                ActorIntent intent = default;
                for (uint frame = 0; frame < fps * 9.5f; frame++)
                {
                    CreatureSenses senses = Calm(Walker, frame);
                    senses.DeltaTime = 1f / fps;
                    brain.Observe(senses);
                    intent = brain.Sample(frame);
                }
                return intent;
            }
            ActorIntent at30 = Run(30), at120 = Run(120);
            Assert.AreEqual(at30.Move, at120.Move);
            Assert.AreEqual(at30.Look, at120.Look);
        }

        sealed class RidgeSurface : IPlanetSurfaceSampler
        {
            public bool Available = true;
            public bool TryGetSurfaceRadius(Vector3 direction, out float radius)
            {
                radius = direction.x > 0.004f && direction.x < 0.008f ? 1005f : 1000f;
                return Available;
            }
        }

        [TestCase(0f)]
        [TestCase(10000f)]
        public void TerrainHidesNearestThreatWithoutHidingAnUnblockedThreat(float centerX)
        {
            var registry = Registry();
            Vector3 center = new(centerX, 0f, 0f);
            registry.ConfigureTerrain(new RidgeSurface(), center);
            Vector3 observer = center + Vector3.up * 1001f;
            registry.Report(Wolf, observer + Vector3.right * 10f, CreatureFaction.Predator);
            Assert.IsFalse(registry.TryFindThreat(observer, OtherDeer, CreatureFaction.Wildlife, 50f, Now, out _));
            var visible = new EntityId(EntityId.HostOwner, 101);
            registry.Report(visible, observer + Vector3.forward * 20f, CreatureFaction.Predator);
            Assert.IsTrue(registry.TryFindThreat(observer, OtherDeer, CreatureFaction.Wildlife, 50f, Now, out ThreatSource found));
            Assert.AreEqual(visible, found.Id);
        }

        [Test]
        public void MissingTerrainDoesNotGrantSightThroughUnloadedGround()
        {
            var registry = Registry();
            registry.ConfigureTerrain(new RidgeSurface { Available = false }, Vector3.zero);
            registry.Report(Wolf, new Vector3(10f, 1001f, 0f), CreatureFaction.Predator);
            Assert.IsFalse(registry.TryFindThreat(Vector3.up * 1001f, OtherDeer,
                CreatureFaction.Wildlife, 50f, Now, out _));
        }

        [Test]
        public void BirdWithoutSafeSupportKeepsFlyingAndAbortsAnExistingLanding()
        {
            foreach (CreatureBehaviour start in new[] { CreatureBehaviour.Wander, CreatureBehaviour.Perch })
            {
                var brain = new CreatureBrain(7, Flier, start);
                for (uint tick = 0; tick < 4000; tick++)
                {
                    CreatureSenses senses = Calm(Flier, tick);
                    senses.HasLandingTarget = false;
                    brain.Observe(senses);
                    brain.Sample(tick);
                    Assert.AreEqual(CreatureBehaviour.Wander, brain.Behaviour);
                }
            }
        }

        [Test]
        public void BirdApproachesItsTargetAndDoesNotRestBeforeArrival()
        {
            var brain = new CreatureBrain(7, Flier, CreatureBehaviour.Perch);
            ActorIntent intent = default;
            for (uint tick = 0; tick < 1000; tick++)
            {
                CreatureSenses senses = Calm(Flier, tick);
                senses.LandingTarget += Vector3.forward * 10f;
                brain.Observe(senses);
                intent = brain.Sample(tick);
            }
            Assert.AreEqual(CreatureBehaviour.Perch, brain.Behaviour);
            Assert.Greater(intent.Move.y, 0f, "the approach must move toward the site");
            brain.Observe(Calm(Flier, 1000));
            intent = brain.Sample(1000);
            Assert.AreEqual(0f, intent.Move.y, "arrival stops horizontal movement");
            Assert.AreEqual(CreatureBehaviour.Perch, brain.Behaviour, "travel cannot consume the rest");
        }

        [TestCase(0f)]
        [TestCase(10000f)]
        public void BirdGroundSitesRejectWaterSlopesAndUnavailableSamples(float centerX)
        {
            Vector3 center = new(centerX, 0f, 0f);
            var surface = new LandingSurface();
            var landing = new BirdLandingGround(surface, center, 999f);
            Assert.IsTrue(landing.TryFind(center + Vector3.up * 1010f, 0.35f, out Vector3 point));
            Assert.Less(Vector3.Distance(point, center + Vector3.up * 1000f), 0.01f);
            surface.Slope = 1f;
            Assert.IsFalse(landing.TryFind(center + Vector3.up * 1010f, 0.35f, out _));
            surface.Slope = 0f;
            Assert.IsFalse(new BirdLandingGround(surface, center, 1001f)
                .TryFind(center + Vector3.up * 1010f, 0.35f, out _));
            surface.Available = false;
            Assert.IsFalse(landing.TryFind(center + Vector3.up * 1010f, 0.35f, out _));
        }

        sealed class LandingSurface : IPlanetSurfaceSampler
        {
            public bool Available = true;
            public float Slope;
            public bool TryGetSurfaceRadius(Vector3 direction, out float radius)
            {
                radius = 1000f + direction.x * 1000f * Slope;
                return Available;
            }
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
