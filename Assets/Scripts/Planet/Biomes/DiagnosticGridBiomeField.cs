using UnityEngine;

sealed class DiagnosticGridBiomeField : IBiomeAssignmentField
{
    readonly DiagnosticGridBiomeLayoutDto _layout;
    readonly float _effectiveBlendWidth;

    public DiagnosticGridBiomeField(DiagnosticGridBiomeLayoutDto layout)
    {
        _layout = layout;
        float cellWidth = layout.Columns > 0 ? 1f / layout.Columns : 1f;
        float cellHeight = layout.Rows > 0 ? 1f / layout.Rows : 1f;
        _effectiveBlendWidth = Mathf.Min(
            Mathf.Clamp01(layout.BlendWidth),
            0.5f * Mathf.Min(cellWidth, cellHeight));
    }

    public BiomeAssignmentSample Evaluate(Vector3 pointOnUnitSphere)
    {
        if (!TryGetTargetFaceUv(pointOnUnitSphere, out Vector2 uv))
            return FallbackSample();

        int col = Mathf.Clamp(Mathf.FloorToInt(uv.x * _layout.Columns), 0, _layout.Columns - 1);
        int row = Mathf.Clamp(Mathf.FloorToInt(uv.y * _layout.Rows), 0, _layout.Rows - 1);
        byte primary = GetCell(col, row);

        if (_effectiveBlendWidth <= 0f)
            return new BiomeAssignmentSample(primary, primary, 0f);

        float uLeft = (float)col / _layout.Columns;
        float uRight = (float)(col + 1) / _layout.Columns;
        float vBottom = (float)row / _layout.Rows;
        float vTop = (float)(row + 1) / _layout.Rows;

        float dBottom = uv.y - vBottom;
        float dTop = vTop - uv.y;
        float dLeft = uv.x - uLeft;
        float dRight = uRight - uv.x;

        int neighborCol = col;
        int neighborRow = row;
        float minDist = dBottom;
        neighborRow = row - 1;

        if (dTop < minDist)
        {
            minDist = dTop;
            neighborCol = col;
            neighborRow = row + 1;
        }
        if (dLeft < minDist)
        {
            minDist = dLeft;
            neighborCol = col - 1;
            neighborRow = row;
        }
        if (dRight < minDist)
        {
            minDist = dRight;
            neighborCol = col + 1;
            neighborRow = row;
        }

        if (minDist >= _effectiveBlendWidth)
            return new BiomeAssignmentSample(primary, primary, 0f);

        byte secondary = GetCell(neighborCol, neighborRow);
        // ponytail: diagnostic grid caps at 50/50 so both sides of a cell seam meet at the same color.
        float secondaryWeight = 0.5f * (1f - Mathf.Clamp01(minDist / _effectiveBlendWidth));
        return new BiomeAssignmentSample(primary, secondary, secondaryWeight);
    }

    public byte EvaluatePrimaryId(Vector3 pointOnUnitSphere)
    {
        if (!TryGetTargetFaceUv(pointOnUnitSphere, out Vector2 uv))
            return _layout.FallbackBiome;

        int col = Mathf.Clamp(Mathf.FloorToInt(uv.x * _layout.Columns), 0, _layout.Columns - 1);
        int row = Mathf.Clamp(Mathf.FloorToInt(uv.y * _layout.Rows), 0, _layout.Rows - 1);
        return GetCell(col, row);
    }

    bool TryGetTargetFaceUv(Vector3 pointOnUnitSphere, out Vector2 uv)
    {
        CoordinateConverter.UnitSphereToCubeFaceUvExact(pointOnUnitSphere, out int face, out uv);
        return face == _layout.Face && _layout.Columns > 0 && _layout.Rows > 0 && _layout.Cells != null;
    }

    byte GetCell(int col, int row)
    {
        if (col < 0 || col >= _layout.Columns || row < 0 || row >= _layout.Rows)
            return _layout.FallbackBiome;

        int index = row * _layout.Columns + col;
        return _layout.Cells != null && index >= 0 && index < _layout.Cells.Length
            ? _layout.Cells[index]
            : _layout.FallbackBiome;
    }

    BiomeAssignmentSample FallbackSample() =>
        new BiomeAssignmentSample(_layout.FallbackBiome, _layout.FallbackBiome, 0f);
}
