using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class BirdPerchTests
    {
        static EntityId Bird => new(EntityId.HostOwner, 710);
        static EntityId Other => new(EntityId.HostOwner, 711);
        static WildlifeLandingTarget Site(Vector3 position) => new(1, position, Vector3.up, 1f, WildlifeLandingUse.Bird);

        [Test]
        public void ClaimsExcludeOtherAnimalsAndDisappearWithTheSite()
        {
            var sites = new WildlifeLandingTargets();
            sites.Publish(new[] { Site(Vector3.up * 1005f) });
            Assert.IsTrue(sites.TryClaim(1, Bird));
            Assert.IsFalse(sites.TryClaim(1, Other));
            sites.Release(1, Other);
            Assert.IsFalse(sites.TryFind(Vector3.up * 1009f, 25f, 0.35f, WildlifeLandingUse.Bird, Other, out _));
            Assert.IsFalse(sites.TryFind(Vector3.up * 1009f, 25f, 0.35f, WildlifeLandingUse.Bee, Bird, out _));
            sites.Publish(Array.Empty<WildlifeLandingTarget>());
            sites.Publish(new[] { Site(Vector3.up * 1005f) });
            Assert.IsTrue(sites.TryClaim(1, Other));
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void BirdLandsOnRaisedSupportThenTakesOffWithoutDropping(int fps)
        {
            using var fixture = new FlightFixture(new Vector3(8f, 1005f, 0f));
            float dt = 1f / fps;
            bool landed = false, departed = false;
            for (int i = 0; i < fps * 25; i++)
            {
                fixture.Tick(dt);
                if (fixture.Flight.HasSupport && fixture.Flight.AltitudeMeters <= 0.001f)
                {
                    Assert.Less(Mathf.Abs(fixture.Position.x - 8f), 0.21f);
                    landed = true;
                }
                if (landed && fixture.Behaviour != CreatureBehaviour.Perch)
                {
                    Assert.GreaterOrEqual(fixture.Position.y, 1005.17f);
                    Assert.IsFalse(fixture.Flight.HasSupport);
                    Assert.IsTrue(fixture.Service.LandingTargets.TryClaim(1, Other));
                    departed = true;
                    break;
                }
            }
            Assert.IsTrue(landed, "Bird must reach the branch contact height.");
            Assert.IsTrue(departed, "Bird must finish its rest and release the branch.");
        }

        [TestCase("removed")]
        [TestCase("moved")]
        [TestCase("predator")]
        [TestCase("flooded")]
        public void InterruptedPerchReleasesClaimAndEscapes(string reason)
        {
            using var fixture = new FlightFixture();
            for (int i = 0; i < 180; i++) fixture.Tick(1f / 60f);
            Assert.IsTrue(fixture.Flight.HasSupport);
            Assert.Less(fixture.Flight.AltitudeMeters, 0.001f);
            float before = fixture.Position.y;
            if (reason == "removed") fixture.Service.LandingTargets.Publish(Array.Empty<WildlifeLandingTarget>());
            if (reason == "moved") fixture.Service.LandingTargets.Publish(new[] { Site(Vector3.up * 1006f) });
            if (reason == "predator") fixture.Threats.Report(Other, fixture.Position + Vector3.forward, CreatureFaction.Predator);
            if (reason == "flooded") Set(fixture.Service, "_seaLevelRadius", 1006f);
            fixture.Tick(1f / 60f);
            Assert.AreEqual(reason == "predator" ? CreatureBehaviour.Flee : CreatureBehaviour.Wander, fixture.Behaviour);
            Assert.IsFalse(fixture.Flight.HasSupport);
            Assert.Greater(fixture.Position.y, before);
            fixture.Service.LandingTargets.Publish(new[] { Site(Vector3.up * 1005f) });
            Assert.IsTrue(fixture.Service.LandingTargets.TryClaim(1, Other));
        }

        [TestCase("Demote")]
        [TestCase("Retire")]
        public void LeavingResidencyReleasesTheBranch(string method)
        {
            using var fixture = new FlightFixture();
            fixture.Tick(1f / 60f);
            Assert.IsFalse(fixture.Service.LandingTargets.TryClaim(1, Other));
            typeof(CreatureResidencyService).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(fixture.Service, method == "Demote" ? new[] { fixture.Resident, (object)100L } : new[] { fixture.Resident });
            Assert.IsTrue(fixture.Service.LandingTargets.TryClaim(1, Other));
            if (method == "Demote") Assert.AreEqual(CreatureBehaviour.Wander, fixture.Behaviour);
        }

        [Test]
        public void SleepKeepsBranchSupportAndPredatorInterruptsIt()
        {
            using var fixture = new FlightFixture(tired: true);
            bool slept = false;
            for (int i = 0; i < 60 * 20; i++)
            {
                fixture.Tick(1f / 60f);
                if (fixture.Behaviour != CreatureBehaviour.Sleep) continue;
                slept = true;
                Assert.That(fixture.Flight.HasSupport, Is.True);
                Assert.That(fixture.Position.y, Is.EqualTo(1005.175f).Within(.01f));
                Assert.That(fixture.Service.LandingTargets.TryClaim(1, Other), Is.False);
                break;
            }
            Assert.That(slept, Is.True);
            float before = fixture.Position.y;
            fixture.Threats.Report(Other, fixture.Position + Vector3.forward, CreatureFaction.Predator);
            fixture.Tick(1f / 60f);
            Assert.That(fixture.Behaviour, Is.EqualTo(CreatureBehaviour.Flee));
            Assert.That(fixture.Flight.HasSupport, Is.False);
            Assert.That(fixture.Position.y, Is.GreaterThan(before));
        }

        [Test]
        public void ScavengerLandsBeforeConsumingMeatAndTakesOffFromThreat()
        {
            using var fixture = new FlightFixture(feeding: true);
            bool fed = false;
            for (int i = 0; i < 60 * 15; i++)
            {
                fixture.Tick(1f / 60f);
                double hunger = Get<ActorNeeds>(fixture.Resident, "Needs").Hunger;
                if (fixture.Flight.AltitudeMeters > .01f) Assert.That(hunger, Is.EqualTo(.9d).Within(.00001));
                if (hunger >= .85d) continue;
                Assert.That(fixture.Behaviour, Is.EqualTo(CreatureBehaviour.Feed));
                Assert.That(fixture.Flight.HasSupport, Is.True);
                fed = true;
                break;
            }
            Assert.That(fed, Is.True, "A scavenger must consume actual carcass nutrition after landing.");
            fixture.Threats.Report(Other, fixture.Position + Vector3.forward, CreatureFaction.Predator);
            float before = fixture.Position.y;
            fixture.Tick(1f / 60f);
            Assert.That(fixture.Behaviour, Is.EqualTo(CreatureBehaviour.Flee));
            Assert.That(fixture.Flight.HasSupport, Is.False);
            Assert.That(fixture.Position.y, Is.GreaterThan(before));
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void TiredBirdChoosesLandingWithoutWaitingForRandomPerch(bool exhausted, bool sleepy)
        {
            var species = CreatureLibraryDto.Placeholder.At(2);
            var brain = new CreatureBrain(5, species, CreatureBehaviour.Wander);
            brain.Observe(new CreatureSenses { Species = species, Position = Vector3.up * 1009,
                Up = Vector3.up, Forward = Vector3.forward, DeltaTime = .01f, AltitudeMeters = 9f,
                HasLandingTarget = true, LandingTarget = Vector3.up * 1000,
                NeedsRecovery = exhausted, NeedsSleep = sleepy });
            brain.Sample(0);
            Assert.That(brain.Behaviour, Is.EqualTo(CreatureBehaviour.Perch));
        }

        static void Set(object obj, string name, object value) => obj.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(obj, value);
        static T Get<T>(object obj, string name) => (T)obj.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(obj);

        sealed class FlightFixture : IDisposable
        {
            public readonly CreatureResidencyService Service;
            public readonly object Resident;
            public readonly FlightGrounding Flight;
            public readonly ThreatRegistry Threats = new();
            public Vector3 Position => Get<Vector3>(Resident, "Position");
            public CreatureBehaviour Behaviour => Get<CreatureBehaviour>(Resident, "Behaviour");
            readonly MethodInfo _simulate = typeof(CreatureResidencyService).GetMethod("Simulate", BindingFlags.Instance | BindingFlags.NonPublic);

            public FlightFixture(Vector3? contact = null, bool tired = false, bool feeding = false)
            {
                Vector3 sitePosition = contact ?? Vector3.up * 1005f;
                var surface = new Sphere();
                Service = new CreatureResidencyService(null, surface, null);
                var species = new CreatureSpeciesDto("Bird", 4, 200f, 6f, 2.5f, 180f, 2f, 4000f, 0.35f,
                    Color.white, Array.Empty<BiomeType>(), CreatureFaction.Wildlife, 70f, 1, "Feathers", 2, 9f);
                if (tired) species = species with { Endurance = new ActorEnduranceProfile(20, 99999, 120) };
                if (feeding) species = species with { Scavenger = true, Diet = ResourceKind.Meat,
                    HungerSeconds = 0, ThirstSeconds = 0,
                    Perception = new ActorPerceptionProfile(ViewAngle: 360, SightResolution: 0, SmellResolution: 0) };
                var ground = new PlanetSurfaceGrounding(surface, Vector3.zero);
                Set(Service, "_grounding", ground);
                Set(Service, "_birdLandingGround", new BirdLandingGround(surface, Vector3.zero, 999f));
                Set(Service, "_library", new CreatureLibraryDto(new[] { species }, 300f));
                Threats.SetRelations(FactionRelationsDto.Default);
                Set(Service, "_threats", Threats);
                Set(Service, "_seaLevelRadius", 999f);
                Service.LandingTargets.Publish(new[] { Site(sitePosition) });
                Resident = Activator.CreateInstance(typeof(CreatureResidencyService).GetNestedType("Resident", BindingFlags.NonPublic), true);
                Flight = new FlightGrounding(ground) { AltitudeMeters = 9f };
                Set(Resident, "Id", Bird);
                Set(Resident, "Health", species.MaxHealth);
                Set(Resident, "Position", Vector3.up * 1009.175f);
                Set(Resident, "Home", Vector3.up * 1000f);
                Set(Resident, "Forward", Vector3.forward);
                Set(Resident, "Flight", Flight);
                Set(Resident, "Driver", new SurfaceCharacterController(new RadialGravityProvider(Vector3.zero), Flight, 0.175f,
                    new CharacterPose(Position, Vector3.up, Vector3.forward)));
                Set(Resident, "Brain", new CreatureBrain(7, species, feeding ? CreatureBehaviour.Wander : CreatureBehaviour.Perch));
                Set(Resident, "Behaviour", feeding ? CreatureBehaviour.Wander : CreatureBehaviour.Perch);
                Set(Resident, "HasLandingTarget", true);
                Set(Resident, "LandingSiteId", 1UL);
                Set(Resident, "LandingTarget", sitePosition);
                if (tired) Set(Resident, "Endurance", new ActorEndurance(.5d, .9d, needsSleep: true));
                if (feeding)
                {
                    Set(Resident, "Perception", new ActorPerception(species.Perception));
                    Set(Resident, "Needs", new ActorNeeds(.9d, 0));
                    var corpses = new CreatureCorpseStore();
                    corpses.Record(0, Vector3.up * 1000f, Quaternion.identity, 100);
                    Set(Service, "_corpses", corpses);
                }
            }
            public void Tick(float dt) => _simulate.Invoke(Service, new[] { Resident, (object)dt, 100L });
            public void Dispose() => Service.Dispose();
        }

        sealed class Sphere : IPlanetSurfaceSampler
        {
            public bool TryGetSurfaceRadius(Vector3 direction, out float radius) { radius = 1000f; return true; }
        }
    }
}
