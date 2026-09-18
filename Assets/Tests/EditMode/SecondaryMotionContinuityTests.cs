using System;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class SecondaryMotionContinuityTests
    {
        sealed class Tail : IDisposable
        {
            readonly GameObject _root = new("secondary continuity");
            public readonly Transform Bone;
            public readonly ProceduralPoseRig Pose;

            public Tail()
            {
                Bone = new GameObject("tail root").transform;
                Bone.SetParent(_root.transform, false);
                var middle = new GameObject("tail middle").transform;
                middle.SetParent(Bone, false); middle.localPosition = Vector3.forward * .5f;
                var tip = new GameObject("tail tip").transform;
                tip.SetParent(middle, false); tip.localPosition = Vector3.forward * .5f;
                var definition = _root.AddComponent<ProceduralRigDefinition>();
                definition.Chains = new[] { new SpringChainDefinition
                {
                    Bones = new[] { Bone, middle, tip }, Frequency = .5f, Damping = .8f,
                    GravityScale = 1f, Weight = 1f, AngleLimit = 45f
                } };
                Pose = new ProceduralPoseRig(_root.transform, definition);
            }

            public void Step(float dt = 1f / 60f)
            {
                Pose.RestoreAnimation(); Pose.CaptureAnimation();
                Pose.Tick(Vector3.up, Vector3.forward, null, 0f, 0f, 0f, dt);
            }

            public void Settle() { for (int i = 0; i < 240; i++) Step(); }
            public void Dispose() => UnityEngine.Object.DestroyImmediate(_root);
        }

        [Test]
        public void DisablingSecondaryMotionReleasesTheDisplayedCorrection()
        {
            using var tail = new Tail();
            tail.Settle();
            Quaternion before = tail.Bone.localRotation;
            float correction = Quaternion.Angle(Quaternion.identity, before);
            Assert.Greater(correction, 10f, "The fixture must contain a visible outgoing secondary pose.");
            tail.Pose.ChainsEnabled = false;
            tail.Step();
            Assert.Less(Quaternion.Angle(before, tail.Bone.localRotation), correction * .5f,
                "Disabling a chain must not discard the displayed correction in one frame.");
            for (int i = 0; i < 60; i++) tail.Step();
            Assert.Less(Quaternion.Angle(Quaternion.identity, tail.Bone.localRotation), .01f);
        }

        [Test]
        public void ReenablingDuringReleaseContinuesFromTheDisplayedWeight()
        {
            using var tail = new Tail();
            tail.Settle();
            tail.Pose.ChainsEnabled = false;
            for (int i = 0; i < 3; i++) tail.Step();
            Quaternion before = tail.Bone.localRotation;
            tail.Pose.ChainsEnabled = true;
            tail.Step();
            Assert.Less(Quaternion.Angle(before, tail.Bone.localRotation), 10f);
            for (int i = 0; i < 60; i++) tail.Step();
            Assert.Greater(Quaternion.Angle(Quaternion.identity, tail.Bone.localRotation), 10f);
        }

        [Test]
        public void AFrameHitchRetainsSettledSecondaryState()
        {
            using var tail = new Tail();
            tail.Settle();
            Quaternion before = tail.Bone.localRotation;
            Assert.Greater(Quaternion.Angle(Quaternion.identity, before), 10f);
            tail.Step(.5f);
            Assert.Less(Quaternion.Angle(before, tail.Bone.localRotation), 5f,
                "A frame hitch must not reset settled secondary motion to its authored pose.");
        }
    }
}
