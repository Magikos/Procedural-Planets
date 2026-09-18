using NUnit.Framework;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ProceduralPlanets.Tests
{
    public sealed class CreatureSwimmingTests
    {
        [Test]
        public void ConfiguredAnimalGraphsKeepNormalizedStatesAndAuthorityOwnedRoots()
        {
            var library = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureLibrary>(
                "Assets/Resources/Settings/CreatureLibrary.asset");
            Assert.IsNotNull(library);
            int tested = 0;
            foreach (var authoring in library.Species)
            {
                if (authoring == null || authoring.Visuals == null) continue;
                var species = CreatureSpeciesDto.From(authoring);
                using var view = new CreatureAnimationView(null, 1UL, species.Visuals, species.BodyHeightMeters);
                Vector3 origin = Vector3.up * species.BodyHeightMeters;
                view.Root.position = origin;
                var bones = view.Root.GetComponentsInChildren<Transform>();
                for (int state = 0; state < 9; state++)
                {
                    view.Eating = state == 1; view.Resting = state == 2; view.Sleeping = state == 3;
                    view.Drinking = state == 4; view.Stalking = state == 5;
                    view.AttackTime = state == 6 ? .1f : null;
                    view.Swimming = state == 7; view.Dead = state == 8;
                    for (int frame = 0; frame < 40; frame++)
                    {
                        view.Tick(state == 0 ? Vector3.forward : Vector3.zero, Vector3.up, .02f,
                            evaluatePose: frame % 3 == 0 || frame == 39);
                        float weight = 0f;
                        for (int input = 0; input < view.Graph.BaseMixer.GetInputCount(); input++)
                        {
                            float value = view.Graph.BaseMixer.GetInputWeight(input);
                            Assert.That(value, Is.InRange(0f, 1f), authoring.DisplayName);
                            weight += value;
                        }
                        Assert.AreEqual(1f, weight, .0001f, authoring.DisplayName);
                        Assert.AreEqual(origin, view.Root.position, "Animation must not move the simulation root.");
                    }
                    foreach (var bone in bones)
                        Assert.IsTrue(CharacterMath.IsFinite(bone.position), authoring.DisplayName + ": " + bone.name);
                }
                tested++;
            }
            Assert.Greater(tested, 0);
        }

        sealed class GroundSpy : IGroundingProvider
        {
            public int Calls;
            public bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult result)
            {
                Calls++;
                result = new GroundResult(new Vector3(position.x, offset, position.z), Vector3.up);
                return true;
            }
        }

        [Test]
        public void EveryConfiguredGroundVisualSuppressesLandGraphAndTerrainIKWhileSwimming()
        {
            var library = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureLibrary>(
                "Assets/Resources/Settings/CreatureLibrary.asset");
            Assert.IsNotNull(library);
            int tested = 0;
            foreach (var authoring in library.Species)
            {
                if (authoring == null || authoring.Visuals == null || authoring.CruiseAltitudeMeters > 0f) continue;
                var species = CreatureSpeciesDto.From(authoring);
                using var view = new CreatureAnimationView(null, 1UL, species.Visuals, species.BodyHeightMeters);
                var ground = new GroundSpy();
                view.Tick(Vector3.forward * species.Visuals.RunMetersPerSecond * 2f, Vector3.up, .1f, ground);
                view.Swimming = true;
                for (int i = 0; i < 20; i++) view.Tick(Vector3.forward * species.Visuals.RunMetersPerSecond * 2f, Vector3.up, .02f, ground);
                Assert.AreEqual(1f, view.SwimWeight, authoring.DisplayName);
                Assert.IsNotNull(view.Pose, authoring.DisplayName);
                Assert.AreEqual(0, view.Pose.PlantedFeet, authoring.DisplayName);
                ground.Calls = 0;
                view.Tick(Vector3.forward * species.Visuals.RunMetersPerSecond * 2f, Vector3.up, .02f, ground);
                Assert.AreEqual(0, ground.Calls, authoring.DisplayName);
                var mixer = (AnimationMixerPlayable)typeof(CreatureAnimationView).GetField("_mixer",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(view);
                for (int input = 0; input < 10; input++)
                    Assert.AreEqual(0f, mixer.GetInputWeight(input), authoring.DisplayName + " land input " + input);
                Assert.AreEqual(1f, mixer.GetInputWeight(10), authoring.DisplayName);
                view.Swimming = false;
                for (int i = 0; i < 20; i++) view.Tick(Vector3.zero, Vector3.up, .02f, ground);
                Assert.AreEqual(0f, view.SwimWeight, authoring.DisplayName);
                Assert.AreEqual(0f, mixer.GetInputWeight(10), authoring.DisplayName);
                Assert.Greater(ground.Calls, 0, authoring.DisplayName + " must resume terrain support");
                view.Swimming = true;
                view.Tick(Vector3.zero, Vector3.up, .3f, ground);
                Assert.AreEqual(1f, view.SwimWeight, authoring.DisplayName);
                view.Dead = true;
                view.Tick(Vector3.zero, Vector3.up, .02f, ground);
                Assert.AreEqual(0f, view.SwimWeight, authoring.DisplayName);
                Assert.That(mixer.GetInputWeight(10), Is.InRange(.01f, .99f), authoring.DisplayName + " must crossfade out of swimming on death");
                for (int i = 0; i < 10; i++) view.Tick(Vector3.zero, Vector3.up, .02f, ground);
                Assert.AreEqual(0f, mixer.GetInputWeight(10), authoring.DisplayName);
                tested++;
            }
            Assert.Greater(tested, 0);
        }

        [TestCase(.1f, 0f, .1f)]
        [TestCase(.5f, .5f, .1f)]
        [TestCase(.6f, .5f, .1f)]
        [TestCase(.1f, .5f, -.1f)]
        [TestCase(float.NaN, .5f, .1f)]
        [TestCase(.1f, float.PositiveInfinity, .1f)]
        [TestCase(.1f, .5f, float.NaN)]
        public void SwimmingProfileRejectsInvalidDimensions(float rootDepth, float bodyDepth, float clearance)
            => Assert.Throws<System.ArgumentOutOfRangeException>(() => new SurfaceSwimProfile(rootDepth, bodyDepth, clearance));

        static CreatureSpeciesDto Rabbit => CreatureLibraryDto.Placeholder.At(0) with
            { BodyHeightMeters = .45f, SwimWaterline = .7f };

        sealed class WaterPlane : ISwimmingProvider, IGroundingProvider
        {
            public Vector3 Up = Vector3.up;
            public float Level = 50f, Depth = .4f;
            public bool HasWater = true;
            public bool TryGetDepth(Vector3 position, out float depth, out float bodyDepth)
            { depth = Level - Vector3.Dot(position, Up); bodyDepth = Depth; return HasWater; }
            public bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult result)
            {
                result = new GroundResult(position + Up * (Level - Depth + offset - Vector3.Dot(position, Up)), Up);
                return true;
            }
        }

        static SurfaceCharacterController Driver(WaterPlane water, float height, SurfaceSwimProfile? profile)
            => new(new ConstantGravityProvider(-water.Up * 9.81f), water, height * .5f,
                new CharacterPose(water.Up * (water.Level - water.Depth + height * .5f), water.Up, Vector3.forward),
                water, profile);

        [Test]
        public void RabbitSwimsInWaterTooShallowForThePlayerDefault()
        {
            var water = new WaterPlane();
            var rabbit = Driver(water, .45f, Rabbit.Swimming);
            var player = Driver(water, 2f, null);
            rabbit.Tick(Vector2.zero, Vector3.forward, 1f, .02f);
            player.Tick(Vector2.zero, Vector3.forward, 1f, .02f);
            Assert.IsTrue(rabbit.Swimming);
            Assert.IsFalse(player.Swimming);
            water.Depth = .3f;
            rabbit.Tick(Vector2.zero, Vector3.forward, 1f, .02f);
            Assert.IsFalse(rabbit.Swimming);
        }

        [TestCase(false, .45f, .7f)]
        [TestCase(true, .45f, .7f)]
        [TestCase(false, .18f, .15f)]
        [TestCase(true, .18f, .15f)]
        public void AnimalFloatsAtRaisedLakeWithBodySubmergedInItsGravityFrame(bool sideways, float height, float waterline)
        {
            var water = new WaterPlane { Up = sideways ? Vector3.right : Vector3.up, Depth = 3f };
            var species = Rabbit with { BodyHeightMeters = height, SwimWaterline = waterline };
            var rabbit = Driver(water, height, species.Swimming);
            for (int i = 0; i < 300; i++) rabbit.Tick(Vector2.zero, Vector3.forward, 1f, .02f);
            Assert.IsTrue(rabbit.Swimming);
            Assert.IsFalse(rabbit.Grounded);
            float root = Vector3.Dot(rabbit.Pose.Position, water.Up);
            Assert.AreEqual(water.Level - species.Swimming.RootDepth, root, .001f);
            Assert.AreEqual(height * waterline, water.Level - (root - height * .5f), .001f);
            Assert.Greater(Vector3.Dot(rabbit.Pose.Up, water.Up), .999f);
        }

        [Test]
        public void AnimalReturnsToGroundAfterLeavingWater()
        {
            var water = new WaterPlane { Depth = .5f };
            var rabbit = Driver(water, .45f, Rabbit.Swimming);
            for (int i = 0; i < 100; i++) rabbit.Tick(Vector2.zero, Vector3.forward, 1f, .02f);
            Assert.IsTrue(rabbit.Swimming);
            water.HasWater = false;
            water.Depth = .25f;
            for (int i = 0; i < 100; i++) rabbit.Tick(Vector2.up, Vector3.forward, 1f, .02f);
            Assert.IsFalse(rabbit.Swimming);
            Assert.IsTrue(rabbit.Grounded);
            Assert.AreEqual(water.Level - water.Depth + .225f, rabbit.Pose.Position.y, .001f);
            Assert.Greater(rabbit.Pose.Position.z, 1f);
        }

        [TestCase("Rabbit")]
        [TestCase("Wolf")]
        public void PaddlingPreservesLimbLengthsAndDoesNotAccumulateAfterAnimationEvaluation(string species)
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/Art/Creatures/" + species + "/" + species + "Visuals.asset");
            Assert.IsNotNull(settings);
            var model = Object.Instantiate(settings.MalePrefab);
            try
            {
                var rig = model.GetComponentInChildren<ProceduralRigDefinition>();
                settings.Idle.SampleAnimation(model, .2f);
                var bones = model.GetComponentsInChildren<Transform>();
                var positions = bones.Select(b => b.localPosition).ToArray();
                var rotations = bones.Select(b => b.localRotation).ToArray();
                var lengths = rig.Feet.Select(f => f.Bones.Skip(1).Select((b, i) =>
                    Vector3.Distance(f.Bones[i].position, b.position)).ToArray()).ToArray();
                var pose = new SwimmingPose(model.transform, rig, settings.ModelHeightMeters);
                Vector3[] expected = null;
                for (int frame = 0; frame < 80; frame++)
                {
                    // Animation evaluation restores local transforms before each procedural pass.
                    for (int i = 0; i < bones.Length; i++)
                    { bones[i].localPosition = positions[i]; bones[i].localRotation = rotations[i]; }
                    pose.Apply(.35f, 1f);
                    if (expected == null) expected = bones.Select(b => b.position).ToArray();
                    for (int i = 0; i < bones.Length; i++)
                        Assert.Less(Vector3.Distance(expected[i], bones[i].position), .00001f);
                    for (int f = 0; f < rig.Feet.Length; f++)
                        for (int b = 1; b < rig.Feet[f].Bones.Length; b++)
                            Assert.AreEqual(lengths[f][b - 1], Vector3.Distance(rig.Feet[f].Bones[b - 1].position,
                                rig.Feet[f].Bones[b].position), .00001f);
                }
                Assert.IsTrue(bones.Where((b, i) => Quaternion.Angle(b.localRotation, rotations[i]) > 1f).Any());
            }
            finally { Object.DestroyImmediate(model); }
        }
    }
}

