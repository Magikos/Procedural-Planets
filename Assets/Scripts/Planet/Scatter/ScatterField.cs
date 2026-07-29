using System;
using System.Collections.Generic;
using UnityEngine;

// Headless deterministic placement core: emits a stable stream of scatter instances (id + world
// transform + prototype) for discrete props, gated by biome / slope / altitude / water-clearance,
// with biome-border density falloff. The reference authority later render/interaction slices
// validate against. No rendering, no GameObjects. Plain class: Planet owns it, constructs it in
// EnsureRuntimeOwners, Configures it after each successful generation, disposes it on teardown.
[CommandPrefix("scatter")]
public sealed class ScatterField : IDisposable
{
    const long CandidateBudget = 2_000_000; // preflight cap: bail before a fine-spacing prototype hangs the main thread
    const int BiomeSampleLevel = 9; // coarse quadtree level the biome eval is memoized at during gather

    public struct ScatterGatherStats { public int Candidates; public int Accepted; public bool CornerStraddle; }

    readonly Transform _planetTransform;
    readonly ISurfaceGroundSampler _ground;
    readonly IBiomeProvider _biome;
    readonly FaceSpaceCell[] _ranges = new FaceSpaceCell[FaceSpaceCellRangeBuilder.MaxRanges];

    ScatterLibraryDto _library;
    int _worldSeed;
    float _baseRadiusLocal;
    float _seaRadiusLocal;
    bool _hasOcean;
    bool _configured;
    int[] _levels; // fixed per prototype per generated world

    public ScatterField(Transform planetTransform, ISurfaceGroundSampler ground, IBiomeProvider biome)
    {
        _planetTransform = planetTransform;
        _ground = ground;
        _biome = biome;
        ConsoleRegistry.RegisterInstance(this);
    }

    // Called after every successful generation (beside _grass.Configure), i.e. after the last
    // cancellable await. Radii are LOCAL. Re-validates the final (possibly overridden) DTO.
    public void Configure(int worldSeed, float baseRadiusLocal, float seaRadiusLocal, bool hasOcean)
    {
        _worldSeed = worldSeed;
        _baseRadiusLocal = baseRadiusLocal;
        _seaRadiusLocal = seaRadiusLocal;
        _hasOcean = hasOcean;
        _library = SettingsProvider.GetSettings<ScatterLibraryDto>();
        _library.EnsureValid();

        float scale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform);
        float worldRadius = baseRadiusLocal * scale;
        _levels = new int[_library.Prototypes.Length];
        for (int i = 0; i < _levels.Length; i++)
            _levels[i] = ScatterQuadtree.LevelForSpacing(worldRadius, _library.Prototypes[i].SpacingMeters);
        _configured = true;
    }

    public void Reset() => _configured = false;

    public int Gather(Vector3 cameraPos, float regionRadiusMeters, int maxLevel, List<ScatterInstance> buffer)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (!(regionRadiusMeters > 0f) || float.IsInfinity(regionRadiusMeters))
            throw new ArgumentOutOfRangeException(nameof(regionRadiusMeters), regionRadiusMeters, "must be finite and positive");
        if (maxLevel < 0 || maxLevel > ScatterId.MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(maxLevel), maxLevel, $"must be 0..{ScatterId.MaxLevel}");
        if (TryCaptureGatherContext(out var ctx) &&
            EstimateCandidates(ctx, regionRadiusMeters, maxLevel, FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform), perPrototypeCull: false) > CandidateBudget)
            throw new InvalidOperationException("scatter: region/spacing exceeds the candidate budget; tile the ROI into smaller queries.");
        return GatherCoreSync(cameraPos, regionRadiusMeters, maxLevel, buffer, reversed: false, out _);
    }

    // Immutable snapshot of the per-generation config the gather reads. Captured on the main thread so
    // a background gather never observes a half-applied Configure: the DTO and levels array are
    // replaced wholesale on regen, so holding the old references stays internally consistent.
    public readonly struct GatherContext
    {
        public readonly ScatterLibraryDto Library;
        public readonly int[] Levels;
        public readonly int WorldSeed;
        public readonly float BaseRadiusLocal;
        public readonly float SeaRadiusLocal;
        public readonly bool HasOcean;

        public GatherContext(ScatterLibraryDto library, int[] levels, int worldSeed,
            float baseRadiusLocal, float seaRadiusLocal, bool hasOcean)
        {
            Library = library; Levels = levels; WorldSeed = worldSeed;
            BaseRadiusLocal = baseRadiusLocal; SeaRadiusLocal = seaRadiusLocal; HasOcean = hasOcean;
        }

        public bool IsValid => Library != null && Levels != null && Levels.Length == Library.Prototypes.Length;
    }

    // Main thread only. Bundles the config a gather needs; invalid until the first Configure.
    public bool TryCaptureGatherContext(out GatherContext context)
    {
        context = default;
        if (!_configured || _library == null || _levels == null) return false;
        context = new GatherContext(_library, _levels, _worldSeed, _baseRadiusLocal, _seaRadiusLocal, _hasOcean);
        return true;
    }

    // Main-thread entry for diagnostics and the public Gather: captures config + transform, then runs
    // the gather synchronously against the shared ranges buffer. The render path captures both on the
    // main thread and calls GatherCore on a background thread (see GatherOffThread).
    int GatherCoreSync(Vector3 cameraPos, float region, int maxLevel, List<ScatterInstance> buffer,
        bool reversed, out ScatterGatherStats stats)
    {
        stats = default;
        if (!TryCaptureGatherContext(out GatherContext ctx)) return 0;
        return GatherCore(ctx, PlanetTransformSnapshot.Capture(_planetTransform), cameraPos, region, maxLevel,
            buffer, reversed, perPrototypeCull: false, _ranges, out stats);
    }

    // Placement-only prototypes (no drawable mesh) still get gathered for SP1/persistence near the
    // observer, but must NOT scan the full render region at fine spacing — that dominated the candidate
    // count (e.g. 2.5 m grass over 700 m) and made the gather take seconds. Cap them tight.
    const float PlacementOnlyGatherMeters = 80f;

    // How far to gather this prototype: its far draw radius (impostor end if it has an impostor tier,
    // else its mesh cull), or a tight cap for placement-only prototypes.
    static float ProtoGatherRadius(ScatterPrototypeDto p, float fallback) =>
        p.FarGatherRadius > 0f ? p.FarGatherRadius : Mathf.Min(fallback, PlacementOnlyGatherMeters);

    // One core for both public gather and the diagnostic reverse traversal. `reversed` flips
    // prototype/cell/candidate order so scatter.verify can prove order-independence. Transform- and
    // config-free: everything Configure mutates arrives via the pre-captured context + snapshot and the
    // caller-owned ranges buffer, so this may run on a background thread (the ground sampler and biome
    // eval are both pure).
    internal int GatherCore(in GatherContext ctx, in PlanetTransformSnapshot snap, Vector3 cameraPos,
        float region, int maxLevel, List<ScatterInstance> buffer, bool reversed, bool perPrototypeCull,
        FaceSpaceCell[] ranges, out ScatterGatherStats stats)
    {
        stats = default;
        if (!ctx.IsValid) return 0;
        float scale = snap.UniformScale;
        // Clip against the observer's surface anchor (the point under the camera), not the camera's
        // 3D position — otherwise altitude shrinks the footprint and empties the gather.
        if (!TryResolveSurfaceAnchor(snap, cameraPos, out Vector3 anchorWS)) return 0;
        int protoCount = ctx.Library.Prototypes.Length;
        int emitted = 0;

        // Coarse-cell biome memo (invocation-local, single entry). The live biome eval (climate noise +
        // resolve) is ~50 us and dominates the gather; biomes are large, so sampling it per fine
        // candidate is wasteful. Sample it once per coarse cell CENTER (deterministic point, so the
        // result is order-independent) and reuse for the finer candidates inside. This is a deliberate,
        // approved layout change: membership near a biome border now snaps to the coarse cell.
        long biomeMemoKey = -1;
        BiomeResult biomeMemo = default;

        for (int pk = 0; pk < protoCount; pk++)
        {
            int pi = reversed ? protoCount - 1 - pk : pk;
            var proto = ctx.Library.Prototypes[pi];
            int level = ctx.Levels[pi];
            if (level > maxLevel) continue;
            float cellUv = ScatterQuadtree.CellUvWidth(level);

            // Render banding: each prototype gathers only out to its own far-cull distance, so a
            // fine-spacing prototype (dense bushes) never enumerates the far ring a coarse one (sparse
            // trees) needs. Diagnostics gather uniformly, so a count query means "everything within R".
            float protoRegion = perPrototypeCull ? Mathf.Min(region, ProtoGatherRadius(proto, region)) : region;
            float pr2 = protoRegion * protoRegion;

            var result = FaceSpaceCellRangeBuilder.BuildRangesLocal(cameraPos, snap, ctx.BaseRadiusLocal, protoRegion, cellUv, 1, ranges);
            stats.CornerStraddle |= result.UncoveredCornerStraddle;
            PlacementRules rules = BuildRules(proto);

            for (int rk = 0; rk < result.Count; rk++)
            {
                FaceSpaceCell cell = ranges[reversed ? result.Count - 1 - rk : rk];
                int gx = cell.GridSize.x, gy = cell.GridSize.y;
                // Nested loops (no gx*gy product) so an extreme range never overflows an int.
                for (int dyi = 0; dyi < gy; dyi++)
                {
                    int dy = reversed ? gy - 1 - dyi : dyi;
                    for (int dxi = 0; dxi < gx; dxi++)
                    {
                        int dx = reversed ? gx - 1 - dxi : dxi;
                        int x = cell.PageOriginCellUV.x + dx, y = cell.PageOriginCellUV.y + dy;
                        if (TryGatherCandidate(ctx, snap, cell.FaceIndex, level, x, y, proto, pi, rules,
                                cellUv, scale, anchorWS, pr2, ref biomeMemoKey, ref biomeMemo, ref stats,
                                out ScatterInstance inst))
                        {
                            buffer.Add(inst);
                            emitted++;
                        }
                    }
                }
            }
        }
        return emitted;
    }

    // One prototype's whole payload for one tile: every cell of that prototype whose fixed parent
    // (ScatterQuadtree.ParentTile at tileLevel) is (face, tileX, tileY). No camera ROI clip and no range
    // builder — the tile is a fixed child-cell block, so the payload is a pure function of (tile, seed),
    // stable regardless of where the camera first loaded it (the property the tile cache relies on). The
    // per-candidate decision is the same TryGatherCandidate the whole-disc gather uses, so the draw-
    // filtered union of tiles equals the whole-disc gather. tileLevel must be <= the prototype level
    // (guaranteed by Configure choosing Lt <= min prototype level).
    public int GatherTilePrototype(in GatherContext ctx, in PlanetTransformSnapshot snap,
        int face, int tileX, int tileY, int tileLevel, int protoIndex, List<ScatterInstance> buffer,
        out ScatterGatherStats stats)
    {
        stats = default;
        if (!ctx.IsValid || buffer == null) return 0;
        if ((uint)protoIndex >= (uint)ctx.Library.Prototypes.Length) return 0;
        var proto = ctx.Library.Prototypes[protoIndex];
        int level = ctx.Levels[protoIndex];
        if (level < tileLevel) return 0;
        float cellUv = ScatterQuadtree.CellUvWidth(level);
        float scale = snap.UniformScale;
        PlacementRules rules = BuildRules(proto);

        int shift = level - tileLevel;
        int span = 1 << shift;
        int x0 = tileX << shift, y0 = tileY << shift;

        long biomeMemoKey = -1;
        BiomeResult biomeMemo = default;
        int emitted = 0;
        for (int dy = 0; dy < span; dy++)
        {
            for (int dx = 0; dx < span; dx++)
            {
                int x = x0 + dx, y = y0 + dy;
                if (TryGatherCandidate(ctx, snap, face, level, x, y, proto, protoIndex, rules, cellUv,
                        scale, default, float.PositiveInfinity, ref biomeMemoKey, ref biomeMemo, ref stats,
                        out ScatterInstance inst))
                {
                    buffer.Add(inst);
                    emitted++;
                }
            }
        }
        return emitted;
    }

    // Shared per-candidate placement: seed -> uv/dir -> radius -> ROI -> biome (coarse memo) -> altitude
    // -> normal/slope -> TryPlace -> instance. Both the whole-disc GatherCore (ROI-clipped) and
    // GatherTilePrototype (whole tile, roiSqr = +inf disables the clip) call this, so their placement
    // decisions are byte-identical. The biome memo is caller-owned (invocation-local) so this stays
    // order-independent and safe on a background thread. Sampling the radius first lets rejected
    // candidates skip the slope normal (2 extra ground samples).
    bool TryGatherCandidate(in GatherContext ctx, in PlanetTransformSnapshot snap, int faceIndex,
        int level, int x, int y, ScatterPrototypeDto proto, int protoIndex, in PlacementRules rules,
        float cellUv, float scale, Vector3 anchorWS, float roiSqr,
        ref long biomeMemoKey, ref BiomeResult biomeMemo, ref ScatterGatherStats stats, out ScatterInstance inst)
    {
        inst = default;
        uint nodeSeed = ScatterHash.Node(ctx.WorldSeed, faceIndex, level, x, y);
        uint slotSeed = ScatterHash.Slot(nodeSeed, proto.SlotId);
        Vector2 uv = ScatterQuadtree.CandidateUv(x, y, cellUv, slotSeed);
        Vector3 dir = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(faceIndex, uv);

        if (!_ground.TrySampleRadius(dir, out float localRadius)) return false;

        Vector3 worldPos = snap.TransformPoint(dir * localRadius);
        if ((worldPos - anchorWS).sqrMagnitude > roiSqr) return false;
        stats.Candidates++;

        // sampleLevel = min(level, BiomeSampleLevel) so a prototype coarser than the biome grid samples at
        // its own level (never a negative shift); the key includes sampleLevel so different levels never
        // alias. Value is EvaluateBiome at the coarse cell CENTER -> order-independent.
        int sampleLevel = level < BiomeSampleLevel ? level : BiomeSampleLevel;
        int bshift = level - sampleLevel;
        int xb = x >> bshift, yb = y >> bshift;
        long biomeKey = ((long)faceIndex << 58) | ((long)sampleLevel << 50) | ((long)xb << 25) | (long)yb;
        if (biomeKey != biomeMemoKey)
        {
            float cellUvB = ScatterQuadtree.CellUvWidth(sampleLevel);
            Vector2 cuv = new Vector2((xb + 0.5f) * cellUvB, (yb + 0.5f) * cellUvB);
            Vector3 cdir = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(faceIndex, cuv);
            float celev = _ground.TrySampleRadius(cdir, out float crad) ? crad / ctx.BaseRadiusLocal - 1f : -1f;
            biomeMemo = _biome.EvaluateBiome(cdir, celev);
            biomeMemoKey = biomeKey;
        }
        float membership = MembershipFor(biomeMemo, proto.Biome);
        if (membership <= 0f) return false;

        float altitudeMeters = (localRadius - ctx.SeaRadiusLocal) * scale;
        if (!ScatterPlacementMath.PassesAltitudeWater(altitudeMeters, ctx.HasOcean, rules)) return false;

        Vector3 localNormal = _ground.SampleNormalAt(dir, localRadius);
        float slopeCos = Mathf.Clamp01(Vector3.Dot(localNormal, dir));
        float densityKeep = ScatterQuadtree.AreaKeep(uv, cellUv, proto.SpacingMeters, ctx.BaseRadiusLocal * scale)
                            * Mathf.Pow(membership, proto.BiomeBlendPower);

        if (!ScatterPlacementMath.TryPlace(slotSeed, dir, localRadius, altitudeMeters, slopeCos,
                densityKeep, ctx.HasOcean, rules, out Vector3 posLocal, out Quaternion rot, out float sc))
            return false;

        ulong id = ScatterId.Pack(faceIndex, level, x, y, proto.SlotId);
        inst = new ScatterInstance(id, snap.TransformPoint(posLocal), snap.Rotation * rot, sc, protoIndex);
        stats.Accepted++;
        return true;
    }

    bool TryResolveSurfaceAnchor(in PlanetTransformSnapshot snap, Vector3 observerWS, out Vector3 anchorWS)
    {
        anchorWS = default;
        Vector3 toObs = observerWS - snap.Center;
        if (toObs.sqrMagnitude < 1e-6f) return false;
        Vector3 localDir = snap.InverseTransformDirection(toObs.normalized).normalized;
        if (!_ground.TrySampleGround(localDir, out float localRadius, out _) || localRadius <= 0f) return false;
        anchorWS = snap.TransformPoint(localDir * localRadius);
        return true;
    }

    // Background-thread gather for the renderer. The caller captures the transform on the main thread,
    // then invokes this from Awaitable.BackgroundThreadAsync with its own ranges buffer (so it never
    // races the diagnostics' shared buffer). Budget-guards against a runaway fine-spacing gather.
    public int GatherOffThread(in GatherContext ctx, in PlanetTransformSnapshot snap, Vector3 cameraPos,
        float region, int maxLevel, List<ScatterInstance> buffer, FaceSpaceCell[] ranges)
    {
        if (buffer == null || ranges == null || !ctx.IsValid) return 0;
        if (region <= 0f || float.IsInfinity(region) || maxLevel < 0 || maxLevel > ScatterId.MaxLevel) return 0;
        if (EstimateCandidates(ctx, region, maxLevel, snap.UniformScale, perPrototypeCull: true) > CandidateBudget) return 0;
        return GatherCore(ctx, snap, cameraPos, region, maxLevel, buffer, reversed: false, perPrototypeCull: true, ranges, out _);
    }

    // World-facing wrapper for the diagnostic/anchor helpers: convert to local, sample the analytic
    // ground, scale back to a world radius. The gather hot path calls _ground directly (local dir).
    bool TryGroundRadiusWorld(Vector3 worldDir, out float worldRadius)
    {
        worldRadius = 0f;
        Vector3 localDir = _planetTransform.InverseTransformDirection(worldDir).normalized;
        if (!_ground.TrySampleGround(localDir, out float localRadius, out _)) return false;
        float scale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform);
        worldRadius = localRadius * scale;
        return worldRadius > 0f;
    }

    // Conservative candidate estimate (one square per prototype at its fixed level) for preflighting
    // diagnostic and public work before allocating or sampling. `long` to avoid overflow.
    long EstimateCandidates(in GatherContext ctx, float region, int maxLevel, float uniformScale, bool perPrototypeCull)
    {
        if (!ctx.IsValid) return 0;
        float worldRadius = ctx.BaseRadiusLocal * uniformScale;
        long total = 0;
        for (int i = 0; i < ctx.Library.Prototypes.Length; i++)
        {
            if (ctx.Levels[i] > maxLevel) continue;
            float protoRegion = perPrototypeCull ? Mathf.Min(region, ProtoGatherRadius(ctx.Library.Prototypes[i], region)) : region;
            float cellWorld = 2f * worldRadius * ScatterQuadtree.CellUvWidth(ctx.Levels[i]);
            long side = (long)(2f * protoRegion / Mathf.Max(cellWorld, 1e-4f)) + 2;
            total += side * side;
            if (total > CandidateBudget) return total;
        }
        return total;
    }

    bool TryPrepDiagnostic(float? regionMeters, float def, float lo, float hi, int maxLevel, out float region, out string error)
    {
        error = null;
        region = regionMeters ?? def;
        if (float.IsNaN(region) || float.IsInfinity(region)) { error = "scatter: region must be finite"; return false; }
        region = Mathf.Clamp(region, lo, hi);
        if (maxLevel < 0 || maxLevel > ScatterId.MaxLevel) { error = $"scatter: maxLevel must be 0..{ScatterId.MaxLevel}"; return false; }
        if (TryCaptureGatherContext(out var ctx))
        {
            long est = EstimateCandidates(ctx, region, maxLevel, FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform), perPrototypeCull: false);
            if (est > CandidateBudget) { error = $"scatter: candidate budget exceeded (~{est:N0}); reduce region or coarsen spacing"; return false; }
        }
        return true;
    }

    static PlacementRules BuildRules(ScatterPrototypeDto p) => new PlacementRules
    {
        Weight = p.Weight,
        MinSlopeCos = Mathf.Cos(p.MaxSlopeDegrees * Mathf.Deg2Rad),
        MaxSlopeCos = Mathf.Cos((p.MaxSlopeDegrees + p.SlopeFadeDegrees) * Mathf.Deg2Rad),
        HasMinAltitude = p.HasMinAltitude, MinAltitude = p.MinAltitudeMeters,
        HasMaxAltitude = p.HasMaxAltitude, MaxAltitude = p.MaxAltitudeMeters,
        MinWaterClearance = p.MinWaterClearanceMeters,
        ScaleRange = p.ScaleRange, RandomYaw = p.RandomYaw,
    };

    // Membership of a prototype's biome in an already-resolved BiomeResult (no eval — the gather
    // memoizes the eval per coarse cell and passes the result in).
    static float MembershipFor(in BiomeResult r, BiomeType biome)
    {
        if (r.PrimaryBiome == biome) return 1f - r.BlendWeight;
        if (r.SecondaryBiome == biome) return r.BlendWeight;
        return 0f;
    }

    // Reported by scatter.count so a zero result is self-diagnosing: without it you cannot tell
    // "placement is broken" from "you are standing in a biome no prototype targets".
    string DescribeBiomeAt(Vector3 observerWS)
    {
        float scale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform);
        Vector3 toObs = observerWS - _planetTransform.position;
        if (toObs.sqrMagnitude < 1e-6f) return "biome here: n/a (at planet centre)";
        Vector3 worldDir = toObs.normalized;
        if (!TryGroundRadiusWorld(worldDir, out float wr) || wr <= 0f) return "biome here: no surface";
        float localRadius = wr / Mathf.Max(scale, 1e-4f);
        Vector3 localDir = _planetTransform.InverseTransformDirection(worldDir).normalized;
        BiomeResult r = _biome.EvaluateBiome(localDir, localRadius / _baseRadiusLocal - 1f);
        float altitude = (localRadius - _seaRadiusLocal) * scale;
        return $"biome here: {r.PrimaryBiome} (secondary {r.SecondaryBiome}, blend {r.BlendWeight:F2}), altitude {altitude:F1} m";
    }

    // --- Diagnostics (registry-target: ScatterField is a plain class) ---------------------------

    [ConsoleCommand("count", "Gather at the camera; per-prototype counts + candidates + elapsed ms.", MonoTargetType.Registry)]
    string CountCmd(float? regionMeters = null, int? maxLevel = null)
    {
        var cam = Camera.main; if (cam == null) return "scatter: no main camera";
        if (!_configured) return "scatter: not configured (generate a planet first)";
        int lvl = maxLevel ?? ScatterId.MaxLevel;
        if (!TryPrepDiagnostic(regionMeters, 80f, 5f, 400f, lvl, out float region, out string err)) return err;
        var buf = new List<ScatterInstance>(8192);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        GatherCoreSync(cam.transform.position, region, lvl, buf, false, out var stats);
        sw.Stop();
        var per = new int[_library.Prototypes.Length];
        foreach (var inst in buf) per[inst.PrototypeIndex]++;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"scatter: {buf.Count} accepted / {stats.Candidates} candidates in {region:F0} m " +
                      $"(maxLevel {lvl}, {sw.ElapsedMilliseconds} ms){(stats.CornerStraddle ? " [corner straddle — expected SP1 gap]" : "")}");
        sb.AppendLine("  " + DescribeBiomeAt(cam.transform.position));
        for (int i = 0; i < per.Length; i++)
            sb.AppendLine($"  [{i}] slot {_library.Prototypes[i].SlotId} {_library.Prototypes[i].DisplayName}: {per[i]}");
        return sb.ToString().TrimEnd();
    }

    [ConsoleCommand("verify", "Proof: nonzero, unique, order-independent, transform-stable, region-independent, id round-trip.", MonoTargetType.Registry)]
    string VerifyCmd(float? regionMeters = null)
    {
        var cam = Camera.main; if (cam == null) return "scatter: no main camera";
        if (!_configured) return "scatter: not configured (generate a planet first)";
        if (_library.Prototypes.Length == 0) return "scatter.verify INCONCLUSIVE: empty library";
        if (!TryPrepDiagnostic(regionMeters, 60f, 5f, 200f, ScatterId.MaxLevel, out float region, out string err)) return err;
        Vector3 c = cam.transform.position;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var fwd = new List<ScatterInstance>(8192);
        var rev = new List<ScatterInstance>(8192);
        GatherCoreSync(c, region, ScatterId.MaxLevel, fwd, false, out var statsF);
        GatherCoreSync(c, region, ScatterId.MaxLevel, rev, true, out _);
        // Corner straddle is a deliberately-unscoped SP1 gap; covered cells are still deterministic,
        // so it is reported (PASS_WITH_KNOWN_CORNER_GAP), not failed.
        if (fwd.Count == 0) return "scatter.verify INCONCLUSIVE: no instances in view (move to a populated biome)";

        var mapF = new Dictionary<ulong, ScatterInstance>(fwd.Count);
        foreach (var i in fwd) if (!mapF.TryAdd(i.Id, i)) return $"scatter.verify FAIL: duplicate id {i.Id} (forward)";
        var mapR = new Dictionary<ulong, ScatterInstance>(rev.Count);
        foreach (var i in rev) if (!mapR.TryAdd(i.Id, i)) return $"scatter.verify FAIL: duplicate id {i.Id} (reverse)";
        if (mapF.Count != mapR.Count) return $"scatter.verify FAIL: {mapF.Count} vs {mapR.Count} unique ids across orders";

        int drift = 0;
        foreach (var kv in mapF)
        {
            if (!mapR.TryGetValue(kv.Key, out var r)) return $"scatter.verify FAIL: id {kv.Key} missing in reverse order";
            var f = kv.Value;
            if ((f.PositionWS - r.PositionWS).sqrMagnitude > 1e-6f || f.PrototypeIndex != r.PrototypeIndex
                || Quaternion.Angle(f.Rotation, r.Rotation) > 0.01f || Mathf.Abs(f.Scale - r.Scale) > 1e-4f) drift++;
        }
        if (drift > 0) return $"scatter.verify FAIL: {drift} transform drifts across orders";

        // Region independence: a smaller ROI equals the larger gather filtered to the small disc.
        if (!TryResolveSurfaceAnchor(PlanetTransformSnapshot.Capture(_planetTransform), c, out Vector3 anchor)) return "scatter.verify INCONCLUSIVE: no surface anchor";
        float small = region * 0.5f, s2 = small * small;
        var smallList = new List<ScatterInstance>(4096);
        GatherCoreSync(c, small, ScatterId.MaxLevel, smallList, false, out _);
        var smallSet = new HashSet<ulong>(); foreach (var i in smallList) smallSet.Add(i.Id);
        var filtered = new HashSet<ulong>(); foreach (var i in fwd) if ((i.PositionWS - anchor).sqrMagnitude <= s2) filtered.Add(i.Id);
        if (!smallSet.SetEquals(filtered)) return $"scatter.verify FAIL: region-independence ({smallSet.Count} small vs {filtered.Count} filtered)";

        // ID pack/unpack incl. player bit = true.
        ScatterId.Unpack(fwd[0].Id, out int f0, out int l0, out int x0, out int y0, out int sl0);
        if (ScatterId.Pack(f0, l0, x0, y0, sl0, false) != fwd[0].Id) return "scatter.verify FAIL: base id round-trip";
        ulong pid = ScatterId.Pack(f0, l0, x0, y0, sl0, true);
        ScatterId.Unpack(pid, out int f1, out int l1, out int x1, out int y1, out int sl1);
        if (!ScatterId.IsPlayer(pid) || f1 != f0 || l1 != l0 || x1 != x0 || y1 != y0 || sl1 != sl0)
            return "scatter.verify FAIL: player id round-trip";

        sw.Stop();
        string status = statsF.CornerStraddle ? "PASS_WITH_KNOWN_CORNER_GAP" : "PASS";
        return $"scatter.verify {status}: {fwd.Count} instances — unique, order-independent, transform-stable, " +
               $"region-independent, id+player round-trip (candidates {statsF.Candidates}, {sw.ElapsedMilliseconds} ms)";
    }

    // Partition proof for the incremental tile gather: the whole-disc gather at the camera must equal the
    // union of the covering tiles' payloads, draw-filtered back to the disc. Confirms GatherTilePrototype
    // over ParentTile blocks reproduces GatherCore exactly. Run at a mid-face pose; cube-corner straddle
    // is a known retained gap (both paths use the same range builder, so they still agree there).
    [ConsoleCommand("tilecheck", "Verify the tile-union gather equals the whole-disc gather at the camera.", MonoTargetType.Registry)]
    string TileCheckCmd(float? regionMeters = null)
    {
        var cam = Camera.main; if (cam == null) return "scatter.tilecheck: no main camera";
        if (!_configured || !TryCaptureGatherContext(out var ctx)) return "scatter.tilecheck: not configured";
        Vector3 c = cam.transform.position;
        var snap = PlanetTransformSnapshot.Capture(_planetTransform);
        if (!TryResolveSurfaceAnchor(snap, c, out Vector3 anchor)) return "scatter.tilecheck: no surface anchor";
        float region = Mathf.Clamp(regionMeters ?? 250f, 60f, 500f);
        float r2 = region * region;

        var disc = new List<ScatterInstance>(8192);
        GatherCoreSync(c, region, ScatterId.MaxLevel, disc, false, out _);
        var discMap = new Dictionary<ulong, ScatterInstance>(disc.Count);
        foreach (var i in disc) discMap[i.Id] = i;

        int minLevel = int.MaxValue;
        for (int p = 0; p < ctx.Levels.Length; p++) minLevel = Mathf.Min(minLevel, ctx.Levels[p]);
        int tileLevel = Mathf.Clamp(Mathf.Min(7, minLevel), 0, 7);
        float cellUvLt = ScatterQuadtree.CellUvWidth(tileLevel);
        float worldScale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform);
        float tileWorld = 2f * ctx.BaseRadiusLocal * worldScale * cellUvLt;

        var ranges = new FaceSpaceCell[FaceSpaceCellRangeBuilder.MaxRanges];
        var res = FaceSpaceCellRangeBuilder.BuildRangesLocal(c, snap, ctx.BaseRadiusLocal, region + 2f * tileWorld, cellUvLt, 1, ranges);
        var uni = new Dictionary<ulong, ScatterInstance>(disc.Count);
        var tileBuf = new List<ScatterInstance>(1024);
        var seenTiles = new HashSet<long>();
        int n = 1 << tileLevel;
        for (int rk = 0; rk < res.Count; rk++)
        {
            FaceSpaceCell cell = ranges[rk];
            for (int dy = 0; dy < cell.GridSize.y; dy++)
            for (int dx = 0; dx < cell.GridSize.x; dx++)
            {
                int tx = cell.PageOriginCellUV.x + dx, ty = cell.PageOriginCellUV.y + dy;
                if ((uint)tx >= (uint)n || (uint)ty >= (uint)n) continue;
                long tileId = ((long)cell.FaceIndex << 20) | ((long)tx << 10) | (uint)ty;
                if (!seenTiles.Add(tileId)) continue;
                for (int p = 0; p < ctx.Library.Prototypes.Length; p++)
                {
                    tileBuf.Clear();
                    GatherTilePrototype(ctx, snap, cell.FaceIndex, tx, ty, tileLevel, p, tileBuf, out _);
                    for (int k = 0; k < tileBuf.Count; k++)
                    {
                        var inst = tileBuf[k];
                        if ((inst.PositionWS - anchor).sqrMagnitude <= r2) uni[inst.Id] = inst;
                    }
                }
            }
        }

        int missing = 0, extra = 0, drift = 0;
        foreach (var kv in discMap) if (!uni.ContainsKey(kv.Key)) missing++;
        foreach (var kv in uni)
        {
            if (!discMap.TryGetValue(kv.Key, out var d)) { extra++; continue; }
            if ((d.PositionWS - kv.Value.PositionWS).sqrMagnitude > 1e-6f
                || Mathf.Abs(d.Scale - kv.Value.Scale) > 1e-4f
                || Quaternion.Angle(d.Rotation, kv.Value.Rotation) > 0.01f) drift++;
        }
        if (missing == 0 && extra == 0 && drift == 0)
            return $"scatter.tilecheck PASS: tile union == whole disc, {discMap.Count} instances (region {region:F0} m, Lt {tileLevel})";
        return $"scatter.tilecheck FAIL: {missing} missing, {extra} extra, {drift} drift of {discMap.Count} (region {region:F0} m, Lt {tileLevel}); " +
               "cube-corner straddle is a known retained gap — retry at a mid-face pose.";
    }

    // Evenly-distributed direction i of n (Fibonacci sphere) — used to hunt for a biome without
    // assuming any particular biome-field layout, so this works on the grid test scene and the
    // real planet alike.
    static Vector3 FibonacciDirection(int i, int n)
    {
        float z = 1f - 2f * (i + 0.5f) / n;
        float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
        float theta = i * 2.399963f; // golden angle
        return new Vector3(r * Mathf.Cos(theta), r * Mathf.Sin(theta), z);
    }

    [ConsoleCommand("goto", "Move the camera to the nearest surface point of a biome, e.g. scatter.goto Forest.", MonoTargetType.Registry)]
    string GotoCmd(BiomeType biome, float? heightMeters = null)
    {
        var cam = Camera.main; if (cam == null) return "scatter: no main camera";
        if (!_configured) return "scatter: not configured (generate a planet first)";

        float scale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform);
        Vector3 center = _planetTransform.position;
        Vector3 fromLocal = _planetTransform
            .InverseTransformDirection((cam.transform.position - center).normalized).normalized;

        const int Samples = 8000;
        float bestDot = -2f;
        Vector3 bestLocal = Vector3.zero;
        bool found = false;
        for (int i = 0; i < Samples; i++)
        {
            Vector3 d = FibonacciDirection(i, Samples);
            Vector3 wd = _planetTransform.TransformDirection(d).normalized;
            if (!TryGroundRadiusWorld(wd, out float wr) || wr <= 0f) continue;
            if (_biome.EvaluateBiome(d, wr / Mathf.Max(scale, 1e-4f) / _baseRadiusLocal - 1f).PrimaryBiome != biome)
                continue;
            float dot = Vector3.Dot(d, fromLocal); // nearest to where the camera already is
            if (dot > bestDot) { bestDot = dot; bestLocal = d; found = true; }
        }

        if (!found) return $"scatter.goto: no surface point found with biome {biome}";

        Vector3 bestWorldDir = _planetTransform.TransformDirection(bestLocal).normalized;
        if (!TryGroundRadiusWorld(bestWorldDir, out float bestRadius) || bestRadius <= 0f)
            return "scatter.goto: surface sample failed at target";

        float height = Mathf.Clamp(heightMeters ?? 15f, 1f, 500f);
        Vector3 surfaceWS = center + bestWorldDir * bestRadius;
        cam.transform.position = surfaceWS + bestWorldDir * height;
        cam.transform.rotation = Quaternion.LookRotation(-bestWorldDir, Vector3.forward);
        return $"scatter.goto {biome}: moved to surface, {height:F0} m up. Run scatter.count to confirm.";
    }

    [ConsoleCommand("profile", "Density bins at face center/edge/corner (face 0). Corner straddle is an expected SP1 gap.", MonoTargetType.Registry)]
    string ProfileCmd(float? regionMeters = null)
    {
        if (!_configured) return "scatter: not configured (generate a planet first)";
        if (!TryPrepDiagnostic(regionMeters, 60f, 5f, 200f, ScatterId.MaxLevel, out float region, out string err)) return err;
        float scale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform);
        (string name, Vector2 uv)[] anchors =
            { ("center", new Vector2(0.5f, 0.5f)), ("edge", new Vector2(0.985f, 0.5f)), ("corner", new Vector2(0.985f, 0.985f)) };
        var sb = new System.Text.StringBuilder();
        var buf = new List<ScatterInstance>(8192);
        bool anyCorner = false;
        foreach (var a in anchors)
        {
            Vector3 dir = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(0, a.uv);
            Vector3 worldDir = _planetTransform.TransformDirection(dir).normalized;
            if (!TryGroundRadiusWorld(worldDir, out float wr) || wr <= 0f) { sb.AppendLine($"  {a.name}: no surface"); continue; }
            Vector3 obs = _planetTransform.TransformPoint(dir * (wr / Mathf.Max(scale, 1e-4f)) * 1.001f);
            buf.Clear();
            GatherCoreSync(obs, region, ScatterId.MaxLevel, buf, false, out var st);
            anyCorner |= st.CornerStraddle;
            sb.AppendLine($"  {a.name}: candidates {st.Candidates}, accepted {st.Accepted}{(st.CornerStraddle ? " [corner straddle]" : "")}");
        }
        return (anyCorner ? "scatter.profile (corner straddle = expected SP1 gap, not a failure):\n" : "scatter.profile:\n") + sb.ToString().TrimEnd();
    }

    [ConsoleCommand("density", "Density readout + ASCII heatmap around the camera; compares actual vs target spacing.", MonoTargetType.Registry)]
    string DensityCmd(float? regionMeters = null, int? gridCells = null)
    {
        var cam = Camera.main; if (cam == null) return "scatter: no main camera";
        if (!_configured) return "scatter: not configured (generate a planet first)";
        if (!TryPrepDiagnostic(regionMeters, 150f, 20f, 400f, ScatterId.MaxLevel, out float region, out string err)) return err;
        int cells = Mathf.Clamp(gridCells ?? 25, 5, 41);

        var buf = new List<ScatterInstance>(16384);
        GatherCoreSync(cam.transform.position, region, ScatterId.MaxLevel, buf, false, out _);
        if (buf.Count == 0) return $"scatter.density: 0 instances in {region:F0} m\n  {DescribeBiomeAt(cam.transform.position)}";

        var snap = PlanetTransformSnapshot.Capture(_planetTransform);
        if (!TryResolveSurfaceAnchor(snap, cam.transform.position, out Vector3 anchor)) return "scatter.density: no surface anchor";
        Vector3 up = (anchor - _planetTransform.position).normalized;
        Vector3 t1 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 t2 = Vector3.Cross(up, t1);

        int[,] grid = new int[cells, cells];
        float half = region, cellSize = 2f * half / cells;
        int peak = 0;
        foreach (var inst in buf)
        {
            Vector3 off = inst.PositionWS - anchor;
            int gx = Mathf.FloorToInt((Vector3.Dot(off, t1) + half) / cellSize);
            int gy = Mathf.FloorToInt((Vector3.Dot(off, t2) + half) / cellSize);
            if (gx < 0 || gx >= cells || gy < 0 || gy >= cells) continue;
            if (++grid[gx, gy] > peak) peak = grid[gx, gy];
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"scatter.density: {buf.Count} instances in {region:F0} m disc ({cellSize:F0} m/cell, peak {peak})");
        sb.AppendLine("  " + DescribeBiomeAt(cam.transform.position));
        float discArea = Mathf.PI * region * region;
        var per = new int[_library.Prototypes.Length];
        foreach (var inst in buf) per[inst.PrototypeIndex]++;
        for (int i = 0; i < per.Length; i++)
        {
            var p = _library.Prototypes[i];
            if (per[i] == 0) { sb.AppendLine($"  [{i}] {p.DisplayName}: 0 (target spacing {p.SpacingMeters:F0} m)"); continue; }
            float impliedSpacing = Mathf.Sqrt(discArea / per[i]);
            sb.AppendLine($"  [{i}] {p.DisplayName}: {per[i]} | ~{impliedSpacing:F1} m apart (target {p.SpacingMeters:F0} m) | {per[i] / discArea * 10000f:F1}/ha");
        }
        const string ramp = " .:-=+*#%@";
        sb.AppendLine("  heatmap (N up, camera centre):");
        for (int gy = cells - 1; gy >= 0; gy--)
        {
            var row = new System.Text.StringBuilder("  ");
            for (int gx = 0; gx < cells; gx++)
            {
                int c = grid[gx, gy];
                int r = peak > 0 ? Mathf.Clamp(Mathf.CeilToInt((float)c / peak * (ramp.Length - 1)), 0, ramp.Length - 1) : 0;
                row.Append(ramp[r]);
            }
            sb.AppendLine(row.ToString());
        }
        return sb.ToString().TrimEnd();
    }

    public void Dispose() => ConsoleRegistry.UnregisterInstance(typeof(ScatterField));
}
