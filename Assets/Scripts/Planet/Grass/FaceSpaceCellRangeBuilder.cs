using UnityEngine;

// Reusable cube-face cell-range builder for camera-centered grass dispatches. Used by
// GrassNearFieldController. Multi-face dispatch handles the cube-face seam case: when
// the visible disc straddles a face edge, we add the neighbor face's overlapping region
// as a separate FaceSpaceCell entry.
//
// Output convention: `BuildRanges` returns 1-5 entries:
//   - outRanges[0] = primary face (always present)
//   - outRanges[1..N] = adjacent faces (only when the disc overflows an edge)
//
// Corner straddling (3 simultaneous faces from a cube corner) is intentionally NOT
// handled in this slice. It is surfaced via `SeamRisk` on the primary range when
// uncovered straddling is detected.
public readonly struct FaceSpaceCell
{
    public readonly int FaceIndex;
    public readonly Vector2Int PageOriginCellUV;
    public readonly Vector2Int GridSize;
    public readonly float CellUvWidth;

    public FaceSpaceCell(int faceIndex, Vector2Int pageOriginCellUV, Vector2Int gridSize, float cellUvWidth)
    {
        FaceIndex = faceIndex;
        PageOriginCellUV = pageOriginCellUV;
        GridSize = gridSize;
        CellUvWidth = cellUvWidth;
    }
}

public readonly struct FaceSpaceRangeResult
{
    public readonly int Count;
    public readonly bool UncoveredCornerStraddle; // true if 3+ faces are involved but only 2 were emitted

    public FaceSpaceRangeResult(int count, bool uncoveredCornerStraddle)
    {
        Count = count;
        UncoveredCornerStraddle = uncoveredCornerStraddle;
    }
}

public static class FaceSpaceCellRangeBuilder
{
    public const int MaxRanges = 5; // primary + up to 4 edge neighbors

    /// <summary>
    /// Computes the cube-face cell ranges covering a disc of `worldRadius` around the
    /// camera's surface anchor. `cellUvWidth` is **fixed at controller construction**
    /// (not derived per-tick) so the same face/cell indices always map to the same world
    /// position. Snapped to `pageCellSize` cell pages so dispatches don't fire on
    /// sub-cell camera motion.
    /// </summary>
    /// <param name="cellUvWidth">Stable cell width in face-UV units, fixed at construction.</param>
    /// <param name="pageCellSize">Number of cells per page (re-dispatch granularity).</param>
    /// <param name="outRanges">Caller-allocated buffer of at least MaxRanges entries.</param>
    /// <returns>Count of valid entries in outRanges, plus an uncovered-corner-straddle flag.</returns>
    public static FaceSpaceRangeResult BuildRanges(
        Camera camera,
        Transform planetTransform,
        float planetRadius,
        float worldRadius,
        float cellUvWidth,
        int pageCellSize,
        FaceSpaceCell[] outRanges)
    {
        int count = 0;
        bool uncoveredCornerStraddle = false;

        Vector3 cameraPos = camera.transform.position;
        Vector3 planetCenter = planetTransform.position;
        Vector3 toCamera = cameraPos - planetCenter;
        if (toCamera.sqrMagnitude < 1e-4f)
            return new FaceSpaceRangeResult(0, false);
        Vector3 dir = toCamera.normalized;

        DirectionToFaceUv(dir, out int primaryFace, out Vector2 primaryFaceUv);

        // Cell width is fixed; disc radius is local (since one UV unit covers different
        // world distances in different regions of a cube face).
        float planetWorldRadius = planetRadius * GetUniformWorldScale(planetTransform);
        float metersPerUV = ComputeMetersPerUV(primaryFace, primaryFaceUv, planetWorldRadius);
        float discRadiusUV = worldRadius / metersPerUV;
        pageCellSize = Mathf.Max(1, pageCellSize);

        // 1. Primary face range — square centered on camera projection.
        int centerCellU = Mathf.FloorToInt(primaryFaceUv.x / cellUvWidth);
        int centerCellV = Mathf.FloorToInt(primaryFaceUv.y / cellUvWidth);
        int halfExtent = Mathf.Max(1, Mathf.CeilToInt(discRadiusUV / cellUvWidth));
        outRanges[count++] = BuildPagedCell(primaryFace, cellUvWidth, pageCellSize,
            centerCellU - halfExtent, centerCellV - halfExtent,
            centerCellU + halfExtent, centerCellV + halfExtent);

        // 2. For each edge, if the disc overflows past it, add the neighbor face range.
        //    distToEdge < discRadiusUV => disc straddles that edge.
        int edgesOverflowing = 0;
        for (int edgeIdx = 0; edgeIdx < 4; edgeIdx++)
        {
            CubeEdge edge = (CubeEdge)edgeIdx;
            float distToEdge = DistanceFromUvToEdge(primaryFaceUv, edge);
            if (distToEdge >= discRadiusUV) continue;
            edgesOverflowing++;
            if (count >= outRanges.Length) continue;

            float overflowAmount = discRadiusUV - distToEdge;
            Vector2 edgeAnchorSrc = ClosestPointOnEdge(primaryFaceUv, edge);
            if (!CubeFaceTopology.TryMirrorUv(edgeAnchorSrc, primaryFace, edge,
                    out int neighborFace, out Vector2 edgeAnchorN))
                continue;

            var nNeighbor = CubeFaceTopology.GetNeighbor(primaryFace, edge);
            outRanges[count++] = BuildNeighborRange(neighborFace, edgeAnchorN, nNeighbor.NeighborEdge,
                discRadiusUV, overflowAmount, cellUvWidth, pageCellSize);
        }

        // Corner straddle detection: more than 2 edges overflowing means the disc reaches
        // a cube corner, where 3 faces meet. Not handled in this slice; surface to caller.
        uncoveredCornerStraddle = edgesOverflowing >= 2 && IsCornerStraddle(primaryFaceUv, discRadiusUV);

        return new FaceSpaceRangeResult(count, uncoveredCornerStraddle);
    }

    // Scatter's entry point. Deliberately a self-contained copy of BuildRanges rather than a shared
    // helper, so the grass Camera overload above stays byte-identical (grass placement is hand-tuned;
    // an extraction that touched its path would need a before/after grass capture gate). The only
    // difference: the observer direction is mapped into the PLANET-LOCAL frame before DirectionToFaceUv,
    // so a rotated planet enumerates the correct face. Takes a PlanetTransformSnapshot rather than the
    // live Transform so scatter can build ranges on a background thread. Unifying the two is a later
    // cleanup, not SP1.
    public static FaceSpaceRangeResult BuildRangesLocal(
        Vector3 cameraPos,
        in PlanetTransformSnapshot planet,
        float planetRadius,
        float worldRadius,
        float cellUvWidth,
        int pageCellSize,
        FaceSpaceCell[] outRanges)
    {
        int count = 0;
        bool uncoveredCornerStraddle = false;

        Vector3 toCamera = cameraPos - planet.Center;
        if (toCamera.sqrMagnitude < 1e-4f)
            return new FaceSpaceRangeResult(0, false);
        Vector3 localDir = planet.InverseTransformDirection(toCamera).normalized;

        DirectionToFaceUv(localDir, out int primaryFace, out Vector2 primaryFaceUv);

        float planetWorldRadius = planetRadius * planet.UniformScale;
        float metersPerUV = ComputeMetersPerUV(primaryFace, primaryFaceUv, planetWorldRadius);
        float discRadiusUV = worldRadius / metersPerUV;
        pageCellSize = Mathf.Max(1, pageCellSize);

        int centerCellU = Mathf.FloorToInt(primaryFaceUv.x / cellUvWidth);
        int centerCellV = Mathf.FloorToInt(primaryFaceUv.y / cellUvWidth);
        int halfExtent = Mathf.Max(1, Mathf.CeilToInt(discRadiusUV / cellUvWidth));
        outRanges[count++] = BuildPagedCell(primaryFace, cellUvWidth, pageCellSize,
            centerCellU - halfExtent, centerCellV - halfExtent,
            centerCellU + halfExtent, centerCellV + halfExtent);

        int edgesOverflowing = 0;
        for (int edgeIdx = 0; edgeIdx < 4; edgeIdx++)
        {
            CubeEdge edge = (CubeEdge)edgeIdx;
            float distToEdge = DistanceFromUvToEdge(primaryFaceUv, edge);
            if (distToEdge >= discRadiusUV) continue;
            edgesOverflowing++;
            if (count >= outRanges.Length) continue;

            float overflowAmount = discRadiusUV - distToEdge;
            Vector2 edgeAnchorSrc = ClosestPointOnEdge(primaryFaceUv, edge);
            if (!CubeFaceTopology.TryMirrorUv(edgeAnchorSrc, primaryFace, edge,
                    out int neighborFace, out Vector2 edgeAnchorN))
                continue;

            var nNeighbor = CubeFaceTopology.GetNeighbor(primaryFace, edge);
            outRanges[count++] = BuildNeighborRange(neighborFace, edgeAnchorN, nNeighbor.NeighborEdge,
                discRadiusUV, overflowAmount, cellUvWidth, pageCellSize);
        }

        uncoveredCornerStraddle = edgesOverflowing >= 2 && IsCornerStraddle(primaryFaceUv, discRadiusUV);

        return new FaceSpaceRangeResult(count, uncoveredCornerStraddle);
    }

    static FaceSpaceCell BuildPagedCell(int face, float cellUvWidth, int pageCellSize,
        int rawStartU, int rawStartV, int rawEndU, int rawEndV)
    {
        int faceCellCount = Mathf.Max(1, Mathf.CeilToInt(1f / Mathf.Max(cellUvWidth, 1e-8f)));
        rawStartU = Mathf.Clamp(rawStartU, 0, faceCellCount);
        rawStartV = Mathf.Clamp(rawStartV, 0, faceCellCount);
        rawEndU = Mathf.Clamp(rawEndU, 0, faceCellCount);
        rawEndV = Mathf.Clamp(rawEndV, 0, faceCellCount);

        if (rawEndU <= rawStartU || rawEndV <= rawStartV)
            return new FaceSpaceCell(face, new Vector2Int(rawStartU, rawStartV), Vector2Int.zero, cellUvWidth);

        int pagedStartU = Mathf.Clamp(FloorDivToMultiple(rawStartU, pageCellSize), 0, faceCellCount);
        int pagedStartV = Mathf.Clamp(FloorDivToMultiple(rawStartV, pageCellSize), 0, faceCellCount);
        int pagedEndU = Mathf.Clamp(CeilDivToMultiple(rawEndU, pageCellSize), 0, faceCellCount);
        int pagedEndV = Mathf.Clamp(CeilDivToMultiple(rawEndV, pageCellSize), 0, faceCellCount);
        int gridW = Mathf.Max(0, pagedEndU - pagedStartU);
        int gridH = Mathf.Max(0, pagedEndV - pagedStartV);
        return new FaceSpaceCell(face,
            new Vector2Int(pagedStartU, pagedStartV),
            new Vector2Int(gridW, gridH),
            cellUvWidth);
    }

    // Build a neighbor-face range. The neighbor's `entryEdge` defines which way is
    // "into the face" (perpendicular) vs "along the shared edge". The range is a tight
    // rectangle covering only the overflow strip - we never dispatch cells past the
    // overflow depth on the neighbor side because they'd be farther from the camera than
    // the disc radius anyway.
    static FaceSpaceCell BuildNeighborRange(int neighborFace, Vector2 edgeAnchorN, CubeEdge entryEdge,
        float discRadiusUV, float overflowAmount, float cellUvWidth, int pageCellSize)
    {
        // perpDir = unit vector pointing into the neighbor face from the entry edge
        // alongDir = unit vector parallel to the entry edge (positive direction)
        Vector2 perpDir;
        Vector2 alongDir;
        switch (entryEdge)
        {
            case CubeEdge.East:  perpDir = new Vector2(-1f, 0f); alongDir = new Vector2(0f, 1f); break;
            case CubeEdge.West:  perpDir = new Vector2( 1f, 0f); alongDir = new Vector2(0f, 1f); break;
            case CubeEdge.North: perpDir = new Vector2( 0f, 1f); alongDir = new Vector2(1f, 0f); break;
            case CubeEdge.South: perpDir = new Vector2( 0f,-1f); alongDir = new Vector2(1f, 0f); break;
            default:             perpDir = Vector2.zero;          alongDir = Vector2.zero;        break;
        }

        // Centroid of overflow strip = edge entry point pushed inward by half the overflow depth.
        Vector2 centerN = edgeAnchorN + perpDir * (overflowAmount * 0.5f);
        Vector2 halfExtentUV = new Vector2(
            Mathf.Abs(alongDir.x) * discRadiusUV + Mathf.Abs(perpDir.x) * (overflowAmount * 0.5f),
            Mathf.Abs(alongDir.y) * discRadiusUV + Mathf.Abs(perpDir.y) * (overflowAmount * 0.5f));

        Vector2 minUV = centerN - halfExtentUV;
        Vector2 maxUV = centerN + halfExtentUV;
        int rawStartU = Mathf.FloorToInt(minUV.x / cellUvWidth);
        int rawStartV = Mathf.FloorToInt(minUV.y / cellUvWidth);
        int rawEndU = Mathf.CeilToInt(maxUV.x / cellUvWidth);
        int rawEndV = Mathf.CeilToInt(maxUV.y / cellUvWidth);
        return BuildPagedCell(neighborFace, cellUvWidth, pageCellSize, rawStartU, rawStartV, rawEndU, rawEndV);
    }

    // Inverse of CubeFaceToUnitSphere. Mirrors HLSL DirectionToFaceUv used by the compute.
    public static void DirectionToFaceUv(Vector3 dir, out int face, out Vector2 uv)
    {
        float ax = Mathf.Abs(dir.x);
        float ay = Mathf.Abs(dir.y);
        float az = Mathf.Abs(dir.z);
        float uSigned, vSigned;

        if (ay >= ax && ay >= az)
        {
            Vector3 pc = dir / Mathf.Max(ay, 1e-8f);
            if (dir.y >= 0f) { face = 0; uSigned = pc.x; vSigned = -pc.z; }
            else { face = 1; uSigned = -pc.x; vSigned = -pc.z; }
        }
        else if (ax >= ay && ax >= az)
        {
            Vector3 pc = dir / Mathf.Max(ax, 1e-8f);
            if (dir.x >= 0f) { face = 3; uSigned = pc.z; vSigned = -pc.y; }
            else { face = 2; uSigned = -pc.z; vSigned = -pc.y; }
        }
        else
        {
            Vector3 pc = dir / Mathf.Max(az, 1e-8f);
            if (dir.z >= 0f) { face = 4; uSigned = pc.y; vSigned = -pc.x; }
            else { face = 5; uSigned = -pc.y; vSigned = -pc.x; }
        }

        uv = new Vector2(uSigned * 0.5f + 0.5f, vSigned * 0.5f + 0.5f);
    }

    // C# mirror of CubeFaceToUnitSphere in BiomeGrassPlace.compute / GrassNearFieldPlace.compute.
    public static Vector3 CubeFaceToUnitSphere(int face, Vector2 uv)
    {
        float u = uv.x * 2f - 1f;
        float v = uv.y * 2f - 1f;
        Vector3 p;
        switch (face)
        {
            case 0:  p = new Vector3(u, 1f, -v); break;
            case 1:  p = new Vector3(-u, -1f, -v); break;
            case 2:  p = new Vector3(-1f, -v, -u); break;
            case 3:  p = new Vector3(1f, -v, u); break;
            case 4:  p = new Vector3(-v, u, 1f); break;
            default: p = new Vector3(-v, -u, -1f); break;
        }
        return p.normalized;
    }

    public static float ComputeMetersPerUV(int face, Vector2 faceUv, float planetWorldRadius)
    {
        const float eps = 0.001f;
        Vector2 faceUvForEps = new Vector2(
            Mathf.Clamp(faceUv.x, eps, 1f - eps),
            Mathf.Clamp(faceUv.y, eps, 1f - eps));
        Vector3 centerWs = CubeFaceToUnitSphere(face, faceUvForEps) * planetWorldRadius;
        Vector3 atUWs = CubeFaceToUnitSphere(face, faceUvForEps + new Vector2(eps, 0f)) * planetWorldRadius;
        Vector3 atVWs = CubeFaceToUnitSphere(face, faceUvForEps + new Vector2(0f, eps)) * planetWorldRadius;
        float metersPerUV_u = (atUWs - centerWs).magnitude / eps;
        float metersPerUV_v = (atVWs - centerWs).magnitude / eps;
        // Conservative: smaller scale = denser cells; never sparser than spec near distortion zones.
        return Mathf.Max(0.0001f, Mathf.Min(metersPerUV_u, metersPerUV_v));
    }

    public static float GetUniformWorldScale(Transform transform)
    {
        if (transform == null) return 1f;
        Vector3 scale = transform.lossyScale;
        return Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
    }

    static float DistanceFromUvToEdge(Vector2 uv, CubeEdge edge)
    {
        switch (edge)
        {
            case CubeEdge.East:  return 1f - uv.x;
            case CubeEdge.West:  return uv.x;
            case CubeEdge.North: return uv.y;
            case CubeEdge.South: return 1f - uv.y;
            default: return float.MaxValue;
        }
    }

    static Vector2 ClosestPointOnEdge(Vector2 uv, CubeEdge edge)
    {
        switch (edge)
        {
            case CubeEdge.East:  return new Vector2(1f, Mathf.Clamp01(uv.y));
            case CubeEdge.West:  return new Vector2(0f, Mathf.Clamp01(uv.y));
            case CubeEdge.North: return new Vector2(Mathf.Clamp01(uv.x), 0f);
            case CubeEdge.South: return new Vector2(Mathf.Clamp01(uv.x), 1f);
            default: return uv;
        }
    }

    // True if the disc reaches a cube corner — distance to the nearest corner is less than
    // the disc radius. Used only for the SeamRisk flag; the algorithm proceeds anyway.
    static bool IsCornerStraddle(Vector2 uv, float discRadiusUV)
    {
        float dx = Mathf.Min(uv.x, 1f - uv.x);
        float dy = Mathf.Min(uv.y, 1f - uv.y);
        float cornerDist = Mathf.Sqrt(dx * dx + dy * dy);
        return cornerDist < discRadiusUV;
    }

    static int FloorDivToMultiple(int value, int multiple)
    {
        int q = value / multiple;
        if (value % multiple != 0 && (value ^ multiple) < 0) q -= 1;
        return q * multiple;
    }

    static int CeilDivToMultiple(int value, int multiple)
    {
        int q = value / multiple;
        if (value % multiple != 0 && (value ^ multiple) >= 0) q += 1;
        return q * multiple;
    }
}
