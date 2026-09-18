using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class BirdPresentationTests
    {
        [TestCase(BirdVisualKind.Eagle)]
        [TestCase(BirdVisualKind.Vulture)]
        [TestCase(BirdVisualKind.Seagull)]
        public void BirdArtSharesMaterialAnimatesAndSurvivesSiblingDisposal(BirdVisualKind kind)
        {
            using var first = new BirdAnimationView(null, 997, .4f, visual: kind);
            using var second = new BirdAnimationView(null, 1994, .4f, visual: kind);
            var a = first.Root.GetComponentInChildren<SkinnedMeshRenderer>();
            var b = second.Root.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(a, Is.Not.Null);
            Assert.That(b, Is.Not.Null);
            Assert.That(a.sharedMaterial, Is.SameAs(b.sharedMaterial));
            Assert.That(a.sharedMaterial.GetTexture("_BaseMap"), Is.Not.Null);
            var before = new Mesh();
            var after = new Mesh();
            try
            {
                b.BakeMesh(before);
                first.Dispose();
                Assert.That(b.sharedMaterial == null, Is.False, "The remaining bird must retain the shared material.");
                second.Tick(false, true, .25f);
                b.BakeMesh(after);
                Vector3[] rest = before.vertices, flight = after.vertices;
                Assert.That(flight.Length, Is.EqualTo(rest.Length));
                float movement = 0;
                for (int i = 0; i < rest.Length; i++) movement = Mathf.Max(movement, (flight[i] - rest[i]).sqrMagnitude);
                Assert.That(movement, Is.GreaterThan(.00001f), "The authored flight clip must move the mesh.");
                second.Tick(true, false, .25f);
                Assert.That(second.VisualKind, Is.EqualTo(kind));
            }
            finally
            {
                Object.DestroyImmediate(before);
                Object.DestroyImmediate(after);
            }
        }

        [TestCase(BirdVisualKind.Eagle)]
        [TestCase(BirdVisualKind.Vulture)]
        [TestCase(BirdVisualKind.Seagull)]
        public void StandingRenderedGeometryMatchesRequestedHeight(BirdVisualKind kind)
        {
            using var bird = new BirdAnimationView(null, 997, .8f, visual: kind);
            float low = float.MaxValue, high = float.MinValue;
            foreach (var skin in bird.Root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                try
                {
                    skin.BakeMesh(mesh, true);
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        float y = skin.transform.TransformPoint(vertex).y;
                        low = Mathf.Min(low, y); high = Mathf.Max(high, y);
                    }
                }
                finally { Object.DestroyImmediate(mesh); }
            }
            Assert.That(high - low, Is.EqualTo(.8f).Within(.04f), "Measure rendered vertices, not importer culling bounds.");
            Assert.That(low, Is.EqualTo(-.4f).Within(.04f), "Standing soles must meet the body bottom.");
        }

        [Test]
        public void GullUsesAuthoredRestTailAndKeyedFlightTail()
        {
            using var bird = new BirdAnimationView(null, 997, .8f, visual: BirdVisualKind.Seagull);
            Transform joint = null;
            foreach (Transform candidate in bird.Root.GetComponentsInChildren<Transform>())
                if (candidate.name == "joint3") joint = candidate;
            Assert.That(joint, Is.Not.Null);
            // Sitting omits this joint; flight has an explicit tail rotation curve.
            var authored = new Quaternion(0f, 0f, .14975709f, .9887228f);
            Assert.That(Quaternion.Angle(joint.localRotation, authored), Is.LessThan(.1f));
            var expected = Object.Instantiate(Resources.Load<GameObject>("Wildlife/Birds/Seagull/Source"));
            try
            {
                Resources.Load<AnimationClip>("Wildlife/Birds/Seagull/Fly").SampleAnimation(expected, .25f);
                Transform expectedJoint = null;
                foreach (Transform candidate in expected.GetComponentsInChildren<Transform>())
                    if (candidate.name == "joint3") expectedJoint = candidate;
                Assert.That(expectedJoint, Is.Not.Null);
                bird.Tick(false, true, .25f);
                Assert.That(Quaternion.Angle(joint.localRotation, expectedJoint.localRotation), Is.LessThan(.1f),
                    "Flight must follow the source curve rather than freeze the sitting tail rotation.");
            }
            finally { Object.DestroyImmediate(expected); }
        }

        [Test]
        public void GullLookBindingsFollowForwardHeadRatherThanRearTail()
        {
            using var bird = new BirdAnimationView(null, 997, .8f, visual: BirdVisualKind.Seagull);
            var rig = bird.Root.GetComponentInChildren<ProceduralRigDefinition>();
            Assert.That(rig, Is.Not.Null);
            Assert.That(rig.Look.Length, Is.EqualTo(2));
            Assert.That(rig.Look[0].name, Is.EqualTo("joint5"));
            Assert.That(rig.Look[1].name, Is.EqualTo("joint6"));
            Transform head = null, tail = null;
            foreach (Transform candidate in bird.Root.GetComponentsInChildren<Transform>())
            {
                if (candidate.name == "joint7") head = candidate;
                if (candidate.name == "joint4") tail = candidate;
            }
            Assert.That(head, Is.Not.Null); Assert.That(tail, Is.Not.Null);
            Assert.That(Vector3.Dot(head.position - rig.Body.position, bird.Root.forward), Is.GreaterThan(0f));
            Assert.That(Vector3.Dot(tail.position - rig.Body.position, bird.Root.forward), Is.LessThan(0f));
            foreach (Transform bone in rig.Look)
                Assert.That(Vector3.Dot(bone.position - rig.Body.position, bird.Root.forward), Is.GreaterThan(0f));
        }

        sealed class GroundProbe : IGroundingProvider
        {
            public int Calls;
            public bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult result)
            {
                Calls++;
                result = new GroundResult(new Vector3(position.x, offset, position.z), Vector3.up);
                return true;
            }
        }

        [TestCase(BirdVisualKind.Eagle)]
        [TestCase(BirdVisualKind.Vulture)]
        public void GroundedBirdUsesTwoFeetAndPreservesRaisedSupport(BirdVisualKind kind)
        {
            using var bird = new BirdAnimationView(null, 997, .4f, visual: kind);
            var rig = bird.Root.GetComponentInChildren<ProceduralRigDefinition>();
            Assert.That(rig, Is.Not.Null, "Run CreatureBirdAssetAuthor.BuildRigs before this test.");
            Assert.That(rig.Feet.Length, Is.EqualTo(2));
            foreach (var foot in rig.Feet)
            {
                Assert.That(foot.Bones.Length, Is.EqualTo(3));
                Assert.That(foot.Contact.IsChildOf(foot.Bones[2]), Is.True);
            }
            var ground = new GroundProbe();
            bird.Root.position = Vector3.up * 5.2f;
            for (int i = 0; i < 60; i++) bird.Tick(true, false, 1f / 60f, Vector3.up,
                ground, CreatureBehaviour.Sleep, Vector3.up * 5f);
            Assert.That(ground.Calls, Is.Zero, "Raised branch support must override terrain below.");
            Assert.That(bird.Pose.PlantedFeet, Is.EqualTo(2));
            Assert.That(bird.Pose.MaxFootError, Is.LessThan(.1f));
            bird.Tick(false, true, .1f, Vector3.up, ground);
            Assert.That(ground.Calls, Is.Zero, "Flight must not solve grounded feet.");
            bird.Root.position = Vector3.up * .2f;
            for (int i = 0; i < 60; i++) bird.Tick(true, false, 1f / 60f, Vector3.up, ground);
            Assert.That(ground.Calls, Is.GreaterThan(0));
        }

        [Test]
        public void AutomaticScavengerStillUsesVultureArt()
        {
            using var bird = new BirdAnimationView(null, 1, .4f, vulture: true);
            Assert.That(bird.VisualKind, Is.EqualTo(BirdVisualKind.Vulture));
        }
    }
}
