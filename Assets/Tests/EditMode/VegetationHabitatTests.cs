using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEditor;

namespace ProceduralPlanets.Tests
{
    public sealed class VegetationHabitatTests
    {
        [Test]
        public void PlainsRemainOpenAndSlopesDoNotIncreaseClearings()
        {
            float forest = 0f, plains = 0f;
            int open = 0, wooded = 0;
            for (int i = 0; i < 4096; i++)
            {
                float3 dir = math.normalize(new float3(math.sin(i * 2.39996f), (i / 4095f - 0.5f) * 2f, math.cos(i * 2.39996f)));
                float f = VegetationHabitat.Cover(dir, 5000f, 12345, 1f, 1f);
                float p = VegetationHabitat.Cover(dir, 5000f, 12345, 1f, 0.1f);
                forest += f; plains += p;
                if (f < 0.05f) open++;
                if (f > 0.85f) wooded++;
                Assert.That(VegetationHabitat.Cover(dir, 5000f, 12345, 0.85f, 1f), Is.GreaterThanOrEqualTo(f));
            }
            Assert.That(forest, Is.GreaterThan(plains * 10f));
            Assert.That(open, Is.GreaterThan(100));
            Assert.That(wooded, Is.GreaterThan(100));
        }

        [Test]
        public void TreeAgeAndGrassRespondToTheSameCover()
        {
            Assert.That(VegetationHabitat.AgeKeep(0.25f, 0.1f), Is.GreaterThan(VegetationHabitat.AgeKeep(1f, 0.1f)));
            Assert.That(VegetationHabitat.AgeKeep(1f, 0.95f), Is.GreaterThan(VegetationHabitat.AgeKeep(0.25f, 0.95f)));
            Assert.That(VegetationHabitat.AgeKeep(0.25f, 1f), Is.GreaterThan(0f));
            Assert.That(VegetationHabitat.GrassKeep(1f), Is.LessThan(VegetationHabitat.GrassKeep(0f)));
        }

        [Test]
        public void StrongOpenPreferenceRetainsIndependentOverlappingColonies()
        {
            int overlap = 0, different = 0;
            for (int i = 0; i < 1024; i++)
            {
                float3 dir = math.normalize(new float3(i * 0.013f - 6f, 1f, math.sin(i)));
                float a = ScatterClumping.Keep(dir, 5000, 1, 18, 17, 6, 1, -1, 42);
                float b = ScatterClumping.Keep(dir, 5000, 1, 18, 29, 6, 1, -1, 42);
                if (a > 0.4f && b > 0.4f) overlap++;
                if (math.abs(a - b) > 0.2f) different++;
                Assert.That(ScatterClumping.Keep(dir, 5000, 0, 18, 17, 6, 1), Is.EqualTo(1));
            }
            Assert.That(overlap, Is.GreaterThan(20));
            Assert.That(different, Is.GreaterThan(100));
        }

        [TestCase(WaterBodyKind.Lake, false, -1f, true)]
        [TestCase(WaterBodyKind.Lake, false, 0f, false)]
        [TestCase(WaterBodyKind.Lake, true, -1f, false)]
        [TestCase(WaterBodyKind.Ocean, false, -1f, false)]
        [TestCase(WaterBodyKind.None, false, -1f, false)]
        public void LiliesRequireSubmergedLake(WaterBodyKind kind, bool river, float bed, bool expected)
        {
            Assert.That(ScatterWaterPlacement.Passes(ScatterWaterHabitat.LakeOnly, kind, river, 0, 0, true, bed), Is.EqualTo(expected));
        }

        [Test]
        public void ReedsUseFlowLimitAndRocksRetainDryBankEligibility()
        {
            Assert.That(ScatterWaterPlacement.Passes(ScatterWaterHabitat.Freshwater, WaterBodyKind.None, true, 0.4f, 0.5f, false, -0.3f), Is.True);
            Assert.That(ScatterWaterPlacement.Passes(ScatterWaterHabitat.Freshwater, WaterBodyKind.Lake, true, 0.6f, 0.5f, false, -0.3f), Is.False);
            Assert.That(ScatterWaterPlacement.Passes(ScatterWaterHabitat.Unrestricted, WaterBodyKind.None, false, 0, 0, false, 4f), Is.True);
        }

        [Test]
        public void AuthoredWaterPlantsPreserveBedDepthBandsAfterImport()
        {
            foreach (string name in new[] { "Lake Lily", "Lake Reeds", "Lake Cattails", "IceBog Reeds Prototype", "Swamp Reeds Prototype", "LMHPOLY Beach Reed" })
            {
                var source = Resources.Load<ScatterPrototype>("Settings/Scatter/" + name);
                Assert.That(source, Is.Not.Null, name);
                var p = ScatterPrototypeDto.From(source);
                Assert.That(p.HasMinAltitude && p.HasMaxAltitude, Is.True, name);
                Assert.That(p.MinAltitudeMeters, Is.LessThan(0f), name);
                Assert.That(p.MinAltitudeMeters, Is.LessThan(p.MaxAltitudeMeters), name);
                if (name == "Lake Lily")
                {
                    Assert.That(p.WaterHabitat, Is.EqualTo(ScatterWaterHabitat.LakeOnly));
                    Assert.That(p.MaxAltitudeMeters, Is.LessThan(0f));
                    Assert.That(p.OnWater, Is.True);
                    Assert.That(p.SpacingMeters, Is.LessThan(p.PatchScaleMeters / 4f));
                }
            }
        }

        [Test]
        public void GpuHabitatMatchesCpuForNegativeCoordinatesAndSeveralSeeds()
        {
            Assert.That(SystemInfo.supportsComputeShaders, Is.True);
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Tests/EditMode/VegetationHabitatProbe.compute");
            Assert.That(shader, Is.Not.Null);
            var input = new Vector4[128];
            var output = new Vector4[128];
            for (int i = 0; i < input.Length; i++) input[i] = new Vector4(i * 0.137f - 8f, math.sin(i) * 3f, math.cos(i * 0.31f) * 5f, (i % 5) / 4f);
            using var inputs = new ComputeBuffer(input.Length, 16);
            using var outputs = new ComputeBuffer(input.Length, 16);
            inputs.SetData(input);
            int kernel = shader.FindKernel("Probe");
            shader.SetBuffer(kernel, "Inputs", inputs); shader.SetBuffer(kernel, "Outputs", outputs);
            shader.SetFloat("Radius", 5000f);
            foreach (int seed in new[] { 0, 12345, -177 })
            {
                shader.SetInt("Seed", seed); shader.Dispatch(kernel, 2, 1, 1); outputs.GetData(output);
                for (int i = 0; i < input.Length; i++)
                {
                    float3 p = new float3(input[i].x, input[i].y, input[i].z);
                    float cover = VegetationHabitat.Cover(math.normalize(p), 5000, (uint)seed, 0.97f, input[i].w);
                    Assert.That(output[i].x, Is.EqualTo(VegetationHabitat.ValueNoise(p, (uint)seed)).Within(0.00002f));
                    Assert.That(output[i].y, Is.EqualTo(cover).Within(0.00002f));
                    Assert.That(output[i].z, Is.EqualTo(VegetationHabitat.GrassKeep(cover)).Within(0.00002f));
                }
            }
        }
    }
}
