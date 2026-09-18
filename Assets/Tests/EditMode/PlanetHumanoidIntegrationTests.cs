using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class PlanetHumanoidIntegrationTests
    {
        PlanetHumanoidActor _actor;
        readonly Vector3 _center = new(7000f, 8000f, 9000f);

        PlanetHumanoidActor Create(Vector3 up, ISwimmingProvider water = null)
        {
            var prefab = Resources.Load<PlanetHumanoidActor>("Characters/PlanetHumanoid");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.gameObject.activeSelf, Is.False, "Providers must bind before OnEnable.");
            _actor = Object.Instantiate(prefab);
            var seed = new CharacterPose(_center + up * 100f, up, CharacterMath.ArbitraryTangent(up));
            _actor.Configure(seed, new RadialGravityProvider(_center),
                new PlanetSurfaceGrounding(new FixedRadiusSampler(100f), _center), water);
            _actor.gameObject.SetActive(true);
            Enable(_actor);
            Assert.That(_actor.Motor, Is.Not.Null);
            Assert.That(_actor.View, Is.Not.Null);
            return _actor;
        }

        // EditMode does not dispatch normal MonoBehaviour lifecycle callbacks.
        static void Enable(PlanetHumanoidActor actor) => typeof(PlanetHumanoidActor)
            .GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(actor, null);
        static void Disable(PlanetHumanoidActor actor) => typeof(HumanoidActorController)
            .GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(actor, null);

        [TearDown]
        public void TearDown()
        {
            if (_actor != null)
            {
                Disable(_actor);
                Object.DestroyImmediate(_actor.gameObject);
            }
        }

        [TestCase(0f, 1f, 0f)]
        [TestCase(1f, 0f, 0f)]
        [TestCase(0f, -1f, 0f)]
        public void AnimatedWalkingFollowsPlanetGravity(float x, float y, float z)
        {
            var actor = Create(new Vector3(x, y, z));
            Vector3 start = actor.Motor.Pose.Position;
            for (int i = 0; i < 90; i++) actor.Step(1f / 60f, new ActorIntent(Vector2.up, Vector2.zero, ActorButtons.None, (uint)i));
            var pose = actor.Motor.Pose;
            Assert.That(Vector3.Distance(start, pose.Position), Is.GreaterThan(1f));
            Assert.That((pose.Position - _center).magnitude, Is.EqualTo(100f).Within(.02f));
            Assert.That(Vector3.Dot(actor.Actor.up, (pose.Position - _center).normalized), Is.GreaterThan(.999f));
            Assert.That(actor.Motor.Grounded, Is.True);
            Assert.That(actor.View.Speed, Is.GreaterThan(.1f));
        }

        [Test]
        public void DisableReleasesPresentationAndAllowsAnotherSpawn()
        {
            var actor = Create(Vector3.right);
            actor.Step(.02f, new ActorIntent(Vector2.up, Vector2.zero, ActorButtons.ToggleCrawl, 0));
            actor.gameObject.SetActive(false);
            Disable(actor);
            Assert.That(actor.View, Is.Null);
            Assert.That(actor.Motor, Is.Null);
            Assert.That(actor.Actor, Is.Null);
            var seed = new CharacterPose(_center - Vector3.up * 100f, Vector3.down, Vector3.forward);
            actor.Configure(seed, new RadialGravityProvider(_center),
                new PlanetSurfaceGrounding(new FixedRadiusSampler(100f), _center), null);
            actor.gameObject.SetActive(true);
            Enable(actor);
            actor.Step(.02f);
            Assert.That(actor.Actor, Is.Not.Null);
            Assert.That(actor.transform.childCount, Is.EqualTo(1));
            Assert.That(actor.Motor.Pose.Up, Is.EqualTo(Vector3.down));
            Assert.That(actor.Crawl, Is.False);
        }

        [Test]
        public void PlanetWaterProviderDrivesSwimming()
        {
            var water = new CharacterSwimmingWater(new FixedBodyWaterQuery(_center, Vector3.right, 103f));
            var actor = Create(Vector3.right, water);
            for (int i = 0; i < 60; i++) actor.Step(.02f);
            Assert.That(actor.Motor.Swimming, Is.True);
            Assert.That(actor.Motor.WaterDepth, Is.GreaterThan(0f));
        }

        [Test]
        public void SpawnDuringGenerationRejectsBeforeCreatingAnActor()
        {
            var go = new GameObject("Generation rejection test");
            try
            {
                var host = go.AddComponent<PlanetCharacterController>();
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(PlanetCharacterController).GetField("_planet", flags).SetValue(host, new GeneratingPlanet(go.transform));
                typeof(PlanetCharacterController).GetField("_sampler", flags).SetValue(host, new FixedRadiusSampler(100f));
                ConsoleRegistry.Scan();
                var result = CommandExecutor.ExecuteImmediate("character.spawn");
                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("planet is generating"));
                Assert.That(host.Actor, Is.Null);
            }
            finally { Object.DestroyImmediate(go); }
        }

        sealed class GeneratingPlanet : IPlanet
        {
            public GeneratingPlanet(Transform transform) => Transform = transform;
            public Transform Transform { get; }
            public int Seed => 1;
            public float LastGeneratedRadius => 100f;
            public float LastSeaLevelRadius => 90f;
            public bool IsGenerating => true;
        }

        [Test]
        public void SpawnRejectionReachesConsoleExecutor()
        {
            IPlanet planet;
            if (ServiceLocator.TryGet(out planet)) Assert.Ignore("Requires an editor without active Planet services.");
            ConsoleRegistry.Scan();
            try
            {
                var result = CommandExecutor.ExecuteImmediate("character.spawn");
                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("generate a planet first"));
            }
            finally
            {
                var host = Object.FindAnyObjectByType<PlanetCharacterController>();
                if (host != null) Object.DestroyImmediate(host.gameObject);
            }
        }
    }
}
