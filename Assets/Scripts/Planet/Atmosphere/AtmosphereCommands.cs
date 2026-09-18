using UnityEngine;

[CommandPrefix("atmosphere", Group = "Sky and weather", ReleasePolicy = ConsoleReleasePolicy.DevelopmentOnly)]
public sealed class AtmosphereCommands : System.IDisposable
{
    readonly AtmosphereController _controller;

    public AtmosphereCommands(AtmosphereController controller)
    {
        _controller = controller ?? throw new System.ArgumentNullException(nameof(controller));
        ConsoleRegistry.RegisterInstance(this);
    }

    public void Dispose()
    {
        if (ReferenceEquals(ConsoleRegistry.GetInstance(typeof(AtmosphereCommands)), this))
            ConsoleRegistry.UnregisterInstance(typeof(AtmosphereCommands));
    }

    [ConsoleCommand("sun-intensity", "Get or set scattering sun intensity (range 1-100).", MonoTargetType.Registry)]
    ConsoleCommandResult SunIntensityCmd(float? value = null)
    {
        var _settings = _controller.SettingsSnapshot;
        if (_settings == null) return ConsoleCommandResult.Fail("Atmosphere settings are not initialized.");
        if (value.HasValue && !float.IsFinite(value.Value)) return ConsoleCommandResult.Fail("Value must be finite.");
        if (value == null) return ConsoleCommandResult.Ok($"sun intensity: {_settings.SunIntensity:F2}");
        float clamped = Mathf.Clamp(value.Value, 1f, 100f);
        if (!_controller.TryApplySettings(_settings with { SunIntensity = clamped }, out string error)) return ConsoleCommandResult.Fail(error);
        return ConsoleCommandResult.Ok($"sun intensity: {clamped:F2}");
    }

    [ConsoleCommand("rayleigh", "Get or set Rayleigh scattering vector (sky color).", MonoTargetType.Registry)]
    ConsoleCommandResult RayleighCmd(Vector3? value = null)
    {
        var _settings = _controller.SettingsSnapshot;
        if (_settings == null) return ConsoleCommandResult.Fail("Atmosphere settings are not initialized.");
        if (value == null)
        {
            Vector3 r = _settings.RayleighScattering;
            return ConsoleCommandResult.Ok($"rayleigh scattering: ({r.x:E3}, {r.y:E3}, {r.z:E3})");
        }
        if (!_controller.TryApplySettings(_settings with { RayleighScattering = value.Value }, out string error)) return ConsoleCommandResult.Fail(error);
        return ConsoleCommandResult.Ok($"rayleigh scattering: ({value.Value.x:E3}, {value.Value.y:E3}, {value.Value.z:E3})");
    }

    // Human 1.0 maps to this Mie coefficient; the 0.002 default therefore reads as 0.5.
    const float MieHumanScaleMax = 0.004f;

    [ConsoleCommand("mie", "Get or set sun-glow / haze strength, 0-1 (0=clear, 0.5=default, 1=heavy). Converts to the Mie scattering coefficient internally.", MonoTargetType.Registry)]
    ConsoleCommandResult MieCmd(float? value = null)
    {
        var _settings = _controller.SettingsSnapshot;
        if (_settings == null) return ConsoleCommandResult.Fail("Atmosphere settings are not initialized.");
        if (value.HasValue && !float.IsFinite(value.Value)) return ConsoleCommandResult.Fail("Value must be finite.");
        if (value == null) return ConsoleCommandResult.Ok($"mie (glow/haze): {_settings.MieScattering / MieHumanScaleMax:F2} (0-1)");
        float human = Mathf.Clamp01(value.Value);
        if (!_controller.TryApplySettings(_settings with { MieScattering = human * MieHumanScaleMax }, out string error)) return ConsoleCommandResult.Fail(error);
        return ConsoleCommandResult.Ok($"mie (glow/haze): {human:F2} (0-1)");
    }

    [ConsoleCommand("glow-tightness","Get or set sun glow tightness (Mie anisotropy, 0-0.99). Higher = smaller, more defined glow core; lower = broad washed-out halo. Default 0.76. Pair with 'mie' (glow strength).", MonoTargetType.Registry)]
    ConsoleCommandResult GlowTightnessCmd(float? value = null)
    {
        var _settings = _controller.SettingsSnapshot;
        if (_settings == null) return ConsoleCommandResult.Fail("Atmosphere settings are not initialized.");
        if (value.HasValue && !float.IsFinite(value.Value)) return ConsoleCommandResult.Fail("Value must be finite.");
        if (value == null) return ConsoleCommandResult.Ok($"glow tightness: {_settings.MieAnisotropy:F3}");
        float clamped = Mathf.Clamp(value.Value, 0f, 0.99f);
        if (!_controller.TryApplySettings(_settings with { MieAnisotropy = clamped }, out string error)) return ConsoleCommandResult.Fail(error);
        return ConsoleCommandResult.Ok($"glow tightness: {clamped:F3}");
    }

    [ConsoleCommand("scale", "Get or set atmosphere thickness scale (range 1.01-1.5).", MonoTargetType.Registry)]
    ConsoleCommandResult ScaleCmd(float? value = null)
    {
        var _settings = _controller.SettingsSnapshot;
        if (_settings == null) return ConsoleCommandResult.Fail("Atmosphere settings are not initialized.");
        if (value.HasValue && !float.IsFinite(value.Value)) return ConsoleCommandResult.Fail("Value must be finite.");
        if (value == null) return ConsoleCommandResult.Ok($"atmosphere scale: {_settings.AtmosphereScale:F3}");
        float clamped = Mathf.Clamp(value.Value, 1.01f, 1.5f);
        if (!_controller.TryApplySettings(_settings with { AtmosphereScale = clamped }, out string error)) return ConsoleCommandResult.Fail(error);
        return ConsoleCommandResult.Ok($"atmosphere scale: {clamped:F3}");
    }

    [ConsoleCommand("shaft-strength", "Get or set god-ray (light shaft) strength. 0=off, ~1 default, up to 8. Enables shafts when >0.", MonoTargetType.Registry)]
    ConsoleCommandResult ShaftStrengthCmd(float? value = null)
    {
        var _settings = _controller.SettingsSnapshot;
        if (_settings == null) return ConsoleCommandResult.Fail("Atmosphere settings are not initialized.");
        if (value.HasValue && !float.IsFinite(value.Value)) return ConsoleCommandResult.Fail("Value must be finite.");
        if (value == null) return ConsoleCommandResult.Ok($"light shaft strength: {_settings.LightShaftStrength:F2} (enabled: {_settings.EnableLightShafts})");
        float clamped = Mathf.Clamp(value.Value, 0f, 8f);
        if (!_controller.TryApplySettings(_settings with { LightShaftStrength = clamped, EnableLightShafts = clamped > 0f }, out string error)) return ConsoleCommandResult.Fail(error);
        return ConsoleCommandResult.Ok($"light shaft strength: {clamped:F2} (enabled: {clamped > 0f})");
    }

    [ConsoleCommand("sun-disc-blend", "Get or set sun disc edge softness, 0-1 (0=hard circle, 1=soft glow). Higher = thin cloud in front reads as a diffuse glow instead of a hard circle. Converts to the internal edge-blend width.", MonoTargetType.Registry)]
    ConsoleCommandResult SunDiscBlendCmd(float? value = null)
    {
        var _settings = _controller.SettingsSnapshot;
        if (_settings == null) return ConsoleCommandResult.Fail("Atmosphere settings are not initialized.");
        if (value.HasValue && !float.IsFinite(value.Value)) return ConsoleCommandResult.Fail("Value must be finite.");
        if (value == null) return ConsoleCommandResult.Ok($"sun disc blend: {Mathf.InverseLerp(AtmosphereSettings.SunDiscBlendMin, AtmosphereSettings.SunDiscBlendMax, _settings.SunDiscBlend):F2} (0-1)");
        float human = Mathf.Clamp01(value.Value);
        if (!_controller.TryApplySettings(_settings with { SunDiscBlend = Mathf.Lerp(AtmosphereSettings.SunDiscBlendMin, AtmosphereSettings.SunDiscBlendMax, human) }, out string error)) return ConsoleCommandResult.Fail(error);
        return ConsoleCommandResult.Ok($"sun disc blend: {human:F2} (0-1)");
    }

    [ConsoleCommand("sun-disc-intensity", "Get or set the hard sun disc brightness, 0-2. 0 = no disc (sun is only the atmosphere glow + god rays; vanishes behind cloud instead of bleeding through as a hard circle). 1 = full. ~0.3-0.5 = dim soft disc that centres the clear-sky sun without the bleed-through. Default 1.", MonoTargetType.Registry)]
    ConsoleCommandResult SunDiscIntensityCmd(float? value = null)
    {
        var _settings = _controller.SettingsSnapshot;
        if (_settings == null) return ConsoleCommandResult.Fail("Atmosphere settings are not initialized.");
        if (value.HasValue && !float.IsFinite(value.Value)) return ConsoleCommandResult.Fail("Value must be finite.");
        if (value == null) return ConsoleCommandResult.Ok($"sun disc intensity: {_settings.SunDiscIntensity:F2}");
        float clamped = Mathf.Clamp(value.Value, 0f, 2f);
        if (!_controller.TryApplySettings(_settings with { SunDiscIntensity = clamped }, out string error)) return ConsoleCommandResult.Fail(error);
        return ConsoleCommandResult.Ok($"sun disc intensity: {clamped:F2}");
    }

    [ConsoleCommand("sun-aureole", "Get or set the low-sun bloom around the sun disc (0-4). Fills the soft 'fireball' halo at dawn/dusk where the Mie glow collapses and leaves a bare circle; auto-fades by midday. 0 = off. Default 0.", MonoTargetType.Registry)]
    ConsoleCommandResult SunAureoleCmd(float? value = null)
    {
        var _settings = _controller.SettingsSnapshot;
        if (_settings == null) return ConsoleCommandResult.Fail("Atmosphere settings are not initialized.");
        if (value.HasValue && !float.IsFinite(value.Value)) return ConsoleCommandResult.Fail("Value must be finite.");
        if (value == null) return ConsoleCommandResult.Ok($"sun aureole: {_settings.SunAureoleStrength:F2}");
        float clamped = Mathf.Clamp(value.Value, 0f, 4f);
        if (!_controller.TryApplySettings(_settings with { SunAureoleStrength = clamped }, out string error)) return ConsoleCommandResult.Fail(error);
        return ConsoleCommandResult.Ok($"sun aureole: {clamped:F2}");
    }

    [ConsoleCommand("sun-aureole-size", "Get or set the sun aureole bloom radius (1-200). Lower = larger, softer bloom; higher = tighter. Default 40 (~15 deg).", MonoTargetType.Registry)]
    ConsoleCommandResult SunAureoleSizeCmd(float? value = null)
    {
        var _settings = _controller.SettingsSnapshot;
        if (_settings == null) return ConsoleCommandResult.Fail("Atmosphere settings are not initialized.");
        if (value.HasValue && !float.IsFinite(value.Value)) return ConsoleCommandResult.Fail("Value must be finite.");
        if (value == null) return ConsoleCommandResult.Ok($"sun aureole size (power): {_settings.SunAureolePower:F0}");
        float clamped = Mathf.Clamp(value.Value, 1f, 200f);
        if (!_controller.TryApplySettings(_settings with { SunAureolePower = clamped }, out string error)) return ConsoleCommandResult.Fail(error);
        return ConsoleCommandResult.Ok($"sun aureole size (power): {clamped:F0}");
    }
}
