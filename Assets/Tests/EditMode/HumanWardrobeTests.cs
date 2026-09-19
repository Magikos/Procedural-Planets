using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class HumanWardrobeTests
    {
        const string Folder = "Assets/Art/Characters/Human/BodyReview/Wardrobe/";

        [TestCase("Rider_01")]
        [TestCase("Soldier_Male_01")]
        [TestCase("Blacksmith_Female_01")]
        [TestCase("Mage_01")]
        [TestCase("Priest_01")]
        public void WardrobePreservesGarmentTrianglesAndCachesFourShapes(string role)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + role + "_Original.prefab")
                .GetComponentsInChildren<SkinnedMeshRenderer>().Single();
            var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + role + "_Fit.prefab");
            var skin = candidate.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name.StartsWith("SOURCE / "));
            var mesh = skin.sharedMesh;
            Assert.AreEqual(source.sharedMesh.vertexCount, mesh.vertexCount);
            Assert.AreEqual(5, mesh.blendShapeCount);
            Assert.AreEqual("SkeletonFit", mesh.GetBlendShapeName(0));
            Assert.IsTrue(candidate.GetComponent<Animator>().avatar.isHuman);
            Assert.IsTrue(candidate.GetComponent<Animator>().avatar.isValid);
            var weights = source.sharedMesh.boneWeights;
            float Head(int index)
            {
                var w = weights[index];
                bool IsHead(int bone) => new[] { "Head", "Eyes", "Eyebrows", "Jaw" }.Contains(source.bones[bone].name);
                return (IsHead(w.boneIndex0) ? w.weight0 : 0) + (IsHead(w.boneIndex1) ? w.weight1 : 0)
                    + (IsHead(w.boneIndex2) ? w.weight2 : 0) + (IsHead(w.boneIndex3) ? w.weight3 : 0);
            }
            var sourceTriangles = source.sharedMesh.triangles;
            var expected = Enumerable.Range(0, sourceTriangles.Length / 3)
                .Select(i => (sourceTriangles[i * 3], sourceTriangles[i * 3 + 1], sourceTriangles[i * 3 + 2]))
                .Where(t => Head(t.Item1) <= .5f && Head(t.Item2) <= .5f && Head(t.Item3) <= .5f).ToArray();
            var triangles = mesh.triangles;
            var actual = Enumerable.Range(0, triangles.Length / 3).Select(i => (triangles[i * 3], triangles[i * 3 + 1], triangles[i * 3 + 2])).ToArray();
            CollectionAssert.AreEquivalent(expected, actual);
            for (int frame = 0; frame < mesh.blendShapeCount; frame++)
            {
                var delta = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(frame, 0, delta, null, null);
                Assert.IsTrue(delta.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)));
                Assert.IsTrue(delta.Any(v => v.sqrMagnitude > 1e-10f), "Empty shape: " + mesh.GetBlendShapeName(frame));
            }
            Assert.IsTrue(skin.bones.All(b => b != null && b.IsChildOf(candidate.transform)));
        }

        [Test]
        public void ReviewedMageHoodKeepsClothAboveTheHeadCut()
        {
            var old = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Mage_01_Fit.prefab")
                .GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name.StartsWith("SOURCE / ")).sharedMesh;
            var current = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Art/Characters/Human/Converted/Mage_01_hood_v1/Mage_01_Fit.prefab")
                .GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name.StartsWith("SOURCE / ")).sharedMesh;
            Assert.Greater(current.triangles.Length, old.triangles.Length, "Hood cloth must survive the head cut.");
            Assert.AreEqual(5, current.blendShapeCount);
            var oldIndices = old.triangles.ToHashSet();
            var extra = current.triangles.Where(i => !oldIndices.Contains(i)).Distinct().ToArray();
            Assert.IsNotEmpty(extra);
            var texture = new Texture2D(2, 2);
            try
            {
                texture.LoadImage(System.IO.File.ReadAllBytes("Assets/Art/Characters/Human/Review/TownsfolkAtlas.png"));
                var uv = current.uv;
                foreach (int i in extra)
                    Assert.That(ColorUtility.ToHtmlStringRGB(texture.GetPixel((int)(uv[i].x * texture.width), (int)(uv[i].y * texture.height))),
                        Is.EqualTo("497D7E").Or.EqualTo("C59E60"), "Source skin and hair must stay excluded.");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Test]
        public void HelmetRetainsTopologyAndUsesIndependentCachedMesh()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Baseline/ModularCharacters.fbx")
                .GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Chr_HeadCoverings_No_Hair_01");
            var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "SoldierHelmet_Fit.prefab");
            var helmet = candidate.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name.Contains("HeadCoverings"));
            Assert.AreNotSame(source.sharedMesh, helmet.sharedMesh);
            CollectionAssert.AreEqual(source.sharedMesh.triangles, helmet.sharedMesh.triangles);
            CollectionAssert.AreEqual(source.sharedMesh.uv, helmet.sharedMesh.uv);
            Assert.AreEqual(5, helmet.sharedMesh.blendShapeCount);
        }
    }
}
