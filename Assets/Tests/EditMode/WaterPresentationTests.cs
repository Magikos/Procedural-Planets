using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class WaterPresentationTests
    {
        static WaterSample Sample(float depth, ushort body = 1) => new(Vector3.zero, Vector3.up, depth, 10f, body, true);

        [Test]
        public void EntryProducesOneSplashAndTeleportDoesNotProduceTrail()
        {
            var state = new WaterContactTracker();
            Assert.IsFalse(state.Step(Vector3.up, Sample(-1f), .4f, .1f, out _));
            Assert.IsTrue(state.Step(Vector3.up * .2f, Sample(-.2f), .4f, .1f, out var splash));
            Assert.IsTrue(splash.Splash);
            Assert.IsFalse(state.Step(Vector3.up * .1f, Sample(-.1f), .4f, .1f, out _));
            Assert.IsFalse(state.Step(Vector3.right * 100f, Sample(.1f), .4f, .1f, out _));
        }

        [Test]
        public void SurfaceMotionProducesWakeButDeepMotionDoesNot()
        {
            var state = new WaterContactTracker();
            state.Step(Vector3.zero, Sample(.1f), .4f, .1f, out _);
            Assert.IsTrue(state.Step(Vector3.right, Sample(.1f), .4f, .2f, out var wake));
            Assert.IsFalse(wake.Splash);
            Assert.IsFalse(state.Step(Vector3.right * 2f, Sample(5f), .4f, .2f, out _));
        }

        [Test]
        public void ChangingWaterBodyAndInvalidInputResetContactHistory()
        {
            var state = new WaterContactTracker();
            state.Step(Vector3.up, Sample(-1f), .4f, .1f, out _);
            Assert.IsFalse(state.Step(Vector3.zero, Sample(.1f, 48), .4f, .1f, out _));
            Assert.IsFalse(state.Step(new Vector3(float.NaN, 0, 0), Sample(.1f), .4f, .1f, out _));
            Assert.IsFalse(state.Step(Vector3.zero, Sample(.1f), .4f, .1f, out _));
        }

        [Test]
        public void ImmersionIsSmoothAndSurfaceNoiseDoesNotRetriggerCrossing()
        {
            var state = new WaterImmersionState();
            state.Tick(-1f, .02f);
            state.Tick(1f, .02f);
            Assert.That(state.Immersion, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.AreEqual(1f, state.Transition);
            state.Tick(.02f, .02f);
            state.Tick(-.02f, .02f);
            Assert.That(state.Transition, Is.LessThan(1f));
            for (int i = 0; i < 100; i++) state.Tick(2f, .02f);
            Assert.That(state.Immersion, Is.GreaterThan(.999f));
        }

        [Test]
        public void CameraTeleportSetsImmersionWithoutScreenPulse()
        {
            var state = new WaterImmersionState();
            state.Tick(-2f, .02f);
            state.Tick(2f, .02f, true);
            Assert.AreEqual(1f, state.Immersion);
            Assert.AreEqual(0f, state.Transition);
        }

        [Test]
        public void UnderwaterFilterRestoresExistingListenerFilter()
        {
            var host = new GameObject("Water listener test");
            try
            {
                host.AddComponent<AudioListener>();
                var filter = host.AddComponent<AudioLowPassFilter>();
                filter.cutoffFrequency = 5000f;
                filter.enabled = false;
                var water = host.AddComponent<WaterListenerFilter>();
                InvokeLifecycle(water, "OnEnable");
                water.SetImmersion(1f);
                Assert.IsTrue(filter.enabled);
                Assert.That(filter.cutoffFrequency, Is.EqualTo(850f).Within(1f));
                InvokeLifecycle(water, "OnDisable");
                Assert.IsFalse(filter.enabled);
                Assert.That(filter.cutoffFrequency, Is.EqualTo(5000f).Within(1f));
                InvokeLifecycle(water, "OnEnable");
                water.SetImmersion(1f);
                water.SetImmersion(0f);
                Assert.IsFalse(filter.enabled);
                Assert.That(filter.cutoffFrequency, Is.EqualTo(5000f).Within(1f));
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void OwnedListenerFilterSurvivesEnableCycleAndIsRemovedWithOwner()
        {
            var host = new GameObject("Owned water listener test");
            try
            {
                host.AddComponent<AudioListener>();
                var water = host.AddComponent<WaterListenerFilter>();
                InvokeLifecycle(water, "OnEnable");
                water.SetImmersion(1f);
                InvokeLifecycle(water, "OnDisable");
                InvokeLifecycle(water, "OnEnable");
                water.SetImmersion(1f);
                Assert.IsTrue(host.GetComponent<AudioLowPassFilter>().enabled);
                InvokeLifecycle(water, "OnDisable");
                InvokeLifecycle(water, "OnDestroy");
                Object.DestroyImmediate(water);
                Assert.IsNull(host.GetComponent<AudioLowPassFilter>());
            }
            finally { Object.DestroyImmediate(host); }
        }

        // Exercise restoration/ownership logic without asking EditMode to dispatch play-only Unity messages.
        static void InvokeLifecycle(WaterListenerFilter target, string method) =>
            typeof(WaterListenerFilter).GetMethod(method,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(target, null);

        sealed class Lake : ISwimmingProvider
        {
            public bool TryGetDepth(Vector3 p, out float depth, out float bodyDepth)
            { depth = 50f - p.y; bodyDepth = 10f; return true; }
        }

        [Test]
        public void SwimmerFloatsAtRaisedLakeAndCanDiveAndSurface()
        {
            var driver = new SurfaceCharacterController(new ConstantGravityProvider(Vector3.down * 9.81f),
                new PlanarGroundingProvider(), 1f, new CharacterPose(new Vector3(0, 49.8f, 0), Vector3.up, Vector3.forward), new Lake());
            for (int i = 0; i < 100; i++) driver.Tick(Vector2.zero, Vector3.forward, 3f, .02f);
            Assert.IsTrue(driver.Swimming);
            Assert.That(driver.Pose.Position.y, Is.EqualTo(49.7f).Within(.02f));
            for (int i = 0; i < 100; i++) driver.Tick(Vector2.up, Vector3.forward, 3f, .02f, false, true);
            Assert.That(driver.Pose.Position.y, Is.LessThan(46f));
            Assert.That(driver.Pose.Position.z, Is.GreaterThan(5f));
            for (int i = 0; i < 150; i++) driver.Tick(Vector2.zero, Vector3.forward, 3f, .02f, true);
            Assert.That(driver.Pose.Position.y, Is.InRange(49.7f, 50.16f));
        }

        [Test]
        public void SwimmerCannotDiveThroughBed()
        {
            var driver = new SurfaceCharacterController(new ConstantGravityProvider(Vector3.down * 9.81f),
                new PlanarGroundingProvider(), 1f, new CharacterPose(new Vector3(0, 49.8f, 0), Vector3.up, Vector3.forward), new Lake());
            for (int i = 0; i < 1000; i++) driver.Tick(Vector2.zero, Vector3.forward, 3f, .02f, false, true);
            Assert.That(driver.Pose.Position.y, Is.EqualTo(41f).Within(.001f));
        }

        [Test]
        public void LowerQualityReducesEachWaterBudget()
        {
            var low = WaterQualityProfile.ForTier("Low");
            var medium = WaterQualityProfile.ForTier("Medium");
            var high = WaterQualityProfile.ForTier("High");
            Assert.AreEqual(0, low.ProbeResolution);
            Assert.Less(low.ReflectionSteps, medium.ReflectionSteps);
            Assert.Less(medium.ReflectionSteps, high.ReflectionSteps);
            Assert.Less(low.ShaftSteps, high.ShaftSteps);
            Assert.Less(low.RippleLimit, high.RippleLimit);
            Assert.Less(medium.ProbeResolution, high.ProbeResolution);
            Assert.Greater(medium.ProbeInterval, high.ProbeInterval);
        }
    }
}
