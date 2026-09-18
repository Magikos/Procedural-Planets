using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class InteractionPoseBlendTests
    {
        [Test]
        public void FixedLedgeRemainsPlantedDuringFastAuthoredClimb()
        {
            var blend = new InteractionPoseBlend();
            var ledge = new InteractionPoseTarget(new Vector3(0f, 1.4f, .4f));
            for (int i = 0; i < 30; i++) blend.Tick(ledge, 1f / 60f, Vector3.zero, Quaternion.identity);
            for (int i = 0; i < 60; i++)
            {
                var authored = Vector3.up * (i * .2f);
                var current = blend.Tick(ledge, 1f / 60f, authored, Quaternion.identity).Value;
                Assert.That(Vector3.Distance(current.Position, ledge.Position), Is.LessThan(.0001f));
                Assert.That(current.Weight, Is.EqualTo(1f));
            }
        }

        [Test]
        public void FastAuthoredTravelDoesNotBecomeCorrectionLag()
        {
            var blend = new InteractionPoseBlend();
            Vector3 correction = new Vector3(.02f, .01f, 0f);
            for (int i = 0; i < 60; i++)
            {
                // The authored hand moves at 12 m/s. Only its 2 cm correction needs blending.
                var authored = Vector3.up * (i * .2f);
                var requested = new InteractionPoseTarget(authored + correction, useContact: true, followAuthoredMotion: true);
                var current = blend.Tick(requested, 1f / 60f, authored, Quaternion.identity).Value;
                Assert.That(Vector3.Distance(current.Position, requested.Position), Is.LessThan(.0001f));
            }
        }

        [Test]
        public void ReleaseCarriesCorrectionWithAuthoredMotionAndRootTurn()
        {
            var blend = new InteractionPoseBlend();
            var correction = Vector3.right * .08f;
            for (int i = 0; i < 30; i++)
                blend.Tick(new InteractionPoseTarget(correction, useContact: true), 1f / 60f, Vector3.zero, Quaternion.identity);
            var authored = new Vector3(2f, 1f, 3f);
            var rotation = Quaternion.Euler(0f, 90f, 0f);
            var outgoing = blend.Tick(null, 1f / 60f, authored, rotation).Value;
            Assert.That(Vector3.Distance(outgoing.Position, authored + rotation * correction), Is.LessThan(.0001f));
            Assert.That(outgoing.Weight, Is.EqualTo(.95f).Within(.0001f));
            var switched = blend.Tick(new InteractionPoseTarget(authored + Vector3.up, useContact: true),
                1f / 60f, authored, rotation).Value;
            Assert.That(Vector3.Distance(switched.Position, outgoing.Position), Is.EqualTo(.05f).Within(.0001f));
            Assert.That(switched.Weight, Is.GreaterThanOrEqualTo(outgoing.Weight));
        }

        [Test]
        public void TargetLossRetainsPoseUntilReleaseCompletes()
        {
            var blend = new InteractionPoseBlend();
            var target = new InteractionPoseTarget(Vector3.one, Quaternion.identity);
            InteractionPoseTarget? current = null;
            for (int i = 0; i < 30; i++) current = blend.Tick(target, 1f / 60f);
            Assert.That(current.Value.Weight, Is.EqualTo(1f));
            current = blend.Tick(null, 1f / 60f);
            Assert.That(current.Value.Weight, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(current.Value.Position, Is.EqualTo(target.Position));
            for (int i = 0; i < 30; i++) current = blend.Tick(null, 1f / 60f);
            Assert.That(current.HasValue, Is.False);
        }

        [Test]
        public void ElbowGuideChangesBlendAndRetainTheOutgoingGuideOnRelease()
        {
            var blend = new InteractionPoseBlend();
            var first = new InteractionPoseTarget(Vector3.one, Quaternion.identity, useContact: true, bendDirection: Vector3.right);
            for (int i = 0; i < 30; i++) blend.Tick(first, 1f / 60f);
            var opposite = new InteractionPoseTarget(Vector3.one, Quaternion.identity, useContact: true, bendDirection: Vector3.left);
            var next = blend.Tick(opposite, 1f / 60f).Value;
            Assert.That(Vector3.Angle(Vector3.right, next.BendDirection.Value), Is.LessThan(4f));
            var released = blend.Tick(null, 1f / 60f).Value;
            Assert.AreEqual(next.BendDirection, released.BendDirection);
            Assert.That(released.Weight, Is.LessThan(next.Weight));
            Assert.Throws<System.ArgumentException>(() => new InteractionPoseTarget(Vector3.zero, bendDirection: Vector3.zero));
        }

        [Test]
        public void InterruptedReleaseAndRetargetPreserveCurrentPose()
        {
            var blend = new InteractionPoseBlend();
            var first = new InteractionPoseTarget(Vector3.zero, Quaternion.identity);
            for (int i = 0; i < 30; i++) blend.Tick(first, 1f / 60f);
            var outgoing = blend.Tick(null, 1f / 60f).Value;
            var next = new InteractionPoseTarget(Vector3.one, Quaternion.Euler(0, 180, 0));
            var incoming = blend.Tick(next, 1f / 60f).Value;
            Assert.That(Vector3.Distance(incoming.Position, outgoing.Position), Is.LessThan(.1f));
            Assert.That(Quaternion.Angle(incoming.Rotation.Value, outgoing.Rotation.Value), Is.LessThan(10f));
            Assert.That(incoming.Weight, Is.GreaterThanOrEqualTo(outgoing.Weight));
            for (int i = 0; i < 120; i++) incoming = blend.Tick(next, 1f / 60f).Value;
            Assert.That(Vector3.Distance(incoming.Position, next.Position), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(incoming.Rotation.Value, next.Rotation.Value), Is.LessThan(.01f));
        }
    }
}
