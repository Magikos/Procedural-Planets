using System;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class InteractionLimbTests
    {
        GameObject _root;
        Transform _upper, _elbow, _tip, _finger;
        InteractionLimbDefinition _definition;
        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Interaction fixture");
            _upper = Bone("Upper", _root.transform, Vector3.zero);
            _elbow = Bone("Elbow", _upper, Vector3.right * .5f);
            _tip = Bone("Tip", _elbow, Vector3.right * .5f);
            _finger = Bone("Finger", _tip, Vector3.forward * .1f);
            _definition = new InteractionLimbDefinition { Bones = new[] { _upper, _elbow, _tip },
                JointLimit = 180f, ContactPosition = Vector3.forward * .1f, BendDirection = Vector3.down,
                WristLimitDegrees = 180f };
        }
        static Transform Bone(string name, Transform parent, Vector3 position)
        {
            var bone = new GameObject(name).transform; bone.SetParent(parent, false); bone.localPosition = position; return bone;
        }
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(_root);

        [Test]
        public void ContactSocketMeetsTargetWithoutChangingBoneLengths()
        {
            var solver = new InteractionLimbSolver(_root.transform, _definition);
            var target = new Vector3(.6f, 0f, .6f);
            solver.Solve(new InteractionPoseTarget(target, Quaternion.identity, useContact: true));
            Assert.Less(Vector3.Distance(solver.WorldContactPosition, target), .02f);
            Assert.IsTrue(solver.Reachable);
            Assert.AreEqual(.5f, Vector3.Distance(_upper.position, _elbow.position), .00001f);
            Assert.AreEqual(.5f, Vector3.Distance(_elbow.position, _tip.position), .00001f);
        }

        [Test]
        public void UnreachableTargetDoesNotReportContactOrStretch()
        {
            _definition.ReachConeDegrees = 45f;
            var solver = new InteractionLimbSolver(_root.transform, _definition);
            solver.Solve(new InteractionPoseTarget(Vector3.back * 5f, useContact: true));
            Assert.IsFalse(solver.Reachable);
            Assert.LessOrEqual(Vector3.Distance(_upper.position, _tip.position), .981f);
            Assert.LessOrEqual(Vector3.Angle(Vector3.forward, _tip.position - _upper.position), 45.1f);
        }

        [Test]
        public void MovingContactPreservesAuthoredReachBehindActor()
        {
            _elbow.localPosition = new Vector3(.2f, .3f, -.4f);
            _tip.localPosition = new Vector3(-.2f, -.3f, -.4f);
            _definition.ReachConeDegrees = 45f;
            var solver = new InteractionLimbSolver(_root.transform, _definition);
            Vector3 palm = solver.WorldContactPosition;
            Vector3 elbow = _elbow.position;
            solver.Solve(new InteractionPoseTarget(palm, useContact: true, followAuthoredMotion: true));
            Assert.Less(Vector3.Distance(solver.WorldContactPosition, palm), .001f);
            Assert.Less(Vector3.Distance(_elbow.position, elbow), .001f);
        }

        [Test]
        public void LegacyTargetStillTargetsTheWrist()
        {
            var solver = new InteractionLimbSolver(_root.transform, _definition);
            var target = new Vector3(.6f, .2f, .5f);
            solver.Solve(new InteractionPoseTarget(target));
            Assert.Less(Vector3.Distance(_tip.position, target), .002f);
            Assert.Greater(Vector3.Distance(solver.WorldContactPosition, target), .09f);
        }

        [Test]
        public void ContactJointsUseSnapshotAndBoundedCorrection()
        {
            _definition.ContactJoints = new[] { new InteractionContactJoint { Bone = _finger,
                LocalRotation = Quaternion.Euler(80f, 0f, 0f), MaxCorrectionDegrees = 20f } };
            var solver = new InteractionLimbSolver(_root.transform, _definition);
            _definition.ContactJoints[0].LocalRotation = Quaternion.identity;
            solver.Solve(new InteractionPoseTarget(new Vector3(.6f, 0f, .6f), weight: .5f, useContact: true));
            Assert.AreEqual(10f, Quaternion.Angle(Quaternion.identity, _finger.localRotation), .01f);
        }

        [Test]
        public void PositionCorrectionPreservesAuthoredGripThroughRelease()
        {
            _definition.ContactJoints = new[] { new InteractionContactJoint { Bone = _finger,
                LocalRotation = Quaternion.identity, MaxCorrectionDegrees = 60f } };
            var solver = new InteractionLimbSolver(_root.transform, _definition);
            var blend = new InteractionPoseBlend();
            var target = new InteractionPoseTarget(new Vector3(.6f, 0f, .6f), useContact: true,
                preserveAuthoredContactJoints: true);
            Quaternion authored = Quaternion.Euler(75f, 0f, 0f);
            for (int i = 0; i < 30; i++)
            {
                _finger.localRotation = authored;
                var current = blend.Tick(i < 18 ? target : (InteractionPoseTarget?)null, 1f / 60f,
                    Vector3.zero, Quaternion.identity);
                if (!current.HasValue) continue;
                Assert.IsTrue(current.Value.PreserveAuthoredContactJoints);
                solver.Solve(current.Value);
                Assert.That(Quaternion.Angle(authored, _finger.localRotation), Is.LessThan(.001f));
            }
        }

        [Test]
        public void InvalidContactDefinitionIsRejected()
        {
            _definition.MaximumExtension = float.NaN;
            Assert.Throws<ArgumentException>(() => new InteractionLimbSolver(_root.transform, _definition));
        }
    }
}
