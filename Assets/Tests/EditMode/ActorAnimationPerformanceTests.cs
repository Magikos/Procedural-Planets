using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorAnimationPerformanceTests
    {
        ActorAnimationPerformanceLibrary _library;
        AnimationClip _clip;

        [SetUp]
        public void SetUp()
        {
            _library = ScriptableObject.CreateInstance<ActorAnimationPerformanceLibrary>();
            _clip = new AnimationClip();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_library);
            Object.DestroyImmediate(_clip);
        }

        ActorAnimationPerformanceLibrary.Entry Entry(string id, float minimum = 0f, string condition = "") => new()
        {
            Id = id, Action = "Jump", RigFamily = "Humanoid", MinProficiency = minimum, Condition = condition,
            Phases = new[] { new ActorAnimationPerformanceLibrary.Phase { Name = "Launch", Clip = _clip } }
        };

        [Test]
        public void SelectionUsesMostProficientEligibleTechniqueAndStableTieBreak()
        {
            _library.Entries = new[] { Entry("basic"), Entry("expert-z", 0.7f), Entry("expert-a", 0.7f) };
            var data = _library.Snapshot();
            Assert.AreEqual("basic", data.Select("Jump", "Humanoid").Id);
            Assert.AreEqual("basic", data.Select("Jump", "Humanoid", 0.69f).Id);
            Assert.AreEqual("expert-a", data.Select("Jump", "Humanoid", 0.7f).Id);
            Array.Reverse(_library.Entries);
            Assert.AreEqual("expert-a", _library.Snapshot().Select("Jump", "Humanoid", 1f).Id);
            Assert.IsNull(data.Select("Jump", "Quadruped", 1f));
            Assert.IsNull(data.Select("Swim", "Humanoid", 1f));
        }

        [Test]
        public void LocomotionExitPhaseIsOptionalValidatedAndCopied()
        {
            var entry = Entry("jump");
            _library.Entries = new[] { entry };
            Assert.AreEqual(-1f, _library.Snapshot()[0].LocomotionExitPhase);
            entry.LocomotionExitPhase = .55f;
            var data = _library.Snapshot();
            entry.LocomotionExitPhase = .2f;
            Assert.AreEqual(.55f, data[0].LocomotionExitPhase);
            foreach (float invalid in new[] { -.1f, -2f, 1.01f, float.NaN, float.PositiveInfinity })
            {
                entry.LocomotionExitPhase = invalid;
                Assert.Throws<ArgumentOutOfRangeException>(() => _library.Snapshot());
            }
        }

        [Test]
        public void ExactConditionWinsAndMissingConditionFallsBackToBasic()
        {
            _library.Entries = new[] { Entry("expert", 0.8f), Entry("basic"), Entry("limp", 0f, "LeftLegWounded") };
            var data = _library.Snapshot();
            Assert.AreEqual("limp", data.Select("Jump", "Humanoid", 1f, "LeftLegWounded").Id);
            Assert.AreEqual("expert", data.Select("Jump", "Humanoid", 1f, "RightLegWounded").Id);
            _library.Entries = new[] { Entry("limp", 0f, "LeftLegWounded") };
            Assert.IsNull(_library.Snapshot().Select("Jump", "Humanoid", 0f));
        }

        [Test]
        public void SnapshotCopiesAuthoringDataButSharesClipResource()
        {
            _library.Entries = new[] { Entry("basic") };
            var data = _library.Snapshot();
            _library.Entries[0].Id = "changed";
            _library.Entries[0].Phases[0].Name = "changed";
            _library.Entries[0].Phases[0].Clip = null;
            _library.Entries = null;
            Assert.AreEqual(1, data.Count);
            Assert.AreEqual("basic", data[0].Id);
            Assert.AreEqual(1, data[0].PhaseCount);
            Assert.AreSame(_clip, data[0].GetPhase("Launch").Clip);
            Assert.IsNull(data[0].GetPhase("Recovery"));
        }

        [Test]
        public void SelectionAllocatesNoManagedMemoryAfterWarmup()
        {
            _library.Entries = new[] { Entry("basic"), Entry("expert", 0.7f) };
            var data = _library.Snapshot();
            for (int i = 0; i < 100; i++) data.Select("Jump", "Humanoid", 1f);
            long start = GC.GetAllocatedBytesForCurrentThread();
            ActorAnimationPerformance selected = null;
            for (int i = 0; i < 1000; i++) selected = data.Select("Jump", "Humanoid", 1f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            Assert.AreEqual(0L, allocated);
            Assert.AreEqual("expert", selected.Id);
        }

        [Test]
        public void InvalidAuthoringDataFailsAtSnapshotBoundary()
        {
            _library.Entries = null;
            Assert.Throws<ArgumentNullException>(() => _library.Snapshot());
            _library.Entries = new ActorAnimationPerformanceLibrary.Entry[] { null };
            Assert.Throws<ArgumentException>(() => _library.Snapshot());
            _library.Entries = new[] { Entry("same"), Entry("same") };
            Assert.Throws<ArgumentException>(() => _library.Snapshot());
            var entry = Entry("basic");
            _library.Entries = new[] { entry };
            entry.Phases = null;
            Assert.Throws<ArgumentException>(() => _library.Snapshot());
            entry.Phases = new ActorAnimationPerformanceLibrary.Phase[] { null };
            Assert.Throws<ArgumentException>(() => _library.Snapshot());
            entry.Phases = Entry("valid").Phases;
            entry.Phases = new[] { entry.Phases[0], entry.Phases[0] };
            Assert.Throws<ArgumentException>(() => _library.Snapshot());
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-0.1f)]
        [TestCase(1.1f)]
        public void InvalidProficiencyRejected(float value)
        {
            _library.Entries = new[] { Entry("basic", value) };
            Assert.Throws<ArgumentOutOfRangeException>(() => _library.Snapshot());
            _library.Entries = new[] { Entry("basic") };
            var data = _library.Snapshot();
            Assert.Throws<ArgumentOutOfRangeException>(() => data.Select("Jump", "Humanoid", value));
        }

        [Test]
        public void InvalidClipRangeFadeAndKeysRejected()
        {
            var entry = Entry("basic");
            _library.Entries = new[] { entry };
            entry.Phases[0].Clip = null;
            Assert.Throws<ArgumentException>(() => _library.Snapshot());
            entry.Phases[0].Clip = _clip;
            entry.Phases[0].StartNormalized = 1f;
            Assert.Throws<ArgumentException>(() => _library.Snapshot());
            entry.Phases[0].StartNormalized = 0f;
            entry.Phases[0].BlendSeconds = float.NaN;
            Assert.Throws<ArgumentOutOfRangeException>(() => _library.Snapshot());
            entry.Phases[0].BlendSeconds = 0f;
            entry.Action = " ";
            Assert.Throws<ArgumentException>(() => _library.Snapshot());
        }
    }
}
