using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ProceduralPoseTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void FootLiftHandlesMotionChanges(int mode)
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/AssetPacks/PolyperfectAnimals/Deer/DeerVisuals.asset");
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
                "Assets/AssetPacks/PolyperfectAnimals/" + asset + ".asset");
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
                "Assets/AssetPacks/PolyperfectAnimals/" + asset + ".asset");
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

        [Test]
        public void DeerFeetDoNotFlipDuringCurvedWalking()
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/AssetPacks/PolyperfectAnimals/Deer/DeerVisuals.asset");
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
