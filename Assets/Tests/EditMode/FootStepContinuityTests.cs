using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class FootStepContinuityTests
    {
        [Test]
        public void FullContactWithoutCorrectionPreservesTheAuthoredKnee()
        {
            var root = new GameObject("authored contact");
            try
            {
                var knee = new GameObject("knee").transform; knee.SetParent(root.transform, false);
                var ankle = new GameObject("ankle").transform; ankle.SetParent(knee, false);
                knee.localPosition = new Vector3(.03f, -.7f, 0f);
                ankle.localPosition = new Vector3(-.03f, -.7f, 0f);
                var solver = new FootPlacementSolver(new FootDefinition
                { Bones = new[] { root.transform, knee, ankle }, SoleOffset = 0f }, 1f, root.transform);
                for (int i = 0; i < 60; i++)
                {
                    root.transform.localRotation = knee.localRotation = ankle.localRotation = Quaternion.identity;
                    solver.Tick(new Plane(-1.4f), Vector3.up, 0f, 1f, 0f, .02f, contactWeight: 1f);
                    Assert.Less(Quaternion.Angle(Quaternion.identity, root.transform.localRotation), .001f);
                    Assert.Less(Quaternion.Angle(Quaternion.identity, knee.localRotation), .001f);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(-1.4f, false)]
        [TestCase(-1.3f, false)]
        [TestCase(-1.4f, true)]
        [TestCase(-1.3f, true)]
        public void AuthoredTravelKeepsTheFootPathWhileCorrectingTerrainHeight(float height, bool stopDuringCapture)
        {
            var root = new GameObject("authored foot travel");
            try
            {
                var knee = new GameObject("knee").transform; knee.SetParent(root.transform, false);
                var ankle = new GameObject("ankle").transform; ankle.SetParent(knee, false);
                knee.localPosition = new Vector3(.2f, -.7f, 0f);
                ankle.localPosition = new Vector3(-.2f, -.7f, 0f);
                var solver = new FootPlacementSolver(new FootDefinition
                { Bones = new[] { root.transform, knee, ankle }, SoleOffset = 0f, JointLimit = 90f }, 1f, root.transform);
                var ground = new Plane(height);
                for (int i = 0; i < 90; i++)
                {
                    bool moving = !stopDuringCapture || i < 60;
                    if (moving) root.transform.position += Vector3.right * .002f;
                    root.transform.localRotation = knee.localRotation = ankle.localRotation = Quaternion.identity;
                    Vector3 authored = ankle.position;
                    solver.Tick(ground, Vector3.up, 0f, moving ? 1f : 0f, 0f, .02f,
                        contactWeight: 1f, preserveAnimatedTravel: true);
                    if (i < 40) continue;
                    Assert.Less(Vector3.ProjectOnPlane(ankle.position - authored, Vector3.up).magnitude, .001f);
                    Assert.AreEqual(height, ankle.position.y, .001f);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void StairClearanceCannotLagBelowTheTread()
        {
            var root = new GameObject("stair clearance");
            try
            {
                var knee = new GameObject("knee").transform; knee.SetParent(root.transform, false);
                var ankle = new GameObject("ankle").transform; ankle.SetParent(knee, false);
                knee.localPosition = new Vector3(.2f, -.7f, 0f);
                ankle.localPosition = new Vector3(-.2f, -.7f, 0f);
                float upper = knee.localPosition.magnitude, lower = ankle.localPosition.magnitude;
                var solver = new FootPlacementSolver(new FootDefinition
                { Bones = new[] { root.transform, knee, ankle }, MaxCorrection = .35f, JointLimit = 90f }, 1f, root.transform);
                solver.Tick(new Plane(), Vector3.up, 0f, 1f, 0f, .02f, contactWeight: 1f, lockStance: true);
                Assert.GreaterOrEqual(ankle.position.y, -1.301f, "Damping must not preserve penetration below a reachable tread.");
                Assert.AreEqual(upper, Vector3.Distance(root.transform.position, knee.position), .0001f);
                Assert.AreEqual(lower, Vector3.Distance(knee.position, ankle.position), .0001f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void SettledContactStaysFixedWhileTheBodyMoves()
        {
            var root = new GameObject("settled contact");
            try
            {
                var knee = new GameObject("knee").transform; knee.SetParent(root.transform, false);
                var ankle = new GameObject("ankle").transform; ankle.SetParent(knee, false);
                knee.localPosition = new Vector3(.2f, -.7f, 0f);
                ankle.localPosition = new Vector3(-.2f, -.7f, 0f);
                var solver = new FootPlacementSolver(new FootDefinition
                { Bones = new[] { root.transform, knee, ankle }, MaxCorrection = 1f, JointLimit = 90f }, 1f, root.transform);
                for (int i = 0; i < 40; i++)
                {
                    root.transform.rotation = knee.localRotation = ankle.localRotation = Quaternion.identity;
                    solver.Tick(new Plane(), Vector3.up, 0f, 1f, 0f, .02f, contactWeight: 1f, lockStance: true);
                }
                Vector3 anchor = ankle.position;
                for (int i = 0; i < 30; i++)
                {
                    root.transform.position += Vector3.right * .003f;
                    root.transform.rotation = knee.localRotation = ankle.localRotation = Quaternion.identity;
                    solver.Tick(new Plane(), Vector3.up, 0f, 1f, 0f, .02f, contactWeight: 1f, lockStance: true);
                    Assert.Less(Vector3.Distance(anchor, ankle.position), .001f);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void NearlyStraightAnimatedKneeKeepsTheAuthoredBendSide()
        {
            var root = new GameObject("straight knee continuity");
            try
            {
                var knee = new GameObject("knee").transform; knee.SetParent(root.transform, false);
                var ankle = new GameObject("ankle").transform; ankle.SetParent(knee, false);
                knee.localPosition = new Vector3(0f, -.7f, .002f);
                ankle.localPosition = new Vector3(0f, -.7f, -.002f);
                var solver = new FootPlacementSolver(new FootDefinition
                { Bones = new[] { root.transform, knee, ankle }, BendDirection = Vector3.forward, MaxCorrection = 1f }, 1f, root.transform);
                for (int i = 0; i < 80; i++)
                {
                    root.transform.localRotation = knee.localRotation = ankle.localRotation = Quaternion.identity;
                    float side = i % 2 == 0 ? .002f : -.002f;
                    knee.localPosition = new Vector3(0f, -.7f, side);
                    ankle.localPosition = new Vector3(0f, -.7f, -side);
                    solver.Tick(new Plane(), Vector3.up, 0f, 1f, 0f, .02f, contactWeight: 1f);
                    if (i > 20) Assert.Greater(knee.position.z, .01f, "A tiny source-pose sign change must not invert the solved knee.");
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void IdleStepStartsAtDisplayedFootAfterAnimationRestoresPose()
        {
            var root = new GameObject("foot continuity");
            try
            {
                var knee = new GameObject("knee").transform;
                knee.SetParent(root.transform, false);
                knee.localPosition = new Vector3(.2f, -.7f, 0f);
                var ankle = new GameObject("ankle").transform;
                ankle.SetParent(knee, false);
                ankle.localPosition = new Vector3(-.2f, -.7f, 0f);
                var solver = new FootPlacementSolver(new FootDefinition
                {
                    Bones = new[] { root.transform, knee, ankle }, MaxCorrection = 1f,
                }, 1f, root.transform);
                var ground = new Plane();
                for (int i = 0; i < 30; i++)
                {
                    root.transform.localRotation = knee.localRotation = ankle.localRotation = Quaternion.identity;
                    solver.Tick(ground, Vector3.up, 0f, 0f, 0f, 1f / 60f);
                }
                Vector3 displayed = ankle.position;
                root.transform.position += Vector3.right * .6f;
                root.transform.localRotation = knee.localRotation = ankle.localRotation = Quaternion.identity;
                Assert.That(Vector3.Distance(displayed, ankle.position), Is.GreaterThan(.4f));
                solver.Tick(ground, Vector3.up, 0f, 0f, 0f, 1f / 60f, allowStep: true);
                Assert.That(solver.Stepping, Is.True);
                Vector3 start = (Vector3)typeof(FootPlacementSolver).GetField("_stepStart",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(solver);
                Assert.That(Vector3.Distance(start, displayed), Is.LessThan(.00001f),
                    "A procedural step must not start at the restored animation contact.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void SampledSwingContactReleasesAPlantedFoot()
        {
            var root = new GameObject("sampled contact");
            try
            {
                var knee = new GameObject("knee").transform;
                knee.SetParent(root.transform, false); knee.localPosition = new Vector3(.2f, -.7f, 0f);
                var ankle = new GameObject("ankle").transform;
                ankle.SetParent(knee, false); ankle.localPosition = new Vector3(-.2f, -.7f, 0f);
                var solver = new FootPlacementSolver(new FootDefinition
                { Bones = new[] { root.transform, knee, ankle }, MaxCorrection = 1f }, 1f, root.transform);
                solver.Tick(new Plane(), Vector3.up, 0f, 0f, 0f, .02f, contactWeight: 1f);
                Assert.IsTrue(solver.Planted);
                solver.Tick(new Plane(), Vector3.up, 0f, 0f, 0f, .02f, contactWeight: 0f);
                Assert.IsFalse(solver.Planted, "Sampled swing must override the generic stance curve.");
                Assert.AreEqual(0f, solver.BodyOffset(new Plane(), Vector3.up, 0f, 0f, 0f, 0f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        sealed class Plane : IGroundingProvider
        {
            readonly float _height;
            public Plane(float height = -1.3f) => _height = height;
            public bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult hit)
            {
                hit = new GroundResult(new Vector3(position.x, _height + offset, position.z), Vector3.up);
                return true;
            }
        }
    }
}
