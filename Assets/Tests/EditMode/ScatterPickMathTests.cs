using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // ScatterPickMath.NearestAlongRay picks the instance the player looks at from the draw buckets' world
    // positions (scatter has no colliders). Pure geometry: nearest point ahead of the ray within reach and
    // within a perpendicular tolerance of the ray line.
    public sealed class ScatterPickMathTests
    {
        static Ray Fwd => new Ray(Vector3.zero, Vector3.forward);

        [Test]
        public void RayStraightAtOne_ReturnsIt()
        {
            var pts = new List<Vector3> { new Vector3(0, 0, 5) };
            int i = ScatterPickMath.NearestAlongRay(Fwd, pts, 10f, 1f, out float t);
            Assert.AreEqual(0, i);
            Assert.AreEqual(5f, t, 1e-4f);
        }

        [Test]
        public void PerpendicularBeyondTolerance_ReturnsNone()
        {
            var pts = new List<Vector3> { new Vector3(3, 0, 5) }; // 3 m off the ray, tolerance 1
            Assert.AreEqual(-1, ScatterPickMath.NearestAlongRay(Fwd, pts, 10f, 1f, out _));
        }

        [Test]
        public void WithinPerpendicularTolerance_Accepted()
        {
            var pts = new List<Vector3> { new Vector3(0.5f, 0, 5) }; // 0.5 m off, tolerance 1
            Assert.AreEqual(0, ScatterPickMath.NearestAlongRay(Fwd, pts, 10f, 1f, out _));
        }

        [Test]
        public void TwoAlongRay_ReturnsNearer()
        {
            var pts = new List<Vector3> { new Vector3(0, 0, 8), new Vector3(0, 0, 3) };
            int i = ScatterPickMath.NearestAlongRay(Fwd, pts, 10f, 1f, out float t);
            Assert.AreEqual(1, i, "the closer (t=3) wins");
            Assert.AreEqual(3f, t, 1e-4f);
        }

        [Test]
        public void BehindOrigin_ReturnsNone()
        {
            var pts = new List<Vector3> { new Vector3(0, 0, -5) };
            Assert.AreEqual(-1, ScatterPickMath.NearestAlongRay(Fwd, pts, 10f, 1f, out _));
        }

        [Test]
        public void BeyondReach_ReturnsNone()
        {
            var pts = new List<Vector3> { new Vector3(0, 0, 20) };
            Assert.AreEqual(-1, ScatterPickMath.NearestAlongRay(Fwd, pts, 10f, 1f, out _));
        }

        [Test]
        public void Empty_ReturnsNone()
        {
            Assert.AreEqual(-1, ScatterPickMath.NearestAlongRay(Fwd, new List<Vector3>(), 10f, 1f, out _));
        }
    }
}
