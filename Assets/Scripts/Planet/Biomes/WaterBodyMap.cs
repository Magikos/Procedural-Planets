using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public enum WaterBodyKind : byte
{
    None = 0,
    Lake = 1,
    Ocean = 2,
}

// One connected below-water region on the sphere.
//
// SurfaceElevation is the height this body's surface sits at, in the same units as PlanetSettings.OceanLevel.
// Oceans report the global level; lakes report the spill height WaterSpillSolver found for their basin, which
// is where a lake with an outflow actually sits. The water MESH is still one shell built from the global
// level, so a lake whose spill height is above sea level is described correctly here but not yet drawn that
// way - that is W5b.
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
    // How far both the shore mask and the water level reach onto dry land. ONE number because the two must
    // agree: the mesh decides water from the level field alone, the biome resolver gates on the mask, and
    // wherever the two disagree the mesh draws water over ground the biome has called land - which shows up
    // as a cell-shaped notch bitten out of the lake bed. Four cells is about 160 m, past the widest shore
    // band, so neither field's own boundary is ever the visible edge.
    const int ShoreRings = 4;

    // How far the water LEVEL is carried onto dry land. Must stay <= ShoreRings - see BuildLevelField.
    const int LevelRings = 1;



    // Water shallower than this is rounding noise, not a lake.
    const float SpillDepthEpsilon = 1e-6f;
    // Smallest basin that becomes water. 16 cells is roughly 23,000 m^2 at R=5000.
    const int MinBasinCells = 16;
    // Level value meaning "no water stands here". Below any real elevation, so `elevation < level` is false.
    const float NoWater = float.NegativeInfinity;

    readonly byte[] _mask = new byte[TotalCells];
    readonly ushort[] _bodyId = new ushort[TotalCells];

    // 4 per cell, -1 where there is none. Built once because the flood fill, the shore dilation, the spill
    // solve and the seam check all walk it, and recomputing the seam projection in each was the bulk of the
    // build cost.
    int[] _neighbors;

    public WaterBodyCatalog Bodies { get; private set; }
    public WaterSpillSolver.Drainage Drainage { get; private set; }

    // Cells the spill solve found underwater. Larger than the wet-cell count whenever basins above sea level
    // would hold water, which is the population W5b turns into raised lakes.
    public int SubmergedCellCount { get; private set; }

    // Connected-component sizes of that set BEFORE the minimum-area cut, largest first. Retained because it
    // is what says whether the cut is set sensibly for a given world.
    public int[] SubmergedBasinSizes { get; private set; } = System.Array.Empty<int>();

    // Basins dropped for being smaller than MinBasinCells.
    public int DrainedBasinCount { get; private set; }

    // Water surface height per cell in PlanetSettings.OceanLevel units, NoWater where none stands. Dilated
    // ShoreRings onto the shore so `elevation < LevelAt(dir)` stays a valid wet test at mesh resolution.
    float[] _level;

    // Same solve carried ShoreRings onto dry land and WITHOUT the anti-flood guard, for consumers that need
    // a reference height rather than a wetness test - see ShoreLevelGrid.
    float[] _shoreLevel;

    // Cells the SOLVE put water in, as opposed to cells the dilation ring only carried a level to. Kept
    // because the two are indistinguishable in _level and must not be treated alike - see TrySolvedLevelAt.
    bool[] _solvedWater;

    // Seam neighbour lookups that did not agree in both directions. Cube faces at equal resolution should be
    // 1:1 across a seam, so this is expected to be 0; a non-zero count means a body could split at a seam.
    public int SeamAsymmetryCount { get; private set; }

    public static WaterBodyMap Build(ISurfaceGroundSampler ground, float baseRadiusLocal, float oceanThreshold, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (ground == null || baseRadiusLocal <= 0f) return null;
        var m = new WaterBodyMap();
        m.BuildInternal(ground, baseRadiusLocal, oceanThreshold, ct);
        ct.ThrowIfCancellationRequested();
        return m;
    }

    public static async Awaitable<WaterBodyMap> BuildAsync(
        ISurfaceGroundSampler ground, float baseRadiusLocal, float oceanThreshold, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Awaitable.BackgroundThreadAsync();
        try
        {
            return Build(ground, baseRadiusLocal, oceanThreshold, ct);
        }
        finally
        {
            await Awaitable.MainThreadAsync();
        }
    }

    void BuildInternal(ISurfaceGroundSampler ground, float baseRadiusLocal, float oceanThreshold, CancellationToken ct)
    {
        var elevation = new float[TotalCells];
        var wet = new bool[TotalCells];
        for (int i = 0; i < TotalCells; i++)
        {
            if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
            Vector3 dir = CellDirection(i);
            float elev = ground.TrySampleRadius(dir, out float rad) ? rad / baseRadiusLocal - 1f : 1f;
            elevation[i] = elev;
            wet[i] = elev < oceanThreshold;
        }

        _neighbors = BuildNeighborTable(ct);

        // The ocean is whatever the global level already floods in one large connected piece. It is only
        // needed as the drain the spill solve pours toward; the real bodies come out of the solved level.
        bool[] oceanSeeds = FindOceanSeeds(wet, ct);
        bool[] submerged = ResolveLevels(oceanSeeds, elevation, oceanThreshold, ct) ?? wet;

        Bodies = new WaterBodyCatalog(BuildBodiesFromLevel(submerged, oceanSeeds, elevation, oceanThreshold, ct));
        DilateShores(submerged, ct);
        SeamAsymmetryCount = CountAsymmetricSeams(ct);
    }

    bool[] FindOceanSeeds(bool[] wet, CancellationToken ct)
    {
        var seeds = new bool[TotalCells];
        var visited = new bool[TotalCells];
        var stack = new Stack<int>(1024);
        var component = new List<int>(2048);
        for (int start = 0; start < TotalCells; start++)
        {
            if ((start & 255) == 0) ct.ThrowIfCancellationRequested();
            if (!wet[start] || visited[start]) continue;
            component.Clear();
            stack.Push(start);
            visited[start] = true;
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                int c = stack.Pop();
                component.Add(c);
                for (int n = 0; n < 4; n++)
                {
                    int ni = _neighbors[c * 4 + n];
                    if (ni >= 0 && wet[ni] && !visited[ni]) { visited[ni] = true; stack.Push(ni); }
                }
            }
            if (component.Count >= LakeMaxCells)
                foreach (int c in component) seeds[c] = true;
        }
        return seeds;
    }

    // Bodies come from the solved level, not the global wet predicate, so a basin perched above sea level is
    // an ordinary body with an id, a catalog entry and a Lake/LakeShore mask - which is what makes the biome
    // bake put reeds and lilies on its shore instead of the forest that was there when it was dry ground.
    List<WaterBody> BuildBodiesFromLevel(bool[] submerged, bool[] oceanSeeds, float[] elevation, float oceanThreshold, CancellationToken ct)
    {
        var bodies = new List<WaterBody>();
        var visited = new bool[TotalCells];
        var stack = new Stack<int>(1024);
        var component = new List<int>(2048);
        ushort nextId = 1;

        for (int start = 0; start < TotalCells; start++)
        {
            if ((start & 255) == 0) ct.ThrowIfCancellationRequested();
            if (!submerged[start] || visited[start]) continue;
            component.Clear();
            stack.Push(start);
            visited[start] = true;
            Vector3 dirSum = Vector3.zero;
            float minElev = float.MaxValue;
            float level = float.MinValue;
            bool touchesOcean = false;
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                int c = stack.Pop();
                component.Add(c);
                dirSum += CellDirection(c);
                if (elevation[c] < minElev) minElev = elevation[c];
                if (_level != null && _level[c] > level) level = _level[c];
                if (oceanSeeds[c]) touchesOcean = true;
                for (int n = 0; n < 4; n++)
                {
                    int ni = _neighbors[c * 4 + n];
                    if (ni >= 0 && submerged[ni] && !visited[ni]) { visited[ni] = true; stack.Push(ni); }
                }
            }

            // Reaching the ocean is what makes a body the ocean, rather than its size. A landlocked basin
            // larger than LakeMaxCells is an inland sea and still wants lake treatment.
            WaterBodyKind kind = touchesOcean ? WaterBodyKind.Ocean : WaterBodyKind.Lake;
            if (level == float.MinValue) level = oceanThreshold;

            ushort id = nextId++;
            foreach (int c in component)
            {
                _bodyId[c] = id;
                if (kind == WaterBodyKind.Lake) _mask[c] = Water;
            }

            bodies.Add(new WaterBody(id, kind, component.Count, dirSum.normalized, minElev, level));

            // Ids are ushort with 0 reserved for "no body"; a world with more bodies than this is not a planet.
            if (nextId == ushort.MaxValue) break;
        }
        return bodies;
    }

    // Runs the spill solve and turns it into the level field plus the submerged set the bodies are built
    // from. Returns null when there is no ocean to drain toward, in which case every basin would fill to its
    // rim and the answer would be meaningless, so the caller falls back to the global wet predicate.
    bool[] ResolveLevels(bool[] oceanSeeds, float[] elevation, float oceanThreshold, CancellationToken ct)
    {
        bool anySeed = false;
        foreach (bool s in oceanSeeds) if (s) { anySeed = true; break; }
        if (!anySeed) return null;

        Drainage = WaterSpillSolver.SolveDrainage(elevation, _neighbors, oceanSeeds, oceanThreshold, ct);
        float[] filled = (float[])Drainage.Filled.Clone();
        SubmergedBasinSizes = MeasureSubmergedBasins(filled, elevation, ct);
        DrainBasinsBelowMinimumArea(filled, elevation, ct);

        var submerged = new bool[TotalCells];
        SubmergedCellCount = 0;
        for (int i = 0; i < TotalCells; i++)
        {
            if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
            submerged[i] = filled[i] > elevation[i] + SpillDepthEpsilon;
            if (submerged[i]) SubmergedCellCount++;
        }
        _solvedWater = submerged;
        _level = BuildLevelField(filled, elevation, LevelRings, guardAgainstFlooding: true, ct);
        _shoreLevel = BuildLevelField(filled, elevation, ShoreRings, guardAgainstFlooding: false, ct);
        return submerged;
    }

    // Grow the lake surface onto surrounding dry land, exactly ShoreRings steps. Each ring is computed against
    // the PRE-ring mask into a to-mark list, then applied - writing into _mask while reading it would let the
    // shore flood across the whole scan in a single pass.
    void DilateShores(bool[] wet, CancellationToken ct)
    {
        var toMark = new List<int>(1024);
        for (int ring = 0; ring < ShoreRings; ring++)
        {
            byte target = (byte)(ring == 0 ? Water : Shore);
            toMark.Clear();
            for (int i = 0; i < TotalCells; i++)
            {
                if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
                if (_mask[i] != None || wet[i]) continue; // only unclaimed dry land
                for (int n = 0; n < 4; n++)
                {
                    int ni = _neighbors[i * 4 + n];
                    if (ni >= 0 && _mask[ni] == target) { toMark.Add(i); break; }
                }
            }
            foreach (int i in toMark) _mask[i] = Shore;
        }
    }

    static readonly int[] NeighborDx = { -1, 1, 0, 0 };
    static readonly int[] NeighborDy = { 0, 0, -1, 1 };

    // Connected-component sizes of everything the spill solve put underwater, largest first. W5b builds its
    // bodies from this set instead of the global wet predicate, so this is what says whether a minimum-area
    // threshold is needed: procedural noise makes single-cell dimples that would otherwise all become ponds.
    int[] MeasureSubmergedBasins(float[] filled, float[] elevation, CancellationToken ct)
    {
        var sizes = new List<int>();
        var visited = new bool[TotalCells];
        var stack = new Stack<int>(256);
        for (int start = 0; start < TotalCells; start++)
        {
            if ((start & 255) == 0) ct.ThrowIfCancellationRequested();
            if (visited[start] || filled[start] <= elevation[start] + SpillDepthEpsilon) continue;
            visited[start] = true;
            stack.Push(start);
            int size = 0;
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                int c = stack.Pop();
                size++;
                for (int n = 0; n < 4; n++)
                {
                    int ni = _neighbors[c * 4 + n];
                    if (ni < 0 || visited[ni] || filled[ni] <= elevation[ni] + SpillDepthEpsilon) continue;
                    visited[ni] = true;
                    stack.Push(ni);
                }
            }
            sizes.Add(size);
        }
        sizes.Sort((a, b) => b.CompareTo(a));
        return sizes.ToArray();
    }

    // Turns the solver's filled heights into a field the mesh can test at ITS resolution.
    //
    // The solver sets filled == ground on land that drains away, which is correct for the solve but useless
    // as a wet test: the mesh samples this grid at roughly half its cell size, so a vertex sitting below its
    // 38 m cell's sampled height would read as under water and half of every hillside would flood.
    //
    // So a level only exists where water actually stands. Land keeps NoWater, and the water level is dilated
    // one ring onto the surrounding land so the shoreline can still be found between the last wet cell and
    // the first dry one - without that ring the coastline would quantise to the coarse grid.
    float[] BuildLevelField(float[] filled, float[] elevation, int rings, bool guardAgainstFlooding, CancellationToken ct)
    {
        var level = new float[TotalCells];
        for (int i = 0; i < TotalCells; i++)
            level[i] = filled[i] > elevation[i] + SpillDepthEpsilon ? filled[i] : NoWater;

        // The ring exists for one reason: the mesh samples elevation far finer than this grid, so the true
        // waterline sits INSIDE the first dry cell and the mesh needs a level there to find it.
        //
        // That reasoning only holds where the cell's ground is ABOVE the water. A dry cell whose ground sits
        // BELOW the level is not a shoreline - the solve already decided water drains away from it - and
        // handing it a lake's level floods it wholesale. Beside a lake on flat ground that submerges a full
        // cell-wide apron of land, and because the outer boundary then falls on cell edges it reads as a
        // straight-edged translucent sheet lying over the grass rather than as a shore.
        // Several rings, not one. Every consumer asks LevelAt "how high is the water here", and LevelAt
        // falls back to the GLOBAL ocean level wherever no level exists - which for a lake perched 30 m up
        // reads as "dry" instantly. So the field's outer boundary is where the water mesh stops being built
        // and where the Lake biome stops being assigned, and with a single ring that boundary sat one cell
        // off the water: a 41 m grid, square, which is exactly the stair-stepped edge seen around every lake.
        //
        // LevelRings, NOT ShoreRings, and it must stay the SMALLER of the two.
        //
        // The guard below stops a dilated cell flooding on the COARSE sample, but the biome bake and the mesh
        // both sample elevation far finer than 41 m. Carried four rings up a bank, a shore cell only a metre
        // above the water has dips inside it that read below the borrowed level, and the biome resolver's
        // `lakeState != 0 && elevation < waterLevel` then paints lake bed on them - a staircase of orange
        // cells wandering inland, with the water tint sitting on top of it.
        //
        // One ring is all the mesh ever needed: enough to find the crossing inside the first dry cell. The
        // shore MASK still runs to ShoreRings, which is what gives LakeShore room for its handoff ramp.
        // Level smaller than mask is also the safe direction for the pair - wherever the level says water,
        // the mask already says lake, so the two cannot disagree and bite a notch out of the bed.
        var dilated = (float[])level.Clone();
        for (int ring = 0; ring < rings; ring++)
        {
            float[] source = (float[])dilated.Clone();
            for (int i = 0; i < TotalCells; i++)
            {
                if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
                if (source[i] != NoWater) continue;
                float highest = NoWater;
                for (int n = 0; n < 4; n++)
                {
                    int ni = _neighbors[i * 4 + n];
                    if (ni < 0 || source[ni] <= highest) continue;
                    // Only the WET-TEST field needs this. The reference field is never compared against
                    // elevation to decide wetness, and the cells it would exclude are exactly the ones whose
                    // height above the water the grass fade has to ask about.
                    if (guardAgainstFlooding && elevation[i] <= source[ni]) continue;
                    // And stop once the ground has climbed clear of the water. Ring count alone let the
                    // level walk 160 m up a bank and, where two basins sit close, straight across the gap
                    // between them. This grid is 41 m but the mesh samples elevation far finer, so every
                    // dip inside a carried cell then came out below that borrowed level and was meshed as
                    // water - sheets of it lying across the grass, joining one lake to the next.
                    //
                    highest = source[ni];
                }
                dilated[i] = highest;
            }
        }
        return dilated;
    }


    // A basin smaller than MinBasinCells is terrain noise rather than a lake, so drop its level back to the
    // ground and it simply never becomes water. Measured on the reference world, 94 of 322 basins are a
    // single cell; the cut at 16 keeps about 92 lakes and at 8 about 139.
    void DrainBasinsBelowMinimumArea(float[] filled, float[] elevation, CancellationToken ct)
    {
        var visited = new bool[TotalCells];
        var stack = new Stack<int>(256);
        var component = new List<int>(256);
        for (int start = 0; start < TotalCells; start++)
        {
            if ((start & 255) == 0) ct.ThrowIfCancellationRequested();
            if (visited[start] || filled[start] <= elevation[start] + SpillDepthEpsilon) continue;
            component.Clear();
            visited[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                int c = stack.Pop();
                component.Add(c);
                for (int n = 0; n < 4; n++)
                {
                    int ni = _neighbors[c * 4 + n];
                    if (ni < 0 || visited[ni] || filled[ni] <= elevation[ni] + SpillDepthEpsilon) continue;
                    visited[ni] = true;
                    stack.Push(ni);
                }
            }

            if (component.Count >= MinBasinCells) continue;
            foreach (int c in component) filled[c] = elevation[c];
            DrainedBasinCount++;
        }
    }

    int[] BuildNeighborTable(CancellationToken ct)
    {
        var table = new int[TotalCells * 4];
        for (int i = 0; i < TotalCells; i++)
        {
            if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
            for (int n = 0; n < 4; n++)
                table[i * 4 + n] = Neighbor(i, n);
        }
        return table;
    }

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

    int CountAsymmetricSeams(CancellationToken ct)
    {
        int bad = 0;
        for (int i = 0; i < TotalCells; i++)
        {
            if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
            int local = i % FaceCells;
            int x = local % Res, y = local / Res;
            if (x > 0 && x < Res - 1 && y > 0 && y < Res - 1) continue;
            for (int n = 0; n < 4; n++)
            {
                int ni = _neighbors[i * 4 + n];
                bool mutual = false;
                for (int b = 0; b < 4; b++) if (_neighbors[ni * 4 + b] == i) { mutual = true; break; }
                if (!mutual) bad++;
            }
        }
        return bad;
    }

    public static Vector3 CellDirection(int index)
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

    public void SampleStateAndLevel(Vector3 direction, float fallbackLevel, out byte state, out float level)
    {
        int index = CellIndex(direction);
        state = _mask[index];
        level = _level == null ? fallbackLevel : _level[index];
        if (level == NoWater) level = fallbackLevel;
    }

    // Body id at a local unit direction; 0 when the cell is not below water.
    public ushort SampleBodyId(Vector3 direction) => _bodyId[CellIndex(direction)];

    // The raw level grid, for consumers outside the water system that need their own copy - scatter has to
    // upload it to a Burst job. Indexed with WaterLevelGrid.Index(dir, Resolution); WaterLevelGrid.NoWater
    // marks cells where no water stands. Null when the solve did not run.
    public float[] LevelGrid => _level;
    internal ushort[] BodyIdGrid => _bodyId;

    // The same solve carried further onto dry land, for asking "how high above the water is this ground"
    // rather than "is this ground wet". Grass fades out approaching water over a few metres of altitude, and
    // against the tight field that fade cannot finish: the field ends one cell from the water, the shader
    // falls back to the GLOBAL sea radius there, and beside a lake perched 30 m up that fallback is a 30 m
    // step against a 4 m fade band. Grass therefore switched from fully suppressed to fully present across a
    // single 41 m cell boundary, leaving cell-shaped patches of bare ground around every lake.
    //
    // Dilated WITHOUT the anti-flood guard on purpose. That guard exists so a dry cell below a neighbouring
    // level cannot be meshed as water; nothing here is ever compared against elevation to decide wetness, and
    // those cells are exactly the ones the fade needs an answer for.
    public float[] ShoreLevelGrid => _shoreLevel;
    public static int Resolution => Res;

    // Water surface height at a direction, for use as `elevation < LevelAt(dir)`.
    //
    // Falls back to the global ocean level both where no water stands and where the solve did not run at all
    // (a world with no ocean has nothing to drain to). That fallback is what makes this change purely
    // additive: every cell the solver had nothing to say about behaves exactly as the old single shell did,
    // and only solved basins depart from it.
    public float LevelAt(Vector3 direction, float fallbackLevel)
    {
        if (_level == null) return fallbackLevel;
        float level = _level[CellIndex(direction)];
        return level == NoWater ? fallbackLevel : level;
    }

    // True once the spill solve has produced a level field. Where it has, NoWater is an ANSWER - water
    // drains away there - and a caller deciding wetness must not substitute the global sea shell for it.
    public bool HasSolvedLevels => _level != null && _solvedWater != null;

    // Water surface height at a direction, and ONLY where the solve actually put water there. Use this, not
    // LevelAt, to decide whether a point is under water.
    //
    // _level holds two different things that look identical: heights the solve computed, and heights the
    // dilation ring carried one cell onto dry land. The ring exists so the mesh can find the waterline
    // crossing INSIDE that first dry cell, and the crossing is always taken from the WET end's level - so a
    // carried height was never needed to decide wetness, only to describe it.
    //
    // Using it as a wetness test is unsound. BuildLevelField's anti-flood guard rejects a carried cell whose
    // COARSE 41 m elevation sits below the borrowed level, but the mesh samples elevation far finer than the
    // grid, so a cell that passes the guard can still hold dips a metre below it. Each such dip was meshed
    // as water and, having no wet neighbour to join, became an isolated 41 m sheet lying on the grass near a
    // lake. Measured on six of them: mask Shore, level 0.2 to 1.3 m above the ground the fine sampler reads.
    public bool TrySolvedLevelAt(Vector3 direction, out float level)
    {
        level = 0f;
        if (_level == null || _solvedWater == null) return false;
        int cell = CellIndex(direction);
        if (!_solvedWater[cell]) return false;
        float value = _level[cell];
        if (value == NoWater) return false;
        level = value;
        return true;
    }

    public bool TrySampleBody(Vector3 direction, out WaterBody body)
    {
        ushort id = SampleBodyId(direction);
        if (id != 0 && Bodies != null) return Bodies.TryGet(id, out body);
        body = null;
        return false;
    }
}
