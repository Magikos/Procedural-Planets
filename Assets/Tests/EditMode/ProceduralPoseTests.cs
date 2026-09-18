using NUnit.Framework;
using System.Linq;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ProceduralPoseTests
    {
        [Test] public void InteractionFacingBlendsThroughReversalAndRelease()
        {
            var root = new GameObject("Interaction facing");
            try
            {
                var spine = new GameObject("Spine").transform; spine.SetParent(root.transform, false);
                var definition = root.AddComponent<ProceduralRigDefinition>(); definition.Spine = new[] { spine };
                var rig = new ProceduralPoseRig(root.transform, definition) { FacingOffsetDegrees = 12 };
                for (int i = 0; i < 60; i++) { rig.RestoreAnimation(); rig.CaptureAnimation(); rig.Tick(Vector3.up, Vector3.forward, null, 0, 0, 0, 1f / 60); }
                var before = spine.localRotation;
                Assert.Greater(Quaternion.Angle(Quaternion.identity, before), 5);
                rig.FacingOffsetDegrees = -12;
                rig.RestoreAnimation(); rig.CaptureAnimation(); rig.Tick(Vector3.up, Vector3.forward, null, 0, 0, 0, 1f / 60);
                Assert.Less(Quaternion.Angle(before, spine.localRotation), 1);
                rig.FacingOffsetDegrees = 0;
                for (int i = 0; i < 60; i++) { rig.RestoreAnimation(); rig.CaptureAnimation(); rig.Tick(Vector3.up, Vector3.forward, null, 0, 0, 0, 1f / 60); }
                Assert.Less(Quaternion.Angle(Quaternion.identity, spine.localRotation), .05f);
                Assert.Throws<System.ArgumentOutOfRangeException>(() => rig.FacingOffsetDegrees = 30);
            }
            finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void TorsoTurnResponseIsBoundedAndBlendsOutForInteractions()
        {
            var frame = new GameObject("Torso turn");
            try
            {
                var spine = new GameObject("Spine").transform; spine.SetParent(frame.transform, false);
                var definition = frame.AddComponent<ProceduralRigDefinition>(); definition.Spine = new[] { spine };
                var rig = new ProceduralPoseRig(frame.transform, definition) { TurnResponseSeconds = .25f, BodyLeanEnabled = false };
                for (int i = 0; i < 60; i++)
                {
                    frame.transform.rotation = Quaternion.Euler(0f, i * 2f, 0f);
                    rig.RestoreAnimation(); rig.CaptureAnimation();
                    rig.Tick(Vector3.up, frame.transform.forward, null, 0f, 0f, 0f, 1f / 60f);
                    Assert.LessOrEqual(Quaternion.Angle(Quaternion.identity, spine.localRotation), definition.SpineLimit + .01f);
                }
                float before = Quaternion.Angle(Quaternion.identity, spine.localRotation);
                Assert.Greater(before, 5f);
                rig.TurnResponseSeconds = 0f;
                rig.RestoreAnimation(); rig.CaptureAnimation();
                rig.Tick(Vector3.up, frame.transform.forward, null, 0f, 0f, 0f, 1f / 60f);
                Assert.That(Quaternion.Angle(Quaternion.identity, spine.localRotation), Is.GreaterThan(before * .5f).And.LessThan(before));
                for (int i = 0; i < 90; i++)
                {
                    rig.RestoreAnimation(); rig.CaptureAnimation();
                    rig.Tick(Vector3.up, frame.transform.forward, null, 0f, 0f, 0f, 1f / 60f);
                }
                Assert.Less(Quaternion.Angle(Quaternion.identity, spine.localRotation), .05f);
            }
            finally { Object.DestroyImmediate(frame); }
        }

        [Test]
        public void DisabledBodyLeanPreservesAuthoredBodyThroughAccelerationAndReversal()
        {
            var frame = new GameObject("authored body");
            try
            {
                var body = new GameObject("body").transform; body.SetParent(frame.transform, false);
                Quaternion authored = Quaternion.Euler(11f, 8f, 5f);
                body.localRotation = authored;
                var definition = frame.AddComponent<ProceduralRigDefinition>(); definition.Body = body;
                var rig = new ProceduralPoseRig(frame.transform, definition) { BodyLeanEnabled = false, SpineEnabled = false };
                for (int i = 0; i < 60; i++)
                {
                    frame.transform.position += new Vector3(i % 2 == 0 ? .1f : -.1f, 0f, .02f);
                    rig.RestoreAnimation(); rig.CaptureAnimation();
                    rig.Tick(Vector3.up, Vector3.forward, null, 0f, 1f, 0f, 1f / 60f);
                    Assert.Less(Quaternion.Angle(authored, body.localRotation), .05f);
                }
            }
            finally { Object.DestroyImmediate(frame); }
        }

        [Test]
        public void DisablingBodyLeanBlendsOutTheDisplayedCorrection()
        {
            var frame = new GameObject("body lean release");
            try
            {
                var body = new GameObject("body").transform; body.SetParent(frame.transform, false);
                var definition = frame.AddComponent<ProceduralRigDefinition>(); definition.Body = body;
                var rig = new ProceduralPoseRig(frame.transform, definition) { SpineEnabled = false };
                for (int i = 0; i < 15; i++)
                {
                    frame.transform.position = Vector3.right * (.005f * i * i);
                    rig.RestoreAnimation(); rig.CaptureAnimation();
                    rig.Tick(Vector3.up, Vector3.forward, null, 0f, 1f, 0f, 1f / 60f);
                }
                float before = Quaternion.Angle(Quaternion.identity, body.localRotation);
                Assert.Greater(before, 1f, "The fixture must contain an outgoing lean.");
                rig.BodyLeanEnabled = false;
                rig.RestoreAnimation(); rig.CaptureAnimation();
                rig.Tick(Vector3.up, Vector3.forward, null, 0f, 1f, 0f, 1f / 60f);
                float first = Quaternion.Angle(Quaternion.identity, body.localRotation);
                Assert.That(first, Is.GreaterThan(before * .5f).And.LessThan(before));
                for (int i = 0; i < 90; i++)
                {
                    rig.RestoreAnimation(); rig.CaptureAnimation();
                    rig.Tick(Vector3.up, Vector3.forward, null, 0f, 0f, 0f, 1f / 60f);
                }
                Assert.Less(Quaternion.Angle(Quaternion.identity, body.localRotation), .05f);
            }
            finally { Object.DestroyImmediate(frame); }
        }

        [Test]
        public void DisabledBodyLeanRetainsExplicitInteractionForwardLean()
        {
            var frame = new GameObject("interaction lean");
            try
            {
                var body = new GameObject("body").transform; body.SetParent(frame.transform, false);
                var definition = frame.AddComponent<ProceduralRigDefinition>();
                definition.Body = body; definition.Spine = new[] { body };
                var rig = new ProceduralPoseRig(frame.transform, definition) { BodyLeanEnabled = false, ForwardLeanDegrees = 12f };
                for (int i = 0; i < 30; i++)
                {
                    rig.RestoreAnimation(); rig.CaptureAnimation();
                    rig.Tick(Vector3.up, Vector3.forward, null, 0f, 0f, 0f, 1f / 60f);
                }
                Assert.AreEqual(12f, Quaternion.Angle(Quaternion.identity, body.localRotation), .05f);
            }
            finally { Object.DestroyImmediate(frame); }
        }

        [Test]
        public void CarcassCoverageRetainsSurfaceAreaAcrossSubmeshesMonotonically()
        {
            var vertices = new Vector3[300];
            var triangles = new[] { new int[150], new int[150] };
            var order = new[] { new float[50], new float[50] };
            double total = 0d;
            for (int t = 0; t < 100; t++)
            {
                float area = 1f + t % 5;
                vertices[t * 3 + 1] = Vector3.right * area * 2f;
                vertices[t * 3 + 2] = Vector3.up;
                for (int j = 0; j < 3; j++) triangles[t / 50][t % 50 * 3 + j] = t * 3 + j;
                order[t / 50][t % 50] = .85f + (99 - t) * .001f;
                total += area;
            }
            typeof(CreatureCorpsePresentation).GetMethod("NormalizeCoverage",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { vertices, triangles, order });
            double previous = -1d;
            for (int step = 0; step <= 20; step++)
            {
                double retained = 0d;
                for (int t = 0; t < 100; t++)
                    if (order[t / 50][t % 50] <= step / 20f) retained += 1 + t % 5;
                Assert.GreaterOrEqual(retained, previous);
                Assert.AreEqual(step / 20d, retained / total, 2.5d / total + .000001d);
                previous = retained;
            }
            Assert.AreEqual(0d, order.SelectMany(values => values).Count(value => value <= 0f));
            Assert.AreEqual(100, order.SelectMany(values => values).Count(value => value <= 1f));
        }

        [Test]
        public void AuthoredCarcassMeshesAreReadableInPlayerBuilds()
        {
            var library = Resources.Load<CreatureLibrary>("Settings/CreatureLibrary");
            foreach (var species in library.Species.Where(s => s?.Visuals != null))
            foreach (var prefab in new[] { species.Visuals.MalePrefab, species.Visuals.FemalePrefab }.Distinct())
            foreach (var skin in prefab.GetComponentsInChildren<SkinnedMeshRenderer>())
                Assert.IsTrue(skin.sharedMesh.isReadable, species.DisplayName + " carcass requires readable source topology.");
        }

        [Test]
        public void ImportedSnakeCallbacksDoNotInvokeVendorComponents()
        {
            var settings = Resources.Load<CreatureLibrary>("Settings/CreatureLibrary").Species.Single(s => s.DisplayName == "Snake");
            using var view = new CreatureAnimationView(null, 1, settings.Visuals.Snapshot(), settings.BodyHeightMeters);
            Assert.IsFalse(view.Root.GetComponentInChildren<Animator>().fireEvents);
            view.Tick(Vector3.zero, Vector3.up, 4f);
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(173f)]
        public void CorpseRibsPointTowardPosedFeetRegardlessOfWorldOrientation(float roll)
        {
            var rotation = Quaternion.Euler(21f, 37f, roll);
            Vector3 forward = rotation * Vector3.forward;
            Vector3 ventral = rotation * Vector3.down;
            var method = typeof(CreatureCorpsePresentation).GetMethod("VentralDirection",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            // Feet can extend along the spine, but ribs must remain perpendicular to it.
            Vector3 actual = (Vector3)method.Invoke(null, new object[] {
                forward, ventral * .4f + forward * .6f, Vector3.down });
            Assert.Greater(Vector3.Dot(actual, ventral), .999f);
            Assert.Less(Mathf.Abs(Vector3.Dot(actual, forward)), .0001f);
        }

        [TestCase("Deer", 1.84f)]
        [TestCase("Wolf", 1f)]
        [TestCase("Rabbit", .45f)]
        [TestCase("Boar", .95f)]
        [TestCase("Fox", .65f)]
        public void PlanetCarcassRetainsSettledPoseOnRotatedGround(string speciesName, float height)
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/Art/Creatures/" + speciesName + "/" + speciesName + "Visuals.asset");
            Vector3 origin = new(100f, 200f, 300f), up = Vector3.right;
            var species = CreatureLibraryDto.Placeholder.At(0) with { Visuals = settings.Snapshot(), BodyHeightMeters = height };
            var corpse = new CreatureCorpse(new EntityId(EntityId.CorpseOwner, 1), 0,
                origin + up * height * .5f, Quaternion.LookRotation(Vector3.forward, up), 0, false);
            using var view = new CreatureCarcassView(null, corpse, species, new CorpsePlane(origin, up));
            for (int i = 0; i < 90 && !view.Settled; i++) view.Sync(1d, CorpseStage.Fresh);
            Assert.That(view.Settled, Is.True, "Carcass support must finish within its bounded settlement budget.");
            var mesh = new Mesh();
            try
            {
                float lowest = float.PositiveInfinity;
                foreach (var renderer in view.Root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    renderer.BakeMesh(mesh, true);
                    foreach (var vertex in mesh.vertices)
                        lowest = Mathf.Min(lowest, Vector3.Dot(renderer.transform.TransformPoint(vertex) - origin, up));
                }
                Assert.AreEqual(.005f, lowest, .002f);
                view.Sync(.5d, CorpseStage.Fresh);
                var skeleton = view.Root.Find("Skeletal scaffold");
                Assert.IsNotNull(skeleton);
                Assert.AreEqual(1, skeleton.GetComponentsInChildren<MeshRenderer>().Length);
                Bounds bounds = skeleton.GetComponent<MeshFilter>().sharedMesh.bounds;
                Assert.Less(bounds.size.magnitude, height * 4f, "Imported culling bounds must not inflate the carcass skeleton.");
                view.Sync(.25d, CorpseStage.Rotting);
                Assert.AreEqual(bounds, skeleton.GetComponent<MeshFilter>().sharedMesh.bounds,
                    "Decay must retain the evaluated death pose instead of rebuilding from an upright rig.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        sealed class CorpsePlane : IGroundingProvider
        {
            readonly Vector3 _origin, _up;
            public CorpsePlane(Vector3 origin, Vector3 up) { _origin = origin; _up = up; }
            public bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult result)
            { result = new GroundResult(position + _up * (offset - Vector3.Dot(position - _origin, _up)), _up); return true; }
        }

        [TestCase("Bear")]
        [TestCase("PolarBear")]
        public void GeneratedDrinkReachesGroundWithSupportedFeet(string species)
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/Art/Creatures/" + species + "/" + species + "Visuals.asset");
            var model = Object.Instantiate(settings.MalePrefab);
            try
            {
                var rig = model.GetComponent<ProceduralRigDefinition>();
                settings.Idle.SampleAnimation(model, 0f);
                Vector3 up = model.transform.up;
                float ground = rig.Feet.Min(f => Vector3.Dot((f.Contact != null ? f.Contact : f.Bones[^1]).position, up));
                Transform mouth = rig.Look[^1].GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.EndsWith("JawEnd_M"))
                    ?? rig.Look[^1].GetComponentsInChildren<Transform>().First(t => t.name.EndsWith("HeadEnd_M"));
                var ankles = rig.Feet.Select(f => f.Bones[^1].position).ToArray();
                for (int sample = 0; sample < 4; sample++)
                {
                    settings.Drink.SampleAnimation(model, sample);
                    float clearance = Vector3.Dot(mouth.position, up) - ground;
                    Assert.That(clearance, Is.InRange(-settings.ModelHeightMeters * .02f, settings.ModelHeightMeters * .05f),
                        "The mouth must reach drinking height without penetrating the ground.");
                    for (int i = 0; i < ankles.Length; i++)
                        Assert.LessOrEqual(Vector3.Distance(ankles[i], rig.Feet[i].Bones[^1].position), settings.ModelHeightMeters * .05f);
                }
            }
            finally { Object.DestroyImmediate(model); }
        }

        [Test]
        public void AuthoredGoatDrinkReachesGroundDuringItsCycleWithoutPenetration()
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/Art/Creatures/Goat/GoatVisuals.asset");
            Assert.AreSame(settings.Eat, settings.Drink, "Keep the reviewed original feeding clip.");
            var model = Object.Instantiate(settings.MalePrefab);
            try
            {
                var rig = model.GetComponent<ProceduralRigDefinition>();
                settings.Idle.SampleAnimation(model, 0f);
                Vector3 up = model.transform.up;
                float ground = rig.Feet.Min(f => Vector3.Dot((f.Contact != null ? f.Contact : f.Bones[^1]).position, up));
                // Goat has Jaw1_M/Jaw2_M, not JawEnd_M. HeadEnd_M is above the skull.
                // Use the named jaw root as the contact proxy; do not substitute a head endpoint.
                Transform mouth = rig.Look[^1].GetComponentsInChildren<Transform>().First(t => t.name == "Jaw1_M");
                float height = settings.ModelHeightMeters, longestContact = 0f, contact = 0f;
                int samples = Mathf.CeilToInt(settings.Drink.length * 60f);
                float dt = settings.Drink.length / samples;
                for (int sample = 0; sample < samples; sample++)
                {
                    settings.Drink.SampleAnimation(model, sample * dt);
                    float clearance = Vector3.Dot(mouth.position, up) - ground;
                    Assert.GreaterOrEqual(clearance, -height * .02f, $"Muzzle penetrates the floor at {sample * dt:F3}s.");
                    contact = clearance <= height * .05f ? contact + dt : 0f;
                    longestContact = Mathf.Max(longestContact, contact);
                    int supported = 0;
                    foreach (var foot in rig.Feet)
                    {
                        float footHeight = Vector3.Dot((foot.Contact != null ? foot.Contact : foot.Bones[^1]).position, up) - ground;
                        Assert.GreaterOrEqual(footHeight, -height * .02f, $"Hoof penetrates the floor at {sample * dt:F3}s.");
                        if (footHeight <= height * .05f) supported++;
                    }
                    Assert.GreaterOrEqual(supported, 2, "The authored feeding stance must retain support.");
                }
                Assert.GreaterOrEqual(longestContact, .1f, "The complete cycle must contain sustained drinking reach, not a single crossing frame.");
            }
            finally { Object.DestroyImmediate(model); }
        }

        [TestCase("Rabbit")]
        [TestCase("Boar")]
        [TestCase("Fox")]
        public void AdditionalQuadrupedsHaveBoundLimbsAndPlayableSurvivalActions(string species)
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/Art/Creatures/" + species + "/" + species + "Visuals.asset");
            Assert.IsNotNull(settings);
            var rig = settings.MalePrefab.GetComponent<ProceduralRigDefinition>();
            Assert.IsNotNull(rig, "Run CreatureQuadrupedRigAuthor.Build before validating presentation assets.");
            Assert.AreEqual(4, rig.Feet.Length);
            Assert.Greater(rig.Spine.Length, 0);
            Assert.Greater(rig.Look.Length, 0);
            Assert.Greater(rig.Chains.Length, 0);
            foreach (var foot in rig.Feet)
            {
                Assert.AreEqual(3, foot.Bones.Length);
                Assert.IsTrue(foot.Bones[1].IsChildOf(foot.Bones[0]));
                Assert.IsTrue(foot.Bones[2].IsChildOf(foot.Bones[1]));
                Assert.IsNotNull(foot.Contact);
                foreach (var curve in new[] { foot.WalkContact, foot.RunContact })
                    for (int sample = 0; sample <= 60; sample++) Assert.That(curve.Evaluate(sample / 60f), Is.InRange(-.001f, 1.001f));
            }
            foreach (var clip in new[] { settings.Eat, settings.Drink, settings.Rest, settings.Sleep, settings.Stalk })
            {
                Assert.IsNotNull(clip);
                Assert.Greater(clip.length, 0f);
                Assert.IsTrue(clip.isLooping);
            }
            using var view = new CreatureAnimationView(null, 1UL, settings.Snapshot(), settings.ModelHeightMeters);
            Assert.IsNotNull(view.Pose);
            view.Root.position = Vector3.up * settings.ModelHeightMeters * .5f;
            var ground = new UnevenGround();
            for (int frame = 0; frame < 120; frame++)
            {
                view.Root.rotation = Quaternion.Euler(0f, frame * .25f, 0f);
                view.Tick(view.Root.forward * settings.WalkMetersPerSecond, Vector3.up, 1f / 60f, ground);
                foreach (Transform bone in view.Root.GetComponentsInChildren<Transform>())
                    Assert.IsTrue(CharacterMath.IsFinite(bone.position));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void FootLiftHandlesMotionChanges(int mode)
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/Art/Creatures/Deer/DeerVisuals.asset");
            using var view = new CreatureAnimationView(null, 1UL, settings.Snapshot(), 1.84f);
            var ground = new UnevenGround();
            var feet = (FootPlacementSolver[])typeof(ProceduralPoseRig).GetField("_feet",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(view.Pose);
            view.Root.position = new Vector3(0f, 0.92f, 0f);
            bool interrupted = false;
            int after = 0;
            float heading = 0f;
            for (int frame = 0; frame < 720 && after < 120; frame++)
            {
                if (!interrupted)
                    foreach (var foot in feet) interrupted |= foot.Stepping;
                float turn = !interrupted ? 25f : mode == 1 ? -25f : 0f;
                heading += turn / 60f;
                view.Root.rotation = Quaternion.Euler(0f, heading, 0f);
                Vector3 velocity = interrupted && mode == 2 ? view.Root.forward * 0.376f : Vector3.zero;
                view.Root.position += velocity / 60f;
                view.Tick(velocity, Vector3.up, 1f / 60f, interrupted && mode == 3 ? null : ground);
                foreach (Transform bone in view.Root.GetComponentsInChildren<Transform>())
                    Assert.IsTrue(CharacterMath.IsFinite(bone.position));
                if (interrupted) after++;
                if (after > 30 && (mode == 2 || mode == 3))
                    foreach (var foot in feet) Assert.IsFalse(foot.Stepping);
                if (after > 0 && mode == 3) Assert.AreEqual(0, view.Pose.PlantedFeet);
            }
            Assert.IsTrue(interrupted, "The test must change motion during an active foot lift.");
            Assert.AreEqual(120, after);
            if (mode == 0) foreach (var foot in feet) Assert.IsFalse(foot.Stepping);
        }

        [TestCase("Deer/DeerVisuals", 1.84f)]
        [TestCase("Wolf/WolfVisuals", 1f)]
        public void SlowWalkingTurnReplantsEveryFoot(string asset, float height)
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/Art/Creatures/" + asset + ".asset");
            using var view = new CreatureAnimationView(null, 1UL, settings.Snapshot(), height);
            var ground = new UnevenGround();
            var feet = (FootPlacementSolver[])typeof(ProceduralPoseRig).GetField("_feet",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(view.Pose);
            var previous = new bool[feet.Length];
            var plants = new int[feet.Length];
            const float speed = 0.376f, turn = 25f * Mathf.Deg2Rad;
            for (int frame = 0; frame < 1200; frame++)
            {
                float angle = frame * turn / 60f;
                float radius = speed / turn;
                Vector3 p = new(radius * (1f - Mathf.Cos(angle)), 0f, radius * Mathf.Sin(angle));
                p.y = height * 0.5f + CreatureAnimationPrototype.GroundHeight(p.x, p.z);
                view.Root.SetPositionAndRotation(p, Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f));
                view.Tick(view.Root.forward * speed, Vector3.up, 1f / 60f, ground);
                for (int i = 0; i < feet.Length; i++)
                {
                    if (feet[i].Planted && !previous[i]) plants[i]++;
                    previous[i] = feet[i].Planted;
                }
            }
            foreach (int count in plants)
                Assert.Greater(count, 4, "Each foot must release and replant during slow walking. The old idle blend planted only once.");
        }

        [TestCase("Deer/DeerVisuals", 1.84f)]
        [TestCase("Wolf/WolfVisuals", 1f)]
        public void StationaryTurnReplantsFeetOneAtATime(string asset, float height)
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/Art/Creatures/" + asset + ".asset");
            using var view = new CreatureAnimationView(null, 1UL, settings.Snapshot(), height);
            var ground = new UnevenGround();
            var feet = (FootPlacementSolver[])typeof(ProceduralPoseRig).GetField("_feet",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(view.Pose);
            view.Root.position = new Vector3(0f, height * 0.5f, 0f);
            int steps = 0;
            var previous = new bool[feet.Length];
            for (int frame = 0; frame < 720; frame++)
            {
                view.Root.rotation = Quaternion.Euler(0f, frame * 25f / 60f, 0f);
                view.Tick(Vector3.zero, Vector3.up, 1f / 60f, ground);
                int swinging = 0;
                for (int i = 0; i < feet.Length; i++)
                {
                    if (feet[i].Stepping) swinging++;
                    if (feet[i].Stepping && !previous[i]) steps++;
                    previous[i] = feet[i].Stepping;
                }
                Assert.LessOrEqual(swinging, 1);
            }
            Assert.Greater(steps, 8, "A rotating idle pose must lift and replant its feet repeatedly.");
            Assert.GreaterOrEqual(view.Pose.PlantedFeet, 2);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeerWalkingPreservesAuthoredHorizontalHoofTravel(bool stopAfterWalking)
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/Art/Creatures/Deer/DeerVisuals.asset");
            using var view = new CreatureAnimationView(null, 0UL, settings.Snapshot(), 1.84f);
            view.Pose.SpineEnabled = view.Pose.LookEnabled = view.Pose.ChainsEnabled = false;
            var feet = view.Root.GetComponentInChildren<ProceduralRigDefinition>().Feet;
            var displayed = new Vector3[feet.Length];
            var ground = new PlaneGround();
            view.Root.position = new Vector3(0f, .92f - 1.4f, 0f);
            float maximum = 0f;
            int walkingFrames = 90 + Mathf.CeilToInt(settings.Walk.length * 2f * 60f);
            int frames = walkingFrames + (stopAfterWalking ? 60 : 0);
            for (int frame = 0; frame < frames; frame++)
            {
                Vector3 velocity = frame < walkingFrames ? Vector3.forward * settings.WalkMetersPerSecond : Vector3.zero;
                view.Root.position += velocity / 60f;
                view.Tick(velocity, Vector3.up, 1f / 60f, ground);
                for (int i = 0; i < feet.Length; i++)
                    displayed[i] = (feet[i].Contact != null ? feet[i].Contact : feet[i].Bones[^1]).position;
                // Restore the exact evaluated production pose, retaining the same graph clocks and blend weights.
                view.Pose.RestoreAnimation();
                if (frame < 90) continue;
                for (int i = 0; i < feet.Length; i++)
                {
                    Vector3 authored = (feet[i].Contact != null ? feet[i].Contact : feet[i].Bones[^1]).position;
                    maximum = Mathf.Max(maximum, Vector3.ProjectOnPlane(displayed[i] - authored, Vector3.up).magnitude);
                }
            }
            Assert.Less(maximum, .02f, "Flat-ground correction must preserve hoof travel; the reviewed world anchor pulled it back 0.234 m.");
        }

        [Test]
        public void DeerFeetDoNotFlipDuringCurvedWalking()
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/Art/Creatures/Deer/DeerVisuals.asset");
            Assert.IsNotNull(settings);
            using var view = new CreatureAnimationView(null, 1UL, settings.Snapshot(), 1.84f);
            var definition = view.Root.GetComponentInChildren<ProceduralRigDefinition>();
            var bones = new System.Collections.Generic.List<Transform>();
            foreach (FootDefinition foot in definition.Feet)
            {
                Transform bone = foot.Contact;
                while (bone != null)
                {
                    bones.Add(bone);
                    if (bone == foot.Bones[0]) break;
                    bone = bone.parent;
                }
            }
            var previous = new Quaternion[bones.Count];
            var ground = new UnevenGround();
            float maximum = 0f;
            for (int frame = 0; frame < 900; frame++)
            {
                float angle = frame / 120f;
                Vector3 p = new(2f * (1f - Mathf.Cos(angle)), 0f, 2f * Mathf.Sin(angle));
                p.y = 0.92f + CreatureAnimationPrototype.GroundHeight(p.x, p.z);
                view.Root.SetPositionAndRotation(p, Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f));
                view.Tick(view.Root.forward, Vector3.up, 1f / 60f, ground);
                for (int i = 0; i < bones.Count; i++)
                {
                    if (frame > 60) maximum = Mathf.Max(maximum, Quaternion.Angle(previous[i], bones[i].localRotation));
                    previous[i] = bones[i].localRotation;
                }
            }
            Assert.Less(maximum, 15f, "Base animation peaks at 11.45 degrees/frame. The original IK reached 142 degrees/frame.");
        }

        static Transform[] Chain(GameObject root)
        {
            var middle = new GameObject("middle").transform;
            middle.SetParent(root.transform, false); middle.localPosition = new Vector3(0.3f, -0.7f, 0f);
            var tip = new GameObject("tip").transform;
            tip.SetParent(middle, false); tip.localPosition = new Vector3(-0.3f, -0.7f, 0f);
            return new[] { root.transform, middle, tip };
        }

        [Test]
        public void LimbReachesTargetWithoutStretchingAndWorksUnderRotatedGravity()
        {
            foreach (Quaternion rotation in new[] { Quaternion.identity, Quaternion.Euler(90f, 15f, 40f) })
            {
                var root = new GameObject("limb");
                try
                {
                    Transform[] bones = Chain(root);
                    root.transform.rotation = rotation;
                    float first = Vector3.Distance(bones[0].position, bones[1].position);
                    float second = Vector3.Distance(bones[1].position, bones[2].position);
                    var solver = new LimbPoseSolver(bones);
                    float error = solver.Solve(rotation * new Vector3(0.4f, -1.1f, 0f), 1f, 90f);
                    Assert.Less(error, 0.015f);
                    Assert.That(Vector3.Distance(bones[0].position, bones[1].position), Is.EqualTo(first).Within(0.00001f));
                    Assert.That(Vector3.Distance(bones[1].position, bones[2].position), Is.EqualTo(second).Within(0.00001f));
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void SpringRemainsFinitePreservesLengthsAndResetsAfterTeleport()
        {
            var root = new GameObject("chain");
            try
            {
                Transform[] bones = Chain(root);
                var definition = root.AddComponent<ProceduralRigDefinition>();
                definition.Chains = new[] { new SpringChainDefinition { Bones = bones } };
                var rig = new ProceduralPoseRig(root.transform, definition);
                Vector3 a = bones[1].localPosition, b = bones[2].localPosition;
                for (int i = 0; i < 500; i++)
                {
                    rig.RestoreAnimation(); rig.CaptureAnimation();
                    root.transform.position = new Vector3(Mathf.Sin(i * 0.05f), 0f, i > 250 ? 100f : 0f);
                    rig.Tick(Vector3.right, Vector3.forward, null, 0f, 0f, 0f, i == 260 ? 0.5f : 1f / 60f);
                    Assert.IsTrue(CharacterMath.IsFinite(bones[2].position));
                    Assert.That(bones[1].localPosition, Is.EqualTo(a));
                    Assert.That(bones[2].localPosition, Is.EqualTo(b));
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void LookDoesNotAccumulateWhenClipsDoNotAnimateTheBone()
        {
            var frame = new GameObject("frame");
            try
            {
                Transform head = new GameObject("head").transform; head.SetParent(frame.transform, false);
                var definition = frame.AddComponent<ProceduralRigDefinition>(); definition.Look = new[] { head };
                var rig = new ProceduralPoseRig(frame.transform, definition);
                for (int i = 0; i < 600; i++)
                {
                    rig.RestoreAnimation(); rig.CaptureAnimation();
                    rig.Tick(Vector3.up, Vector3.right, null, 0f, 0f, 0f, 1f / 60f);
                }
                Assert.That(Quaternion.Angle(Quaternion.identity, head.localRotation), Is.EqualTo(50f).Within(0.05f));
                rig.RestoreAnimation();
                Assert.That(Quaternion.Angle(Quaternion.identity, head.localRotation), Is.LessThan(0.001f));
            }
            finally { Object.DestroyImmediate(frame); }
        }

        [Test]
        public void MissingGroundReleasesContacts()
        {
            var root = new GameObject("foot");
            try
            {
                var solver = new FootPlacementSolver(new FootDefinition { Bones = Chain(root), MaxCorrection = 2f }, 1f);
                solver.Tick(new PlaneGround(), Vector3.up, 0f, 0f, 0f, 0.1f);
                Assert.IsTrue(solver.Planted);
                solver.Tick(null, Vector3.up, 0f, 0f, 0f, 0.1f);
                Assert.IsFalse(solver.Planted);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void SwingClearanceIsNotReportedAsAnIkFailure()
        {
            var root = new GameObject("swing foot");
            try
            {
                Transform[] bones = Chain(root);
                root.transform.position = Vector3.up;
                Vector3 tip = bones[^1].position;
                var zero = AnimationCurve.Constant(0f, 1f, 0f);
                var solver = new FootPlacementSolver(new FootDefinition
                    { Bones = bones, WalkContact = zero, RunContact = zero }, 1f);
                solver.Tick(new PlaneGround(), Vector3.up, 0.5f, 1f, 0f, 0.1f);
                Assert.IsFalse(solver.Planted);
                Assert.AreEqual(0f, solver.Error);
                Assert.AreEqual(tip, bones[^1].position);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void RestingWanderNeverRequestsBodyRotation()
        {
            int rests = 0, turns = 0;
            for (int seed = 0; seed < 100; seed++)
            {
                var brain = new CreatureBrain(seed, null, CreatureBehaviour.Wander);
                brain.Observe(new CreatureSenses { Up = Vector3.up, Forward = Vector3.forward, DeltaTime = 0.02f });
                ActorIntent intent = brain.Sample(0);
                if (intent.Move.sqrMagnitude == 0f) { rests++; Assert.AreEqual(0f, intent.Look.x); }
                else if (intent.Look.x != 0f) turns++;
            }
            Assert.Greater(rests, 0); Assert.Greater(turns, 0);
        }

        sealed class PlaneGround : IGroundingProvider
        {
            public bool TryGround(Vector3 p, Vector3 down, float offset, out GroundResult result)
            {
                result = new GroundResult(new Vector3(p.x, -1.4f + offset, p.z), Vector3.up); return true;
            }
        }

        sealed class UnevenGround : IGroundingProvider
        {
            public bool TryGround(Vector3 p, Vector3 down, float offset, out GroundResult result)
            {
                float dx = 0.198f * Mathf.Cos(p.x * 1.1f) * Mathf.Cos(p.z * 0.8f);
                float dz = -0.144f * Mathf.Sin(p.x * 1.1f) * Mathf.Sin(p.z * 0.8f);
                result = new GroundResult(new Vector3(p.x, CreatureAnimationPrototype.GroundHeight(p.x, p.z) + offset, p.z),
                    new Vector3(-dx, 1f, -dz).normalized);
                return true;
            }
        }
    }
}
