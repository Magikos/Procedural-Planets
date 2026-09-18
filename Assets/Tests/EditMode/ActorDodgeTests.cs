using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorDodgeTests
    {
        GameObject _root;
        ActorDodge _dodge;
        readonly Vector3 _origin = new(1700, 1700, 1700);

        [SetUp]
        public void Setup()
        {
            _root = new GameObject("Dodge test");
            _root.transform.position = _origin;
            Box(new Vector3(0, -.2f, 0), new Vector3(12, .4f, 12));
            _dodge = new ActorDodge(new ActorCollision(1));
        }

        [TearDown] public void Teardown() => Object.DestroyImmediate(_root);

        void Box(Vector3 position, Vector3 scale)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.SetParent(_root.transform, false);
            box.transform.localPosition = position; box.transform.localScale = scale;
            Physics.SyncTransforms();
        }

        ActorTraversalMotion Motion()
        {
            var first = new ActorTraversalMotion.Frame { CapsuleA = Vector3.up * .3f, CapsuleB = Vector3.up, Radius = .2f };
            var last = first; last.Root = Vector3.right * 2; last.PlanarFitWeight = 1;
            return new ActorTraversalMotion(.6f, Vector3.zero, 0, 0, new[] { first, last });
        }
        CharacterPose Pose => new(_origin, _root.transform.up, _root.transform.forward);

        [Test]
        public void FullStepRetainsFacingAndRejectsRetrigger()
        {
            Assert.IsTrue(_dodge.Begin(Motion(), 1, Pose));
            _dodge.Tick(.1f);
            Assert.IsFalse(_dodge.Begin(Motion(), 0, Pose));
            _dodge.RequestStop();
            _dodge.Tick(1f);
            Assert.IsFalse(_dodge.Active);
            Assert.Less(Vector3.Distance(_dodge.Pose.Position, _origin + Vector3.right * 2), .001f);
            Assert.AreEqual(Pose.Forward, _dodge.Pose.Forward);
        }

        [Test]
        public void WallRejectsEntireRouteBeforeMovement()
        {
            Box(new Vector3(1, 1, 0), new Vector3(.1f, 2, 3));
            Assert.IsFalse(_dodge.Begin(Motion(), 1, Pose));
            Assert.AreEqual(_origin, _dodge.Pose.Position);
        }

        [Test]
        public void UnsupportedLandingRejects()
        {
            Object.DestroyImmediate(_root.transform.GetChild(0).gameObject);
            Box(new Vector3(0, -.2f, 0), new Vector3(1, .4f, 3));
            Assert.IsFalse(_dodge.Begin(Motion(), 1, Pose));
        }

        [Test]
        public void NewObstacleStopsBeforeCrossing()
        {
            Assert.IsTrue(_dodge.Begin(Motion(), 1, Pose));
            _dodge.Tick(.1f);
            Box(new Vector3(1, 1, 0), new Vector3(.1f, 2, 3));
            _dodge.Tick(1f);
            Assert.IsFalse(_dodge.Active);
            Assert.Less(_dodge.Pose.Position.x - _origin.x, .75f);
            Assert.That(_dodge.Status, Does.Contain("interrupted"));
        }

        [Test]
        public void LostSupportEndsAction()
        {
            Assert.IsTrue(_dodge.Begin(Motion(), 1, Pose));
            Object.DestroyImmediate(_root.transform.GetChild(0).gameObject);
            Physics.SyncTransforms();
            _dodge.Tick(.02f);
            Assert.IsFalse(_dodge.Active);
        }

        [Test]
        public void MotionUsesActorUpRatherThanWorldUp()
        {
            _root.transform.rotation = Quaternion.Euler(0, 0, 90);
            Physics.SyncTransforms();
            Assert.IsTrue(_dodge.Begin(Motion(), 1, Pose), _dodge.Status);
            _dodge.Tick(1f);
            Assert.Less(Vector3.Distance(_dodge.Pose.Position, _origin + _root.transform.right * 2), .001f);
        }

        [Test]
        public void RejectsInvalidTimeAndDirection()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _dodge.Tick(float.NaN));
            Assert.IsFalse(_dodge.Begin(Motion(), 4, Pose));
        }

        [Test]
        public void RollRequiresClearanceForExtendedBody()
        {
            Box(new Vector3(2, 1, .7f), new Vector3(.3f, 2, .3f));
            Assert.IsTrue(_dodge.Begin(Motion(), 1, Pose));
            _dodge.Reset();
            Assert.IsFalse(_dodge.Begin(Motion(), 3, Pose));
        }

        [Test]
        public void RollCompletesAndRetainsItsPhase()
        {
            Assert.IsTrue(_dodge.Begin(Motion(), 3, Pose));
            Assert.AreEqual("ForwardRoll", _dodge.PhaseName);
            _dodge.RequestStop();
            _dodge.Tick(1f);
            Assert.IsFalse(_dodge.Active);
            Assert.Less(Vector3.Distance(_dodge.Pose.Position, _origin + Vector3.right * 2), .001f);
        }

        [Test]
        public void MovementCanResumeOnlyAfterAuthoredSupportMarker()
        {
            Assert.IsTrue(_dodge.Begin(Motion(), 3, Pose, .5f));
            _dodge.Tick(.25f);
            Assert.IsFalse(_dodge.TryResumeLocomotion());
            _dodge.Tick(.06f);
            var supportedPose = _dodge.Pose;
            Assert.IsTrue(_dodge.TryResumeLocomotion());
            Assert.IsFalse(_dodge.Active);
            Assert.AreEqual(supportedPose.Position, _dodge.Pose.Position);
        }

        [Test]
        public void NoMovementPreservesFullRecoveryAndDefaultStaysCommitted()
        {
            Assert.IsTrue(_dodge.Begin(Motion(), 3, Pose, .5f));
            _dodge.Tick(.4f);
            Assert.IsTrue(_dodge.Active);
            _dodge.Tick(.3f);
            Assert.IsFalse(_dodge.Active);
            Assert.IsTrue(_dodge.Begin(Motion(), 1, Pose));
            _dodge.Tick(.4f);
            Assert.IsFalse(_dodge.TryResumeLocomotion());
        }
    }
}
