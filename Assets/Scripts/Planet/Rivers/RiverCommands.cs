using System;
using UnityEngine;

[CommandPrefix("river", Group = "World and surface", ReleasePolicy = ConsoleReleasePolicy.DevelopmentOnly)]
public sealed class RiverCommands : IDisposable
{
    readonly Planet _planet;
    public RiverCommands(Planet planet)
    {
        _planet = planet;
        ConsoleRegistry.RegisterInstance(this);
    }

    [ConsoleCommand("status", "Show generated river segment and waterfall counts.", MonoTargetType.Registry)]
    public ConsoleCommandResult Status()
    {
        var field = _planet != null ? _planet.Rivers : null;
        return field == null ? ConsoleCommandResult.Fail("Generate a planet before inspecting rivers.")
            : ConsoleCommandResult.Ok($"Rivers: {field.SegmentCount} segments, {field.WaterfallCount} waterfalls. Use river.visit 0 or river.visit 0 true.");
    }

    [ConsoleCommand("visit", "Visit a generated river segment or waterfall.", MonoTargetType.Registry, Example = "river.visit 0 true")]
    public ConsoleCommandResult Visit(int index = 0, bool waterfall = false)
    {
        if (_planet == null || !_planet.TryGetRiverView(index, waterfall, out var location))
            return ConsoleCommandResult.Fail("River index is outside the generated range.");
        if (!ServiceLocator.TryGet<ICameraRigContext>(out var context) || context is not ICameraTeleportTarget target)
            return ConsoleCommandResult.Fail("The current camera does not support teleporting.");
        return target.TryApply(location, out string error) ? ConsoleCommandResult.Ok($"Viewing {(waterfall ? "waterfall" : "river")} {index}.")
            : ConsoleCommandResult.Fail(error);
    }

    public void Dispose()
    {
        if (ReferenceEquals(ConsoleRegistry.GetInstance(typeof(RiverCommands)), this))
            ConsoleRegistry.UnregisterInstance(typeof(RiverCommands));
    }
}
