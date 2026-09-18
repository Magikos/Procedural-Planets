using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class AuthoredInteractionFootTravelTests
    {
        sealed class Floor : IGroundingProvider
        {
            public bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult result)
            {
                result = new GroundResult(new Vector3(position.x, offset, position.z), Vector3.up);
                return true;
            }
        }

        [Test]
        public void AuthoredStationaryInteractionRetainsItsSteppingFeet()
        {
            const string folder = "Assets/Art/Characters/";
            var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(folder + "Baseline/Baseline.prefab"));
            var library = ScriptableObject.CreateInstance<ActorAnimationPerformanceLibrary>();
            AnimationClip Clip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .First(c => !c.name.StartsWith("__preview__"));
            var close = Clip("Assets/Art/Interactions/Animations/Loot_TreasureChest_Close_Only.fbx");
            library.Entries = new[] { new ActorAnimationPerformanceLibrary.Entry { Id = "close", Action = "Close",
                RigFamily = "Humanoid", Phases = new[] { new ActorAnimationPerformanceLibrary.Phase {
                    Name = "Close", Clip = close, StartNormalized = 0f, EndNormalized = 1f, BlendSeconds = .15f } } } };
            try
            {
                var animator = actor.GetComponent<Animator>();
                var rig = actor.AddComponent<ProceduralRigDefinition>();
                HumanoidRigBinding.Bind(animator, rig);
                using var view = new HumanoidAnimationView(animator, rig,
                    Clip(folder + "Animations/HumanoidIdle.fbx"), Clip(folder + "Animations/HumanoidWalk.fbx"),
                    Clip(folder + "Animations/HumanoidRun.fbx"), performances: library.Snapshot());
                view.Pose.LookEnabled = view.Pose.SpineEnabled = view.Pose.ChainsEnabled = false;
                var floor = new Floor();
                var feet = rig.Feet.Select(f => f.Contact != null ? f.Contact : f.Bones[^1]).ToArray();
                var displayed = new Vector3[feet.Length];
                float maximum = 0f;
                for (int i = 0; i < 90; i++)
                {
                    view.SetLocomotionState(ActorStance.Standing, true, TraversalKind.None, 0f);
                    view.Tick(Vector3.zero, Vector3.up, null, floor, 1f / 60f);
                }
                Assert.IsTrue(view.BeginInteraction("Close"));
                for (int frame = 0; frame < Mathf.CeilToInt(close.length * 60f); frame++)
                {
                    view.SetInteractionPhase("Close", frame / (close.length * 60f));
                    view.Tick(Vector3.zero, Vector3.up, null, floor, 1f / 60f);
                    for (int i = 0; i < feet.Length; i++) displayed[i] = feet[i].position;
                    view.Pose.RestoreAnimation();
                    for (int i = 0; i < feet.Length; i++)
                        maximum = Mathf.Max(maximum, Vector3.ProjectOnPlane(displayed[i] - feet[i].position, Vector3.up).magnitude);
                }
                Assert.Less(maximum, .025f, "Zero motor input must not pin an authored interaction step to an idle anchor.");
            }
            finally { Object.DestroyImmediate(actor); Object.DestroyImmediate(library); }
        }
    }
}

