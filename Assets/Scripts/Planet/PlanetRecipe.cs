using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Planet Recipe")]
public class PlanetRecipe : ScriptableObject
{
    public PlanetSettings PlanetSettings;
    public BiomeSettings BiomeSettings;
    public DiagnosticGridBiomeLayout DiagnosticGridBiomeLayout;
    public DiagnosticTerrainLayout DiagnosticTerrainLayout;

    public BiomeSettings BiomeSettingsSource =>
        BiomeSettings != null ? BiomeSettings : PlanetSettings != null ? PlanetSettings.BiomeSettings : null;

    public PlanetDto ToPlanetDto()
    {
        PlanetDto planet = PlanetDto.From(PlanetSettings);
        return planet != null && DiagnosticTerrainLayout != null
            ? planet with { DiagnosticTerrainLayout = DiagnosticTerrainLayout.ToDto() }
            : planet;
    }

    public BiomeDto ToBiomeDto()
    {
        BiomeDto biome = BiomeDto.From(BiomeSettingsSource);
        if (biome == null || DiagnosticGridBiomeLayout == null)
            return biome;

        DiagnosticGridBiomeLayoutDto layout = DiagnosticGridBiomeLayout.ToDto(biome.Registry);
        return layout != null
            ? biome with { AssignmentMode = BiomeAssignmentMode.DiagnosticGrid, DiagnosticGridLayout = layout }
            : biome;
    }
}
