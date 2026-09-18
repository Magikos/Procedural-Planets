using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class MoonOrbitTests
{
    [TestCase(0f, 0f)]
    [TestCase(0.25f, 0.5f)]
    [TestCase(0.5f, 1f)]
    [TestCase(0.75f, 0.5f)]
    public void NamedPhasesRemainCorrectThroughoutTheDay(float phase, float fullness)
    {
        for (int dayStep = 0; dayStep < 24; dayStep++)
        {
            float day = dayStep / 24f;
            Vector3 sun = MoonOrbit.Frame(day, 23.5f) * Vector3.up;
            Vector3 moon = MoonOrbit.Direction(day, phase, 23.5f, 0f, 37f, out Vector3 pole);
            Assert.That((1f - Vector3.Dot(sun, moon)) * 0.5f, Is.EqualTo(fullness).Within(0.0001f));
            Assert.That(moon.magnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(Vector3.Dot(moon, pole), Is.EqualTo(0f).Within(0.0001f));
        }
    }

    [Test]
    public void WaxingAndWaningQuartersUseOppositeSidesOfTheSun()
    {
        Vector3 first = MoonOrbit.Direction(0f, 0.25f, 0f, 0f, 0f, out _);
        Vector3 last = MoonOrbit.Direction(0f, 0.75f, 0f, 0f, 0f, out _);
        Assert.That(first.x, Is.GreaterThan(0.999f));
        Assert.That(last.x, Is.LessThan(-0.999f));
        Assert.That(Vector3.Dot(first, last), Is.EqualTo(-1f).Within(0.0001f));
    }

    [Test]
    public void InclinationUsesTheDailyPlaneAndKeepsAStablePhase()
    {
        Vector3 referenceSun = MoonOrbit.Frame(0f, 23.5f) * Vector3.up;
        Vector3 referenceMoon = MoonOrbit.Direction(0f, 0.125f, 23.5f, 15f, 35f, out _);
        float alignment = Vector3.Dot(referenceSun, referenceMoon);
        for (int step = 0; step < 24; step++)
        {
            var frame = MoonOrbit.Frame(step / 24f, 23.5f);
            var moon = MoonOrbit.Direction(step / 24f, 0.125f, 23.5f, 15f, 35f, out var pole);
            Assert.That(Vector3.Angle(frame * Vector3.forward, pole), Is.EqualTo(15f).Within(0.001f));
            Assert.That(Vector3.Dot(frame * Vector3.up, moon), Is.EqualTo(alignment).Within(0.0001f));
        }
    }

    [Test]
    public void CycleDurationIsNewMoonToNewMoonAndZeroDurationCannotCorruptState()
    {
        Assert.That(MoonOrbit.Advance(0f, 120f, 120f, 8f), Is.EqualTo(0.125f).Within(0.000001f));
        Assert.That(MoonOrbit.Advance(0.25f, 960f, 120f, 8f), Is.EqualTo(0.25f).Within(0.000001f));
        Assert.That(MoonOrbit.Advance(-0.25f, 0f, 0f, 8f), Is.EqualTo(0.75f));
        Assert.That(MoonOrbit.Advance(0.5f, 10f, 120f, 0f), Is.EqualTo(0.5f));
        Assert.That(MoonOrbit.Advance(0.5f, 10f, float.NaN, 8f), Is.EqualTo(0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => MoonOrbit.Advance(float.NaN, 0f, 120f, 8f));
    }

    [Test]
    public void PhaseLabelsWrapAroundNewMoon()
    {
        for (int index = 0; index < 8; index++)
            Assert.That(MoonOrbit.PhaseIndex(index / 8f), Is.EqualTo(index));
        Assert.That(MoonOrbit.PhaseIndex(0.99f), Is.Zero);
        Assert.That(MoonOrbit.PhaseIndex(-0.25f), Is.EqualTo(6));
    }

    [Test]
    public void ApparentDiameterIsIndependentOfDistanceAndCannotIntersectThePlanet()
    {
        foreach (float distance in new[] { 2f, 3f, 100f })
        {
            float radius = MoonOrbit.VisualRadius(distance, MoonSettings.MaxDiameter);
            Assert.That(2f * Mathf.Asin(radius / distance) * Mathf.Rad2Deg,
                Is.EqualTo(MoonSettings.MaxDiameter).Within(0.0001f));
            Assert.That(distance - radius, Is.GreaterThan(1f));
        }
    }

    [Test]
    public void DistantVisualStaysInsideFarClipWithoutChangingItsAngularSize()
    {
        const float distance = 530000f, radius = 42000f, farClip = 100000f;
        float scale = MoonOrbit.ProjectionScale(distance, radius, farClip);
        Assert.That((distance + radius) * scale, Is.LessThan(farClip));
        Assert.That(radius * scale / (distance * scale), Is.EqualTo(radius / distance).Within(0.000001f));
        Assert.That(MoonOrbit.ProjectionScale(10000f, 1250f, farClip), Is.EqualTo(1f));
    }

    [Test]
    public void SettingsRejectInvalidValuesWithoutMutatingTheAuthoringAsset()
    {
        var source = ScriptableObject.CreateInstance<MoonSettings>();
        try
        {
            var settings = MoonDto.From(source);
            Assert.That(settings.TryValidate(out _), Is.True);
            foreach (var invalid in new[] { settings with { CycleDays = 0f }, settings with { Distance = float.PositiveInfinity },
                settings with { Diameter = 90f }, settings with { Inclination = -1f }, settings with { Detail = float.NaN },
                settings with { Tint = new Color(2f, 0f, 0f) } })
                Assert.That(invalid.TryValidate(out _), Is.False);
            Assert.That(source.CycleDays, Is.EqualTo(settings.CycleDays));
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
    }

    [Test]
    public void CommandAliasAndRejectionsUseTheSharedExecutor()
    {
        var registry = (IDictionary<string, CommandData>)ConsoleRegistry.Commands;
        var saved = new Dictionary<string, CommandData>(registry);
        var previous = ConsoleRegistry.GetInstance(typeof(CelestialCommands)) as CelestialCommands;
        var go = new GameObject("Moon command test");
        try
        {
            var celestial = go.AddComponent<CelestialManager>();
            using var commands = new CelestialCommands(celestial);
            ConsoleRegistry.Scan();
            Assert.That(ConsoleRegistry.TryGet("time.moon-phase", out var alias), Is.True);
            Assert.That(ConsoleRegistry.TryGet("time.moon.phase", out var canonical), Is.True);
            Assert.That(alias, Is.SameAs(canonical));
            Assert.That(canonical.ReleasePolicy, Is.EqualTo(ConsoleReleasePolicy.DevelopmentOnly));
            Assert.That(CommandExecutor.ExecuteImmediate("time.speed 0").Success, Is.False);
            Assert.That(CommandExecutor.ExecuteImmediate("time.shadow-grazing 2").Success, Is.False);
            Assert.That(CommandExecutor.ExecuteImmediate("time.set-local 0.5").Success, Is.False);
            Assert.That(CommandExecutor.ExecuteImmediate("time.moon.distance 3").Success, Is.False,
                "Uninitialized world settings must reject changes.");
            Assert.That(celestial.TrySetMoonPhase(float.PositiveInfinity), Is.False);
            Assert.That(celestial.TrySetMoonPhase(-0.25f), Is.True);
            Assert.That(celestial.MoonCycleProgress, Is.EqualTo(0.75f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
            if (previous != null) ConsoleRegistry.RegisterInstance(previous);
            registry.Clear();
            foreach (var entry in saved) registry.Add(entry.Key, entry.Value);
        }
    }
}
