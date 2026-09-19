using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    [TestFixture("Assets/Art/Characters/Human/BodyReview/SourcePartsBody.prefab", 5)]
    [TestFixture("Assets/Art/Characters/Human/BodyReview/FullBody/FullSourceBody.prefab", 10)]
    [TestFixture("Assets/Art/Characters/Human/BodyReview/Outfit02/Outfit02Body.prefab", 10)]
    [TestFixture("Assets/Art/Characters/Human/BodyReview/Townsfolk/Monk_01_Fit.prefab", 1)]
    [TestFixture("Assets/Art/Characters/Human/BodyReview/Townsfolk/Peasant_Male_01_Fit.prefab", 1)]
    public sealed class HumanBodyShapeTests
    {
        readonly string _prefabPath;
        readonly int _partCount;

        public HumanBodyShapeTests(string prefabPath, int partCount) { _prefabPath = prefabPath; _partCount = partCount; }
        [TestCase("defaultBuff", "Muscular")]
        [TestCase("defaultHeavy", "Heavy")]
        [TestCase("defaultSkinny", "Skinny")]
        [TestCase("masculineFeminine", "Feminine")]
        public void SavedShapeMovesEveryConvertedPartAndResets(string shape, string control)
        {
            var root = new GameObject("Body shape test"); root.SetActive(false);
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabPath);
                var character = Object.Instantiate(prefab, root.transform);
                var skins = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var review = root.AddComponent<HumanBodyReview>();
                review.ConvertedParts = skins.Where(r => r.name.StartsWith("SOURCE / ")).ToArray();
                review.NativeParts = skins.Where(r => !r.name.StartsWith("SOURCE / ")).ToArray();
                Assert.AreEqual(_partCount, review.ConvertedParts.Length);
                foreach (var skin in review.ConvertedParts)
                {
                    var mesh = skin.sharedMesh;
                    Assert.AreEqual(5, mesh.blendShapeCount);
                    int index = mesh.GetBlendShapeIndex("BodyBlends." + shape);
                    Assert.Greater(index, 0, skin.name);
                    Assert.AreEqual(100, mesh.GetBlendShapeFrameWeight(index, 0));
                    var delta = new Vector3[mesh.vertexCount]; var normals = new Vector3[mesh.vertexCount];
                    mesh.GetBlendShapeFrameVertices(index, 0, delta, normals, null);
                    Assert.IsTrue(delta.All(Finite) && normals.All(Finite), skin.name);
                    Assert.Greater(delta.Max(v => v.magnitude), .0001f, skin.name);
                }
                root.SetActive(true);
                typeof(HumanBodyReview).GetField(control).SetValue(review, 100f);
                review.ApplyBodyShapes();
                foreach (var skin in review.ConvertedParts)
                    Assert.AreEqual(100, skin.GetBlendShapeWeight(skin.sharedMesh.GetBlendShapeIndex("BodyBlends." + shape)));
                review.SkeletonFit = 0; review.ApplyBodyShapes();
                foreach (var skin in review.ConvertedParts)
                    Assert.AreEqual(0, skin.GetBlendShapeWeight(skin.sharedMesh.GetBlendShapeIndex("BodyBlends." + shape)));
                var torso = review.NativeParts.First(r => r.name.Contains("01HEAD"));
                int nativeIndex = Enumerable.Range(0, torso.sharedMesh.blendShapeCount).First(i => torso.sharedMesh.GetBlendShapeName(i).EndsWith(shape));
                Assert.AreEqual(100, torso.GetBlendShapeWeight(nativeIndex), "Native shape must remain independent of the source fit slider.");
                typeof(HumanBodyReview).GetField(control).SetValue(review, 0f);
                review.SkeletonFit = 100; review.ApplyBodyShapes();
                foreach (var skin in review.ConvertedParts)
                    for (int i = 1; i < skin.sharedMesh.blendShapeCount; i++) Assert.AreEqual(0, skin.GetBlendShapeWeight(i));
            }
            finally { Object.DestroyImmediate(root); }
        }

        static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
