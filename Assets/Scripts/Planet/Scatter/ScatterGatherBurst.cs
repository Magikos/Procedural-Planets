using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

// Burst-side mirror of the managed scatter gather's per-candidate math (ScatterField.TryGatherCandidate +
// AnalyticGroundSampler + ScatterPlacementMath). It exists so ScatterGatherJob can run the placement loop
// in parallel Burst without calling managed samplers. Everything that feeds an acceptance THRESHOLD
// (elevation, surface normal, AreaKeep, membership^power) is kept bit-identical to the managed path by
// reusing the same pure helpers (ScatterQuadtree, ScatterHash, CubeFaceToUnitSphere) and the same
// float ops; only the two post-acceptance Quaternion constructors are re-derived (native, not
// Burst-compilable) and need match the managed rotation only within verify epsilon. NoiseFilterEvaluator
// is the already-blittable, golden-tested elevation core (see NoiseFilterEvaluatorGoldenTests).

// Blittable placement rules for the job — same fields as PlacementRules with byte flags instead of bool
// so it is unambiguously valid inside a NativeArray element.
public struct PlacementRulesBurst
{
    public float Weight;
    public float MaxSlopeCos;
    public float MinSlopeCos;
    public byte HasMinAltitude; public float MinAltitude;
    public byte HasMaxAltitude; public float MaxAltitude;
    public float MinWaterClearance;
    public float2 ScaleRange;
    public byte RandomYaw;
}

// The only biome fields the gather's membership test reads, promoted to blittable ints.
public struct ScatterBiomeSample
{
    public int PrimaryBiome;
    public int SecondaryBiome;
    public float BlendWeight;
}

public static class ScatterGatherBurst
{
    const float NormalProbeMeters = 2f; // must match AnalyticGroundSampler.NormalProbeMeters

    // --- elevation: mirrors ShapeGenerator.SampleElevation + AnalyticGroundSampler.RadiusAt ---

    // Unscaled elevation at a UNIT-sphere direction. Reproduces ShapeGenerator.SampleElevation exactly:
    // layer 0 evaluated unconditionally to seed the mask value, first-layer-as-mask on later layers.
    public static float SampleElevation(Vector3 unitDir, in NativeArray<NoiseFilterData> layers,
        in DiagnosticTerrainSettingsData diag, in NativeArray<byte> diagCells)
    {
        float3 p = new float3(unitDir.x, unitDir.y, unitDir.z);
        if (diag.Enabled != 0)
            return DiagnosticTerrainEvaluator.Evaluate(p, diag, diagCells);

        float elevation = 0f;
        float firstLayerValue = 0f;
        if (layers.Length > 0)
        {
            NoiseFilterData f0 = layers[0];
            firstLayerValue = NoiseFilterEvaluator.Evaluate(ref f0, p);
            if (f0.Enabled != 0) elevation = firstLayerValue;
        }
        for (int i = 1; i < layers.Length; i++)
        {
            NoiseFilterData fi = layers[i];
            if (fi.Enabled == 0) continue;
            float mask = fi.UseFirstLayerAsMask != 0 ? firstLayerValue : 1f;
            elevation += NoiseFilterEvaluator.Evaluate(ref fi, p) * mask;
        }
        return elevation;
    }

    // Local radius at an already-normalized unit direction (mirrors AnalyticGroundSampler.RadiusAt =
    // GetScaledElevation(SampleElevation)).
    static float RadiusAt(Vector3 unitDir, in NativeArray<NoiseFilterData> layers,
        in DiagnosticTerrainSettingsData diag, in NativeArray<byte> diagCells, float planetRadius)
        => planetRadius * (1f + SampleElevation(unitDir, layers, diag, diagCells));

    // Mirrors AnalyticGroundSampler.TrySampleRadius (normalizes the direction first).
    public static float SampleRadius(Vector3 dir, in NativeArray<NoiseFilterData> layers,
        in DiagnosticTerrainSettingsData diag, in NativeArray<byte> diagCells, float planetRadius)
        => RadiusAt(dir.normalized, layers, diag, diagCells, planetRadius);

    // Mirrors AnalyticGroundSampler.SampleNormalAt (two tangent probes -> surface triangle).
    public static Vector3 SampleNormalAt(Vector3 dirIn, float localRadius, in NativeArray<NoiseFilterData> layers,
        in DiagnosticTerrainSettingsData diag, in NativeArray<byte> diagCells, float planetRadius)
    {
        Vector3 dir = dirIn.normalized;
        float arc = NormalProbeMeters / localRadius;
        Vector3 t1 = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Vector3 t2 = Vector3.Cross(dir, t1);
        Vector3 dA = (dir + t1 * arc).normalized;
        Vector3 dB = (dir + t2 * arc).normalized;

        Vector3 p0 = dir * localRadius;
        Vector3 pA = dA * RadiusAt(dA, layers, diag, diagCells, planetRadius);
        Vector3 pB = dB * RadiusAt(dB, layers, diag, diagCells, planetRadius);
        Vector3 n = Vector3.Cross(pA - p0, pB - p0).normalized;
        if (Vector3.Dot(n, dir) < 0f) n = -n;
        return n;
    }

    // --- placement: mirrors ScatterPlacementMath, with the two native quaternion ctors re-derived ---

    static float SlopeKeep(float maxSlopeCos, float minSlopeCos, float slopeCos)
    {
        if (minSlopeCos - maxSlopeCos <= 1e-5f)
            return slopeCos >= minSlopeCos ? 1f : 0f;
        return Mathf.InverseLerp(maxSlopeCos, minSlopeCos, slopeCos);
    }

    public static bool PassesAltitudeWater(float altitudeMeters, bool hasOcean, in PlacementRulesBurst rules)
    {
        if (rules.HasMinAltitude != 0 && altitudeMeters < rules.MinAltitude) return false;
        if (rules.HasMaxAltitude != 0 && altitudeMeters > rules.MaxAltitude) return false;
        if (hasOcean && rules.MinWaterClearance > 0f && altitudeMeters < rules.MinWaterClearance) return false;
        return true;
    }

    public static bool TryPlace(uint slotSeed, Vector3 dir, float localRadius,
        float altitudeMeters, float slopeCos, float densityKeep, bool hasOcean, in PlacementRulesBurst rules,
        out Vector3 posLocal, out Quaternion rot, out float scale)
    {
        posLocal = default; rot = Quaternion.identity; scale = 0f;

        if (!PassesAltitudeWater(altitudeMeters, hasOcean, rules)) return false;

        float slopeKeep = SlopeKeep(rules.MaxSlopeCos, rules.MinSlopeCos, slopeCos);
        if (slopeKeep <= 0f) return false;

        float accept = rules.Weight * slopeKeep * Mathf.Clamp01(densityKeep);
        if (ScatterHash.To01(slotSeed) >= accept) return false;

        posLocal = dir * localRadius;
        scale = Mathf.Lerp(rules.ScaleRange.x, rules.ScaleRange.y, ScatterHash.To01(ScatterHash.Slot(slotSeed, 7)));
        float yaw = rules.RandomYaw != 0 ? ScatterHash.To01(ScatterHash.Slot(slotSeed, 9)) * 360f : 0f;

        float3 dirF = new float3(dir.x, dir.y, dir.z);
        quaternion align = FromUpTo(dirF);
        quaternion yawQ = quaternion.AxisAngle(dirF, math.radians(yaw));
        quaternion q = math.mul(yawQ, align);
        rot = new Quaternion(q.value.x, q.value.y, q.value.z, q.value.w);
        return true;
    }

    // Shortest-arc rotation taking world up (0,1,0) onto a unit direction. Matches
    // Quaternion.FromToRotation(Vector3.up, dir) within rotation epsilon (post-acceptance only).
    static quaternion FromUpTo(float3 to)
    {
        float3 from = new float3(0f, 1f, 0f);
        float d = math.dot(from, to);
        if (d >= 1f - 1e-6f) return quaternion.identity;
        if (d <= -1f + 1e-6f) return quaternion.AxisAngle(new float3(1f, 0f, 0f), math.PI);
        float3 c = math.cross(from, to);
        float s = math.sqrt((1f + d) * 2f);
        float invs = 1f / s;
        return math.normalize(new quaternion(c.x * invs, c.y * invs, c.z * invs, s * 0.5f));
    }

    // --- membership + id (mirror ScatterField.MembershipFor + ScatterId.Pack without the throw) ---

    public static float Membership(in ScatterBiomeSample r, int biome)
    {
        if (r.PrimaryBiome == biome) return 1f - r.BlendWeight;
        if (r.SecondaryBiome == biome) return r.BlendWeight;
        return 0f;
    }

    // Bit layout copied from ScatterId.Pack; inputs are known-valid inside the gather so validation
    // (which throws with string interpolation, not Burst-compilable) is dropped.
    public static ulong PackUnchecked(int face, int level, int x, int y, int slot)
    {
        const int FaceBits = 3, LevelBits = 5, CoordBits = 24, SlotBits = 6;
        const int LevelShift = FaceBits;
        const int XShift = LevelShift + LevelBits;
        const int YShift = XShift + CoordBits;
        const int SlotShift = YShift + CoordBits;
        const ulong FaceMask = (1UL << FaceBits) - 1;
        const ulong LevelMask = (1UL << LevelBits) - 1;
        const ulong CoordMask = (1UL << CoordBits) - 1;
        const ulong SlotMask = (1UL << SlotBits) - 1;
        return ((ulong)face & FaceMask)
             | (((ulong)level & LevelMask) << LevelShift)
             | (((ulong)(uint)x & CoordMask) << XShift)
             | (((ulong)(uint)y & CoordMask) << YShift)
             | (((ulong)slot & SlotMask) << SlotShift);
    }
}
