using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public sealed class SunLightingRegressionTests
{
    const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [Test]
    public void ManualDirectionPersistsAndPublishesWithoutAnAtmosphereController()
    {
        var go = new GameObject("Sun ownership test");
        go.SetActive(false);
        var celestial = go.AddComponent<CelestialManager>();
        celestial.SunLight = go.AddComponent<Light>();
        celestial.SunLight.type = LightType.Directional;
        Vector4 saved = Shader.GetGlobalVector(ShaderGlobalIds.SunParams);
        try
        {
            SetField(celestial, "_initialized", true);
            celestial.SetTimeFrozen(true);
            Vector3 direction = new Vector3(1f, 2f, 3f).normalized;
            Assert.IsTrue(celestial.TrySetSunDirection(direction));
            for (int i = 0; i < 3; i++) Invoke(celestial, "Update");
            AssertDirection(celestial, direction);
            Assert.IsTrue(celestial.IsSunDirectionOverridden);

            Assert.IsFalse(celestial.TrySetSunDirection(new Vector3(float.NaN, 0f, 0f)));
            AssertDirection(celestial, direction);
            celestial.SetTimeOfDay(0.125f);
            Assert.IsFalse(celestial.IsSunDirectionOverridden);
            AssertDirection(celestial, MoonOrbit.Frame(0.125f, celestial.AxialTilt) * Vector3.up);

            Assert.IsTrue(celestial.TrySetSunDirection(Vector3.up));
            Invoke(celestial, "UpdateSun", 0.25f);
            AssertDirection(celestial, Vector3.up);
            celestial.ResetSunDirection();
            Assert.IsTrue(celestial.IsTimeFrozen);
            AssertDirection(celestial, MoonOrbit.Frame(celestial.TimeOfDay, celestial.AxialTilt) * Vector3.up);

            Assert.IsTrue(celestial.TrySetSunDirection(Vector3.down));
            celestial.ToggleTimeFrozen();
            Assert.IsFalse(celestial.IsTimeFrozen);
            Assert.IsFalse(celestial.IsSunDirectionOverridden);
            AssertDirection(celestial, MoonOrbit.Frame(celestial.TimeOfDay, celestial.AxialTilt) * Vector3.up);
        }
        finally
        {
            Object.DestroyImmediate(go);
            Shader.SetGlobalVector(ShaderGlobalIds.SunParams, saved);
        }
    }

    [TestCase("0 0 0")]
    [TestCase("NaN 0 0")]
    [TestCase("1e38 1e38 0")]
    public void InvalidDirectionIsRejectedThroughCommandExecutor(string vector)
    {
        var registry = (IDictionary<string, CommandData>)ConsoleRegistry.Commands;
        var saved = new Dictionary<string, CommandData>(registry);
        Vector4 sun = Shader.GetGlobalVector(ShaderGlobalIds.SunParams);
        try
        {
            ConsoleRegistry.Scan();
            var result = CommandExecutor.ExecuteImmediate("light.direction " + vector);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(sun, Shader.GetGlobalVector(ShaderGlobalIds.SunParams));
        }
        finally
        {
            registry.Clear();
            foreach (var entry in saved) registry.Add(entry.Key, entry.Value);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AtmosphereReenableRestoresOrRecreatesOpticalDepth(bool releaseTexture)
    {
        var go = new GameObject("Atmosphere binding test");
        go.SetActive(false);
        var atmosphere = go.AddComponent<AtmosphereController>();
        var source = ScriptableObject.CreateInstance<AtmosphereSettings>();
        Texture saved = Shader.GetGlobalTexture(ShaderGlobalIds.BakedOpticalDepth);
        RenderTexture texture = null;
        try
        {
            atmosphere.OpticalDepthCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Graphics/Shaders/OpticalDepth.compute");
            SetField(atmosphere, "_settings", AtmosphereDto.From(source) with { BakeTextureSize = 8, BakeSteps = 8 });
            SetField(atmosphere, "_planetRadius", 100f);
            SetField(atmosphere, "_seaLevelRadius", 95f);
            Invoke(atmosphere, "OnEnable");
            texture = (RenderTexture)Shader.GetGlobalTexture(ShaderGlobalIds.BakedOpticalDepth);
            Assert.IsNotNull(texture);
            Assert.IsTrue(texture.IsCreated());
            Invoke(atmosphere, "OnDisable");
            Assert.IsNull(Shader.GetGlobalTexture(ShaderGlobalIds.BakedOpticalDepth));
            if (releaseTexture) texture.Release();
            Invoke(atmosphere, "OnEnable");
            Assert.AreSame(texture, Shader.GetGlobalTexture(ShaderGlobalIds.BakedOpticalDepth));
            Assert.IsTrue(texture.IsCreated());
            var request = AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBAFloat);
            request.WaitForCompletion();
            Assert.IsFalse(request.hasError);
            Assert.IsTrue(request.GetData<Color>().Any(c => c.r > 0f || c.g > 0f));
        }
        finally
        {
            Invoke(atmosphere, "OnDisable");
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(source);
            Shader.SetGlobalTexture(ShaderGlobalIds.BakedOpticalDepth, saved);
        }
    }

    [Test]
    public void ShaderMathKeepsHorizonContinuousAndRejectsUnlitSpecular()
    {
        var shader = ShaderUtil.CreateShaderAsset(@"Shader ""Hidden/SunLightingRegression"" {
SubShader { Pass { ZTest Always Cull Off ZWrite Off
CGPROGRAM
#pragma vertex vert_img
#pragma fragment frag
#pragma target 4.0
#include ""UnityCG.cginc""
#include ""Assets/Graphics/Shaders/Includes/PlanetSunLighting.hlsl""
float3 _AuditRay, _AuditSun, _AuditView;
float4 frag(v2f_img input) : SV_Target {
    return float4(PlanetHorizonVisibility(float3(0,110,0),100,_AuditRay),
        PlanetSunSpecular(float3(0,1,0),_AuditSun,_AuditView,0.2),0,1);
}
ENDCG
} } }");
        Material material = null;
        var target = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        try
        {
            Assert.IsEmpty(ShaderUtil.GetShaderMessages(shader).Where(m =>
                m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error));
            material = new Material(shader);
            material.SetVector("_AuditSun", new Vector3(Mathf.Sqrt(0.99f), -0.1f, 0f));
            material.SetVector("_AuditView", Vector3.up);
            Color inside = Sample(-0.001f);
            Color outside = Sample(0.001f);
            Assert.That(inside.r, Is.InRange(0.49f, 0.5f));
            Assert.That(outside.r, Is.InRange(0.5f, 0.51f));
            Assert.That(outside.r - inside.r, Is.LessThan(0.01f));
            Assert.AreEqual(0f, inside.g);
            Assert.AreEqual(0f, outside.g);

            material.SetVector("_AuditRay", Vector3.up);
            material.SetVector("_AuditSun", Vector3.up);
            Assert.That(Read().r, Is.EqualTo(1f));
            Assert.That(Read().g, Is.GreaterThan(0f));
            material.SetVector("_AuditView", Vector3.down);
            Assert.That(Read().g, Is.EqualTo(0f), "Opposing light and view directions must remain finite.");
            material.SetVector("_AuditRay", Vector3.down);
            Assert.That(Read().r, Is.EqualTo(0f));

            Color Sample(float clearance)
            {
                float x = (100f + clearance) / 110f;
                material.SetVector("_AuditRay", new Vector3(x, -Mathf.Sqrt(1f - x * x), 0f));
                return Read();
            }

            Color Read()
            {
                Graphics.Blit(Texture2D.blackTexture, target, material);
                var request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBAFloat);
                request.WaitForCompletion();
                Assert.IsFalse(request.hasError);
                return request.GetData<Color>()[0];
            }
        }
        finally
        {
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(shader);
        }
    }

    static void AssertDirection(CelestialManager celestial, Vector3 expected)
    {
        Assert.That(Vector3.Distance(celestial.SunDirection, expected), Is.LessThan(1e-5f));
        Assert.That(Vector3.Distance((Vector3)Shader.GetGlobalVector(ShaderGlobalIds.SunParams), expected), Is.LessThan(1e-5f));
        Assert.That(Vector3.Distance(-celestial.SunLight.transform.forward, expected), Is.LessThan(1e-5f));
    }

    static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, PrivateInstance).SetValue(target, value);

    static void Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, PrivateInstance).Invoke(target, args);
}
