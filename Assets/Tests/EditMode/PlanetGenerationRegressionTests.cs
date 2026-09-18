using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class PlanetGenerationRegressionTests
    {
        const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator LowGenerationCancellationCompletesJobs()
        {
            var parent = new GameObject("Cancellation ownership test");
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<PlanetSettings>("Assets/Game Data/Planet Settings/Planet.asset");
            var settings = PlanetDto.From(asset).BuildShapeSettings();
            foreach (var layer in settings.NoiseLayers) layer.NoiseSettings.Layers = 16;
            var shape = new ShapeGenerator(); shape.Configure(settings); shape.Initialize(123);
            var provider = new PerFaceSurfaceProvider(parent.transform, shape, 128, null, Planet.FaceRenderMask.All);
            using var cts = new CancellationTokenSource();
            try
            {
                var awaiter = provider.GenerateAsync(null, cts.Token).GetAwaiter();
                Assert.That(awaiter.IsCompleted, Is.False, "The test must cancel work already in flight.");
                cts.Cancel();
                while (!awaiter.IsCompleted) yield return null;
                Assert.Throws<OperationCanceledException>(() => awaiter.GetResult());
            }
            finally { provider.Dispose(); UnityEngine.Object.DestroyImmediate(parent); }
        }

        [Test]
        public void WaterMapStopsSamplingAfterCancellation()
        {
            using var cts = new CancellationTokenSource();
            var ground = new CancelingGround(cts);
            var saved = WaterBodyMap.Current;
            Assert.Throws<OperationCanceledException>(() => WaterBodyMap.Build(ground, 5000, .02f, cts.Token));
            Assert.That(ground.Samples, Is.InRange(1, 256));
            Assert.That(WaterBodyMap.Current, Is.SameAs(saved));
        }

        sealed class CancelingGround : ISurfaceGroundSampler
        {
            readonly CancellationTokenSource _cts;
            public int Samples;
            public CancelingGround(CancellationTokenSource cts) { _cts = cts; }
            public bool TrySampleRadius(Vector3 direction, out float radius)
            {
                Samples++; _cts.Cancel(); radius = 5000; return true;
            }
            public Vector3 SampleNormalAt(Vector3 direction, float radius) => direction;
            public bool TrySampleGround(Vector3 direction, out float radius, out Vector3 normal)
            {
                normal = direction; return TrySampleRadius(direction, out radius);
            }
        }

        [Test]
        public void WaterOwnerReleasesMeshAfterChildDestruction()
        {
            var parent = new GameObject("Water mesh ownership test");
            var owner = new PlanetWaterSurface(parent.transform);
            var mesh = new Mesh();
            try
            {
                typeof(PlanetWaterSurface).GetField("_waterMesh", PrivateInstance).SetValue(owner, mesh);
                owner.NotifyChildrenDestroyed();
                Assert.That(mesh == null, Is.True);
            }
            finally { owner.Dispose(); if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(parent); }
        }

        [TestCase(256, 65536)]
        [TestCase(1, 16777216)]
        [TestCase(1, -1)]
        [TestCase(0, int.MinValue)]
        public void NoiseUsesAllSeedBitsAndRepeats(int first, int second)
        {
            var a = new Noise(first);
            var repeat = new Noise(first);
            var b = new Noise(second);
            int differences = 0;
            for (int i = 0; i < 32; i++)
            {
                var point = new Vector3(i * .137f, .413f, -.71f);
                Assert.That(a.Evaluate(point), Is.EqualTo(repeat.Evaluate(point)));
                if (a.Evaluate(point) != b.Evaluate(point)) differences++;
            }
            Assert.That(differences, Is.GreaterThan(24));
        }

        [Test]
        public void LowFacesInheritTransformAndReleaseMeshesAfterChildrenAreDestroyed()
        {
            var parent = new GameObject("Low face ownership test");
            var provider = new PerFaceSurfaceProvider(parent.transform, new ShapeGenerator(), 8, null, Planet.FaceRenderMask.All);
            try
            {
                parent.transform.SetPositionAndRotation(new Vector3(100, 200, 300), Quaternion.Euler(20, 40, 10));
                parent.transform.localScale = Vector3.one * 2;
                typeof(PerFaceSurfaceProvider).GetMethod("EnsureFaces", PrivateInstance).Invoke(provider, null);
                var meshes = provider.MeshFilters.Select(f => f.sharedMesh).ToArray();
                foreach (var filter in provider.MeshFilters)
                {
                    Assert.That(filter.transform.localPosition, Is.EqualTo(Vector3.zero));
                    Assert.That(filter.transform.localRotation, Is.EqualTo(Quaternion.identity));
                    Assert.That(filter.transform.localScale, Is.EqualTo(Vector3.one));
                    UnityEngine.Object.DestroyImmediate(filter.gameObject);
                }
                provider.Dispose();
                Assert.That(meshes.All(m => m == null), Is.True);
                provider.Dispose();
            }
            finally { provider.Dispose(); UnityEngine.Object.DestroyImmediate(parent); }
        }

        [Test]
        public void WaterComputationsRejectCancellationBeforeReadingInputs()
        {
            var ct = new CancellationToken(true);
            Assert.Throws<OperationCanceledException>(() => WaterBodyMap.Build(null, 5000, .02f, ct));
            Assert.Throws<OperationCanceledException>(() => WaterMeshBuilder.Compute(null, default, null, ct));
        }

        [TestCase(-.0005f, 6, 191)]
        [TestCase(.00125f, 2, 128)]
        [TestCase(.003f, 2, 255)]
        public void LakeShoreBakeRetainsMembership(float delta, int dominant, int weight)
        {
            var saved = WaterBodyMap.Current;
            try
            {
                var map = new WaterBodyMap();
                var mask = (byte[])typeof(WaterBodyMap).GetField("_mask", PrivateInstance).GetValue(map);
                for (int i = 0; i < mask.Length; i++) mask[i] = WaterBodyMap.Shore;
                typeof(WaterBodyMap).GetField("_level", PrivateInstance).SetValue(map, Enumerable.Repeat(.02f, mask.Length).ToArray());
                WaterBodyMap.Current = map;
                var result = Bake(Chunk(.25f, .02f + delta), Field(new byte[] { 2 }));
                Assert.That(result.ids[2080].r, Is.EqualTo(dominant));
                Assert.That(result.weights[2080].r, Is.EqualTo(weight));
                Assert.That(result.weights[2080].r + result.weights[2080].g, Is.EqualTo(255));
            }
            finally { WaterBodyMap.Current = saved; }
        }

        [Test]
        public void AdjacentBiomeMapsHaveIdenticalSharedEdges()
        {
            var saved = WaterBodyMap.Current;
            try
            {
                WaterBodyMap.Current = null;
                var field = Field(new byte[] { 2, 3 });
                var left = Bake(Chunk(.25f, .04f), field);
                var right = Bake(Chunk(.75f, .04f), field);
                for (int y = 0; y < 64; y++)
                {
                    Assert.That(left.colors[y * 64 + 63], Is.EqualTo(right.colors[y * 64]));
                    Assert.That(left.ids[y * 64 + 63], Is.EqualTo(right.ids[y * 64]));
                    Assert.That(left.weights[y * 64 + 63], Is.EqualTo(right.weights[y * 64]));
                }
            }
            finally { WaterBodyMap.Current = saved; }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AdjacentBiomeMapsShareVaryingInputs(bool vertical)
        {
            var saved = WaterBodyMap.Current;
            try
            {
                WaterBodyMap.Current = null;
                var trees = Enumerable.Range(0, 6).Select(f => new TerrainQuadtree(f)).ToArray();
                foreach (var tree in trees)
                {
                    tree.BuildToFixedDepth(1);
                    foreach (var chunk in tree.Root.Children)
                    {
                        chunk.CpuElevations = new float[4];
                        chunk.CpuBiomeData = new Vector4[4];
                        for (int y = 0; y < 2; y++)
                        for (int x = 0; x < 2; x++)
                        {
                            float u = chunk.UvCenter.x + (x - .5f) * .5f;
                            float v = chunk.UvCenter.y + (y - .5f) * .5f;
                            float t = vertical ? v : u;
                            chunk.CpuElevations[y * 2 + x] = (t - .5f) * .1f;
                            chunk.CpuBiomeData[y * 2 + x] = new Vector4(t, 1f-t, 0, 0);
                        }
                    }
                }
                var field = Field(new byte[] { 2, 3 });
                var a = Bake(trees[0].Root.Children[0], field, trees);
                var b = Bake(trees[0].Root.Children[vertical ? 2 : 1], field, trees);
                for (int i = 0; i < 64; i++)
                {
                    int ia = vertical ? 63 * 64 + i : i * 64 + 63;
                    int ib = vertical ? i : i * 64;
                    Assert.That(a.ids[ia], Is.EqualTo(b.ids[ib]), $"ids {i}");
                    Assert.That(a.weights[ia], Is.EqualTo(b.weights[ib]), $"weights {i}");
                    Assert.That(a.colors[ia], Is.EqualTo(b.colors[ib]), $"colors {i}");
                }
            }
            finally { WaterBodyMap.Current = saved; }
        }

        [Test]
        public void CubeFaceAtlasEdgesAndCornersShareAnOwner()
        {
            const int resolution = 9;
            var type = typeof(BiomeAtlasService).GetNestedType("FaceAtlasPixels", BindingFlags.NonPublic);
            var faces = Array.CreateInstance(type, 6);
            for (int f = 0; f < 6; f++)
            {
                var face = Activator.CreateInstance(type, new object[] { resolution });
                foreach (string name in new[] { "Blended", "Ids", "Weights" })
                {
                    var pixels = (Color32[])type.GetField(name).GetValue(face);
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i] = new Color32((byte)f, (byte)i, 0, 255);
                }
                faces.SetValue(face, f);
            }
            typeof(BiomeAtlasService).GetMethod("SynchronizeFaceEdges", PrivateStatic)
                .Invoke(null, new object[] { faces, resolution });
            var edgeIndex = typeof(BiomeAtlasService).GetMethod("EdgeIndex", PrivateStatic);
            for (int f = 0; f < 6; f++)
            for (int e = 0; e < 4; e++)
            for (int i = 0; i < resolution; i++)
            {
                var n = CubeFaceTopology.GetNeighbor(f, (CubeEdge)e);
                int j = n.EdgeParamReversed ? resolution - 1 - i : i;
                int a = (int)edgeIndex.Invoke(null, new object[] { (CubeEdge)e, i, resolution });
                int b = (int)edgeIndex.Invoke(null, new object[] { n.NeighborEdge, j, resolution });
                foreach (string name in new[] { "Blended", "Ids", "Weights" })
                {
                    var left = (Color32[])type.GetField(name).GetValue(faces.GetValue(f));
                    var right = (Color32[])type.GetField(name).GetValue(faces.GetValue(n.NeighborFace));
                    Assert.That(left[a], Is.EqualTo(right[b]), $"{name} face {f} edge {e} texel {i}");
                }
            }
        }

        static PlanetChunk Chunk(float x, float elevation)
        {
            var chunk = new PlanetChunk(4, 1, 0, 0, new Vector2(x, .25f), .25f, null);
            chunk.CpuElevations = Enumerable.Repeat(elevation, 4).ToArray();
            chunk.CpuBiomeData = Enumerable.Repeat(new Vector4(.5f, .5f, 0, 0), 4).ToArray();
            return chunk;
        }

        static object Field(byte[] cells) => Activator.CreateInstance(
            typeof(BiomeMapBaker).Assembly.GetType("DiagnosticGridBiomeField"),
            new object[] { new DiagnosticGridBiomeLayoutDto(0, cells.Length, 1, 0, cells, 2) });

        static (Color32[] colors, Color32[] ids, Color32[] weights) Bake(PlanetChunk chunk, object field, TerrainQuadtree[] terrain = null)
        {
            var lookup = new BiomeLookupData { TemperatureSteps = 1, MoistureSteps = 1,
                OceanBiomeId = 0, BeachBiomeId = 1, MountainBiomeId = 4, SnowyMountainBiomeId = 5,
                LakeBiomeId = 6, LakeShoreBiomeId = 7, BiomeCount = 8 };
            var colors = new Color32[4096]; var ids = new Color32[4096]; var weights = new Color32[4096];
            var lut = Enumerable.Range(0, 8).Select(i => new Color(i / 8f, 0, 0, 1)).ToArray();
            int size = BiomeMapBaker.HighResolutionSize;
            typeof(BiomeMapBaker).GetMethod("Bake", PrivateStatic).Invoke(null,
                new object[] { chunk, lookup, field, lut, colors, ids, weights, new byte[size * size], terrain });
            return (colors, ids, weights);
        }
    }
}
