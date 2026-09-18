using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class RiverRendererTests
    {
        [Test]
        public void HorizontalWaterSharesMaterialAndDisposalKeepsItsOwnerAlive()
        {
            var planet = new GameObject("River material test");
            var settings = ScriptableObject.CreateInstance<WaterSettings>();
            var material = new Material(Shader.Find("Planet/Ocean"));
            RiverRenderer renderer = null;
            using var field = new RiverField(new[]
            {
                Segment(0f, .004f, 5020f, 5020f, false),
                Segment(.004f, .008f, 5020f, 5000f, true)
            }, 5000f);
            try
            {
                renderer = new RiverRenderer(planet.transform, field, WaterDto.From(settings), null, material);
                Assert.That(renderer.VolumeSurfaces, Is.Not.Empty);
                foreach (var surface in renderer.VolumeSurfaces)
                {
                    Assert.That(surface.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(material));
                    Assert.That(WaterSurfaceRegistry.Snapshot, Does.Contain(surface));
                }
                var surfaces = renderer.VolumeSurfaces.ToArray();
                bool hasFallingSheet = false;
                foreach (var mesh in planet.GetComponentsInChildren<MeshRenderer>())
                    if (mesh.sharedMaterial != material)
                    {
                        hasFallingSheet = true;
                        Assert.That(mesh.sharedMaterial.shader.name, Is.EqualTo("Planet/River"));
                    }
                Assert.That(hasFallingSheet, Is.True);
                material.SetFloat("_DeepDepth", 123f);
                Assert.That(surfaces[0].GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_DeepDepth"), Is.EqualTo(123f));
                renderer.Dispose();
                renderer = null;
                Assert.That(material != null, Is.True, "The receiving water owns the shared material.");
                foreach (var surface in surfaces)
                    CollectionAssert.DoesNotContain(WaterSurfaceRegistry.Snapshot, surface);
            }
            finally
            {
                renderer?.Dispose();
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(planet);
            }
        }

        [Test]
        public void MissingSharedMaterialRejectsBeforeCreatingObjects()
        {
            var planet = new GameObject("Missing water material test");
            try
            {
                Assert.Throws<ArgumentNullException>(() => new RiverRenderer(planet.transform, null, null, null, null));
                Assert.That(planet.transform.childCount, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(planet); }
        }

        static RiverSegment Segment(float a, float b, float top, float bottom, bool waterfall) => new()
        {
            A = new float4(math.normalize(new float3(a, 1f, 0f)), top),
            B = new float4(math.normalize(new float3(b, 1f, 0f)), bottom),
            Shape = new float4(5f, 2f, 3f, waterfall ? 1f : 0f),
            Flow = new float4(1f, 0f, 20f, 10f),
            Profile = new float4(5f, 0f, 1f, 0f)
        };
    }
}
