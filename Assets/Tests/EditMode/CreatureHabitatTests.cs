using System;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class CreatureHabitatTests
    {
        [TestCase(0)]
        [TestCase(2)]
        [TestCase(4)]
        public void CubeCellAreasSumToSphereArea(int level)
        {
            const double radius = 5000d;
            double sum = 0d;
            int side = 1 << level;
            for (int face = 0; face < 6; face++)
            for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
                sum += CreatureHabitatSampling.AreaSquareKm(CreatureKey.Slot(face, level, x, y, 0), radius);
            Assert.AreEqual(4d * Math.PI * radius * radius / 1e6d, sum, 1e-8);
        }

        [Test]
        public void AreaScalesWithRadiusSquaredAndDoesNotDependOnSpeciesSlot()
        {
            var first = CreatureKey.Slot(0, 4, 3, 7, 0);
            var second = CreatureKey.Slot(0, 4, 3, 7, 40);
            double area = CreatureHabitatSampling.AreaSquareKm(first, 1000d);
            Assert.AreEqual(area, CreatureHabitatSampling.AreaSquareKm(second, 1000d));
            Assert.AreEqual(area * 4d, CreatureHabitatSampling.AreaSquareKm(first, 2000d), 1e-12);
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatureHabitatSampling.AreaSquareKm(first, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatureHabitatSampling.AreaSquareKm(first, -1d));
            Assert.Throws<ArgumentException>(() => CreatureHabitatSampling.AreaSquareKm(default, 1000d));
        }

        [Test]
        public void OccupancyUsesDensityAndWaterWithoutExceedingCapacity()
        {
            var habitat = new CreatureHabitatDto(20f, 45f, 2f, .5f, true);
            Assert.AreEqual(.1d, habitat.Occupancy(.1d, 10, false), 1e-9);
            Assert.AreEqual(.4d, habitat.Occupancy(.1d, 10, true), 1e-9);
            Assert.AreEqual(1d, habitat.Occupancy(100d, 10, true));
            Assert.AreEqual(0d, habitat.Occupancy(100d, 0, true));
            Assert.AreEqual(0d, habitat.Occupancy(0d, 10, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => habitat.Occupancy(double.NaN, 10, true));
            Assert.IsFalse(CreatureHabitatSampling.NearFreshWater(null, Vector3.up));
        }

        [Test]
        public void BiomeWeightsAreSnapshotsAndUnspecifiedBiomesUseNormalDensity()
        {
            var source = new CreatureHabitat { DensityPerSquareKm = 20f, DryLandMultiplier = 1f,
                BiomeWeights = new[] { new CreatureHabitat.BiomeWeight { Biome = BiomeType.Desert, Weight = .1f } } };
            var snapshot = source.Snapshot();
            source.BiomeWeights[0].Weight = .9f;
            Assert.AreEqual(.02d, snapshot.Occupancy(.1d, 10, false, BiomeType.Desert), 1e-8);
            Assert.AreEqual(.2d, snapshot.Occupancy(.1d, 10, false, BiomeType.Forest), 1e-8);
            source.BiomeWeights = new[] { source.BiomeWeights[0], source.BiomeWeights[0] };
            Assert.Throws<ArgumentException>(() => source.Snapshot());
        }

        [Test]
        public void HabitatSnapshotRejectsInvalidValues()
        {
            var source = new CreatureHabitat { MaximumSlope = 90f };
            Assert.Throws<ArgumentException>(() => source.Snapshot());
            source.MaximumSlope = 30f;
            source.DryLandMultiplier = float.NaN;
            Assert.Throws<ArgumentException>(() => source.Snapshot());
        }

        [Test]
        public void SlopeMatchesGroundAndRejectsMissingSupport()
        {
            Assert.AreEqual(0f, CreatureHabitatSampling.Slope(new Incline(0f), Vector3.up, 1000f), .01f);
            float slope = CreatureHabitatSampling.Slope(new Incline(1f), Vector3.up, 1000f);
            Assert.AreEqual(45f, slope, .02f);
            Assert.Greater(slope, 30f, "This incline exceeds a 30 degree habitat limit.");
            Assert.AreEqual(90f, CreatureHabitatSampling.Slope(null, Vector3.up, 1000f));
            Assert.AreEqual(90f, CreatureHabitatSampling.Slope(new Incline(float.NaN), Vector3.up, 1000f));
        }

        sealed class Incline : IPlanetSurfaceSampler
        {
            readonly float _gradient;
            public Incline(float gradient) => _gradient = gradient;
            public bool TryGetSurfaceRadius(Vector3 direction, out float radius)
            { radius = 1000f + direction.x * 1000f * _gradient; return true; }
        }
    }
}
