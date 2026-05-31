using UnityEngine;

// Cube-face adjacency lookup for the chunked-planet quadtree (Phase A step 2).
//
// Cube faces use CoordinateConverter's convention:
//   0=Top(+Y), 1=Bottom(-Y), 2=Left(-X), 3=Right(+X), 4=Front(+Z), 5=Back(-Z)
//
// Per-face UV axes match TerrainFaceMeshJob / CubeFaceToUnitSphere:
//   axisA = (uy, uz, ux), axisB = cross(localUp, axisA)
//   pointOnCube = localUp + (2u-1)*axisA + (2v-1)*axisB
//
// Edges in UV space:
//   East  = u=1 edge (param = v)
//   West  = u=0 edge (param = v)
//   North = v=0 edge (param = u)
//   South = v=1 edge (param = u)
//
// The 24 entries below were derived by enumerating the 12 cube edges and identifying which
// edge of each face they sit on. Self-test asserts:
//   1. neighbor(neighbor(F, E)) == (F, E) — reflexive
//   2. EdgeParamReversed is consistent in both directions
//   3. MirrorUvAcrossEdge is involutive for points on the shared edge
public enum CubeEdge
{
    East = 0,
    West = 1,
    North = 2,
    South = 3
}

public readonly struct CubeFaceEdgeNeighbor
{
    public readonly int NeighborFace;
    public readonly CubeEdge NeighborEdge;
    // True if our edge param (0→1) maps to the neighbor's edge param (1→0).
    public readonly bool EdgeParamReversed;

    public CubeFaceEdgeNeighbor(int neighborFace, CubeEdge neighborEdge, bool reversed)
    {
        NeighborFace = neighborFace;
        NeighborEdge = neighborEdge;
        EdgeParamReversed = reversed;
    }
}

public static class CubeFaceTopology
{
    // 6 faces × 4 edges. Indexed by [face, (int)edge].
    static readonly CubeFaceEdgeNeighbor[,] _table = new CubeFaceEdgeNeighbor[6, 4]
    {
        // Face 0 (Top, +Y) — axisA=+X, axisB=-Z
        // E:u=1 is the +X side; corners (1,1,1)→(1,1,-1) — cube edge 2-6
        // W:u=0 is the -X side; corners (-1,1,1)→(-1,1,-1) — cube edge 3-7
        // N:v=0 is the +Z side; corners (-1,1,1)→(1,1,1) — cube edge 6-7
        // S:v=1 is the -Z side; corners (-1,1,-1)→(1,1,-1) — cube edge 2-3
        {
            /* E */ new CubeFaceEdgeNeighbor(3, CubeEdge.North, true),
            /* W */ new CubeFaceEdgeNeighbor(2, CubeEdge.North, false),
            /* N */ new CubeFaceEdgeNeighbor(4, CubeEdge.East,  true),
            /* S */ new CubeFaceEdgeNeighbor(5, CubeEdge.West,  true),
        },
        // Face 1 (Bottom, -Y) — axisA=-X, axisB=-Z
        {
            /* E */ new CubeFaceEdgeNeighbor(2, CubeEdge.South, false),
            /* W */ new CubeFaceEdgeNeighbor(3, CubeEdge.South, true),
            /* N */ new CubeFaceEdgeNeighbor(4, CubeEdge.West,  false),
            /* S */ new CubeFaceEdgeNeighbor(5, CubeEdge.East,  false),
        },
        // Face 2 (Left, -X) — axisA=-Z, axisB=-Y
        {
            /* E */ new CubeFaceEdgeNeighbor(5, CubeEdge.South, false),
            /* W */ new CubeFaceEdgeNeighbor(4, CubeEdge.South, true),
            /* N */ new CubeFaceEdgeNeighbor(0, CubeEdge.West,  false),
            /* S */ new CubeFaceEdgeNeighbor(1, CubeEdge.East,  false),
        },
        // Face 3 (Right, +X) — axisA=+Z, axisB=-Y
        {
            /* E */ new CubeFaceEdgeNeighbor(4, CubeEdge.North, true),
            /* W */ new CubeFaceEdgeNeighbor(5, CubeEdge.North, false),
            /* N */ new CubeFaceEdgeNeighbor(0, CubeEdge.East,  true),
            /* S */ new CubeFaceEdgeNeighbor(1, CubeEdge.West,  true),
        },
        // Face 4 (Front, +Z) — axisA=+Y, axisB=-X
        {
            /* E */ new CubeFaceEdgeNeighbor(0, CubeEdge.North, true),
            /* W */ new CubeFaceEdgeNeighbor(1, CubeEdge.North, false),
            /* N */ new CubeFaceEdgeNeighbor(3, CubeEdge.East,  true),
            /* S */ new CubeFaceEdgeNeighbor(2, CubeEdge.West,  true),
        },
        // Face 5 (Back, -Z) — axisA=-Y, axisB=-X
        {
            /* E */ new CubeFaceEdgeNeighbor(1, CubeEdge.South, false),
            /* W */ new CubeFaceEdgeNeighbor(0, CubeEdge.South, true),
            /* N */ new CubeFaceEdgeNeighbor(3, CubeEdge.West,  false),
            /* S */ new CubeFaceEdgeNeighbor(2, CubeEdge.East,  false),
        },
    };

    public static CubeFaceEdgeNeighbor GetNeighbor(int face, CubeEdge edge)
    {
        return _table[face, (int)edge];
    }

    // Reflects a point inside face `fromFace` across `fromEdge` and returns its UV on the
    // neighbor face. The depth into the source face (perpendicular distance from the edge)
    // becomes the depth into the neighbor face from its entry edge; the param along the shared
    // edge maps directly (or reversed, per the topology entry).
    //
    // Returns false if `fromFace` or `fromEdge` are out of range.
    public static bool TryMirrorUv(Vector2 sourceUv, int fromFace, CubeEdge fromEdge,
        out int neighborFace, out Vector2 neighborUv)
    {
        neighborFace = -1;
        neighborUv = default;
        if (fromFace < 0 || fromFace >= 6) return false;

        var n = _table[fromFace, (int)fromEdge];
        neighborFace = n.NeighborFace;

        // (s) = param along the shared edge from the source's perspective.
        // (d) = depth from the source edge into the source's interior (positive in [0, 1]).
        float s, d;
        switch (fromEdge)
        {
            case CubeEdge.East:  s = sourceUv.y; d = 1f - sourceUv.x; break;
            case CubeEdge.West:  s = sourceUv.y; d = sourceUv.x;       break;
            case CubeEdge.North: s = sourceUv.x; d = sourceUv.y;       break;
            case CubeEdge.South: s = sourceUv.x; d = 1f - sourceUv.y;  break;
            default: return false;
        }

        // Neighbor's edge param, with reversal applied.
        float sNeighbor = n.EdgeParamReversed ? (1f - s) : s;

        // Reconstruct (u, v) on the neighbor from (sNeighbor, d) given which edge of the
        // neighbor is the entry edge.
        switch (n.NeighborEdge)
        {
            case CubeEdge.East:  neighborUv = new Vector2(1f - d, sNeighbor); return true;
            case CubeEdge.West:  neighborUv = new Vector2(d,       sNeighbor); return true;
            case CubeEdge.North: neighborUv = new Vector2(sNeighbor, d);       return true;
            case CubeEdge.South: neighborUv = new Vector2(sNeighbor, 1f - d);  return true;
            default: return false;
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RunSelfTestAtStartup()
    {
        if (!TryRunSelfTest(out string error))
            Debug.LogError($"[CubeFaceTopology] Self-test FAILED: {error}");
    }

    // Returns true if the table is internally consistent. Sets `error` to a description of
    // the first failure otherwise. Public so editor tools / tests can call it explicitly.
    public static bool TryRunSelfTest(out string error)
    {
        error = null;

        for (int face = 0; face < 6; face++)
        {
            for (int edgeIdx = 0; edgeIdx < 4; edgeIdx++)
            {
                var edge = (CubeEdge)edgeIdx;
                var n = _table[face, edgeIdx];

                // 1) Neighbor face is a valid distinct face.
                if (n.NeighborFace < 0 || n.NeighborFace >= 6 || n.NeighborFace == face)
                {
                    error = $"face {face} edge {edge} → invalid neighbor face {n.NeighborFace}";
                    return false;
                }

                // 2) Round-trip: the neighbor's record of OUR edge points back at us.
                var back = _table[n.NeighborFace, (int)n.NeighborEdge];
                if (back.NeighborFace != face || back.NeighborEdge != edge)
                {
                    error = $"face {face} edge {edge} → ({n.NeighborFace}, {n.NeighborEdge}), but reverse goes to ({back.NeighborFace}, {back.NeighborEdge})";
                    return false;
                }
                if (back.EdgeParamReversed != n.EdgeParamReversed)
                {
                    error = $"face {face} edge {edge} reversal flag disagrees with neighbor ({n.NeighborFace}, {n.NeighborEdge})";
                    return false;
                }

                // 3) MirrorUv is involutive when applied to a point exactly on the edge.
                Vector2 onEdge = EdgeMidpointUv(edge);
                if (!TryMirrorUv(onEdge, face, edge, out int nf, out Vector2 mirroredUv))
                {
                    error = $"face {face} edge {edge} mirror returned false on edge midpoint";
                    return false;
                }
                if (!TryMirrorUv(mirroredUv, nf, n.NeighborEdge, out int rf, out Vector2 roundTrip))
                {
                    error = $"face {face} edge {edge} reverse mirror returned false";
                    return false;
                }
                if (rf != face)
                {
                    error = $"face {face} edge {edge} mirror→mirror landed on face {rf}, expected {face}";
                    return false;
                }
                if (Vector2.Distance(roundTrip, onEdge) > 1e-4f)
                {
                    error = $"face {face} edge {edge} mirror→mirror landed at {roundTrip}, expected {onEdge}";
                    return false;
                }

                // 4) MirrorUv maps a point on the edge to a point on the corresponding
                //    neighbor edge (depth from neighbor edge should be ~0).
                float depthOnNeighbor = DepthFromEdge(mirroredUv, n.NeighborEdge);
                if (Mathf.Abs(depthOnNeighbor) > 1e-4f)
                {
                    error = $"face {face} edge {edge} mirror placed edge point at depth {depthOnNeighbor} from {n.NeighborEdge}";
                    return false;
                }
            }
        }

        return true;
    }

    static Vector2 EdgeMidpointUv(CubeEdge edge)
    {
        return edge switch
        {
            CubeEdge.East  => new Vector2(1f, 0.5f),
            CubeEdge.West  => new Vector2(0f, 0.5f),
            CubeEdge.North => new Vector2(0.5f, 0f),
            CubeEdge.South => new Vector2(0.5f, 1f),
            _ => Vector2.zero,
        };
    }

    static float DepthFromEdge(Vector2 uv, CubeEdge edge)
    {
        return edge switch
        {
            CubeEdge.East  => 1f - uv.x,
            CubeEdge.West  => uv.x,
            CubeEdge.North => uv.y,
            CubeEdge.South => 1f - uv.y,
            _ => 0f,
        };
    }
#endif
}
