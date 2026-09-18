using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorJumpResponsivenessTests
    {
        sealed class World : IGravityProvider, IGroundingProvider
        {
            public Vector3 Up = Vector3.up;
            public float Height;
            public bool TryGetGravity(Vector3 position, out Vector3 acceleration)
            { acceleration = -Up * 9.81f; return true; }
            public bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult result)
            { result = new GroundResult(Vector3.ProjectOnPlane(position, Up) + Up * (Height + offset), Up); return true; }
        }
        static SurfaceCharacterController Motor(World world) => new(world, world, 0f,
            new CharacterPose(world.Up * world.Height, world.Up, Vector3.forward));

        [TestCase(2f, 6f)]
        [TestCase(6f, 2f)]
        public void SprintChangesCannotChangeAirborneSpeed(float launchSpeed, float changedSpeed)
        {
            var world = new World(); var motor = Motor(world);
            motor.Tick(Vector2.up, Vector3.forward, launchSpeed, .02f, true);
            float before = motor.Pose.Position.z;
            motor.Tick(Vector2.up, Vector3.forward, changedSpeed, .02f);
            Assert.AreEqual(launchSpeed * .02f, motor.Pose.Position.z - before, .00001f);
        }

        [TestCase(.04f, true)]
        [TestCase(.14f, false)]
        public void CoyoteJumpHasABoundedWindow(float delay, bool expected)
        {
            var world = new World(); var motor = Motor(world);
            world.Height = -10f;
            motor.Tick(Vector2.up, Vector3.forward, 2f, .02f);
            motor.Tick(Vector2.up, Vector3.forward, 6f, delay, true);
            Assert.AreEqual(expected, motor.Jumping);
            if (expected)
            {
                float height = motor.Pose.Position.y;
                motor.Tick(Vector2.up, Vector3.forward, 6f, .02f, false);
                motor.Tick(Vector2.up, Vector3.forward, 6f, .02f, true);
                Assert.Less(motor.Pose.Position.y - height, .23f, "A second press cannot reset launch velocity.");
            }
        }

        [Test]
        public void BufferedPressLaunchesAtLandingAndHeldInputDoesNotRepeat()
        {
            var world = new World(); var motor = Motor(world);
            motor.Tick(Vector2.zero, Vector3.forward, 0f, .02f, true);
            for (int i = 0; i < 40; i++) motor.Tick(Vector2.zero, Vector3.forward, 0f, .02f);
            while (motor.Pose.Position.y > .18f) motor.Tick(Vector2.zero, Vector3.forward, 0f, .02f);
            motor.Tick(Vector2.zero, Vector3.forward, 0f, .02f, true);
            bool roseAgain = false;
            for (int i = 0; i < 100; i++)
            {
                motor.Tick(Vector2.zero, Vector3.forward, 0f, .02f, true);
                roseAgain |= motor.Pose.Position.y > .5f;
            }
            Assert.IsTrue(roseAgain);
            Assert.IsTrue(motor.Grounded);
            Assert.IsFalse(motor.Jumping);
        }

        [Test]
        public void ReleasedMovementRetainsMomentumAndStandingJumpCannotAccelerate()
        {
            var world = new World(); var motor = Motor(world);
            motor.Tick(Vector2.up, Vector3.forward, 3f, .02f, true);
            float before = motor.Pose.Position.z;
            motor.Tick(Vector2.zero, Vector3.forward, 0f, .02f);
            Assert.AreEqual(.06f, motor.Pose.Position.z - before, .00001f);
            motor.ResetPose(new CharacterPose(Vector3.zero, Vector3.up, Vector3.forward));
            motor.Tick(Vector2.zero, Vector3.forward, 0f, .02f, true);
            motor.Tick(Vector2.up, Vector3.forward, 6f, .02f);
            Assert.AreEqual(0f, motor.Pose.Position.z, .00001f);
        }

        [Test]
        public void InvalidIntentAndGravityCannotPoisonLaterMotion()
        {
            var world = new World(); var motor = Motor(world);
            motor.Tick(new Vector2(float.NaN, 1f), Vector3.forward, 2f, .02f, true);
            Assert.AreEqual(Vector3.zero, motor.Pose.Position);
            world.Up = Vector3.zero;
            motor.Tick(Vector2.up, Vector3.forward, 2f, .02f, true);
            Assert.AreEqual(Vector3.zero, motor.Pose.Position);
            world.Up = Vector3.up;
            motor.Tick(Vector2.up, Vector3.forward, 2f, .02f, true);
            Assert.IsTrue(motor.Jumping);
            Assert.IsTrue(CharacterMath.IsFinite(motor.Pose.Position));
            Assert.AreEqual(.04f, motor.Pose.Position.z, .00001f);
        }

        [Test]
        public void SidewaysGravityPreservesTakeoffMomentum()
        {
            var world = new World { Up = Vector3.right }; var motor = Motor(world);
            motor.Tick(Vector2.up, Vector3.forward, 3f, .02f, true);
            float before = motor.Pose.Position.z;
            motor.Tick(Vector2.zero, Vector3.forward, 0f, .02f);
            Assert.AreEqual(.06f, motor.Pose.Position.z - before, .00001f);
            Assert.Greater(motor.Pose.Position.x, 0f);
            Assert.AreEqual(0f, motor.Pose.Position.y, .00001f);
        }

        [Test]
        public void ResetClearsBufferedInputAndAirborneSpeed()
        {
            var world = new World(); var motor = Motor(world);
            motor.Tick(Vector2.up, Vector3.forward, 6f, .02f, true);
            motor.Tick(Vector2.up, Vector3.forward, 6f, .02f);
            motor.Tick(Vector2.up, Vector3.forward, 6f, .02f, true);
            motor.ResetPose(new CharacterPose(Vector3.zero, Vector3.up, Vector3.forward));
            motor.Tick(Vector2.up, Vector3.forward, 2f, .02f);
            Assert.IsTrue(motor.Grounded);
            Assert.AreEqual(.04f, motor.Pose.Position.z, .00001f);
        }
    }
}
