using UnityEngine;

// Immutable runtime DTOs for grass placement. Per the project's settings DTO pattern
// ([[feedback-settings-dto-pattern]]), grass placement consumers receive these snapshots
// rather than reading BiomeDefinition or other Settings SOs directly. The composition
// root (GrassPlacementController) builds these from the SOs once at chunk init.
//
// Why: keeps consumer dependencies signature-honest. If GrassTintBase is renamed on
// BiomeDefinition, only the builder changes; placement code and shaders are unaffected.

/// <summary>
/// Per-biome grass tint configuration. Built once per biome at planet init from
/// <see cref="BiomeDefinition"/>; passed to the placement compute as a packed buffer.
/// The compute weighted-blends these by the top-K biome weights at each blade root,
/// then lerps between dry and lush by local moisture.
/// </summary>
public readonly struct GrassBiomeTintConfig
{
    /// <summary>Base tint applied before any climate shift.</summary>
    public readonly Color TintBase;

    /// <summary>Multiplier applied to <see cref="TintBase"/> when local moisture is 0.</summary>
    public readonly Color TintDryShift;

    /// <summary>Multiplier applied to <see cref="TintBase"/> when local moisture is 1.</summary>
    public readonly Color TintLushShift;

    public GrassBiomeTintConfig(Color tintBase, Color tintDryShift, Color tintLushShift)
    {
        TintBase = tintBase;
        TintDryShift = tintDryShift;
        TintLushShift = tintLushShift;
    }

    /// <summary>
    /// Composition root: extract the tint contract from a biome SO. ONE place that
    /// knows BiomeDefinition's field layout. Rename a field on the SO and only this
    /// method changes.
    /// </summary>
    public static GrassBiomeTintConfig From(BiomeDefinition src)
    {
        if (src == null)
            return new GrassBiomeTintConfig(Color.white, Color.white, Color.white);
        return new GrassBiomeTintConfig(src.GrassTintBase, src.GrassTintDryShift, src.GrassTintLushShift);
    }
}

