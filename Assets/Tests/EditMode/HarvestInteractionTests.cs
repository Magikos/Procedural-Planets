using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class HarvestInteractionTests
    {
        ActorInteractionDefinition _definition;
        AnimationClip _clip;

        [SetUp]
        public void SetUp()
        {
            _clip = new AnimationClip();
            _definition = ScriptableObject.CreateInstance<ActorInteractionDefinition>();
            _definition.Action = "Chop";
            _definition.Phases = new[] {
                new ActorInteractionDefinition.Phase { Seconds = 1f, WaitForInput = true, RepeatUntilInput = true,
                    Marker = "HarvestImpact", MarkerProgress = .5f,
                    Animation = new ActorAnimationPerformanceLibrary.Phase { Name = "Chop", Clip = _clip, Loop = true } },
                new ActorInteractionDefinition.Phase { Seconds = 1f,
                    Animation = new ActorAnimationPerformanceLibrary.Phase { Name = "Finish", Clip = _clip } }
            };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_definition); Object.DestroyImmediate(_clip);
        }

        [TestCase(ScatterInteraction.Chop, "Wood")]
        [TestCase(ScatterInteraction.Mine, "Stone")]
        public void HitchCommitsThreeImpactsAndOneYieldThenFinishes(ScatterInteraction kind, string expectedItem)
        {
            int wood = 0; bool felled = false;
            var service = new HarvestService(_ => { if (felled) return false; felled = true; return true; },
                (_, _) => { }, (item, n) => { Assert.That(item, Is.EqualTo(expectedItem)); wood += n; }, _ => new ProtoHarvestInfo(kind, "Node", 3), null);
            var action = new HarvestInteraction(service, new ScatterPick(7, 0, Vector3.zero, Quaternion.identity, 1), kind == ScatterInteraction.Mine ? ToolTier.BasicPickaxe : ToolTier.BasicAxe, () => !felled);
            var session = new ActorInteractionSession();
            session.Marker += _ => { if (action.Strike().Outcome == HarvestOutcome.Felled) session.Continue(); };
            session.Begin(_definition.Snapshot()); session.Advance(4.5f);
            Assert.That(action.Impacts, Is.EqualTo(3)); Assert.That(wood, Is.EqualTo(3));
            Assert.That(session.Active, Is.False);
            action.Strike(); Assert.That(wood, Is.EqualTo(3));
        }

        [Test]
        public void CollectionGrantsNamedItemOnceAndRejectsUnavailableTarget()
        {
            bool available = false, depleted = false;
            int collected = 0;
            var service = new HarvestService(_ => { if (depleted) return false; depleted = true; return true; },
                (_, _) => { }, (item, count) => { Assert.That(item, Is.EqualTo("Berries")); collected += count; },
                _ => new ProtoHarvestInfo(ScatterInteraction.Collect, "Berries"), null);
            var action = new HarvestInteraction(service, new ScatterPick(7, 0, Vector3.zero, Quaternion.identity, 1),
                new ToolTier("Hands", 1), () => available);
            Assert.That(action.Strike().Outcome, Is.EqualTo(HarvestOutcome.NotHarvestable));
            Assert.That(collected, Is.Zero);
            available = true;
            Assert.That(action.Strike().Outcome, Is.EqualTo(HarvestOutcome.Felled));
            Assert.That(action.Strike().Outcome, Is.EqualTo(HarvestOutcome.AlreadyHarvested));
            Assert.That(collected, Is.EqualTo(1));
        }

        [Test]
        public void StopFinishesCurrentCycleWithoutAnExtraCycle()
        {
            int impacts = 0;
            var session = new ActorInteractionSession(); session.Marker += _ => impacts++;
            session.Begin(_definition.Snapshot()); session.Advance(.6f); session.Continue(); session.Advance(.5f);
            Assert.That(impacts, Is.EqualTo(1)); Assert.That(session.Phase.Name, Is.EqualTo("Finish"));
        }

        [Test]
        public void CancelBeforeImpactAndLostTargetGrantNothing()
        {
            int commits = 0;
            var service = new HarvestService(_ => { commits++; return true; }, (_, _) => { }, (_, _) => { },
                _ => new ProtoHarvestInfo(ScatterInteraction.Chop, "Tree"), null);
            bool available = true;
            var action = new HarvestInteraction(service, new ScatterPick(7, 0, Vector3.zero, Quaternion.identity, 1), ToolTier.BasicAxe, () => available);
            var session = new ActorInteractionSession(); session.Marker += _ => action.Strike();
            session.Begin(_definition.Snapshot()); session.Advance(.4f); session.Cancel(); session.Advance(5f);
            available = false; session.Begin(_definition.Snapshot()); session.Advance(2f);
            Assert.That(commits, Is.Zero);
        }

        [Test]
        public void MarkerCanRestartSamePlanWithoutConsumingOldRemainder()
        {
            var session = new ActorInteractionSession(); var plan = _definition.Snapshot();
            session.Marker += _ => session.Begin(plan);
            session.Begin(plan); session.Advance(2f);
            Assert.That(session.Elapsed, Is.Zero); Assert.That(session.PhaseIndex, Is.Zero);
        }

        [Test]
        public void PartialDamageSurvivesActionRestartAndResetClearsIt()
        {
            var committed = new HashSet<ulong>();
            var service = new HarvestService(p => committed.Add(p.Id), (_, _) => { }, (_, _) => { },
                _ => new ProtoHarvestInfo(ScatterInteraction.Chop, "Tree", 3), null);
            var pick = new ScatterPick(7, 0, Vector3.zero, Quaternion.identity, 1);
            Assert.That(service.TryHarvest(pick, ToolTier.BasicAxe).Outcome, Is.EqualTo(HarvestOutcome.Hit));
            Assert.That(service.TryHarvest(pick, ToolTier.BasicAxe).Outcome, Is.EqualTo(HarvestOutcome.Hit));
            service.ResetProgress();
            Assert.That(service.TryHarvest(pick, ToolTier.BasicAxe).Outcome, Is.EqualTo(HarvestOutcome.Hit));
            Assert.That(service.TryHarvest(pick, ToolTier.BasicAxe).Outcome, Is.EqualTo(HarvestOutcome.Hit));
            Assert.That(service.TryHarvest(pick, ToolTier.BasicAxe).Outcome, Is.EqualTo(HarvestOutcome.Felled));
            Assert.That(service.TryHarvest(pick, ToolTier.BasicAxe).Outcome, Is.EqualTo(HarvestOutcome.AlreadyHarvested));
        }
    }
}
