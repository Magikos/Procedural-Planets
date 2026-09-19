using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorLadderTests
    {
        GameObject _root;
        AnimationClip _clip;
        ActorLadder _ladder;
        ActorAnimationPerformance _performance;
        readonly Vector3 _origin = new(1200f, 1200f, 1200f);
        Vector3 Bottom => _origin + Vector3.up * .025f;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Ladder authority fixture"); _root.transform.position = _origin;
            Box(new Vector3(0f, -.1f, 0f), new Vector3(5f, .2f, 5f));
            _clip = new AnimationClip();
            _clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0f, 1f, 0f));
            var entry = new ActorAnimationPerformanceLibrary.Entry { Id = "test.ladder", Action = "Ladder", RigFamily = "Humanoid",
                Phases = new[] { "MountBottom", "MountTop", "Up", "Down", "Idle", "ExitBottom", "ExitTop", "ExitTopMirrored", "ExitBottomMirrored", "ApproachStep", "SlideStart", "Slide", "SlideEnd", "SprintUp", "SprintUpRight", "SprintTop" }
                    .Select(name => new ActorAnimationPerformanceLibrary.Phase { Name = name, Clip = _clip }).ToArray() };
            _performance = new ActorAnimationPerformanceLibraryData(new[] { entry })[0];
            _ladder = new ActorLadder(new ActorCollision(1 << 0));
        }

        [TearDown]
        public void TearDown() { UnityEngine.Object.DestroyImmediate(_root); UnityEngine.Object.DestroyImmediate(_clip); }

        GameObject Box(Vector3 position, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.transform.SetParent(_root.transform, false);
            box.transform.localPosition = position; box.transform.localScale = size; Physics.SyncTransforms(); return box;
        }

        bool Begin(float height, float spacing, bool top = false)
        {
            Vector3 position = top ? Bottom + Vector3.up * height + Vector3.forward * .95f : Bottom - Vector3.forward * .25f;
            return _ladder.Begin(new CharacterPose(position, Vector3.up, Vector3.forward), Bottom, Vector3.up,
                Vector3.forward, height, spacing, top, _performance);
        }

        [TestCase(2.55f, .3f)]
        [TestCase(3.75f, .25f)]
        public void MountBothEndsStopReverseAndDismount(float height, float spacing)
        {
            Box(new Vector3(0f, height - .1f, 1.2f), new Vector3(2f, .2f, 1.5f));
            Assert.IsTrue(Begin(height, spacing), _ladder.Rejection);
            for (int i = 0; i < 55; i++) _ladder.Tick(0f, .02f);
            Assert.AreEqual(ActorLadderPhase.Climbing, _ladder.Phase);
            for (int i = 0; i < 35; i++) _ladder.Tick(1f, .02f);
            for (int i = 0; i < 15; i++) _ladder.Tick(0f, .02f);
            Vector3 stopped = _ladder.Pose.Position;
            for (int i = 0; i < 20; i++) _ladder.Tick(0f, .02f);
            Assert.That(Vector3.Distance(stopped, _ladder.Pose.Position), Is.LessThan(.0001f));
            Assert.AreEqual("Idle", _ladder.AnimationPhase);
            for (int i = 0; i < 15; i++) _ladder.Tick(-1f, .02f);
            Assert.Less(_ladder.ClimbHeight, Vector3.Dot(stopped - Bottom, Vector3.up));
            for (int i = 0; i < 600 && _ladder.Active; i++) _ladder.Tick(1f, .02f);
            Assert.IsFalse(_ladder.Active, _ladder.Rejection);
            Assert.That(Vector3.Distance(_ladder.Pose.Position, Bottom + Vector3.up * height + Vector3.forward * .95f), Is.LessThan(.001f));
            Assert.IsTrue(Begin(height, spacing, true), _ladder.Rejection);
            for (int i = 0; i < 600 && _ladder.Active; i++) _ladder.Tick(-1f, .02f);
            Assert.IsFalse(_ladder.Active, _ladder.Rejection);
            Assert.That(Vector3.Distance(_ladder.Pose.Position, Bottom - Vector3.forward * .25f), Is.LessThan(.001f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BlockedTopExitKeepsReverseAndReleaseAvailable(bool fast)
        {
            const float height = 2.55f;
            Box(new Vector3(0f, height - .1f, 1.2f), new Vector3(2f, .2f, 1.5f));
            Box(new Vector3(0f, height + .9f, 1.1f), new Vector3(1f, 1.8f, .8f));
            Assert.IsTrue(Begin(height, .3f));
            for (int i = 0; i < 400; i++) _ladder.Tick(1f, .02f, fast);
            Assert.AreEqual(ActorLadderPhase.Climbing, _ladder.Phase);
            Assert.IsNotNull(_ladder.Rejection);
            Assert.AreEqual("Idle", _ladder.AnimationPhase, "A blocked exit must hold the authored idle instead of freezing a moving gait.");
            float stoppedHeight = _ladder.ClimbHeight;
            for (int i = 0; i < 25; i++) _ladder.Tick(-1f, .02f, fast);
            Assert.Less(_ladder.ClimbHeight, stoppedHeight);
            CharacterPose before = _ladder.Pose;
            _ladder.Cancel();
            Assert.IsFalse(_ladder.Active);
            Assert.AreEqual(before.Position, _ladder.Tick(0f, .02f).Position, "Release must return the displayed authority pose without teleporting.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InPlaceFinalStepAndMotorReconstructTheAuthoredBodyTrajectory(bool footIK)
        {
            const string folder = "Assets/Art/Characters/";
            // These motion assets are baked for the review scene's converted rider.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Art/Characters/Human/Converted/Rider_01_v2/Rider_01_Fit.prefab");
            var original = AssetDatabase.LoadAllAssetsAtPath(folder + "Animations/Ladder/Ladder Final Step.FBX")
                .OfType<AnimationClip>().First(clip => !clip.name.StartsWith("__", StringComparison.Ordinal));
            var adapted = AssetDatabase.LoadAssetAtPath<AnimationClip>(folder + "Animations/Ladder/Ladder Final Step.anim");
            var asset = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(folder + "Motion/Ladder ApproachStep Motion.asset");
            Assert.IsNotNull(adapted); Assert.IsNotNull(asset, "Bake the final step before testing source reconstruction.");
            var motion = asset.CreateMotion();
            // Editor helpers live outside this runtime test assembly's references.
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("AuthoredAnimationReference")).First(value => value != null);
            using var source = (IDisposable)Activator.CreateInstance(type, prefab, original, false);
            using var production = (IDisposable)Activator.CreateInstance(type, prefab, adapted, false);
            var sample = type.GetMethod("Sample");
            var playableField = type.GetField("_playable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            foreach (var reference in new[] { source, production })
            {
                var playable = (UnityEngine.Animations.AnimationClipPlayable)playableField.GetValue(reference);
                playable.SetApplyFootIK(footIK);
                sample.Invoke(reference, new object[] { 0d });
            }
            var sourceAnimator = (Animator)type.GetProperty("Animator").GetValue(source);
            var productionAnimator = (Animator)type.GetProperty("Animator").GetValue(production);
            Vector3 origin = sourceAnimator.GetBoneTransform(HumanBodyBones.Hips).position -
                productionAnimator.GetBoneTransform(HumanBodyBones.Hips).position;
            foreach (float phase in Enumerable.Range(0, 121).Select(index => index / 120f))
            {
                sample.Invoke(source, new object[] { (double)(original.length * phase) });
                sample.Invoke(production, new object[] { (double)(adapted.length * phase) });
                foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftFoot,
                    HumanBodyBones.RightFoot, HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                {
                    Vector3 expected = sourceAnimator.GetBoneTransform(bone).position - origin;
                    Vector3 actual = productionAnimator.GetBoneTransform(bone).position + motion.Sample(phase).Root;
                    Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.002f),
                        $"{bone} at {phase:F3}: body-relative source motion plus motor travel must reconstruct authored world motion.");
                }
            }
        }

        [Test]
        public void AuthoredApproachStepTransfersContinuouslyIntoMountAndCanRelease()
        {
            ActorTraversalMotion Motion(Vector3 end, bool elevated) => new(1f, Vector3.zero, .15f, .15f, new[] {
                new ActorTraversalMotion.Frame { CapsuleA = Vector3.up * .9f, CapsuleB = Vector3.up * 1.4f, Radius = .14f },
                new ActorTraversalMotion.Frame { Root = end, CapsuleA = Vector3.up * .9f, CapsuleB = Vector3.up * 1.4f, Radius = .14f, PlanarFitWeight = 1f }
            }, elevatedLanding: elevated);
            var step = Motion(Vector3.forward * .2f, false);
            var mount = Motion(Vector3.up * .35f, true);
            var entrance = new CharacterPose(Bottom - Vector3.forward * .2f - Vector3.up * .05f, Vector3.up, Vector3.forward);
            Assert.IsTrue(_ladder.Begin(entrance, Bottom, Vector3.up, Vector3.forward, 2.55f, .3f, false,
                _performance, bottomMount: mount, approachStep: step), _ladder.Rejection);
            Assert.AreEqual(ActorLadderPhase.ApproachStep, _ladder.Phase);
            _ladder.Tick(0f, .5f);
            Assert.That(Vector3.Distance(_ladder.Pose.Position, entrance.Position + Vector3.forward * .1f), Is.LessThan(.001f));
            _ladder.Cancel();
            Assert.IsFalse(_ladder.Active);
            Assert.IsTrue(_ladder.Begin(entrance, Bottom, Vector3.up, Vector3.forward, 2.55f, .3f, false,
                _performance, bottomMount: mount, approachStep: step), _ladder.Rejection);
            _ladder.Tick(0f, 1.01f);
            Assert.AreEqual(ActorLadderPhase.MountBottom, _ladder.Phase);
            Assert.That(Vector3.Distance(_ladder.Pose.Position, Bottom - Vector3.up * .05f), Is.LessThan(.01f),
                "The ground step must preserve the motor's standing height until the mount begins.");
            _ladder.Tick(0f, 1f);
            Assert.AreEqual(ActorLadderPhase.Climbing, _ladder.Phase);
            Assert.That(_ladder.ClimbHeight, Is.EqualTo(.35f).Within(.001f));
        }

        [Test]
        public void NativeBottomMountRetainsSourceDipAndFinishesAboveGround()
        {
            var frames = new[] {
                new ActorTraversalMotion.Frame { CapsuleA = Vector3.up * .9f, CapsuleB = Vector3.up * 1.4f, Radius = .14f },
                new ActorTraversalMotion.Frame { Root = new Vector3(0f, -.02f, -.04f), CapsuleA = Vector3.up * .9f, CapsuleB = Vector3.up * 1.4f, Radius = .14f, PlanarFitWeight = .5f },
                new ActorTraversalMotion.Frame { Root = new Vector3(0f, .35f, -.02f), CapsuleA = Vector3.up * .9f, CapsuleB = Vector3.up * 1.4f, Radius = .14f, PlanarFitWeight = 1f } };
            var motion = new ActorTraversalMotion(1f, Vector3.zero, .15f, .15f, frames, elevatedLanding: true);
            var entrance = new CharacterPose(Bottom + Vector3.forward * .02f, Vector3.up, Vector3.forward);
            Assert.IsTrue(_ladder.Begin(entrance, Bottom, Vector3.up, Vector3.forward, 2.55f, .3f, false, _performance, bottomMount: motion), _ladder.Rejection);
            _ladder.Tick(0f, .5f);
            Assert.That(Vector3.Distance(_ladder.Pose.Position, entrance.Position + frames[1].Root), Is.LessThan(.001f));
            _ladder.Tick(0f, .51f);
            Assert.AreEqual(ActorLadderPhase.Climbing, _ladder.Phase);
            Assert.That(_ladder.ClimbHeight, Is.EqualTo(.35f).Within(.001f));
            Assert.That(Vector3.Dot(_ladder.Pose.Position - Bottom, Vector3.forward), Is.EqualTo(0f).Within(.001f));
        }

        [Test]
        public void BlockedBottomExitKeepsTheClimbRouteClearForReversal()
        {
            Assert.IsTrue(Begin(2.55f, .3f));
            for (int i = 0; i < 90; i++) _ladder.Tick(1f, .02f);
            Assert.Greater(_ladder.ClimbHeight, .2f);
            Box(new Vector3(0f, .925f, -.45f), new Vector3(.8f, 1.8f, .1f));
            for (int i = 0; i < 150; i++) _ladder.Tick(-1f, .02f);
            Assert.AreEqual(ActorLadderPhase.Climbing, _ladder.Phase);
            Assert.That(_ladder.ClimbHeight, Is.LessThan(.001f), "The blocker must leave the vertical rung route clear.");
            StringAssert.Contains("blocked", _ladder.Rejection);
            for (int i = 0; i < 30; i++) _ladder.Tick(0f, .02f);
            Assert.AreEqual("Idle", _ladder.AnimationPhase);
            for (int i = 0; i < 30; i++) _ladder.Tick(1f, .02f);
            Assert.Greater(_ladder.ClimbHeight, .2f);
        }

        [Test]
        public void NativeTopMountPreservesItsLateralArcAndTurnAndRejectsShortGeometry()
        {
            var frames = new[] {
                new ActorTraversalMotion.Frame { Root = Vector3.zero, RootYawDegrees = 180f, CapsuleA = Vector3.up * .9f, CapsuleB = Vector3.up * 1.4f, Radius = .14f },
                new ActorTraversalMotion.Frame { Root = new Vector3(.3f, -.6f, -.5f), RootYawDegrees = 90f, CapsuleA = Vector3.up * .9f, CapsuleB = Vector3.up * 1.4f, Radius = .14f, PlanarFitWeight = .5f },
                new ActorTraversalMotion.Frame { Root = new Vector3(0f, -1.85f, -.9f), RootYawDegrees = 0f, CapsuleA = Vector3.up * .9f, CapsuleB = Vector3.up * 1.4f, Radius = .14f, PlanarFitWeight = 1f } };
            var motion = new ActorTraversalMotion(1f, Vector3.zero, .15f, .15f, frames, elevatedLanding: true);
            CharacterPose Entrance(float height) => new(Bottom + Vector3.up * height + Vector3.forward * .9f, Vector3.up, Vector3.back);
            Assert.IsFalse(_ladder.Begin(Entrance(1.8f), Bottom, Vector3.up, Vector3.forward, 1.8f, .3f, true, _performance, topMount: motion));
            StringAssert.Contains("too short", _ladder.Rejection);
            var entrance = Entrance(2.55f);
            Assert.IsTrue(_ladder.Begin(entrance, Bottom, Vector3.up, Vector3.forward, 2.55f, .3f, true, _performance, topMount: motion), _ladder.Rejection);
            _ladder.Tick(0f, .5f);
            Assert.That(Vector3.Distance(_ladder.Pose.Position, entrance.Position + frames[1].Root), Is.LessThan(.001f));
            Assert.That(Vector3.Angle(_ladder.Pose.Forward, Vector3.right), Is.LessThan(.1f));
        }

        [Test]
        public void ObstacleAddedDuringMountReleasesAtTheLastClearPose()
        {
            Assert.IsTrue(Begin(2.55f, .3f));
            _ladder.Tick(0f, .2f);
            CharacterPose before = _ladder.Pose;
            Box(before.Position - _origin + Vector3.up * .9f, new Vector3(.5f, 1.8f, .5f));
            _ladder.Tick(0f, .02f);
            Assert.IsFalse(_ladder.Active);
            Assert.AreEqual(before.Position, _ladder.Pose.Position);
            StringAssert.Contains("blocked", _ladder.Rejection);
        }

        [Test]
        public void DistantMountAndInvalidGeometryCannotMoveTheActor()
        {
            var pose = new CharacterPose(Bottom - Vector3.forward * 2f, Vector3.up, Vector3.forward);
            Assert.IsFalse(_ladder.Begin(pose, Bottom, Vector3.up, Vector3.forward, 3f, .3f, false, _performance));
            Assert.IsFalse(_ladder.Active);
            Assert.Throws<ArgumentException>(() => _ladder.Begin(pose, Bottom, Vector3.up, Vector3.forward, 3f, 0f, false, _performance));
        }

        [TestCase(ActorLadderPhase.MountBottom)]
        [TestCase(ActorLadderPhase.MountTop)]
        [TestCase(ActorLadderPhase.ExitBottom)]
        [TestCase(ActorLadderPhase.ExitTop)]
        public void FastTransfersKeepTheAuthoredRouteAndFinishSooner(ActorLadderPhase phase)
        {
            const float height = 2.55f;
            Box(new Vector3(0f, height - .1f, 1.2f), new Vector3(2f, .2f, 1.5f));
            var bottomMount = Gait(0f, .4f); var topMount = Gait(0f, -.4f);
            ActorTraversalMotion Exit(bool top) => new ActorTraversalMotion(1f, Vector3.zero, .15f, .15f,
                (top ? new[] { Vector3.zero, Vector3.up * .4f, Vector3.up * .4f + Vector3.forward * .95f } :
                    new[] { Vector3.zero, Vector3.down * .4f, Vector3.down * .4f - Vector3.forward * .25f })
                .Select((root, index) => new ActorTraversalMotion.Frame { Root = root, CapsuleA = Vector3.up * .4f,
                    CapsuleB = Vector3.up * 1.2f, Radius = .1f, PlanarFitWeight = index / 2f }).ToArray(), elevatedLanding: true);
            var topExit = Exit(true); var bottomExit = Exit(false);
            var motion = phase == ActorLadderPhase.MountBottom ? bottomMount : phase == ActorLadderPhase.MountTop ? topMount :
                phase == ActorLadderPhase.ExitTop ? topExit : bottomExit;
            (float elapsed, Vector3 end) Run(bool fast)
            {
                var actor = new ActorLadder(new ActorCollision(1 << 0));
                bool top = phase == ActorLadderPhase.MountTop;
                var initial = new CharacterPose(Bottom + (top ? Vector3.up * height : Vector3.zero), Vector3.up, Vector3.forward);
                Assert.IsTrue(actor.Begin(initial, Bottom, Vector3.up, Vector3.forward, height, .3f, top, _performance,
                    topExit: topExit, topMount: topMount, bottomMount: bottomMount, bottomExit: bottomExit, sprintPlaybackRate: 2f), actor.Rejection);
                if (phase == ActorLadderPhase.ExitTop || phase == ActorLadderPhase.ExitBottom)
                {
                    var start = Bottom + Vector3.up * (phase == ActorLadderPhase.ExitTop ? height - .4f : .4f);
                    typeof(ActorLadder).GetProperty("Pose").SetValue(actor, new CharacterPose(start, Vector3.up, Vector3.forward));
                    var method = typeof(ActorLadder).GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    Assert.IsTrue((bool)method.Invoke(actor, new object[] { phase, start + motion.Sample(1f).Root, false }), actor.Rejection);
                }
                Vector3 origin = actor.Pose.Position;
                float elapsed = 0f;
                while (actor.Phase == phase && elapsed < 2f)
                {
                    actor.Tick(0f, .01f, fast); elapsed += .01f;
                    if (actor.Phase == phase)
                        Assert.That(Vector3.Distance(actor.Pose.Position, origin + motion.Sample(actor.AnimationProgress).Root), Is.LessThan(.001f));
                }
                Assert.AreNotEqual(phase, actor.Phase, actor.Rejection);
                return (elapsed, actor.Pose.Position);
            }
            var normal = Run(false); var quick = Run(true);
            Assert.That(quick.elapsed, Is.LessThan(normal.elapsed * .7f));
            Assert.That(Vector3.Distance(normal.end, quick.end), Is.LessThan(.001f));
        }

        [Test]
        public void ReleasingFastIntentPreservesTransferProgressAndNormalGaitClock()
        {
            Assert.IsTrue(_ladder.Begin(new CharacterPose(Bottom, Vector3.up, Vector3.forward), Bottom, Vector3.up,
                Vector3.forward, 5f, .3f, false, _performance, bottomMount: Gait(0f, .4f),
                upGait: Gait(0f, .7f), downGait: Gait(0f, -.7f), sprintPlaybackRate: 2f));
            _ladder.Tick(0f, .2f, fast: true);
            float phase = _ladder.AnimationProgress, rate = _ladder.EffectivePlaybackRate;
            _ladder.Tick(0f, .01f, fast: false);
            Assert.That(_ladder.AnimationProgress, Is.GreaterThan(phase));
            Assert.That(_ladder.AnimationProgress - phase, Is.LessThanOrEqualTo(rate * .01f + .00001f));
            Assert.That(_ladder.EffectivePlaybackRate, Is.LessThan(rate));
            _ladder.Tick(0f, 1f);
            for (int i = 0; i < 30; i++) _ladder.Tick(1f, .01f, fast: true);
            Assert.That(_ladder.EffectivePlaybackRate, Is.EqualTo(2f).Within(.00001f));
            float before = _ladder.SourceCycleTravel, startHeight = _ladder.ClimbHeight;
            _ladder.Tick(1f, .1f, fast: true);
            Assert.That(_ladder.SourceCycleTravel - before, Is.EqualTo(.2f).Within(.0001f));
            Assert.That(_ladder.ClimbHeight - startHeight, Is.EqualTo(.12f).Within(.001f));
            for (int i = 0; i < 30; i++) _ladder.Tick(1f, .01f);
            Assert.That(_ladder.EffectivePlaybackRate, Is.EqualTo(1f).Within(.00001f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FullSprintAscentUsesItsNativeClockAndValidatesTheCompleteExit(bool blocked)
        {
            const float height = 2.55f;
            Box(new Vector3(0f, height - .1f, 1.2f), new Vector3(2f, .2f, 1.5f));
            if (blocked) Box(new Vector3(0f, height + .6f, .9f), new Vector3(1f, .5f, .5f));
            var motion = new ActorTraversalMotion(1f, Vector3.zero, .15f, .15f,
                new[] { Vector3.zero, Vector3.up * height, Vector3.up * height + Vector3.forward * 1.15f }
                    .Select((root, index) => new ActorTraversalMotion.Frame { Root = root, CapsuleA = Vector3.up * .4f,
                        CapsuleB = Vector3.up * 1.2f, Radius = .1f, PlanarFitWeight = index / 2f }).ToArray(), elevatedLanding: true);
            Vector3 approach = Vector3.back * .2f;
            bool began = _ladder.Begin(new CharacterPose(Bottom + approach, Vector3.up, Vector3.forward), Bottom,
                Vector3.up, Vector3.forward, height, .3f, false, _performance, sprintPlaybackRate: 2f,
                sprintTop: motion, sprintFromBottom: true, sprintApproachOffset: approach);
            if (blocked)
            {
                Assert.IsFalse(began);
                Assert.AreEqual("The ladder mount or exit is blocked.", _ladder.Rejection);
                Assert.IsFalse(_ladder.Active);
                return;
            }
            Assert.IsTrue(began, _ladder.Rejection);
            Assert.AreEqual(ActorLadderPhase.SprintTop, _ladder.Phase);
            _ladder.Tick(1f, .4f, fast: true);
            Assert.That(_ladder.AnimationProgress, Is.EqualTo(.4f).Within(.0001f));
            Assert.AreEqual(1f, _ladder.EffectivePlaybackRate);
            Assert.That(Vector3.Distance(_ladder.Pose.Position, Bottom + approach + motion.Sample(.4f).Root), Is.LessThan(.001f));
            _ladder.Tick(0f, .7f);
            Assert.IsFalse(_ladder.Active);
            Assert.That(Vector3.Distance(_ladder.Pose.Position, Bottom + approach + motion.Sample(1f).Root), Is.LessThan(.001f));
            Assert.IsFalse(_ladder.Begin(new CharacterPose(Bottom + approach, Vector3.up, Vector3.forward), Bottom,
                Vector3.up, Vector3.forward, height + .3f, .3f, false, _performance,
                sprintTop: motion, sprintFromBottom: true, sprintApproachOffset: approach), "An incompatible ladder must not stretch the authored full ascent.");
        }

        [Test]
        public void ReleasedLadderPoseFallsWithoutGroundedSupportSnapping()
        {
            var provider = new FlatGround(_origin.y);
            var pose = new CharacterPose(_origin + Vector3.up * .08f, Vector3.up, Vector3.forward);
            var motor = new SurfaceCharacterController(provider, provider, 0f, pose, collision: new ActorCollision(1 << 0));
            motor.ResetPose(pose, grounded: false);
            var next = motor.Tick(Vector2.zero, Vector3.forward, 0f, .02f, false, false);
            Assert.IsFalse(motor.Grounded);
            Assert.That(Vector3.Distance(next.Position, pose.Position), Is.InRange(.001f, .005f),
                "Release must integrate gravity instead of using the larger grounded support tolerance.");
        }

        [Test]
        public void AuthoredContactCacheIgnoresCorrectionsAndFollowsTheActorFrame()
        {
            var bones = new Transform[3];
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = new GameObject("Contact joint " + i).transform;
                bones[i].SetParent(i == 0 ? _root.transform : bones[i - 1], false);
                bones[i].localPosition = i == 0 ? Vector3.up : Vector3.forward * .5f;
            }
            var rig = _root.AddComponent<ProceduralRigDefinition>();
            rig.Interactions = new[] { new InteractionLimbDefinition { Id = "Contact", Bones = bones } };
            var pose = new ProceduralPoseRig(_root.transform, rig);
            Assert.IsTrue(pose.TryGetAuthoredInteractionContact("Contact", out Vector3 authored));
            bones[2].position += Vector3.right * .1f;
            Assert.IsTrue(pose.TryGetAuthoredInteractionContact("Contact", out Vector3 retained));
            Assert.AreEqual(authored, retained, "A final correction must not become the next authored contact target.");
            Vector3 movement = new(.2f, .3f, .4f);
            _root.transform.position += movement;
            pose.TryGetAuthoredInteractionContact("Contact", out Vector3 moved);
            Assert.That(Vector3.Distance(moved, authored + movement), Is.LessThan(.0002f));
        }

        [TestCase(2.55f)]
        [TestCase(2.6f)]
        public void RungContactIncludesTheTopPlatformEdge(float height)
        {
            var ladder = _root.AddComponent<LadderInteraction>();
            ladder.Height = height; ladder.RungSpacing = .3f; ladder.RungOffset = .15f;
            Vector3 contact = ladder.NearestRungContact(_root.transform.TransformPoint(new Vector3(0f, 3f, .1f)));
            Assert.That(_root.transform.InverseTransformPoint(contact).y, Is.EqualTo(height).Within(.0001f));
            contact = ladder.NearestRungContact(_root.transform.TransformPoint(new Vector3(0f, 2.55f, .1f)));
            Assert.That(_root.transform.InverseTransformPoint(contact).y, Is.EqualTo(2.55f).Within(.0001f));
        }

        [Test]
        public void RungSupportAnchorsOnlySlowAuthoredContactsAndBoundsCorrections()
        {
            var ladder = _root.AddComponent<LadderInteraction>();
            var bones = new Transform[3];
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = new GameObject("Ladder support joint " + i).transform;
                bones[i].SetParent(i == 0 ? _root.transform : bones[i - 1], false);
                bones[i].localPosition = i == 0 ? new Vector3(0f, .55f, .02f) : Vector3.up * .4f;
            }
            var rig = _root.AddComponent<ProceduralRigDefinition>();
            rig.Interactions = new[] { "LeftHand", "RightHand", "LadderLeftFoot", "LadderRightFoot" }
                .Select(id => new InteractionLimbDefinition { Id = id, Bones = bones }).ToArray();
            var pose = new ProceduralPoseRig(_root.transform, rig);
            ladder.ApplyContacts(pose, true, .02f);
            Assert.IsFalse(ladder.ContactAnchored("LeftHand"), "One sample cannot identify a support interval.");
            ladder.ApplyContacts(pose, true, .02f);
            Assert.IsFalse(ladder.ContactAnchored("LadderLeftFoot"), "One slow apex sample must not create a foot plant.");
            bones[2].position += Vector3.up * .02f;
            pose.CaptureAnimation(); ladder.ApplyContacts(pose, true, .02f);
            for (int i = 0; i < 3; i++) ladder.ApplyContacts(pose, true, .02f);
            Assert.IsFalse(ladder.ContactAnchored("LadderLeftFoot"));
            bones[2].position -= Vector3.up * .02f;
            pose.CaptureAnimation(); ladder.ApplyContacts(pose, true, .02f);
            for (int i = 0; i < 4; i++) ladder.ApplyContacts(pose, true, .02f);
            Assert.IsFalse(ladder.ContactAnchored("LadderLeftFoot"), "Moving between candidate plants must reset the stability interval.");
            for (int i = 0; i < 2; i++) ladder.ApplyContacts(pose, true, .02f);
            Assert.IsTrue(ladder.ContactAnchored("LadderLeftFoot"), "Sustained near-rung support must still acquire.");
            Assert.IsTrue(ladder.ContactAnchored("LeftHand"));
            for (int i = 0; i < 14; i++)
            {
                bones[2].position += Vector3.up * .004f;
                pose.CaptureAnimation(); ladder.ApplyContacts(pose, true, .02f);
                Assert.That(ladder.MaxRequestedCorrection, Is.LessThanOrEqualTo(.06001f));
            }
            Assert.IsTrue(ladder.ContactAnchored("LeftHand"), "Small spacing drift retains the same rung anchor.");
            bones[2].position += Vector3.up * .02f;
            pose.CaptureAnimation(); ladder.ApplyContacts(pose, true, .02f);
            Assert.IsFalse(ladder.ContactAnchored("LeftHand"), "An authored swing must release the rung.");
            ladder.ApplyContacts(pose, false, .02f);
            Assert.AreEqual(0f, ladder.MaxRequestedCorrection);
        }

        [Test]
        public void ProductionAdapterRejectsMismatchedMotionBeforeApproaching()
        {
            const string folder = "Assets/Art/Characters/";
            var target = _root.AddComponent<LadderInteraction>();
            target.TopExitMotion = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(folder + "Motion/Ladder ExitTop Motion.asset");
            target.BottomMountMotion = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(folder + "Motion/Ladder MountBottom Motion.asset");
            target.TopMountMotion = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(folder + "Motion/Ladder MountTop Motion.asset"));
            var actorObject = new GameObject("Invalid ladder actor"); actorObject.SetActive(false); actorObject.transform.SetParent(_root.transform, false);
            var actor = actorObject.AddComponent<HumanoidAnimationPrototype>();
            actor.LadderPerformances = AssetDatabase.LoadAssetAtPath<ActorAnimationPerformanceLibrary>(folder + "Motion/Ladder Performances.asset");
            try
            {
                target.TopMountMotion.Clip = target.TopExitMotion.Clip;
                Assert.IsFalse(actor.TryUseLadder(target, true));
                StringAssert.Contains("must match", actor.LadderStatus);
                Assert.IsFalse(actor.LadderApproaching);
            }
            finally { UnityEngine.Object.DestroyImmediate(target.TopMountMotion); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ProductionAdapterRejectsMissingOrEmptyMotionWithoutThrowing(bool emptyAsset)
        {
            var target = _root.AddComponent<LadderInteraction>();
            var asset = emptyAsset ? ScriptableObject.CreateInstance<ActorTraversalMotionAsset>() : null;
            target.TopMountMotion = target.TopExitMotion = asset;
            var actorObject = new GameObject("Unconfigured ladder actor"); actorObject.SetActive(false); actorObject.transform.SetParent(_root.transform, false);
            var actor = actorObject.AddComponent<HumanoidAnimationPrototype>();
            try
            {
                Assert.DoesNotThrow(() => _ = target.TopApproach);
                Assert.IsFalse(target.GeometryValid);
                Assert.IsFalse(actor.TryUseLadder(target, true));
                StringAssert.Contains("authored ladder motion assets", actor.LadderStatus);
                Assert.IsFalse(actor.LadderApproaching);
            }
            finally { if (asset != null) UnityEngine.Object.DestroyImmediate(asset); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GroundedPrototypeApproachReachesMountDespiteSupportHeightOffset(bool authoredStep)
        {
            const string folder = "Assets/Art/Characters/";
            AnimationClip Load(string name) => AssetDatabase.LoadAllAssetsAtPath(folder + "Animations/" + name + ".fbx")
                .OfType<AnimationClip>().First(clip => !clip.name.StartsWith("__", StringComparison.Ordinal));
            var actorObject = new GameObject("Ladder approach actor"); actorObject.SetActive(false);
            actorObject.transform.SetParent(_root.transform, false); actorObject.transform.localPosition = Vector3.back * .97f;
            var actor = actorObject.AddComponent<HumanoidAnimationPrototype>();
            var targetObject = new GameObject("Ladder approach target"); targetObject.transform.SetParent(_root.transform, false);
            var target = targetObject.AddComponent<LadderInteraction>();
            target.TopExitMotion = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(folder + "Motion/Ladder ExitTop Motion.asset");
            target.TopMountMotion = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(folder + "Motion/Ladder MountTop Motion.asset");
            target.BottomMountMotion = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(folder + "Motion/Ladder MountBottom Motion.asset");
            if (authoredStep)
            {
                target.ApproachStepMotion = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(folder + "Motion/Ladder ApproachStep Motion.asset");
                Assert.IsNotNull(target.ApproachStepMotion, "Bake the authored final ladder step before its production regression.");
            }
            Assert.IsNotNull(target.TopExitMotion); Assert.IsNotNull(target.TopMountMotion);
            Assert.IsNotNull(target.BottomMountMotion);
            actor.CharacterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Art/Characters/Human/Converted/Rider_01_v2/Rider_01_Fit.prefab");
            actor.Idle = Load("HumanoidIdle"); actor.Walk = Load("HumanoidWalk"); actor.Run = Load("HumanoidRun");
            actor.LadderPerformances = AssetDatabase.LoadAssetAtPath<ActorAnimationPerformanceLibrary>(folder + "Motion/Ladder Performances.asset");
            Assert.IsNotNull(actor.LadderPerformances, "Author the ladder review library before its production regression.");
            actor.EnableWater = false; actor.ShowControls = false; actor.Ladders = new[] { target };
            try
            {
                actorObject.SetActive(true); actor.Initialize();
                var rig = actor.Actor.GetComponentInChildren<ProceduralRigDefinition>();
                LadderInteraction.BindFootContacts(rig);
                Assert.That(rig.Interactions.Count(limb => limb.Id == "LadderLeftFoot"), Is.EqualTo(1));
                Assert.That(rig.Interactions.Count(limb => limb.Id == "LadderRightFoot"), Is.EqualTo(1));
                for (int i = 0; i < 20; i++) actor.Step(.02f);
                Assert.IsTrue(actor.TryUseLadder(target), actor.LadderStatus);
                for (int i = 0; i < 200 && !actor.Ladder.Active; i++)
                {
                    float arrivalSpeed = actor.View.Speed, supportSpeed = actor.LadderEntrySupportSpeed;
                    actor.Step(.02f);
                    if (!actor.Ladder.Active) continue;
                    if (authoredStep)
                    {
                        Assert.AreEqual(ActorLadderPhase.ApproachStep, actor.Ladder.Phase);
                        continue;
                    }
                    Assert.That(arrivalSpeed, Is.LessThan(.025f), "Mount entry must complete the locomotion deceleration.");
                    Assert.That(supportSpeed, Is.LessThan(.04f), "Mount entry must wait for the outgoing foot support to settle.");
                }
                Assert.IsTrue(actor.Ladder.Active, actor.LadderStatus);
                Assert.IsFalse(actor.LadderApproaching);
                Box(new Vector3(0f, target.Height - .1f, .75f), new Vector3(2f, .2f, 1.5f));
                var climb = new ActorIntent(new Vector2(0f, 1f), Vector2.zero, ActorButtons.None, 0);
                for (int i = 0; i < 500 && actor.Ladder.Phase != ActorLadderPhase.ExitTop; i++) actor.Step(.02f, climb);
                Assert.AreEqual(ActorLadderPhase.ExitTop, actor.Ladder.Phase, actor.Ladder.Rejection);
                int sampled = 0;
                for (int i = 0; i < 60 && actor.Ladder.Phase == ActorLadderPhase.ExitTop; i++)
                {
                    actor.Step(.02f, climb);
                    if (actor.Ladder.AnimationProgress < .3f || actor.Ladder.AnimationProgress > .6f) continue;
                    actor.View.Pose.TryGetAuthoredInteractionContact("LeftHand", out var authored);
                    actor.View.Pose.TryGetInteractionContact("LeftHand", out var final, out _);
                    Assert.That(Mathf.Abs(final.y - authored.y), Is.LessThan(.1f),
                        "Fast authored ladder rise must not accumulate ordinary ground-step body compensation.");
                    sampled++;
                }
                Assert.Greater(sampled, 5);
                for (int i = 0; i < 60; i++) actor.Step(.02f);
                AssertReleasedContacts();
                Assert.IsTrue(actor.TryUseLadder(target, true), actor.LadderStatus);
                for (int i = 0; i < 200 && actor.Ladder.Phase != ActorLadderPhase.MountTop; i++) actor.Step(.02f);
                Assert.AreEqual(ActorLadderPhase.MountTop, actor.Ladder.Phase, actor.LadderStatus);
                sampled = 0;
                for (int i = 0; i < 120 && actor.Ladder.Phase == ActorLadderPhase.MountTop; i++)
                {
                    actor.Step(.02f);
                    if (actor.Ladder.AnimationProgress < .3f || actor.Ladder.AnimationProgress > .65f) continue;
                    foreach (string id in new[] { "LeftHand", "RightHand" })
                    {
                        actor.View.Pose.TryGetAuthoredInteractionContact(id, out var authored);
                        actor.View.Pose.TryGetInteractionContact(id, out var final, out _);
                        Assert.That(Vector3.Distance(final, authored), Is.LessThan(.08f),
                            "Authored top-mount rotation must not receive a second procedural spine turn.");
                    }
                    sampled++;
                }
                Assert.Greater(sampled, 10);
                var descend = new ActorIntent(new Vector2(0f, -1f), Vector2.zero, ActorButtons.None, 0);
                for (int i = 0; i < 600 && actor.Ladder.Active; i++) actor.Step(.02f, descend);
                Assert.IsFalse(actor.Ladder.Active, actor.Ladder.Rejection);
                AssertReleasedContacts();
                // The transfer releases authority before the next ordinary motor grounding tick.
                for (int i = 0; i < 60 && !actor.Motor.Grounded; i++) actor.Step(.02f);
                Assert.IsTrue(actor.Motor.Grounded, "The completed bottom exit must recover ordinary grounded motor control.");

                Assert.IsTrue(actor.TryUseLadder(target), actor.LadderStatus);
                for (int i = 0; i < 300 && actor.Ladder.Phase != ActorLadderPhase.Climbing; i++) actor.Step(.02f);
                Assert.AreEqual(ActorLadderPhase.Climbing, actor.Ladder.Phase, actor.LadderStatus);
                actor.View.Pose.TryGetAuthoredInteractionContact("RightHand", out var palm);
                // Align this fixture rung to a real authored palm, so cancellation starts with acquired support.
                target.transform.position += target.transform.forward * target.transform.InverseTransformPoint(palm).z;
                target.RungOffset = Mathf.Repeat(target.transform.InverseTransformPoint(palm).y, target.RungSpacing);
                target.ReleaseContacts(actor.View.Pose);
                for (int i = 0; i < 7; i++) target.ApplyContacts(actor.View.Pose, true, .02f);
                Assert.IsTrue(target.ContactAnchored("RightHand"), "The cancellation regression must begin with acquired rung support.");
                actor.CancelLadder();
                AssertReleasedContacts();

                void AssertReleasedContacts()
                {
                    var requests = (System.Collections.IDictionary)typeof(ProceduralPoseRig).GetField("_interactionTargets",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(actor.View.Pose);
                    foreach (string id in new[] { "LeftHand", "RightHand", "LadderLeftFoot", "LadderRightFoot" })
                    {
                        Assert.IsFalse(target.ContactAnchored(id), "Completed transfers must clear persistent rung support.");
                        Assert.IsFalse(requests.Contains(id), "Completed transfers must release every ladder target before clearing the ladder.");
                    }
                }
            }
            finally
            {
                actor.CancelLadder();
                actor.View?.Dispose();
                if (actor.Actor != null) UnityEngine.Object.DestroyImmediate(actor.Actor.gameObject);
                actorObject.SetActive(false);
                UnityEngine.Object.DestroyImmediate(actorObject);
                UnityEngine.Object.DestroyImmediate(targetObject);
            }
        }

        ActorTraversalMotion Gait(params float[] heights) => Gait(0f, heights);
        ActorTraversalMotion Gait(float maximumFit, float[] heights) => new ActorTraversalMotion(1f, Vector3.zero, maximumFit, 0f,
            heights.Select((height, index) => new ActorTraversalMotion.Frame {
                Root = Vector3.up * height, CapsuleA = Vector3.up * .4f, CapsuleB = Vector3.up * 1.2f,
                Radius = .1f, PlanarFitWeight = index / (float)(heights.Length - 1)
            }).ToArray(), elevatedLanding: true);

        [Test]
        public void NativeGaitPreservesRootPlateauAndHoldsItsSupportPoseWhenStopped()
        {
            var up = Gait(0f, .3f, .3f, .7f, .7f);
            var down = Gait(0f, 0f, -.4f, -.4f, -.7f);
            Assert.IsTrue(_ladder.Begin(new CharacterPose(Bottom - Vector3.forward * .25f, Vector3.up, Vector3.forward),
                Bottom, Vector3.up, Vector3.forward, 5f, .3f, false, _performance, upGait: up, downGait: down));
            _ladder.Tick(0f, 1.01f);
            while (_ladder.AnimationProgress < .3f) _ladder.Tick(1f, .01f);
            float before = _ladder.ClimbHeight, phase = _ladder.AnimationProgress;
            _ladder.Tick(1f, .1f);
            Assert.That(_ladder.AnimationProgress, Is.GreaterThan(phase + .09f));
            Assert.That(_ladder.ClimbHeight, Is.EqualTo(before).Within(.0001f),
                "An authored root plateau must keep its native timing while the limb arc continues.");
            for (int i = 0; i < 25; i++) _ladder.Tick(0f, .01f);
            var stopped = _ladder.Pose; phase = _ladder.AnimationProgress;
            _ladder.Tick(0f, .3f);
            Assert.AreEqual("Up", _ladder.AnimationPhase, "A pause must retain the established staggered support pose.");
            Assert.AreEqual(phase, _ladder.AnimationProgress);
            Assert.AreEqual(stopped.Position, _ladder.Pose.Position);
            for (int i = 0; i < 30 && _ladder.ClimbDirection != "Down"; i++) _ladder.Tick(-1f, .01f);
            Assert.AreEqual("Down", _ladder.ClimbDirection);
            Assert.AreEqual("Up", _ladder.AnimationPhase, "Direction changes retain the same canonical source input.");
            Assert.That(_ladder.AnimationProgress, Is.EqualTo(phase).Within(.001f),
                "The reversible source must retain its pose while its clock reverses.");
        }

        [Test]
        public void SupportFitUsesMotorCollisionAndBoundsEachTransfer()
        {
            Assert.IsTrue(Begin(5f, .3f));
            _ladder.Tick(0f, 1.01f);
            Vector3 start = _ladder.Pose.Position;
            _ladder.BeginSupportTransfer();
            _ladder.FitSupport(Vector3.forward * 10f, 1f);
            Assert.That(Vector3.Distance(start, _ladder.Pose.Position), Is.EqualTo(.15f).Within(.0001f));
            _ladder.FitSupport(Vector3.forward, 1f);
            Assert.That(_ladder.SupportFitOffset.magnitude, Is.LessThanOrEqualTo(.15001f));
            _ladder.BeginSupportTransfer();
            Box(new Vector3(.30f, .9f, .15f), new Vector3(.10f, 1.8f, 1f));
            start = _ladder.Pose.Position;
            Assert.AreEqual(Vector3.zero, _ladder.FitSupport(Vector3.right * .1f, 1f));
            Assert.IsTrue(_ladder.SupportFitBlocked);
            Assert.AreEqual(start, _ladder.Pose.Position, "Rejected support fitting must not bypass the motor collision boundary.");
        }

        [Test]
        public void NativeGaitSelectsOppositeLeadTopTransferWithinItsFittingLimit()
        {
            const float height = 2.1f;
            Box(new Vector3(0f, height - .1f, 1.2f), new Vector3(2f, .2f, 1.5f));
            var frames = new[] { Vector3.zero, Vector3.up * 1.8f, Vector3.up * 1.8f + Vector3.forward * .95f }
                .Select((root, index) => new ActorTraversalMotion.Frame {
                    Root = root, CapsuleA = Vector3.up * .4f, CapsuleB = Vector3.up * 1.2f,
                    Radius = .1f, PlanarFitWeight = index / 2f
                }).ToArray();
            var transfer = new ActorTraversalMotion(1f, Vector3.zero, .15f, .15f, frames, elevatedLanding: true);
            Assert.IsTrue(_ladder.Begin(new CharacterPose(Bottom - Vector3.forward * .25f, Vector3.up, Vector3.forward),
                Bottom, Vector3.up, Vector3.forward, height, .3f, false, _performance,
                topExit: transfer, upGait: Gait(0f, .7f), downGait: Gait(0f, -.7f), topExitMirrored: transfer));
            _ladder.Tick(0f, 1.01f);
            for (int i = 0; i < 100 && _ladder.Phase == ActorLadderPhase.Climbing; i++) _ladder.Tick(1f, .01f);
            Assert.AreEqual(ActorLadderPhase.ExitTop, _ladder.Phase, _ladder.Rejection);
            Assert.AreEqual("ExitTopMirrored", _ladder.AnimationPhase);
            Assert.That(Mathf.Abs(_ladder.GaitPhase - .5f), Is.LessThan(.04f));
            Assert.That(Mathf.Abs(_ladder.ClimbHeight - _ladder.TopClimbHeight), Is.LessThanOrEqualTo(.15f));
        }

        [Test]
        public void HandRungContactOffsetsThePalmWithoutMovingTheFootPlane()
        {
            _root.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
            var ladder = _root.AddComponent<LadderInteraction>();
            Vector3 point = _root.transform.TransformPoint(new Vector3(.1f, .8f, .2f));
            Vector3 foot = _root.transform.InverseTransformPoint(ladder.NearestRungContact(point));
            Vector3 hand = _root.transform.InverseTransformPoint(ladder.NearestHandRungContact(point));
            Assert.That(foot.z, Is.EqualTo(0f).Within(.0002f));
            Assert.That(hand.z, Is.EqualTo(-ladder.RungPalmDepth).Within(.0002f));
            Assert.That(hand.x, Is.EqualTo(foot.x).Within(.0002f));
            Assert.That(hand.y, Is.EqualTo(foot.y).Within(.0002f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LowTopTransferFinishesHandoverBeforeHoldingABlockedRoute(bool persistentObstacle)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(
                "Assets/Art/Characters/Motion/Ladder ExitTopMirrored Motion.asset");
            Assert.NotNull(asset);
            var transfer = new ActorTraversalMotion(1f, asset.ReferenceEdge, asset.MaxHeightAdjustment,
                asset.MaxPlanarAdjustment, asset.Frames, elevatedLanding: true);
            const float height = 3.75f;
            Box(new Vector3(0f, height - .1f, .75f), new Vector3(2f, .2f, 1.5f));
            if (persistentObstacle) Box(new Vector3(0f, height + .5f, .3f), new Vector3(2f, 1f, .2f));
            Vector3 bottom = Bottom - Vector3.forward * .325f;
            Assert.IsTrue(_ladder.Begin(new CharacterPose(bottom - Vector3.forward * .25f, Vector3.up, Vector3.forward),
                bottom, Vector3.up, Vector3.forward, height, .25f, false, _performance, rungsPerCycle: 3,
                topExit: transfer, topExitMirrored: transfer, upGait: Gait(0f, 0f, 0f, .75f, .75f), downGait: Gait(0f, -.75f)));
            _ladder.Tick(0f, 1.01f);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(ActorLadder).GetField("_cycle", flags).SetValue(_ladder, 2.5133312f);
            typeof(ActorLadder).GetProperty("Pose").SetValue(_ladder,
                new CharacterPose(_origin + new Vector3(.01579f, 2.267798f, -.31723f), Vector3.up, Vector3.forward));
            _ladder.Tick(1f, .5f);
            if (persistentObstacle)
            {
                Assert.AreEqual(ActorLadderPhase.Climbing, _ladder.Phase);
                Assert.That(_ladder.GaitPhase, Is.EqualTo(.54f).Within(.00001f));
                var held = _ladder.Pose.Position;
                _ladder.Tick(1f, .5f);
                Assert.AreEqual(held, _ladder.Pose.Position);
                _ladder.Tick(-1f, .5f);
                Assert.That(_ladder.GaitPhase, Is.LessThan(.54f));
            }
            else Assert.AreEqual(ActorLadderPhase.ExitTop, _ladder.Phase, _ladder.Rejection);
        }

        [Test]
        public void FastNormalGaitVisitsTransferWindowDuringAFrameHitch()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(
                "Assets/Art/Characters/Motion/Ladder ExitTopMirrored Motion.asset");
            var transfer = new ActorTraversalMotion(1f, asset.ReferenceEdge, asset.MaxHeightAdjustment,
                asset.MaxPlanarAdjustment, asset.Frames, elevatedLanding: true);
            const float height = 3.75f;
            Box(new Vector3(0f, height - .1f, .75f), new Vector3(2f, .2f, 1.5f));
            Vector3 bottom = Bottom - Vector3.forward * .325f;
            Assert.IsTrue(_ladder.Begin(new CharacterPose(bottom - Vector3.forward * .25f, Vector3.up, Vector3.forward),
                bottom, Vector3.up, Vector3.forward, height, .25f, false, _performance, rungsPerCycle: 3,
                topExit: transfer, topExitMirrored: transfer, upGait: Gait(0f, .75f), downGait: Gait(0f, -.75f), sprintPlaybackRate: 2f));
            _ladder.Tick(0f, 1.01f);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(ActorLadder).GetField("_cycle", flags).SetValue(_ladder, 2.42f);
            typeof(ActorLadder).GetField("_speed", flags).SetValue(_ladder, 2f);
            typeof(ActorLadder).GetProperty("Pose").SetValue(_ladder,
                new CharacterPose(_origin + new Vector3(.01579f, 2.255f, -.31723f), Vector3.up, Vector3.forward));
            _ladder.Tick(1f, .1f, fast: true);
            Assert.AreEqual(ActorLadderPhase.ExitTop, _ladder.Phase, _ladder.Rejection);
            Assert.AreEqual("ExitTopMirrored", _ladder.AnimationPhase);
            Assert.That(_ladder.GaitPhase, Is.InRange(.46f, .54f),
                "The complete outer step would land at .62 and miss this transfer window.");
        }

        [Test]
        public void NativeDescentUsesBoundedOppositeLeadBottomTransfer()
        {
            var frames = new[] { Vector3.zero, Vector3.down * .3f - Vector3.forward * .2f }
                .Select((root, index) => new ActorTraversalMotion.Frame {
                    Root = root, CapsuleA = Vector3.up * .4f, CapsuleB = Vector3.up * 1.2f,
                    Radius = .1f, PlanarFitWeight = index
                }).ToArray();
            var exit = new ActorTraversalMotion(1f, Vector3.zero, .15f, .15f, frames, elevatedLanding: true);
            Assert.IsTrue(_ladder.Begin(new CharacterPose(Bottom - Vector3.forward * .25f, Vector3.up, Vector3.forward),
                Bottom, Vector3.up, Vector3.forward, 5f, .3f, false, _performance,
                bottomExit: exit, upGait: Gait(0f, .7f), downGait: Gait(0f, -.7f), bottomExitMirrored: exit));
            _ladder.Tick(0f, 1.01f);
            for (int i = 0; i < 110; i++) _ladder.Tick(1f, .01f);
            for (int i = 0; i < 200 && _ladder.Phase == ActorLadderPhase.Climbing; i++) _ladder.Tick(-1f, .01f);
            Assert.AreEqual("ExitBottomMirrored", _ladder.AnimationPhase, _ladder.Rejection);
            Assert.That(Mathf.Abs(_ladder.ClimbHeight - .3f), Is.LessThanOrEqualTo(.15f));
        }

        [Test]
        public void TopMountRejectsSupportPlacementBeyondAuthoredLimits()
        {
            var mount = Gait(0f, -.7f);
            Assert.IsFalse(_ladder.Begin(new CharacterPose(Bottom, Vector3.up, Vector3.forward),
                Bottom, Vector3.up, Vector3.forward, 3f, .3f, true, _performance,
                topMount: mount, topMountSupportOffset: Vector3.up * .01f));
            Assert.AreEqual("Top mount support placement exceeds the authored motion fitting limits.", _ladder.Rejection);
            Assert.IsFalse(_ladder.Active);
        }

        [Test]
        public void SlideUsesPartialNativeLoopThenBrakesBeforeNormalBottomExit()
        {
            Assert.IsTrue(_ladder.Begin(new CharacterPose(Bottom - Vector3.forward * .25f, Vector3.up, Vector3.forward),
                Bottom, Vector3.up, Vector3.forward, 5f, .3f, false, _performance,
                upGait: Gait(0f, .7f), downGait: Gait(0f, -.7f),
                slideStart: Gait(0f, -.1f), slide: Gait(0f, -.8f), slideEnd: Gait(0f, .1f)));
            _ladder.Tick(0f, 1.01f);
            for (int i = 0; i < 90; i++) _ladder.Tick(1f, .01f);
            bool sawStart = false, sawLoop = false, sawBrake = false, sawExit = false;
            float lastLoopProgress = 0f;
            for (int i = 0; i < 700 && _ladder.Active; i++)
            {
                _ladder.Tick(-1f, .01f, fast: true);
                sawStart |= _ladder.Phase == ActorLadderPhase.SlideStart;
                if (_ladder.Phase == ActorLadderPhase.Slide)
                { sawLoop = true; lastLoopProgress = _ladder.AnimationProgress; }
                if (_ladder.Phase == ActorLadderPhase.SlideEnd)
                {
                    sawBrake = true;
                    Assert.That(_ladder.ClimbHeight, Is.GreaterThanOrEqualTo(-.076f));
                }
                sawExit |= _ladder.Phase == ActorLadderPhase.ExitBottom;
            }
            Assert.IsTrue(sawStart && sawLoop && sawBrake && sawExit, _ladder.Rejection);
            Assert.That(lastLoopProgress, Is.LessThan(.99f), "The brake must not require a complete loop on a short remaining descent.");
            Assert.IsFalse(_ladder.Active, _ladder.Rejection);
        }

        [Test]
        public void SlideReturnPlacementFitsBeforePlaybackAndKeepsItsAuthoredBudget()
        {
            Assert.IsTrue(_ladder.Begin(new CharacterPose(Bottom - Vector3.forward * .25f, Vector3.up, Vector3.forward),
                Bottom, Vector3.up, Vector3.forward, 5f, .3f, false, _performance,
                upGait: Gait(0f, .7f), downGait: Gait(0f, -.7f),
                slideStart: Gait(0f, -.1f), slide: Gait(0f, -.8f), slideEnd: Gait(.15f, new[] { 0f, .1f })));
            _ladder.Tick(0f, 1.01f);
            for (int i = 0; i < 180; i++) _ladder.Tick(1f, .01f);
            for (int i = 0; i < 400 && _ladder.Phase != ActorLadderPhase.Slide; i++) _ladder.Tick(-1f, .01f, fast: true);
            Assert.AreEqual(ActorLadderPhase.Slide, _ladder.Phase);
            _ladder.Tick(0f, .01f);
            Assert.AreEqual(ActorLadderPhase.SlideEnd, _ladder.Phase);
            var initial = _ladder.Pose.Position; var end = _ladder.TransitionEndPosition;
            Assert.IsFalse(_ladder.FitSlideReturn(Vector3.up * .151f));
            Assert.AreEqual(end, _ladder.TransitionEndPosition);
            Assert.IsTrue(_ladder.FitSlideReturn(Vector3.up * .1f));
            Assert.IsFalse(_ladder.FitSlideReturn(Vector3.up * .1f), "Repeated requests must share the same total placement budget.");
            Assert.AreEqual(initial, _ladder.Pose.Position, "Endpoint fitting must not teleport the current body.");
            Assert.That(_ladder.TransitionEndPosition.y - end.y, Is.EqualTo(.1f).Within(.001f));
            _ladder.Tick(0f, .01f);
            Assert.IsFalse(_ladder.FitSlideReturn(Vector3.up * .01f), "The full route must be fixed before brake playback starts.");
        }

        [TestCase(false, "LeftHand", .20f, .50f, 2)]
        [TestCase(false, "RightHand", .70f, 1f, 1)]
        [TestCase(true, "RightHand", .80f, .50f, -2)]
        [TestCase(true, "LeftHand", .30f, 0f, -1)]
        public void FastNormalPreparationSelectsTheUpcomingPhysicalHandLanding(
            bool descending, string hand, float preparationPhase, float landingPhase, int rungStep)
        {
            Assert.IsTrue(_ladder.Begin(new CharacterPose(Bottom - Vector3.forward * .25f, Vector3.up, Vector3.forward),
                Bottom, Vector3.up, Vector3.forward, 5f, .25f, false, _performance,
                rungsPerCycle: 3f, upGait: Gait(0f, .7f), downGait: Gait(0f, -.7f), sprintPlaybackRate: 2f));
            _ladder.Tick(0f, 1.01f);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(ActorLadder).GetField("_speed", flags).SetValue(_ladder, descending ? -2f : 2f);
            typeof(ActorLadder).GetField("_gaitDirection", flags).SetValue(_ladder, descending ? "Down" : "Up");
            var cycle = typeof(ActorLadder).GetField("_cycle", flags);
            var target = _root.AddComponent<LadderInteraction>(); target.Height = 5f; target.RungSpacing = .25f;
            var bones = new Transform[3];
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = new GameObject("Authored landing " + i).transform;
                bones[i].SetParent(i == 0 ? _root.transform : bones[i - 1], false);
                bones[i].localPosition = Vector3.up * .4f;
            }
            var rig = _root.AddComponent<ProceduralRigDefinition>();
            rig.Interactions = new[] { "LeftHand", "RightHand", "LadderLeftFoot", "LadderRightFoot" }
                .Select(id => new InteractionLimbDefinition { Id = id, Bones = bones }).ToArray();
            var pose = new ProceduralPoseRig(_root.transform, rig);
            void Sample(float phase)
            {
                cycle.SetValue(_ladder, phase); pose.CaptureAnimation(); target.ApplyAuthoredContacts(pose, _ladder, .016f);
            }
            Vector3 Anchor(string id)
            {
                var contacts = (System.Collections.IDictionary)typeof(LadderInteraction).GetField("_support", flags).GetValue(target);
                object contact = contacts[id];
                return (Vector3)contact.GetType().GetField("Anchor").GetValue(contact);
            }
            Sample(preparationPhase);
            Assert.That(_ladder.EffectivePlaybackRate, Is.EqualTo(2f).Within(.0001f));
            Assert.That(target.ContactPreparationWeight(hand), Is.GreaterThan(0f));
            Assert.AreEqual(0f, target.SupportWeight(hand));
            Vector3 prepared = Anchor(hand);
            Vector3 incumbent = Anchor(hand == "LeftHand" ? "RightHand" : "LeftHand");
            Assert.That(prepared.y - incumbent.y, Is.EqualTo(rungStep * .25f).Within(.0002f));
            Sample(landingPhase);
            Assert.That(target.SupportWeight(hand), Is.EqualTo(1f));
            Assert.AreEqual(prepared, Anchor(hand), "The landing must retain the rung chosen during early preparation.");
            Assert.That(target.MaxRequestedCorrection, Is.LessThanOrEqualTo(.06001f));
        }

        [Test]
        public void SprintPreparationTracksAlongRungUntilAuthoredSupportStarts()
        {
            Assert.IsTrue(_ladder.Begin(new CharacterPose(Bottom - Vector3.forward * .25f, Vector3.up, Vector3.forward),
                Bottom, Vector3.up, Vector3.forward, 5f, .3f, false, _performance,
                upGait: Gait(0f, .7f), downGait: Gait(0f, -.7f),
                sprintUp: Gait(.15f, new[] { 0f, .591f }), sprintUpRight: Gait(.15f, new[] { 0f, .591f })));
            _ladder.Tick(0f, 1.01f); _ladder.Tick(1f, .01f, fast: true);
            var target = _root.AddComponent<LadderInteraction>(); target.Height = 5f;
            var bones = new Transform[3];
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = new GameObject("Authored reach " + i).transform;
                bones[i].SetParent(i == 0 ? _root.transform : bones[i - 1], false);
                bones[i].localPosition = Vector3.up * .4f;
            }
            var rig = _root.AddComponent<ProceduralRigDefinition>();
            rig.Interactions = new[] { "LeftHand", "RightHand", "LadderLeftFoot", "LadderRightFoot" }
                .Select(id => new InteractionLimbDefinition { Id = id, Bones = bones }).ToArray();
            var pose = new ProceduralPoseRig(_root.transform, rig);
            var cycle = typeof(ActorLadder).GetField("_cycle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            void Sample(float phase) { cycle.SetValue(_ladder, phase); pose.CaptureAnimation(); target.ApplyAuthoredContacts(pose, _ladder, .016f); }
            Vector3 PlannedAnchor()
            {
                var contacts = (System.Collections.IDictionary)typeof(LadderInteraction).GetField("_support", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(target);
                object contact = contacts["LeftHand"];
                return (Vector3)contact.GetType().GetField("Anchor").GetValue(contact);
            }
            Sample(.25f); var first = PlannedAnchor();
            bones[2].position += Vector3.right * .1f;
            Sample(.4f); var reaching = PlannedAnchor();
            Assert.That(reaching.y, Is.EqualTo(first.y).Within(.0001f), "Preparation must retain its selected rung.");
            Assert.That(reaching.x - first.x, Is.EqualTo(.1f).Within(.001f));
            Sample(.47f); target.TryGetContactAnchor("LeftHand", out var planted);
            bones[2].position += Vector3.right * .1f;
            Sample(.5f); target.TryGetContactAnchor("LeftHand", out var retained);
            Assert.AreEqual(planted, retained, "Authored support must freeze the contact along the rung.");
            Assert.That(target.MaxRequestedCorrection, Is.LessThanOrEqualTo(.06001f));
            // A brake has already prepared its future normal contact. Unlike an
            // unprepared reach, its first normal sample must retain that placement.
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(ActorLadder).GetField("_sprinting", flags).SetValue(_ladder, false);
            var prepared = target.NearestHandRungContact(_origin + new Vector3(.25f, 1.35f, 0f));
            typeof(LadderInteraction).GetField("_slideReturnPrepared", flags).SetValue(target, true);
            typeof(LadderInteraction).GetField("_slideReturnAnchor", flags).SetValue(target, prepared);
            typeof(LadderInteraction).GetProperty("SlidingRailContacts").SetValue(target, true);
            Sample(0f);
            Assert.IsTrue(target.TryGetContactAnchor("RightHand", out var returned));
            Assert.AreEqual(prepared, returned, "The first normal frame must not replace the brake's prepared anchor.");
        }

        [Test]
        public void SprintAlternatesAuthoredLeadingTakesAndCanStopThenReverse()
        {
            Assert.IsTrue(_ladder.Begin(new CharacterPose(Bottom - Vector3.forward * .25f, Vector3.up, Vector3.forward),
                Bottom, Vector3.up, Vector3.forward, 5f, .3f, false, _performance,
                upGait: Gait(0f, .7f), downGait: Gait(0f, -.7f),
                sprintUp: Gait(.15f, new[] { 0f, .591f }), sprintUpRight: Gait(.15f, new[] { 0f, .591f })));
            _ladder.Tick(0f, 1.01f);
            _ladder.Tick(1f, .01f, fast: true);
            Assert.AreEqual("SprintUp", _ladder.AnimationPhase);
            var entryPose = _ladder.Pose.Position;
            _ladder.Tick(0f, .1f, fast: true);
            Assert.AreEqual(0f, _ladder.AnimationProgress, "Zero movement input must hold the entry pose.");
            Assert.AreEqual(entryPose, _ladder.Pose.Position);
            float startHeight = _ladder.ClimbHeight;
            _ladder.Tick(1f, .1f, fast: true);
            Assert.That(_ladder.AnimationProgress, Is.EqualTo(.075f).Within(.00001f));
            Assert.That(_ladder.ClimbHeight - startHeight, Is.EqualTo(.591f * .075f).Within(.001f),
                "Entry acceleration must advance body and source together during the existing pose blend, without a separate clock hold.");
            for (int i = 0; i < 80; i++) _ladder.Tick(1f, .01f, fast: true);
            Assert.AreEqual("SprintUpRight", _ladder.AnimationPhase);
            for (int i = 0; i < 25; i++) _ladder.Tick(0f, .01f, fast: true);
            var stopped = _ladder.Pose;
            _ladder.Tick(0f, .2f, fast: true);
            Assert.AreEqual(stopped.Position, _ladder.Pose.Position);
            for (int i = 0; i < 150 && _ladder.Sprinting; i++) _ladder.Tick(-1f, .01f, fast: true);
            Assert.IsFalse(_ladder.Sprinting, "Reversing an unfinished large reach must return to normal descent.");
            Assert.AreEqual(ActorLadderPhase.Climbing, _ladder.Phase);
            Assert.IsTrue(_ladder.PrimarySupportIsLeft, "The unfinished right-leading take must return to its left incumbent.");
            Assert.That(_ladder.AnimationProgress, Is.EqualTo(.5f));
            float returnBlend = _performance.GetPhase("Up").BlendSeconds;
            Assert.That(_ladder.ReturnBlendRemaining, Is.EqualTo(returnBlend));
            _ladder.Tick(-1f, returnBlend * .5f);
            Assert.That(_ladder.AnimationProgress, Is.EqualTo(.5f), "The returning source pose must blend before descent resumes.");
            _ladder.Tick(-1f, returnBlend * .5f);
            _ladder.Tick(-1f, .01f);
            Assert.That(_ladder.AnimationProgress, Is.LessThan(.5f));
        }

        [Test]
        public void SprintDoesNotForceTwoRungsBeyondItsAuthoredFit()
        {
            Assert.IsTrue(_ladder.Begin(new CharacterPose(Bottom - Vector3.forward * .25f, Vector3.up, Vector3.forward),
                Bottom, Vector3.up, Vector3.forward, 5f, .5f, false, _performance,
                upGait: Gait(0f, .7f), downGait: Gait(0f, -.7f),
                sprintUp: Gait(.15f, new[] { 0f, .591f }), sprintUpRight: Gait(.15f, new[] { 0f, .591f })));
            _ladder.Tick(0f, 1.01f);
            _ladder.Tick(1f, .01f, fast: true);
            Assert.IsFalse(_ladder.SprintSpacingFits);
            Assert.IsFalse(_ladder.Sprinting, "Wide rungs must retain normal climbing instead of stretching the authored reach.");
            Assert.AreEqual(ActorLadderPhase.Climbing, _ladder.Phase);
        }

        [TestCase(.3f, 1, 1)]
        [TestCase(.25f, 2, 1)]
        public void MotorCycleTravelMatchesBothAuthoredHandLandings(float spacing, int firstStep, int secondStep)
        {
            var target = _root.AddComponent<LadderInteraction>(); target.RungSpacing = spacing;
            Assert.AreEqual(firstStep, target.HandRungStep(true));
            Assert.AreEqual(secondStep, target.HandRungStep(false));
            Assert.IsTrue(_ladder.Begin(new CharacterPose(Bottom - Vector3.forward * .25f, Vector3.up, Vector3.forward),
                Bottom, Vector3.up, Vector3.forward, 5f, spacing, false, _performance,
                rungsPerCycle: target.RungsPerCycle, upGait: Gait(0f, .7f), downGait: Gait(0f, -.7f)));
            _ladder.Tick(0f, 1.01f);
            for (int i = 0; i < 25; i++) _ladder.Tick(1f, .01f);
            float before = _ladder.ClimbHeight;
            for (int i = 0; i < 20; i++) _ladder.Tick(1f, .05f);
            Assert.That(_ladder.ClimbHeight - before, Is.EqualTo((firstStep + secondStep) * spacing).Within(.001f),
                "The motor and planned contacts must cover the same rung count in one native cycle.");
            target.HalfCycleHandSeparation = float.NaN;
            Assert.Throws<ArgumentException>(() => target.HandRungStep(true));
        }

        [Test]
        public void SlideRejectsAnIncompleteAuthoredFamily()
        {
            Assert.IsFalse(_ladder.Begin(new CharacterPose(Bottom, Vector3.up, Vector3.forward),
                Bottom, Vector3.up, Vector3.forward, 3f, .3f, false, _performance, slide: Gait(0f, -.8f)));
            Assert.AreEqual("Sliding requires matching authored start, descent, and brake motions.", _ladder.Rejection);
            Assert.IsFalse(_ladder.Active);
        }

        [Test]
        public void NativeGaitRejectsMissingOrWrongDirectionMotion()
        {
            var up = Gait(0f, .7f);
            bool BeginWith(ActorTraversalMotion down) => _ladder.Begin(
                new CharacterPose(Bottom - Vector3.forward * .25f, Vector3.up, Vector3.forward),
                Bottom, Vector3.up, Vector3.forward, 5f, .3f, false, _performance, upGait: up, downGait: down);
            Assert.IsFalse(BeginWith(null));
            Assert.IsFalse(BeginWith(up));
            Assert.AreEqual("Ladder gait motions must match both authored climb directions.", _ladder.Rejection);
            Assert.IsFalse(_ladder.Active);
        }

        sealed class FlatGround : IGroundingProvider, IGravityProvider
        {
            readonly float _height;
            public FlatGround(float height) => _height = height;
            public bool TryGetGravity(Vector3 position, out Vector3 acceleration) { acceleration = Vector3.down * 9.81f; return true; }
            public bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult result)
            { result = new GroundResult(new Vector3(position.x, _height + offset, position.z), Vector3.up); return true; }
        }
    }
}


