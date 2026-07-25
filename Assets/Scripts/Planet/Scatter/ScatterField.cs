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
        if (_configured && EstimateCandidates(regionRadiusMeters, maxLevel) > CandidateBudget)
            throw new InvalidOperationException("scatter: region/spacing exceeds the candidate budget; tile the ROI into smaller queries.");
        return GatherCore(cameraPos, regionRadiusMeters, maxLevel, buffer, reversed: false, out _);
    }

    // One core for both public gather and the diagnostic reverse traversal. `reversed` flips
    // prototype/cell/candidate order so scatter.verify can prove order-independence.
    internal int GatherCore(Vector3 cameraPos, float region, int maxLevel, List<ScatterInstance> buffer,
        bool reversed, out ScatterGatherStats stats)
    {
        stats = default;
        if (!_configured || _library == null) return 0;
        Transform t = _planetTransform;
        float scale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(t);
        // Clip against the observer's surface anchor (the point under the camera), not the camera's
        // 3D position — otherwise altitude shrinks the footprint and empties the gather.
        if (!TryResolveSurfaceAnchor(cameraPos, out Vector3 anchorWS)) return 0;
        float r2 = region * region;
        int protoCount = _library.Prototypes.Length;
        int emitted = 0;

        for (int pk = 0; pk < protoCount; pk++)
        {
            int pi = reversed ? protoCount - 1 - pk : pk;
            var proto = _library.Prototypes[pi];
            int level = _levels[pi];
            if (level > maxLevel) continue;
            float cellUv = ScatterQuadtree.CellUvWidth(level);

            var result = FaceSpaceCellRangeBuilder.BuildRangesLocal(cameraPos, t, _baseRadiusLocal, region, cellUv, 1, _ranges);
            stats.CornerStraddle |= result.UncoveredCornerStraddle;
            PlacementRules rules = BuildRules(proto);

            for (int rk = 0; rk < result.Count; rk++)
            {
                FaceSpaceCell cell = _ranges[reversed ? result.Count - 1 - rk : rk];
                int gx = cell.GridSize.x, gy = cell.GridSize.y;
                // Nested loops (no gx*gy product) so an extreme range never overflows an int.
                for (int dyi = 0; dyi < gy; dyi++)
                {
                    int dy = reversed ? gy - 1 - dyi : dyi;
                    for (int dxi = 0; dxi < gx; dxi++)
                    {
                        int dx = reversed ? gx - 1 - dxi : dxi;
                        int x = cell.PageOriginCellUV.x + dx, y = cell.PageOriginCellUV.y + dy;

                        uint nodeSeed = ScatterHash.Node(_worldSeed, cell.FaceIndex, level, x, y);
                        uint slotSeed = ScatterHash.Slot(nodeSeed, proto.SlotId);
                        Vector2 uv = ScatterQuadtree.CandidateUv(x, y, cellUv, slotSeed);
                        Vector3 dir = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(cell.FaceIndex, uv);

                        // Analytic surface (radius + normal in one query): LOD-independent and
                        // deterministic, so props snap to the surface the mesh converges to instead
                        // of whatever streaming chunk is resident, and it is pure math — no Transform,
                        // no chunk sample — so the gather can move off the main thread.
                        if (!_ground.TrySampleGround(dir, out float localRadius, out Vector3 localNormal) || localRadius <= 0f) continue;

                        Vector3 worldPos = t.TransformPoint(dir * localRadius);
                        if ((worldPos - anchorWS).sqrMagnitude > r2) continue;
                        stats.Candidates++;

                        float membership = Membership(dir, localRadius, proto.Biome);
                        if (membership <= 0f) continue;

                        float slopeCos = Mathf.Clamp01(Vector3.Dot(localNormal, dir));
                        float altitudeMeters = (localRadius - _seaRadiusLocal) * scale;
                        float densityKeep = ScatterQuadtree.AreaKeep(uv, cellUv, proto.SpacingMeters, _baseRadiusLocal * scale)
                                            * Mathf.Pow(membership, proto.BiomeBlendPower);

                        if (ScatterPlacementMath.TryPlace(slotSeed, dir, localRadius, altitudeMeters, slopeCos,
                                densityKeep, _hasOcean, rules, out Vector3 posLocal, out Quaternion rot, out float sc))
                        {
                            ulong id = ScatterId.Pack(cell.FaceIndex, level, x, y, proto.SlotId);
                            buffer.Add(new ScatterInstance(id, t.TransformPoint(posLocal), t.rotation * rot, sc, pi));
                            emitted++; stats.Accepted++;
                        }
                    }
                }
            }
        }
        return emitted;
    }

    bool TryResolveSurfaceAnchor(Vector3 observerWS, out Vector3 anchorWS)
    {
        anchorWS = default;
        Vector3 toObs = observerWS - _planetTransform.position;
        if (toObs.sqrMagnitude < 1e-6f) return false;
        Vector3 localDir = _planetTransform.InverseTransformDirection(toObs.normalized).normalized;
        if (!_ground.TrySampleGround(localDir, out float localRadius, out _) || localRadius <= 0f) return false;
        anchorWS = _planetTransform.TransformPoint(localDir * localRadius);
        return true;
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
    long EstimateCandidates(float region, int maxLevel)
    {
        if (_levels == null) return 0;
        float scale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform);
        float worldRadius = _baseRadiusLocal * scale;
        long total = 0;
        for (int i = 0; i < _library.Prototypes.Length; i++)
        {
            if (_levels[i] > maxLevel) continue;
            float cellWorld = 2f * worldRadius * ScatterQuadtree.CellUvWidth(_levels[i]);
            long side = (long)(2f * region / Mathf.Max(cellWorld, 1e-4f)) + 2;
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
        long est = EstimateCandidates(region, maxLevel);
        if (est > CandidateBudget) { error = $"scatter: candidate budget exceeded (~{est:N0}); reduce region or coarsen spacing"; return false; }
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

    float Membership(Vector3 dir, float localRadius, BiomeType biome)
    {
        float elevation = localRadius / _baseRadiusLocal - 1f;
        BiomeResult r = _biome.EvaluateBiome(dir, elevation);
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
        GatherCore(cam.transform.position, region, lvl, buf, false, out var stats);
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
        GatherCore(c, region, ScatterId.MaxLevel, fwd, false, out var statsF);
        GatherCore(c, region, ScatterId.MaxLevel, rev, true, out _);
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
        if (!TryResolveSurfaceAnchor(c, out Vector3 anchor)) return "scatter.verify INCONCLUSIVE: no surface anchor";
        float small = region * 0.5f, s2 = small * small;
        var smallList = new List<ScatterInstance>(4096);
        GatherCore(c, small, ScatterId.MaxLevel, smallList, false, out _);
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
            GatherCore(obs, region, ScatterId.MaxLevel, buf, false, out var st);
            anyCorner |= st.CornerStraddle;
            sb.AppendLine($"  {a.name}: candidates {st.Candidates}, accepted {st.Accepted}{(st.CornerStraddle ? " [corner straddle]" : "")}");
        }
        return (anyCorner ? "scatter.profile (corner straddle = expected SP1 gap, not a failure):\n" : "scatter.profile:\n") + sb.ToString().TrimEnd();
    }

    public void Dispose() => ConsoleRegistry.UnregisterInstance(typeof(ScatterField));
}
