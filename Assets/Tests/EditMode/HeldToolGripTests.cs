using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class HeldToolGripTests
    {
        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(179f)]
        [TestCase(270f)]
        public void PrimaryAnchorMatchesPalmAcrossSwingAndScale(float angle)
        {
            var tool = new GameObject("Tool");
            try
            {
                var grip = tool.AddComponent<HeldToolGrip>();
                grip.Primary = new GameObject("Primary grip").transform;
                grip.Primary.SetParent(tool.transform, false);
                grip.Primary.localPosition = new Vector3(.1f, .2f, -.3f);
                grip.Primary.localRotation = Quaternion.Euler(15, 80, 110);
                tool.transform.localScale = Vector3.one * 1.4f;
                tool.transform.SetPositionAndRotation(Vector3.one * 5, Quaternion.Euler(20, 45, 70));
                var palm = new Vector3(2, 1, 3);
                var rotation = Quaternion.Euler(angle, angle * .4f, angle * .8f);
                Assert.That(grip.TryFit(palm, rotation, out var fitted), Is.True);
                tool.transform.SetPositionAndRotation(fitted.position, fitted.rotation);
                Assert.That(Vector3.Distance(grip.Primary.position, palm), Is.LessThan(.00001f));
                Assert.That(Quaternion.Angle(grip.Primary.rotation, rotation), Is.LessThan(.05f));
            }
            finally { Object.DestroyImmediate(tool); }
        }

        [Test]
        public void MissingOrExternalAnchorRejectsAttachment()
        {
            var tool = new GameObject("Tool"); var other = new GameObject("Other");
            try
            {
                var grip = tool.AddComponent<HeldToolGrip>();
                Assert.That(grip.TryFit(Vector3.zero, Quaternion.identity, out _), Is.False);
                grip.Primary = other.transform;
                Assert.That(grip.TryFit(Vector3.zero, Quaternion.identity, out _), Is.False);
            }
            finally { Object.DestroyImmediate(tool); Object.DestroyImmediate(other); }
        }
    }
}
