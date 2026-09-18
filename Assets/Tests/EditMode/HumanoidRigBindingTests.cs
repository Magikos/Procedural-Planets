using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class HumanoidRigBindingTests
    {
        [TestCase(0f)]
        [TestCase(90f)]
        public void SkinSoleMeasurementUsesPosedVerticesAlongActorUp(float roll)
        {
            var root = new GameObject("Sole mesh");
            var mesh = new Mesh();
            try
            {
                root.transform.rotation = Quaternion.Euler(0f, 0f, roll);
                root.transform.position = root.transform.up * 3f;
                mesh.vertices = new[] { new Vector3(0f, -.1f, 0f), Vector3.right, Vector3.forward };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.bindposes = new[] { Matrix4x4.identity };
                var weight = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                mesh.boneWeights = new[] { weight, weight, weight };
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh; renderer.bones = new[] { root.transform }; renderer.rootBone = root.transform;
                float minimum = (float)typeof(HumanoidRigBinding).GetMethod("SkinMinimum",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    .Invoke(null, new object[] { root.transform, root.transform.up });
                Assert.AreEqual(2.9f, minimum, .0001f);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void MissingAvatarRejectsBindingWithoutReplacingExistingRig()
        {
            var root = new GameObject("Binding validation");
            try
            {
                var rig = root.AddComponent<ProceduralRigDefinition>(); rig.Body = root.transform;
                var feet = rig.Feet;
                Assert.Throws<System.ArgumentNullException>(() => HumanoidRigBinding.Bind(null, rig));
                Assert.Throws<System.ArgumentException>(() => HumanoidRigBinding.Bind(root.AddComponent<Animator>(), rig));
                Assert.AreSame(root.transform, rig.Body);
                Assert.AreSame(feet, rig.Feet);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DefaultBipedContactsAlternateAndRemainBounded(bool running)
        {
            var method = typeof(HumanoidRigBinding).GetMethod("ContactCurve",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var left = (AnimationCurve)method.Invoke(null, new object[] { true, running });
            var right = (AnimationCurve)method.Invoke(null, new object[] { false, running });
            Assert.Greater(left.Evaluate(.15f), .9f);
            Assert.Less(right.Evaluate(.15f), .1f);
            Assert.Greater(right.Evaluate(.65f), .9f);
            Assert.Less(left.Evaluate(.65f), .1f);
            for (int sample = 0; sample <= 100; sample++)
            {
                Assert.That(left.Evaluate(sample / 100f), Is.InRange(0f, 1f));
                Assert.That(right.Evaluate(sample / 100f), Is.InRange(0f, 1f));
            }
            Assert.AreEqual(left.Evaluate(0f), left.Evaluate(1f), .0001f);
            Assert.AreEqual(right.Evaluate(0f), right.Evaluate(1f), .0001f);
        }
    }
}
