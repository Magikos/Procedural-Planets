using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class HumanAccessoryFitTests
    {
        [Test]
        public void MountFollowsCombinedShapesWithoutDriftAndReturnsToNeutral()
        {
            var root = new GameObject("Accessory fit test");
            try
            {
                var body = root.AddComponent<HumanBodyReview>();
                var item = new GameObject("Pouch"); item.transform.SetParent(root.transform);
                var mount = item.AddComponent<HumanAccessoryFit>();
                mount.Body = body; mount.NeutralLocalPosition = new Vector3(.2f, .8f, 0);
                mount.HeavyOffset = new Vector3(.1f, 0, -.2f);
                mount.FeminineOffset = new Vector3(.04f, .02f, 0);
                body.Heavy = 100; body.Feminine = 50; body.SkeletonFit = 50;
                for (int i = 0; i < 100; i++) mount.Apply();
                Assert.That(Vector3.Distance(item.transform.localPosition, new Vector3(.26f, .805f, -.1f)), Is.LessThan(1e-6f));
                body.SkeletonFit = 0; mount.Apply();
                Assert.AreEqual(mount.NeutralLocalPosition, item.transform.localPosition);
                body.SkeletonFit = 100; body.Heavy = body.Feminine = 0; mount.Apply();
                Assert.AreEqual(mount.NeutralLocalPosition, item.transform.localPosition);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
