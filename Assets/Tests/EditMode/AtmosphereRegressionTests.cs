using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class AtmosphereRegressionTests
{
    const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [Test]
    public void AuthoredSettingsRemainValid()
    {
        var source = Resources.Load<AtmosphereSettings>("Settings/AtmosphereSettings");
        Assert.IsTrue(AtmosphereDto.From(source).TryValidate(out string error), error);
    }

    [Test]
    public void InvalidSnapshotsAreRejectedBeforeApplying()
    {
        var source = ScriptableObject.CreateInstance<AtmosphereSettings>();
        var go = new GameObject("Atmosphere validation test");
        go.SetActive(false);
        var controller = go.AddComponent<AtmosphereController>();
        var valid = AtmosphereDto.From(source);
        SetField(controller, "_settings", valid);
        try
        {
            var invalid = new[]
            {
                valid with { ViewSteps = 0 }, valid with { BakeSteps = 0 },
                valid with { BakeTextureSize = 513 }, valid with { AtmosphereScale = 1f },
                valid with { RayleighScattering = new Vector3(-1f, 0f, 0f) },
                valid with { RayleighScaleHeight = 0f }, valid with { MieAnisotropy = 1f },
                valid with { SunIntensity = float.NaN }, valid with { LightShaftDecay = float.PositiveInfinity },
                valid with { TerrainClarityDistance = float.NaN }, valid with { SunDiscBlend = 0f },
            };
            foreach (var next in invalid)
            {
                Assert.IsFalse(next.TryValidate(out var error));
                Assert.IsNotEmpty(error);
                Assert.IsFalse(controller.TryApplySettings(next, out error));
                Assert.AreSame(valid, controller.SettingsSnapshot);
            }
        }
        finally { Object.DestroyImmediate(go); Object.DestroyImmediate(source); }
    }

    [TestCase("atmosphere.rayleigh -1 0 0")]
    [TestCase("atmosphere.mie NaN")]
    [TestCase("atmosphere.scale Infinity")]
    public void InvalidCommandsFailThroughExecutor(string command)
    {
        var registry = (IDictionary<string, CommandData>)ConsoleRegistry.Commands;
        var saved = new Dictionary<string, CommandData>(registry);
        var previous = ConsoleRegistry.GetInstance(typeof(AtmosphereCommands)) as AtmosphereCommands;
        var go = new GameObject("Atmosphere command test");
        go.SetActive(false);
        var controller = go.AddComponent<AtmosphereController>();
        var source = ScriptableObject.CreateInstance<AtmosphereSettings>();
        var settings = AtmosphereDto.From(source);
        SetField(controller, "_settings", settings);
        using var adapter = new AtmosphereCommands(controller);
        try
        {
            ConsoleRegistry.Scan();
            Assert.IsFalse(CommandExecutor.ExecuteImmediate(command).Success);
            Assert.AreSame(settings, controller.SettingsSnapshot);
            Assert.IsTrue(CommandExecutor.ExecuteImmediate("atmosphere.rayleigh").Success);
        }
        finally
        {
            adapter.Dispose();
            if (previous != null) ConsoleRegistry.RegisterInstance(previous);
            registry.Clear();
            foreach (var entry in saved) registry.Add(entry.Key, entry.Value);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(source);
        }
    }

    [Test]
    public void RenderFeatureRequiresReadyController()
    {
        var feature = ScriptableObject.CreateInstance<AtmosphereRenderFeature>();
        var controller = new AtmosphereStub();
        SetField(feature, "_cachedController", controller);
        try
        {
            Assert.IsFalse((bool)Invoke(feature, "TryGetLiveController"));
            controller.IsReady = true;
            Assert.IsTrue((bool)Invoke(feature, "TryGetLiveController"));
            controller.IsReady = false;
            Assert.IsFalse((bool)Invoke(feature, "TryGetLiveController"));
        }
        finally { Object.DestroyImmediate(feature); }
    }

    [Test]
    public void ResizingAndDestroyingBakeDestroysNativeTextures()
    {
        var go = new GameObject("Atmosphere lifetime test");
        go.SetActive(false);
        var controller = go.AddComponent<AtmosphereController>();
        var source = ScriptableObject.CreateInstance<AtmosphereSettings>();
        var settings = AtmosphereDto.From(source) with { BakeTextureSize = 8, BakeSteps = 8 };
        Texture saved = Shader.GetGlobalTexture(ShaderGlobalIds.BakedOpticalDepth);
        try
        {
            controller.OpticalDepthCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Graphics/Shaders/OpticalDepth.compute");
            SetField(controller, "_planetRadius", 100f);
            SetField(controller, "_seaLevelRadius", 95f);
            SetField(controller, "_settings", settings);
            Assert.IsFalse(controller.IsReady);
            Invoke(controller, "BakeOpticalDepth");
            var first = Shader.GetGlobalTexture(ShaderGlobalIds.BakedOpticalDepth);
            Assert.IsNotNull(first);
            go.SetActive(true);
            Assert.IsTrue(controller.IsReady);
            controller.enabled = false;
            Assert.IsFalse(controller.IsReady);
            controller.enabled = true;

            SetField(controller, "_settings", settings with { BakeTextureSize = 17 });
            Invoke(controller, "BakeOpticalDepth");
            Assert.IsTrue(first == null, "Resizing must destroy the old native object, not only release GPU storage.");
            var second = Shader.GetGlobalTexture(ShaderGlobalIds.BakedOpticalDepth);
            Assert.AreEqual(17, second.width);
            Invoke(controller, "OnDestroy");
            Assert.IsTrue(second == null);
            Assert.IsFalse(controller.IsReady);
        }
        finally
        {
            Invoke(controller, "OnDestroy");
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(source);
            Shader.SetGlobalTexture(ShaderGlobalIds.BakedOpticalDepth, saved);
        }
    }

    sealed class AtmosphereStub : IAtmosphereRuntime
    {
        public bool IsReady { get; set; }
    }

    static void SetField(object target, string name, object value) => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
    static object Invoke(object target, string name) => target.GetType().GetMethod(name, PrivateInstance).Invoke(target, null);
}
