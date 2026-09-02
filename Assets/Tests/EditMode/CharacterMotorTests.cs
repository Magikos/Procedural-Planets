using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // The character MVP's only cheaply-automatable surface: the pure motor, the composable capabilities, and
    // the actor-agnostic driver. Grounded locomotion behaviour (grass, camera, walking a real planet) is
    // inherently visual/play-mode; these lock the math + contracts an executor might silently break.
    public sealed class CharacterMotorTests
    {
        const float Tol = 1e-4f;

        static readonly Vector3 Up = Vector3.up;
        static readonly Vector3 Fwd = Vector3.forward;

        // --- CharacterMotor.TryStep (a)-(g) ---

        [Test]
        public void Step_a_LiesInTangentPlane()
        {
            Assert.IsTrue(CharacterMotor.TryStep(Up, new Vector3(0, 5, 0), new Vector2(0, 1), Fwd, 2f, 0.5f, out Vector3 next));
            Vector3 delta = next - new Vector3(0, 5, 0);
            Assert.AreEqual(0f, Vector3.Dot(delta.normalized, Up), Tol, "step must be perpendicular to up");
        }

        [Test]
        public void Step_b_ZeroInputHoldsPosition()
        {
            Vector3 pos = new Vector3(1, 2, 3);
            Assert.IsTrue(CharacterMotor.TryStep(Up, pos, Vector2.zero, Fwd, 5f, 1f, out Vector3 next));
            Assert.AreEqual(pos, next);
        }

        [Test]
        public void Step_c_ForwardThenBackwardReturnsNearStart()
        {
            Vector3 start = new Vector3(0, 5, 0);
            Assert.IsTrue(CharacterMotor.TryStep(Up, start, new Vector2(0, 1), Fwd, 3f, 0.5f, out Vector3 mid));
            Assert.IsTrue(CharacterMotor.TryStep(Up, mid, new Vector2(0, -1), Fwd, 3f, 0.5f, out Vector3 back));
            Assert.Less((back - start).magnitude, Tol);
        }

        [Test]
        public void Step_d_PerpendicularInputMovesAlongRight()
        {
            // up=+Y, fwd=+Z => right = cross(up,fwd) = +X; move.x drives +X.
            Vector3 pos = new Vector3(0, 5, 0);
            Assert.IsTrue(CharacterMotor.TryStep(Up, pos, new Vector2(1, 0), Fwd, 4f, 1f, out Vector3 next));
            Assert.Greater(next.x, pos.x + 1f);
            Assert.AreEqual(0f, next.z, Tol);
        }

        [Test]
        public void Step_e_CameraAlongUpDoesNotNaN()
        {
            Assert.IsTrue(CharacterMotor.TryStep(Up, new Vector3(0, 5, 0), new Vector2(0, 1), Up, 2f, 0.5f, out Vector3 next));
            Assert.IsTrue(CharacterMath.IsFinite(next));
        }

        [Test]
        public void Step_f_FlatWorldPortability_YConstant()
        {
            // up=(0,1,0): the same motor produces planar motion (y unchanged) with no planet involved.
            Vector3 pos = new Vector3(3, 7, -2);
            Assert.IsTrue(CharacterMotor.TryStep(Up, pos, new Vector2(0.5f, 1f), Fwd, 6f, 0.25f, out Vector3 next));
            Assert.AreEqual(pos.y, next.y, Tol);
        }

        [Test]
        public void Step_g_DegenerateUpFailsCleanly()
        {
            Vector3 pos = new Vector3(1, 2, 3);
            Assert.IsFalse(CharacterMotor.TryStep(Vector3.zero, pos, new Vector2(0, 1), Fwd, 2f, 1f, out Vector3 n1));
            Assert.AreEqual(pos, n1);

            Vector3 nonFinite = new Vector3(float.NaN, 0, 0);
            Assert.IsFalse(CharacterMotor.TryStep(nonFinite, pos, new Vector2(0, 1), Fwd, 2f, 1f, out Vector3 n2));
            Assert.AreEqual(pos, n2);
        }

        // --- RadialGravityProvider ---

        [Test]
        public void RadialGravity_PointsToCenter_NonZeroMagnitude()
        {
            var g = new RadialGravityProvider(Vector3.zero, 9.81f);
            Assert.IsTrue(g.TryGetGravity(new Vector3(0, 10, 0), out Vector3 accel));
            Assert.AreEqual(9.81f, accel.magnitude, 1e-3f);
            Assert.Greater(Vector3.Dot(accel.normalized, Vector3.down), 0.9999f, "accel must point toward center");
        }

        [Test]
        public void RadialGravity_AtCenterFailsCleanly()
        {
            var g = new RadialGravityProvider(Vector3.zero, 9.81f);
            Assert.IsFalse(g.TryGetGravity(Vector3.zero, out Vector3 accel));
            Assert.AreEqual(Vector3.zero, accel);
        }

        [Test]
        public void RadialGravity_ZeroMagnitudeFails()
        {
            var g = new RadialGravityProvider(Vector3.zero, 0f);
            Assert.IsFalse(g.TryGetGravity(new Vector3(0, 10, 0), out _));
        }

        // --- PlanetSurfaceGrounding ---

        [Test]
        public void PlanetGrounding_PlacesAtRadiusPlusFootOffset()
        {
            var grounding = new PlanetSurfaceGrounding(new FixedRadiusSampler(100f), Vector3.zero);
            Assert.IsTrue(grounding.TryGround(new Vector3(0, 50, 0), Vector3.down, 1f, out GroundResult r));
            Assert.AreEqual(101f, r.Position.magnitude, 1e-3f);
            Assert.Greater(Vector3.Dot(r.Normal, Vector3.up), 0.9999f, "planet normal is radial up");
        }

        // --- The water floor is per body, not one global sea radius (the "lake under the lake" defect) ---

        [Test]
        public void PlanetGrounding_InsideRaisedLake_StandsOnThatLakeSurface()
        {
            const float terrain = 4990f;   // lake bed
            const float lake = 5041.26f;   // that body's solved surface, 41 m above the global sea radius
            var water = new FixedBodyWaterQuery(Vector3.zero, Vector3.up, lake);
            var grounding = new PlanetSurfaceGrounding(
                new FixedRadiusSampler(terrain), Vector3.zero, new CharacterWaterFloor(water, Vector3.zero));

            Assert.IsTrue(grounding.TryGround(new Vector3(0, 5000f, 0), Vector3.down, 1f, out GroundResult r));
            Assert.AreEqual(lake + 1f, r.Position.magnitude, 1e-2f, "grounded on the lake surface, not its bed");
        }

        [Test]
        public void PlanetGrounding_OutsideAnyBody_GroundsOnTerrain()
        {
            const float terrain = 4990f;
            var water = new FixedBodyWaterQuery(Vector3.zero, Vector3.up, 5041.26f);
            var grounding = new PlanetSurfaceGrounding(
                new FixedRadiusSampler(terrain), Vector3.zero, new CharacterWaterFloor(water, Vector3.zero));

            Assert.IsTrue(grounding.TryGround(new Vector3(5000f, 0, 0), Vector3.left, 1f, out GroundResult r));
            Assert.AreEqual(terrain + 1f, r.Position.magnitude, 1e-2f, "dry land is never lifted to a lake");
        }

        // --- SurfaceCharacterController driven by fakes (no planet) ---

        static SurfaceCharacterController FlatDriver(float foot)
        {
            var seed = new CharacterPose(new Vector3(0, foot, 0), Up, Fwd);
            return new SurfaceCharacterController(
                new ConstantGravityProvider(new Vector3(0, -9.81f, 0)),
                new PlanarGroundingProvider(), foot, seed);
        }

        [Test]
        public void Driver_FlatWorld_WalksAndStaysGrounded()
        {
            var d = FlatDriver(1f);
            CharacterPose p = d.Tick(new Vector2(0, 1), Fwd, 2f, 1f);
            Assert.Greater(p.Position.z, 1.5f, "walked forward ~2m");
            Assert.AreEqual(1f, p.Position.y, Tol, "grounded at footOffset");
            Assert.Greater(Vector3.Dot(p.Up, Up), 0.9999f);
        }

        [Test]
        public void Driver_FailingGravityHoldsSeedPose()
        {
            var seed = new CharacterPose(new Vector3(0, 1, 0), Up, Fwd);
            var d = new SurfaceCharacterController(
                new ConstantGravityProvider(Vector3.zero), new PlanarGroundingProvider(), 1f, seed);
            CharacterPose p = d.Tick(new Vector2(0, 1), Fwd, 5f, 1f);
            Assert.AreEqual(seed.Position, p.Position, "no gravity => hold the seeded pose, no NaN");
            Assert.IsTrue(CharacterMath.IsFinite(p.Position));
        }

        [Test]
        public void Driver_ResetPoseAdoptsNewCenter()
        {
            var d = FlatDriver(1f);
            var seed2 = new CharacterPose(new Vector3(10, 2, -5), Up, Fwd);
            d.ResetPose(seed2);
            Assert.AreEqual(seed2.Position, d.Pose.Position);
            Assert.AreEqual(seed2.Up, d.Pose.Up);
        }

        // --- C15: orientation comes from the FINAL grounded frame (the sphere lean guard) ---

        [Test]
        public void Driver_Sphere_UpMatchesSettledRadial_NotPreStepUp()
        {
            const float radius = 100f;
            const float foot = 0.5f;
            var center = Vector3.zero;
            var seed = new CharacterPose(new Vector3(0, radius + foot, 0), Up, Fwd);
            var d = new SurfaceCharacterController(
                new RadialGravityProvider(center, 9.81f),
                new PlanetSurfaceGrounding(new FixedRadiusSampler(radius), center),
                foot, seed);

            // High speed + small radius => the walked point tilts noticeably off the pre-step up (0,1,0).
            CharacterPose p = d.Tick(new Vector2(0, 1), Fwd, 40f, 1f);

            Assert.AreEqual(radius + foot, p.Position.magnitude, 1e-2f, "grounded at radius+foot");
            Assert.Greater(p.Position.z, 0f, "walked +z along the sphere");
            // pose.Up must equal the radial at the SETTLED position (final frame), not the pre-step (0,1,0).
            Assert.Greater(Vector3.Dot(p.Up, p.Position.normalized), 0.9999f, "up is the final-frame radial");
            Assert.Less(Vector3.Dot(p.Up, Up), 0.9999f, "and it actually moved off the pre-step up (lean guard)");
        }
    }
}
