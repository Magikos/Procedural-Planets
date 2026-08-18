using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // A generated rock is derived from its seed and stored nowhere, exactly like the scatter instance that
    // carries it. If the same seed ever stops producing the same mesh, every rock in a saved world silently
    // changes shape between sessions — and unlike a placement bug that is invisible in code review, because the
    // generator still "works". These lock the properties that make derive-from-seed safe.
    public sealed class RockGeneratorTests
    {
        static RockDef Def() => new RockDef
        {
            Name = "Test",
            Size = 2.6f,
            AxisBias = new Vector3(1f, 0.62f, 0.92f),
            Roughness = 0.34f,
            Subdivisions = 2,
            Buried = 0.22f,
            Color = Color.grey,
        };

        static void Cleanup(GeneratedRock r)
        {
            foreach (Mesh m in r.Lods) if (m != null) Object.DestroyImmediate(m);
            if (r.Collider != null) Object.DestroyImmediate(r.Collider);
        }

        [Test]
        public void SameSeed_ProducesIdenticalMesh()
        {
            GeneratedRock a = RockGenerator.Generate(Def(), 12345);
            GeneratedRock b = RockGenerator.Generate(Def(), 12345);
            try
            {
                Vector3[] va = a.Lod0.vertices, vb = b.Lod0.vertices;
                Assert.AreEqual(va.Length, vb.Length, "vertex count must match");
                for (int i = 0; i < va.Length; i++)
                    Assert.AreEqual(va[i], vb[i], $"vertex {i} must be identical for the same seed");
            }
            finally { Cleanup(a); Cleanup(b); }
        }

        [Test]
        public void DifferentSeed_ProducesDifferentMesh()
        {
            GeneratedRock a = RockGenerator.Generate(Def(), 12345);
            GeneratedRock b = RockGenerator.Generate(Def(), 99999);
            try
            {
                Vector3[] va = a.Lod0.vertices, vb = b.Lod0.vertices;
                Assert.AreEqual(va.Length, vb.Length, "topology is fixed; only positions vary");
                float maxDelta = 0f;
                for (int i = 0; i < va.Length; i++) maxDelta = Mathf.Max(maxDelta, (va[i] - vb[i]).magnitude);
                Assert.Greater(maxDelta, 0.05f, "a different seed must produce a visibly different rock");
            }
            finally { Cleanup(a); Cleanup(b); }
        }

        // Rocks are placed with their pivot on the terrain, so anything below y=0 is buried and anything the
        // generator leaves below it would hang through a slope as a visible hole.
        [Test]
        public void MeshSitsOnItsPivot()
        {
            GeneratedRock r = RockGenerator.Generate(Def(), 777);
            try
            {
                foreach (Vector3 v in r.Lod0.vertices)
                    Assert.GreaterOrEqual(v.y, -1e-4f, "no vertex may sit below the pivot plane");
                Assert.Greater(r.Height, 0f, "a rock must have height above its pivot");
            }
            finally { Cleanup(r); }
        }

        // The collider is the coarsest shell on purpose: cooking LOD0 per instance is the expensive half of the
        // streamed-collider budget, and a player standing on a boulder cannot feel its facets.
        [Test]
        public void ColliderIsCoarserThanLod0()
        {
            GeneratedRock r = RockGenerator.Generate(Def(), 777);
            try
            {
                Assert.IsNotNull(r.Collider);
                Assert.Less(r.Collider.vertexCount, r.Lod0.vertexCount,
                    "collider hull must be cheaper than the drawn mesh");
            }
            finally { Cleanup(r); }
        }

        [Test]
        public void Subdivisions_ControlDetail()
        {
            RockDef coarse = Def(); coarse.Subdivisions = 0;
            RockDef fine = Def(); fine.Subdivisions = 2;
            GeneratedRock a = RockGenerator.Generate(coarse, 5);
            GeneratedRock b = RockGenerator.Generate(fine, 5);
            try
            {
                Assert.AreEqual(60, a.Lod0.vertexCount, "20 icosahedron faces, flat-shaded");
                Assert.Greater(b.Lod0.vertexCount, a.Lod0.vertexCount);
            }
            finally { Cleanup(a); Cleanup(b); }
        }
    }
}
