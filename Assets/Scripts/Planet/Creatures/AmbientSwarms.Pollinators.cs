using System.Collections.Generic;
using UnityEngine;

public sealed partial class AmbientSwarms
{
    sealed class Insect
    {
        public AmbientSwarmKind Kind;
        public Vector3 Position, From, Target, Forward;
        public ulong FlowerId;
        public uint Seed;
        public int ArtVariant;
        public float Travel, Duration, Rest, Age, Alpha;
        public bool Retiring;
    }

    readonly struct Flower
    {
        public readonly ulong Id;
        public readonly Vector3 Position;
        public readonly float DistanceSquared;

        public Flower(ulong id, Vector3 position, float distanceSquared)
        { Id = id; Position = position; DistanceSquared = distanceSquared; }
    }

    const int FlowerLimit = 64;
    readonly List<Insect> _insects = new();
    readonly List<Flower> _flowers = new();
    readonly MaterialPropertyBlock _insectProperties = new();
    static readonly int TintId = Shader.PropertyToID("_Tint");
    static readonly int FlapId = Shader.PropertyToID("_FlapAngle");
    static readonly int ButterflyAtlasId = Shader.PropertyToID("_ButterflyAtlas");
    static readonly int ButterflyFrameId = Shader.PropertyToID("_ButterflyFrame");
    static readonly int UseButterflyAtlasId = Shader.PropertyToID("_UseButterflyAtlas");
    ScatterField _flowerField;
    ScatterTileCache _flowerCache;
    float _nextFlowerScan;
    Mesh _insectMesh;
    Mesh _butterflyMesh;
    Texture2D[] _butterflyTextures;

    void LoadButterflyArt()
    {
        if (_butterflyTextures != null) return;
        _butterflyTextures = new Texture2D[4];
        Mesh[] meshes = Resources.LoadAll<Mesh>("Wildlife/Butterflies/FX_Butterfly_Mesh_01");
        if (meshes.Length > 0) _butterflyMesh = meshes[0];
        for (int i = 0; i < _butterflyTextures.Length; i++)
            _butterflyTextures[i] = Resources.Load<Texture2D>($"Wildlife/Butterflies/Butterfly_{i + 1:00}");
        if (_butterflyMesh == null || System.Array.Exists(_butterflyTextures, texture => texture == null))
            _log?.Log(LogLevel.Warning, "Creature", "Butterfly art is incomplete; missing variants use procedural wings.");
    }

    public int NearbyFlowerCount => _flowers.Count;

    static bool IsPollinator(AmbientSwarmKind kind) =>
        kind == AmbientSwarmKind.Butterflies || kind == AmbientSwarmKind.Bees;

    int CountInsects(AmbientSwarmKind kind, bool includeRetiring)
    {
        int count = 0;
        foreach (Insect insect in _insects)
            if (insect.Kind == kind && (includeRetiring || !insect.Retiring)) count++;
        return count;
    }

    void ConfineFireflies()
    {
        foreach (Swarm swarm in _swarms)
        {
            if (swarm.Kind != AmbientSwarmKind.Fireflies || swarm.System == null) continue;
            int count = swarm.System.GetParticles(_fireflyParticles);
            for (int i = 0; i < count; i++)
            {
                var particle = _fireflyParticles[i];
                Vector3 position = Vector3.ClampMagnitude(particle.position, 1f);
                if (position != particle.position)
                    particle.velocity = Vector3.ProjectOnPlane(particle.velocity, position.normalized);
                Vector3 world = swarm.System.transform.TransformPoint(position);
                Vector3 up = (world - _center).normalized;
                if (_sampler.TryGetSurfaceRadius(up, out float radius))
                {
                    float floor = Mathf.Max(radius, _seaLevelRadius) + 0.15f;
                    if ((world - _center).magnitude < floor)
                    {
                        Vector3 aboveGround = swarm.System.transform.InverseTransformPoint(_center + up * floor);
                        if (aboveGround.sqrMagnitude > 1f) particle.remainingLifetime = 0f;
                        else position = aboveGround;
                    }
                }
                particle.position = position;
                _fireflyParticles[i] = particle;
            }
            swarm.System.SetParticles(_fireflyParticles, count);
        }
    }

    void ScanFlowers(Vector3 observer)
    {
        _flowers.Clear();
        if (_flowerCache == null || _flowerField == null ||
            !_flowerField.TryCaptureGatherContext(out ScatterField.GatherContext context)) return;

        // Read the live draw buckets: harvested and unloaded plants cannot remain landing targets.
        for (int p = 0; p < context.Library.Prototypes.Length; p++)
        {
            ScatterPrototypeDto prototype = context.Library.Prototypes[p];
            if (PlantInjection.Classify(prototype) != PlantInjection.Kind.Flower || !prototype.CanRender) continue;
            Bounds bounds = default;
            bool hasBounds = false;
            foreach (ScatterPartDto part in prototype.Parts)
            {
                if (part == null || !part.CanRender) continue;
                if (!hasBounds) { bounds = part.LodMeshes[0].bounds; hasBounds = true; }
                else bounds.Encapsulate(part.LodMeshes[0].bounds);
            }
            if (!hasBounds) continue;
            var positions = _flowerCache.Positions(p);
            var matrices = _flowerCache.Matrices(p);
            var ids = _flowerCache.Ids(p);
            for (int i = 0; i < positions.Count; i++)
            {
                float distance = (positions[i] - observer).sqrMagnitude;
                if (distance > AnchorRadiusMeters * AnchorRadiusMeters ||
                    (_flowers.Count == FlowerLimit && distance >= _flowers[^1].DistanceSquared)) continue;
                Vector3 top = matrices[i].MultiplyPoint3x4(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
                int insert = 0;
                while (insert < _flowers.Count && _flowers[insert].DistanceSquared < distance) insert++;
                _flowers.Insert(insert, new Flower(ids[i], top, distance));
                if (_flowers.Count > FlowerLimit) _flowers.RemoveAt(FlowerLimit);
            }
        }
    }

    bool ChooseLanding(Insect insect, AmbientSwarmProfile profile, Vector3 observer)
    {
        uint draw = ScatterHash.Mix(++insect.Seed);
        for (int i = 0; i < _flowers.Count; i++)
        {
            Flower flower = _flowers[(int)((draw + (uint)i) % (uint)_flowers.Count)];
            if (flower.Id == insect.FlowerId) continue;
            bool occupied = false;
            foreach (Insect other in _insects)
                if (other != insect && !other.Retiring && other.FlowerId == flower.Id) { occupied = true; break; }
            if (occupied) continue;
            insect.FlowerId = flower.Id;
            insect.Target = flower.Position + (flower.Position - _center).normalized * 0.035f;
            BeginFlight(insect, profile);
            return true;
        }
        if (profile.Kind == AmbientSwarmKind.Bees) return false;
        if (!TryPlaceAmbientAnchor(profile, observer, out Vector3 anchor)) return false;
        Vector3 up = (anchor - _center).normalized;
        if (!_sampler.TryGetSurfaceRadius(up, out float radius)) return false;
        insect.FlowerId = 0;
        insect.Target = _center + up * (radius + 0.035f);
        BeginFlight(insect, profile);
        return true;
    }

    static void BeginFlight(Insect insect, AmbientSwarmProfile profile)
    {
        insect.From = insect.Position;
        insect.Travel = 0f;
        insect.Duration = Mathf.Max(1f, Vector3.Distance(insect.From, insect.Target) / profile.SpeedMps);
        insect.Rest = 0f;
    }

    void TickInsects(Vector3 observer, float localSun, long nowUnixSeconds)
    {
        if (Time.time >= _nextFlowerScan)
        {
            ScanFlowers(observer);
            _nextFlowerScan = Time.time + 1f;
            foreach (Insect insect in _insects)
            {
                if (insect.FlowerId == 0) continue;
                bool found = false;
                foreach (Flower flower in _flowers)
                    if (flower.Id == insect.FlowerId) { found = true; break; }
                if (!found && !ChooseLanding(insect, ProfileOf(insect.Kind), observer)) insect.Retiring = true;
            }
        }

        foreach (AmbientSwarmProfile profile in _profiles)
        {
            if (!IsPollinator(profile.Kind)) continue;
            int want = profile.CountAt(localSun);
            int live = 0;
            foreach (Insect insect in _insects)
            {
                if (insect.Kind != profile.Kind || insect.Retiring) continue;
                if (KeptDistance(insect.Position, observer) > KeepAnchorMeters || live >= want) insect.Retiring = true;
                else live++;
            }
            // One arrival per kind per tick, with opacity ramped independently of population decisions.
            if (live < want && TryPlaceAmbientAnchor(profile, observer, out Vector3 anchor) &&
                KeptDistance(anchor, observer) <= KeepAnchorMeters)
            {
                var insect = new Insect { Kind = profile.Kind, Position = anchor, Seed = ScatterHash.Mix(_draw++),
                    ArtVariant = (int)(_draw % 4), Forward = HeadingAt(anchor) };
                if (ChooseLanding(insect, profile, observer)) _insects.Add(insect);
            }
        }

        for (int i = _insects.Count - 1; i >= 0; i--)
        {
            Insect insect = _insects[i];
            AmbientSwarmProfile profile = ProfileOf(insect.Kind);
            float dt = Time.deltaTime;
            insect.Age += dt;
            insect.Alpha = Mathf.MoveTowards(insect.Alpha, insect.Retiring ? 0f : 1f, dt * 0.5f);
            if (insect.Retiring && insect.Alpha <= 0f) { _insects.RemoveAt(i); continue; }
            bool threatened = _threats != null && _threats.TryFindThreat(insect.Position, EntityId.None,
                CreatureFaction.Wildlife, profile.ScatterRadiusMeters, nowUnixSeconds, out _);
            if (insect.Rest > 0f)
            {
                insect.Rest -= dt;
                if (threatened || insect.Rest <= 0f)
                    if (!ChooseLanding(insect, profile, observer)) insect.Retiring = true;
            }
            else
            {
                insect.Travel = Mathf.Min(1f, insect.Travel + dt / insect.Duration);
                float t = insect.Travel;
                Vector3 up = (insect.Position - _center).normalized;
                Vector3 side = Vector3.Cross(up, insect.Target - insect.From).normalized;
                float arc = Mathf.Sin(t * Mathf.PI);
                float flutter = insect.Kind == AmbientSwarmKind.Bees ? 0.06f : 0.3f;
                Vector3 next = Vector3.Lerp(insect.From, insect.Target, t) + up * arc * 1.2f +
                    side * (Mathf.Sin(insect.Age * 5f + insect.Seed % 31) * flutter * arc);
                Vector3 direction = (next - _center).normalized;
                if (!_sampler.TryGetSurfaceRadius(direction, out float floor))
                {
                    insect.Retiring = true;
                    DrawInsect(insect, profile);
                    continue;
                }
                next = _center + direction * Mathf.Max((next - _center).magnitude, Mathf.Max(floor, _seaLevelRadius) + 0.035f);
                if (CharacterMath.TryProjectOntoTangent(next - insect.Position, up, out Vector3 heading))
                    insect.Forward = Vector3.Slerp(insect.Forward, heading, 1f - Mathf.Exp(-6f * dt));
                insect.Position = next;
                if (t >= 1f)
                    insect.Rest = Mathf.Lerp(2f, 6f, ScatterHash.To01(ScatterHash.Mix(++insect.Seed)));
            }
            DrawInsect(insect, profile);
        }
    }

    void DrawInsect(Insect insect, AmbientSwarmProfile profile)
    {
        Material material = EnsureMaterial(profile);
        if (material == null) return;
        if (_insectMesh == null)
        {
            _insectMesh = new Mesh { name = "Pollinator wings" };
            _insectMesh.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0, 0, -0.5f),
                new Vector3(0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0, 0, 0.5f), new Vector3(0.5f, 0, 0.5f) };
            _insectMesh.uv = new[] { new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(1, 0),
                new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(1, 1) };
            _insectMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white, Color.white, Color.white };
            _insectMesh.triangles = new[] { 0, 3, 1, 1, 3, 4, 1, 4, 2, 2, 4, 5 };
            _insectMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        }
        Vector3 up = (insect.Position - _center).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(insect.Forward, up).normalized;
        if (forward.sqrMagnitude < 0.001f) forward = CharacterMath.ArbitraryTangent(up);
        float rate = insect.Kind == AmbientSwarmKind.Bees ? 35f : 9f;
        float angle = insect.Rest > 0f ? 1.2f + Mathf.Sin(insect.Age * 1.5f) * 0.1f
            : Mathf.Sin(insect.Age * rate * Mathf.PI * 2f + insect.Seed % 31) * 1.1f;
        Color tint = profile.Color;
        bool textured = insect.Kind == AmbientSwarmKind.Butterflies && _butterflyMesh != null &&
            _butterflyTextures != null && _butterflyTextures[insect.ArtVariant] != null;
        // The imported mesh carries five wing poses. Each atlas row reveals one pose; do not bend it again.
        _insectProperties.SetFloat(UseButterflyAtlasId, textured ? 1f : 0f);
        Mesh mesh = _insectMesh;
        Quaternion rotation = Quaternion.LookRotation(forward, up);
        float scale = profile.ParticleSize;
        Vector3 position = insect.Position;
        if (textured)
        {
            mesh = _butterflyMesh;
            tint = Color.white;
            angle = 0f;
            _insectProperties.SetTexture(ButterflyAtlasId, _butterflyTextures[insect.ArtVariant]);
            float frame = insect.Rest > 0f ? 0f : Mathf.Floor(Mathf.Repeat(insect.Age * rate + insect.ArtVariant * 0.25f, 1f) * 8f);
            _insectProperties.SetFloat(ButterflyFrameId, frame);
            // The source body's long axis is tilted thirty degrees in its mesh coordinates.
            rotation *= Quaternion.Euler(30f, 0f, 0f);
            scale /= Mathf.Max(mesh.bounds.size.x, 0.001f);
        }
        tint.a *= insect.Alpha;
        _insectProperties.SetColor(TintId, tint);
        _insectProperties.SetFloat(FlapId, angle);
        Graphics.DrawMesh(mesh, Matrix4x4.TRS(position, rotation,
            Vector3.one * scale), material, _parent.gameObject.layer, null, 0, _insectProperties,
            UnityEngine.Rendering.ShadowCastingMode.Off, false);
    }
}
