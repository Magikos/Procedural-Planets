using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public enum DiagnosticTerrainFace
{
    Top = 0,
    Bottom = 1,
    Left = 2,
    Right = 3,
    Front = 4,
    Back = 5,
}

public enum DiagnosticTerrainCell
{
    Flat = 0,
    Water = 1,
    Beach = 2,
    Hill = 3,
    Mountain = 4,
    Valley = 5,
}

public sealed record DiagnosticTerrainLayoutDto(
    int Face,
    int Columns,
    int Rows,
    float BlendWidth,
    byte[] Cells,
    byte FallbackCell);

public struct DiagnosticTerrainSettingsData
{
    public int Enabled;
    public int Face;
    public int Columns;
    public int Rows;
    public float BlendWidth;
    public byte FallbackCell;
}

[CreateAssetMenu(menuName = "Planet/Settings/Diagnostic Terrain Layout")]
public class DiagnosticTerrainLayout : ScriptableObject
{
    [Range(0, 5)] public DiagnosticTerrainFace Face = DiagnosticTerrainFace.Back;
    [Range(1, 16)] public int Columns = 5;
    [Range(1, 16)] public int Rows = 5;
    [Range(0f, 0.5f)] public float BlendWidth = 0.02f;
    public DiagnosticTerrainCell FallbackCell = DiagnosticTerrainCell.Flat;
    public DiagnosticTerrainCell[] Cells;

    public DiagnosticTerrainLayoutDto ToDto()
    {
        int columns = Mathf.Clamp(Columns, 1, 16);
        int rows = Mathf.Clamp(Rows, 1, 16);
        int count = columns * rows;
        var cells = new byte[count];
        byte fallback = (byte)FallbackCell;

        for (int i = 0; i < count; i++)
            cells[i] = (byte)GetCell(i, FallbackCell);

        return new DiagnosticTerrainLayoutDto(
            Mathf.Clamp((int)Face, 0, 5),
            columns,
            rows,
            Mathf.Clamp01(BlendWidth),
            cells,
            fallback);
    }

    DiagnosticTerrainCell GetCell(int index, DiagnosticTerrainCell fallback)
    {
        if (Cells == null || index < 0 || index >= Cells.Length)
            return fallback;

        return Cells[index];
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
                Cells[i] = FallbackCell;
        }
    }
}

public static class DiagnosticTerrainEvaluator
{
    public static DiagnosticTerrainSettingsData ToData(DiagnosticTerrainLayoutDto layout)
    {
        if (layout == null || layout.Cells == null || layout.Cells.Length == 0)
            return default;

        int columns = math.clamp(layout.Columns, 1, 16);
        int rows = math.clamp(layout.Rows, 1, 16);
        float cellWidth = 1f / columns;
        float cellHeight = 1f / rows;
        float blendWidth = math.min(math.saturate(layout.BlendWidth), 0.5f * math.min(cellWidth, cellHeight));

        return new DiagnosticTerrainSettingsData
        {
            Enabled = 1,
            Face = math.clamp(layout.Face, 0, 5),
            Columns = columns,
            Rows = rows,
            BlendWidth = blendWidth,
            FallbackCell = layout.FallbackCell,
        };
    }

    public static float Evaluate(float3 point, DiagnosticTerrainSettingsData settings, NativeArray<byte> cells)
    {
        if (settings.Enabled == 0 || cells.Length == 0)
            return 0f;

        return TryGetTargetFaceUv(point, settings.Face, out float2 uv)
            ? EvaluateUv(uv, settings, cells)
            : CellElevation(settings.FallbackCell, new float2(0.5f, 0.5f));
    }

    public static float Evaluate(float3 point, DiagnosticTerrainSettingsData settings, byte[] cells)
    {
        if (settings.Enabled == 0 || cells == null || cells.Length == 0)
            return 0f;

        return TryGetTargetFaceUv(point, settings.Face, out float2 uv)
            ? EvaluateUv(uv, settings, cells)
            : CellElevation(settings.FallbackCell, new float2(0.5f, 0.5f));
    }

    static float EvaluateUv(float2 uv, DiagnosticTerrainSettingsData settings, NativeArray<byte> cells)
    {
        int col = math.clamp((int)math.floor(uv.x * settings.Columns), 0, settings.Columns - 1);
        int row = math.clamp((int)math.floor(uv.y * settings.Rows), 0, settings.Rows - 1);
        float primary = CellElevation(GetCell(col, row, settings, cells), CellUv(uv, col, row, settings));

        if (settings.BlendWidth <= 0f)
            return primary;

        FindNearestNeighbor(uv, col, row, settings, out int neighborCol, out int neighborRow, out float minDist);
        if (minDist >= settings.BlendWidth)
            return primary;

        float secondary = CellElevation(
            GetCell(neighborCol, neighborRow, settings, cells),
            CellUv(uv, neighborCol, neighborRow, settings));
        float t = 0.5f * (1f - math.saturate(minDist / settings.BlendWidth));
        return math.lerp(primary, secondary, t);
    }

    static float EvaluateUv(float2 uv, DiagnosticTerrainSettingsData settings, byte[] cells)
    {
        int col = math.clamp((int)math.floor(uv.x * settings.Columns), 0, settings.Columns - 1);
        int row = math.clamp((int)math.floor(uv.y * settings.Rows), 0, settings.Rows - 1);
        float primary = CellElevation(GetCell(col, row, settings, cells), CellUv(uv, col, row, settings));

        if (settings.BlendWidth <= 0f)
            return primary;

        FindNearestNeighbor(uv, col, row, settings, out int neighborCol, out int neighborRow, out float minDist);
        if (minDist >= settings.BlendWidth)
            return primary;

        float secondary = CellElevation(
            GetCell(neighborCol, neighborRow, settings, cells),
            CellUv(uv, neighborCol, neighborRow, settings));
        float t = 0.5f * (1f - math.saturate(minDist / settings.BlendWidth));
        return math.lerp(primary, secondary, t);
    }

    static void FindNearestNeighbor(float2 uv, int col, int row, DiagnosticTerrainSettingsData settings,
        out int neighborCol, out int neighborRow, out float minDist)
    {
        float uLeft = (float)col / settings.Columns;
        float uRight = (float)(col + 1) / settings.Columns;
        float vBottom = (float)row / settings.Rows;
        float vTop = (float)(row + 1) / settings.Rows;

        minDist = uv.y - vBottom;
        neighborCol = col;
        neighborRow = row - 1;

        float dTop = vTop - uv.y;
        if (dTop < minDist)
        {
            minDist = dTop;
            neighborCol = col;
            neighborRow = row + 1;
        }

        float dLeft = uv.x - uLeft;
        if (dLeft < minDist)
        {
            minDist = dLeft;
            neighborCol = col - 1;
            neighborRow = row;
        }

        float dRight = uRight - uv.x;
        if (dRight < minDist)
        {
            minDist = dRight;
            neighborCol = col + 1;
            neighborRow = row;
        }
    }

    static float2 CellUv(float2 uv, int col, int row, DiagnosticTerrainSettingsData settings)
    {
        if (col < 0 || col >= settings.Columns || row < 0 || row >= settings.Rows)
            return new float2(0.5f, 0.5f);

        return math.saturate(new float2(uv.x * settings.Columns - col, uv.y * settings.Rows - row));
    }

    static byte GetCell(int col, int row, DiagnosticTerrainSettingsData settings, NativeArray<byte> cells)
    {
        if (col < 0 || col >= settings.Columns || row < 0 || row >= settings.Rows)
            return settings.FallbackCell;

        int index = row * settings.Columns + col;
        return index >= 0 && index < cells.Length ? cells[index] : settings.FallbackCell;
    }

    static byte GetCell(int col, int row, DiagnosticTerrainSettingsData settings, byte[] cells)
    {
        if (col < 0 || col >= settings.Columns || row < 0 || row >= settings.Rows)
            return settings.FallbackCell;

        int index = row * settings.Columns + col;
        return index >= 0 && index < cells.Length ? cells[index] : settings.FallbackCell;
    }

    static float CellElevation(byte cell, float2 uv)
    {
        float2 d = uv - new float2(0.5f, 0.5f);
        float bump = math.saturate(1f - math.dot(d, d) * 4f);
        bump = bump * bump * (3f - 2f * bump);

        switch ((DiagnosticTerrainCell)cell)
        {
            case DiagnosticTerrainCell.Water:
                return -0.07f;
            case DiagnosticTerrainCell.Beach:
                return -0.004f + 0.008f * uv.y;
            case DiagnosticTerrainCell.Hill:
                return 0.02f + 0.055f * bump;
            case DiagnosticTerrainCell.Mountain:
            {
                float peak = bump * bump;
                float ridge = math.saturate(1f - math.abs(d.x - d.y) * 3.2f);
                return 0.035f + 0.16f * peak + 0.035f * ridge * bump;
            }
            case DiagnosticTerrainCell.Valley:
                return 0.01f - 0.055f * bump;
            default:
                return 0.02f;
        }
    }

    static bool TryGetTargetFaceUv(float3 direction, int targetFace, out float2 uv)
    {
        UnitSphereToCubeFaceUvExact(direction, out int face, out uv);
        return face == targetFace;
    }

    static void UnitSphereToCubeFaceUvExact(float3 direction, out int face, out float2 uv)
    {
        direction = math.normalizesafe(direction, new float3(0f, 0f, 1f));
        float3 absDir = math.abs(direction);

        float3 localUp;
        if (absDir.y >= absDir.x && absDir.y >= absDir.z)
        {
            face = direction.y >= 0f ? 0 : 1;
            localUp = direction.y >= 0f ? new float3(0f, 1f, 0f) : new float3(0f, -1f, 0f);
        }
        else if (absDir.x >= absDir.y && absDir.x >= absDir.z)
        {
            face = direction.x >= 0f ? 3 : 2;
            localUp = direction.x >= 0f ? new float3(1f, 0f, 0f) : new float3(-1f, 0f, 0f);
        }
        else
        {
            face = direction.z >= 0f ? 4 : 5;
            localUp = direction.z >= 0f ? new float3(0f, 0f, 1f) : new float3(0f, 0f, -1f);
        }

        float3 axisA = new float3(localUp.y, localUp.z, localUp.x);
        float3 axisB = math.cross(localUp, axisA);
        float major = math.max(math.abs(math.dot(direction, localUp)), 0.00001f);
        float u = math.dot(direction, axisA) / major;
        float v = math.dot(direction, axisB) / major;
        uv = math.saturate(new float2(u * 0.5f + 0.5f, v * 0.5f + 0.5f));
    }
}
