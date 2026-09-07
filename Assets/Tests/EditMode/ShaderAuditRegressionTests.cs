using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ProceduralPlanets.Tests
{
    public sealed class ShaderAuditRegressionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void BiomeFallback_InitializesEveryMipWithoutOverwritingCopiedSlices(bool compressed)
        {
            var reference = new Texture2D(16, 16, TextureFormat.RGBA32, true, true);
            var noMips = new Texture2D(16, 16, TextureFormat.RGBA32, false, true);
            var definition = ScriptableObject.CreateInstance<BiomeDefinition>();
            using var arrays = new BiomeSurfaceTextureArrays();
            try
            {
                reference.SetPixels(Enumerable.Repeat(Color.green, 256).ToArray());
                reference.Apply(true);
                if (compressed) reference.Compress(true);
                definition.SurfaceAlbedo = reference;
                BiomeDefinitionDto first = BiomeDefinitionDto.From(definition);
                var registry = new BiomeRegistryDto(1, 3, new[]
                {
                    first, first with { SurfaceAlbedo = null }, first with { SurfaceAlbedo = noMips },
                }, null, null, null, null, null, null);
                arrays.Build(registry);
                Assert.AreEqual(5, arrays.AlbedoArray.mipmapCount);
                for (int mip = 0; mip < 5; mip++)
                for (int slice = 0; slice < 3; slice++)
                {
                    int size = Math.Max(1, 16 >> mip);
                    var readback = AsyncGPUReadback.Request(arrays.AlbedoArray, mip, 0, size, 0, size,
                        slice + 2, 1, TextureFormat.RGBA32);
                    readback.WaitForCompletion();
                    Assert.IsFalse(readback.hasError);
                    Color32 expected = slice == 0 ? new Color32(0, 255, 0, 255) : new Color32(255, 0, 255, 255);
                    foreach (Color32 pixel in readback.GetData<Color32>())
                        Assert.AreEqual(expected, pixel, $"slice {slice}, mip {mip}");
                }
            }
            finally
            {
                Object.DestroyImmediate(reference);
                Object.DestroyImmediate(noMips);
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void OpticalDepthLut_RemainsFiniteInHalfPrecisionAcrossTheTerminator()
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Graphics/Shaders/OpticalDepth.compute");
            var target = new RenderTexture(32, 32, 0, RenderTextureFormat.RGHalf) { enableRandomWrite = true };
            try
            {
                target.Create();
                int kernel = compute.FindKernel("Main");
                compute.SetTexture(kernel, "_Result", target);
                compute.SetInt("_TextureSize", 32);
                compute.SetInt("_NumSteps", 32);
                compute.SetFloat("_SeaLevelRadius", 5000f);
                compute.SetFloat("_AtmosphereRadius", 5500f);
                compute.SetFloat("_RayleighScaleHeight", 100f);
                compute.SetFloat("_MieScaleHeight", 30f);
                compute.Dispatch(kernel, 4, 4, 1);
                var readback = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBAFloat);
                readback.WaitForCompletion();
                Assert.IsFalse(readback.hasError);
                float maximum = 0f;
                foreach (Color sample in readback.GetData<Color>())
                {
                    Assert.That(sample.r, Is.InRange(0f, 1f));
                    Assert.That(sample.g, Is.InRange(0f, 1f));
                    Assert.IsFalse(float.IsNaN(sample.r) || float.IsNaN(sample.g));
                    maximum = Mathf.Max(maximum, sample.r);
                }
                Assert.Greater(maximum, 0.1f);
            }
            finally { Object.DestroyImmediate(target); }
        }

        [Test]
        public void FailedBake_RestoresGlobalsAndRemovesItsTemporaryRig()
        {
            var mesh = new Mesh { bounds = new Bounds(Vector3.zero, Vector3.one) };
            float saved = Shader.GetGlobalFloat(ShaderGlobalIds.ImpostorAlbedoBake);
            RenderTexture active = RenderTexture.active;
            int rigs = Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.name == "ImpostorAtlasBakeRig");
            try
            {
                Shader.SetGlobalFloat(ShaderGlobalIds.ImpostorAlbedoBake, 0.375f);
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    ScatterImpostorBaker.BakeAtlas(new[] { mesh }, Array.Empty<Material>(), 2, 16));
                Assert.AreEqual(0.375f, Shader.GetGlobalFloat(ShaderGlobalIds.ImpostorAlbedoBake));
                Assert.AreSame(active, RenderTexture.active);
                Assert.AreEqual(rigs, Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.name == "ImpostorAtlasBakeRig"));
            }
            finally
            {
                Shader.SetGlobalFloat(ShaderGlobalIds.ImpostorAlbedoBake, saved);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void FoliageBake_IsIndependentOfWindAndRestoresPriorBakeState()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh mesh = cube.GetComponent<MeshFilter>().sharedMesh;
            var material = new Material(Shader.Find("Scatter/FoliageLit"));
            Vector4 savedWind = Shader.GetGlobalVector(ShaderGlobalIds.WindDirection);
            float savedSpeed = Shader.GetGlobalFloat(ShaderGlobalIds.WindSpeedMps);
            float savedStrength = Shader.GetGlobalFloat(ShaderGlobalIds.WindStrength01);
            var a = default(ScatterImpostorBaker.AtlasCard);
            var b = default(ScatterImpostorBaker.AtlasCard);
            try
            {
                cube.SetActive(false);
                material.SetFloat("_ForceLeaf", 1f);
                material.SetFloat("_WindStrength", 2f);
                Shader.SetGlobalFloat(ShaderGlobalIds.WindSpeedMps, 0f);
                Shader.SetGlobalFloat(ShaderGlobalIds.WindStrength01, 0f);
                a = ScatterImpostorBaker.BakeAtlas(new[] { mesh }, new[] { material }, 2, 32);
                Shader.SetGlobalVector(ShaderGlobalIds.WindDirection, Vector3.right);
                Shader.SetGlobalFloat(ShaderGlobalIds.WindSpeedMps, 30f);
                Shader.SetGlobalFloat(ShaderGlobalIds.WindStrength01, 1f);
                b = ScatterImpostorBaker.BakeAtlas(new[] { mesh }, new[] { material }, 2, 32);
                Assert.IsTrue(a.Valid && b.Valid);
                CollectionAssert.AreEqual(a.Texture.GetPixels32(), b.Texture.GetPixels32());
                CollectionAssert.AreEqual(a.NormalTexture.GetPixels32(), b.NormalTexture.GetPixels32());
            }
            finally
            {
                Shader.SetGlobalVector(ShaderGlobalIds.WindDirection, savedWind);
                Shader.SetGlobalFloat(ShaderGlobalIds.WindSpeedMps, savedSpeed);
                Shader.SetGlobalFloat(ShaderGlobalIds.WindStrength01, savedStrength);
                Object.DestroyImmediate(a.Texture); Object.DestroyImmediate(a.NormalTexture);
                Object.DestroyImmediate(b.Texture); Object.DestroyImmediate(b.NormalTexture);
                Object.DestroyImmediate(material); Object.DestroyImmediate(cube);
            }
        }
    }
}
