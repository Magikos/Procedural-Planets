using UnityEngine;

/// <summary>
/// Border interlock-blend knobs, published as shader globals. The terrain's top-K albedo blend
/// (<c>CornerTriplanarWeightedPbr</c>) is a linear weighted average, so where two dissimilar biome materials
/// meet — bright grey rock against dark green forest — the correct 50/50 midpoint reads as a muddy third-colour
/// band. The interlock treats per-biome value noise as a pseudo-height and keeps only the locally-tallest
/// material within a narrow depth, resolving each pixel to (mostly) one material with an organic wiggly seam
/// instead of the average. Single-biome interiors are untouched. Defaults published on each planet build;
/// tuned live via <c>biome.interlock</c>.
/// </summary>
public static class BiomeBlendRuntime
{
    static readonly int _enabledId = Shader.PropertyToID(ShaderGlobalIds.BiomeInterlockEnabled);
    static readonly int _depthId = Shader.PropertyToID(ShaderGlobalIds.BiomeInterlockDepth);
    static readonly int _ampId = Shader.PropertyToID(ShaderGlobalIds.BiomeInterlockNoiseAmp);
    static readonly int _scaleId = Shader.PropertyToID(ShaderGlobalIds.BiomeInterlockNoiseScale);

    public const bool DefaultEnabled = true;
    public const float DefaultDepth = 0.08f;
    public const float DefaultNoiseAmp = 0.70f;
    public const float DefaultNoiseScale = 0.13f;

    static bool _enabled = DefaultEnabled;
    static float _depth = DefaultDepth;
    static float _amp = DefaultNoiseAmp;
    static float _scale = DefaultNoiseScale;

    public static bool Enabled => _enabled;
    public static float Depth => _depth;
    public static float NoiseAmp => _amp;
    public static float NoiseScale => _scale;

    /// <summary>Push the current (default at boot) knobs to the shader — call on each planet build.</summary>
    public static void PublishDefaults() => Publish();

    public static void Set(bool enabled, float depth, float amp, float scale)
    {
        _enabled = enabled;
        _depth = Mathf.Max(depth, 0.0001f);
        _amp = Mathf.Max(amp, 0f);
        _scale = Mathf.Max(scale, 0.0001f);
        Publish();
    }

    static void Publish()
    {
        Shader.SetGlobalFloat(_enabledId, _enabled ? 1f : 0f);
        Shader.SetGlobalFloat(_depthId, _depth);
        Shader.SetGlobalFloat(_ampId, _amp);
        Shader.SetGlobalFloat(_scaleId, _scale);
    }

    public static string Describe()
        => $"biome interlock: {(_enabled ? "on" : "off")} depth={_depth:F3} amp={_amp:F2} scale={_scale:F3}";
}
