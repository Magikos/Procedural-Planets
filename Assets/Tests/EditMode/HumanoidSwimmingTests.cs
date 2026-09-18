using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class HumanoidSwimmingTests
    {
        sealed class Pool : ISwimmingProvider, IGroundingProvider, IGravityProvider
        {
            public Vector3 Up = Vector3.up;
            public bool Wet = true;
            public float BodyDepth = 5f;
            public bool TryGetDepth(Vector3 p, out float depth, out float body)
            { depth = 10f - Vector3.Dot(p, Up); body = BodyDepth; return Wet; }
            public bool TryGround(Vector3 p, Vector3 down, float offset, out GroundResult hit)
            { hit = new GroundResult(p + Up * (10f - BodyDepth + offset - Vector3.Dot(p, Up)), Up); return true; }
            public bool TryGetGravity(Vector3 p, out Vector3 acceleration)
            { acceleration = -Up * 9.81f; return true; }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DiveHoldsDepthThenContinuousAscentStopsAtSurface(bool sideways)
        {
            var pool = new Pool { Up = sideways ? Vector3.right : Vector3.up };
            var motor = new SurfaceCharacterController(pool, pool, 0f,
                new CharacterPose(pool.Up * 8.75f, pool.Up, Vector3.forward), pool,
                new SurfaceSwimProfile(1.25f, 1.45f, .2f, true));
            for (int i = 0; i < 40; i++) motor.Tick(Vector2.up, Vector3.forward, 1f, .02f, dive: true);
            Assert.IsTrue(motor.Diving);
            Assert.Greater(motor.WaterDepth, 3f);
            float depth = motor.WaterDepth;
            for (int i = 0; i < 100; i++) motor.Tick(Vector2.zero, Vector3.forward, 0f, .02f);
            Assert.AreEqual(depth, motor.WaterDepth, .001f);
            for (int i = 0; i < 150; i++) motor.Tick(Vector2.zero, Vector3.forward, 0f, .02f, jump: true);
            Assert.IsTrue(motor.Swimming); Assert.IsFalse(motor.Diving);
            Assert.AreEqual(1.25f, motor.WaterDepth, .001f);
            Assert.Greater(motor.Pose.Position.z, .5f);
            for (int i = 0; i < 200; i++) motor.Tick(Vector2.zero, Vector3.forward, 0f, .02f, dive: true);
            Assert.AreEqual(5f, motor.WaterDepth, .001f, "The root must not descend through the floor.");
            pool.Wet = false;
            motor.Tick(Vector2.zero, Vector3.forward, 0f, .02f);
            Assert.IsFalse(motor.Swimming); Assert.IsFalse(motor.Diving); Assert.IsTrue(motor.Grounded);
        }

        [Test]
        public void ShallowWaterResistanceScalesWithDepthAndPreservesDefaultSpeed()
        {
            foreach (Vector3 up in new[] { Vector3.up, Vector3.right })
            foreach (float depth in new[] { .25f, 1f })
            {
                var pool = new Pool { Up = up, BodyDepth = depth };
                var seed = new CharacterPose(up * (10f - depth), up, Vector3.forward);
                var wader = new SurfaceCharacterController(pool, pool, 0f, seed, pool,
                    new SurfaceSwimProfile(1.25f, 1.45f, .2f, wadingSpeedMultiplier: .5f));
                var animal = new SurfaceCharacterController(pool, pool, 0f, seed, pool,
                    new SurfaceSwimProfile(1.25f, 1.45f, .2f));
                wader.Tick(Vector2.up, Vector3.forward, 2f, .1f);
                animal.Tick(Vector2.up, Vector3.forward, 2f, .1f);
                Assert.IsTrue(wader.Grounded);
                Assert.IsFalse(wader.Swimming);
                Assert.AreEqual(depth, wader.WaterDepth, .001f);
                Assert.AreEqual(.2f * Mathf.Lerp(1f, .5f, depth / 1.45f), wader.Pose.Position.z, .001f);
                Assert.AreEqual(.2f, animal.Pose.Position.z, .001f);
                pool.Wet = false;
                float before = wader.Pose.Position.z;
                wader.Tick(Vector2.up, Vector3.forward, 2f, .1f);
                Assert.AreEqual(.2f, wader.Pose.Position.z - before, .001f);
                Assert.AreEqual(0f, wader.WaterDepth);
            }
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(1.1f)]
        [TestCase(float.NaN)]
        public void WadingProfileRejectsInvalidResistance(float multiplier)
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new SurfaceSwimProfile(1.25f, 1.45f, .2f, wadingSpeedMultiplier: multiplier));
        }

        [Test]
        public void DefaultBuoyancyStillReturnsAnimalsToSurface()
        {
            var pool = new Pool();
            var motor = new SurfaceCharacterController(pool, pool, 0f,
                new CharacterPose(Vector3.up * 8.75f, Vector3.up, Vector3.forward), pool,
                new SurfaceSwimProfile(1.25f, 1.45f, .2f));
            for (int i = 0; i < 40; i++) motor.Tick(Vector2.zero, Vector3.forward, 0f, .02f, dive: true);
            for (int i = 0; i < 200; i++) motor.Tick(Vector2.zero, Vector3.forward, 0f, .02f);
            Assert.AreEqual(1.25f, motor.WaterDepth, .001f);
            motor.ResetPose(new CharacterPose(Vector3.up * 8.75f, Vector3.up, Vector3.forward));
            Assert.IsFalse(motor.Diving); Assert.AreEqual(0f, motor.WaterDepth);
        }
    }
}
