using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class HumanAccessoryRefinementTests
    {
        const string Folder = "Assets/Art/Characters/Human/BodyReview/AccessoryMotion/";

        [Test]
        public void CupPartitionKeepsEveryBackpackTriangleExactlyOnce()
        {
            var original = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Human/Review/BagExplorer_01.fbx").GetComponentInChildren<MeshFilter>().sharedMesh;
            var bag = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "BackpackWithoutCup.asset");
            var cup = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "HangingCup.asset");
            Assert.AreEqual(original.triangles.Length, bag.triangles.Length + cup.triangles.Length);
            var all = Triangles(bag); Assert.IsFalse(all.Overlaps(Triangles(cup)));
            all.UnionWith(Triangles(cup)); CollectionAssert.AreEquivalent(Triangles(original), all);
            CollectionAssert.AreEqual(original.vertices, cup.vertices);
            CollectionAssert.AreEqual(original.uv, cup.uv);
        }

        [TestCase("Original")]
        [TestCase("Fit")]
        public void RobeRefinementKeepsFeetAndCachedBodyShapes(string rig)
        {
            string name = "Monk_01_" + rig;
            var reference = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Human/BodyReview/Townsfolk/" + name + ".prefab")
                .GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name.Contains("SM_Chr_Monk_01"));
            var source = reference.sharedMesh;
            var refined = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + name + "_Robe.asset");
            Assert.Less(refined.triangles.Length, source.triangles.Length);
            Assert.AreEqual(source.blendShapeCount, refined.blendShapeCount);
            var kept = Triangles(refined); var vertices = source.vertices;
            foreach (var t in Triangles(source).Where(t => vertices[t.Item1].y < .1f && vertices[t.Item2].y < .1f && vertices[t.Item3].y < .1f))
                Assert.IsTrue(kept.Contains(t), "The foot and hem triangles must remain.");
            for (int i = 0; i < source.blendShapeCount; i++)
            {
                var before = new Vector3[source.vertexCount]; var after = new Vector3[refined.vertexCount];
                source.GetBlendShapeFrameVertices(i, 0, before, null, null);
                refined.GetBlendShapeFrameVertices(i, 0, after, null, null);
                // Rebuilding sparse Unity shape frames omitted deltas up to 7.35 micrometres in this asset.
                for (int v = 0; v < before.Length; v++)
                    Assert.That(Vector3.Distance(before[v], after[v]), Is.LessThanOrEqualTo(.00001f), source.GetBlendShapeName(i));
            }
        }

        static HashSet<(int, int, int)> Triangles(Mesh mesh)
        {
            var result = new HashSet<(int, int, int)>(); var indices = mesh.triangles;
            for (int i = 0; i < indices.Length; i += 3) result.Add((indices[i], indices[i + 1], indices[i + 2]));
            return result;
        }
    }
}
