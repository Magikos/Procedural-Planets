using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class CreaturePresentationCadenceTests
    {
        [Test]
        public void NearAndMissingObserverKeepFullRateWithHysteresis()
        {
            var cadence = new ActorPoseCadence();
            Assert.That(cadence.ShouldEvaluate(1, Vector3.zero, 29, true, true, 0), Is.True);
            for (int i = 0; i < 20; i++)
            {
                cadence.BeginFrame(.001f);
                Assert.That(cadence.ShouldEvaluate(1, Vector3.zero, 34, true, true, 0), Is.True);
                Assert.That(cadence.ShouldEvaluate(2, Vector3.zero, 1000, false, false, 0), Is.True);
            }
        }

        [Test]
        public void StableIdentityStaggerHasNoStarvationOrCatchup()
        {
            var first = new ActorPoseCadence(); var second = new ActorPoseCadence();
            var count = new int[32]; int mixedFrames = 0;
            for (int frame = 0; frame < 240; frame++)
            {
                first.BeginFrame(1f / 60f); second.BeginFrame(1f / 60f);
                int due = 0;
                for (ulong id = 0; id < 32; id++)
                {
                    bool a = first.ShouldEvaluate(id, Vector3.zero, 100, false, true, 0);
                    Assert.That(second.ShouldEvaluate(id, Vector3.zero, 100, false, true, 0), Is.EqualTo(a));
                    if (a) { count[id]++; due++; }
                }
                if (due > 0 && due < 32) mixedFrames++;
            }
            foreach (int samples in count) Assert.That(samples, Is.InRange(19, 22));
            Assert.That(mixedFrames, Is.GreaterThan(100));
            first.BeginFrame(100f);
            Assert.That(first.ShouldEvaluate(1, Vector3.zero, 100, false, true, 0), Is.True);
            Assert.That(first.ShouldEvaluate(1, Vector3.zero, 100, false, true, 0), Is.False);
        }

        [Test]
        public void VisibilityActionTeleportAndLifecycleForceFreshPose()
        {
            var cadence = new ActorPoseCadence();
            Assert.That(cadence.ShouldEvaluate(1, Vector3.zero, 100, false, true, 0), Is.True);
            Assert.That(cadence.ShouldEvaluate(1, Vector3.zero, 100, false, true, 0), Is.False);
            Assert.That(cadence.ShouldEvaluate(1, Vector3.zero, 100, true, true, 0), Is.True);
            Assert.That(cadence.ShouldEvaluate(1, Vector3.zero, 100, true, true, 1), Is.True);
            Assert.That(cadence.ShouldEvaluate(1, Vector3.right * 10, 100, true, true, 1), Is.True);
            Assert.That(cadence.ShouldEvaluate(1, Vector3.right * 10, 100, true, true, 1, critical: true), Is.True);
            cadence.Forget(1); Assert.That(cadence.Count, Is.Zero);
            Assert.That(cadence.ShouldEvaluate(1, Vector3.zero, 100, false, true, 0), Is.True);
            cadence.Clear(); Assert.That(cadence.Count, Is.Zero);
        }

        [Test]
        public void SkippedBirdSamplesRetainCurrentClipPoseWithoutCatchup()
        {
            using var full = new BirdAnimationView(null, 997, .8f, visual: BirdVisualKind.Eagle);
            using var budget = new BirdAnimationView(null, 997, .8f, visual: BirdVisualKind.Eagle);
            for (int i = 0; i < 60; i++)
            {
                full.Tick(false, true, 1f / 60f);
                budget.Tick(false, true, 1f / 60f, evaluatePose: i == 59);
            }
            Assert.That(full.PoseEvaluations, Is.EqualTo(60));
            Assert.That(budget.PoseEvaluations, Is.EqualTo(1));
            var a = new Mesh(); var b = new Mesh();
            try
            {
                full.Root.GetComponentInChildren<SkinnedMeshRenderer>().BakeMesh(a, true);
                budget.Root.GetComponentInChildren<SkinnedMeshRenderer>().BakeMesh(b, true);
                Vector3[] expected = a.vertices, actual = b.vertices;
                Assert.That(actual.Length, Is.EqualTo(expected.Length));
                for (int i = 0; i < expected.Length; i++)
                    Assert.That(Vector3.Distance(actual[i], expected[i]), Is.LessThan(.0001f));
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }
    }
}
