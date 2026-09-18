using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class AuthoredAnimationReferenceTests
    {
        [Test]
        public void AbsoluteSamplingPreservesNaturalTimeAndBakedBodyTravelWhenScrubbing()
        {
            var rig = new GameObject("Reference fixture");
            var bone = new GameObject("Hips");
            bone.transform.SetParent(rig.transform, false);
            rig.AddComponent<Animator>();
            var clip = new AnimationClip();
            // A generic fixture models body travel baked into a child, independently of the Animator root.
            clip.SetCurve("Hips", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 2f, 2f));
            IDisposable reference = null;
            try
            {
                // Editor helpers live outside the runtime test assembly's references.
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("AuthoredAnimationReference")).First(t => t != null);
                reference = (IDisposable)Activator.CreateInstance(type, rig, clip, false);
                var sample = type.GetMethod("Sample");
                var root = (GameObject)type.GetProperty("Root").GetValue(reference);
                root.transform.position = new Vector3(10f, 0f, 0f);
                var animatorTransform = root.GetComponentInChildren<Animator>().transform;
                var sampledBone = animatorTransform.Find("Hips");
                foreach (double time in new[] { 1.5d, .25d, .25d, 5d })
                {
                    sample.Invoke(reference, new object[] { time });
                    Assert.That(type.GetProperty("ElapsedSeconds").GetValue(reference), Is.EqualTo(time));
                    Assert.That(sampledBone.localPosition.x, Is.EqualTo(Math.Min(time, 2d)).Within(.0001d));
                    Assert.That(animatorTransform.localPosition, Is.EqualTo(Vector3.zero));
                    Assert.That(sampledBone.position.x, Is.EqualTo(10d + Math.Min(time, 2d)).Within(.0001d),
                        "Fixing the Animator root must preserve baked body translation in world space.");
                }
                Assert.That(type.GetProperty("Label").GetValue(reference), Does.Contain("RETARGETED"));
                Assert.That(bone.transform.localPosition, Is.EqualTo(Vector3.zero), "The original rig must remain unchanged.");
                reference.Dispose();
                reference.Dispose();
                Assert.That(root == null, Is.True);
            }
            finally
            {
                reference?.Dispose();
                UnityEngine.Object.DestroyImmediate(rig);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }
    }
}
