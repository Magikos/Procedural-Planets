using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorReachMotionTests
    {
        [Test]
        public void ContactOccursOnceAndMovingTargetReleasesWithoutRootAuthority()
        {
            var motion = new ActorReachMotion();
            Assert.IsTrue(motion.Begin(Vector3.one));
            Assert.IsFalse(motion.Begin(Vector3.zero));
            motion.Advance(.4f, Vector3.one);
            Assert.IsFalse(motion.ObserveContact(Vector3.zero, .035f));
            Assert.IsTrue(motion.ObserveContact(Vector3.one, .035f));
            Assert.IsFalse(motion.ObserveContact(Vector3.one, .035f));
            motion.Advance(.2f, Vector3.right);
            Assert.AreEqual(Vector3.right, motion.Target.Value.Position);
            Assert.Greater(motion.ContactProgress, 0f);
            for (int i = 0; i < 100; i++) motion.Advance(.02f, Vector3.right);
            Assert.IsFalse(motion.Active); Assert.AreEqual(0f, motion.Weight);
            Assert.IsNull(motion.Target);
            Assert.IsTrue(motion.Begin(Vector3.zero)); Assert.IsFalse(motion.Contacted);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LostTargetAndCancellationReleaseWithoutContact(bool lostTarget)
        {
            var motion = new ActorReachMotion(); motion.Begin(Vector3.one);
            motion.Advance(.2f, Vector3.one);
            float previous = motion.Weight;
            if (!lostTarget) motion.Cancel();
            motion.Advance(.02f, lostTarget ? null : Vector3.one);
            Assert.That(motion.Weight, Is.InRange(0.001f, previous));
            Assert.IsFalse(motion.ObserveContact(Vector3.one, .035f));
            motion.Advance(1f, null);
            Assert.IsFalse(motion.Active); Assert.IsFalse(motion.Contacted);
        }

        [Test]
        public void UnreachableTargetTimesOutAndInvalidDeltaDoesNotMutate()
        {
            var motion = new ActorReachMotion(); motion.Begin(Vector3.one * 100f);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => motion.Advance(float.NaN, Vector3.one));
            Assert.AreEqual(0f, motion.Weight);
            for (int i = 0; i < 150; i++) motion.Advance(.02f, Vector3.one * 100f);
            Assert.IsFalse(motion.Active); Assert.IsFalse(motion.Contacted);
        }
    }
}
