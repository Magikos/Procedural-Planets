using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorTraversalMotionTests
    {
        [Test]
        public void ElevatedLandingRequiresExplicitOptInAndNonzeroHeight()
        {
            var frames = new[] { new ActorTraversalMotion.Frame { Radius = .15f },
                new ActorTraversalMotion.Frame { Root = new Vector3(0f, .75f, 1f), PoseOffset = new Vector3(0f, .75f, 1f),
                    Radius = .15f, PlanarFitWeight = 1f } };
            Assert.Throws<System.ArgumentException>(() => new ActorTraversalMotion(1f, Vector3.up, .3f, .7f, frames));
            var motion = new ActorTraversalMotion(1f, Vector3.up, .3f, .7f, frames, elevatedLanding: true);
            Assert.That(motion.Sample(.5f).Root.y, Is.EqualTo(.375f).Within(.0001f));
            Assert.That(motion.Sample(.5f).PoseOffset.y, Is.EqualTo(.375f).Within(.0001f));
            frames[1].Root.y = -.75f;
            Assert.DoesNotThrow(() => new ActorTraversalMotion(1f, Vector3.up, .3f, .7f, frames, elevatedLanding: true));
            frames[1].Root.y = 0f;
            Assert.Throws<System.ArgumentException>(() => new ActorTraversalMotion(1f, Vector3.up, .3f, .7f, frames, elevatedLanding: true));
        }
    }
}
