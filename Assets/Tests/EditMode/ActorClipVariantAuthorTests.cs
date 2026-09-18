using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorClipVariantAuthorTests
    {
        string _folder;
        AnimationClip _source;
        static readonly EditorCurveBinding Binding = EditorCurveBinding.FloatCurve("Body", typeof(Transform), "m_LocalPosition.x");

        [SetUp]
        public void SetUp()
        {
            string name = "ClipVariantTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            _folder = "Assets/" + name;
            _source = new AnimationClip();
            AnimationUtility.SetEditorCurve(_source, Binding, AnimationCurve.Linear(0f, 1f, 2f, 3f));
            AnimationUtility.SetAnimationEvents(_source, new[] { new AnimationEvent { time = 1f, functionName = "Contact", intParameter = 17 } });
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_source);
            AssetDatabase.DeleteAsset(_folder);
        }

        AnimationClip Create(string name, float speed, string bone, Vector3 offset, bool reverse = false)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ActorClipVariantAuthor")).First(t => t != null);
            try { return (AnimationClip)type.GetMethod("Create").Invoke(null, new object[] { _source, _folder + "/" + name + ".anim", speed, bone, offset, reverse }); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        [Test]
        public void VariantRetimesMotionAndEventsWithoutChangingOriginal()
        {
            var clip = Create("Fast", 2f, "Body", Vector3.right * .4f);
            var curve = AnimationUtility.GetEditorCurve(clip, Binding);
            Assert.That(clip.length, Is.EqualTo(1f).Within(.0001f));
            for (int i = 0; i <= 10; i++)
                Assert.That(curve.Evaluate(i / 10f), Is.EqualTo(1.4f + i / 5f).Within(.0001f));
            var events = AnimationUtility.GetAnimationEvents(clip);
            Assert.That(events.Single().time, Is.EqualTo(.5f));
            Assert.That(events.Single().intParameter, Is.EqualTo(17));
            Assert.That(AnimationUtility.GetEditorCurve(_source, Binding).Evaluate(1f), Is.EqualTo(2f).Within(.0001f));
            Assert.That(AnimationUtility.GetAnimationEvents(_source).Single().time, Is.EqualTo(1f));
        }

        [Test]
        public void ReversePreservesWeightedMotionShapeAndReordersEvents()
        {
            var first = new Keyframe(0f, 1f, .2f, 2f, .2f, .7f) { weightedMode = WeightedMode.Out };
            var last = new Keyframe(2f, 3f, -.3f, .4f, .3f, .5f) { weightedMode = WeightedMode.In };
            AnimationUtility.SetEditorCurve(_source, Binding, new AnimationCurve(first, last));
            AnimationUtility.SetAnimationEvents(_source, new[]
            {
                new AnimationEvent { time = .3f, functionName = "Start" },
                new AnimationEvent { time = 1.7f, functionName = "Finish" },
            });
            var original = AnimationUtility.GetEditorCurve(_source, Binding);
            var clip = Create("Reverse", 2f, "Body", Vector3.zero, true);
            var reversed = AnimationUtility.GetEditorCurve(clip, Binding);
            for (int i = 0; i <= 20; i++)
                Assert.That(reversed.Evaluate(i / 20f), Is.EqualTo(original.Evaluate(2f - i / 10f)).Within(.002f));
            var events = AnimationUtility.GetAnimationEvents(clip);
            Assert.That(events[0].functionName, Is.EqualTo("Finish"));
            Assert.That(events[0].time, Is.EqualTo(.15f).Within(.0001f));
            Assert.That(events[1].time, Is.EqualTo(.85f).Within(.0001f));
            Assert.That(AnimationUtility.GetAnimationEvents(_source)[0].functionName, Is.EqualTo("Start"));
        }

        [Test]
        public void ReverseRejectsSteppedMotionWithoutCreatingAsset()
        {
            AnimationUtility.SetEditorCurve(_source, Binding, new AnimationCurve(
                new Keyframe(0f, 0f, 0f, float.PositiveInfinity), new Keyframe(2f, 1f)));
            Assert.Throws<ArgumentException>(() => Create("Stepped", 1f, "Body", Vector3.zero, true));
            Assert.That(AssetDatabase.LoadMainAssetAtPath(_folder + "/Stepped.anim"), Is.Null);
        }

        [Test]
        public void RejectedVariantCannotOverwriteOrCreatePartialAsset()
        {
            var existing = Create("Keep", 1f, "Body", Vector3.zero);
            Assert.Throws<System.IO.IOException>(() => Create("Keep", 2f, "Body", Vector3.zero));
            Assert.That(existing.length, Is.EqualTo(2f).Within(.0001f));
            Assert.Throws<ArgumentException>(() => Create("MissingBone", 1f, "Missing", Vector3.right));
            Assert.That(AssetDatabase.LoadMainAssetAtPath(_folder + "/MissingBone.anim"), Is.Null);
            Assert.Throws<ArgumentOutOfRangeException>(() => Create("InvalidSpeed", 0f, "Body", Vector3.zero));
        }
    }
}
