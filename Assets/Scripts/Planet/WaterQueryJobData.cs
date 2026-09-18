using System;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public interface IWaterQueryField
{
    float Radius { get; }
    float Offset { get; }
    float Scale { get; }
    Vector3 ToLocal(Vector3 point);
    Vector3 ToWorld(Vector3 point);
    Vector3 Direction(Vector3 direction);
    Vector3 Velocity(Vector3 velocity);
    float Level(Vector3 direction);
    ushort Body(Vector3 direction);
    bool Ocean(ushort body);
    bool River(Vector3 direction, out RiverSegment river, out float along);
    bool Ground(Vector3 direction, out float radius);
}

public static class WaterQueryKernel
{
    public static bool Sample<T>(T field, Vector3 position, out WaterSample sample)
        where T : struct, IWaterQueryField
    {
        sample = default;
        Vector3 local = field.ToLocal(position);
        if (!CharacterMath.IsFinite(local)) return false;
        float radius = local.magnitude;
        if (radius < .0001f) return false;
        Vector3 dir = local / radius;
        float surface = field.Radius * (1f + field.Level(dir)) + field.Offset;
        ushort body = field.Body(dir);
        bool riverHit = field.River(dir, out var river, out float along) && river.Shape.w == 0f;
        if (riverHit) { surface = river.Radius(along) + field.Offset; body = (ushort)river.Flow.x; }
        if (body == 0) return false;
        float bed = field.Ground(dir, out float ground) ? ground : surface;
        Vector3 normal = field.Direction(dir).normalized;
        Vector3 velocity = Vector3.zero;
        if (riverHit)
        {
            Vector3 downstream = ((Vector3)river.B.xyz * river.B.w - (Vector3)river.A.xyz * river.A.w).normalized;
            Vector3 side = Vector3.Cross(downstream, dir).normalized;
            normal = field.Direction(Vector3.Cross(side, downstream).normalized);
            velocity = field.Velocity(downstream * river.Shape.z);
        }
        sample = new WaterSample(field.ToWorld(dir * surface), normal,
            (surface - radius) * field.Scale, Mathf.Max(surface - bed, 0f) * field.Scale,
            body, field.Ocean(body), velocity);
        return true;
    }
}

public struct SurfaceQueryJobData
{
    [ReadOnly] public NativeArray<NoiseFilterData> Noise;
    [ReadOnly] public NativeArray<byte> DiagnosticCells;
    public DiagnosticTerrainSettingsData Diagnostic;
    public RiverFieldData Rivers;
    public float PlanetRadius;

    public bool TryGetRadius(Vector3 direction, out float radius)
    {
        radius = 0f;
        if (direction.sqrMagnitude < 1e-8f) return false;
        float3 dir = direction.normalized;
        float elevation = Diagnostic.Enabled != 0
            ? DiagnosticTerrainEvaluator.Evaluate(dir, Diagnostic, DiagnosticCells)
            : NoiseFilterEvaluator.EvaluateLayers(Noise, dir);
        radius = PlanetRadius * (1f + Rivers.Carve(dir, elevation));
        return radius > 0f;
    }
}

public struct WaterQueryJobData : IWaterQueryField, IWaterQueryService
{
    [ReadOnly] public NativeArray<float> Levels;
    [ReadOnly] public NativeArray<ushort> Bodies;
    [ReadOnly] public NativeArray<byte> Kinds;
    public SurfaceQueryJobData Terrain;
    public Matrix4x4 WorldToLocal, LocalToWorld;
    public Quaternion Rotation;
    public float PlanetRadius, SurfaceOffset, WorldScale, OceanLevel;
    public int Resolution;
    public float Radius => PlanetRadius;
    public float Offset => SurfaceOffset;
    public float Scale => WorldScale;
    public Vector3 ToLocal(Vector3 point) => WorldToLocal.MultiplyPoint3x4(point);
    public Vector3 ToWorld(Vector3 point) => LocalToWorld.MultiplyPoint3x4(point);
    public Vector3 Direction(Vector3 dir) => Rotation * dir;
    public Vector3 Velocity(Vector3 velocity) => LocalToWorld.MultiplyVector(velocity);
    public float Level(Vector3 dir)
    {
        float level = Levels[WaterLevelGrid.Index(dir, Resolution)];
        return level == WaterLevelGrid.NoWater ? OceanLevel : level;
    }
    public ushort Body(Vector3 dir) => Bodies[WaterLevelGrid.Index(dir, Resolution)];
    public bool Ocean(ushort body) => Kinds[body] == (byte)WaterBodyKind.Ocean;
    public bool River(Vector3 dir, out RiverSegment river, out float along) =>
        Terrain.Rivers.Sample(dir, out river, out along, out _);
    public bool Ground(Vector3 dir, out float radius) => Terrain.TryGetRadius(dir, out radius);
    public bool TryGetWaterSurface(Vector3 point, out WaterSample sample) => WaterQueryKernel.Sample(this, point, out sample);
    public bool IsUnderwater(Vector3 point) => TryGetWaterSurface(point, out var sample) && sample.IsSubmerged;
}

[BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
public struct WaterQueryBatchJob : IJobParallelFor
{
    public WaterQueryJobData Water;
    [ReadOnly] public NativeArray<Vector3> Positions;
    [WriteOnly] public NativeArray<WaterSample> Samples;
    [WriteOnly] public NativeArray<byte> Valid;
    public void Execute(int index)
    {
        bool found = Water.TryGetWaterSurface(Positions[index], out var sample);
        Samples[index] = sample;
        Valid[index] = found ? (byte)1 : (byte)0;
    }
}

// Owns only copied query data. River arrays remain world-owned; readers must finish before world teardown.
public sealed class WaterQueryJobSnapshot : IDisposable
{
    public WaterQueryJobData Data;
    public WaterQueryJobSnapshot(WaterBodyMap bodies, IBurstElevationSource source, float radius,
        float oceanLevel, float offset, Transform transform)
    {
        try
        {
            Data.Levels = new NativeArray<float>(bodies.LevelGrid, Allocator.Persistent);
            Data.Bodies = new NativeArray<ushort>(bodies.BodyIdGrid, Allocator.Persistent);
            Data.Kinds = new NativeArray<byte>(ushort.MaxValue + 1, Allocator.Persistent);
            foreach (var body in bodies.Bodies.Bodies) Data.Kinds[body.Id] = (byte)body.Kind;
            Data.Terrain.Noise = source.BuildNoiseFilterData(Allocator.Persistent);
            Data.Terrain.DiagnosticCells = source.BuildDiagnosticCells(Allocator.Persistent);
            Data.Terrain.Diagnostic = source.DiagnosticData;
            Data.Terrain.Rivers = source.Rivers;
            Data.Terrain.PlanetRadius = source.PlanetRadius;
            Data.PlanetRadius = radius; Data.OceanLevel = oceanLevel; Data.SurfaceOffset = offset;
            Data.Resolution = WaterBodyMap.Resolution;
            CaptureTransform(transform);
        }
        catch { Dispose(); throw; }
    }
    public void CaptureTransform(Transform transform)
    {
        Data.WorldToLocal = transform.worldToLocalMatrix; Data.LocalToWorld = transform.localToWorldMatrix;
        Data.Rotation = transform.rotation;
        Vector3 scale = transform.lossyScale;
        Data.WorldScale = Mathf.Max(scale.x, Mathf.Max(scale.y, Mathf.Max(scale.z, .0001f)));
    }
    public void Dispose()
    {
        if (Data.Levels.IsCreated) Data.Levels.Dispose();
        if (Data.Bodies.IsCreated) Data.Bodies.Dispose();
        if (Data.Kinds.IsCreated) Data.Kinds.Dispose();
        if (Data.Terrain.Noise.IsCreated) Data.Terrain.Noise.Dispose();
        if (Data.Terrain.DiagnosticCells.IsCreated) Data.Terrain.DiagnosticCells.Dispose();
        Data = default;
    }
}
