using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // The Burst scatter gather samples terrain elevation through NoiseFilterEvaluator (blittable
    // NoiseFilterData) instead of the managed SimpleNoiseFilter/RigidNoiseFilter. Placement gates
    // (accept probability, altitude/water/slope) are thresholded on the resulting elevation, so a
    // drift of even one ULP between the two implementations would flip candidates in/out and move
    // props in every world. These tests pin that equality: the Burst evaluator must equal the managed
    // filter EXACTLY, and the golden literals catch a change that moves both together.
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

        static float Burst(NoiseSettings s, int seed, Vector3 p)
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
                float b = Burst(settings, seed, p);
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
                float b = Burst(settings, seed, p);
                Assert.IsTrue(m == b, $"rigid noise drift at {p}: managed {m:R} vs burst {b:R}");
            }
        }

        // --- Golden literals: catch a change that moves managed AND burst together ---
        // Captured from the live implementation; a real algorithm change moves these by >> 1e-6.

        [Test]
        public void Simple_GoldenValue()
        {
            float v = Burst(Simple(), 12345, new Vector3(0.3f, 0.6f, -0.72f));
            Assert.AreEqual(0.6359836f, v, 1e-6f);
        }

        [Test]
        public void Rigid_GoldenValue()
        {
            float v = Burst(Rigid(), 777, new Vector3(-0.5f, 0.5f, 0.707f));
            Assert.AreEqual(0.918946147f, v, 1e-6f);
        }
    }
}
