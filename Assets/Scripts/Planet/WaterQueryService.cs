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
    RiverFieldData _rivers;
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
        float planetRadius, float oceanLevel, float surfaceOffset, RiverFieldData rivers = default)
    {
        _readers.Complete(); _readers = default; Version++;
        _bodies = bodies;
        _rivers = rivers;
        _ground = ground;
        _planetRadius = planetRadius;
        _oceanLevel = oceanLevel;
        _surfaceOffset = surfaceOffset;
    }

    public void Reset()
    {
        _readers.Complete(); _readers = default; Version++;
        _bodies = null;
        _rivers = default;
        _ground = null;
    }

    public bool TryGetWaterSurface(Vector3 worldPosition, out WaterSample sample)
    {
        sample = default;
        if (!IsConfigured) return false;

        return WaterQueryKernel.Sample(new Field(this), worldPosition, out sample);
    }

    public bool IsUnderwater(Vector3 worldPosition) =>
        TryGetWaterSurface(worldPosition, out WaterSample sample) && sample.IsSubmerged;


    public int Version { get; private set; }
    Unity.Jobs.JobHandle _readers;
    public void RegisterReader(Unity.Jobs.JobHandle reader) => _readers = Unity.Jobs.JobHandle.CombineDependencies(_readers, reader);
    public bool TryCreateJobSnapshot(out WaterQueryJobSnapshot snapshot)
    {
        snapshot = null;
        if (!IsConfigured || _bodies.LevelGrid == null || _ground is not IBurstElevationSource source) return false;
        snapshot = new WaterQueryJobSnapshot(_bodies, source, _planetRadius, _oceanLevel, _surfaceOffset, _planetTransform);
        return true;
    }
    public void CaptureJobTransform(WaterQueryJobSnapshot snapshot) => snapshot.CaptureTransform(_planetTransform);

    readonly struct Field : IWaterQueryField
    {
        readonly WaterQueryService _owner;
        public Field(WaterQueryService owner) => _owner = owner;
        public float Radius => _owner._planetRadius;
        public float Offset => _owner._surfaceOffset;
        public float Scale => _owner.LocalToWorldScale();
        public Vector3 ToLocal(Vector3 point) => _owner._planetTransform.InverseTransformPoint(point);
        public Vector3 ToWorld(Vector3 point) => _owner._planetTransform.TransformPoint(point);
        public Vector3 Direction(Vector3 direction) => _owner._planetTransform.TransformDirection(direction);
        public Vector3 Velocity(Vector3 velocity) => _owner._planetTransform.TransformVector(velocity);
        public float Level(Vector3 direction) => _owner._bodies.LevelAt(direction, _owner._oceanLevel);
        public ushort Body(Vector3 direction) => _owner._bodies.SampleBodyId(direction);
        public bool Ocean(ushort body) => _owner._bodies.Bodies != null &&
            _owner._bodies.Bodies.TryGet(body, out var info) && info.Kind == WaterBodyKind.Ocean;
        public bool River(Vector3 direction, out RiverSegment river, out float along) =>
            _owner._rivers.Sample(direction, out river, out along, out _);
        public bool Ground(Vector3 direction, out float radius)
        { radius = 0f; return _owner._ground != null && _owner._ground.TrySampleRadius(direction, out radius); }
    }

    float LocalToWorldScale()
    {
        Vector3 s = _planetTransform.lossyScale;
        return Mathf.Max(Mathf.Max(s.x, s.y), Mathf.Max(s.z, 0.0001f));
    }
}
