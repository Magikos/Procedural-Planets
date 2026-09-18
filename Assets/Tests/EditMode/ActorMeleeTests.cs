using System;
using NUnit.Framework;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorMeleeTests
    {
        ActorMelee Create() => new(new ActorAttackDefinition(20, 1.65f, 55, .3f, .15f, .35f, .1, 0, 0), .5f);

        [Test] public void AttackContactsOnceAndCannotRestartDuringRecovery()
        {
            var actor = Create(); int hits = 0; actor.Strike += () => hits++;
            Assert.IsTrue(actor.Attack()); actor.Tick(.29f); Assert.AreEqual(0, hits);
            actor.Tick(.02f); Assert.AreEqual(1, hits); Assert.IsFalse(actor.Attack());
            actor.Tick(.46f); Assert.IsFalse(actor.Attack()); actor.Tick(.05f);
            Assert.AreEqual(ActorMelee.Motion.Ready, actor.State); Assert.AreEqual(1, hits);
            Assert.IsTrue(actor.Attack()); actor.Tick(10); Assert.AreEqual(2, hits);
        }

        [TestCase(false)] [TestCase(true)] public void InterruptedWindupCannotDamage(bool hit)
        {
            var actor = Create(); int hits = 0; actor.Strike += () => hits++;
            actor.Attack(); actor.Tick(.1f);
            if (hit) actor.Receive(new ActorHealth(100), 10, 0); else actor.Cancel();
            actor.Tick(1); Assert.AreEqual(0, hits); Assert.AreEqual(ActorMelee.Motion.Ready, actor.State);
        }

        [Test] public void GuardBlocksFrontButNotRearAndReturnsAfterReaction()
        {
            var actor = Create(); var health = new ActorHealth(100); actor.Guard(true);
            Assert.IsTrue(actor.Receive(health, 10, 60)); Assert.AreEqual(100, health.Current);
            actor.Tick(.6f); Assert.AreEqual(ActorMelee.Motion.Block, actor.State);
            Assert.IsFalse(actor.Receive(health, 10, 180)); Assert.AreEqual(90, health.Current);
            actor.Guard(false); actor.Tick(.6f); Assert.AreEqual(ActorMelee.Motion.Ready, actor.State);
        }

        [Test] public void GuardRequestedDuringAttackStartsAfterRecovery()
        {
            var actor = Create(); actor.Attack(); actor.Guard(true);
            Assert.IsFalse(actor.Guarding); actor.Tick(1); Assert.IsTrue(actor.Guarding);
        }

        [Test] public void InvalidDamageDoesNotChangeStateOrHealth()
        {
            var actor = Create(); var health = new ActorHealth(100);
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.Receive(health, double.NaN, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.Tick(-1));
            Assert.AreEqual(100, health.Current); Assert.AreEqual(ActorMelee.Motion.Ready, actor.State);
        }
    }
}
