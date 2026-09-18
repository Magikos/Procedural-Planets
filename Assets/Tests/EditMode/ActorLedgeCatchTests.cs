using System;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorLedgeCatchTests
    {
        GameObject _wall;
        ActorTraversal _traversal;
        readonly Vector3 _origin = new(1800f, 1800f, 1800f);
        CharacterPose Pose => new(_origin + new Vector3(0f, .05f, -.31f), Vector3.up, Vector3.forward);

        [SetUp]
        public void SetUp()
        {
            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.transform.position = _origin + new Vector3(0f, .8f, .5f);
            _wall.transform.localScale = new Vector3(2f, 1.6f, 1f);
            Physics.SyncTransforms();
            _traversal = new ActorTraversal(new ActorCollision(1 << 0));
        }
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(_wall);

        [Test]
        public void DescendingCatchReachesHangWithoutAnotherJump()
        {
            Assert.IsTrue(_traversal.TryCatchLedge(Pose, Vector3.down * 2f, Vector3.forward));
            Assert.AreEqual(TraversalKind.JumpGrab, _traversal.Kind);
            var end = _traversal.Tick(.13f);
            Assert.AreEqual(TraversalKind.Hanging, _traversal.Kind);
            Assert.Less(Vector3.Distance(Pose.Position, end.Position), .4f);
        }

        [Test]
        public void AuthoredJumpPreparesOnGroundBeforeRisingToHang()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(
                "Assets/Art/Characters/Animations/ReviewCandidates/Ledge Jump Motion.asset");
            _traversal.JumpGrabMotion = asset.CreateMotion();
            _wall.transform.position = _origin + new Vector3(0f, 2.45f, .5f);
            _wall.transform.localScale = new Vector3(2f, .2f, 1f);
            Physics.SyncTransforms();
            var pose = new CharacterPose(_origin + Vector3.back * .5f, Vector3.up, Vector3.forward);
            Assert.IsTrue(_traversal.TryBegin(pose), _traversal.Rejection);
            Assert.That(Vector3.Distance(_traversal.Tick(.35f).Position, pose.Position), Is.LessThan(.001f));
            Assert.That(Vector3.Dot(_traversal.Tick(.25f).Position - pose.Position, Vector3.up), Is.GreaterThan(.3f));
            _traversal.Tick(asset.Clip.length);
            Assert.That(_traversal.Kind, Is.EqualTo(TraversalKind.Hanging));
        }

        [Test]
        public void ReactiveCatchDoesNotReplayGroundedPreparation()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(
                "Assets/Art/Characters/Animations/ReviewCandidates/Ledge Jump Motion.asset");
            _traversal.JumpGrabMotion = asset.CreateMotion();
            Assert.IsTrue(_traversal.TryCatchLedge(Pose, Vector3.down * 2f, Vector3.forward));
            Assert.IsNull(_traversal.ActiveMotion);
            _traversal.Tick(.13f);
            Assert.That(_traversal.Kind, Is.EqualTo(TraversalKind.Hanging));
        }

        [Test]
        public void BackingAwayDoesNotCatch()
        {
            Assert.IsFalse(_traversal.TryCatchLedge(Pose, Vector3.down, Vector3.back));
        }

        [Test]
        public void JumpCanReachSuspendedReviewPlatformWithoutALowWall()
        {
            _wall.transform.position = _origin + new Vector3(0f, 2.45f, .5f);
            _wall.transform.localScale = new Vector3(2f, .2f, 1f);
            Physics.SyncTransforms();
            Assert.IsTrue(_traversal.TryBegin(new CharacterPose(_origin + Vector3.back * .5f, Vector3.up, Vector3.forward)), _traversal.Rejection);
            _traversal.Tick(.61f);
            Assert.AreEqual(TraversalKind.Hanging, _traversal.Kind);
        }

        [TestCase(1f)]
        [TestCase(-1f)]
        public void StationaryJumpCatchesThinPlatformInEitherFlightDirection(float verticalSpeed)
        {
            _wall.transform.position = _origin + new Vector3(0f, 1.55f, .5f);
            _wall.transform.localScale = new Vector3(2f, .1f, 1f);
            Physics.SyncTransforms();
            Assert.IsTrue(_traversal.TryCatchLedge(Pose, Vector3.up * verticalSpeed, Vector3.zero));
            _traversal.Tick(.13f);
            Assert.AreEqual(TraversalKind.Hanging, _traversal.Kind);
        }

        [Test]
        public void ReleaseSuppressesRecatchUntilGroundedOrSeparated()
        {
            Assert.IsTrue(_traversal.TryCatchLedge(Pose, Vector3.down, Vector3.forward));
            _traversal.Tick(.13f); _traversal.Cancel();
            _traversal.UpdateCatchAvailability(Pose, false);
            Assert.IsFalse(_traversal.TryCatchLedge(Pose, Vector3.down, Vector3.forward));
            _traversal.UpdateCatchAvailability(Pose, true);
            Assert.IsTrue(_traversal.TryCatchLedge(Pose, Vector3.down, Vector3.forward));
        }

        [Test]
        public void NarrowTopAndOutOfReachTopDoNotCatch()
        {
            _wall.transform.localScale = new Vector3(.2f, 1.6f, 1f);
            Physics.SyncTransforms();
            Assert.IsFalse(_traversal.TryCatchLedge(Pose, Vector3.down, Vector3.forward));
            _wall.transform.localScale = new Vector3(2f, 3f, 1f);
            Physics.SyncTransforms();
            Assert.IsFalse(_traversal.TryCatchLedge(Pose, Vector3.down, Vector3.forward));
        }

        [Test]
        public void MovedSupportCancelsCapture()
        {
            Assert.IsTrue(_traversal.TryCatchLedge(Pose, Vector3.down, Vector3.forward));
            _wall.transform.position += Vector3.right * .1f;
            _traversal.Tick(.02f);
            Assert.IsFalse(_traversal.Active);
        }

        [Test]
        public void CatchUsesTheActorGravityFrame()
        {
            Quaternion rotation = Quaternion.AngleAxis(90f, Vector3.forward);
            _wall.transform.position = _origin + rotation * new Vector3(0f, .8f, .5f);
            _wall.transform.rotation = rotation;
            Physics.SyncTransforms();
            var pose = new CharacterPose(_origin + rotation * new Vector3(0f, .05f, -.31f),
                rotation * Vector3.up, rotation * Vector3.forward);
            Assert.IsTrue(_traversal.TryCatchLedge(pose, rotation * Vector3.down, rotation * Vector3.forward));
            _traversal.Tick(.13f);
            Assert.AreEqual(TraversalKind.Hanging, _traversal.Kind);
        }

        [Test]
        public void TopEdgeDescentMovesOutsideBeforeLoweringIntoHang()
        {
            var top = new CharacterPose(_origin + new Vector3(0f, 1.64f, .25f), Vector3.up, Vector3.back);
            Assert.IsTrue(_traversal.TryDropToHang(top, Vector3.back), _traversal.Rejection);
            Assert.AreEqual(TraversalKind.DropToHang, _traversal.Kind);
            var turn = _traversal.Tick(.44f);
            Assert.AreEqual(top.Position.y, turn.Position.y, .001f);
            Assert.Less(turn.Position.z, _origin.z);
            Assert.Greater(Vector3.Dot(turn.Forward, Vector3.forward), .99f);
            _traversal.Tick(.7f);
            Assert.AreEqual(TraversalKind.Hanging, _traversal.Kind, _traversal.Rejection);
            Assert.AreEqual(_origin.y + .05f, _traversal.Pose.Position.y, .002f);
        }

        [Test]
        public void TopDescentRejectsInteriorAndMissingHandSupport()
        {
            _wall.transform.position = _origin + new Vector3(0f, .8f, 1.5f);
            _wall.transform.localScale = new Vector3(2f, 1.6f, 3f);
            Physics.SyncTransforms();
            var top = new CharacterPose(_origin + new Vector3(0f, 1.64f, 1.3f), Vector3.up, Vector3.back);
            Assert.IsFalse(_traversal.TryDropToHang(top, Vector3.back));
            _wall.transform.position = _origin + new Vector3(0f, .8f, .5f);
            _wall.transform.localScale = new Vector3(.2f, 1.6f, 1f);
            Physics.SyncTransforms();
            top = new CharacterPose(_origin + new Vector3(0f, 1.64f, .25f), Vector3.up, Vector3.back);
            Assert.IsFalse(_traversal.TryDropToHang(top, Vector3.back));
        }

        [Test]
        public void TopDescentFindsNearbyEdgeWhenActorFacesInward()
        {
            var top = new CharacterPose(_origin + new Vector3(0f, 1.64f, .25f), Vector3.up, Vector3.forward);
            Assert.IsFalse(_traversal.TryDropToHang(top, Vector3.forward));
            Assert.IsTrue(_traversal.TryDropToHang(top), _traversal.Rejection);
            Assert.AreEqual(top.Position, _traversal.Pose.Position);
            var early = _traversal.Tick(.02f);
            Assert.Less(Vector3.Distance(early.Position, top.Position), .02f);
            _traversal.Tick(2f);
            Assert.AreEqual(TraversalKind.Hanging, _traversal.Kind, _traversal.Rejection);
        }

        [Test]
        public void TopDescentRejectsAnObstacleAcrossTheApproach()
        {
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                obstacle.transform.position = _origin + new Vector3(0f, .6f, -.31f);
                obstacle.transform.localScale = new Vector3(1f, .5f, .2f);
                Physics.SyncTransforms();
                var top = new CharacterPose(_origin + new Vector3(0f, 1.64f, .25f), Vector3.up, Vector3.back);
                Assert.IsFalse(_traversal.TryDropToHang(top, Vector3.back));
                Assert.IsFalse(_traversal.Active);
            }
            finally { UnityEngine.Object.DestroyImmediate(obstacle); }
        }

        [Test]
        public void TopDescentWorksWithSidewaysGravity()
        {
            Quaternion rotation = Quaternion.AngleAxis(90f, Vector3.forward);
            _wall.transform.position = _origin + rotation * new Vector3(0f, .8f, .5f);
            _wall.transform.rotation = rotation;
            Physics.SyncTransforms();
            var top = new CharacterPose(_origin + rotation * new Vector3(0f, 1.64f, .25f),
                rotation * Vector3.up, rotation * Vector3.back);
            Assert.IsTrue(_traversal.TryDropToHang(top, rotation * Vector3.back), _traversal.Rejection);
            _traversal.Tick(2f);
            Assert.AreEqual(TraversalKind.Hanging, _traversal.Kind, _traversal.Rejection);
        }

        [Test]
        public void AngledFacingDoesNotIncreaseDistanceToTheNearbyEdge()
        {
            var top = new CharacterPose(_origin + new Vector3(0f, 1.64f, .4f), Vector3.up,
                new Vector3(1f, 0f, 1f).normalized);
            Assert.IsTrue(_traversal.TryDropToHang(top), _traversal.Rejection);
            Assert.AreEqual(_origin.x, _traversal.Edge.x, .001f);
            _traversal.Tick(2f);
            Assert.AreEqual(TraversalKind.Hanging, _traversal.Kind);
        }

        [Test]
        public void InvalidMotionIsRejected()
        {
            Assert.Throws<ArgumentException>(() => _traversal.TryCatchLedge(Pose, Vector3.positiveInfinity, Vector3.forward));
        }
    }
}
