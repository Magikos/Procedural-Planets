using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

// Planet-local units throughout. The same segment data feeds terrain, placement, water queries and shaders.
public struct RiverSegment
{
    public float4 A; // direction, surface radius
    public float4 B;
    public float4 Shape; // half width, depth, speed, waterfall
    public float4 Flow; // stable body id, downstream distance at A/B, bank width
    public float4 Profile; // end half width (zero uses Shape.x), middle widening, reach ID, clipped-end bits

    public float Radius(float t) => math.lerp(A.w, B.w, t);
    public float Width(float t) => math.lerp(Shape.x, Profile.x > 0f ? Profile.x : Shape.x, t) +
        Profile.y * math.pow(math.sin(math.PI * t), 2f);
    public float MaxWidth => math.max(Shape.x, Profile.x) + Profile.y;
    public float BankWidth(float t) => Width(t) * (Flow.w / Shape.x);

    public float Distance(float3 direction, float planetRadius, out float t)
    {
        float3 edge = B.xyz - A.xyz;
        float raw = math.dot(direction - A.xyz, edge) / math.max(math.lengthsq(edge), 1e-12f);
        t = math.saturate(raw);
        float beyond = (raw - t) * math.length(edge) * planetRadius;
        if ((((int)Profile.w & 1) != 0 && beyond < -.005f) ||
            (((int)Profile.w & 2) != 0 && beyond > .005f)) return float.MaxValue;
        float3 center = math.normalizesafe(math.lerp(A.xyz, B.xyz, t), A.xyz);
        return math.length(direction - center) * planetRadius;
    }
}

[BurstCompile]
public struct RiverFieldData
{
    public const int Resolution = 64;
    [ReadOnly] public NativeArray<RiverSegment> Segments;
    [ReadOnly] public NativeArray<int2> Ranges;
    [ReadOnly] public NativeArray<int> Indices;
    public float PlanetRadius;

    public bool Sample(float3 dir, out RiverSegment segment, out float along, out float distance)
        => SampleCompiled(in this, in dir, out segment, out along, out distance);

    [BurstCompile(CompileSynchronously = true)]
    static bool SampleCompiled(in RiverFieldData field, in float3 dir,
        out RiverSegment segment, out float along, out float distance)
        => field.SampleReference(dir, out segment, out along, out distance);

    // One implementation serves managed parity checks and native gameplay/terrain callers.
    public bool SampleReference(float3 dir, out RiverSegment segment, out float along, out float distance)
    {
        segment = default;
        along = 0f;
        distance = float.MaxValue;
        if (!Ranges.IsCreated || Ranges.Length == 0) return false;
        int2 range = Ranges[WaterLevelGrid.Index(new Vector3(dir.x, dir.y, dir.z), Resolution)];
        float score = float.MaxValue;
        float radiusSum = 0f, weightSum = 0f;
        for (int i = range.x; i < range.y; i++)
        {
            var candidate = Segments[Indices[i]];
            if (candidate.Shape.w > 0f) continue; // Falling sheets do not enclose a water volume.
            float d = candidate.Distance(dir, PlanetRadius, out float t);
            float relative = d / candidate.Width(t);
            if (relative >= score) continue;
            score = relative;
            segment = candidate;
            along = t;
            distance = d;
        }
        for (int i = range.x; i < range.y; i++)
        {
            var candidate = Segments[Indices[i]];
            if (candidate.Shape.w > 0f || candidate.Profile.z != segment.Profile.z) continue;
            float d = candidate.Distance(dir, PlanetRadius, out float t);
            float weight = math.pow(math.saturate(1f - d / candidate.BankWidth(t)), 4f);
            radiusSum += candidate.Radius(t) * weight;
            weightSum += weight;
        }
        if (weightSum > 1e-6f)
        {
            float offset = radiusSum / weightSum - segment.Radius(along);
            if (((int)segment.Profile.w & 1) != 0) offset *= math.smoothstep(0f, .2f, along);
            if (((int)segment.Profile.w & 2) != 0) offset *= math.smoothstep(0f, .2f, 1f - along);
            segment.A.w += offset; segment.B.w += offset;
        }
        return distance <= segment.Width(along) + .005f;
    }

    public float Carve(float3 dir, float elevation)
        => CarveCompiled(in this, in dir, elevation);

    [BurstCompile(CompileSynchronously = true)]
    static float CarveCompiled(in RiverFieldData field, in float3 dir, float elevation)
        => field.CarveReference(dir, elevation);

    public float CarveReference(float3 dir, float elevation)
    {
        if (!Ranges.IsCreated || Ranges.Length == 0) return elevation;
        float original = PlanetRadius * (1f + elevation);
        float carved = original;
        int2 range = Ranges[WaterLevelGrid.Index(new Vector3(dir.x, dir.y, dir.z), Resolution)];
        for (int i = range.x; i < range.y; i++)
        {
            var s = Segments[Indices[i]];
            float distance = s.Distance(dir, PlanetRadius, out float t);
            float width = s.Width(t);
            float blend = math.saturate((distance - width) / math.max(s.BankWidth(t) - width, .01f));
            blend = blend * blend * (3f - 2f * blend);
            float cross = math.saturate(distance / width);
            float bed = s.Radius(t) - s.Shape.y * (1f - cross * cross);
            carved = math.min(carved, math.lerp(bed, original, blend));
        }
        return carved / PlanetRadius - 1f;
    }
}

public sealed class RiverField : IDisposable
{
    public RiverFieldData Data { get; private set; }
    public int SegmentCount => Data.Segments.Length;
    public int WaterfallCount { get; }

    public RiverField(IReadOnlyList<RiverSegment> segments, float radius)
    {
        if (!(radius > 0f) || !float.IsFinite(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
        var buckets = new Dictionary<int, HashSet<int>>();
        int falls = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            if (!math.all(math.isfinite(s.A)) || !math.all(math.isfinite(s.B)) ||
                !math.all(math.isfinite(s.Shape)) || !math.all(math.isfinite(s.Flow)) || !math.all(math.isfinite(s.Profile)) ||
                s.Profile.x < 0f || s.Profile.y < 0f ||
                s.A.w <= 0f || s.B.w <= 0f || s.Shape.x <= 0f || s.Shape.y <= 0f || s.Flow.w <= s.Shape.x ||
                math.abs(math.lengthsq(s.A.xyz) - 1f) > .001f || math.abs(math.lengthsq(s.B.xyz) - 1f) > .001f)
                throw new ArgumentException("River segments require finite radii, unit directions, positive widths and valid banks.");
            if (s.Shape.w > 0f) falls++;
            // Sample the swept bank at sub-cell spacing. Include a full neighboring-cell margin at seams.
            float spacing = radius / RiverFieldData.Resolution * .25f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(math.distance(s.A.xyz, s.B.xyz) * radius / spacing));
            float margin = s.MaxWidth * (s.Flow.w / s.Shape.x) + radius / RiverFieldData.Resolution * 3f;
            for (int step = 0; step <= steps; step++)
            {
                Vector3 dir = math.normalize(math.lerp(s.A.xyz, s.B.xyz, (float)step / steps));
                Vector3 right = Vector3.Cross(dir, Mathf.Abs(dir.y) < .95f ? Vector3.up : Vector3.right).normalized;
                Vector3 forward = Vector3.Cross(right, dir);
                int taps = Mathf.Max(2, Mathf.CeilToInt(margin / spacing));
                for (int y = -taps; y <= taps; y++)
                for (int x = -taps; x <= taps; x++)
                {
                    Vector3 sample = (dir + (right * x + forward * y) * (margin / taps / radius)).normalized;
                    int cell = WaterLevelGrid.Index(sample, RiverFieldData.Resolution);
                    if (!buckets.TryGetValue(cell, out var entries)) buckets[cell] = entries = new HashSet<int>();
                    entries.Add(i);
                }
            }
        }
        var ordered = new List<int>();
        var ranges = new NativeArray<int2>(6 * RiverFieldData.Resolution * RiverFieldData.Resolution, Allocator.Persistent);
        for (int cell = 0; cell < ranges.Length; cell++)
        {
            int start = ordered.Count;
            if (buckets.TryGetValue(cell, out var entries))
            {
                var sorted = new List<int>(entries);
                sorted.Sort();
                ordered.AddRange(sorted);
            }
            ranges[cell] = new int2(start, ordered.Count);
        }
        var nativeSegments = new NativeArray<RiverSegment>(segments.Count, Allocator.Persistent);
        for (int i = 0; i < segments.Count; i++) nativeSegments[i] = segments[i];
        Data = new RiverFieldData
        {
            PlanetRadius = radius, Segments = nativeSegments, Ranges = ranges,
            Indices = new NativeArray<int>(ordered.ToArray(), Allocator.Persistent)
        };
        WaterfallCount = falls;
    }

    public void Dispose()
    {
        var data = Data;
        if (data.Segments.IsCreated) data.Segments.Dispose();
        if (data.Ranges.IsCreated) data.Ranges.Dispose();
        if (data.Indices.IsCreated) data.Indices.Dispose();
        Data = default;
    }
}
