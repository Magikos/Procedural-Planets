using UnityEngine;

public readonly struct GrassBiomeTintConfig
{
    public readonly Color TintBase;
    public readonly Color TintDryShift;
    public readonly Color TintLushShift;

    public GrassBiomeTintConfig(Color tintBase, Color tintDryShift, Color tintLushShift)
    {
        TintBase = tintBase;
        TintDryShift = tintDryShift;
        TintLushShift = tintLushShift;
    }

    public static GrassBiomeTintConfig From(BiomeDefinition src)
    {
        if (src == null)
            return new GrassBiomeTintConfig(Color.white, Color.white, Color.white);
        return new GrassBiomeTintConfig(src.GrassTintBase, src.GrassTintDryShift, src.GrassTintLushShift);
    }
}

public readonly struct GrassBiomePlacementConfig
{
    public readonly float Density;
    public readonly float Height;
    public readonly float Width;
    public readonly float ClumpStrength;
    public readonly float MaxSlopeDegrees;
    public readonly float SlopeFadeDegrees;
    public readonly float MinWaterClearance;
    public readonly float BiomeBlendPower;

    public GrassBiomePlacementConfig(float density, float height, float width, float clumpStrength,
        float maxSlopeDegrees, float slopeFadeDegrees, float minWaterClearance, float biomeBlendPower)
    {
        Density = density;
        Height = height;
        Width = width;
        ClumpStrength = clumpStrength;
        MaxSlopeDegrees = maxSlopeDegrees;
        SlopeFadeDegrees = slopeFadeDegrees;
        MinWaterClearance = minWaterClearance;
        BiomeBlendPower = biomeBlendPower;
    }

    public static GrassBiomePlacementConfig From(BiomeDefinition src)
    {
        if (src == null) return default;
        return new GrassBiomePlacementConfig(
            density: Mathf.Clamp01(src.GrassDensity),
            height: Mathf.Max(0f, src.GrassHeight),
            width: Mathf.Max(0f, src.GrassWidth),
            clumpStrength: Mathf.Clamp01(src.GrassClumpStrength),
            maxSlopeDegrees: Mathf.Clamp(src.GrassMaxSlopeDegrees, 0f, 90f),
            slopeFadeDegrees: Mathf.Max(0f, src.GrassSlopeFadeDegrees),
            minWaterClearance: Mathf.Max(0f, src.GrassMinWaterClearance),
            biomeBlendPower: Mathf.Max(0.001f, src.GrassBiomeBlendPower));
    }
}
