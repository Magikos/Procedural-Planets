using System;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorPerceptionTests
    {
        static ActorStimulus Signal(ActorSense sense, Vector3 position, float strength = 1) =>
            new(10, ActorObservationKind.Actor, sense, position, strength, 0, 20);
        static bool Detect(ActorPerception perception, ActorKnowledge memory, ActorStimulus signal, float light = 1,
            bool blocked = false, double now = 0) => perception.Observe(1, Vector3.zero, Vector3.forward, Vector3.up,
                light, blocked, signal, now, memory);
        [Test]
        public void SightUsesLightFieldOfViewAndOcclusion()
        {
            var weak = new ActorPerception(new(DaySight: 20, NightSight: 3, ViewAngle: 120));
            var keen = new ActorPerception(weak.Profile with { NightSight = 15 });
            var memory = new ActorKnowledge(); var signal = Signal(ActorSense.Sight, Vector3.forward * 10);
            Assert.IsTrue(Detect(weak, memory, signal));
            Assert.IsFalse(Detect(weak, memory, signal, 0));
            Assert.IsTrue(Detect(keen, memory, signal, 0));
            Assert.IsFalse(Detect(keen, memory, signal, 1, true));
            Assert.IsFalse(Detect(keen, memory, Signal(ActorSense.Sight, Vector3.back * 2)));
        }
        [Test]
        public void HearingUsesEmittedLocationAndDoesNotRenewOldSound()
        {
            var sense = new ActorPerception(new()); var memory = new ActorKnowledge();
            var sound = Signal(ActorSense.Hearing, Vector3.forward * 6, 2);
            Assert.IsTrue(Detect(sense, memory, sound, blocked: true));
            memory.TryGet(10, ActorObservationKind.Actor, 0, out var first);
            Assert.Greater(first.Uncertainty, .5f); Assert.Less(first.IdentityConfidence, 1);
            Detect(sense, memory, sound, blocked: true, now: 5);
            memory.TryGet(10, ActorObservationKind.Actor, 5, out var later);
            Assert.AreEqual(first.Position, later.Position); Assert.AreEqual(first.Expires, later.Expires);
            Assert.Greater(later.Uncertainty, first.Uncertainty);
            Assert.IsFalse(Detect(sense, memory, sound, now: 13));
        }
        [Test]
        public void ScentResolutionIsIndependentOfDetectionRange()
        {
            var coarse = new ActorPerception(new(SmellResolution: 8));
            var keen = new ActorPerception(coarse.Profile with { SmellResolution = .5f });
            var a = new ActorKnowledge(); var b = new ActorKnowledge(); var signal = Signal(ActorSense.Smell, new(2, 1, 8));
            Assert.IsTrue(Detect(coarse, a, signal, blocked: true)); Assert.IsTrue(Detect(keen, b, signal, blocked: true));
            a.TryGet(10, ActorObservationKind.Actor, 0, out var rough); b.TryGet(10, ActorObservationKind.Actor, 0, out var precise);
            Assert.Greater(rough.Uncertainty, precise.Uncertainty * 10);
            Assert.LessOrEqual(Vector3.Distance(rough.Position, signal.Position), rough.Uncertainty);
            Assert.LessOrEqual(Vector3.Distance(precise.Position, signal.Position), precise.Uncertainty);
            Assert.IsFalse(Detect(coarse, a, signal, now: 20));
        }
        [Test]
        public void SharedMemoryLosesPrecisionWithoutFollowingTargetOrRefreshingExpiry()
        {
            var a = new ActorKnowledge(); var b = new ActorKnowledge(); var sense = new ActorPerception(new());
            Detect(sense, a, Signal(ActorSense.Sight, Vector3.forward * 5));
            a.TryGet(10, ActorObservationKind.Actor, 0, out var first);
            a.ShareWith(b, 5); b.ShareWith(a, 8);
            b.TryGet(10, ActorObservationKind.Actor, 8, out var shared);
            Assert.AreEqual(first.Position, shared.Position); Assert.AreEqual(first.Expires, shared.Expires);
            Assert.Greater(shared.Uncertainty, first.Uncertainty); Assert.Less(shared.Confidence, first.Confidence);
            Assert.IsFalse(b.Knows(10, ActorObservationKind.Actor, 12));
        }
        [Test]
        public void ProfilesInheritDefaultsAndRejectCyclesDuplicatesAndInvalidValues()
        {
            var parent = ScriptableObject.CreateInstance<CreaturePerceptionSettings>();
            var child = ScriptableObject.CreateInstance<CreaturePerceptionSettings>();
            try
            {
                parent.Overrides = new[] { new CreaturePerceptionSettings.Override { Setting = CreaturePerceptionSettings.Setting.NightSight, Value = 18 } };
                child.Parent = parent;
                child.Overrides = new[] { new CreaturePerceptionSettings.Override { Setting = CreaturePerceptionSettings.Setting.SmellResolution, Value = .5f } };
                var result = child.Snapshot(); Assert.AreEqual(18, result.NightSight); Assert.AreEqual(.5f, result.SmellResolution);
                Assert.AreEqual(new ActorPerceptionProfile().HearingRange, result.HearingRange);
                parent.Parent = child; Assert.Throws<InvalidOperationException>(() => child.Snapshot()); parent.Parent = null;
                child.Overrides = new[] { child.Overrides[0], child.Overrides[0] };
                Assert.Throws<InvalidOperationException>(() => child.Snapshot());
                Assert.Throws<ArgumentOutOfRangeException>(() => new ActorPerception(new(SmellRange: float.NaN)));
            }
            finally { UnityEngine.Object.DestroyImmediate(parent); UnityEngine.Object.DestroyImmediate(child); }
        }
        [Test]
        public void CuriosityDoesNotOverrideCriticalNeedsButDangerDoes()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
            var senses = new CreatureSenses { Forward = Vector3.forward, Up = Vector3.up, DeltaTime = .1f,
                HasInterest = true, InterestId = new EntityId(20), InterestPosition = Vector3.forward * 8,
                Needs = new ActorNeeds(1, .1), Food = new CreatureResourceTarget { Id = new EntityId(10), Available = true } };
            brain.Observe(senses); brain.Sample(0); Assert.AreEqual(CreatureObjective.FindFood, brain.Objective);
            senses.HasThreat = true; senses.ThreatPosition = Vector3.right;
            brain.Observe(senses); brain.Sample(1); Assert.AreEqual(CreatureObjective.Escape, brain.Objective);
        }
        [Test]
        public void TrackerLeavesSearchedScentForANewerTrailSample()
        {
            var memory = new ActorKnowledge(); var sense = new ActorPerception(new(SmellResolution: .5f));
            var first = Signal(ActorSense.Smell, Vector3.forward);
            Detect(sense, memory, first);
            memory.TryGet(10, ActorObservationKind.Actor, 0, out var found);
            sense.SearchReachedScent(found.Position, Vector3.up, found, memory);
            Assert.IsFalse(Detect(sense, memory, first));
            var next = new ActorStimulus(10, ActorObservationKind.Actor, ActorSense.Smell, Vector3.forward * 4, 1, 1, 20);
            Assert.IsTrue(Detect(sense, memory, next, now: 1));
        }
        [Test]
        public void CurrentSightOverridesAStrongScentLeftBehind()
        {
            var memory = new ActorKnowledge(); var sense = new ActorPerception(new(SmellResolution: .5f));
            var trail = Signal(ActorSense.Smell, Vector3.forward);
            Detect(sense, memory, trail);
            var actor = Signal(ActorSense.Sight, Vector3.forward * 15);
            Detect(sense, memory, actor);
            Detect(sense, memory, trail);
            memory.TryGet(10, ActorObservationKind.Actor, 0, out var observation);
            Assert.AreEqual(ActorSense.Sight, observation.Sense);
            Assert.Less(Vector3.Distance(observation.Position, actor.Position), .2f);
            var moved = new ActorStimulus(10, ActorObservationKind.Actor, ActorSense.Sight, Vector3.forward * 20, 1, .1, 20);
            Detect(sense, memory, moved, now: .1);
            memory.TryGet(10, ActorObservationKind.Actor, .1, out var fresh);
            Assert.Greater(fresh.Position.z, 19.8f, "Older, stronger sight must not override current sight.");
        }
        [Test]
        public void ConsumptionApproachesInsideTheEstimatedContactBoundary()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
            var senses = new CreatureSenses { Forward = Vector3.forward, Up = Vector3.up, DeltaTime = .1f,
                Needs = new ActorNeeds(.8, .1), Food = new CreatureResourceTarget { Id = new EntityId(10),
                    Position = Vector3.forward * 1.05f, Available = true, Uncertainty = .15f } };
            ActorIntent intent = default;
            for (uint i = 0; i < 3; i++) { brain.Observe(senses); intent = brain.Sample(i); }
            Assert.AreEqual(CreatureBehaviour.Feed, brain.Behaviour);
            Assert.Greater(intent.Move.y, 0, "Estimated arrival must not stop outside actual contact.");
        }
        [Test]
        public void UncertainInterestInvestigatesThenTimesOutAndCannotStrike()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
            var senses = new CreatureSenses { Position = Vector3.zero, Forward = Vector3.forward, Up = Vector3.up,
                HasInterest = true, InvestigateInterest = true, InterestId = new EntityId(10), InterestPosition = Vector3.forward * 5,
                DeltaTime = .1f, PerceptionLimited = true, Needs = new ActorNeeds(.7, .1) };
            bool searched = false;
            for (uint i = 0; i < 85; i++) { brain.Observe(senses); brain.Sample(i); searched |= brain.Behaviour == CreatureBehaviour.Investigate; }
            Assert.IsTrue(searched); Assert.AreNotEqual(CreatureObjective.Investigate, brain.Objective);
            var context = new CreatureContext { Objective = CreatureObjective.Hunt, Senses = senses };
            Assert.IsFalse(CreatureHunt.CanStrike(context, Vector3.forward));
            senses.HasThreat = true; senses.ThreatPosition = Vector3.right * 2;
            brain.Observe(senses); brain.Sample(86); Assert.AreEqual(CreatureObjective.Escape, brain.Objective);
        }
    }
}
