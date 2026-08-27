using UnityEngine;

/// <summary>
/// The `creature.*` commands that need a camera, kept out of <see cref="CreatureResidencyService"/> because
/// that is authority code and may not touch one. The service answers WHERE to stand; this moves the view.
/// </summary>
/// <remarks>
/// Written after "I flew around Grassland and never saw a creature" turned out to be unanswerable from the
/// console: the residency was working and the animals were there, but nothing could take you to one.
/// </remarks>
[CommandPrefix("creature")]
public static class CreatureDebugCommands
{
    [ConsoleCommand("vision", "Predator view: dull the world to blue and ring every creature in a hot colour, through terrain.",
        MonoTargetType.Static)]
    public static string VisionCmd(bool on = true)
    {
        if (!ServiceLocator.TryGet(out CreatureView view))
            return "creature.vision: no creature view (generate a planet first)";

        view.PredatorVision.Enabled = on;
        return on
            ? "predator view ON. Markers hold a constant screen size and ignore depth, so a rabbit behind a " +
              "hill at 300 m is still a dot. `creature.vision false` to turn it off."
            : "predator view off";
    }

    [ConsoleCommand("swarms", "Butterflies, fireflies and carcass flies on or off, and how many are up.",
        MonoTargetType.Static)]
    public static string SwarmsCmd(bool on = true)
    {
        if (!ServiceLocator.TryGet(out AmbientSwarms swarms))
            return "creature.swarms: no swarm system (generate a planet first)";

        swarms.Enabled = on;
        return on
            ? $"swarms ON - {swarms.SwarmCount} up. Butterflies want the sun well up, fireflies want it below " +
              "the horizon WHERE YOU ARE, and flies want a carcass that has been dead a couple of minutes."
            : "swarms off";
    }

    [ConsoleCommand("goto", "Move the camera to the nearest creature, so 'I cannot find one' has an answer.",
        MonoTargetType.Static)]
    public static string GotoCmd()
    {
        Camera cam = Camera.main;
        if (cam == null) return "creature.goto: no main camera";
        if (!ServiceLocator.TryGet(out CreatureResidencyService creatures))
            return "creature.goto: no creature residency (generate a planet first)";

        if (!creatures.TryGetViewpointOfNearest(out Vector3 viewpoint, out Vector3 lookAt, out string described))
            return "creature.goto: nothing in range. `creature.status` lists each species' biomes - one whose " +
                   "list does not include the ground you are standing on has nothing here to find.";

        if (!ServiceLocator.TryGet(out IPlanet planet) || planet.Transform == null)
            return "creature.goto: no planet";

        Vector3 up = (lookAt - planet.Transform.position).normalized;
        cam.transform.position = viewpoint;
        cam.transform.rotation = Quaternion.LookRotation((lookAt - viewpoint).normalized, up);
        return "creature.goto: " + described;
    }
}
