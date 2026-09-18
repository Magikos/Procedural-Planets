using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class AmbientWildlifeBrainTests
    {
        sealed class Surface : IPlanetSurfaceSampler
        {
            public bool Available = true;
            public bool TryGetSurfaceRadius(Vector3 up, out float radius) { radius = 1000f; return Available; }
        }

        static AmbientSwarmProfile Profile(AmbientSwarmKind kind, int count = 1) =>
            AmbientSwarmProfile.Defaults.Single(p => p.Kind == kind) with { SwarmCount = count };
        static bool Place(AmbientSwarmProfile profile, Vector3 observer, out Vector3 position)
        {
            position = Vector3.up * (1000f + Mathf.Max(2f, profile.HeightMeters));
            return true;
        }
        static WildlifeLandingTarget Site(ulong id, Vector3 position) =>
            new(id, position, position.normalized, 1f, WildlifeLandingUse.Bird | WildlifeLandingUse.Bee | WildlifeLandingUse.Butterfly);

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void RestHasDurationAndThreatInterruptsEveryActivity(int fps)
        {
            var brain = new WildlifeBrain();
            brain.Tick(1f / fps, false, true, false, 2f);
            Assert.AreEqual(WildlifeActivity.Visit, brain.Activity);
            brain.Tick(1f / fps, true, true, false, 2f);
            Assert.AreEqual(WildlifeActivity.Escape, brain.Activity);
            brain.Tick(1f / fps, false, true, true, 2f);
            Assert.AreEqual(WildlifeActivity.Rest, brain.Activity);
            float elapsed = 0f;
            while (!brain.VisitComplete && elapsed < 3f)
            { brain.Tick(1f / fps, false, true, true, 2f); elapsed += 1f / fps; }
            Assert.That(elapsed, Is.InRange(1.99f, 2f + 2f / fps));
            Assert.AreEqual(WildlifeActivity.Travel, brain.Activity);
            brain.Tick(1f / fps, false, true, true, 2f);
            brain.Tick(1f / fps, true, true, true, 2f);
            Assert.AreEqual(WildlifeActivity.Escape, brain.Activity);
        }

        [Test]
        public void BirdsAndPollinatorsShareClaimsAndMovedTargetsReleaseThem()
        {
            var sites = new WildlifeLandingTargets();
            Vector3 position = Vector3.up * 1001f;
            sites.Publish(new[] { Site(5, position) });
            var bird = new WildlifeLandingReservation(new EntityId(EntityId.AmbientWildlifeOwner, 1));
            var bee = new WildlifeLandingReservation(new EntityId(EntityId.AmbientWildlifeOwner, 2));
            Assert.IsTrue(bird.TryAcquire(sites, position, 5f, 0.35f, WildlifeLandingUse.Bird));
            Assert.IsFalse(bee.TryAcquire(sites, position, 5f, 0.1f, WildlifeLandingUse.Bee));
            sites.Publish(new[] { Site(5, position + Vector3.right) });
            Assert.IsFalse(bird.Refresh());
            Assert.IsTrue(bee.TryAcquire(sites, position, 5f, 0.1f, WildlifeLandingUse.Bee));
            sites.Publish(Array.Empty<WildlifeLandingTarget>());
            Assert.IsFalse(bee.Refresh());
        }

        [TestCase(AmbientSwarmKind.Bees)]
        [TestCase(AmbientSwarmKind.Butterflies)]
        public void PollinatorsLandEscapeDuringFlightAndReleaseClaimsOnClear(AmbientSwarmKind kind)
        {
            var sites = new WildlifeLandingTargets();
            var threats = new ThreatRegistry();
            Vector3 flower = Vector3.up * 1001f;
            sites.Publish(new[] { Site(5, flower) });
            var simulation = new AmbientWildlifeSimulation(new Surface(), threats, sites, Vector3.zero, 990f,
                Place, new[] { Profile(kind) });
            Vector3 observer = Vector3.up * 1001f;
            bool landed = false;
            for (int i = 0; i < 120; i++)
            {
                simulation.Tick(observer, 1f, 100, 1f / 30f);
                landed |= simulation.Poses.Any(p => p.Resting);
            }
            Assert.IsTrue(landed);
            var pose = simulation.Poses.Single();
            Assert.AreEqual(EntityId.AmbientWildlifeOwner, pose.Id.Owner);
            EntityId predator = new(EntityId.HostOwner, 77);
            threats.Report(predator, pose.Position + Vector3.right, CreatureFaction.Predator);
            simulation.Tick(observer, 1f, 100, 0.1f);
            var escaped = simulation.Poses.Single();
            Assert.IsTrue(escaped.Fleeing);
            Assert.Greater(Vector3.Distance(escaped.Position, pose.Position + Vector3.right), 1f);
            simulation.Tick(observer, 1f, 100, 0.1f);
            Assert.IsTrue(simulation.Poses.Single().Fleeing, "An airborne insect must keep escaping.");
            simulation.Clear();
            Assert.IsEmpty(simulation.Poses);
            Assert.IsTrue(sites.TryClaim(5, predator));
        }

        [Test]
        public void FlowerRemovalAndNightReleaseBeeReservations()
        {
            var simulation = new AmbientWildlifeSimulation(new Surface(), null, null, Vector3.zero, 990f,
                Place, new[] { Profile(AmbientSwarmKind.Bees) });
            simulation.PublishFlowers(new[] { Site(5, Vector3.up * 1001f) });
            simulation.Tick(Vector3.up * 1001f, 1f, 100, 0.1f);
            Assert.AreEqual(1, simulation.Poses.Count);
            simulation.PublishFlowers(Array.Empty<WildlifeLandingTarget>());
            for (int i = 0; i < 30; i++) simulation.Tick(Vector3.up * 1001f, -1f, 100, 0.1f);
            Assert.IsEmpty(simulation.Poses);
        }

        [Test]
        public void HeadlessBirdsKeepGroupSizesLandAndDepartWithoutRendererObjects()
        {
            var simulation = new AmbientWildlifeSimulation(new Surface(), null, null, Vector3.zero, 990f,
                Place, new[] { Profile(AmbientSwarmKind.Birds) });
            bool landed = false, departed = false;
            EntityId landedId = default;
            for (int i = 0; i < 30 * 180; i++)
            {
                simulation.Tick(Vector3.up * 1001f, 1f, 100, 1f / 30f);
                Assert.That(simulation.Poses.Count, Is.EqualTo(1).Or.EqualTo(7).Or.EqualTo(24));
                foreach (var bird in simulation.Poses)
                {
                    Assert.GreaterOrEqual(bird.Position.magnitude, 1000.17f);
                    if (bird.Resting) { landed = true; landedId = bird.Id; }
                    if (!landedId.IsNone && bird.Id == landedId && !bird.Resting) departed = true;
                }
            }
            Assert.IsTrue(landed, "At least one bird must reach ground contact.");
            Assert.IsTrue(departed, "A rested bird must return to flight.");
            simulation.Clear();
            Assert.IsEmpty(simulation.Poses);
        }

        [Test]
        public void RaisedLakeSetsFlockCruiseFloorAndPreventsGroundLanding()
        {
            var water = new FixedBodyWaterQuery(Vector3.zero, Vector3.up, 1080f, -1f);
            var floor = new CharacterWaterFloor(water, Vector3.zero);
            var landing = new BirdLandingGround(new Surface(), Vector3.zero, 990f, floor);
            Assert.IsFalse(landing.TryFind(Vector3.up * 1100f, .35f, out _));
            var simulation = new AmbientWildlifeSimulation(new Surface(), null, null, Vector3.zero, 990f,
                Place, new[] { Profile(AmbientSwarmKind.Birds) }, floor);
            for (int i = 0; i < 30 * 20; i++)
            {
                simulation.Tick(Vector3.up * 1081f, 1f, 100, 1f / 30f);
                foreach (var bird in simulation.Poses)
                {
                    Assert.GreaterOrEqual(bird.Position.magnitude, 1080.17f);
                    Assert.IsFalse(bird.Resting, "These land birds cannot rest on the lake surface.");
                }
            }
            Assert.IsNotEmpty(simulation.Poses);
            Assert.That(simulation.Poses.Max(bird => bird.Position.magnitude), Is.GreaterThan(1120f),
                "The flock must climb toward its cruise height above the raised lake, not hug the water.");
            simulation.Clear();
        }
    }
}
