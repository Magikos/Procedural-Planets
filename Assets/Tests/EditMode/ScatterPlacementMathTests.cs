using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // ScatterPlacementMath.TryPlace is the CPU/GPU-parity placement decision: pure, sampler-free, so the
    // SP3 compute kernel can mirror it. These tests lock the gate order and the accept/reject boundaries
    // (altitude, water clearance, slope, probability) and confirm the transform outputs are deterministic
    // functions of the slot seed.
    public sealed class ScatterPlacementMathTests
    {
        // Flat ground, fully accepted when densityKeep/weight allow: slopeCos = 1 with this ramp keeps 1.
        static PlacementRules FlatAcceptRules() => new PlacementRules
        {
            Weight = 1f,
            MaxSlopeCos = 0f,    // cos(maxSlope + fade)  (steepest allowed)
            MinSlopeCos = 0.5f,  // cos(maxSlope)
            HasMinAltitude = false, MinAltitude = 0f,
            HasMaxAltitude = false, MaxAltitude = 0f,
            MinWaterClearance = 0f,
            ScaleRange = new Vector2(0.8f, 1.5f),
            RandomYaw = true,
        };

        static bool Place(uint seed, PlacementRules rules, float altitude, float slopeCos, float densityKeep,
            bool hasOcean, out Vector3 pos, out Quaternion rot, out float scale)
            => ScatterPlacementMath.TryPlace(seed, Vector3.up, 10f, altitude, slopeCos, densityKeep, hasOcean,
                in rules, out pos, out rot, out scale);

        [Test]
        public void Places_WhenAcceptIsOne()
        {
            PlacementRules r = FlatAcceptRules();
            for (uint s = 1; s < 200; s++)
                Assert.IsTrue(Place(s, r, 100f, 1f, 1f, false, out _, out _, out _),
                    $"seed {s} should always place when accept == 1");
        }

        [Test]
        public void Rejects_WhenDensityKeepZero()
        {
            PlacementRules r = FlatAcceptRules();
            for (uint s = 0; s < 200; s++)
                Assert.IsFalse(Place(s, r, 100f, 1f, 0f, false, out _, out _, out _));
        }

        [Test]
        public void Rejects_WhenWeightZero()
        {
            PlacementRules r = FlatAcceptRules();
            r.Weight = 0f;
            for (uint s = 0; s < 200; s++)
                Assert.IsFalse(Place(s, r, 100f, 1f, 1f, false, out _, out _, out _));
        }

        [Test]
        public void Rejects_BelowMinAltitude()
        {
            PlacementRules r = FlatAcceptRules();
            r.HasMinAltitude = true; r.MinAltitude = 50f;
            Assert.IsFalse(Place(1u, r, 10f, 1f, 1f, false, out _, out _, out _));
            Assert.IsTrue(Place(1u, r, 60f, 1f, 1f, false, out _, out _, out _));
        }

        [Test]
        public void Rejects_AboveMaxAltitude()
        {
            PlacementRules r = FlatAcceptRules();
            r.HasMaxAltitude = true; r.MaxAltitude = 50f;
            Assert.IsFalse(Place(1u, r, 100f, 1f, 1f, false, out _, out _, out _));
            Assert.IsTrue(Place(1u, r, 40f, 1f, 1f, false, out _, out _, out _));
        }

        [Test]
        public void WaterClearance_OnlyGatesWhenOceanPresent()
        {
            PlacementRules r = FlatAcceptRules();
            r.MinWaterClearance = 2f;
            // Below clearance with an ocean -> rejected; identical inputs without an ocean -> placed.
            Assert.IsFalse(Place(1u, r, 1f, 1f, 1f, true, out _, out _, out _));
            Assert.IsTrue(Place(1u, r, 1f, 1f, 1f, false, out _, out _, out _));
            // Above clearance with an ocean -> placed.
            Assert.IsTrue(Place(1u, r, 5f, 1f, 1f, true, out _, out _, out _));
        }

        [Test]
        public void Rejects_SlopeBelowCutoff()
        {
            PlacementRules r = FlatAcceptRules();
            r.MaxSlopeCos = 0.5f; r.MinSlopeCos = 0.7f;
            Assert.IsFalse(Place(1u, r, 100f, 0.3f, 1f, false, out _, out _, out _), "steep slope rejected");
            Assert.IsTrue(Place(1u, r, 100f, 0.9f, 1f, false, out _, out _, out _), "shallow slope kept");
        }

        [Test]
        public void IsDeterministic()
        {
            PlacementRules r = FlatAcceptRules();
            bool a = Place(12345u, r, 100f, 1f, 1f, false, out Vector3 pa, out Quaternion qa, out float sa);
            bool b = Place(12345u, r, 100f, 1f, 1f, false, out Vector3 pb, out Quaternion qb, out float sb);
            Assert.AreEqual(a, b);
            Assert.AreEqual(pa.x, pb.x); Assert.AreEqual(pa.y, pb.y); Assert.AreEqual(pa.z, pb.z);
            Assert.AreEqual(qa.x, qb.x); Assert.AreEqual(qa.y, qb.y); Assert.AreEqual(qa.z, qb.z); Assert.AreEqual(qa.w, qb.w);
            Assert.AreEqual(sa, sb);
        }

        [Test]
        public void Position_IsDirectionTimesRadius()
        {
            PlacementRules r = FlatAcceptRules();
            Place(1u, r, 100f, 1f, 1f, false, out Vector3 pos, out _, out _);
            Assert.AreEqual(0f, pos.x);
            Assert.AreEqual(10f, pos.y); // dir = up, radius = 10
            Assert.AreEqual(0f, pos.z);
        }

        [Test]
        public void Scale_StaysWithinRange()
        {
            PlacementRules r = FlatAcceptRules();
            for (uint s = 1; s < 500; s++)
            {
                Place(s, r, 100f, 1f, 1f, false, out _, out _, out float scale);
                Assert.GreaterOrEqual(scale, r.ScaleRange.x);
                Assert.LessOrEqual(scale, r.ScaleRange.y);
            }
        }

        [Test]
        public void NoRandomYaw_OnUpFacingSurface_IsIdentityRotation()
        {
            PlacementRules r = FlatAcceptRules();
            r.RandomYaw = false;
            Place(1u, r, 100f, 1f, 1f, false, out _, out Quaternion rot, out _);
            // dir = up, so FromToRotation(up, up) is identity and there is no yaw.
            Assert.That(Quaternion.Angle(rot, Quaternion.identity), Is.LessThan(0.01f));
        }
    }
}
