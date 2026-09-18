using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class CreaturePresentationContinuityTests
    {
        sealed class RaisedGround : IGroundingProvider
        {
            readonly float _height;
            public RaisedGround(float height = .12f) => _height = height;
            public bool TryGround(Vector3 p, Vector3 down, float offset, out GroundResult result)
            {
                result = new GroundResult(new Vector3(p.x, _height + offset, p.z), Vector3.up);
                return true;
            }
        }

        [TestCase("swim")]
        [TestCase("drink")]
        [TestCase("eat")]
        public void EnteringAuthoredActionReleasesOutgoingFootCorrections(string action)
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/Art/Creatures/Deer/DeerVisuals.asset");
            // The identical source clip isolates correction continuity from a change of authored pose.
            var visual = settings.Snapshot() with { Swim = settings.Idle, Drink = settings.Idle, Eat = settings.Idle };
            using var view = new CreatureAnimationView(null, 0UL, visual, 1.84f);
            view.Root.position = Vector3.up * .92f;
            view.Pose.SpineEnabled = view.Pose.LookEnabled = view.Pose.ChainsEnabled = false;
            var feet = view.Root.GetComponentInChildren<ProceduralRigDefinition>().Feet;
            var ground = new RaisedGround();
            for (int i = 0; i < 120; i++) view.Tick(Vector3.zero, Vector3.up, 1f / 60f, ground);
            float before = Correction(view, feet);
            Assert.Greater(before, .02f, "The fixture must contain an outgoing terrain correction.");
            view.Swimming = action == "swim";
            view.Drinking = action == "drink";
            view.Eating = action == "eat";
            view.Tick(Vector3.zero, Vector3.up, 1f / 60f, ground);
            float first = Correction(view, feet);
            Assert.Greater(first, before * .4f, "Action entry must retain outgoing support long enough to release it.");
            Assert.Less(first, before + .01f);
            for (int i = 0; i < 120; i++) view.Tick(Vector3.zero, Vector3.up, 1f / 60f, ground);
            Assert.Less(Correction(view, feet), .005f, "The authored action must eventually own the ungrounded pose.");
        }

        [Test]
        public void GoatDrinkRecoveryDoesNotKickAllFeetBackIntoSupport()
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(
                "Assets/Art/Creatures/Goat/GoatVisuals.asset");
            using var view = new CreatureAnimationView(null, 0UL, settings.Snapshot(), settings.ModelHeightMeters);
            view.Root.position = Vector3.up * settings.ModelHeightMeters * .5f;
            var ground = new RaisedGround(0f);
            var feet = view.Root.GetComponentInChildren<ProceduralRigDefinition>().Feet;
            const float dt = 1f / 60f;
            for (int i = 0; i < 60; i++) view.Tick(Vector3.zero, Vector3.up, dt, ground);
            view.Drinking = true;
            for (int i = 0; i < Mathf.CeilToInt(settings.Drink.length * 2f / dt); i++)
                view.Tick(Vector3.zero, Vector3.up, dt, ground);
            view.Drinking = false;
            var previous = new Vector3[feet.Length];
            var older = new Vector3[feet.Length];
            float maximum = 0f;
            for (int frame = 0; frame < 90; frame++)
            {
                view.Tick(Vector3.zero, Vector3.up, dt, ground);
                var displayed = new Vector3[feet.Length];
                for (int i = 0; i < feet.Length; i++) displayed[i] = Contact(feet[i]).position;
                view.Pose.RestoreAnimation();
                for (int i = 0; i < feet.Length; i++)
                {
                    Vector3 correction = displayed[i] - Contact(feet[i]).position;
                    if (frame > 1) maximum = Mathf.Max(maximum, (correction - 2f * previous[i] + older[i]).magnitude);
                    older[i] = previous[i]; previous[i] = correction;
                }
            }
            Assert.Less(maximum, .008f, "Returning support must avoid the reproduced 13mm correction acceleration at 60Hz.");
        }

        static float Correction(CreatureAnimationView view, FootDefinition[] feet)
        {
            var displayed = new Vector3[feet.Length];
            for (int i = 0; i < feet.Length; i++) displayed[i] = Contact(feet[i]).position;
            view.Pose.RestoreAnimation();
            float maximum = 0f;
            for (int i = 0; i < feet.Length; i++) maximum = Mathf.Max(maximum, Vector3.Distance(displayed[i], Contact(feet[i]).position));
            return maximum;
        }

        static Transform Contact(FootDefinition foot) => foot.Contact != null ? foot.Contact : foot.Bones[^1];
    }
}
