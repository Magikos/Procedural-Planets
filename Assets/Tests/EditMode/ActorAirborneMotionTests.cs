using System;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorAirborneMotionTests
    {
        [Test]
        public void JumpFollowsVelocityAndRetainsIntentThroughLanding()
        {
            var motion = new ActorAirborneMotion();
            motion.Advance(.02f, false, true, Vector3.up * 5f, Vector3.up);
            Assert.AreEqual(ActorAirbornePhase.Ascent, motion.Phase);
            Assert.AreEqual(0f, motion.AscentProgress);
            motion.Advance(.02f, false, false, Vector3.up * 2.5f, Vector3.up);
            Assert.AreEqual(.5f, motion.AscentProgress, .00001f);
            motion.Advance(.02f, false, false, Vector3.zero, Vector3.up);
            Assert.AreEqual(ActorAirbornePhase.Apex, motion.Phase);
            motion.Advance(.02f, false, false, Vector3.down * 4f, Vector3.up);
            Assert.AreEqual(ActorAirbornePhase.Descent, motion.Phase);
            Assert.AreEqual(.8f, motion.DescentProgress, .00001f);
            motion.Advance(.02f, true, false, Vector3.zero, Vector3.up);
            Assert.AreEqual(ActorAirbornePhase.Landing, motion.Phase);
            Assert.AreEqual(4f, motion.LandingSpeed);
            Assert.IsTrue(motion.IntentionalJump);
            motion.Advance(.21f, true, false, Vector3.zero, Vector3.up);
            Assert.IsFalse(motion.Active);
            Assert.AreEqual(0f, motion.LandingSpeed);
        }

        [TestCase(.1f, ActorAirbornePhase.Grounded)]
        [TestCase(.3f, ActorAirbornePhase.Landing)]
        public void PassiveDropOnlyRecoversAboveMinimumDistance(float drop, ActorAirbornePhase expected)
        {
            var motion = new ActorAirborneMotion();
            motion.Advance(drop, false, false, Vector3.down, Vector3.up);
            Assert.AreEqual(ActorAirbornePhase.Falling, motion.Phase);
            motion.Advance(.02f, true, false, Vector3.zero, Vector3.up);
            Assert.AreEqual(expected, motion.Phase);
        }

        [Test]
        public void EquivalentGravityFramesProduceEquivalentMotion()
        {
            var normal = new ActorAirborneMotion();
            var sideways = new ActorAirborneMotion();
            foreach (float speed in new[] { 5f, 2f, 0f, -2f, -5f })
            {
                normal.Advance(.03f, false, true, Vector3.up * speed + Vector3.forward * 3f, Vector3.up);
                sideways.Advance(.03f, false, true, Vector3.right * speed + Vector3.forward * 3f, Vector3.right * 2f);
                Assert.AreEqual(normal.Phase, sideways.Phase);
                Assert.AreEqual(normal.DropDistance, sideways.DropDistance, .00001f);
                Assert.AreEqual(normal.AscentProgress, sideways.AscentProgress, .00001f);
                Assert.AreEqual(normal.DescentProgress, sideways.DescentProgress, .00001f);
            }
        }

        [Test]
        public void NewJumpAndSuspensionClearPriorSequence()
        {
            var motion = new ActorAirborneMotion();
            motion.Advance(.1f, false, true, Vector3.down * 5f, Vector3.up);
            motion.Advance(.02f, true, false, Vector3.zero, Vector3.up);
            motion.Advance(.02f, false, true, Vector3.up * 3f, Vector3.up);
            Assert.AreEqual(ActorAirbornePhase.Ascent, motion.Phase);
            Assert.AreEqual(0f, motion.LandingSpeed);
            Assert.AreEqual(.02f, motion.AirTime);
            motion.Advance(.02f, false, true, Vector3.up, Vector3.up, true);
            Assert.IsFalse(motion.Active);
            Assert.IsFalse(motion.IntentionalJump);
            Assert.AreEqual(0f, motion.AirTime);
        }

        [Test]
        public void CeilingStopAdvancesToDescentWithoutWaitingForClipTime()
        {
            var motion = new ActorAirborneMotion();
            motion.Advance(.02f, false, true, Vector3.up * 5f, Vector3.up);
            motion.Advance(.02f, false, false, Vector3.zero, Vector3.up);
            Assert.AreEqual(ActorAirbornePhase.Apex, motion.Phase);
            motion.Advance(.02f, false, false, Vector3.down, Vector3.up);
            Assert.AreEqual(ActorAirbornePhase.Descent, motion.Phase);
            Assert.AreEqual(1f, motion.AscentProgress);
            motion.Advance(.02f, true, false, Vector3.zero, Vector3.up);
            Assert.AreEqual(ActorAirbornePhase.Landing, motion.Phase);
        }

        [Test]
        public void ZeroDeltaDoesNotStartOrInterruptMotion()
        {
            var motion = new ActorAirborneMotion();
            motion.Advance(0f, false, true, Vector3.up, Vector3.up);
            Assert.IsFalse(motion.Active);
            motion.Advance(.02f, false, true, Vector3.up, Vector3.up);
            motion.Advance(0f, true, false, Vector3.zero, Vector3.up, true);
            Assert.AreEqual(ActorAirbornePhase.Ascent, motion.Phase);
            Assert.AreEqual(.02f, motion.AirTime);
        }

        [Test]
        public void InvalidInputsFailBeforeChangingState()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActorAirborneMotion(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActorAirborneMotion(.2f, -1f));
            var motion = new ActorAirborneMotion();
            Assert.Throws<ArgumentOutOfRangeException>(() => motion.Advance(-1f, false, true, Vector3.up, Vector3.up));
            Assert.Throws<ArgumentOutOfRangeException>(() => motion.Advance(.02f, false, true, Vector3.up, Vector3.zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => motion.Advance(.02f, false, true, Vector3.positiveInfinity, Vector3.up));
            Assert.IsFalse(motion.Active);
        }
    }
}
