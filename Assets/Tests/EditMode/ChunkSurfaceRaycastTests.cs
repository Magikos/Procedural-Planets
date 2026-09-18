using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ChunkSurfaceRaycastTests
    {
        delegate bool TriangleCast(Ray ray, Vector3 a, Vector3 b, Vector3 c, float max,
            out float distance, out float u, out float v);

        static readonly TriangleCast Triangle = (TriangleCast)typeof(ChunkSurfaceQueries)
            .GetMethod("RaycastTriangle", BindingFlags.Static | BindingFlags.NonPublic)
            .CreateDelegate(typeof(TriangleCast));

        [TestCase(9)]
        [TestCase(33)]
        [TestCase(97)]
        public void BoundsTraversal_MatchesExhaustiveTriangles(int resolution)
        {
            var chunk = new PlanetChunk(1, 0, 0, 0, Vector2.one * .5f, .5f, null);
            chunk.CpuVertices = new Vector3[resolution * resolution];
            chunk.CpuNormals = new Vector3[chunk.CpuVertices.Length];
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                int i = y * resolution + x;
                chunk.CpuVertices[i] = new Vector3(x, 5000 + Mathf.Sin(x * .3f) * Mathf.Cos(y * .2f), y);
                chunk.CpuNormals[i] = new Vector3(Mathf.Sin(x), 2, Mathf.Cos(y)).normalized;
            }
            int[] triangles = ChunkTriangleTemplate.Get(resolution);
            var random = new System.Random(718);
            for (int sample = 0; sample < 160; sample++)
            {
                float x = (float)random.NextDouble() * resolution;
                float z = (float)random.NextDouble() * resolution;
                var ray = new Ray(new Vector3(x, 5002, z),
                    new Vector3((float)random.NextDouble() - .5f, -1, (float)random.NextDouble() - .5f).normalized);
                float max = sample % 3 == 0 ? 1f : 20f;
                bool expected = false;
                float distance = max;
                Vector3 normal = default;
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    if (!Triangle(ray, chunk.CpuVertices[a], chunk.CpuVertices[b], chunk.CpuVertices[c], distance,
                        out float d, out float u, out float v)) continue;
                    expected = true;
                    distance = d;
                    normal = (chunk.CpuNormals[a] * (1 - u - v) + chunk.CpuNormals[b] * u + chunk.CpuNormals[c] * v).normalized;
                }
                bool actual = ChunkSurfaceQueries.RaycastChunkTriangles(ray, chunk, triangles, max,
                    out float actualDistance, out Vector3 point, out Vector3 actualNormal);
                Assert.AreEqual(expected, actual, $"ray {sample}");
                if (!expected) continue;
                Assert.AreEqual(distance, actualDistance);
                Assert.AreEqual(ray.origin + ray.direction * distance, point);
                Assert.That(Vector3.Dot(normal, actualNormal), Is.GreaterThan(.99999f));
            }
        }

        [Test]
        public void ReplacedGeometry_InvalidatesBounds_AndInsideBoundsHitsRemainVisible()
        {
            var chunk = new PlanetChunk(1, 0, 0, 0, Vector2.one * .5f, .5f, null);
            int[] triangles = { 0, 1, 2 };
            chunk.CpuVertices = new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(0, 0, 1) };
            Assert.IsTrue(ChunkSurfaceQueries.RaycastChunkTriangles(new Ray(Vector3.up, Vector3.down), chunk,
                triangles, 2, out _, out _, out _));
            chunk.CpuVertices = new[] { new Vector3(-1, 5, -1), new Vector3(1, 5, -1), new Vector3(0, 5, 1) };
            Assert.IsTrue(ChunkSurfaceQueries.RaycastChunkTriangles(new Ray(new Vector3(0, 5.0005f, 0), Vector3.down), chunk,
                triangles, .001f, out _, out _, out _));
            Assert.IsFalse(ChunkSurfaceQueries.RaycastChunkTriangles(new Ray(Vector3.up, Vector3.down), chunk,
                triangles, 2, out _, out _, out _));
        }
    }
}
