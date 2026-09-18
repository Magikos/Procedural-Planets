using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class HumanCapeClothTests
    {
        [Test]
        public void SubdividedCapePreservesSourceSurfaceAndSkinning()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Human/Review/Townsfolk_Capes.fbx");
            var source = model.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "SM_Chr_Mage_Cape_01").sharedMesh;
            var derived = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Characters/Human/Review/MageCapeCloth.asset");
            Assert.IsNotNull(derived);
            Assert.AreEqual(source.triangles.Length * 16, derived.triangles.Length);
            CollectionAssert.AreEqual(source.vertices, derived.vertices.Take(source.vertexCount));
            CollectionAssert.AreEqual(source.uv, derived.uv.Take(source.vertexCount));
            CollectionAssert.AreEqual(source.bindposes, derived.bindposes);
            Assert.That(Vector3.Distance(source.bounds.size, derived.bounds.size), Is.LessThan(.00001f));
            foreach (var weight in derived.boneWeights)
                Assert.That(weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3, Is.EqualTo(1).Within(.00001f));
            float Area(Mesh mesh)
            {
                var vertices = mesh.vertices; var triangles = mesh.triangles; float total = 0;
                for (int i = 0; i < triangles.Length; i += 3)
                    total += Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]],
                        vertices[triangles[i + 2]] - vertices[triangles[i]]).magnitude * .5f;
                return total;
            }
            Assert.That(Area(derived), Is.EqualTo(Area(source)).Within(.0001f));
        }
    }
}
