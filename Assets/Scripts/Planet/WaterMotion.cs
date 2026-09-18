using UnityEngine;

// Presentation uses the same three swell phases as WaterDisplacement.hlsl. Still-water gameplay queries remain deterministic.
public static class WaterMotion
{
    public static float Height(WaterSample surface, Vector3 center, WaterDto settings, Vector3 wind,
        float weatherEnergy, float time, float distanceScale, float liquid = 1f)
    {
        if (settings == null || !float.IsFinite(time) || !float.IsFinite(surface.BodyDepth) || surface.BodyDepth <= 0f)
            return 0f;
        Vector3 a = wind.sqrMagnitude > 0.000001f ? wind.normalized : Vector3.right;
        Vector3 reference = Mathf.Abs(Vector3.Dot(a, Vector3.up)) < .92f ? Vector3.up : Vector3.forward;
        Vector3 b = Vector3.Cross(reference, a).normalized;
        Vector3 local = surface.SurfacePoint - center;
        Vector2 position = new(Vector3.Dot(local, a), Vector3.Dot(local, b));
        float depth01 = surface.BodyDepth / Mathf.Max(settings.DeepDepth * distanceScale, 1f);
        // The query has metric depth, while the mesh carries a shoreline-distance field. Both become one in open water.
        float shore01 = Mathf.Clamp01(surface.BodyDepth / Mathf.Max(settings.ShoreRange * distanceScale, 1f));
        float energy = (.5f + Mathf.Clamp01(weatherEnergy) * .5f)
            * Smooth(.003f, .035f, depth01) * Smooth(.005f, .040f, shore01);
        float wavelength = Mathf.Max(settings.SwellWavelength, 24f) * (surface.IsOcean ? 1f : .42f);
        float amplitude = Mathf.Max(settings.SwellAmplitude, 0f) * (surface.IsOcean ? 1f : .1f) * energy;
        float speed = Mathf.Max(settings.WaveSpeed, .001f);
        float h = Wave(position, Vector2.right, wavelength, speed * .5f, amplitude * .58f, 0f, time);
        h += Wave(position, new Vector2(.78f, .45f).normalized, wavelength * .68f, speed * .62f, amplitude * .30f, 1.7f, time);
        h += Wave(position, new Vector2(.40f, -.74f).normalized, wavelength * .46f, speed * .78f, amplitude * .16f, 3.1f, time);
        return h * Mathf.Clamp01(liquid);
    }

    static float Wave(Vector2 position, Vector2 direction, float wavelength, float speed, float amplitude, float phase, float time) =>
        Mathf.Sin(Vector2.Dot(position, direction) * (Mathf.PI * 2f / wavelength) - time * speed + phase) * amplitude;

    public static float Smooth(float low, float high, float value)
    {
        float t = Mathf.Clamp01((value - low) / Mathf.Max(high - low, .0001f));
        return t * t * (3f - 2f * t);
    }
}

public sealed class WaterImmersionState
{
    bool _initialized, _submerged;
    public float Immersion { get; private set; }
    public float Transition { get; private set; }

    public void Tick(float depth, float deltaTime, bool reset = false)
    {
        if (!float.IsFinite(depth) || !float.IsFinite(deltaTime) || deltaTime < 0f) return;
        float target = WaterMotion.Smooth(-.18f, .28f, depth);
        if (!_initialized || reset)
        {
            _initialized = true;
            _submerged = depth > 0f;
            Immersion = target;
            Transition = 0f;
            return;
        }
        bool submerged = _submerged ? depth > -.10f : depth > .10f;
        Transition *= Mathf.Exp(-deltaTime * 3f);
        if (submerged != _submerged) Transition = 1f;
        _submerged = submerged;
        Immersion = Mathf.Lerp(Immersion, target, 1f - Mathf.Exp(-deltaTime * 14f));
    }
}
