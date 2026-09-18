using NUnit.Framework;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorBowTests
    {
        ActorBow Ready()
        {
            var bow = new ActorBow();
            Assert.IsTrue(bow.Equip(true));
            Assert.IsTrue(bow.CompletePhase(true, false, true));
            return bow;
        }

        [Test] public void PlaybackCompletionCannotInventContactOrArrow()
        {
            var bow = new ActorBow();
            Assert.IsFalse(bow.Equip(false));
            Assert.IsTrue(bow.Equip(true));
            Assert.IsFalse(bow.CompletePhase(false, false, true));
            Assert.IsTrue(bow.CompletePhase(true, false, true));
            Assert.IsFalse(bow.Retrieve(false));
            Assert.IsTrue(bow.Retrieve(true));
            Assert.IsFalse(bow.CompletePhase(true, false, true));
            Assert.IsFalse(bow.CompletePhase(true, true, true));
            Assert.IsTrue(bow.CompletePhase(true, true, false));
            Assert.AreEqual(ActorBow.Motion.Drawing, bow.State);
        }

        [Test] public void ShotRequiresHoldingAndObservedRelease()
        {
            var bow = Ready();
            Assert.IsFalse(bow.Release()); bow.Retrieve(true);
            Assert.IsFalse(bow.Release()); bow.CompletePhase(true, true, false);
            Assert.IsFalse(bow.Release()); bow.CompletePhase(true, true, false);
            Assert.IsTrue(bow.Release()); Assert.IsFalse(bow.Release());
            Assert.IsFalse(bow.CompletePhase(true, true, false));
            Assert.IsFalse(bow.CompletePhase(true, false, true));
            Assert.IsTrue(bow.CompletePhase(true, false, false));
            Assert.IsFalse(bow.Retrieve(false));
        }

        [Test] public void CancelBeforeArrowContactReversesWithoutAcquisition()
        {
            var bow = Ready(); bow.Retrieve(true); bow.Cancel(true, false);
            Assert.AreEqual(ActorBow.Motion.Retrieving, bow.State);
            Assert.AreEqual(-1, bow.Direction);
            Assert.IsFalse(bow.CompletePhase(true, true, false));
            Assert.IsTrue(bow.CompletePhase(true, false, true));
            Assert.AreEqual(ActorBow.Motion.Ready, bow.State);
        }

        [TestCase(false)] [TestCase(true)]
        public void CancelTensionLowersAndReturnsSameArrow(bool holding)
        {
            var bow = Ready(); bow.Retrieve(true); bow.CompletePhase(true, true, false);
            if (holding) bow.CompletePhase(true, true, false);
            bow.Cancel(true, true);
            Assert.AreEqual(ActorBow.Motion.Lowering, bow.State);
            Assert.IsFalse(bow.Release()); Assert.IsFalse(bow.Stow());
            Assert.IsTrue(bow.CompletePhase(true, true, false));
            Assert.AreEqual(ActorBow.Motion.ReturningArrow, bow.State);
            bow.Cancel(true, true);
            Assert.IsFalse(bow.CompletePhase(true, true, false));
            Assert.IsTrue(bow.CompletePhase(true, false, true));
            Assert.IsTrue(bow.Stow());
        }

        [Test] public void CancelRetrievedArrowRequiresReturnContact()
        {
            var bow = Ready(); bow.Retrieve(true); bow.Cancel(true, true);
            Assert.AreEqual(ActorBow.Motion.ReturningArrow, bow.State);
            Assert.IsFalse(bow.CompletePhase(true, true, false));
            Assert.IsTrue(bow.CompletePhase(true, false, true));
        }

        [Test] public void EquipAndStowCancellationRespectCurrentCustody()
        {
            var bow = new ActorBow(); bow.Equip(true); bow.Cancel(false, false);
            Assert.AreEqual(-1, bow.Direction);
            Assert.IsTrue(bow.CompletePhase(false, false, true));
            bow.Equip(true); bow.Cancel(true, false);
            Assert.AreEqual(1, bow.Direction);
            Assert.IsTrue(bow.CompletePhase(true, false, true));
            bow.Stow(); bow.Cancel(true, false);
            Assert.AreEqual(-1, bow.Direction);
            Assert.IsTrue(bow.CompletePhase(true, false, true));
            bow.Stow(); bow.Cancel(false, false);
            Assert.IsTrue(bow.CompletePhase(false, false, true));
            Assert.AreEqual(ActorBow.Motion.Stowed, bow.State);
        }

        [Test] public void BlockedReturnRecoversToHoldingWithoutLosingArrow()
        {
            var bow = Ready();
            Assert.IsFalse(bow.ReturnFailed());
            bow.Retrieve(true); bow.Cancel(true, true);
            Assert.IsFalse(bow.CompletePhase(true, true, true));
            Assert.AreEqual(ActorBow.Motion.ReturningArrow, bow.State);
            Assert.IsTrue(bow.ReturnFailed());
            Assert.AreEqual(ActorBow.Motion.Retrieving, bow.State);
            Assert.AreEqual(1, bow.Direction);
            Assert.IsFalse(bow.ReturnFailed());
            Assert.IsFalse(bow.CompletePhase(true, true, true));
            Assert.IsTrue(bow.CompletePhase(true, true, false));
            Assert.IsFalse(bow.CompletePhase(true, true, true));
            Assert.IsTrue(bow.CompletePhase(true, true, false));
            Assert.AreEqual(ActorBow.Motion.Holding, bow.State);
            Assert.IsTrue(bow.Release());
        }
    }
}
