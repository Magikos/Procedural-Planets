using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class RiverBankGpuTests
    {
        [TestCase(0f, 0f, 5010f, false, false, 0, true)]
        [TestCase(0f, 15f, 5010f, false, false, 0, true)]
        [TestCase(0f, 15f, 5010f, true, false, 0, false)]
        [TestCase(0f, 0f, 5010f, true, false, 0, true)]
        [TestCase(0f, 25f, 5010f, false, false, 0, false)]
        [TestCase(0f, 0f, 5000f, false, false, 0, false)]
        [TestCase(0f, 0f, 5005f, false, false, 0, false)]
        [TestCase(0f, 0f, 5010f, false, true, 0, false)]
        [TestCase(-27f, 0f, 5020f, false, false, 0, true)]
        [TestCase(-27f, 0f, 5020f, false, false, 1, false)]
        public void StandingCoverRespectsRiverBanksAndWaterfallEnds(float along, float across,
            float standingRadius, bool standingBank, bool waterfall, int clippedEnds, bool excluded)
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders are unavailable.");
            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Tests/EditMode/RiverBankProbe.compute");
            Assert.That(compute, Is.Not.Null);
            var names = new[] { "_RiverSegments", "_RiverRanges", "_RiverIndices" };
            var textures = new Texture[names.Length];
            for (int i = 0; i < names.Length; i++) textures[i] = Shader.GetGlobalTexture(names[i]);
            int active = Shader.GetGlobalInt("_RiverActive");
            float radius = Shader.GetGlobalFloat("_RiverPlanetRadius");
            using var field = new RiverField(new[]
            {
                new RiverSegment
                {
                    A = new float4(math.normalize(new float3(-.004f, 1f, 0f)), 5010f),
                    B = new float4(math.normalize(new float3(.004f, 1f, 0f)), 5000f),
                    Shape = new float4(10f, 2f, 3f, waterfall ? 1f : 0f),
                    Flow = new float4(1f, 0f, 40f, 18f),
                    Profile = new float4(10f, 0f, 1f, clippedEnds)
                }
            }, 5000f);
            try
            {
                using var gpu = new RiverGpu(field);
                using var buffer = new ComputeBuffer(1, sizeof(float) * 4);
                int kernel = compute.FindKernel("Probe");
                RiverGpu.Bind(compute, kernel);
                Vector3 direction = new Vector3(along / 5000f, 1f, across / 5000f).normalized;
                compute.SetVector("_Probe", new Vector4(direction.x, direction.y, direction.z, standingRadius));
                compute.SetInt("_PreserveStandingBanks", standingBank ? 1 : 0);
                compute.SetBuffer(kernel, "_Result", buffer);
                compute.Dispatch(kernel, 1, 1, 1);
                var result = new Vector4[1];
                buffer.GetData(result);
                Assert.That(result[0].z, Is.EqualTo(excluded ? 1f : 0f));
                if (field.Data.SampleReference(direction, out var segment, out float t, out float distance))
                {
                    Assert.That(result[0].x, Is.EqualTo(segment.Radius(t)).Within(.002f));
                    Assert.That(result[0].y, Is.EqualTo(segment.Width(t) - distance).Within(.002f));
                }
            }
            finally
            {
                for (int i = 0; i < names.Length; i++) Shader.SetGlobalTexture(names[i], textures[i]);
                Shader.SetGlobalInt("_RiverActive", active);
                Shader.SetGlobalFloat("_RiverPlanetRadius", radius);
            }
        }
    }
}
