using UnityEngine;

// Answers "where is the water here" for gameplay, from the same solved level field the mesh, the biome
// bake, scatter and the shaders all read. One authority, so a floating object and the surface it floats on
// cannot disagree.
//
// Two-phase by necessity: the world context registers this before generation runs, and the body map does
// not exist until generation is well underway. Configure supplies it afterwards; every query answers false
// until then rather than guessing.
//
// Determinism: pure over (position, body map, planet radius). The body map comes from a seeded solve that
// produced identical results across every run measured, and nothing here reads a clock or a random source,
// so two clients derive the same answer without exchanging anything. See §7.2/§7.3 of the Magikos
// architecture - water replicates as seed only.
public sealed class WaterQueryService : IWaterQueryService
{
    readonly Transform _planetTransform;

    WaterBodyMap _bodies;
    ISurfaceGroundSampler _ground;
    float _planetRadius;
    float _oceanLevel;
    float _surfaceOffset;

    public WaterQueryService(Transform planetTransform)
    {
        _planetTransform = planetTransform;
    }

    public bool IsConfigured => _bodies != null && _planetRadius > 0f;

    // Called after generation, with the same body map and offset the water mesh was built from. Passing the
    // mesh's SurfaceOffset matters: the rendered surface sits that far above the solved level, and a query
    // that ignored it would report a boat floating a few centimetres inside its own hull.
    public void Configure(WaterBodyMap bodies, ISurfaceGroundSampler ground,
        float planetRadius, float oceanLevel, float surfaceOffset)
    {
        _bodies = bodies;
        _ground = ground;
        _planetRadius = planetRadius;
        _oceanLevel = oceanLevel;
        _surfaceOffset = surfaceOffset;
    }

    public void Reset()
    {
        _bodies = null;
        _ground = null;
    }

    public bool TryGetWaterSurface(Vector3 worldPosition, out WaterSample sample)
    {
        sample = default;
        if (!IsConfigured) return false;

        Vector3 local = _planetTransform.InverseTransformPoint(worldPosition);
        float localRadius = local.magnitude;
        if (localRadius < 0.0001f) return false;
        Vector3 dir = local / localRadius;

        // NoWater resolves to the global ocean level, which is exactly how the mesh and shaders behave, so
        // a position over dry land reports the ocean surface far below it and falls out as "not in water".
        float level = _bodies.LevelAt(dir, _oceanLevel);
        float surfaceRadius = _planetRadius * (1f + level) + _surfaceOffset;

        ushort bodyId = _bodies.SampleBodyId(dir);
        if (bodyId == 0) return false;

        bool isOcean = _bodies.Bodies != null
            && _bodies.Bodies.TryGet(bodyId, out WaterBody body)
            && body.Kind == WaterBodyKind.Ocean;

        float bedRadius = _ground != null && _ground.TrySampleRadius(dir, out float groundRadius)
            ? groundRadius
            : surfaceRadius;

        float scale = LocalToWorldScale();
        Vector3 surfacePoint = _planetTransform.TransformPoint(dir * surfaceRadius);
        Vector3 normal = _planetTransform.TransformDirection(dir).normalized;

        sample = new WaterSample(
            surfacePoint,
            normal,
            (surfaceRadius - localRadius) * scale,
            Mathf.Max(surfaceRadius - bedRadius, 0f) * scale,
            bodyId,
            isOcean);
        return true;
    }

    public bool IsUnderwater(Vector3 worldPosition) =>
        TryGetWaterSurface(worldPosition, out WaterSample sample) && sample.IsSubmerged;

    float LocalToWorldScale()
    {
        Vector3 s = _planetTransform.lossyScale;
        return Mathf.Max(Mathf.Max(s.x, s.y), Mathf.Max(s.z, 0.0001f));
    }
}
