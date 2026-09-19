using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class HumanTownsfolkTests
    {
        [TestCase("Monk_01")]
        [TestCase("Peasant_Male_01")]
        public void CombinedBodyKeepsClothingAndRemovesOriginalHead(string role)
        {
            const string folder = "Assets/Art/Characters/Human/BodyReview/Townsfolk/";
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(folder + role + "_Original.prefab").GetComponentsInChildren<SkinnedMeshRenderer>().Single();
            var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(folder + role + "_Fit.prefab");
            var skin = candidate.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name.StartsWith("SOURCE / "));
            Assert.AreEqual(11, candidate.GetComponentsInChildren<SkinnedMeshRenderer>().Length);
            Assert.Less(skin.sharedMesh.triangles.Length, original.sharedMesh.triangles.Length);
            Assert.Greater(skin.sharedMesh.triangles.Length, original.sharedMesh.triangles.Length / 2);
            var originalTriangles = original.sharedMesh.triangles;
            var allowed = new HashSet<(int, int, int)>();
            for (int i = 0; i < originalTriangles.Length; i += 3)
                allowed.Add((originalTriangles[i], originalTriangles[i + 1], originalTriangles[i + 2]));
            var triangles = skin.sharedMesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
                Assert.IsTrue(allowed.Contains((triangles[i], triangles[i + 1], triangles[i + 2])), "The conversion must not invent or reverse triangles.");
            var head = new HashSet<string> { "Head", "Eyes", "Eyebrows", "Jaw" };
            var weights = original.sharedMesh.boneWeights;
            foreach (int i in triangles.Distinct())
            {
                var w = weights[i];
                float amount = (head.Contains(original.bones[w.boneIndex0].name) ? w.weight0 : 0)
                    + (head.Contains(original.bones[w.boneIndex1].name) ? w.weight1 : 0)
                    + (head.Contains(original.bones[w.boneIndex2].name) ? w.weight2 : 0)
                    + (head.Contains(original.bones[w.boneIndex3].name) ? w.weight3 : 0);
                Assert.LessOrEqual(amount, .5f, "A source head vertex remains visible.");
            }
            var uv = skin.sharedMesh.uv; var sourceUv = original.sharedMesh.uv;
            foreach (int i in skin.sharedMesh.GetTriangles(0).Distinct()) Assert.AreEqual(sourceUv[i], uv[i]);
            Assert.AreEqual(original.sharedMaterial, skin.sharedMaterials[0]);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Characters/Human/Human.mat"), skin.sharedMaterials[1]);
        }
    }
}
