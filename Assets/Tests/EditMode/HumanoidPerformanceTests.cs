using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

namespace ProceduralPlanets.Tests
{
    public sealed class HumanoidPerformanceTests
    {
        const string Folder = "Assets/Art/Characters";
        GameObject _actor;
        HumanoidAnimationView _view;

        [SetUp]
        public void SetUp()
        {
            _actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Baseline/Baseline.prefab"));
            var animator = _actor.GetComponent<Animator>();
            var rig = _actor.AddComponent<ProceduralRigDefinition>();
            HumanoidRigBinding.Bind(animator, rig);
            ConfigureLibrary("Basic Performances");
        }

        ActorAnimationPerformanceLibraryData ConfigureLibrary(string name)
        {
            _view?.Dispose();
            AnimationClip Clip(string clipName) => AssetDatabase.LoadAllAssetsAtPath(Folder + "/Animations/" + clipName + ".fbx")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var library = AssetDatabase.LoadAssetAtPath<ActorAnimationPerformanceLibrary>(Folder + "/Motion/" + name + ".asset");
            Assert.IsNotNull(library, "Configure the review performance library first.");
            var data = library.Snapshot();
            _view = new HumanoidAnimationView(_actor.GetComponent<Animator>(), _actor.GetComponent<ProceduralRigDefinition>(),
                Clip("HumanoidIdle"), Clip("HumanoidWalk"), Clip("HumanoidRun"), performances: data);
            return data;
        }

        [TearDown]
        public void TearDown() { _view?.Dispose(); Object.DestroyImmediate(_actor); }

        void Step(bool grounded, bool jumping, float vertical, bool evaluate = true, TraversalKind traversal = TraversalKind.None,
            Vector3? velocity = null)
        {
            _view.SetLocomotionState(ActorStance.Standing, grounded, traversal, 0f, jumping: jumping);
            _view.Tick(velocity ?? new Vector3(0f, vertical, 2f), Vector3.up, null, null, .02f, evaluate);
            float total = 0f;
            for (int i = 0; i < _view.Graph.BaseMixer.GetInputCount(); i++)
            {
                float weight = _view.Graph.BaseMixer.GetInputWeight(i);
                Assert.That(weight, Is.InRange(0f, 1f)); total += weight;
            }
            Assert.That(total, Is.EqualTo(1f).Within(.0001f));
            Assert.That(_actor.transform.position, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void PhysicalFlightHoldsBeforeLandingAndRecoveryReturnsToLocomotion()
        {
            Step(false, true, 5f);
            Assert.AreEqual("humanoid.jump.basic", _view.PerformanceId);
            Assert.AreEqual("Ascent", _view.PerformancePhase);
            float start = _view.PerformanceTime;
            Step(false, true, 2f);
            Assert.Greater(_view.PerformanceTime, start);
            Step(false, true, 0f);
            Assert.AreEqual("Apex", _view.PerformancePhase);
            for (int i = 0; i < 100; i++) Step(false, true, -8f);
            Assert.AreEqual("Descent", _view.PerformancePhase);
            Assert.LessOrEqual(_view.PerformanceTime, (56f - 20f) / 54f);
            Step(true, false, 0f);
            Assert.AreEqual("Landing", _view.PerformancePhase);
            for (int i = 0; i < 50; i++) Step(true, false, 0f);
            Assert.IsNull(_view.PerformanceId);
        }

        [Test]
        public void SkippedPosesKeepPhaseAndTraversalInterruptsJump()
        {
            Step(false, true, 5f, false);
            Step(false, true, -2f, false);
            Assert.AreEqual("Descent", _view.PerformancePhase);
            Step(false, false, 0f, true, TraversalKind.JumpGrab);
            Assert.AreEqual(ActorAirbornePhase.Grounded, _view.Airborne.Phase);
            Assert.IsNull(_view.PerformanceId);
            _view.Reset();
            Step(false, true, 4f);
            Assert.AreEqual("Ascent", _view.PerformancePhase);
        }

        [Test]
        public void PassiveDropDoesNotSelectAnIntentionalJump()
        {
            Step(false, false, -1f);
            Assert.AreEqual(ActorAirbornePhase.Falling, _view.Airborne.Phase);
            Assert.IsNull(_view.PerformanceId);
            Step(true, false, 0f);
            Assert.AreEqual(ActorAirbornePhase.Grounded, _view.Airborne.Phase);
        }

        [TestCase(0f, 5f, true)]
        [TestCase(0f, -5f, false)]
        [TestCase(5f, 0f, false)]
        [TestCase(0f, 0f, false)]
        public void AuthoredJumpSelectionUsesTakeoffDirectionAndRetainsItThroughRecovery(float sideways, float forward, bool running)
        {
            var library = ConfigureLibrary("Authored Jump Performances");
            string condition = sideways == 0f && forward == 0f ? "Stationary" : running ? "Running" : "";
            var expected = library.Select("Jump", "Humanoid", condition: condition);
            Assert.IsNotNull(expected);
            Assert.AreEqual(condition, expected.Condition);
            if (running) Assert.AreEqual("humanoid.jump.running", expected.Id);

            Step(false, true, 5f, velocity: new Vector3(sideways, 5f, forward));
            Assert.AreEqual(expected.Id, _view.PerformanceId);
            var landing = expected.GetPhase("Landing");
            float duration = landing.Clip.length * (landing.EndNormalized - landing.StartNormalized);
            Assert.That(_view.Airborne.RecoveryDuration, Is.EqualTo(duration).Within(.0001f));

            // Reverse the selection condition in flight. An active authored performance must keep its identity.
            Vector3 changed = new Vector3(0f, -3f, running ? -5f : 5f);
            Step(false, true, -3f, velocity: changed);
            Assert.AreEqual(expected.Id, _view.PerformanceId);
            Assert.AreEqual("Descent", _view.PerformancePhase);
            Step(true, false, 0f, velocity: Vector3.zero);
            Assert.AreEqual("Landing", _view.PerformancePhase);
            for (int i = 1; i * .02f < duration - .001f; i++)
            {
                Step(true, false, 0f, velocity: Vector3.zero);
                Assert.AreEqual(expected.Id, _view.PerformanceId, "Recovery must retain the selected authored landing.");
            }
            for (int i = 0; i < 3; i++) Step(true, false, 0f, velocity: Vector3.zero);
            Assert.IsNull(_view.PerformanceId);
            Assert.AreEqual(ActorAirbornePhase.Grounded, _view.Airborne.Phase);
        }

        [Test]
        public void StandingHopPreparationPrecedesAirbornePlayback()
        {
            ConfigureLibrary("Authored Jump Performances");
            Assert.IsTrue(_view.PrepareStandingHop(.5f));
            Step(true, false, 0f);
            Assert.AreEqual("Preparation", _view.PerformancePhase);
            Assert.AreEqual(ActorAirbornePhase.Grounded, _view.Airborne.Phase);
            _view.PrepareStandingHop(-1f);
            Step(false, true, 5f, velocity: Vector3.up * 5f);
            Assert.AreEqual("humanoid.jump.stationary", _view.PerformanceId);
            Assert.AreEqual("Ascent", _view.PerformancePhase);
        }

        [Test]
        public void CancelledPreparationReleasesWithoutStartingFlight()
        {
            ConfigureLibrary("Authored Jump Performances");
            Assert.IsTrue(_view.PrepareStandingHop(.5f));
            Step(true, false, 0f);
            _view.PrepareStandingHop(-1f);
            for (int i = 0; i < 40; i++) Step(true, false, 0f);
            Assert.IsNull(_view.PerformanceId);
            Assert.AreEqual(ActorAirbornePhase.Grounded, _view.Airborne.Phase);
        }

        [Test]
        public void RunningJumpTraversalInterruptionDuringEntryKeepsNormalizedWeights()
        {
            ConfigureLibrary("Authored Jump Performances");
            Step(false, true, 5f, velocity: new Vector3(0f, 5f, 5f));
            Assert.AreEqual("humanoid.jump.running", _view.PerformanceId);
            Step(false, false, 0f, traversal: TraversalKind.JumpGrab, velocity: Vector3.zero);
            Assert.IsNull(_view.PerformanceId);
            // Step checks every graph weight and total, including a new jump during the outgoing fade.
            Step(false, true, 5f, velocity: new Vector3(0f, 5f, 5f));
            Assert.AreEqual("humanoid.jump.running", _view.PerformanceId);
        }

        [Test]
        public void RunningLandingMatchesIncomingGaitBeforeItsReleaseBlend()
        {
            var library = ConfigureLibrary("Authored Jump Performances");
            float exit = library.Select("Jump", "Humanoid", condition: "Running").LocomotionExitPhase;
            Assert.That(exit, Is.InRange(0f, 1f));
            Step(false, true, 5f, velocity: new Vector3(0f, 5f, 5f));
            Step(false, true, -5f, velocity: new Vector3(0f, -5f, 5f));
            Step(true, false, 0f, velocity: Vector3.forward * 5f);
            for (int i = 0; i < 100 && _view.Airborne.Phase == ActorAirbornePhase.Landing; i++)
                Step(true, false, 0f, velocity: Vector3.forward * 5f);
            Assert.AreEqual(ActorAirbornePhase.Grounded, _view.Airborne.Phase);
            var run = (UnityEngine.Animations.AnimationClipPlayable)_view.Graph.BaseMixer.GetInput(2);
            float phase = (float)(run.GetTime() / run.GetAnimationClip().length % 1d);
            Assert.That(Mathf.Repeat(phase - exit, 1f), Is.InRange(0f, .08f));
            Assert.Less(_view.Graph.BaseMixer.GetInputWeight(2), .5f, "The matched gait must enter through the existing blend.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RunningJumpUsesCurrentIntentAtContactWithoutReplayingAirborneStride(bool resume)
        {
            var library = ConfigureLibrary("Authored Jump Performances");
            var running = library.Select("Jump", "Humanoid", condition: "Running");
            Assert.IsNotNull(running.GetPhase("LandingStop"));
            void Tick(bool grounded, Vector3 velocity, Vector3 intent)
            {
                _view.SetLocomotionState(ActorStance.Standing, grounded, TraversalKind.None, 0f,
                    jumping: !grounded, movementIntent: intent);
                _view.Tick(velocity, Vector3.up, null, null, .02f);
                float sum = 0f;
                for (int i = 0; i < _view.Graph.BaseMixer.GetInputCount(); i++) sum += _view.Graph.BaseMixer.GetInputWeight(i);
                Assert.That(sum, Is.EqualTo(1f).Within(.0001f));
            }
            Tick(false, new Vector3(0f, 5f, 5f), Vector3.forward * 5f);
            Tick(false, new Vector3(0f, 2f, 5f), Vector3.zero);
            Vector3 finalIntent = resume ? Vector3.forward * 5f : Vector3.zero;
            Tick(false, new Vector3(0f, -4f, 5f), finalIntent);
            Assert.AreEqual(resume ? "LandingPrepare" : "Descent", _view.PerformancePhase);
            Assert.AreEqual("Forward Jump", running.GetPhase("Descent").Clip.name);
            Tick(true, Vector3.forward * 5f, finalIntent);
            Assert.AreEqual(resume ? "Landing" : "LandingStop", _view.PerformancePhase);
            for (int i = 0; i < 100; i++) Tick(true, finalIntent, finalIntent);
            Assert.AreEqual(ActorAirbornePhase.Grounded, _view.Airborne.Phase);
        }

        [TestCase(1)]
        [TestCase(12)]
        public void MovingDuringStoppingRecoveryReleasesWithoutReplayingTouchdown(int recoveryTicks)
        {
            ConfigureLibrary("Authored Jump Performances");
            _view.SetLocomotionState(ActorStance.Standing, false, TraversalKind.None, 0f, jumping: true, movementIntent: Vector3.zero);
            _view.Tick(new Vector3(0f, 5f, 5f), Vector3.up, null, null, .02f);
            _view.Tick(new Vector3(0f, -5f, 5f), Vector3.up, null, null, .02f);
            _view.SetLocomotionState(ActorStance.Standing, true, TraversalKind.None, 0f, movementIntent: Vector3.zero);
            for (int i = 0; i < recoveryTicks; i++) _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
            Assert.AreEqual("LandingStop", _view.PerformancePhase);
            _view.SetLocomotionState(ActorStance.Standing, true, TraversalKind.None, 0f, movementIntent: Vector3.forward * 5f);
            for (int i = 0; i < 3; i++) _view.Tick(Vector3.forward * 5f, Vector3.up, null, null, .02f);
            Assert.AreEqual(ActorAirbornePhase.Grounded, _view.Airborne.Phase);
            Assert.IsNull(_view.PerformancePhase);
            Assert.Greater(_view.Graph.BaseMixer.GetInputWeight(2), 0f);
            Assert.Less(_view.Graph.BaseMixer.GetInputWeight(2), 1f, "The recovery still fades from its displayed pose.");
        }
    }
}
