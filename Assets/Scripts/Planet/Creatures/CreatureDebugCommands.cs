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

    [ConsoleCommand("ambience", "Why there are or are not butterflies, fireflies, birds and flies where you " +
        "are standing: the sun angle, the biome under your feet, and what each kind is waiting for.",
        MonoTargetType.Static)]
    public static string AmbienceCmd()
    {
        Camera cam = Camera.main;
        if (cam == null) return "creature.ambience: no main camera";
        if (!ServiceLocator.TryGet(out AmbientSwarms swarms))
            return "creature.ambience: no swarm system (generate a planet first)";
        if (!ServiceLocator.TryGet(out IPlanet planet) || planet.Transform == null)
            return "creature.ambience: no planet";

        Vector3 here = cam.transform.position;
        Vector3 up = (here - planet.Transform.position).normalized;

        float localSun = 1f;
        if (ServiceLocator.TryGet(out ICelestialTimeController celestial))
            localSun = Vector3.Dot(up, celestial.SunDirection);

        // Asked of the swarm system, not of ServiceLocator - the locator carries no IBiomeProvider, so
        // resolving one here returned a silent fallback and this line reported a biome nothing was tested
        // against. A readout that can disagree with the thing it reports on is worse than no readout.
        bool knowBiome = swarms.TryBiomeAt(here, out BiomeType biome);

        var sb = new System.Text.StringBuilder();
        sb.Append("sun ").Append(localSun.ToString("F2")).Append(' ').Append(DescribeSun(localSun))
          .Append("   biome ").Append(knowBiome ? biome.ToString() : "UNKNOWN")
          .Append("   swarms ").Append(swarms.Enabled ? "on" : "OFF (creature.swarms true)")
          .Append("   nearby flowers ").Append(swarms.NearbyFlowerCount);

        foreach (AmbientSwarmProfile p in swarms.Profiles)
        {
            int live = swarms.CountLive(p.Kind);
            sb.Append("\n  ").Append(p.DisplayName.PadRight(12))
              .Append(" live=").Append(live)
              .Append(" visible=").Append(swarms.CountParticles(p.Kind));

            if (swarms.TryNearest(p.Kind, here, out float metres, out float height))
            {
                sb.Append("  nearest ").Append(metres.ToString("F0")).Append(" m away");
                if (height > 3f) sb.Append(", ").Append(height.ToString("F0")).Append(" m UP - look up");
            }

            sb.Append("  -> ").Append(WhyNone(p, live, localSun, biome, knowBiome));
        }
        return sb.ToString();
    }

    // The whole point of the command: when a kind is missing, say which gate is holding it back.
    static string WhyNone(AmbientSwarmProfile p, int live, float localSun, BiomeType biome, bool knowBiome)
    {
        if (p.Kind == AmbientSwarmKind.Flies)
            return live > 0 ? "on a carcass" : "needs a carcass dead 2+ min within 120 m (creature.corpses)";

        float activity = p.ActivityAt(localSun);
        if (activity <= 0f)
            return localSun > p.MaxLocalSun
                ? $"too bright - wants sun below {p.MaxLocalSun:F2}"
                : $"too dark - wants sun above {p.MinLocalSun:F2}";

        int want = p.CountAt(localSun);
        if (p.Kind == AmbientSwarmKind.Bees && live == 0)
            return "needs an available nearby flower in the live scatter cache";
        if (knowBiome && !p.LivesIn(biome))
            return $"not a {biome} species - wants {string.Join("/", p.Biomes)}";
        if (live < want) return $"wants {want} here, still settling";
        return "here";
    }

    static string DescribeSun(float localSun) =>
        localSun > 0.6f ? "(high)" : localSun > 0.15f ? "(day)" : localSun > 0.02f ? "(low)"
        : localSun > -0.15f ? "(dusk/dawn)" : "(night)";

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
