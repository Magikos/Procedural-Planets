using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class HumanAccessoryMotionContinuityTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void DisablingAccessoryMotionReleasesAndCanReverse(bool disableComponent)
        {
            var host = new GameObject("accessory continuity");
            try
            {
                var review = host.AddComponent<HumanStyleReview>();
                var motion = host.AddComponent<HumanAccessoryMotion>();
                var bone = new GameObject("strap root").transform; bone.SetParent(host.transform, false);
                var middle = new GameObject("strap middle").transform; middle.SetParent(bone, false);
                var tip = new GameObject("strap tip").transform; tip.SetParent(middle, false);
                middle.localPosition = tip.localPosition = Vector3.forward * .5f;
                motion.Review = review;
                motion.Chain = new SpringChainDefinition
                {
                    Bones = new[] { bone, middle, tip }, Frequency = .5f, Damping = .8f,
                    GravityScale = 1f, Weight = 1f, AngleLimit = 45f
                };
                for (int i = 0; i < 240; i++) motion.Tick(1f / 60f);
                Quaternion before = bone.localRotation;
                Assert.Greater(Quaternion.Angle(Quaternion.identity, before), 10f);
                if (disableComponent)
                {
                    motion.enabled = false;
                    typeof(HumanAccessoryMotion).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(motion, null);
                }
                else review.SecondaryMotion = false;
                motion.Tick(1f / 60f);
                Assert.Less(Quaternion.Angle(before, bone.localRotation), 10f,
                    "Visible accessory motion must not reset when disabled.");
                for (int i = 0; i < 2; i++) motion.Tick(1f / 60f);
                before = bone.localRotation;
                motion.enabled = true; review.SecondaryMotion = true;
                motion.Tick(1f / 60f);
                Assert.Less(Quaternion.Angle(before, bone.localRotation), 10f);
                review.SecondaryMotion = false;
                for (int i = 0; i < 60; i++) motion.Tick(1f / 60f);
                Assert.Less(Quaternion.Angle(Quaternion.identity, bone.localRotation), .01f);
            }
            finally { Object.DestroyImmediate(host); }
        }
    }
}
