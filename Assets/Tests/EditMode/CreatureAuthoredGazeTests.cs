using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class CreatureAuthoredGazeTests
    {
        [Test]
        public void ConfiguredCreaturesKeepAuthoredIdleGazeWithoutWorldTarget()
        {
            var library = Resources.Load<CreatureLibrary>("Settings/CreatureLibrary");
            int tested = 0;
            foreach (var authoring in library.Species)
            {
                if (authoring?.Visuals == null) continue;
                var species = CreatureSpeciesDto.From(authoring);
                using var view = new CreatureAnimationView(null, 1UL, species.Visuals, species.BodyHeightMeters);
                if (view.Pose == null) continue;
                IsolateGaze(view);
                var rig = view.Root.GetComponentInChildren<ProceduralRigDefinition>();
                for (int frame = 0; frame < 90; frame++)
                {
                    view.Tick(Vector3.zero, Vector3.up, 1f / 60f);
                    Assert.That(GazeCorrection(view, rig), Is.LessThan(.1f), authoring.DisplayName);
                }
                tested++;
            }
            Assert.That(tested, Is.GreaterThan(0));
        }

        [Test]
        public void ExplicitGazeBlendsInAndReturnsToAuthoredPoseOnTargetLoss()
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/Art/Creatures/Wolf/WolfVisuals.asset");
            using var view = new CreatureAnimationView(null, 1UL, settings.Snapshot(), settings.ModelHeightMeters);
            IsolateGaze(view);
            var rig = view.Root.GetComponentInChildren<ProceduralRigDefinition>();
            view.LookTarget = view.Pose.LookOrigin + Vector3.right * 5f + Vector3.forward * 5f;
            view.Tick(Vector3.zero, Vector3.up, 1f / 60f);
            float entry = GazeCorrection(view, rig);
            for (int i = 0; i < 90; i++) view.Tick(Vector3.zero, Vector3.up, 1f / 60f);
            float tracking = GazeCorrection(view, rig);
            Assert.That(tracking, Is.GreaterThan(1f));
            Assert.That(entry, Is.LessThan(tracking));
            view.LookTarget = null;
            view.Tick(Vector3.zero, Vector3.up, 1f / 60f);
            float release = GazeCorrection(view, rig);
            Assert.That(release, Is.GreaterThan(.1f), "Target loss must retain a fading correction.");
            Assert.That(release, Is.LessThan(tracking));
            for (int i = 0; i < 120; i++) view.Tick(Vector3.zero, Vector3.up, 1f / 60f);
            Assert.That(GazeCorrection(view, rig), Is.LessThan(.1f));
        }

        [Test]
        public void AuthoredEatingGazeOverridesWorldTargetAfterBlend()
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/Art/Creatures/Wolf/WolfVisuals.asset");
            Assert.That(settings.Eat, Is.Not.Null);
            using var view = new CreatureAnimationView(null, 1UL, settings.Snapshot(), settings.ModelHeightMeters);
            IsolateGaze(view);
            var rig = view.Root.GetComponentInChildren<ProceduralRigDefinition>();
            view.LookTarget = view.Pose.LookOrigin + Vector3.right * 5f;
            for (int i = 0; i < 90; i++) view.Tick(Vector3.zero, Vector3.up, 1f / 60f);
            Assert.That(GazeCorrection(view, rig), Is.GreaterThan(1f));
            view.Eating = true;
            for (int i = 0; i < 180; i++) view.Tick(Vector3.zero, Vector3.up, 1f / 60f);
            Assert.That(GazeCorrection(view, rig), Is.LessThan(.1f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RestAndDeathReleaseDisplayedGazeInsteadOfResettingIt(bool dead)
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/Art/Creatures/Wolf/WolfVisuals.asset");
            using var view = new CreatureAnimationView(null, 1UL, settings.Snapshot(), settings.ModelHeightMeters);
            IsolateGaze(view);
            var rig = view.Root.GetComponentInChildren<ProceduralRigDefinition>();
            view.LookTarget = view.Pose.LookOrigin + Vector3.right * 5f + Vector3.forward * 5f;
            for (int i = 0; i < 90; i++) view.Tick(Vector3.zero, Vector3.up, 1f / 60f);
            float tracking = GazeCorrection(view, rig);
            Assert.That(tracking, Is.GreaterThan(1f));
            view.Dead = dead;
            view.Resting = !dead;
            view.Tick(Vector3.zero, Vector3.up, 1f / 60f);
            float release = GazeCorrection(view, rig);
            Assert.That(release, Is.GreaterThan(.1f), "The first transition frame must retain outgoing gaze.");
            Assert.That(release, Is.LessThan(tracking));
            for (int i = 0; i < 180; i++) view.Tick(Vector3.zero, Vector3.up, 1f / 60f);
            Assert.That(GazeCorrection(view, rig), Is.LessThan(.1f));
        }

        static void IsolateGaze(CreatureAnimationView view)
        {
            view.Pose.SpineEnabled = false;
            view.Pose.ChainsEnabled = false;
            view.Pose.FeetEnabled = false;
            view.Pose.SurfaceEnabled = false;
        }

        static float GazeCorrection(CreatureAnimationView view, ProceduralRigDefinition rig)
        {
            var displayed = new Quaternion[rig.Look.Length];
            for (int i = 0; i < displayed.Length; i++) displayed[i] = rig.Look[i].localRotation;
            view.Pose.RestoreAnimation();
            float correction = 0f;
            for (int i = 0; i < displayed.Length; i++)
                correction = Mathf.Max(correction, Quaternion.Angle(displayed[i], rig.Look[i].localRotation));
            return correction;
        }
    }
}
