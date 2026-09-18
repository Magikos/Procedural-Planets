using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorTraversalTests
    {
        GameObject _root;
        readonly Vector3 _origin = new(1000f, 1000f, 1000f);
        ActorCollision _collision;
        [SetUp] public void SetUp()
        {
            _root = new GameObject("Traversal fixture"); _root.transform.position = _origin;
            _collision = new ActorCollision(1 << 0);
            Box(new Vector3(0f, -.5f, 0f), new Vector3(30f, 1f, 30f));
        }
        [TearDown] public void TearDown() => Object.DestroyImmediate(_root);
        GameObject Box(Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = center; go.transform.localScale = size; Physics.SyncTransforms(); return go;
        }
        CharacterPose Pose(float z = 0f) => new(_origin + new Vector3(0f, .025f, z), Vector3.up, Vector3.forward);

        [TestCase(ActorStance.Standing)]
        [TestCase(ActorStance.Crouching)]
        public void SettledCrawlCanRiseWithoutTheFloorBlockingIt(ActorStance destination)
        {
            Assert.IsTrue(_collision.TryStance(ActorStance.Crawling, Pose()));
            Assert.IsTrue(_collision.Support(Pose().Position, Vector3.up, Vector3.forward, out var support));
            var settled = new CharacterPose(support.Position, Vector3.up, Vector3.forward);
            Assert.IsTrue(_collision.TryStance(destination, settled));
            Assert.AreEqual(destination, _collision.Stance);
        }

        ActorTraversal BakedVault()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(
                "Assets/Art/Characters/Motion/Basic Vault Motion.asset");
            Assert.IsNotNull(asset, "The review vault motion asset must exist.");
            Box(new Vector3(0f, .5f, .85f), new Vector3(2f, 1f, .4f));
            return new ActorTraversal(_collision) { VaultMotion = asset.CreateMotion() };
        }

        CharacterPose VaultPose() => new(_origin + Vector3.up * .09f, Vector3.up, Vector3.forward);

        [TestCase(1.4f)]
        [TestCase(4f)]
        public void EarlyVaultInputWaitsForOrdinaryMovementWithoutSnapping(float speed)
        {
            var traversal = BakedVault();
            var pose = new CharacterPose(VaultPose().Position - Vector3.forward * .45f, Vector3.up, Vector3.forward);
            Assert.IsFalse(traversal.ResolveJump(pose, Vector3.forward * speed, true, true, .02f));
            Assert.IsTrue(traversal.ApproachPending);
            Assert.IsFalse(traversal.Active);
            for (int i = 0; i < 13 && !traversal.Active; i++)
            {
                pose = new CharacterPose(pose.Position + Vector3.forward * (speed * .02f), pose.Up, pose.Forward);
                Assert.IsFalse(traversal.ResolveJump(pose, Vector3.forward * speed, true, false, .02f));
            }
            Assert.AreEqual(TraversalKind.Vault, traversal.Kind, traversal.Rejection);
            Assert.AreEqual(pose.Position, traversal.Pose.Position, "Starting a queued vault must retain the current motor position.");
            Assert.AreEqual(0f, traversal.Progress);
            Assert.IsFalse(traversal.ApproachPending);
        }

        [TestCase(0f)]
        [TestCase(-1.4f)]
        public void EarlyVaultInputCancelsWhenActorStopsOrTurnsAway(float speed)
        {
            var traversal = BakedVault();
            var pose = new CharacterPose(VaultPose().Position - Vector3.forward * .45f, Vector3.up, Vector3.forward);
            Assert.IsFalse(traversal.ResolveJump(pose, Vector3.forward * 1.4f, true, true, .02f));
            Assert.IsFalse(traversal.ResolveJump(pose, Vector3.forward * speed, true, false, .02f));
            Assert.IsFalse(traversal.ApproachPending);
            Assert.IsFalse(traversal.Active);
        }

        [Test]
        public void OrdinaryJumpIsImmediateAndApproachCannotWaitForever()
        {
            var traversal = new ActorTraversal(_collision);
            Assert.IsTrue(traversal.ResolveJump(VaultPose(), Vector3.forward * 1.4f, true, true, .02f));
            Assert.IsFalse(traversal.ApproachPending);
            traversal = BakedVault();
            var pose = new CharacterPose(VaultPose().Position - Vector3.forward * .45f, Vector3.up, Vector3.forward);
            Assert.IsFalse(traversal.ResolveJump(pose, Vector3.forward * 1.4f, true, true, .02f));
            Assert.IsTrue(traversal.ResolveJump(pose, Vector3.forward * 1.4f, true, false, .3f));
            Assert.IsFalse(traversal.ApproachPending);
            Assert.IsFalse(traversal.Active);
        }

        [Test]
        public void AnotherTraversalStartConsumesPendingApproach()
        {
            var traversal = BakedVault();
            var pose = new CharacterPose(VaultPose().Position - Vector3.forward * .45f, Vector3.up, Vector3.forward);
            Assert.IsFalse(traversal.ResolveJump(pose, Vector3.forward * 1.4f, true, true, .02f));
            Assert.IsTrue(traversal.TryBegin(VaultPose()), traversal.Rejection);
            Assert.IsFalse(traversal.ApproachPending, "A direct action must not leave a delayed jump behind.");
        }

        [Test]
        public void BakedVaultCrossesReviewBarrierAndLandsWithStandingClearance()
        {
            var traversal = BakedVault();
            Assert.IsTrue(traversal.TryBegin(VaultPose()), traversal.Rejection);
            Assert.AreEqual(TraversalKind.Vault, traversal.Kind);
            var motion = traversal.ActiveMotion;
            for (int i = 0; i < 180 && traversal.Active; i++) traversal.Tick(1f / 60f);
            Assert.IsNull(traversal.Rejection);
            Assert.IsFalse(traversal.Active);
            Assert.AreEqual(1f, traversal.Progress);
            Assert.AreSame(motion, traversal.ActiveMotion, "Outgoing presentation still needs its authored trajectory.");
            Assert.Greater(traversal.Pose.Position.z - _origin.z, 1.3f);
            Assert.AreEqual(.04f, traversal.Pose.Position.y - _origin.y, .002f);
            Assert.IsTrue(_collision.Fits(traversal.Pose.Position, Vector3.up, Vector3.forward, ActorStance.Standing));
        }

        [Test]
        public void BakedVaultRejectsBodyObstructionAboveBarrier()
        {
            var traversal = BakedVault();
            Box(new Vector3(0f, 1.7f, .85f), new Vector3(3f, .2f, 2f));
            Assert.IsFalse(traversal.TryBegin(VaultPose()));
            Assert.IsFalse(traversal.Active);
            Assert.IsNotNull(traversal.Rejection);
        }

        [Test]
        public void BakedVaultCancelRetainsLastAcceptedPhaseAndPose()
        {
            var traversal = BakedVault();
            Assert.IsTrue(traversal.TryBegin(VaultPose()), traversal.Rejection);
            traversal.Tick(.4f);
            float progress = traversal.Progress;
            var position = traversal.Pose.Position;
            var motion = traversal.ActiveMotion;
            traversal.Cancel();
            traversal.Tick(1f);
            Assert.AreEqual(progress, traversal.Progress);
            Assert.AreEqual(position, traversal.Pose.Position);
            Assert.AreSame(motion, traversal.ActiveMotion);
            Assert.IsFalse(traversal.Active);
        }

        [Test]
        public void BakedVaultLateBlockRetainsLastAcceptedPhaseAndPose()
        {
            var traversal = BakedVault();
            Assert.IsTrue(traversal.TryBegin(VaultPose()), traversal.Rejection);
            traversal.Tick(.4f);
            float progress = traversal.Progress;
            var position = traversal.Pose.Position;
            Box(position - _origin + Vector3.up, new Vector3(2f, 2f, 2f));
            traversal.Tick(.1f);
            Assert.IsFalse(traversal.Active);
            Assert.AreEqual("Traversal path became blocked.", traversal.Rejection);
            Assert.AreEqual(position, traversal.Pose.Position);
            Assert.AreEqual(progress, traversal.Progress, "A rejected sweep must not advance the animation phase.");
        }

        [Test]
        public void SampledMotionCopiesFramesAndRejectsInvalidGeometry()
        {
            var frames = new[] {
                new ActorTraversalMotion.Frame { CapsuleA = Vector3.up, CapsuleB = Vector3.up * 1.4f, Radius = .15f },
                new ActorTraversalMotion.Frame { Root = Vector3.forward * 2f, CapsuleA = Vector3.up, CapsuleB = Vector3.up * 1.4f, Radius = .15f, PlanarFitWeight = 1f }
            };
            var motion = new ActorTraversalMotion(1f, Vector3.one, .2f, .5f, frames);
            frames[1].Root = Vector3.zero;
            Assert.AreEqual(Vector3.forward, motion.Sample(.5f).Root);
            frames[0].Radius = float.NaN;
            Assert.Throws<System.ArgumentException>(() => new ActorTraversalMotion(1f, Vector3.one, .2f, .5f, frames));
        }

        [Test]
        public void ExplicitBodySweepRejectsObstacleWithoutIgnoringSupport()
        {
            Box(new Vector3(0f, 1f, 1f), new Vector3(2f, 2f, .1f));
            var a = _origin + Vector3.up * .5f;
            var b = _origin + Vector3.up * 1.5f;
            Assert.IsTrue(_collision.FitsCapsule(a, b, .1f));
            Assert.IsFalse(_collision.ClearCapsuleSegment(a, b, .1f, a + Vector3.forward * 2f, b + Vector3.forward * 2f, .1f));
        }

        [Test]
        public void StepsPermitProgressButTallWallsDoNot()
        {
            var step = Box(new Vector3(0f, .1f, 2f), new Vector3(2f, .2f, 2f));
            var p = Pose().Position;
            for (int i = 0; i < 120; i++) p = _collision.Move(p, p + Vector3.forward * .02f, Vector3.up, Vector3.forward, true);
            Assert.Greater(p.z - _origin.z, 2f); Assert.Greater(p.y - _origin.y, .15f);
            Object.DestroyImmediate(step);
            Box(new Vector3(0f, .6f, 2f), new Vector3(2f, 1.2f, 2f));
            p = Pose().Position;
            for (int i = 0; i < 120; i++) p = _collision.Move(p, p + Vector3.forward * .02f, Vector3.up, Vector3.forward, true);
            Assert.Less(p.z - _origin.z, .85f); Assert.Less(p.y - _origin.y, .1f);
        }

        [TestCase(1.3f, ActorStance.Crouching)]
        [TestCase(.7f, ActorStance.Crawling)]
        public void LowerPostureFitsButStandingIsRejectedUnderCeiling(float clearance, ActorStance stance)
        {
            Box(new Vector3(0f, clearance + .1f, 0f), new Vector3(3f, .2f, 4f));
            Assert.IsTrue(_collision.TryStance(stance, Pose()));
            Assert.IsFalse(_collision.TryStance(ActorStance.Standing, Pose()));
            Assert.AreEqual(stance, _collision.Stance);
            Assert.IsTrue(_collision.TryStance(ActorStance.Standing, Pose(6f)));
        }

        ActorTraversal HangingTravel(float width = 3f)
        {
            Box(new Vector3(0f, 1.1f, 1.6f), new Vector3(width, 2.2f, 2f));
            var action = new ActorTraversal(_collision);
            action.HangRightMotion = Travel(.5f);
            action.HangLeftMotion = Travel(-.5f);
            Assert.IsTrue(action.TryBegin(Pose()), action.Rejection);
            action.Tick(1f);
            Assert.AreEqual(TraversalKind.Hanging, action.Kind);
            return action;
        }

        static ActorTraversalMotion Travel(float distance) => new ActorTraversalMotion(1f, Vector3.zero, 0f, 0f,
            new[] {
                new ActorTraversalMotion.Frame { Radius = .15f },
                new ActorTraversalMotion.Frame { Radius = .15f, Root = Vector3.right * distance, PlanarFitWeight = 1f }
            });

        [Test]
        public void HangingTravelMovesBothDirectionsAndStopsAtEnd()
        {
            var action = HangingTravel();
            Vector3 start = action.Pose.Position;
            Assert.IsTrue(action.MoveAlongLedge(1));
            Assert.IsFalse(action.MoveAlongLedge(-1), "Do not replace an unfinished support step.");
            action.Tick(1);
            Assert.AreEqual(TraversalKind.Hanging, action.Kind);
            Assert.That(action.Pose.Position.x - start.x, Is.EqualTo(.5f).Within(.001f));
            Assert.IsTrue(action.MoveAlongLedge(-1)); action.Tick(1);
            Assert.That(Vector3.Distance(action.Pose.Position, start), Is.LessThan(.001f));
            Assert.IsTrue(action.MoveAlongLedge(1)); action.Tick(1);
            Assert.IsTrue(action.MoveAlongLedge(1)); action.Tick(1);
            Vector3 end = action.Pose.Position;
            Assert.IsFalse(action.MoveAlongLedge(1));
            Assert.AreEqual(end, action.Pose.Position);
            Assert.AreEqual(TraversalKind.Hanging, action.Kind);
        }

        [Test]
        public void HangingTravelRejectsObstaclesAndDropsOnSupportLoss()
        {
            var action = HangingTravel();
            var obstacle = Box(new Vector3(.6f, 1f, .05f), new Vector3(.2f, 2f, .3f));
            Assert.IsFalse(action.MoveAlongLedge(1));
            Object.DestroyImmediate(obstacle); Physics.SyncTransforms();
            Assert.IsTrue(action.MoveAlongLedge(1));
            foreach (var collider in _root.GetComponentsInChildren<Collider>()) collider.enabled = false;
            action.Tick(.1f);
            Assert.IsFalse(action.Active);
        }

        [Test]
        public void LateObstacleStopsTravelWithoutDroppingOrTunnelling()
        {
            var action = HangingTravel();
            Assert.IsTrue(action.MoveAlongLedge(1));
            action.Tick(.1f);
            Box(new Vector3(.6f, 1f, .05f), new Vector3(.2f, 2f, .3f));
            action.Tick(2f);
            Assert.AreEqual(TraversalKind.Hanging, action.Kind);
            Assert.That(action.Pose.Position.x - _origin.x, Is.LessThan(.4f));
            Assert.AreEqual("Hanging travel became blocked.", action.Rejection);
            action.Cancel();
            Assert.IsFalse(action.Active);
        }

        ActorTraversal CornerTravel(float direction, bool inward = false)
        {
            var action = HangingTravel(3f);
            action.CornerMotions = System.Array.ConvertAll(new[] { "outward Left", "outward Right", "inward Left", "inward Right" },
                name => UnityEditor.AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(
                    "Assets/Art/Interactions/Animations/Ledge " + name + " motion.asset").CreateMotion());
            if (inward) Box(new Vector3(direction * 1.5f, 1.1f, -.4f), new Vector3(.5f, 2.2f, 2f));
            Assert.IsTrue(action.MoveAlongLedge(direction), action.Rejection); action.Tick(1f);
            if (!inward) { Assert.IsTrue(action.MoveAlongLedge(direction), action.Rejection); action.Tick(1f); }
            return action;
        }

        [TestCase(-1f)]
        [TestCase(1f)]
        public void OutsideCornerTurnsAndSupportsClimbOrDrop(float direction)
        {
            var action = CornerTravel(direction);
            Assert.IsTrue(action.MoveAlongLedge(direction), action.Rejection);
            Assert.IsTrue(action.TurningCorner);
            Vector3 previous = action.Pose.Position;
            for (int i = 0; i < 85; i++)
            {
                action.Tick(1f / 60f);
                Assert.Less(Vector3.Distance(previous, action.Pose.Position), .1f);
                previous = action.Pose.Position;
            }
            Assert.AreEqual(TraversalKind.Hanging, action.Kind, action.Rejection);
            Assert.Greater(Vector3.Dot(action.Pose.Forward, Vector3.left * direction), .99f);
            Assert.IsTrue(action.Climb(), action.Rejection);
            action.Cancel(); Assert.IsFalse(action.Active);
        }

        [TestCase(-1f)]
        [TestCase(1f)]
        public void InsideCornerCrossesSupportAndReverses(float direction)
        {
            var action = CornerTravel(direction, true);
            Assert.IsTrue(action.MoveAlongLedge(direction), action.Rejection);
            Assert.IsTrue(action.TurningCorner);
            action.Tick(2f);
            Assert.AreEqual(TraversalKind.Hanging, action.Kind, action.Rejection);
            Assert.Greater(Vector3.Dot(action.Pose.Forward, Vector3.right * direction), .99f);
            Assert.IsTrue(action.MoveAlongLedge(-direction), action.Rejection);
            Assert.IsTrue(action.TurningCorner);
            action.Tick(2f);
            Assert.AreEqual(TraversalKind.Hanging, action.Kind, action.Rejection);
            Assert.Greater(Vector3.Dot(action.Pose.Forward, Vector3.forward), .99f);
        }

        [Test]
        public void LateCornerObstacleReleasesAtLastSafePose()
        {
            var action = CornerTravel(1f);
            Assert.IsTrue(action.MoveAlongLedge(1f), action.Rejection);
            action.Tick(.3f);
            Vector3 before = action.Pose.Position; float phase = action.Progress;
            Box(before - _origin + Vector3.up, Vector3.one * 2f);
            action.Tick(.5f);
            Assert.IsFalse(action.Active);
            Assert.AreEqual(before, action.Pose.Position);
            Assert.AreEqual(phase, action.Progress);
            Assert.AreEqual("The corner path became blocked.", action.Rejection);
        }

        [Test]
        public void CornerSupportLossReleasesWithoutMoving()
        {
            var action = CornerTravel(1f);
            Assert.IsTrue(action.MoveAlongLedge(1f), action.Rejection);
            action.Tick(.2f); Vector3 before = action.Pose.Position;
            foreach (var collider in _root.GetComponentsInChildren<Collider>()) collider.enabled = false;
            action.Tick(.2f);
            Assert.IsFalse(action.Active); Assert.AreEqual(before, action.Pose.Position);
        }

        [Test]
        public void JumpGrabHangsThenClimbsOrDrops()
        {
            Box(new Vector3(0f, 1.1f, 1.6f), new Vector3(3f, 2.2f, 2f));
            var action = new ActorTraversal(_collision);
            Assert.IsTrue(action.TryBegin(Pose()), action.Rejection);
            Assert.AreEqual(TraversalKind.JumpGrab, action.Kind);
            for (int i = 0; i < 40; i++) action.Tick(.02f);
            Assert.AreEqual(TraversalKind.Hanging, action.Kind);
            Vector3 hanging = action.Pose.Position;
            action.Tick(1f); Assert.AreEqual(hanging, action.Pose.Position);
            Assert.IsTrue(action.Climb(), action.Rejection);
            for (int i = 0; i < 90; i++) action.Tick(.02f);
            Assert.IsFalse(action.Active);
            Assert.Greater(action.Pose.Position.y - _origin.y, 2.2f);
            Assert.IsTrue(action.TryBegin(Pose()), action.Rejection);
            for (int i = 0; i < 40; i++) action.Tick(.02f);
            action.Cancel(); Assert.IsFalse(action.Active);
        }

        [Test]
        public void VaultRejectsCeilingAndSupportRemovalCancels()
        {
            var wall = Box(new Vector3(0f, .5f, .8f), new Vector3(2f, 1f, .4f));
            var action = new ActorTraversal(_collision);
            var ceiling = Box(new Vector3(0f, 2f, .8f), new Vector3(3f, .2f, 3f));
            Assert.IsFalse(action.TryBegin(Pose()));
            Object.DestroyImmediate(ceiling); Physics.SyncTransforms();
            Assert.IsTrue(action.TryBegin(Pose()), action.Rejection);
            Assert.AreEqual(TraversalKind.Vault, action.Kind);
            Object.DestroyImmediate(wall); action.Tick(.02f);
            Assert.IsFalse(action.Active);
        }

        [Test]
        public void BasicVaultSoftensPhaseCornersAndCompletesWithClearance()
        {
            Box(new Vector3(0f, .5f, .8f), new Vector3(2f, 1f, .4f));
            var action = new ActorTraversal(_collision);
            Assert.IsTrue(action.TryBegin(Pose()), action.Rejection);
            Assert.AreEqual(TraversalKind.Vault, action.Kind);
            float elapsed = 0f;
            foreach (float corner in new[] { 1.25f * .35f, 1.25f * .65f })
            {
                action.Tick(corner - .01f - elapsed);
                Vector3 before = action.Pose.Position;
                action.Tick(.01f);
                Vector3 at = action.Pose.Position;
                action.Tick(.01f);
                Vector3 after = action.Pose.Position;
                Assert.IsTrue(action.Active, action.Rejection);
                Vector3 enteringVelocity = (at - before) / .01f, leavingVelocity = (after - at) / .01f;
                Assert.Greater(enteringVelocity.magnitude, .3f, "The body must keep moving across the airborne phase boundary.");
                Assert.Less(Vector3.Distance(enteringVelocity, leavingVelocity), .25f, "Velocity must remain continuous at the phase boundary.");
                elapsed = corner + .01f;
            }
            action.Tick(2f);
            Assert.IsFalse(action.Active);
            Assert.IsNull(action.Rejection);
            Assert.Greater(action.Pose.Position.z - _origin.z, 1.5f);
            Assert.AreEqual(.04f, action.Pose.Position.y - _origin.y, .002f);
            Assert.IsTrue(_collision.Fits(action.Pose.Position, Vector3.up, Vector3.forward, ActorStance.Standing));
        }

        [Test]
        public void CompletedStepRetainsGroundSupportOnMotorHandoff()
        {
            Box(new Vector3(0f, .375f, 1.6f), new Vector3(3f, .75f, 2f));
            var action = new ActorTraversal(_collision);
            Assert.IsTrue(action.TryBegin(Pose()), action.Rejection);
            for (int i = 0; i < 60; i++) action.Tick(.02f);
            var ground = new Ground(_origin);
            var motor = new SurfaceCharacterController(ground, ground, 0f, action.Pose, collision: _collision);
            for (int i = 0; i < 10; i++)
            {
                motor.Tick(Vector2.zero, Vector3.forward, 0f, .02f);
                Assert.IsTrue(motor.Grounded, "Traversal support must transfer without a false fall.");
            }
        }

        [Test]
        public void DescendingSmallStepRetainsSupportWithoutAirborneFrames()
        {
            Box(new Vector3(0f, .1f, 0f), new Vector3(3f, .2f, 2f));
            var ground = new Ground(_origin);
            var motor = new SurfaceCharacterController(ground, ground, 0f,
                new CharacterPose(_origin + new Vector3(0f, .225f, .5f), Vector3.up, Vector3.forward), collision: _collision);
            for (int i = 0; i < 35; i++)
            {
                motor.Tick(Vector2.up, Vector3.forward, 2f, .02f);
                Assert.IsTrue(motor.Grounded, "A 0.2 metre downstep must retain support. Frame " + i);
                Assert.IsFalse(motor.Jumping);
            }
            Assert.Greater(motor.Pose.Position.z - _origin.z, 1.5f);
            Assert.Less(motor.Pose.Position.y - _origin.y, .06f);
        }

        [Test]
        public void DescendingLargeStepEntersPhysicalFall()
        {
            Box(new Vector3(0f, .3f, 0f), new Vector3(3f, .6f, 2f));
            var ground = new Ground(_origin);
            var motor = new SurfaceCharacterController(ground, ground, 0f,
                new CharacterPose(_origin + new Vector3(0f, .625f, .5f), Vector3.up, Vector3.forward), collision: _collision);
            bool fell = false;
            for (int i = 0; i < 35; i++)
            {
                motor.Tick(Vector2.up, Vector3.forward, 2f, .02f);
                fell |= !motor.Grounded;
                Assert.IsFalse(motor.Jumping);
            }
            Assert.IsTrue(fell, "A 0.6 metre drop must not snap down as a supported stair step.");
        }

        [Test]
        public void IntentionalJumpLeavesNearbyStepSupport()
        {
            Box(new Vector3(0f, .1f, 0f), new Vector3(3f, .2f, 2f));
            var ground = new Ground(_origin);
            Vector3 start = _origin + new Vector3(0f, .225f, .5f);
            var motor = new SurfaceCharacterController(ground, ground, 0f,
                new CharacterPose(start, Vector3.up, Vector3.forward), collision: _collision);
            motor.Tick(Vector2.up, Vector3.forward, 2f, .02f, true);
            Assert.IsTrue(motor.Jumping);
            Assert.IsFalse(motor.Grounded);
            Assert.Greater(motor.Pose.Position.y, start.y + .08f);
        }

        [Test]
        public void OneMetreStepRangeIsConsistentAcrossBlockApproaches()
        {
            var block = Box(new Vector3(0f, .45f, 0f), new Vector3(3f, .9f, 3f));
            foreach (var forward in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            foreach (float groundHeight in new[] { .025f, .10f })
            {
                var pose = new CharacterPose(_origin - forward * 2f + Vector3.up * groundHeight, Vector3.up, forward);
                var action = new ActorTraversal(_collision);
                Assert.IsTrue(action.TryBegin(pose), action.Rejection);
                Assert.AreEqual(TraversalKind.StepUp, action.Kind, "Ordinary approach-height variation must stay within the basic step motion.");
                Assert.Less(Vector3.Distance(action.Edge, block.GetComponent<Collider>().ClosestPoint(action.Edge)), .001f);
            }
        }

        sealed class Ground : IGroundingProvider, IGravityProvider
        {
            readonly Vector3 _origin;
            public Ground(Vector3 origin) => _origin = origin;
            public bool TryGetGravity(Vector3 position, out Vector3 acceleration) { acceleration = Vector3.down * 9.81f; return true; }
            public bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult result)
            { result = new GroundResult(new Vector3(position.x, _origin.y + offset, position.z), Vector3.up); return true; }
        }
    }
}
