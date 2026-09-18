using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class BodyLeanTests
    {
        [TestCase(0f, 1f, 0f)]
        [TestCase(0f, -1f, 0f)]
        [TestCase(1f, 0f, 0f)]
        [TestCase(-1f, 0f, 90f)]
        public void LeanTracksSignedAccelerationInsteadOfSpeedMagnitude(float sideways, float forward, float gravityRoll)
        {
            var root = new GameObject("Signed lean");
            var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
            try
            {
                root.transform.rotation = Quaternion.Euler(0f, 0f, gravityRoll);
                var definition = root.AddComponent<ProceduralRigDefinition>();
                definition.Body = body.transform; definition.BodyLeanWeight = 1f;
                var pose = new ProceduralPoseRig(root.transform, definition);
                pose.Tick(root.transform.up, root.transform.forward, null, 0f, 0f, 0f, .02f);
                root.transform.position += root.transform.TransformDirection(new Vector3(sideways, 0f, forward)) * .05f;
                pose.RestoreAnimation(); pose.CaptureAnimation();
                pose.Tick(root.transform.up, root.transform.forward, null, 0f, 1f, 0f, .02f);
                Vector3 bodyUp = root.transform.InverseTransformDirection(body.transform.up);
                Assert.Greater(bodyUp.x * sideways + bodyUp.z * forward, .01f);
                if (sideways == 0f) Assert.Less(Mathf.Abs(bodyUp.x), .001f);
                if (forward == 0f) Assert.Less(Mathf.Abs(bodyUp.z), .001f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        sealed class ChainGround : IGroundingProvider
        {
            public Vector3 Up;
            public int Calls;
            public bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult result)
            {
                Calls++;
                result = new GroundResult(position + Up * (offset - Vector3.Dot(position, Up)), Up);
                return true;
            }
        }

        [TestCase(0f)]
        [TestCase(90f)]
        public void ChainGroundCollisionPreservesLengthAndResetsInAnyGravityFrame(float roll)
        {
            var root = new GameObject("Grounded chain");
            var tip = new GameObject("Tip"); tip.transform.SetParent(root.transform, false);
            tip.transform.localPosition = Vector3.forward;
            var rotation = Quaternion.Euler(0f, 0f, roll);
            Vector3 up = rotation * Vector3.up;
            var ground = new ChainGround { Up = up };
            root.transform.position = up * .2f;
            root.transform.rotation = rotation;
            try
            {
                var definition = new SpringChainDefinition { Bones = new[] { root.transform, tip.transform },
                    Frequency = 1f, GravityScale = 2f, AngleLimit = 80f, GroundClearance = .1f };
                var spring = new BoneChainSpring(definition);
                for (int i = 0; i < 40; i++)
                {
                    root.transform.rotation = rotation;
                    spring.Tick(-up * 9.81f, .05f, 0f, ground, up);
                    Assert.GreaterOrEqual(Vector3.Dot(tip.transform.position, up), .099f);
                    Assert.AreEqual(1f, Vector3.Distance(root.transform.position, tip.transform.position), .0001f);
                }
                Assert.AreEqual(40, ground.Calls, "Query each movable node once per tick, not once per substep.");
                Assert.Less(spring.MaxGroundPenetration, .001f);
                root.transform.position += up * 5f; root.transform.rotation = rotation; spring.Reset();
                spring.Tick(Vector3.zero, .05f, 0f, ground, up);
                Assert.AreEqual(5.2f, Vector3.Dot(tip.transform.position, up), .001f);
                definition.GroundClearance = 0f;
                var disabled = new BoneChainSpring(definition); ground.Calls = 0;
                disabled.Tick(-up * 9.81f, .05f, 0f, ground, up);
                Assert.AreEqual(0, ground.Calls);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void SecondaryDragRejectsInvalidValuesAndPreservesSegmentLength()
        {
            var root = new GameObject("Chain");
            var tip = new GameObject("Tip");
            tip.transform.SetParent(root.transform, false);
            tip.transform.localPosition = Vector3.forward;
            try
            {
                var spring = new BoneChainSpring(new SpringChainDefinition {
                    Bones = new[] { root.transform, tip.transform }, GravityScale = 1f,
                    Frequency = 1f, Damping = 0f, AngleLimit = 80f });
                Assert.Throws<System.ArgumentOutOfRangeException>(() => spring.Tick(Vector3.down, .02f, -1f));
                Assert.Throws<System.ArgumentOutOfRangeException>(() => spring.Tick(Vector3.down, .02f, float.NaN));
                float Displacement(float drag)
                {
                    spring.Reset();
                    for (int frame = 0; frame < 5; frame++)
                    {
                        // Restore the animation target, while retaining the spring's simulated state.
                        root.transform.rotation = Quaternion.identity;
                        spring.Tick(Vector3.down * 9.81f, .1f, drag);
                        Assert.AreEqual(1f, Vector3.Distance(root.transform.position, tip.transform.position), .00001f);
                    }
                    return Mathf.Abs(tip.transform.position.y);
                }
                float dry = Displacement(0f);
                float damped = Displacement(1000f);
                Assert.Greater(dry, .05f, "The undamped chain must show measurable movement.");
                Assert.Less(damped, dry * .5f, "Strong drag must reduce the transient movement.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(0f)]
        [TestCase(90f)]
        public void LeanFollowsGravityFrameAndResetsAfterTeleport(float roll)
        {
            var root = new GameObject("Lean test");
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            try
            {
                var frame = Quaternion.Euler(0f, 0f, roll);
                root.transform.rotation = frame;
                var definition = root.AddComponent<ProceduralRigDefinition>();
                definition.Body = body.transform;
                definition.BodyLeanLimit = 8f;
                definition.BodyLeanWeight = 1f;
                var pose = new ProceduralPoseRig(root.transform, definition);
                Vector3 up = frame * Vector3.up;
                pose.Tick(up, root.transform.forward, null, 0f, 1f, 0f, .02f);
                for (int i = 0; i < 40; i++)
                {
                    pose.RestoreAnimation();
                    root.transform.rotation = frame * Quaternion.Euler(0f, i * 2f, 0f);
                    root.transform.position += root.transform.forward * .08f;
                    pose.CaptureAnimation();
                    pose.Tick(up, root.transform.forward, null, 0f, 1f, 0f, .02f);
                }
                Vector3 localUp = root.transform.InverseTransformDirection(body.transform.up);
                Assert.Greater(localUp.x, .02f, "A right turn must lean toward the inside of the turn.");
                Assert.Less(Quaternion.Angle(Quaternion.identity, body.transform.localRotation), 12f);
                pose.RestoreAnimation();
                root.transform.position += up * 10f;
                pose.CaptureAnimation();
                pose.Tick(up, root.transform.forward, null, 0f, 0f, 0f, .02f);
                Assert.Less(Quaternion.Angle(Quaternion.identity, body.transform.localRotation), .001f);
                for (int i = 0; i < 40; i++)
                {
                    pose.RestoreAnimation(); pose.CaptureAnimation();
                    pose.Tick(up, root.transform.forward, null, 0f, 0f, 0f, .02f);
                }
                Assert.Less(Quaternion.Angle(Quaternion.identity, body.transform.localRotation), .001f);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
