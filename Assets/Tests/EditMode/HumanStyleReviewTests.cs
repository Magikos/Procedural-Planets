using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class HumanStyleReviewTests
    {
        [Test]
        public void MotionChangesAnimateBothRigsWithoutMovingTheirDisplayRoots()
        {
            var root = new GameObject("Style review test"); root.SetActive(false);
            try
            {
                var review = root.AddComponent<HumanStyleReview>();
                review.Characters = new[] {
                    Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Human/Human.prefab"), root.transform).GetComponent<Animator>(),
                    Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Baseline/Baseline.prefab"), root.transform).GetComponent<Animator>() };
                review.Motions = new[] { "HumanoidIdle", "HumanoidRun" }.Select(name =>
                    AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Characters/Animations/" + name + ".fbx")
                        .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"))).ToArray();
                for (int i = 0; i < review.Characters.Length; i++) review.Characters[i].transform.position = new Vector3(i * 3f, 0f, 0f);
                var positions = review.Characters.Select(a => a.transform.position).ToArray();
                root.SetActive(true); review.SelectMotion(0); review.Phase = .1f; review.Sample();
                var initial = review.Characters.Select(a => a.GetBoneTransform(HumanBodyBones.LeftUpperLeg).localRotation).ToArray();
                for (int pass = 0; pass < 3; pass++)
                {
                    review.SelectMotion(1); review.Phase = .35f; review.Sample();
                    for (int i = 0; i < review.Characters.Length; i++)
                    {
                        Assert.Less(Vector3.Distance(positions[i], review.Characters[i].transform.position), .0001f);
                        Assert.Greater(Quaternion.Angle(initial[i], review.Characters[i].GetBoneTransform(HumanBodyBones.LeftUpperLeg).localRotation), 2f);
                    }
                    review.SelectMotion(0);
                }
                root.SetActive(false); root.SetActive(true); review.Sample();
                for (int i = 0; i < review.Characters.Length; i++)
                    Assert.Less(Vector3.Distance(positions[i], review.Characters[i].transform.position), .0001f);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
