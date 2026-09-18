using UnityEngine;

[CommandPrefix("time", Group = "Sky and weather", ReleasePolicy = ConsoleReleasePolicy.DevelopmentOnly)]
public sealed class CelestialCommands : System.IDisposable
{
    readonly CelestialManager _celestial;

    public CelestialCommands(CelestialManager celestial)
    {
        _celestial = celestial ?? throw new System.ArgumentNullException(nameof(celestial));
        ConsoleRegistry.RegisterInstance(this);
    }

    public void Dispose()
    {
        if (ReferenceEquals(ConsoleRegistry.GetInstance(typeof(CelestialCommands)), this))
            ConsoleRegistry.UnregisterInstance(typeof(CelestialCommands));
    }

    [ConsoleCommand("freeze", "Get or set the freeze state of both sun and moon.", MonoTargetType.Registry)]
    string Freeze(bool? frozen = null)
    {
        if (frozen.HasValue) _celestial.SetTimeFrozen(frozen.Value);
        return $"time frozen: {_celestial.IsTimeFrozen}";
    }

    [ConsoleCommand("speed", "Get or set day length in real seconds.", MonoTargetType.Registry)]
    ConsoleCommandResult Speed(float? seconds = null)
    {
        if (seconds.HasValue)
        {
            if (!float.IsFinite(seconds.Value) || seconds.Value < 0.1f)
                return ConsoleCommandResult.Fail("Day length must be finite and at least 0.1 seconds.");
            _celestial.DayLengthSeconds = seconds.Value;
        }
        return ConsoleCommandResult.Ok($"day length: {_celestial.DayLengthSeconds:F1}s");
    }

    [ConsoleCommand("shadow-grazing", "Get or set horizon shadow strength (0-1).", MonoTargetType.Registry)]
    ConsoleCommandResult ShadowGrazing(float? strength = null)
    {
        if (strength.HasValue)
        {
            if (!float.IsFinite(strength.Value) || strength < 0f || strength > 1f)
                return ConsoleCommandResult.Fail("Shadow strength must be between 0 and 1.");
            _celestial.ShadowGrazingStrength = strength.Value;
        }
        return ConsoleCommandResult.Ok($"grazing shadow strength: {_celestial.ShadowGrazingStrength:F2}");
    }

    [ConsoleCommand("shadow-fade-elev", "Get or set the shadow fade elevation in degrees.", MonoTargetType.Registry)]
    ConsoleCommandResult ShadowFade(float? degrees = null)
    {
        if (degrees.HasValue)
        {
            if (!float.IsFinite(degrees.Value) || degrees < 0f || degrees > 90f)
                return ConsoleCommandResult.Fail("Shadow fade elevation must be between 0 and 90 degrees.");
            _celestial.ShadowFadeElevationDeg = degrees.Value;
        }
        return ConsoleCommandResult.Ok($"shadow fade elevation: {_celestial.ShadowFadeElevationDeg:F1} deg");
    }

    [ConsoleCommand("set-local", "Set local time at the camera (0=midnight, 0.25=sunrise, 0.5=noon, 0.75=sunset).", MonoTargetType.Registry)]
    ConsoleCommandResult LocalTime(float fraction) => _celestial.TrySetLocalTimeOfDay(fraction)
        ? ConsoleCommandResult.Ok($"local time: {Mathf.Repeat(fraction, 1f):F2}; global: {_celestial.TimeOfDay:F2}")
        : ConsoleCommandResult.Fail("Local time requires a finite value and a camera away from the planet center and celestial poles.");

    [ConsoleCommand("moon.phase", "Get or set one of eight phases: 0=new, 2=first quarter, 4=full, 6=last quarter. Wraps modulo 8.",
        MonoTargetType.Registry, Aliases = new[] { "time.moon-phase" }, Example = "time.moon.phase 4")]
    ConsoleCommandResult Phase(int? index = null)
    {
        if (index.HasValue) _celestial.TrySetMoonPhase(((index.Value % 8 + 8) % 8) / 8f);
        return Status();
    }

    [ConsoleCommand("moon.progress", "Get or set continuous lunar progress (0=new, 0.5=full, 1=new). Values wrap.", MonoTargetType.Registry)]
    ConsoleCommandResult Progress(float? value = null)
    {
        if (value.HasValue && !_celestial.TrySetMoonPhase(value.Value))
            return ConsoleCommandResult.Fail("Moon progress must be finite.");
        return Status();
    }

    [ConsoleCommand("moon.hold", "Hold the lunar phase while the moon continues across the daily sky.", MonoTargetType.Registry)]
    string Hold(bool? held = null)
    {
        if (held.HasValue) _celestial.SetMoonPhaseHeld(held.Value);
        return $"moon phase held: {_celestial.IsMoonPhaseHeld}";
    }

    [ConsoleCommand("moon.status", "Show lunar progress, fullness, orbit, and appearance controls.", MonoTargetType.Registry)]
    ConsoleCommandResult Status()
    {
        var s = _celestial.MoonSettingsSnapshot;
        if (s == null) return ConsoleCommandResult.Fail("Moon settings are not initialized.");
        return ConsoleCommandResult.Ok($"moon phase: {_celestial.MoonPhaseIndex}; progress: {_celestial.MoonCycleProgress:F4}; fullness: {_celestial.MoonFullness:F4}; held: {_celestial.IsMoonPhaseHeld}\n"
            + $"cycle: {s.CycleDays} days; distance: {s.Distance} planet radii; diameter: {s.Diameter} deg; inclination: {s.Inclination} deg; node: {s.NodeAngle} deg\n"
            + $"brightness: {s.Brightness}; detail: {s.Detail}; earthshine: {s.Earthshine}; tint: {s.Tint}");
    }

    [ConsoleCommand("moon.cycle", "Get or set days per lunar cycle (0.1-365).", MonoTargetType.Registry)]
    ConsoleCommandResult Cycle(float? value = null) => Change(value, (s, v) => s with { CycleDays = v });

    [ConsoleCommand("moon.distance", "Get or set orbit distance in planet radii (2-100).", MonoTargetType.Registry)]
    ConsoleCommandResult Distance(float? value = null) => Change(value, (s, v) => s with { Distance = v });

    [ConsoleCommand("moon.diameter", "Get or set apparent diameter in degrees at the planet center (0.1-30).", MonoTargetType.Registry)]
    ConsoleCommandResult Diameter(float? value = null) => Change(value, (s, v) => s with { Diameter = v });

    [ConsoleCommand("moon.inclination", "Get or set orbital inclination in degrees (0-45).", MonoTargetType.Registry)]
    ConsoleCommandResult Inclination(float? value = null) => Change(value, (s, v) => s with { Inclination = v });

    [ConsoleCommand("moon.node", "Get or set the orbit node angle in degrees relative to the sun's daily frame (0-360).", MonoTargetType.Registry)]
    ConsoleCommandResult Node(float? value = null) => Change(value, (s, v) => s with { NodeAngle = v });

    [ConsoleCommand("moon.brightness", "Get or set moon surface brightness (0-4).", MonoTargetType.Registry)]
    ConsoleCommandResult Brightness(float? value = null) => Change(value, (s, v) => s with { Brightness = v });

    [ConsoleCommand("moon.detail", "Get or set crater normal strength (0-2).", MonoTargetType.Registry)]
    ConsoleCommandResult Detail(float? value = null) => Change(value, (s, v) => s with { Detail = v });

    [ConsoleCommand("moon.earthshine", "Get or set faint dark-side illumination (0-0.1).", MonoTargetType.Registry)]
    ConsoleCommandResult Earthshine(float? value = null) => Change(value, (s, v) => s with { Earthshine = v });

    [ConsoleCommand("moon.tint", "Set moon tint using red, green, and blue components (0-1).", MonoTargetType.Registry)]
    ConsoleCommandResult Tint(float red, float green, float blue)
    {
        var s = _celestial.MoonSettingsSnapshot;
        if (s == null) return ConsoleCommandResult.Fail("Moon settings are not initialized.");
        return _celestial.TryApplyMoonSettings(s with { Tint = new Color(red, green, blue) }, out string error)
            ? Status() : ConsoleCommandResult.Fail(error);
    }

    ConsoleCommandResult Change(float? value, System.Func<MoonDto, float, MoonDto> change)
    {
        var s = _celestial.MoonSettingsSnapshot;
        if (s == null) return ConsoleCommandResult.Fail("Moon settings are not initialized.");
        if (value.HasValue && !_celestial.TryApplyMoonSettings(change(s, value.Value), out string error))
            return ConsoleCommandResult.Fail(error);
        return Status();
    }
}
