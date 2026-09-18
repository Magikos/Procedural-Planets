using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace ProceduralPlanets.Tests
{
    public sealed class HumanoidWalkArmAuthorTests
    {
        const string Folder = "Assets/Art/Characters/Animations/";

        [Test]
        public void SubduedWalkScalesDonorSwayRangeAndPreservesMean()
        {
            var donor = AssetDatabase.LoadAllAssetsAtPath(Folder + "Neutral Male Walk.fbx")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var derived = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "Relaxed Walk Forward.anim");
            Assert.IsNotNull(derived);
            float offset = AnimationUtility.GetAnimationClipSettings(donor).cycleOffset -
                AnimationUtility.GetAnimationClipSettings(derived).cycleOffset;
            int tested = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(donor))
            {
                if (binding.type != typeof(Animator) || !new[] { "Spine ", "Chest ", "UpperChest ", "Left Shoulder ", "Right Shoulder " }
                    .Any(prefix => binding.propertyName.StartsWith(prefix, StringComparison.Ordinal))) continue;
                var original = AnimationUtility.GetEditorCurve(donor, binding);
                var changed = AnimationUtility.GetEditorCurve(derived, binding);
                Assert.IsNotNull(changed, binding.propertyName);
                float originalMin = float.PositiveInfinity, originalMax = float.NegativeInfinity;
                float changedMin = float.PositiveInfinity, changedMax = float.NegativeInfinity;
                float originalMean = 0f, changedMean = 0f;
                for (int i = 0; i < 128; i++)
                {
                    float a = original.Evaluate(Mathf.Repeat(i / 128f + offset, 1f) * donor.length);
                    float b = changed.Evaluate(i * derived.length / 128f);
                    originalMin = Mathf.Min(originalMin, a); originalMax = Mathf.Max(originalMax, a);
                    changedMin = Mathf.Min(changedMin, b); changedMax = Mathf.Max(changedMax, b);
                    originalMean += a / 128f; changedMean += b / 128f;
                }
                Assert.AreEqual(originalMean, changedMean, .00001f, binding.propertyName + " mean");
                Assert.AreEqual((originalMax - originalMin) * .4f, changedMax - changedMin, .00001f, binding.propertyName + " range");
                tested++;
            }
            Assert.Greater(tested, 3);
        }
        [TestCase("Forward")]
        [TestCase("Left")]
        [TestCase("Right")]
        [TestCase("Backward")]
        [TestCase("ForwardLeft")]
        [TestCase("ForwardRight")]
        [TestCase("BackwardLeft")]
        [TestCase("BackwardRight")]
        public void DerivedWalkPreservesLowerBodyCurvesAndSamplingButChangesArms(string direction)
        {
            var source = AssetDatabase.LoadAllAssetsAtPath(Folder + "Walk " + direction + ".fbx")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var derived = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "Relaxed Walk " + direction + ".anim");
            Assert.IsNotNull(derived);
            Assert.IsTrue(derived.isHumanMotion);
            Assert.AreEqual(source.length, derived.length, .0001f);
            var originalSettings = AnimationUtility.GetAnimationClipSettings(source);
            var derivedSettings = AnimationUtility.GetAnimationClipSettings(derived);
            Assert.AreEqual(originalSettings.cycleOffset, derivedSettings.cycleOffset);
            Assert.AreEqual(originalSettings.loopTime, derivedSettings.loopTime);
            Assert.AreEqual(originalSettings.startTime, derivedSettings.startTime);
            Assert.AreEqual(originalSettings.stopTime, derivedSettings.stopTime);
            Assert.AreEqual(originalSettings.keepOriginalPositionY, derivedSettings.keepOriginalPositionY);
            Assert.AreEqual(originalSettings.heightFromFeet, derivedSettings.heightFromFeet);
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                if (IsUpperBody(binding.propertyName)) continue;
                var original = AnimationUtility.GetEditorCurve(source, binding);
                var copy = AnimationUtility.GetEditorCurve(derived, binding);
                Assert.IsNotNull(copy, binding.propertyName);
                CollectionAssert.AreEqual(original.keys, copy.keys, binding.propertyName);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Baseline/Baseline.prefab");
            var first = Object.Instantiate(prefab);
            var second = Object.Instantiate(prefab);
            var graph = PlayableGraph.Create("Relaxed walk regression");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var firstAnimator = first.GetComponent<Animator>();
                var secondAnimator = second.GetComponent<Animator>();
                firstAnimator.applyRootMotion = secondAnimator.applyRootMotion = false;
                firstAnimator.cullingMode = secondAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var sourcePlayable = AnimationClipPlayable.Create(graph, source);
                var derivedPlayable = AnimationClipPlayable.Create(graph, derived);
                sourcePlayable.SetSpeed(0d); derivedPlayable.SetSpeed(0d);
                AnimationPlayableOutput.Create(graph, "Original", firstAnimator).SetSourcePlayable(sourcePlayable);
                AnimationPlayableOutput.Create(graph, "Relaxed", secondAnimator).SetSourcePlayable(derivedPlayable);
                graph.Play();
                float maximumArmDifference = 0f;
                for (int sample = 0; sample < 32; sample++)
                {
                    double time = source.length * sample / 32d;
                    sourcePlayable.SetTime(time); derivedPlayable.SetTime(time); graph.Evaluate(0f);
                    foreach (var bone in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
                    {
                        Vector3 originalPosition = first.transform.InverseTransformPoint(firstAnimator.GetBoneTransform(bone).position);
                        Vector3 derivedPosition = second.transform.InverseTransformPoint(secondAnimator.GetBoneTransform(bone).position);
                        Assert.Less(Vector3.Distance(originalPosition, derivedPosition), .02f, direction + " " + bone + " sample " + sample);
                    }
                    foreach (var bone in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm })
                        maximumArmDifference = Mathf.Max(maximumArmDifference, Quaternion.Angle(firstAnimator.GetBoneTransform(bone).localRotation,
                            secondAnimator.GetBoneTransform(bone).localRotation));
                }
                Assert.Greater(maximumArmDifference, 3f, "Edited muscle curves must affect evaluated Humanoid motion.");
                Assert.AreEqual(first.transform.position, second.transform.position);
            }
            finally { graph.Destroy(); Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        static bool IsUpperBody(string property) =>
            new[] { "Spine ", "Chest ", "UpperChest ", "Left Shoulder", "Right Shoulder", "Left Arm", "Right Arm", "Left Forearm", "Right Forearm", "Left Hand", "Right Hand", "LeftHand.", "RightHand." }
                .Any(prefix => property.StartsWith(prefix, StringComparison.Ordinal));
    }
}

