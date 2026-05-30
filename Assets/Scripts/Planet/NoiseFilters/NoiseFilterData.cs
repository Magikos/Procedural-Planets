using Unity.Burst;
using Unity.Mathematics;

// Blittable per-layer noise data + settings for Burst jobs. Embeds the full simplex
// permutation table (NoiseData has a 512-int fixed buffer), so this struct is ~2 KB.
// Pass arrays via [ReadOnly] NativeArray<NoiseFilterData> — the table is shared across
// worker threads with no per-iteration allocation.
public struct NoiseFilterData
{
    public NoiseData Noise;

    // 0 = Simple, 1 = Rigid (matches NoiseSettings.FilterType ordering).
    public int FilterType;

    public float Strength;
    public int Layers;
    public float BaseRoughness;
    public float Roughness;
    public float Persistence;
    public float3 Center;
    public float MinValue;

    // ShapeSettings.NoiseLayer flags promoted into the blittable struct so the job
    // doesn't need to dereference the managed ShapeSettings.
    public byte Enabled;
    public byte UseFirstLayerAsMask;

    public static NoiseFilterData Create(NoiseSettings settings, int seed, bool enabled, bool useFirstLayerAsMask)
    {
        return new NoiseFilterData
        {
            Noise = NoiseData.Create(seed),
            FilterType = (int)settings.Filter,
            Strength = settings.Strength,
            Layers = settings.Layers,
            BaseRoughness = settings.BaseRoughness,
            Roughness = settings.Roughness,
            Persistence = settings.Persistence,
            Center = new float3(settings.Center.x, settings.Center.y, settings.Center.z),
            MinValue = settings.MinValue,
            Enabled = enabled ? (byte)1 : (byte)0,
            UseFirstLayerAsMask = useFirstLayerAsMask ? (byte)1 : (byte)0,
        };
    }
}

[BurstCompile]
public static class NoiseFilterEvaluator
{
    public static float Evaluate(ref NoiseFilterData f, float3 point)
    {
        return f.FilterType == 1 ? EvaluateRigid(ref f, point) : EvaluateSimple(ref f, point);
    }

    static float EvaluateSimple(ref NoiseFilterData f, float3 point)
    {
        float noiseValue = 0f;
        float frequency = f.BaseRoughness;
        float amplitude = 1f;
        for (int i = 0; i < f.Layers; i++)
        {
            float v = f.Noise.Evaluate(point * frequency + f.Center);
            noiseValue += (v + 1f) * 0.5f * amplitude;
            frequency *= f.Roughness;
            amplitude *= f.Persistence;
        }
        noiseValue -= f.MinValue;
        return noiseValue * f.Strength;
    }

    static float EvaluateRigid(ref NoiseFilterData f, float3 point)
    {
        float noiseValue = 0f;
        float frequency = f.BaseRoughness;
        float amplitude = 1f;
        float weight = 1f;
        for (int i = 0; i < f.Layers; i++)
        {
            float v = 1f - math.abs(f.Noise.Evaluate(point * frequency + f.Center));
            v *= v;
            v *= weight;
            weight = v;
            noiseValue += v * amplitude;
            frequency *= f.Roughness;
            amplitude *= f.Persistence;
        }
        noiseValue -= f.MinValue;
        return noiseValue * f.Strength;
    }
}
