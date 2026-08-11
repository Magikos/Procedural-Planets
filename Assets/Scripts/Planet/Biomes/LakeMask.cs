using System.Collections.Generic;
using UnityEngine;

// Global lake-vs-ocean classification, sampled by direction. The biome bake is per-chunk and only knows a
// local elevation threshold (elevation < OceanThreshold = water), so it can't tell a small inland lake from
// the ocean. This builds a coarse whole-sphere mask once per world gen: sample ground elevation over a
// per-face grid, flood-fill the below-water cells, and tag SMALL connected bodies as lake (+ a shore ring
// on the surrounding land). The biome resolvers query Sample(direction) and override Ocean->Lake /
// Beach->LakeShore where it returns non-zero.
//
// Self-limiting by design: only components SMALLER than LakeMaxCells are tagged, so the huge ocean is never
// mis-tagged. If the elevation sampling is off, the worst case is "no lakes detected" (every cell reads the
// same wetness -> one big component -> not small -> not a lake), never a broken ocean.
public sealed class LakeMask
{
    public const byte None = 0;
    public const byte Water = 1;  // small water body surface -> Lake biome
    public const byte Shore = 2;  // land ring around a lake -> LakeShore biome

    // Whatever the active world built; the bake path reads this (null => no override, current behaviour).
    public static LakeMask Current;

    const int Res = 192;                 // cells per face axis (192^2 * 6 = ~221k elevation samples at gen)
    const int FaceCells = Res * Res;
    const int LakeMaxCells = 1400;       // a below-water body smaller than this (per face) is a lake, not ocean
    const int ShoreRings = 2;            // land cells within this many steps of lake water become LakeShore

    readonly byte[] _mask = new byte[FaceCells * 6];

    // Builds the mask by sampling ground elevation over the 6 cube faces. oceanThreshold is the same
    // BiomeConstants.OceanThreshold the resolvers use; baseRadiusLocal converts sampled radius to elevation
    // (elevation = radius / baseRadiusLocal - 1), matching ScatterField / ScatterBiomePrecompute.
    public static LakeMask Build(ISurfaceGroundSampler ground, float baseRadiusLocal, float oceanThreshold)
    {
        if (ground == null || baseRadiusLocal <= 0f) return null;
        var m = new LakeMask();
        for (int face = 0; face < 6; face++)
            m.BuildFace(face, ground, baseRadiusLocal, oceanThreshold);
        return m;
    }

    void BuildFace(int face, ISurfaceGroundSampler ground, float baseRadiusLocal, float oceanThreshold)
    {
        int baseIdx = face * FaceCells;
        var wet = new bool[FaceCells];
        for (int y = 0; y < Res; y++)
        for (int x = 0; x < Res; x++)
        {
            Vector2 uv = new Vector2((x + 0.5f) / Res, (y + 0.5f) / Res);
            Vector3 dir = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(face, uv);
            float elev = ground.TrySampleRadius(dir, out float rad) ? rad / baseRadiusLocal - 1f : 1f;
            wet[y * Res + x] = elev < oceanThreshold;
        }

        // 4-neighbour flood-fill; small below-water components -> lake water.
        var visited = new bool[FaceCells];
        var stack = new Stack<int>(256);
        var component = new List<int>(512);
        for (int start = 0; start < FaceCells; start++)
        {
            if (!wet[start] || visited[start]) continue;
            component.Clear();
            stack.Push(start);
            visited[start] = true;
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                component.Add(c);
                int cx = c % Res, cy = c / Res;
                if (cx > 0) Push(cx - 1, cy);
                if (cx < Res - 1) Push(cx + 1, cy);
                if (cy > 0) Push(cx, cy - 1);
                if (cy < Res - 1) Push(cx, cy + 1);
            }
            if (component.Count < LakeMaxCells)
                foreach (int c in component)
                    _mask[baseIdx + c] = Water;

            void Push(int nx, int ny)
            {
                int ni = ny * Res + nx;
                if (wet[ni] && !visited[ni]) { visited[ni] = true; stack.Push(ni); }
            }
        }

        // Dilate lake water onto the surrounding dry land (exactly ShoreRings steps) -> LakeShore. Each ring
        // is computed against the PRE-ring mask into a to-mark list, then applied — writing into _mask while
        // HasNeighbor reads it would let the shore flood across the whole scan in a single pass.
        var toMark = new List<int>(256);
        for (int ring = 0; ring < ShoreRings; ring++)
        {
            byte target = (byte)(ring == 0 ? Water : Shore);
            toMark.Clear();
            for (int y = 0; y < Res; y++)
            for (int x = 0; x < Res; x++)
            {
                int i = y * Res + x;
                if (_mask[baseIdx + i] != None || wet[i]) continue; // only unclaimed dry land
                if (HasNeighbor(baseIdx, x, y, target))
                    toMark.Add(i);
            }
            foreach (int i in toMark)
                _mask[baseIdx + i] = Shore;
        }
    }

    bool HasNeighbor(int baseIdx, int x, int y, byte value)
    {
        if (x > 0 && _mask[baseIdx + y * Res + (x - 1)] == value) return true;
        if (x < Res - 1 && _mask[baseIdx + y * Res + (x + 1)] == value) return true;
        if (y > 0 && _mask[baseIdx + (y - 1) * Res + x] == value) return true;
        if (y < Res - 1 && _mask[baseIdx + (y + 1) * Res + x] == value) return true;
        return false;
    }

    // Lake state at a world/local unit direction. Read-only after Build, safe from parallel bake threads.
    public byte Sample(Vector3 direction)
    {
        FaceSpaceCellRangeBuilder.DirectionToFaceUv(direction, out int face, out Vector2 uv);
        int x = Mathf.Clamp((int)(uv.x * Res), 0, Res - 1);
        int y = Mathf.Clamp((int)(uv.y * Res), 0, Res - 1);
        return _mask[face * FaceCells + y * Res + x];
    }
}
