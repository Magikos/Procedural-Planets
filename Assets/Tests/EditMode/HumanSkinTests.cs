using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class HumanSkinTests
    {
        [TestCase("Assets/Art/Characters/Human/BodyReview/FullBody/FullSourceBody.prefab")]
        [TestCase("Assets/Art/Characters/Human/BodyReview/Outfit02/Outfit02Body.prefab")]
        public void SkinPartitionPreservesGeometryAndClothing(string path)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Baseline/ModularCharacters.fbx");
            var target = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var clothing = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Characters/Baseline/Hero.mat");
            var native = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Characters/Human/Human.mat");
            int skinParts = 0;
            foreach (var renderer in target.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.name.StartsWith("SOURCE / ")))
            {
                var original = source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => "SOURCE / " + r.name == renderer.name);
                var mesh = renderer.sharedMesh;
                Assert.AreEqual(original.sharedMesh.vertexCount, mesh.vertexCount, renderer.name);
                CollectionAssert.AreEquivalent(original.sharedMesh.triangles, mesh.triangles, renderer.name);
                Assert.AreEqual(clothing, renderer.sharedMaterials[0]);
                var uv = mesh.uv; var originalUv = original.sharedMesh.uv;
                foreach (int vertex in mesh.GetTriangles(0).Distinct()) Assert.AreEqual(originalUv[vertex], uv[vertex], renderer.name);
                if (mesh.subMeshCount == 1) continue;
                skinParts++;
                Assert.AreEqual(native, renderer.sharedMaterials[1]);
                Assert.Greater(mesh.GetTriangles(1).Length, 0);
                Assert.AreEqual(1, mesh.GetTriangles(1).Select(i => uv[i]).Distinct().Count());
                Assert.AreEqual(5, mesh.blendShapeCount);
            }
            Assert.Greater(skinParts, 0, "The review must exercise skin remapping.");
        }
    }
}
