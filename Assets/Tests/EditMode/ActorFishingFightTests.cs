using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorFishingFightTests
    {
        static readonly Vector3 Tip = new(0, 1.5f, 0);
        static ActorFishingFight NewFight()
        {
            var fight = new ActorFishingFight();
            fight.Begin(new Vector3(0, 0, 4), Tip, Vector3.up, Vector3.forward);
            return fight;
        }
        static void Advance(ActorFishingFight fight, float seconds, float reel, float give, float lateral = 1)
        {
            for (int i = 0; i < Mathf.RoundToInt(seconds * 60); i++) fight.Tick(1f / 60, Tip, reel, give, lateral);
        }
        [Test] public void ForwardPullStaysInItsPlaneAndBuildsTension()
        {
            var f = NewFight(); Advance(f, 4, 0, 0, 0);
            Assert.That(f.Position.x, Is.EqualTo(0).Within(.001));
            Assert.That(f.Position.y, Is.EqualTo(0).Within(.001));
            Assert.Greater(f.Tension, .05f); Assert.IsFalse(f.ReadyToLand);
        }
        [Test] public void SidewaysFightMovesAcrossTheLine()
        {
            var f = NewFight(); Advance(f, 2, 0, 0);
            Assert.Greater(Mathf.Abs(f.Position.x), .2f);
        }
        [Test] public void GivingLineRelievesTensionAndPreventsLanding()
        {
            var f = NewFight(); Advance(f, 3, 0, 0); float taut = f.Tension;
            Advance(f, 4, 1, 1);
            Assert.Less(f.Tension, taut); Assert.IsFalse(f.ReadyToLand); Assert.Greater(f.LineLength, 7);
        }
        [Test] public void ReelingBringsFishIntoLandingRange()
        {
            var f = NewFight(); Advance(f, 7, 1, 0);
            Assert.IsTrue(f.ReadyToLand); Assert.Less(f.Position.z, 3);
        }
        [Test] public void ThirtyAndSixtyFpsProduceMatchingMotion()
        {
            var a = NewFight(); var b = NewFight(); Advance(a, 4, .5f, 0);
            for (int i = 0; i < 120; i++) b.Tick(1f / 30, Tip, .5f, 0);
            Assert.Less(Vector3.Distance(a.Position, b.Position), .002f);
            Assert.That(a.Tension, Is.EqualTo(b.Tension).Within(.002));
        }
        [Test] public void InvalidInputCannotPoisonState()
        {
            var f = NewFight(); var before = f.Position;
            Assert.Throws<System.ArgumentOutOfRangeException>(() => f.Tick(float.NaN, Tip, 0, 0));
            Assert.Throws<System.ArgumentException>(() => f.Tick(.02f, new Vector3(float.NaN, 0, 0), 0, 0));
            Assert.AreEqual(before, f.Position);
        }
    }
}
