using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class FishMovementTests
    {
        sealed class Water : IWaterQueryService
        {
            public bool Shore;
            public int Queries;
            public bool TryGetWaterSurface(Vector3 p, out WaterSample sample)
            {
                Queries++;
                sample = new WaterSample(new Vector3(p.x, 0f, p.z), Vector3.up, -p.y, 5f,
                    (ushort)(p.x > 4f ? 2 : 1), false);
                return !Shore || p.x < 0.9f || p.x > 1.1f;
            }
            public bool IsUnderwater(Vector3 p) => TryGetWaterSurface(p, out var s) && s.IsSubmerged;
        }

        [Test]
        public void SchoolChecksCurrentAndDestinationHabitatOncePerStep()
        {
            var water = new Water();
            var school = new FishSchool(water, new[] { new Vector3(0f, -2f, 0f) }, Vector3.forward);
            water.Queries = 0;
            school.Tick(.01f, null, 0);
            Assert.That(water.Queries, Is.EqualTo(10));
            water.Queries = 0;
            school.Tick(.01f, null, 0);
            Assert.That(water.Queries, Is.EqualTo(10), "Each step must refresh the habitat sample.");
        }

        [Test]
        public void FishFearOtherClassesRememberThreatsAndIgnoreTheirOwnClass()
        {
            var water = new Water();
            var threats = new ThreatRegistry();
            var id = new EntityId(EntityId.HostOwner, 700);
            var source = new EntityId(EntityId.HostOwner, 701);
            Vector3 start = new(0f, -2f, 0f);
            var school = new FishSchool(water, new[] { start }, Vector3.forward, FishSpecies.Freshwater, id);
            threats.Report(source, start - Vector3.forward, CreatureFaction.Wildlife, "SmallFish");
            school.Tick(.1f, threats, 100);
            Assert.AreEqual(0, school.FleeingCount);
            threats.Report(source, start - Vector3.forward, CreatureFaction.Wildlife);
            school.Tick(.1f, threats, 100);
            Assert.AreEqual(1, school.FleeingCount, "Passive land animals must also startle fish.");
            threats.Withdraw(source);
            school.Tick(1f, threats, 101);
            Assert.AreEqual(1, school.FleeingCount, "Losing sight must not immediately end escape.");
            Assert.Greater(school.Positions[0].z, start.z + 2f);
            school.Tick(3f, threats, 104);
            Assert.AreEqual(0, school.FleeingCount);
            Assert.Greater(Vector3.Dot(school.Headings[0], Vector3.forward), 0f);
        }

        [TestCase(CreatureFaction.Player)]
        [TestCase(CreatureFaction.Predator)]
        [TestCase(CreatureFaction.Wildlife)]
        public void OverheadThreatsCauseLateralEscapeWithoutLeavingWater(CreatureFaction faction)
        {
            var water = new Water();
            var threats = new ThreatRegistry();
            Vector3 start = new(0f, -4.5f, 0f);
            var school = new FishSchool(water, new[] { start }, Vector3.forward, FishSpecies.Freshwater);
            threats.Report(new EntityId(EntityId.HostOwner, 702), Vector3.zero, faction);
            for (int i = 0; i < 60; i++)
            {
                school.Tick(1f / 30f, threats, 100);
                Assert.IsTrue(FishMovement.IsHabitat(water, school.Positions[0], 1, FishSpecies.Freshwater.Clearance));
            }
            Assert.Greater(school.Positions[0].z, start.z + 2f);
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
