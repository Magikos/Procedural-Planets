using NUnit.Framework;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

namespace ProceduralPlanets.Tests
{
    [TestFixture("Assets/Art/Characters/Baseline/Baseline.prefab", .3f)]
    [TestFixture("Assets/Art/Characters/Human/Converted/Rider_01_v2/Rider_01_Fit.prefab", .35f)]
    public sealed class HumanoidContactIntegrationTests
    {
        readonly string _prefabPath;
        readonly float _panelDepth;
        public HumanoidContactIntegrationTests(string prefabPath, float panelDepth)
        { _prefabPath = prefabPath; _panelDepth = panelDepth; }
        GameObject _actor;
        ProceduralRigDefinition _rig;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabPath);
            Assert.IsNotNull(prefab);
            _actor = Object.Instantiate(prefab);
            _rig = _actor.AddComponent<ProceduralRigDefinition>();
            HumanoidRigBinding.Bind(_actor.GetComponent<Animator>(), _rig);
        }

        [TearDown]
        public void TearDown() { if (_actor != null) Object.DestroyImmediate(_actor); }

        [TestCase(0)]
        [TestCase(1)]
        public void SourcePalmContactIsBeyondTheWrist(int side)
        {
            var hand = _rig.Interactions[side];
            Assert.Greater(hand.ContactPosition.magnitude, .001f, "A mapped reduced-finger hand still needs a palm socket.");
            Assert.IsNotEmpty(hand.ContactJoints);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void MatchingPalmPoseRemainsAligned(int side)
        {
            var hand = _rig.Interactions[side];
            hand.MaximumExtension = 1f;
            hand.ReachConeDegrees = 180f;
            hand.BendDirection = Vector3.zero;
            Transform wrist = hand.Bones[2];
            Vector3 point = wrist.TransformPoint(hand.ContactPosition);
            Quaternion rotation = wrist.rotation * hand.ContactRotation;
            var solver = new InteractionLimbSolver(_actor.transform, hand);
            solver.Solve(new InteractionPoseTarget(point, rotation, 1f, useContact: true));
            Assert.Less(Vector3.Distance(solver.WorldContactPosition, point), .02f);
            Assert.Less(Quaternion.Angle(wrist.rotation * hand.ContactRotation, rotation), 2f);
        }

        [Test]
        public void ForwardReachPreservesAFeasiblePalmOrientation()
        {
            var hand = _rig.Interactions[1];
            hand.MaximumExtension = 1f;
            hand.BendDirection = Vector3.zero;
            Transform upper = hand.Bones[0], wrist = hand.Bones[2];
            Quaternion original = upper.localRotation;
            Vector3 direction = (_actor.transform.forward + _actor.transform.right * .3f).normalized;
            upper.rotation = Quaternion.FromToRotation(wrist.position - upper.position, direction) * upper.rotation;
            Vector3 point = wrist.TransformPoint(hand.ContactPosition);
            Quaternion rotation = wrist.rotation * hand.ContactRotation;
            upper.localRotation = original;
            var solver = new InteractionLimbSolver(_actor.transform, hand);
            solver.Solve(new InteractionPoseTarget(point, rotation, 1f, useContact: true));
            Assert.Less(Vector3.Distance(solver.WorldContactPosition, point), .02f);
            Assert.Less(Quaternion.Angle(wrist.rotation * hand.ContactRotation, rotation), 15f);
            Assert.IsTrue(solver.Reachable);
        }

        [Test]
        public void KnobGripKeepsPalmAndFingersOutsideTheKnob()
        {
            var animator = _actor.GetComponent<Animator>();
            var idle = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Characters/Animations/HumanoidIdle.fbx")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            using var graph = new ActorAnimationGraph(animator, "Knob grip", 1);
            graph.AddBaseClip(0, idle); graph.BaseMixer.SetInputWeight(0, 1f); graph.Evaluate();
            var hand = _rig.Interactions[1];
            var solver = new InteractionLimbSolver(_actor.transform, hand);
            Vector3 center = new(.35f, 1.2f, .3f);
            foreach (float angle in new[] { 0f, 15f, 30f, 45f, 60f })
            {
                graph.Evaluate();
                center = new Vector3(.15f, 1.2f, _panelDepth) + Quaternion.Euler(0f, angle, 0f) * Vector3.right * .2f;
                solver.Solve(new InteractionPoseTarget(center, Quaternion.LookRotation(Vector3.left, Vector3.forward),
                    useContact: true, gripRadius: .04f));
                Assert.IsTrue(solver.Reachable, $"Panel angle {angle}: error {solver.Error}, rotation {solver.RotationError}");
                Assert.LessOrEqual(solver.GripPenetration, .001f);
                Assert.Greater(Vector3.Distance(solver.WorldContactPosition, center), .04f);
                Assert.IsTrue(hand.ContactJoints.Any(j => Quaternion.Angle(j.LocalRotation, j.Bone.localRotation) > 10f),
                    "A grip must articulate the fingers instead of retaining the flat palm pose.");
            }
        }

        [Test]
        public void ReviewPanelCanBeReachedFromIdleWithBoundedWrist()
        {
            var animator = _actor.GetComponent<Animator>();
            var idle = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Characters/Animations/HumanoidIdle.fbx")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            using var graph = new ActorAnimationGraph(animator, "Panel contact", 1);
            graph.AddBaseClip(0, idle);
            graph.BaseMixer.SetInputWeight(0, 1f);
            graph.Evaluate();
            var hand = _rig.Interactions[1];
            Quaternion wrist = hand.Bones[2].localRotation;
            var solver = new InteractionLimbSolver(_actor.transform, hand);
            solver.Solve(new InteractionPoseTarget(new Vector3(.35f, 1.2f, .3f), Quaternion.identity, useContact: true));
            Assert.IsTrue(solver.Reachable, $"Position error {solver.Error}, rotation error {solver.RotationError}");
            Assert.LessOrEqual(Quaternion.Angle(wrist, hand.Bones[2].localRotation), hand.WristLimitDegrees + .01f);
        }

        [Test]
        public void ContactCannotSucceedWithOppositePalmOrientation()
        {
            var hand = _rig.Interactions[1];
            hand.MaximumExtension = 1f;
            hand.ReachConeDegrees = 180f;
            hand.BendDirection = Vector3.zero;
            hand.ContactPosition = Vector3.zero;
            hand.WristLimitDegrees = 0f;
            Transform wrist = hand.Bones[2];
            Quaternion rotation = wrist.rotation * hand.ContactRotation * Quaternion.Euler(0f, 180f, 0f);
            var solver = new InteractionLimbSolver(_actor.transform, hand);
            solver.Solve(new InteractionPoseTarget(wrist.position, rotation, 1f, useContact: true));
            Assert.Greater(Quaternion.Angle(wrist.rotation * hand.ContactRotation, rotation), 90f);
            Assert.IsFalse(solver.Reachable, "Position alone cannot establish palm contact.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FarOrBehindTargetDoesNotStretchArm(bool behind)
        {
            var hand = _rig.Interactions[0];
            float upper = Vector3.Distance(hand.Bones[0].position, hand.Bones[1].position);
            float lower = Vector3.Distance(hand.Bones[1].position, hand.Bones[2].position);
            Vector3 target = hand.Bones[0].position + _actor.transform.forward * (behind ? -2f : 4f);
            var solver = new InteractionLimbSolver(_actor.transform, hand);
            solver.Solve(new InteractionPoseTarget(target, null, 1f, useContact: true));
            Assert.IsFalse(solver.Reachable);
            Assert.AreEqual(upper, Vector3.Distance(hand.Bones[0].position, hand.Bones[1].position), .0001f);
            Assert.AreEqual(lower, Vector3.Distance(hand.Bones[1].position, hand.Bones[2].position), .0001f);
        }

        [Test]
        public void InteractionSpineLeanBlendsDuringAcquisitionCancellationAndDisable()
        {
            var pose = new ProceduralPoseRig(_actor.transform, _rig);
            var spine = _actor.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Spine);
            Quaternion initial = spine.localRotation;
            pose.CaptureAnimation();
            pose.ForwardLeanDegrees = 25f;
            pose.Tick(Vector3.up, Vector3.forward, null, 0f, 0f, 0f, 1f / 60f);
            Assert.That(Quaternion.Angle(initial, spine.localRotation), Is.InRange(.01f, 1.1f));
            for (int i = 0; i < 30; i++)
            {
                pose.RestoreAnimation();
                pose.Tick(Vector3.up, Vector3.forward, null, 0f, 0f, 0f, 1f / 60f);
            }
            Quaternion held = spine.localRotation;
            pose.ForwardLeanDegrees = 0f;
            pose.RestoreAnimation();
            pose.Tick(Vector3.up, Vector3.forward, null, 0f, 0f, 0f, 1f / 60f);
            Assert.Less(Quaternion.Angle(held, spine.localRotation), 1.1f);
            Assert.Greater(Quaternion.Angle(initial, spine.localRotation), 1f);
            pose.SpineEnabled = false;
            for (int i = 0; i < 30; i++)
            {
                pose.RestoreAnimation();
                pose.Tick(Vector3.up, Vector3.forward, null, 0f, 0f, 0f, 1f / 60f);
            }
            Assert.Less(Quaternion.Angle(initial, spine.localRotation), .01f);
        }

        [Test]
        public void ReleaseRestoresWristAndFingerPose()
        {
            var hand = _rig.Interactions[0];
            Assert.IsNotEmpty(hand.ContactJoints);
            Transform finger = hand.ContactJoints[0].Bone;
            finger.localRotation *= Quaternion.Euler(12f, 8f, 0f);
            Quaternion fingerPose = finger.localRotation;
            Quaternion wristPose = hand.Bones[2].localRotation;
            var pose = new ProceduralPoseRig(_actor.transform, _rig);
            pose.CaptureAnimation();
            pose.SetInteractionTarget(hand.Id, new InteractionPoseTarget(hand.Bones[2].position + Vector3.forward * .2f,
                Quaternion.identity, .5f, useContact: true));
            pose.Tick(_actor.transform.up, _actor.transform.forward, null, 0f, 0f, 0f, .02f);
            pose.SetInteractionTarget(hand.Id, null);
            pose.RestoreAnimation();
            Assert.Less(Quaternion.Angle(wristPose, hand.Bones[2].localRotation), .001f);
            Assert.Less(Quaternion.Angle(fingerPose, finger.localRotation), .001f);
        }
    }
}
