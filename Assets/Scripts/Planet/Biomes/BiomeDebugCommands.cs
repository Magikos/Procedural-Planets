using UnityEngine;

/// <summary>
/// Console diagnostics for the biome terrain-material blend. Static (no instance state): the toggles are
/// shader globals published via <see cref="Shader.SetGlobalFloat"/>.
/// </summary>
[CommandPrefix("biome")]
public static class BiomeDebugCommands
{
    static readonly int _forceSameMaterialId = Shader.PropertyToID(ShaderGlobalIds.BiomeDebugForceSameMaterial);

    [ConsoleCommand("force-same-material",
        "Diagnostic: force every biome slot to sample ONE material while keeping the atlas weights + 4-corner path (0/1). "
        + "Border vanishes -> the band is material CONTRAST; border remains -> weights/filtering; remains only at distance -> minification.",
        MonoTargetType.Static)]
    public static string ForceSameMaterialCmd(float? on = null)
    {
        if (on.HasValue)
            Shader.SetGlobalFloat(_forceSameMaterialId, on.Value > 0.5f ? 1f : 0f);
        return $"biome force-same-material: {(Shader.GetGlobalFloat(_forceSameMaterialId) > 0.5f ? "on" : "off")}";
    }

    [ConsoleCommand("tint",
        "Live per-biome production albedo multiplier: biome.tint <BiomeType> <r> <g> <b> (default white). "
        + "Equalize biome mean lightness toward neighbours to soften high-contrast borders WITHOUT desaturating; "
        + "transient until regen -- bake winning values into the biome SurfaceAlbedoTint SO field.",
        MonoTargetType.Static)]
    public static string TintCmd(BiomeType biome, float r, float g, float b)
    {
        if (!BiomeAlbedoTintRuntime.TryGetSlot(biome, out int slot))
            return $"biome tint: '{biome}' is not in the active registry. {BiomeAlbedoTintRuntime.Describe()}";
        return BiomeAlbedoTintRuntime.SetTint(slot, new Color(r, g, b));
    }

    [ConsoleCommand("tint-list", "List the active biomes' current production albedo tints (slot:type (r,g,b)).", MonoTargetType.Static)]
    public static string TintListCmd() => BiomeAlbedoTintRuntime.Describe();
}
