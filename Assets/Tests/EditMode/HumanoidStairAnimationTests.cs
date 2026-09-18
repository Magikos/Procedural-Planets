using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

namespace ProceduralPlanets.Tests
{
    public sealed class HumanoidStairAnimationTests
    {
        sealed class Treads : IGroundingProvider
        {
            public float Rise;
            public bool Slope;
            public bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult result)
            {
                float height = Slope ? position.z * Rise : position.z >= 0f ? Rise : 0f;
                result = new GroundResult(new Vector3(position.x, height + offset, position.z),
                    Slope ? new Vector3(0f, 1f, -Rise).normalized : Vector3.up);
                return true;
            }
        }

        [TestCase(.2f, false, 0)]
        [TestCase(-.2f, false, 1)]
        [TestCase(0f, false, -1)]
        [TestCase(.25f, true, -1)]
        public void StairSelectionUsesTreadDirectionAndPreservesAuthority(float rise, bool slope, int expected)
        {
            const string folder = "Assets/Art/Characters/";
            AnimationClip Clip(string name) => AssetDatabase.LoadAllAssetsAtPath(folder + "Animations/" + name + ".fbx")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(folder + "Baseline/Baseline.prefab"));
            HumanoidAnimationView view = null;
            try
            {
                var animator = actor.GetComponent<Animator>();
                var rig = actor.AddComponent<ProceduralRigDefinition>();
                HumanoidRigBinding.Bind(animator, rig);
                view = new HumanoidAnimationView(animator, rig, Clip("HumanoidIdle"), Clip("HumanoidWalk"), Clip("HumanoidRun"),
                    stairs: new[] { Clip("Stair Walk Up"), Clip("Stair Walk Down") });
                actor.transform.position = Vector3.zero;
                view.SetLocomotionState(ActorStance.Standing, true, TraversalKind.None, 0f);
                var ground = new Treads { Rise = rise, Slope = slope };
                for (int frame = 0; frame < 100; frame++)
                {
                    view.Tick(Vector3.forward * 1.4f, Vector3.up, null, ground, .02f);
                    float sum = 0f;
                    for (int i = 0; i < view.Graph.BaseMixer.GetInputCount(); i++)
                    {
                        float weight = view.Graph.BaseMixer.GetInputWeight(i);
                        Assert.GreaterOrEqual(weight, 0f); sum += weight;
                    }
                    Assert.AreEqual(1f, sum, .001f);
                    Assert.AreEqual(Vector3.zero, actor.transform.position);
                }
                if (expected >= 0) Assert.Greater(view.StairBlend[expected], .99f);
                else Assert.Less(view.StairBlend.sqrMagnitude, .001f);
                view.SetLocomotionState(ActorStance.Standing, false, TraversalKind.None, 0f);
                for (int i = 0; i < 100; i++) view.Tick(Vector3.forward, Vector3.up, null, ground, .02f);
                Assert.Less(view.StairBlend.sqrMagnitude, .001f);
            }
            finally { view?.Dispose(); Object.DestroyImmediate(actor); }
        }
    }
}
