using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorRopeTests
    {
        GameObject _root;
        RopeInteraction _rope;
        ActorRope _actor;
        readonly Vector3 _origin = new(1700, 1700, 1700);
        [SetUp] public void SetUp()
        {
            _root = new GameObject("Rope fixture"); _root.transform.position = _origin;
            _rope = _root.AddComponent<RopeInteraction>();
            _rope.Motions = Enumerable.Range(0, 5).Select(i => AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(
                "Assets/Art/Interactions/Animations/Rope " + RopeInteraction.PhaseName(i) + " motion.asset")).ToArray();
            Box(new Vector3(0, -.2f, -.5f), new Vector3(3, .4f, 4));
            _actor = new ActorRope(new ActorCollision(1 << 0));
        }
        [TearDown] public void TearDown() => Object.DestroyImmediate(_root);
        void Box(Vector3 position, Vector3 scale)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.transform.SetParent(_root.transform, false);
            box.transform.localPosition = position; box.transform.localScale = scale; Physics.SyncTransforms();
        }
        void Begin()
        {
            Assert.IsTrue(_actor.Begin(_rope, new CharacterPose(_rope.Entry, Vector3.up, Vector3.forward)), _actor.Rejection);
            Step(0, 60); Assert.AreEqual("Idle", _actor.PhaseName);
        }
        void Step(float move, int count) { for (int i = 0; i < count; i++) _actor.Tick(move, .02f); }
        [Test] public void MountClimbStopReverseAndExit()
        {
            Begin(); Step(1, 85); Step(0, 60);
            var stopped = _actor.Pose.Position; Assert.Greater(stopped.y - _origin.y, 2);
            Step(0, 50); Assert.AreEqual(stopped, _actor.Pose.Position);
            for (int i = 0; i < 400 && _actor.Active; i++) _actor.Tick(-1, .02f);
            Assert.IsFalse(_actor.Active, _actor.Rejection);
            Assert.That(Vector3.Distance(_actor.Pose.Position, _rope.Entry), Is.LessThan(.002f));
        }
        [Test] public void TopLimitKeepsDescentAvailable()
        {
            Begin(); Step(1, 500); Assert.IsTrue(_actor.Active);
            Assert.AreEqual("Idle", _actor.PhaseName); Assert.IsNotNull(_actor.Rejection);
            float height = _actor.Pose.Position.y; Step(-1, 60);
            Assert.Less(_actor.Pose.Position.y, height);
        }
        [Test] public void BlockedClimbDoesNotMoveAndCanExit()
        {
            Begin(); Box(new Vector3(0, 2.2f, -.22f), new Vector3(2, .2f, 2));
            var before = _actor.Pose.Position; Step(1, 60);
            Assert.AreEqual(before, _actor.Pose.Position); Assert.IsNotNull(_actor.Rejection);
            Step(-1, 60); Assert.IsFalse(_actor.Active);
        }
        [TestCase(false)] [TestCase(true)] public void CancelOrLostSupportPreservesReleasePosition(bool lost)
        {
            Begin(); Step(1, 30); var before = _actor.Pose.Position;
            if (lost) { _rope.enabled = false; Step(0, 1); } else _actor.Cancel();
            Assert.IsFalse(_actor.Active); Assert.AreEqual(before, _actor.Pose.Position);
        }
        [Test] public void InvalidApproachDoesNotAcquire()
        {
            Assert.IsFalse(_actor.Begin(_rope, new CharacterPose(_origin + Vector3.right * 3, Vector3.up, Vector3.forward)));
            Assert.IsFalse(_actor.Active);
        }
    }
}
