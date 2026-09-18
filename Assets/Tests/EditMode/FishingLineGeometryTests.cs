using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class FishingLineGeometryTests
    {
        [Test] public void FreeLineHasNoTangentBreakAtFormerContactPoint()
        {
            Vector3 Sample(float t) => FishingLineGeometry.Point(Vector3.up * 3, Vector3.forward * 4,
                Vector3.left, Vector3.up, .3f, 0, t);
            const float t = 2f / 3, h = .0001f;
            var incoming = (Sample(t) - Sample(t - h)).normalized;
            var outgoing = (Sample(t + h) - Sample(t)).normalized;
            Assert.Less(Vector3.Angle(incoming, outgoing), .1f);
        }
        [Test] public void AcquiredLinePassesThroughHandAndBothEndpoints()
        {
            var tip = Vector3.up * 3; var end = Vector3.forward; var hand = new Vector3(0, 1, 1);
            Assert.That(FishingLineGeometry.Point(tip, end, hand, Vector3.up, .1f, 1, 0), Is.EqualTo(tip));
            Assert.Less(Vector3.Distance(hand, FishingLineGeometry.Point(tip, end, hand, Vector3.up, .1f, 1, 2f / 3)), .00001f);
            Assert.That(FishingLineGeometry.Point(tip, end, hand, Vector3.up, .1f, 1, 1), Is.EqualTo(end));
        }
        [Test] public void LiftingTipImmediatelyLiftsFishWithoutStretching()
        {
            var fish = FishingLineGeometry.LimitReach(Vector3.up * 3, Vector3.zero, 2);
            Assert.That(fish.y, Is.EqualTo(1).Within(.00001f));
            Assert.That(Vector3.Distance(Vector3.up * 3, fish), Is.EqualTo(2).Within(.00001f));
        }
    }
}
