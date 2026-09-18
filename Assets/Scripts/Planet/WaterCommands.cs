using UnityEngine;

[CommandPrefix("water", Group = "World and surface", ReleasePolicy = ConsoleReleasePolicy.DevelopmentOnly)]
public sealed class WaterCommands : System.IDisposable
{
    readonly PlanetWaterSurface _surface;

    public WaterCommands(PlanetWaterSurface surface)
    {
        _surface = surface ?? throw new System.ArgumentNullException(nameof(surface));
        ConsoleRegistry.RegisterInstance(this);
    }

    public void Dispose()
    {
        if (ReferenceEquals(ConsoleRegistry.GetInstance(typeof(WaterCommands)), this))
            ConsoleRegistry.UnregisterInstance(typeof(WaterCommands));
    }

    // Fields the mesh build consumes. Changing one only reaches the screen after a regenerate, so the
    // setter says so rather than half-applying: DeepDepth in particular is ALSO a live material float
    // and is the divisor that normalized the baked vertex depths, so a live-only change would rescale
    // every depth read against a mesh built for the old value.
    static readonly System.Collections.Generic.HashSet<string> _bakedFields = new(System.StringComparer.OrdinalIgnoreCase)
    {
        nameof(WaterDto.RiversEnabled),
        nameof(WaterDto.RiverCatchmentFraction),
        nameof(WaterDto.RiverHalfWidth),
        nameof(WaterDto.RiverDepth),
        nameof(WaterDto.WaterfallMinDrop),
        nameof(WaterDto.DeepDepth),
        nameof(WaterDto.ShoreRange),
        nameof(WaterDto.ReferenceRadius),
        nameof(WaterDto.LakeFreezeStartTemperature01),
        nameof(WaterDto.LakeFreezeCompleteTemperature01),
        nameof(WaterDto.OceanFreezeStartTemperature01),
        nameof(WaterDto.OceanFreezeCompleteTemperature01),
    };

    [ConsoleCommand("list", "List water settings with current values; [baked] needs planet.generate to take effect.", MonoTargetType.Registry)]
    ConsoleCommandResult ListCmd(string filter = null)
    {
        if (!SettingsProvider.IsRegistered<WaterDto>()) return ConsoleCommandResult.Fail("water: no WaterDto registered");
        var dto = SettingsProvider.GetSettings<WaterDto>();
        var sb = new System.Text.StringBuilder();
        foreach (var p in typeof(WaterDto).GetConstructors()[0].GetParameters())
        {
            if (!string.IsNullOrEmpty(filter) && p.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            object v = typeof(WaterDto).GetProperty(p.Name).GetValue(dto);
            sb.AppendLine($"{p.Name} = {v}{(_bakedFields.Contains(p.Name) ? "   [baked]" : "")}");
        }
        return ConsoleCommandResult.Ok(sb.Length == 0 ? "water: no fields matched" : sb.ToString().TrimEnd());
    }

    [ConsoleCommand("set", "water.set <field> <value> - update a float setting on the runtime DTO.", MonoTargetType.Registry)]
    ConsoleCommandResult SetCmd([CompletionSource(typeof(WaterFloatFieldNamesProvider))] string field, float value)
    {
        if (!SettingsProvider.IsRegistered<WaterDto>()) return ConsoleCommandResult.Fail("water: no WaterDto registered");
        WaterDto next = WithFloat(SettingsProvider.GetSettings<WaterDto>(), field, value, out string canonical, out string error);
        if (next == null) return ConsoleCommandResult.Fail("water.set: " + error);

        _surface.ApplySettings(next);
        return ConsoleCommandResult.Ok(_bakedFields.Contains(canonical)
            ? $"water.set {canonical} = {value}  [baked - run planet.generate to rebuild the mesh]"
            : $"water.set {canonical} = {value}");
    }

    [ConsoleCommand("setcolor", "water.setcolor <field> <r> <g> <b> [a] - update a Color setting (0-1 components).", MonoTargetType.Registry)]
    ConsoleCommandResult SetColorCmd([CompletionSource(typeof(WaterColorFieldNamesProvider))] string field, float r, float g, float b, float a = 1f)
    {
        if (!SettingsProvider.IsRegistered<WaterDto>()) return ConsoleCommandResult.Fail("water: no WaterDto registered");
        var value = new Color(r, g, b, a);
        WaterDto next = WithValue(SettingsProvider.GetSettings<WaterDto>(), field, value, typeof(Color), out string canonical, out string error);
        if (next == null) return ConsoleCommandResult.Fail("water.setcolor: " + error);

        _surface.ApplySettings(next);
        return ConsoleCommandResult.Ok($"water.setcolor {canonical} = {value}");
    }

    [ConsoleCommand("bodies", "List the water bodies found this generation (id, kind, size, level).", MonoTargetType.Registry)]
    ConsoleCommandResult BodiesCmd()
    {
        WaterBodyCatalog catalog = WaterBodyMap.Current?.Bodies;
        if (catalog == null) return ConsoleCommandResult.Fail("water.bodies: no catalog (generate a planet first)");

        var planet = SettingsProvider.GetSettings<PlanetDto>();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"catalog v{catalog.Version}: {catalog.CountOf(WaterBodyKind.Lake)} lake(s), " +
                      $"{catalog.CountOf(WaterBodyKind.Ocean)} ocean(s), seam asymmetry " +
                      $"{WaterBodyMap.Current.SeamAsymmetryCount}, submerged cells {WaterBodyMap.Current.SubmergedCellCount}");
        sb.AppendLine($"  global ocean level {planet.OceanLevel:F4}; rise is metres above it at R={planet.PlanetRadius:F0}");

        // What the spill solve found, against what the global wet predicate currently draws. The gap is the
        // work W5b turns into raised lakes, and the size histogram is what sets its minimum-area threshold.
        int[] basins = WaterBodyMap.Current.SubmergedBasinSizes;
        if (basins.Length > 0)
        {
            sb.Append("  spill basins by minimum size:");
            foreach (int t in new[] { 1, 4, 8, 16, 64 })
            {
                int n = 0;
                foreach (int s in basins) if (s >= t) n++;
                sb.Append($"  >={t}:{n}");
            }
            sb.AppendLine();
        }
        foreach (WaterBody b in catalog.Bodies)
        {
            float riseMeters = (b.SurfaceElevation - planet.OceanLevel) * planet.PlanetRadius;
            float depthMeters = (b.SurfaceElevation - b.MinElevation) * planet.PlanetRadius;
            sb.AppendLine($"  #{b.Id} {b.Kind} cells={b.CellCount} level={b.SurfaceElevation:F5} " +
                          $"rise={riseMeters:F1}m depth={depthMeters:F1}m " +
                          $"dir=({b.CenterDirection.x:F2},{b.CenterDirection.y:F2},{b.CenterDirection.z:F2})");
        }
        return ConsoleCommandResult.Ok(sb.ToString().TrimEnd());
    }

    [ConsoleCommand("at", "water.at [x y z] - report the water surface at a world position, or at the camera.", MonoTargetType.Registry)]
    ConsoleCommandResult AtCmd(float? x = null, float? y = null, float? z = null)
    {
        if (!ServiceLocator.TryGet(out IWaterQueryService query))
            return ConsoleCommandResult.Fail("water.at: no IWaterQueryService registered");

        if ((x.HasValue || y.HasValue || z.HasValue) && !(x.HasValue && y.HasValue && z.HasValue))
            return ConsoleCommandResult.Fail("water.at: provide all three coordinates or none");

        Vector3 probe;
        if (x.HasValue && y.HasValue && z.HasValue) probe = new Vector3(x.Value, y.Value, z.Value);
        else if (Camera.main != null) probe = Camera.main.transform.position;
        else return ConsoleCommandResult.Fail("water.at: no position given and no main camera");

        if (!query.TryGetWaterSurface(probe, out WaterSample s))
            return ConsoleCommandResult.Ok($"water.at ({probe.x:F1},{probe.y:F1},{probe.z:F1}): no water body here");

        return ConsoleCommandResult.Ok($"water.at ({probe.x:F1},{probe.y:F1},{probe.z:F1}): body #{s.BodyId} {(s.IsOcean ? "ocean" : "lake")}, " +
               $"{(s.IsSubmerged ? "submerged" : "above surface")} by {Mathf.Abs(s.SignedDepth):F2} m, " +
               $"body depth {s.BodyDepth:F1} m, surface at ({s.SurfacePoint.x:F1},{s.SurfacePoint.y:F1},{s.SurfacePoint.z:F1})");
    }

    [ConsoleCommand("reset", "Restore the runtime DTO from the authored WaterSettings asset.", MonoTargetType.Registry)]
    ConsoleCommandResult ResetCmd()
    {
        var source = Resources.FindObjectsOfTypeAll<WaterSettings>();
        if (source == null || source.Length == 0) return ConsoleCommandResult.Fail("water.reset: no WaterSettings asset loaded");
        WaterDto restored = WaterDto.From(source[0]);
        if (restored == null) return ConsoleCommandResult.Fail("water.reset: failed to read WaterSettings");
        _surface.ApplySettings(restored);
        return ConsoleCommandResult.Ok("water.reset: restored from " + source[0].name + " (baked fields need planet.generate)");
    }

    static WaterDto WithFloat(WaterDto src, string field, float value, out string canonical, out string error) =>
        WithValue(src, field, value, typeof(float), out canonical, out error);

    // Records are immutable and positional, so build the replacement through the primary constructor.
    // Doing it reflectively means a new WaterDto field needs no matching change here.
    static WaterDto WithValue(WaterDto src, string field, object value, System.Type expected, out string canonical, out string error)
    {
        canonical = null;
        var ctor = typeof(WaterDto).GetConstructors()[0];
        var parameters = ctor.GetParameters();
        var args = new object[parameters.Length];
        int target = -1;
        for (int i = 0; i < parameters.Length; i++)
        {
            args[i] = typeof(WaterDto).GetProperty(parameters[i].Name).GetValue(src);
            if (string.Equals(parameters[i].Name, field, System.StringComparison.OrdinalIgnoreCase))
                target = i;
        }

        if (target < 0) { error = $"unknown field '{field}' (try water.list)"; return null; }
        if (parameters[target].ParameterType != expected)
        {
            error = $"'{parameters[target].Name}' is {parameters[target].ParameterType.Name}, not a {expected.Name}";
            return null;
        }

        canonical = parameters[target].Name;
        if (canonical == nameof(WaterDto.RiverCatchmentFraction) || canonical == nameof(WaterDto.RiverHalfWidth) ||
            canonical == nameof(WaterDto.RiverDepth) || canonical == nameof(WaterDto.WaterfallMinDrop))
        {
            float riverValue = (float)value;
            float minimum = canonical == nameof(WaterDto.RiverCatchmentFraction) ? .00005f : canonical == nameof(WaterDto.RiverDepth) ? .25f : 1f;
            float maximum = canonical == nameof(WaterDto.RiverCatchmentFraction) ? .01f : canonical == nameof(WaterDto.RiverHalfWidth) ? 50f : canonical == nameof(WaterDto.RiverDepth) ? 20f : 200f;
            if (!float.IsFinite(riverValue) || riverValue < minimum || riverValue > maximum)
            {
                error = $"'{canonical}' must be finite and within {minimum} to {maximum}";
                return null;
            }
        }
        if (canonical == nameof(WaterDto.UnderwaterSurfaceDetail))
        {
            float detail = (float)value;
            if (float.IsNaN(detail) || detail < 0f || detail > 1f)
            {
                error = $"'{canonical}' must be a finite value from 0 to 1";
                return null;
            }
        }
        if (canonical == nameof(WaterDto.UnderwaterVisibility) || canonical == nameof(WaterDto.UnderwaterShaftWidth))
        {
            float distance = (float)value;
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 1f)
            {
                error = $"'{canonical}' must be a finite distance of at least 1 metre";
                return null;
            }
        }
        if (canonical == nameof(WaterDto.UnderwaterFogColor))
        {
            Color color = (Color)value;
            for (int channel = 0; channel < 4; channel++)
            {
                if (float.IsNaN(color[channel]) || color[channel] < 0f || color[channel] > 1f)
                {
                    error = $"'{canonical}' components must be finite values from 0 to 1";
                    return null;
                }
            }
        }
        args[target] = value;
        error = null;
        return (WaterDto)ctor.Invoke(args);
    }

}
