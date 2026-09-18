using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // The Burst scatter gather samples terrain elevation through NoiseFilterEvaluator (blittable
    // NoiseFilterData) instead of the managed SimpleNoiseFilter/RigidNoiseFilter. Placement gates
    // (accept probability, altitude/water/slope) are thresholded on the resulting elevation, so a
    // drift of even one ULP between the two implementations would flip candidates in/out and move
    // props in a world. These direct-call tests pin snapshot/managed equality. They do not execute
    // compiled Burst jobs, whose floating-point arithmetic can differ. Golden literals pin the
    // corrected full-seed shuffle introduced on 2026-09-09.
    public sealed class NoiseFilterEvaluatorGoldenTests
    {
        static NoiseSettings Simple() => new NoiseSettings
        {
            Filter = NoiseSettings.FilterType.Simple,
            Strength = 1.3f, Layers = 4, BaseRoughness = 1.1f, Roughness = 2.2f,
            Persistence = 0.45f, Center = new Vector3(1.5f, -2.0f, 0.7f), MinValue = 0.3f,
        };

        static NoiseSettings Rigid() => new NoiseSettings
        {
            Filter = NoiseSettings.FilterType.Rigid,
            Strength = 0.9f, Layers = 5, BaseRoughness = 1.4f, Roughness = 2.0f,
            Persistence = 0.5f, Center = Vector3.zero, MinValue = 0.1f,
        };

        static float Snapshot(NoiseSettings s, int seed, Vector3 p)
        {
            NoiseFilterData d = NoiseFilterData.Create(s, seed, true, false);
            return NoiseFilterEvaluator.Evaluate(ref d, new float3(p.x, p.y, p.z));
        }

        static readonly Vector3[] Probes =
        {
            new Vector3(0.3f, 0.6f, -0.72f),
            new Vector3(-0.5f, 0.5f, 0.707f),
            new Vector3(1f, 0f, 0f),
            new Vector3(-0.211f, 0.885f, 0.414f),
            new Vector3(0.577f, 0.577f, 0.577f),
        };

        // --- Exact equality: the Burst evaluator must reproduce the managed filter bit-for-bit ---

        [Test]
        public void Simple_MatchesManagedFilterExactly()
        {
            const int seed = 12345;
            var settings = Simple();
            var managed = new SimpleNoiseFilter(settings, seed);
            foreach (var p in Probes)
            {
                float m = managed.Evaluate(p);
                float b = Snapshot(settings, seed, p);
                Assert.IsTrue(m == b, $"simple noise drift at {p}: managed {m:R} vs burst {b:R}");
            }
        }

        [Test]
        public void Rigid_MatchesManagedFilterExactly()
        {
            const int seed = 777;
            var settings = Rigid();
            var managed = new RigidNoiseFilter(settings, seed);
            foreach (var p in Probes)
            {
                float m = managed.Evaluate(p);
                float b = Snapshot(settings, seed, p);
                Assert.IsTrue(m == b, $"rigid noise drift at {p}: managed {m:R} vs burst {b:R}");
            }
        }

        // --- Golden literals: catch a change that moves managed AND burst together ---
        // Captured from the live implementation; a real algorithm change moves these by >> 1e-6.

        [Test]
        public void Simple_GoldenValue()
        {
            float v = Snapshot(Simple(), 12345, new Vector3(0.3f, 0.6f, -0.72f));
            Assert.AreEqual(0.975106955f, v, 1e-6f);
        }

        [Test]
        public void Rigid_GoldenValue()
        {
            float v = Snapshot(Rigid(), 777, new Vector3(-0.5f, 0.5f, 0.707f));
            Assert.AreEqual(0.5213849f, v, 1e-6f);
        }
        [TestCase(-0.02f, 0.04f, 0f)]
        [TestCase(0.02f, 0.04f, 0.5f)]
        [TestCase(0.04f, 0.08f, 0.5f)]
        [TestCase(0.08f, 0.04f, 1f)]
        [TestCase(0.02f, 0f, 0f)]
        public void LandMaskIsNormalizedCoverage(float elevation, float strength, float expected)
        {
            Assert.That(NoiseFilterEvaluator.FirstLayerMask(elevation, strength), Is.EqualTo(expected));
        }

        [TestCase(12345)]
        [TestCase(1691104419)]
        public void MountainLayersMatchManagedAndLeaveSeafloorUnmasked(int seed)
        {
            var asset = ScriptableObject.CreateInstance<PlanetSettings>();
            try
            {
                var settings = PlanetDto.From(asset).BuildShapeSettings();
                var shape = new ShapeGenerator();
                shape.Configure(settings);
                shape.Initialize(seed);
                using var filters = shape.BuildNoiseFilterData(Unity.Collections.Allocator.TempJob);
                float maximumUplift = 0f;
                int seaSamples = 0;
                for (int i = 0; i < 256; i++)
                {
                    float y = 1f - 2f * (i + .5f) / 256f;
                    float angle = i * 2.39996323f;
                    float r = Mathf.Sqrt(1f - y * y);
                    var point = new Vector3(r * Mathf.Cos(angle), y, r * Mathf.Sin(angle));
                    var first = filters[0];
                    float continent = NoiseFilterEvaluator.Evaluate(ref first, (float3)point);
                    float actual = NoiseFilterEvaluator.EvaluateLayers(filters, (float3)point);
                    Assert.That(actual, Is.EqualTo(shape.SampleElevation(point)).Within(1e-6f));
                    if (continent <= 0f)
                    {
                        Assert.That(actual, Is.EqualTo(continent));
                        seaSamples++;
                    }
                    maximumUplift = Mathf.Max(maximumUplift, actual - continent);
                }
                Assert.That(seaSamples, Is.GreaterThan(0));
                Assert.That(maximumUplift, Is.GreaterThan(0.005f), "Default relief must remain visible relative to radius.");
                Assert.That(maximumUplift, Is.LessThanOrEqualTo(0.0325f), "Default masked relief must respect its radius budget.");
            }
            finally { Object.DestroyImmediate(asset); }
        }
        [TestCase(1f)]
        [TestCase(50f)]
        [TestCase(5000f)]
        public void RecipeReliefBudgetScalesWithRadius(float radius)
        {
            var asset = ScriptableObject.CreateInstance<PlanetSettings>();
            try
            {
                asset.PlanetRadius = radius;
                foreach (float height in new[] { 0f, 0.5f, 1f })
                foreach (float roughness in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                {
                    asset.MountainHeight = height;
                    asset.TerrainRoughness = roughness;
                    var settings = PlanetDto.From(asset).BuildShapeSettings();
                    Assert.That(settings.PlanetRadius, Is.EqualTo(radius));
                    for (int index = 1; index < settings.NoiseLayers.Length; index++)
                    {
                        var noise = settings.NoiseLayers[index].NoiseSettings;
                        float sum = 0f;
                        float amplitude = 1f;
                        for (int octave = 0; octave < noise.Layers; octave++)
                        {
                            sum += amplitude;
                            amplitude *= noise.Persistence;
                        }
                        float budget = index == 1 ? 0.06f * height : 0.005f * roughness;
                        Assert.That(noise.Strength * sum * radius,
                            Is.EqualTo(budget * radius).Within(1e-5f * radius));
                    }
                }
            }
            finally { Object.DestroyImmediate(asset); }
        }
    }
}
