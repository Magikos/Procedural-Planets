using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class FishMovementTests
    {
        sealed class Water : IWaterQueryService
        {
            public bool Shore;
            public bool TryGetWaterSurface(Vector3 p, out WaterSample sample)
            {
                sample = new WaterSample(new Vector3(p.x, 0f, p.z), Vector3.up, -p.y, 5f,
                    (ushort)(p.x > 4f ? 2 : 1), false);
                return !Shore || p.x < 0.9f || p.x > 1.1f;
            }
            public bool IsUnderwater(Vector3 p) => TryGetWaterSurface(p, out var s) && s.IsSubmerged;
        }

        [Test]
        public void SwimmingCannotLeaveTheSurfaceBedOrWaterBody()
        {
            var water = new Water();
            Vector3 start = new(0f, -2f, 0f);
            Assert.IsTrue(FishMovement.TryMove(water, start, new Vector3(1f,-2f,0f), 1, 0.2f, out _));
            foreach (Vector3 destination in new[]{Vector3.up, Vector3.down * 6f, new Vector3(5f,-2f,0f)})
            {
                Assert.IsFalse(FishMovement.TryMove(water, start, destination, 1, 0.2f, out Vector3 result));
                Assert.AreEqual(start, result);
            }
        }

        [Test]
        public void ValidEndpointsCannotCrossAShoreline()
        {
            var water = new Water { Shore = true };
            Assert.IsFalse(FishMovement.TryMove(water, new Vector3(0f,-2f,0f), new Vector3(2f,-2f,0f),
                1, 0.2f, out _));
        }

        [TestCase(1)]
        [TestCase(12)]
        public void SolitaryFishAndSchoolsStayInTheirHabitat(int count)
        {
            var water = new Water { Shore = true };
            var positions = new Vector3[count];
            for (int i = 0; i < count; i++) positions[i] = new Vector3(-i * 0.3f, -2f, 0f);
            var school = new FishSchool(water, positions, Vector3.right);
            for (int frame = 0; frame < 1800; frame++)
            {
                school.Tick(1f / 30f, null, 0);
                foreach (Vector3 point in school.Positions)
                    Assert.IsTrue(FishMovement.IsHabitat(water, point, 1, 0.2f));
            }
        }
    }
}
