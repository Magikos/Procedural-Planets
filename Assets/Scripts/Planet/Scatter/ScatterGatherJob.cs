using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// One (tile, prototype) pair of gather work — the parallel unit.
public struct ScatterPairInput
{
    public int Face;
    public int TileX;
    public int TileY;
    public int Level;
    public int ProtoIndex;
}

// Blittable per-prototype gather parameters, indexed by prototype (library) index.
public struct ScatterProtoParams
{
    public int SlotId;
    public int Biome;             // (int)BiomeType
    public float SpacingMeters;
    public float BiomeBlendPower;
    public byte OnWater;          // 1 = float on the sea surface inside water cells
    public float Clumpiness;      // 0 = uniform placement, unchanged
    public float PatchScaleMeters;
    public uint ClumpGroupSeed;   // per SPECIES, so a species' variants share one grove field
    public float ShadePreference; // <0 prefers open ground, >0 prefers dense wood
    public PlacementRulesBurst Rules;

    public static ScatterProtoParams From(ScatterPrototypeDto p) => new ScatterProtoParams
    {
        SlotId = p.SlotId,
        Biome = (int)p.Biome,
        SpacingMeters = p.SpacingMeters,
        BiomeBlendPower = p.BiomeBlendPower,
        OnWater = p.OnWater ? (byte)1 : (byte)0,
        Clumpiness = p.Clumpiness,
        PatchScaleMeters = p.PatchScaleMeters,
        ClumpGroupSeed = p.ClumpGroupSeed,
        ShadePreference = p.ShadePreference,
        Rules = new PlacementRulesBurst
        {
            Weight = p.Weight,
            MinSlopeCos = Mathf.Cos(p.MaxSlopeDegrees * Mathf.Deg2Rad),
            MaxSlopeCos = Mathf.Cos((p.MaxSlopeDegrees + p.SlopeFadeDegrees) * Mathf.Deg2Rad),
            HasMinAltitude = p.HasMinAltitude ? (byte)1 : (byte)0, MinAltitude = p.MinAltitudeMeters,
            HasMaxAltitude = p.HasMaxAltitude ? (byte)1 : (byte)0, MaxAltitude = p.MaxAltitudeMeters,
            MinWaterClearance = p.MinWaterClearanceMeters,
            ScaleRange = new float2(p.ScaleRange.x, p.ScaleRange.y),
            RandomYaw = p.RandomYaw ? (byte)1 : (byte)0,
            ConformToSlope = p.ConformToSlope,
            BaseRadius = p.GroundContactRadius(),
        },
    };
}

// Parallel scatter gather: each Execute(i) runs one (tile, prototype) pair's candidate loop — the same
// body as ScatterField.GatherTilePrototype/TryGatherCandidate — and writes its accepted instances to
// stream foreach-index i, so results stay grouped per pair for the cache to file under ByProto[proto].
// Biome is read from a precomputed map (managed Voronoi can't run in Burst); everything else mirrors the
// managed reference bit-for-bit on the threshold path (see ScatterGatherBurst).
[BurstCompile(FloatMode = FloatMode.Deterministic)]
public struct ScatterGatherJob : IJobParallelFor
{
    const int BiomeSampleLevel = 9;

    [ReadOnly] public NativeArray<ScatterPairInput> Pairs;
    [ReadOnly] public NativeArray<NoiseFilterData> NoiseLayers;
    [ReadOnly] public NativeArray<byte> DiagCells;
    public DiagnosticTerrainSettingsData DiagData;
    [ReadOnly] public NativeArray<ScatterProtoParams> Protos;
    [ReadOnly] public NativeParallelHashMap<long, ScatterBiomeSample> Biome;

    public PlanetTransformSnapshot Snap;
    public int WorldSeed;
    public int TileLevel;
    public float BaseRadiusLocal;
    public float SeaRadiusLocal;
    // Per-basin water levels, empty to fall back to SeaRadiusLocal everywhere. Must stay bit-identical to
    // ScatterField.GatherContext.SeaRadiusAt - same helper, same argument order.
    [ReadOnly] public NativeArray<float> WaterLevel;
    public int WaterLevelRes;
    public float PlanetRadius;
    public float Scale;
    public byte HasOcean;

    public NativeStream.Writer Out;

    public void Execute(int index)
    {
        Out.BeginForEachIndex(index);
        ScatterPairInput pair = Pairs[index];
        ScatterProtoParams pp = Protos[pair.ProtoIndex];
        int level = pair.Level;
        float cellUv = ScatterQuadtree.CellUvWidth(level);
        int shift = level - TileLevel;
        int span = 1 << shift;
        int x0 = pair.TileX << shift, y0 = pair.TileY << shift;

        long memoKey = -1;
        ScatterBiomeSample memo = default;
        for (int dy = 0; dy < span; dy++)
        for (int dx = 0; dx < span; dx++)
        {
            int x = x0 + dx, y = y0 + dy;
            if (TryCandidate(pair.Face, level, x, y, pair.ProtoIndex, pp, cellUv, ref memoKey, ref memo, out ScatterInstance inst))
                Out.Write(inst);
        }
        Out.EndForEachIndex();
    }

    bool TryCandidate(int face, int level, int x, int y, int protoIndex, in ScatterProtoParams pp,
        float cellUv, ref long memoKey, ref ScatterBiomeSample memo, out ScatterInstance inst)
    {
        inst = default;
        uint nodeSeed = ScatterHash.Node(WorldSeed, face, level, x, y);
        uint slotSeed = ScatterHash.Slot(nodeSeed, pp.SlotId);
        Vector2 uv = ScatterQuadtree.CandidateUv(x, y, cellUv, slotSeed);
        Vector3 dir = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(face, uv);

        float localRadius = ScatterGatherBurst.SampleRadius(dir, NoiseLayers, DiagData, DiagCells, PlanetRadius);
        // Tile path: roiSqr = +inf in the managed reference, so no ROI clip here.

        int sampleLevel = level < BiomeSampleLevel ? level : BiomeSampleLevel;
        int bshift = level - sampleLevel;
        int xb = x >> bshift, yb = y >> bshift;
        long biomeKey = ((long)face << 58) | ((long)sampleLevel << 50) | ((long)xb << 25) | (long)yb;
        if (biomeKey != memoKey)
        {
            if (!Biome.TryGetValue(biomeKey, out memo)) memo = default;
            memoKey = biomeKey;
        }
        float membership = ScatterGatherBurst.Membership(memo, pp.Biome);
        if (membership <= 0f) return false;

        // OnWater prototypes (lily pads) float on the sea surface inside their biome's water cells, so they
        // place at the sea radius with zero altitude and a flat (radial) normal instead of on the lakebed.
        bool onWater = pp.OnWater != 0;
        float seaRadiusHere = WaterLevel.IsCreated && WaterLevel.Length > 0
            ? WaterLevelGrid.SeaRadius(WaterLevel[WaterLevelGrid.Index(dir, WaterLevelRes)], BaseRadiusLocal, SeaRadiusLocal)
            : SeaRadiusLocal;
        float placeRadius = onWater ? seaRadiusHere + ScatterPlacementMath.OnWaterSurfaceOffsetMeters / Scale : localRadius;
        float altitudeMeters = onWater ? 0f : (localRadius - seaRadiusHere) * Scale;
        if (!ScatterGatherBurst.PassesAltitudeWater(altitudeMeters, HasOcean != 0, pp.Rules)) return false;

        Vector3 localNormal = onWater ? dir : ScatterGatherBurst.SampleNormalAt(dir, localRadius, NoiseLayers, DiagData, DiagCells, PlanetRadius);
        float slopeCos = onWater ? 1f : Mathf.Clamp01(Vector3.Dot(localNormal, dir));
        // Must stay bit-identical to the managed path in ScatterField — same helper, same argument order.
        float densityKeep = ScatterQuadtree.AreaKeep(uv, cellUv, pp.SpacingMeters, BaseRadiusLocal * Scale)
                            * Mathf.Pow(membership, pp.BiomeBlendPower)
                            * ScatterClumping.Keep(dir, BaseRadiusLocal * Scale, pp.Clumpiness,
                                pp.PatchScaleMeters, pp.ClumpGroupSeed, (uint)pp.Biome, slopeCos,
                                pp.ShadePreference);

        if (!ScatterGatherBurst.TryPlace(slotSeed, dir, localNormal, placeRadius, altitudeMeters, slopeCos,
                densityKeep, HasOcean != 0, pp.Rules, out Vector3 posLocal, out Quaternion rot, out float sc))
            return false;

        ulong id = ScatterGatherBurst.PackUnchecked(face, level, x, y, pp.SlotId);
        inst = new ScatterInstance(id, Snap.TransformPoint(posLocal), Snap.Rotation * rot, sc, protoIndex);
        return true;
    }
}
