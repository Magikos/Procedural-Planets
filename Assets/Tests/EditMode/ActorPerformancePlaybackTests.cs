using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorPerformancePlaybackTests
    {
        GameObject _actor;
        AnimationClip _clip;
        ActorAnimationGraph _graph;
        ActorPerformancePlayback _playback;

        [SetUp]
        public void SetUp()
        {
            _actor = new GameObject("Performance test");
            _clip = new AnimationClip();
            _clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 2f, 1f));
            var data = new ActorAnimationPerformanceLibraryData(new[]
            {
                new ActorAnimationPerformanceLibrary.Entry
                {
                    Id = "basic", Action = "Jump", RigFamily = "Humanoid",
                    Phases = new[]
                    {
                        new ActorAnimationPerformanceLibrary.Phase { Name = "Launch", Clip = _clip, StartNormalized = 0.1f, EndNormalized = 0.4f, BlendSeconds = 1f },
                        new ActorAnimationPerformanceLibrary.Phase { Name = "Land", Clip = _clip, StartNormalized = 0.6f, EndNormalized = 0.9f, BlendSeconds = 1f, Loop = true }
                    }
                }
            });
            _graph = new ActorAnimationGraph(_actor.AddComponent<Animator>(), "Performance test", 5);
            _graph.AddBaseClip(0, _clip);
            _graph.BaseMixer.SetInputWeight(0, 0.3f);
            _playback = new ActorPerformancePlayback(_graph, 1, data);
        }

        [TearDown]
        public void TearDown()
        {
            _graph.Dispose();
            Object.DestroyImmediate(_actor);
            Object.DestroyImmediate(_clip);
        }

        [Test]
        public void PhaseMapsTimeAndDoesNotRestartFadeEveryFrame()
        {
            Assert.IsTrue(_playback.Begin("Jump", "Humanoid"));
            _playback.ApplyPhase("Launch", 0.5f, 0.5f);
            Assert.AreEqual(0.5f, _playback.Weight, 0.0001f);
            Assert.AreEqual(0.25f, _playback.CurrentNormalizedTime, 0.0001f);
            Assert.AreEqual(0.5d, _graph.BaseMixer.GetInput(1).GetTime(), 0.0001d);
            _playback.ApplyPhase("Launch", 1f, 0.5f);
            Assert.AreEqual(1f, _playback.Weight);
            Assert.AreEqual(0.3f, _graph.BaseMixer.GetInputWeight(0));
        }

        [Test]
        public void InterruptedFadeRetainsCurrentWeightsAndClearFinishes()
        {
            _playback.Begin("Jump", "Humanoid");
            _playback.ApplyPhase("Launch", 1f, 1f);
            _playback.ApplyPhase("Land", 0f, 0.25f);
            Assert.AreEqual(0.75f, _graph.BaseMixer.GetInputWeight(1));
            Assert.AreEqual(0.25f, _graph.BaseMixer.GetInputWeight(3));
            _playback.ApplyPhase("Launch", 0f, 0f);
            Assert.AreEqual(0.75f, _graph.BaseMixer.GetInputWeight(1));
            Assert.AreEqual(0.25f, _graph.BaseMixer.GetInputWeight(3));
            _playback.Clear(0.5f);
            Assert.AreEqual(0.5f, _playback.Weight, 0.0001f);
            _playback.Clear(0.5f);
            Assert.AreEqual(0f, _playback.Weight);
            Assert.IsNull(_playback.ActivePerformance);
            Assert.AreEqual(0.3f, _graph.BaseMixer.GetInputWeight(0));
        }

        [Test]
        public void RepeatedActionUsesSpareThenDefersThirdRestartWithoutRewindingVisibleClips()
        {
            _playback.Begin("Jump", "Humanoid");
            _playback.ApplyPhase("Launch", 1f, 1f);
            double outgoing = _graph.BaseMixer.GetInput(1).GetTime();
            _playback.Begin("Jump", "Humanoid");
            _playback.ApplyPhase("Launch", 0.2f, 0.25f);
            Assert.AreEqual(outgoing, _graph.BaseMixer.GetInput(1).GetTime());
            Assert.AreEqual(0.75f, _graph.BaseMixer.GetInputWeight(1));
            Assert.AreEqual(0.25f, _graph.BaseMixer.GetInputWeight(2));
            double incoming = _graph.BaseMixer.GetInput(2).GetTime();
            _playback.Begin("Jump", "Humanoid");
            _playback.ApplyPhase("Launch", 0f, 0f);
            Assert.IsTrue(_playback.RestartPending);
            Assert.AreEqual(outgoing, _graph.BaseMixer.GetInput(1).GetTime());
            Assert.AreEqual(incoming, _graph.BaseMixer.GetInput(2).GetTime());
            Assert.AreEqual(0.75f, _graph.BaseMixer.GetInputWeight(1));
            _playback.ApplyPhase("Launch", 0.3f, 0.75f);
            Assert.AreEqual(0f, _graph.BaseMixer.GetInputWeight(1));
            Assert.AreEqual(incoming, _graph.BaseMixer.GetInput(2).GetTime());
            _playback.ApplyPhase("Launch", 0.4f, 0f);
            Assert.IsFalse(_playback.RestartPending);
            Assert.AreEqual(0.44d, _graph.BaseMixer.GetInput(1).GetTime(), 0.0001d);
            Assert.AreEqual(incoming, _graph.BaseMixer.GetInput(2).GetTime());
            Assert.AreEqual(1f, _graph.BaseMixer.GetInputWeight(2));
        }

        [Test]
        public void PhaseReentryDoesNotRewindOutgoingPhaseInstance()
        {
            _playback.Begin("Jump", "Humanoid");
            _playback.ApplyPhase("Launch", 1f, 1f);
            _playback.ApplyPhase("Land", 0f, 0.25f);
            double outgoing = _graph.BaseMixer.GetInput(1).GetTime();
            _playback.ApplyPhase("Launch", 0f, 0f);
            Assert.AreEqual(outgoing, _graph.BaseMixer.GetInput(1).GetTime());
            Assert.AreEqual(0.2d, _graph.BaseMixer.GetInput(2).GetTime(), 0.0001d);
            Assert.AreEqual(0f, _graph.BaseMixer.GetInputWeight(2));
        }

        [Test]
        public void LoopWrapsWithinAuthoredRangeWhileNonLoopingPhaseClamps()
        {
            _playback.Begin("Jump", "Humanoid");
            _playback.ApplyPhase("Launch", 2.25f, 1f);
            Assert.AreEqual(0.4f, _playback.CurrentNormalizedTime, 0.0001f);
            _playback.ApplyPhase("Land", 2.25f, 1f);
            Assert.AreEqual(0.675f, _playback.CurrentNormalizedTime, 0.0001f);
            _playback.ApplyPhase("Land", 3f, 0f);
            Assert.AreEqual(0.6f, _playback.CurrentNormalizedTime, 0.0001f);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _playback.ApplyPhase("Land", -0.1f, 0f));
        }

        [Test]
        public void ResetClearsOnlyOwnedSlotsAndUnsupportedSelectionFails()
        {
            _playback.Begin("Jump", "Humanoid");
            _playback.ApplyPhase("Launch", 0f, 1f);
            _playback.Reset();
            Assert.AreEqual(0f, _graph.BaseMixer.GetInputWeight(1));
            Assert.AreEqual(0f, _graph.BaseMixer.GetInputWeight(3));
            Assert.AreEqual(0.3f, _graph.BaseMixer.GetInputWeight(0));
            Assert.IsFalse(_playback.Begin("Jump", "Dog"));
            Assert.IsTrue(_graph.IsValid());
        }
    }
}

