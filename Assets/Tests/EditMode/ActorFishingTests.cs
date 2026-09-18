using NUnit.Framework;
using UnityEditor;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorFishingTests
    {
        ActorFishing _fishing;
        [SetUp] public void SetUp()
        {
            _fishing = new ActorFishing();
            _fishing.Begin(AssetDatabase.LoadAssetAtPath<ActorInteractionDefinition>(
                "Assets/Art/Interactions/Definitions/Fishing.asset").Snapshot());
        }
        void ReachBite()
        {
            for (int i = 0; i < 2000 && _fishing.Session.Phase.Name != "Bite"; i++) _fishing.Tick(.02f);
            Assert.AreEqual("Bite", _fishing.Session.Phase.Name);
        }
        [Test] public void ValidHookAwardsExactlyOneCatchAcrossLargeFrames()
        {
            ReachBite(); Assert.IsTrue(_fishing.Hook()); Assert.IsFalse(_fishing.Hook());
            int rewards = 0; _fishing.Caught += () => rewards++;
            _fishing.Tick(30); _fishing.Tick(30);
            Assert.AreEqual(1, rewards); Assert.AreEqual(1, _fishing.Catches); Assert.IsFalse(_fishing.Active);
        }
        [Test] public void EarlyHookDoesNotSkipCasting()
        {
            Assert.IsFalse(_fishing.Hook()); Assert.AreEqual("Cast", _fishing.Session.Phase.Name);
        }
        [Test] public void MissedBiteRecoversWithoutReward()
        {
            _fishing.Tick(30); Assert.IsFalse(_fishing.Active); Assert.AreEqual(0, _fishing.Catches);
        }
        [TestCase(false)] [TestCase(true)] public void CancellationOrTargetLossBeforeCatchNeverRewards(bool lost)
        {
            ReachBite(); _fishing.Hook(); _fishing.Tick(1);
            if (lost) _fishing.Tick(.02f, false); else _fishing.Cancel();
            Assert.AreEqual("Recover", _fishing.Session.Phase.Name);
            _fishing.Tick(30); Assert.AreEqual(0, _fishing.Catches); Assert.IsFalse(_fishing.Active);
        }
        [Test] public void SecondCastCanAwardOneMoreCatch()
        {
            var plan = _fishing.Session.Plan;
            ReachBite(); _fishing.Hook(); _fishing.Tick(30);
            _fishing.Begin(plan); ReachBite(); _fishing.Hook(); _fishing.Tick(30);
            Assert.AreEqual(2, _fishing.Catches);
        }
        [Test] public void RepeatedCancelDoesNotRestartRecovery()
        {
            _fishing.Cancel(); _fishing.Tick(1);
            float elapsed = _fishing.Session.Elapsed;
            _fishing.Cancel();
            Assert.AreEqual(elapsed, _fishing.Session.Elapsed);
            _fishing.Tick(30); Assert.IsFalse(_fishing.Active); Assert.AreEqual(0, _fishing.Catches);
        }
        [Test] public void FightCannotFinishUntilLandingIsAllowed()
        {
            ReachBite(); _fishing.Hook(); _fishing.Tick(20, readyToLand: false);
            Assert.AreEqual("Pull", _fishing.Session.Phase.Name); Assert.AreEqual(0, _fishing.Catches);
            _fishing.Tick(20, readyToLand: true);
            Assert.IsFalse(_fishing.Active); Assert.AreEqual(1, _fishing.Catches);
        }
    }
}
