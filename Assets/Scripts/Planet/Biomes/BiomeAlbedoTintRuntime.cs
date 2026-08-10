using UnityEngine;

/// <summary>
/// Per-biome production albedo multiplier (D2), published as the <c>_BiomeAlbedoTint</c> global vector array
/// indexed by biome slot id — the same slot id the atlas <c>_BiomeIds</c> store and the shader's
/// <c>CornerTriplanarWeightedPbr</c> resolves per slot. Authored default is white (identity, no change);
/// tuned live via <c>biome.tint</c>, baked into the SurfaceAlbedo textures for the final ship.
/// </summary>
public static class BiomeAlbedoTintRuntime
{
    // Shader vector-array uniform capacity. Biome counts are tiny (~14 land + water); 64 is ample headroom.
    const int MaxBiomes = 64;
    static readonly int _tintArrayId = Shader.PropertyToID(ShaderGlobalIds.BiomeAlbedoTint);
    static readonly Vector4[] _tints = new Vector4[MaxBiomes];
    static readonly BiomeType[] _types = new BiomeType[MaxBiomes];
    static int _count;

    static BiomeAlbedoTintRuntime() => ResetToIdentity();

    /// <summary>Publish the authored per-biome tints (called on each planet build).</summary>
    public static void Publish(BiomeRegistryDto registry, int sliceCount)
    {
        ResetToIdentity();
        _count = Mathf.Clamp(sliceCount, 0, MaxBiomes);
        if (registry != null)
        {
            for (int slot = 0; slot < _count; slot++)
            {
                BiomeDefinitionDto def = registry.GetDefinitionByIndex(slot);
                Color c = def != null ? def.SurfaceAlbedoTint : Color.white;
                _tints[slot] = new Vector4(c.r, c.g, c.b, 1f);
                _types[slot] = def != null ? def.Type : default;
            }
        }
        Shader.SetGlobalVectorArray(_tintArrayId, _tints);
    }

    /// <summary>Live-tune one biome's tint (transient until the next planet build re-publishes the authored values).</summary>
    public static string SetTint(int slot, Color c)
    {
        if (slot < 0 || slot >= MaxBiomes)
            return $"biome tint: slot {slot} out of range 0-{MaxBiomes - 1}";
        _tints[slot] = new Vector4(c.r, c.g, c.b, 1f);
        Shader.SetGlobalVectorArray(_tintArrayId, _tints);
        return $"biome {slot} tint = ({c.r:F2},{c.g:F2},{c.b:F2})" + (slot >= _count ? " (beyond active biome count)" : "");
    }

    public static Color GetTint(int slot)
        => slot >= 0 && slot < MaxBiomes ? new Color(_tints[slot].x, _tints[slot].y, _tints[slot].z) : Color.white;

    public static int Count => _count;

    public static bool TryGetSlot(BiomeType type, out int slot)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_types[i] == type) { slot = i; return true; }
        }
        slot = -1;
        return false;
    }

    public static string Describe()
    {
        if (_count == 0) return "biome tints: no active biome registry";
        var sb = new System.Text.StringBuilder($"biome tints ({_count}): ");
        for (int i = 0; i < _count; i++)
            sb.Append($"[{i}:{_types[i]} ({_tints[i].x:F2},{_tints[i].y:F2},{_tints[i].z:F2})] ");
        return sb.ToString();
    }

    public static void Clear()
    {
        ResetToIdentity();
        Shader.SetGlobalVectorArray(_tintArrayId, _tints);
        _count = 0;
    }

    static void ResetToIdentity()
    {
        for (int i = 0; i < MaxBiomes; i++)
            _tints[i] = Vector4.one;
    }
}
