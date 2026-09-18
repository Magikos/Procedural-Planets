using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorBeamTests
    {
        GameObject _root;
        BeamInteraction _beam;
        ActorBeam _actor;
        readonly Vector3 _origin = new(1500f, 1500f, 1500f);

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Beam fixture");
            _root.transform.position = _origin;
            _beam = _root.AddComponent<BeamInteraction>();
            _beam.Surface = Box(new Vector3(0f, -.2f, 0f), new Vector3(.34f, .4f, 4f)).GetComponent<Collider>();
            Box(new Vector3(0f, -.2f, -3.5f), new Vector3(2f, .4f, 3f));
            Box(new Vector3(0f, -.2f, 3.5f), new Vector3(2f, .4f, 3f));
            _beam.Motions = Enumerable.Range(0, 7).Select(i => AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(
                "Assets/Art/Interactions/Animations/Beam " + BeamInteraction.PhaseName(i) + " motion.asset")).ToArray();
            _actor = new ActorBeam(new ActorCollision(1 << 0));
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(_root);

        GameObject Box(Vector3 position, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.SetParent(_root.transform, false);
            box.transform.localPosition = position; box.transform.localScale = size;
            Physics.SyncTransforms(); return box;
        }

        void Begin(Vector3 forward)
        {
            Assert.IsTrue(_actor.Begin(_beam, new CharacterPose(_beam.Entry(forward), Vector3.up, forward)), _actor.Rejection);
            Step(0f, 100);
            Assert.AreEqual("Idle", _actor.PhaseName);
        }

        void Step(float move, int count)
        {
            for (int i = 0; i < count; i++) _actor.Tick(move, false, .02f);
        }

        [TestCase(1f)] [TestCase(-1f)]
        public void CrossFromEitherEnd(float direction)
        {
            Begin(Vector3.forward * direction);
            for (int i = 0; i < 1500 && _actor.Active; i++) _actor.Tick(1f, false, .02f);
            Assert.IsFalse(_actor.Active, _actor.Rejection);
            Assert.Greater((_actor.Pose.Position.z - _origin.z) * direction, 2f);
        }

        [Test]
        public void StopBackUpTurnAndReturn()
        {
            Begin(Vector3.forward);
            Step(1f, 100); Step(0f, 100);
            var stopped = _actor.Pose.Position;
            Step(0f, 50);
            Assert.AreEqual(stopped, _actor.Pose.Position);
            Step(-1f, 1); Step(0f, 100);
            Assert.Less(_actor.Pose.Position.z, stopped.z);
            _actor.Tick(0f, true, .02f); Step(0f, 60);
            Assert.Greater(Vector3.Dot(_actor.Pose.Forward, Vector3.back), .999f);
            for (int i = 0; i < 500 && _actor.Active; i++) _actor.Tick(1f, false, .02f);
            Assert.IsFalse(_actor.Active);
            Assert.Less(_actor.Pose.Position.z - _origin.z, -2f);
        }

        [Test]
        public void BlockedStepPreservesReverseAndCancel()
        {
            Begin(Vector3.forward);
            Box(new Vector3(0f, 1f, -1.4f), new Vector3(1f, 2f, .2f));
            var before = _actor.Pose.Position;
            Step(1f, 100);
            Assert.AreEqual(before, _actor.Pose.Position);
            Assert.AreEqual("Idle", _actor.PhaseName);
            Assert.IsNotNull(_actor.Rejection);
            Step(-1f, 1); Step(0f, 100);
            Assert.Less(_actor.Pose.Position.z, before.z);
            _actor.Cancel();
            Assert.IsFalse(_actor.Active);
            before = _actor.Pose.Position; Step(1f, 100);
            Assert.AreEqual(before, _actor.Pose.Position);
        }

        [TestCase(false)] [TestCase(true)]
        public void LostOrMovedSupportReleasesWithoutTeleport(bool moved)
        {
            Begin(Vector3.forward);
            var before = _actor.Pose.Position;
            if (moved) _root.transform.position += Vector3.up;
            else _beam.Surface.enabled = false;
            Physics.SyncTransforms(); Step(1f, 1);
            Assert.IsFalse(_actor.Active);
            Assert.AreEqual(before, _actor.Pose.Position);
        }

        void UseLedge()
        {
            _beam.WallSideWalk = true;
            _beam.Surface.transform.localScale = new Vector3(.65f, .4f, 4f);
            _beam.Wall = Box(new Vector3(-.65f, 1f, 0f), new Vector3(.5f, 2f, 4f)).GetComponent<Collider>();
            _beam.Motions = Enumerable.Range(0, 7).Select(i => AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(
                "Assets/Art/Interactions/Animations/Ledge Walk " + BeamInteraction.PhaseName(i) + " motion.asset")).ToArray();
            Physics.SyncTransforms();
        }

        [TestCase(false)] [TestCase(true)]
        public void LedgeCrossOrReversePreservesExitFacing(bool reverse)
        {
            UseLedge(); Begin(Vector3.forward);
            Step(1f, 100); Step(0f, 70);
            for (int i = 0; i < 1200 && _actor.Active; i++) _actor.Tick(reverse ? -1f : 1f, false, .02f);
            Assert.IsFalse(_actor.Active, _actor.Rejection);
            Assert.Greater(Vector3.Dot(_actor.Pose.Forward, reverse ? Vector3.back : Vector3.forward), .999f);
            Assert.Greater((_actor.Pose.Position.z - _origin.z) * (reverse ? -1f : 1f), 2f);
        }

        [Test]
        public void LedgeRejectsMissingWallAndDoesNotTurnIntoIt()
        {
            UseLedge(); Begin(Vector3.forward);
            _actor.Tick(0f, true, .02f); Step(0f, 100);
            Assert.AreEqual("Idle", _actor.PhaseName);
            Assert.Greater(Vector3.Dot(_actor.Pose.Forward, Vector3.forward), .999f);
            _beam.Wall.enabled = false;
            var before = _actor.Pose.Position; Step(1f, 1);
            Assert.IsFalse(_actor.Active);
            Assert.AreEqual(before, _actor.Pose.Position);
        }

        [Test]
        public void InvalidPoseDoesNotAcquireBeam()
        {
            Assert.IsFalse(_actor.Begin(_beam, new CharacterPose(Vector3.positiveInfinity, Vector3.up, Vector3.forward)));
            Assert.IsFalse(_actor.Active);
        }
    }
}
