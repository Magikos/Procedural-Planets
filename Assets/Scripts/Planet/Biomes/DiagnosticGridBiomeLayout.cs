using UnityEngine;

public enum DiagnosticGridBiomeFace
{
    Top = 0,
    Bottom = 1,
    Left = 2,
    Right = 3,
    Front = 4,
    Back = 5,
}

public sealed record DiagnosticGridBiomeLayoutDto(
    int Face,
    int Columns,
    int Rows,
    float BlendWidth,
    byte[] Cells,
    byte FallbackBiome);

[CreateAssetMenu(menuName = "Planet/Settings/Diagnostic Grid Biome Layout")]
public class DiagnosticGridBiomeLayout : ScriptableObject
{
    [Range(0, 5)] public DiagnosticGridBiomeFace Face = DiagnosticGridBiomeFace.Front;
    [Range(1, 16)] public int Columns = 5;
    [Range(1, 16)] public int Rows = 5;
    [Range(0f, 0.5f)] public float BlendWidth = 0.04f;
    public BiomeType FallbackBiome = BiomeType.Grassland;
    public BiomeType[] Cells;

    public DiagnosticGridBiomeLayoutDto ToDto(BiomeRegistryDto registry)
    {
        if (registry == null)
            return null;

        int columns = Mathf.Clamp(Columns, 1, 16);
        int rows = Mathf.Clamp(Rows, 1, 16);
        int count = columns * rows;
        var ids = new byte[count];
        byte fallback = registry.GetSliceIdForBiomeType(FallbackBiome);

        for (int i = 0; i < count; i++)
            ids[i] = ResolveBiomeId(registry, GetCellType(i, fallbackType: FallbackBiome), fallback);

        return new DiagnosticGridBiomeLayoutDto(
            Mathf.Clamp((int)Face, 0, 5),
            columns,
            rows,
            Mathf.Clamp01(BlendWidth),
            ids,
            fallback);
    }

    BiomeType GetCellType(int index, BiomeType fallbackType)
    {
        if (Cells == null || index < 0 || index >= Cells.Length)
            return fallbackType;

        return Cells[index];
    }

    static byte ResolveBiomeId(BiomeRegistryDto registry, BiomeType type, byte fallback)
    {
        return registry.GetDefinition(type) != null
            ? registry.GetSliceIdForBiomeType(type)
            : fallback;
    }

    void OnValidate()
    {
        Columns = Mathf.Clamp(Columns, 1, 16);
        Rows = Mathf.Clamp(Rows, 1, 16);
        BlendWidth = Mathf.Clamp01(BlendWidth);
        int count = Columns * Rows;
        if (Cells == null || Cells.Length != count)
        {
            int oldLength = Cells?.Length ?? 0;
            System.Array.Resize(ref Cells, count);
            for (int i = oldLength; i < Cells.Length; i++)
                Cells[i] = FallbackBiome;
        }
    }
}
