using System.Collections.Generic;
using UnityEngine;

public enum WaterBodyKind : byte
{
    None = 0,
    Lake = 1,
    Ocean = 2,
}

// One connected below-water region on the sphere.
//
// SurfaceElevation is the level this body surface sits at. Every body currently reports the global ocean
// level because the water mesh is a single shell built from one threshold; a per-basin spill solve replaces
// the value later, and that is what lets a lake sit above sea level without moving the ocean.
public sealed record WaterBody(
    ushort Id,
    WaterBodyKind Kind,
    int CellCount,
    Vector3 CenterDirection,
    float MinElevation,
    float SurfaceElevation);

// Immutable per-world list of the water bodies found this generation. Version increments on every build so
// downstream caches can tell a rebuild apart from an unchanged world.
public sealed class WaterBodyCatalog
{
    static int _nextVersion;

    public int Version { get; }
    public IReadOnlyList<WaterBody> Bodies { get; }

    readonly Dictionary<ushort, WaterBody> _byId;

    public WaterBodyCatalog(IReadOnlyList<WaterBody> bodies)
    {
        Version = ++_nextVersion;
        Bodies = bodies;
        _byId = new Dictionary<ushort, WaterBody>(bodies.Count);
        foreach (WaterBody b in bodies) _byId[b.Id] = b;
    }

    public bool TryGet(ushort id, out WaterBody body) => _byId.TryGetValue(id, out body);

    public int CountOf(WaterBodyKind kind)
    {
        int n = 0;
        foreach (WaterBody b in Bodies) if (b.Kind == kind) n++;
        return n;
    }
}

// Whole-sphere water body identification, sampled by direction.
//
// The biome bake is per-chunk and only knows a local elevation threshold (elevation < OceanThreshold = water),
// so it cannot tell a small inland lake from the ocean. This builds a coarse whole-sphere map once per world
// gen: sample ground elevation over a per-face grid, flood-fill the below-water cells ACROSS cube-face seams,
// and give every connected body an id. Small bodies are tagged Lake (plus a shore ring on the surrounding
// land); everything else is Ocean. Biome resolvers query Sample(direction) for the lake/shore override.
//
// Self-limiting by design: only components smaller than LakeMaxCells become lakes, so the ocean is never
// mis-tagged. If elevation sampling is off, the worst case is no lakes detected (every cell reads the same
// wetness, giving one huge component that is not small and so not a lake), never a broken ocean.
public sealed class WaterBodyMap
{
    public const byte None = 0;
    public const byte Water = 1;  // small water body surface -> Lake biome
    public const byte Shore = 2;  // land ring around a lake -> LakeShore biome

    // Whatever the active world built. Planet clears this before each generate so a cancelled or failed
    // generation cannot leave the previous world bodies visible to the next one.
    public static WaterBodyMap Current;

    const int Res = 192;                 // cells per face axis (192^2 * 6 = ~221k elevation samples at gen)
    const int FaceCells = Res * Res;
    const int TotalCells = FaceCells * 6;
    // A body smaller than this is a lake, not ocean. Now measured over the WHOLE body rather than per face,
    // so a lake straddling a cube seam is judged by its true size instead of being counted twice.
    const int LakeMaxCells = 1400;
    const int ShoreRings = 2;            // land cells within this many steps of lake water become LakeShore

    readonly byte[] _mask = new byte[TotalCells];
    readonly ushort[] _bodyId = new ushort[TotalCells];

    public WaterBodyCatalog Bodies { get; private set; }

    // Seam neighbour lookups that did not agree in both directions. Cube faces at equal resolution should be
    // 1:1 across a seam, so this is expected to be 0; a non-zero count means a body could split at a seam.
    public int SeamAsymmetryCount { get; private set; }

    public static WaterBodyMap Build(ISurfaceGroundSampler ground, float baseRadiusLocal, float oceanThreshold)
    {
        if (ground == null || baseRadiusLocal <= 0f) return null;
        var m = new WaterBodyMap();
        m.BuildInternal(ground, baseRadiusLocal, oceanThreshold);
        return m;
    }

    void BuildInternal(ISurfaceGroundSampler ground, float baseRadiusLocal, float oceanThreshold)
    {
        var elevation = new float[TotalCells];
        var wet = new bool[TotalCells];
        for (int i = 0; i < TotalCells; i++)
        {
            Vector3 dir = CellDirection(i);
            float elev = ground.TrySampleRadius(dir, out float rad) ? rad / baseRadiusLocal - 1f : 1f;
            elevation[i] = elev;
            wet[i] = elev < oceanThreshold;
        }

        var bodies = new List<WaterBody>();
        var visited = new bool[TotalCells];
        var stack = new Stack<int>(1024);
        var component = new List<int>(2048);
        ushort nextId = 1;

        for (int start = 0; start < TotalCells; start++)
        {
            if (!wet[start] || visited[start]) continue;
            component.Clear();
            stack.Push(start);
            visited[start] = true;
            Vector3 dirSum = Vector3.zero;
            float minElev = float.MaxValue;
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                component.Add(c);
                dirSum += CellDirection(c);
                if (elevation[c] < minElev) minElev = elevation[c];
                for (int n = 0; n < 4; n++)
                {
                    int ni = Neighbor(c, n);
                    if (wet[ni] && !visited[ni]) { visited[ni] = true; stack.Push(ni); }
                }
            }

            WaterBodyKind kind = component.Count < LakeMaxCells ? WaterBodyKind.Lake : WaterBodyKind.Ocean;
            ushort id = nextId++;
            foreach (int c in component)
            {
                _bodyId[c] = id;
                if (kind == WaterBodyKind.Lake) _mask[c] = Water;
            }

            bodies.Add(new WaterBody(id, kind, component.Count, dirSum.normalized, minElev, oceanThreshold));

            // Ids are ushort with 0 reserved for "no body"; a world with more bodies than this is not a planet.
            if (nextId == ushort.MaxValue) break;
        }

        Bodies = new WaterBodyCatalog(bodies);
        DilateShores(wet);
        SeamAsymmetryCount = CountAsymmetricSeams();
    }

    // Grow the lake surface onto surrounding dry land, exactly ShoreRings steps. Each ring is computed against
    // the PRE-ring mask into a to-mark list, then applied - writing into _mask while reading it would let the
    // shore flood across the whole scan in a single pass.
    void DilateShores(bool[] wet)
    {
        var toMark = new List<int>(1024);
        for (int ring = 0; ring < ShoreRings; ring++)
        {
            byte target = (byte)(ring == 0 ? Water : Shore);
            toMark.Clear();
            for (int i = 0; i < TotalCells; i++)
            {
                if (_mask[i] != None || wet[i]) continue; // only unclaimed dry land
                for (int n = 0; n < 4; n++)
                {
                    if (_mask[Neighbor(i, n)] == target) { toMark.Add(i); break; }
                }
            }
            foreach (int i in toMark) _mask[i] = Shore;
        }
    }

    static readonly int[] NeighborDx = { -1, 1, 0, 0 };
    static readonly int[] NeighborDy = { 0, 0, -1, 1 };

    // 4-neighbour of a global cell index. Inside a face this is plain index maths (exact). At a seam we step
    // the cell centre one cell past the face edge in face-uv, project that to a direction and re-classify it.
    // That reuses the same projection Sample() uses, so there is no per-edge adjacency table to drift.
    int Neighbor(int index, int n)
    {
        int face = index / FaceCells;
        int local = index - face * FaceCells;
        int x = local % Res + NeighborDx[n];
        int y = local / Res + NeighborDy[n];
        if (x >= 0 && x < Res && y >= 0 && y < Res) return face * FaceCells + y * Res + x;

        Vector3 dir = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(
            face, new Vector2((x + 0.5f) / Res, (y + 0.5f) / Res));
        return CellIndex(dir);
    }

    int CountAsymmetricSeams()
    {
        int bad = 0;
        for (int i = 0; i < TotalCells; i++)
        {
            int local = i % FaceCells;
            int x = local % Res, y = local / Res;
            if (x > 0 && x < Res - 1 && y > 0 && y < Res - 1) continue;
            for (int n = 0; n < 4; n++)
            {
                int ni = Neighbor(i, n);
                bool mutual = false;
                for (int b = 0; b < 4; b++) if (Neighbor(ni, b) == i) { mutual = true; break; }
                if (!mutual) bad++;
            }
        }
        return bad;
    }

    static Vector3 CellDirection(int index)
    {
        int face = index / FaceCells;
        int local = index - face * FaceCells;
        var uv = new Vector2((local % Res + 0.5f) / Res, (local / Res + 0.5f) / Res);
        return FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(face, uv);
    }

    static int CellIndex(Vector3 direction)
    {
        FaceSpaceCellRangeBuilder.DirectionToFaceUv(direction, out int face, out Vector2 uv);
        int x = Mathf.Clamp((int)(uv.x * Res), 0, Res - 1);
        int y = Mathf.Clamp((int)(uv.y * Res), 0, Res - 1);
        return face * FaceCells + y * Res + x;
    }

    // Lake state at a local unit direction. Read-only after Build, safe from parallel bake threads.
    public byte Sample(Vector3 direction) => _mask[CellIndex(direction)];

    // Body id at a local unit direction; 0 when the cell is not below water.
    public ushort SampleBodyId(Vector3 direction) => _bodyId[CellIndex(direction)];

    public bool TrySampleBody(Vector3 direction, out WaterBody body)
    {
        ushort id = SampleBodyId(direction);
        if (id != 0 && Bodies != null) return Bodies.TryGet(id, out body);
        body = null;
        return false;
    }
}
