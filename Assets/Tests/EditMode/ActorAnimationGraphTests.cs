using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorAnimationGraphTests
    {
        static AnimationClip Clip(string name, float lower, float upper)
        {
            var clip = new AnimationClip { name = name };
            clip.SetCurve("Lower", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0, 2, lower));
            clip.SetCurve("Upper", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0, 2, upper));
            return clip;
        }

        [Test]
        public void BaseAndLayerClipsPreserveNativeFootGoalsWithoutPlayableIK()
        {
            var root = new GameObject("Native foot goal policy");
            var clip = Clip("Authored", 0f, 0f);
            try
            {
                using var graph = new ActorAnimationGraph(root.AddComponent<Animator>(), "Foot goal policy", 1);
                var baseline = graph.AddBaseClip(0, clip);
                graph.AddLayer(clip);
                var layer = (AnimationClipPlayable)graph.BaseMixer.GetGraph().GetOutput(0).GetSourcePlayable().GetInput(1);
                foreach (var playable in new[] { baseline, layer })
                {
                    Assert.IsTrue(playable.GetApplyFootIK(), "Humanoid retargeting must retain the clip foot goals before procedural contacts.");
                    Assert.IsFalse(playable.GetApplyPlayableIK(), "Playable IK must not introduce another contact writer.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void MaskedLayersPreserveLowerBodyAndZeroWeightRestoresBase(bool additive)
        {
            var root = new GameObject("Layer fixture");
            var lower = new GameObject("Lower").transform; lower.SetParent(root.transform, false);
            var upper = new GameObject("Upper").transform; upper.SetParent(root.transform, false);
            var animator = root.AddComponent<Animator>();
            var avatar = AvatarBuilder.BuildGenericAvatar(root, ""); animator.avatar = avatar;
            var baseline = Clip("Base", 2f, 1f);
            baseline.SetCurve("Lower", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 2, 2, 4));
            var overlay = Clip("Upper overlay", 9f, 0f);
            // Unity derives additive deltas from the clip reference pose. The first frame is neutral.
            overlay.SetCurve("Upper", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1, 1));
            var mask = new AvatarMask { transformCount = 3 };
            mask.SetTransformPath(0, ""); mask.SetTransformActive(0, true);
            mask.SetTransformPath(1, "Lower"); mask.SetTransformActive(1, false);
            mask.SetTransformPath(2, "Upper"); mask.SetTransformActive(2, true);
            try
            {
                using var graph = new ActorAnimationGraph(animator, "Layer test", 1);
                graph.AddBaseClip(0, baseline); graph.BaseMixer.SetInputWeight(0, 1f);
                graph.Evaluate();
                Assert.That(lower.localPosition.x, Is.EqualTo(2f).Within(.0001f));
                Assert.That(upper.localPosition.x, Is.EqualTo(1f).Within(.0001f));
                var layer = graph.AddLayer(overlay, mask, additive);
                layer.Weight = 1f; layer.Time = .5; layer.Speed = 0;
                graph.Advance(.1f); graph.Evaluate();
                Assert.That(layer.Time, Is.EqualTo(.5));
                Assert.That(lower.localPosition.x, Is.EqualTo(2.1f).Within(.0001f), "Upper-body mask must preserve base locomotion.");
                Assert.That(upper.localPosition.x, Is.EqualTo(additive ? 1.5f : .5f).Within(.0001f));
                layer.Weight = 0f; graph.Evaluate();
                Assert.That(upper.localPosition.x, Is.EqualTo(1f).Within(.0001f));
                Assert.That(animator.fireEvents, Is.False);
                graph.Dispose(); graph.Dispose();
                Assert.That(graph.IsValid(), Is.False);
                Assert.Throws<ObjectDisposedException>(() => layer.Weight = 1f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(baseline); UnityEngine.Object.DestroyImmediate(overlay);
                UnityEngine.Object.DestroyImmediate(mask);
            }
        }

        [Test]
        public void SkippedEvaluationAdvancesBaseAndOverlayClocksIdentically()
        {
            var a = new GameObject("Sampled"); var b = new GameObject("Skipped");
            var clip = new AnimationClip();
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 2, 2));
            try
            {
                using var first = new ActorAnimationGraph(a.AddComponent<Animator>(), "Sampled", 1);
                using var second = new ActorAnimationGraph(b.AddComponent<Animator>(), "Skipped", 1);
                var firstBase = first.AddBaseClip(0, clip); var secondBase = second.AddBaseClip(0, clip);
                first.BaseMixer.SetInputWeight(0, 1); second.BaseMixer.SetInputWeight(0, 1);
                var firstLayer = first.AddLayer(clip); var secondLayer = second.AddLayer(clip);
                firstLayer.Speed = secondLayer.Speed = .7;
                firstLayer.Weight = secondLayer.Weight = .5f;
                for (int i = 0; i < 60; i++)
                {
                    first.Advance(1f / 60f); first.Evaluate();
                    second.Advance(1f / 60f);
                }
                second.Evaluate();
                Assert.That(secondLayer.Time, Is.EqualTo(firstLayer.Time));
                Assert.That(secondBase.GetTime(), Is.EqualTo(firstBase.GetTime()));
                Assert.That(Vector3.Distance(a.transform.position, b.transform.position), Is.LessThan(.00001f));
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(clip); }
        }

        [Test]
        public void StateChangesAndInterruptionsCrossfadeWithoutLosingWeight()
        {
            var root = new GameObject("Crossfade");
            var clip = new AnimationClip();
            try
            {
                using var graph = new ActorAnimationGraph(root.AddComponent<Animator>(), "Crossfade", 2);
                graph.AddBaseClip(0, clip); graph.AddBaseClip(1, clip);
                graph.BaseMixer.SetInputWeight(0, 0f); graph.BaseMixer.SetInputWeight(1, 1f);
                graph.BlendBaseWeights(.02f);
                float incoming = graph.BaseMixer.GetInputWeight(1);
                Assert.That(incoming, Is.InRange(.1f, .5f));
                graph.BaseMixer.SetInputWeight(0, 1f); graph.BaseMixer.SetInputWeight(1, 0f);
                graph.BlendBaseWeights(.01f);
                Assert.That(graph.BaseMixer.GetInputWeight(1), Is.InRange(.01f, incoming));
                Assert.AreEqual(1f, graph.BaseMixer.GetInputWeight(0) + graph.BaseMixer.GetInputWeight(1), .0001f);
                graph.ResetBlend();
                graph.BaseMixer.SetInputWeight(0, 1f); graph.BaseMixer.SetInputWeight(1, 0f);
                graph.BlendBaseWeights(0f);
                Assert.AreEqual(1f, graph.BaseMixer.GetInputWeight(0));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(clip); }
        }
    }
}
