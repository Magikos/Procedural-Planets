using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorDeathPoseTests
    {
        [TestCase(0f, 0f)]
        [TestCase(20f, 0f)]
        [TestCase(20f, 90f)]
        public void SettlesOnSupportWithoutMovingFrameOrChangingBoneLengths(float slope, float gravityRoll)
        {
            var frame = new GameObject("death frame");
            try
            {
                frame.transform.rotation = Quaternion.Euler(0f, 0f, gravityRoll);
                var rig = frame.AddComponent<ProceduralRigDefinition>();
                rig.Body = Bone(frame.transform, "body", Vector3.up * .3f);
                var spine = Bone(rig.Body, "spine", Vector3.forward * .4f);
                rig.Spine = new[] { spine };
                var hip = Bone(rig.Body, "hip", Vector3.right * .2f);
                var knee = Bone(hip, "knee", new Vector3(.2f, -.1f, 0f));
                var foot = Bone(knee, "foot", new Vector3(.1f, -.1f, 0f));
                rig.Feet = new[] { new FootDefinition { Bones = new[] { hip, knee, foot } } };
                float first = Vector3.Distance(hip.position, knee.position), second = Vector3.Distance(knee.position, foot.position);
                var initialRotation = frame.transform.rotation;
                Vector3 up = frame.transform.up;
                Vector3 normal = Quaternion.AngleAxis(slope, frame.transform.forward) * up;
                var ground = new Plane(normal);
                var pose = new ActorDeathPose(frame.transform, rig, 1f);
                for (int i = 0; i < 100; i++) pose.Tick(ground, up, 1f / 60f);
                Assert.That(pose.Settled, Is.True);
                Assert.That(pose.HasSupport, Is.True);
                Assert.That(Vector3.Dot(rig.Body.up, normal), Is.GreaterThan(.999f));
                Assert.That(Vector3.Distance(hip.position, knee.position), Is.EqualTo(first).Within(.00001f));
                Assert.That(Vector3.Distance(knee.position, foot.position), Is.EqualTo(second).Within(.00001f));
                Assert.That(frame.transform.position, Is.EqualTo(Vector3.zero));
                Assert.That(frame.transform.rotation, Is.EqualTo(initialRotation));
                int calls = ground.Calls;
                Vector3 settled = rig.Body.localPosition;
                for (int i = 0; i < 100; i++) pose.Tick(ground, up, .1f);
                Assert.That(ground.Calls, Is.EqualTo(calls));
                Assert.That(rig.Body.localPosition, Is.EqualTo(settled));
                Assert.That(pose.FrozenPose.Count, Is.EqualTo(5));
                var frozenRotations = new Quaternion[pose.FrozenPose.Count];
                for (int i = 0; i < frozenRotations.Length; i++) frozenRotations[i] = pose.FrozenPose[i].Rotation;
                pose.Reset();
                rig.Feet[0].Contact = frame.transform;
                rig.Feet[0].JointLimit = 0f;
                rig.Feet[0].BendDirection = Vector3.back;
                rig.Feet[0].Bones = System.Array.Empty<Transform>();
                for (int i = 0; i < 100; i++) pose.Tick(ground, up, 1f / 60f);
                for (int i = 0; i < frozenRotations.Length; i++)
                    Assert.That(Quaternion.Angle(pose.FrozenPose[i].Rotation, frozenRotations[i]), Is.LessThan(.001f),
                        "Editing authoring settings must not change an existing death pose solver.");
            }
            finally { Object.DestroyImmediate(frame); }
        }

        [Test]
        public void MissingGroundStopsWithinBudgetAndResetRestoresAuthoredPose()
        {
            var frame = new GameObject("unsupported death");
            try
            {
                var rig = frame.AddComponent<ProceduralRigDefinition>();
                rig.Body = Bone(frame.transform, "body", Vector3.up);
                var pose = new ActorDeathPose(frame.transform, rig, 1f);
                Assert.Throws<System.InvalidOperationException>(() => pose.CaptureFrozenPose());
                for (int i = 0; i < 90; i++) pose.Tick(null, Vector3.up, .01f);
                Assert.That(pose.Settled, Is.True);
                Assert.That(pose.HasSupport, Is.False);
                Assert.That(rig.Body.localPosition, Is.EqualTo(Vector3.up));
                rig.Body.localPosition += Vector3.up * .1f;
                pose.CaptureFrozenPose();
                Assert.That(pose.FrozenPose[0].Position, Is.EqualTo(rig.Body.localPosition));
                pose.Reset();
                Assert.That(pose.Settled, Is.False);
                Assert.That(pose.FrozenPose, Is.Null);
            }
            finally { Object.DestroyImmediate(frame); }
        }

        [Test]
        public void RejectsMoreThan256Bones()
        {
            var frame = new GameObject("large death rig");
            try
            {
                var rig = frame.AddComponent<ProceduralRigDefinition>();
                rig.Body = Bone(frame.transform, "body", Vector3.zero);
                for (int i = 0; i < 256; i++) Bone(rig.Body, "bone" + i, Vector3.zero);
                Assert.Throws<System.ArgumentException>(() => new ActorDeathPose(frame.transform, rig, 1f));
            }
            finally { Object.DestroyImmediate(frame); }
        }

        static Transform Bone(Transform parent, string name, Vector3 position)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false); bone.localPosition = position;
            return bone;
        }

        sealed class Plane : IGroundingProvider
        {
            readonly Vector3 _normal;
            public int Calls;
            public Plane(Vector3 normal) => _normal = normal;
            public bool TryGround(Vector3 p, Vector3 down, float offset, out GroundResult hit)
            {
                Calls++;
                hit = new GroundResult(p + down * (-Vector3.Dot(p, _normal) / Vector3.Dot(down, _normal)) - down * offset, _normal);
                return true;
            }
        }
    }
}
