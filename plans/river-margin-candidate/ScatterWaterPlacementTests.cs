using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ScatterWaterPlacementTests
    {
        // Fixture values exercise boundaries. They are not proposed authoring defaults.
        static ScatterWaterRules Rules(bool shelter = true) => new ScatterWaterRules
        {
            MinDepthMeters = 0.2f,
            MaxDepthMeters = 2f,
            MaxSpeedMetersPerSecond = 0.3f,
            RequireShelter = shelter,
        };

        static WaterSample Water(float depth = 1f, float speed = 0.1f, bool ocean = false, ushort body = 1)
            => new WaterSample(Vector3.up * 100f, Vector3.up, depth, depth, body, ocean,
                Vector3.right * speed);

        [TestCase(0f, false)]
        [TestCase(-1f, false)]
        [TestCase(0.19f, false)]
        [TestCase(0.2f, true)]
        [TestCase(2f, true)]
        [TestCase(2.01f, false)]
        public void DepthBand_IsInclusiveButRejectsDryGround(float depth, bool expected)
        {
            Assert.AreEqual(expected, ScatterWaterPlacement.PassesHabitat(true, Water(depth), true, Rules()));
        }

        [Test]
        public void RequiresAValidFreshwaterBody()
        {
            Assert.IsFalse(ScatterWaterPlacement.PassesHabitat(false, Water(), true, Rules()));
            Assert.IsFalse(ScatterWaterPlacement.PassesHabitat(true, Water(body: 0), true, Rules()));
            Assert.IsFalse(ScatterWaterPlacement.PassesHabitat(true, Water(ocean: true), true, Rules()));
            Assert.IsTrue(ScatterWaterPlacement.PassesHabitat(true, Water(), true, Rules()));
        }

        [Test]
        public void SpeedLimitUsesWorldVelocityMagnitude()
        {
            Assert.IsTrue(ScatterWaterPlacement.PassesHabitat(true, Water(speed: 0.3f), true, Rules()));
            Assert.IsFalse(ScatterWaterPlacement.PassesHabitat(true, Water(speed: 0.31f), true, Rules()));
            Assert.IsFalse(ScatterWaterPlacement.PassesHabitat(true, Water(speed: -0.31f), true, Rules()));
        }

        [Test]
        public void ZeroSpeedDoesNotEstablishShelter()
        {
            Assert.IsFalse(ScatterWaterPlacement.PassesHabitat(true, Water(speed: 0f), false, Rules()));
            Assert.IsTrue(ScatterWaterPlacement.PassesHabitat(true, Water(speed: 0f), true, Rules()));
            Assert.IsTrue(ScatterWaterPlacement.PassesHabitat(true, Water(), false, Rules(shelter: false)));
        }

        [Test]
        public void InvalidDepthVelocityAndRulesReject()
        {
            Assert.IsFalse(ScatterWaterPlacement.PassesHabitat(true, Water(float.NaN), true, Rules()));
            Assert.IsFalse(ScatterWaterPlacement.PassesHabitat(true, Water(float.PositiveInfinity), true, Rules()));
            Assert.IsFalse(ScatterWaterPlacement.PassesHabitat(true, Water(speed: float.NaN), true, Rules()));
            Assert.IsFalse(ScatterWaterPlacement.PassesHabitat(true, Water(speed: float.PositiveInfinity), true, Rules()));
            var rules = Rules();
            rules.MaxDepthMeters = 0.1f;
            Assert.IsFalse(ScatterWaterPlacement.PassesHabitat(true, Water(), true, rules));
            rules = Rules();
            rules.MinDepthMeters = -1f;
            Assert.IsFalse(ScatterWaterPlacement.PassesHabitat(true, Water(), true, rules));
            rules = Rules();
            rules.MaxSpeedMetersPerSecond = float.NaN;
            Assert.IsFalse(ScatterWaterPlacement.PassesHabitat(true, Water(), true, rules));
        }

        [TestCase(1f)]
        [TestCase(2f)]
        [TestCase(0.5f)]
        public void FloatingAnchorKeepsBedAltitudeAndWorldOffset(float scale)
        {
            Assert.IsTrue(ScatterWaterPlacement.TryAnchor(true, 99f, 100f, scale,
                out float radius, out float altitude));
            Assert.AreEqual(-scale, altitude);
            Assert.That((radius - 100f) * scale,
                Is.EqualTo(ScatterPlacementMath.OnWaterSurfaceOffsetMeters).Within(0.00002f));
        }

        [Test]
        public void RaisedBasinUsesLocalWaterLevel()
        {
            Assert.IsTrue(ScatterWaterPlacement.TryAnchor(true, 139f, 140f, 1f,
                out float radius, out float altitude));
            Assert.AreEqual(-1f, altitude);
            Assert.That(radius, Is.EqualTo(140.15f).Within(0.00002f));
        }

        [TestCase(100f)]
        [TestCase(101f)]
        public void FloatingAnchorRejectsDryAndWaterlineGround(float ground)
        {
            Assert.IsFalse(ScatterWaterPlacement.TryAnchor(true, ground, 100f, 1f, out _, out _));
        }

        [Test]
        public void GroundedAnchorStaysOnBed()
        {
            Assert.IsTrue(ScatterWaterPlacement.TryAnchor(false, 99f, 100f, 2f,
                out float radius, out float altitude));
            Assert.AreEqual(99f, radius);
            Assert.AreEqual(-2f, altitude);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidScaleRejects(float scale)
        {
            Assert.IsFalse(ScatterWaterPlacement.TryAnchor(true, 99f, 100f, scale, out _, out _));
        }

        [Test]
        public void BedAltitudeReachesExistingAltitudeGate()
        {
            Assert.IsTrue(ScatterWaterPlacement.TryAnchor(true, 97f, 100f, 1f, out _, out float altitude));
            var rules = new PlacementRules { HasMinAltitude = true, MinAltitude = -2f };
            Assert.IsFalse(ScatterPlacementMath.PassesAltitudeWater(altitude, true, rules));
            Assert.IsTrue(ScatterPlacementMath.PassesAltitudeWater(0f, true, rules),
                "The previous zero-altitude input conceals this deep-bed rejection.");
        }
    }
}
