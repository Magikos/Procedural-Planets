using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ProceduralPlanets.Tests
{
    public sealed class HumanoidCrawlStrafeAuthorTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void LateralBakeRetainsBodyCurvesAndClosesLimbCycle(bool left)
        {
            const string folder = "Assets/Art/Characters/";
            var source = AssetDatabase.LoadAllAssetsAtPath(folder + "Animations/Basic Crawl Forward.fbx")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "Baseline/Baseline.prefab");
            var result = AssetDatabase.LoadAssetAtPath<AnimationClip>(folder + "Animations/Crawl " + (left ? "Left" : "Right") + ".anim");
            Assert.IsNotNull(result);
            var actor = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Assert.IsTrue(result.isHumanMotion);
                Assert.AreEqual(source.length, result.length, .0001f);
                int retained = 0;
                foreach (var binding in AnimationUtility.GetCurveBindings(source))
                {
                    if (!(binding.propertyName.StartsWith("Root", StringComparison.Ordinal) ||
                          binding.propertyName.StartsWith("Spine", StringComparison.Ordinal) ||
                          binding.propertyName.StartsWith("Chest", StringComparison.Ordinal))) continue;
                    CollectionAssert.AreEqual(AnimationUtility.GetEditorCurve(source, binding).keys,
                        AnimationUtility.GetEditorCurve(result, binding).keys, binding.propertyName);
                    retained++;
                }
                Assert.Greater(retained, 0);
                var arm = AnimationUtility.GetEditorCurve(result, EditorCurveBinding.FloatCurve("", typeof(Animator), "Left Arm Front-Back"));
                Assert.IsNotNull(arm);
                Assert.AreEqual(arm.Evaluate(0f), arm.Evaluate(result.length), .0001f);
                Assert.IsTrue(arm.keys.All(key => float.IsFinite(key.value)));
                var animator = actor.GetComponent<Animator>();
                using var graph = new ActorAnimationGraph(animator, "Lateral crawl regression", 1);
                var playable = graph.AddBaseClip(0, result);
                graph.BaseMixer.SetInputWeight(0, 1f);
                foreach (var bone in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                {
                    Vector3 min = Vector3.one * 999f, max = -min;
                    for (int frame = 0; frame < 64; frame++)
                    {
                        playable.SetTime(result.length * frame / 64d); graph.Evaluate();
                        Vector3 position = actor.transform.InverseTransformPoint(animator.GetBoneTransform(bone).position);
                        min = Vector3.Min(min, position); max = Vector3.Max(max, position);
                    }
                    Vector3 range = max - min;
                    Assert.Greater(range.x, .08f, bone + " must make a lateral stroke.");
                    Assert.Greater(range.x, range.z * 2f, bone + " must not retain the source forward goal trajectory.");
                    Assert.Greater(bone == HumanBodyBones.LeftFoot ? -max.x : min.x, .09f,
                        "The inward stroke must leave room for the other boot.");
                }
                Vector3 minimum = Vector3.one * float.PositiveInfinity, maximum = Vector3.one * float.NegativeInfinity;
                for (int i = 0; i < 64; i++)
                {
                    playable.SetTime(result.length * i / 64f); graph.Evaluate();
                    Vector3 point = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                    minimum = Vector3.Min(minimum, point); maximum = Vector3.Max(maximum, point);
                }
                Vector3 excursion = maximum - minimum;
                Assert.Greater(excursion.x, excursion.z * 1.3f, "The hand stroke must travel sideways.");
                Assert.Greater(minimum.x, 0f, "The right hand must stay on its side of the body.");
            }
            finally { UnityEngine.Object.DestroyImmediate(actor); }
        }
    }
}
