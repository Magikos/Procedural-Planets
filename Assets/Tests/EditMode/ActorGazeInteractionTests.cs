using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorGazeInteractionTests
    {
        [Test]
        public void GazeWeightsNormalizeAndBehindNoiseDoesNotReverseTheHead()
        {
            var root = new GameObject("Actor");
            var neck = new GameObject("Neck"); var head = new GameObject("Head");
            neck.transform.SetParent(root.transform, false); head.transform.SetParent(neck.transform, false);
            try
            {
                var definition = root.AddComponent<ProceduralRigDefinition>();
                definition.Look = new[] { neck.transform, head.transform };
                definition.LookWeights = new[] { 1f, 3f };
                var pose = new ProceduralPoseRig(root.transform, definition);
                for (int i = 0; i < 100; i++)
                {
                    pose.RestoreAnimation(); pose.CaptureAnimation();
                    Vector3 target = Quaternion.Euler(0f, i % 2 == 0 ? 179f : -179f, 0f) * Vector3.forward;
                    pose.Tick(Vector3.up, target, null, 0f, 0f, 0f, .02f);
                }
                Assert.AreEqual(12.5f, Quaternion.Angle(Quaternion.identity, neck.transform.localRotation), .05f);
                Assert.AreEqual(37.5f, Quaternion.Angle(Quaternion.identity, head.transform.localRotation), .05f);
                pose.LookInfluence = 0f;
                for (int i = 0; i < 100; i++)
                {
                    pose.RestoreAnimation(); pose.CaptureAnimation();
                    pose.Tick(Vector3.up, Vector3.back, null, 0f, 0f, 0f, .02f);
                }
                Assert.Less(Quaternion.Angle(Quaternion.identity, head.transform.rotation), .05f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void InteractionSolvesPositionAndOrientationWithoutChangingLengthsAndCanRelease()
        {
            var root = new GameObject("Actor");
            var shoulder = new GameObject("Shoulder"); var elbow = new GameObject("Elbow"); var hand = new GameObject("Hand");
            shoulder.transform.SetParent(root.transform, false); elbow.transform.SetParent(shoulder.transform, false);
            hand.transform.SetParent(elbow.transform, false);
            elbow.transform.localPosition = Vector3.forward; hand.transform.localPosition = Vector3.forward;
            try
            {
                var definition = root.AddComponent<ProceduralRigDefinition>();
                definition.Interactions = new[] { new InteractionLimbDefinition {
                    Id = "hand", Bones = new[] { shoulder.transform, elbow.transform, hand.transform }, JointLimit = 180f } };
                var pose = new ProceduralPoseRig(root.transform, definition);
                Vector3 target = new(1f, 0f, 1f); Quaternion rotation = Quaternion.Euler(0f, 30f, 0f);
                pose.SetInteractionTarget("hand", new InteractionPoseTarget(target, rotation));
                pose.Tick(Vector3.up, Vector3.forward, null, 0f, 0f, 0f, .02f);
                Assert.Greater(Vector3.Distance(target, hand.transform.position), .01f, "Entry must blend before reaching full contact.");
                for (int frame = 0; frame < 60; frame++)
                {
                    pose.RestoreAnimation(); pose.CaptureAnimation();
                    pose.Tick(Vector3.up, Vector3.forward, null, 0f, 0f, 0f, .02f);
                }
                Assert.Less(Vector3.Distance(target, hand.transform.position), .01f);
                Assert.Less(Quaternion.Angle(rotation, hand.transform.rotation), .01f);
                Assert.AreEqual(1f, Vector3.Distance(shoulder.transform.position, elbow.transform.position), .0001f);
                Assert.AreEqual(1f, Vector3.Distance(elbow.transform.position, hand.transform.position), .0001f);
                pose.SetInteractionTarget("hand", null); pose.RestoreAnimation(); pose.CaptureAnimation();
                pose.Tick(Vector3.up, Vector3.forward, null, 0f, 0f, 0f, .02f);
                Assert.Greater(Vector3.Distance(Vector3.forward * 2f, hand.transform.position), .01f, "Release must retain the outgoing contact during its blend.");
                for (int frame = 0; frame < 60; frame++)
                {
                    pose.RestoreAnimation(); pose.CaptureAnimation();
                    pose.Tick(Vector3.up, Vector3.forward, null, 0f, 0f, 0f, .02f);
                }
                Assert.Less(Vector3.Distance(Vector3.forward * 2f, hand.transform.position), .0001f);
                Assert.Throws<System.ArgumentException>(() => pose.SetInteractionTarget("missing", null));
                Assert.Throws<System.ArgumentException>(() => new InteractionPoseTarget(new Vector3(float.NaN, 0f, 0f)));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
