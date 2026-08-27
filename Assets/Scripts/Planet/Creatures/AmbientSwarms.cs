using System.Collections.Generic;
using UnityEngine;

/// <summary>The three swarms. Each is the same machinery with different numbers and a different anchor.</summary>
public enum AmbientSwarmKind : byte
{
    /// <summary>Daytime, in green biomes, anchored near the observer.</summary>
    Butterflies = 0,

    /// <summary>Night, same placement rule. Bright rather than luminous — see the shader for why.</summary>
    Fireflies = 1,

    /// <summary>Anchored to a carcass rather than to the ground, and only while there is something to eat.</summary>
    Flies = 2,

    /// <summary>A flock overhead, drifting across the sky. Decoration only - the huntable bird is a resident.</summary>
    Birds = 3,
}

/// <summary>
/// What one kind of swarm looks like and where it is allowed to be.
/// </summary>
/// <remarks>
/// A record rather than a ScriptableObject for now: these are placeholder looks on placeholder shapes, and the
/// authoring surface should arrive with the art rather than ahead of it.
/// </remarks>
// ponytail: hard-coded profiles. Promote to a CreatureAmbienceSettings SO + DTO the moment Bryan wants to tune
// colours or counts without a recompile - the DTO shape is already the one this record has.
public sealed record AmbientSwarmProfile(
    AmbientSwarmKind Kind,
    string DisplayName,
    int SwarmCount,
    int Particles,
    float SwarmRadiusMeters,
    float ParticleSize,
    float SpeedMps,
    Color Color,
    bool Additive,
    float MinLocalSun,
    float MaxLocalSun,
    BiomeType[] Biomes,
    float ScatterRadiusMeters,
    float ReturnAfterSeconds,
    float HeightMeters,
    float AnchorDriftMps)
{
    /// <summary>
    /// Whether this kind is out, given how high the sun stands WHERE THE OBSERVER IS: the dot of the local up
    /// against the sun direction, so 1 is overhead, 0 is the horizon and negative is night.
    /// </summary>
    /// <remarks>
    /// Deliberately not a clock reading. On a sphere the global time of day says nothing about whether it is
    /// dark here, and fireflies coming out at noon on the far side of the planet is exactly the bug that
    /// would produce. This is the same quantity the terrain shader lights from.
    /// </remarks>
    public bool ActiveAt(float localSun) => localSun >= MinLocalSun && localSun <= MaxLocalSun;

    public bool LivesIn(BiomeType biome)
    {
        if (Biomes == null || Biomes.Length == 0) return true;
        for (int i = 0; i < Biomes.Length; i++)
            if (Biomes[i] == biome) return true;
        return false;
    }

    public static readonly AmbientSwarmProfile[] Defaults =
    {
        new(AmbientSwarmKind.Butterflies, "Butterflies", SwarmCount: 4, Particles: 14,
            SwarmRadiusMeters: 3.5f, ParticleSize: 0.18f, SpeedMps: 1.1f,
            new Color(0.95f, 0.80f, 0.30f), Additive: false,
            MinLocalSun: 0.15f, MaxLocalSun: 1f,
            new[] { BiomeType.Grassland, BiomeType.Forest, BiomeType.Tropical, BiomeType.Savanna, BiomeType.Swamp },
            ScatterRadiusMeters: 4f, ReturnAfterSeconds: 4f, HeightMeters: 0f, AnchorDriftMps: 0f),

        new(AmbientSwarmKind.Fireflies, "Fireflies", SwarmCount: 5, Particles: 22,
            SwarmRadiusMeters: 5f, ParticleSize: 0.12f, SpeedMps: 0.5f,
            new Color(0.75f, 1.00f, 0.35f), Additive: true,
            MinLocalSun: -1f, MaxLocalSun: 0.02f,
            new[] { BiomeType.Forest, BiomeType.Swamp, BiomeType.Tropical, BiomeType.Taiga, BiomeType.Grassland },
            ScatterRadiusMeters: 3f, ReturnAfterSeconds: 6f, HeightMeters: 0f, AnchorDriftMps: 0f),

        // No biome list and no daylight window: flies go wherever a body is, whenever there is one.
        new(AmbientSwarmKind.Flies, "Flies", SwarmCount: 0, Particles: 26,
            SwarmRadiusMeters: 0.7f, ParticleSize: 0.05f, SpeedMps: 1.6f,
            new Color(0.10f, 0.09f, 0.08f), Additive: false,
            MinLocalSun: -1f, MaxLocalSun: 1f,
            System.Array.Empty<BiomeType>(),
            ScatterRadiusMeters: 6f, ReturnAfterSeconds: 8f, HeightMeters: 0f, AnchorDriftMps: 0f),

        // Overhead, all day, anywhere, and crossing the sky rather than hovering. ScatterRadius 0 means they
        // are never startled: nothing on the ground reaches them, and a flock that panicked at a footstep
        // eighty metres below would read as a bug rather than as wildlife.
        new(AmbientSwarmKind.Birds, "Birds", SwarmCount: 2, Particles: 11,
            SwarmRadiusMeters: 14f, ParticleSize: 0.55f, SpeedMps: 2.2f,
            new Color(0.20f, 0.19f, 0.22f), Additive: false,
            MinLocalSun: -0.05f, MaxLocalSun: 1f,
            System.Array.Empty<BiomeType>(),
            ScatterRadiusMeters: 0f, ReturnAfterSeconds: 1f,
            HeightMeters: 55f, AnchorDriftMps: 5f),
    };
}

/// <summary>
/// Butterflies, fireflies and flies. Decoration, not residents: no identity, no record, no save cost, and
/// nothing here may ever be something the player can interact with.
/// </summary>
/// <remarks>
/// <para>
/// Ambient kinds are anchored NEAR THE OBSERVER and re-anchored as they fall behind, because a butterfly is
/// not a thing anyone tracks — it is atmosphere within a few tens of metres. That is what makes the whole
/// system free: there is no population to simulate, only what is currently in front of you.
/// </para>
/// <para>
/// Flies are the exception, and they are why this exists at all: their anchor is a carcass, so they arrive
/// where something died, stay while there is something to eat, and go when it is down to bones.
/// </para>
/// <para>
/// Scattering reads the THREAT registry rather than the observer position, which keeps section 16's invariant
/// intact: the debug freecam has no identity, so it cannot make a cloud of flies lift off, exactly as it
/// cannot frighten a deer.
/// </para>
/// </remarks>
public sealed class AmbientSwarms : System.IDisposable
{
    /// <summary>Metres from the observer an ambient swarm is placed within. Beyond it, it is re-anchored.</summary>
    const float AnchorRadiusMeters = 30f;
    const float AnchorMinMeters = 8f;

    /// <summary>Metres past which an ambient swarm is left behind rather than followed.</summary>
    const float KeepAnchorMeters = 60f;

    sealed class Swarm
    {
        public AmbientSwarmKind Kind;
        public ulong AnchorId;          // the carcass it is on, or 0 for a free-floating ambient swarm
        public Vector3 Anchor;
        public ParticleSystem System;
        public Vector3 Heading;         // tangent it drifts along, for the kinds that cross the sky
        public bool Scattered;
        public float SettleAtTime;      // unscaled time the scatter ends
    }

    readonly Transform _parent;
    readonly List<Swarm> _swarms = new();
    readonly Dictionary<int, Material> _materials = new();
    readonly List<CreatureCorpse> _corpseScratch = new();
    readonly List<Swarm> _retired = new();
    readonly AmbientSwarmProfile[] _profiles;
    readonly ILogger _log;

    IPlanetSurfaceSampler _sampler;
    IBiomeProvider _biome;
    ThreatRegistry _threats;
    Vector3 _center;
    float _planetRadius;
    float _seaLevelRadius;
    uint _draw;
    bool _configured;
    bool _enabled = true;

    public AmbientSwarms(Transform parent, ILogger log = null, AmbientSwarmProfile[] profiles = null)
    {
        _parent = parent;
        _log = log;
        _profiles = profiles ?? AmbientSwarmProfile.Defaults;
    }

    public int SwarmCount => _swarms.Count;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (!_enabled) ClearSwarms();
        }
    }

    public void Configure(IPlanetSurfaceSampler sampler, IBiomeProvider biome, ThreatRegistry threats,
        Vector3 center, float planetRadius, float seaLevelRadius)
    {
        ClearSwarms();
        _sampler = sampler;
        _biome = biome;
        _threats = threats;
        _center = center;
        _planetRadius = planetRadius;
        _seaLevelRadius = seaLevelRadius;
        _configured = sampler != null && planetRadius > 0f;

        if (!_configured)
            _log?.Log(LogLevel.Info, "Creature", "Ambient swarms inactive: no surface sampler or radius.");
    }

    /// <summary>
    /// One tick. <paramref name="localSun"/> is dot(local up, sun direction) at the observer; corpses may be null.
    /// </summary>
    public void Tick(Vector3 observerWorldPos, float localSun, CreatureCorpseStore corpses, long nowUnixSeconds)
    {
        if (!_configured || !_enabled || _parent == null) return;

        SyncFlies(corpses, nowUnixSeconds);
        SyncAmbient(observerWorldPos, localSun);
        UpdateScatter(nowUnixSeconds);
    }

    // --- flies: anchored to what died ------------------------------------

    void SyncFlies(CreatureCorpseStore corpses, long nowUnixSeconds)
    {
        AmbientSwarmProfile profile = ProfileOf(AmbientSwarmKind.Flies);
        _corpseScratch.Clear();

        if (corpses != null)
        {
            foreach (CreatureCorpse c in corpses.All)
                if (corpses.Decay.HasFlies(c, nowUnixSeconds))
                    _corpseScratch.Add(c);
        }

        for (int i = 0; i < _corpseScratch.Count; i++)
        {
            CreatureCorpse c = _corpseScratch[i];
            Swarm swarm = FindByAnchorId(c.Id.Value);
            if (swarm == null)
            {
                swarm = Spawn(profile, c.Id.Value);
                if (swarm == null) continue;
            }
            swarm.Anchor = c.Position + (c.Position - _center).normalized * 0.4f;
            swarm.System.transform.position = swarm.Anchor;
        }

        // A fly swarm whose body is gone or picked clean is destroyed rather than parked: the count is
        // unbounded in time otherwise, one dead swarm per animal the player has ever killed.
        _retired.Clear();
        for (int i = 0; i < _swarms.Count; i++)
        {
            if (_swarms[i].Kind != AmbientSwarmKind.Flies) continue;
            bool stillFed = false;
            for (int j = 0; j < _corpseScratch.Count && !stillFed; j++)
                stillFed = _corpseScratch[j].Id.Value == _swarms[i].AnchorId;
            if (!stillFed) _retired.Add(_swarms[i]);
        }
        RetireCollected();
    }

    // --- ambient: anchored near whoever is watching ----------------------

    void SyncAmbient(Vector3 observerWorldPos, float localSun)
    {
        for (int p = 0; p < _profiles.Length; p++)
        {
            AmbientSwarmProfile profile = _profiles[p];
            if (profile.Kind == AmbientSwarmKind.Flies) continue;

            bool active = profile.ActiveAt(localSun);

            // Something 55 m up is a long way away before it is out of sight, so its keep radius grows with
            // its height. Without this a flock is retired and replaced every few seconds of drift.
            float keep = KeepAnchorMeters + profile.HeightMeters * 2f;
            float keepSqr = keep * keep;

            // Out of hours, or left behind: retire rather than follow. A butterfly that jumps thirty metres to
            // keep up with you is far more noticeable than one that simply is not there.
            _retired.Clear();
            for (int i = 0; i < _swarms.Count; i++)
            {
                Swarm s = _swarms[i];
                if (s.Kind != profile.Kind) continue;
                if (!active || (s.Anchor - observerWorldPos).sqrMagnitude > keepSqr) _retired.Add(s);
            }
            RetireCollected();

            if (!active) continue;

            for (int have = CountOf(profile.Kind); have < profile.SwarmCount; have++)
            {
                if (!TryPlaceAmbientAnchor(profile, observerWorldPos, out Vector3 anchor)) break;
                Swarm swarm = Spawn(profile, anchorId: 0UL);
                if (swarm == null) break;
                swarm.Anchor = anchor;
                swarm.Heading = HeadingAt(anchor);
                swarm.System.transform.position = anchor;
            }

            if (profile.AnchorDriftMps > 0f) Drift(profile);
        }
    }

    /// <summary>
    /// Move a drifting kind's anchor along the surface. The height is re-derived from the ground under the new
    /// position every step, so a flock crosses a valley at its own altitude instead of flying into the hill on
    /// the far side.
    /// </summary>
    void Drift(AmbientSwarmProfile profile)
    {
        float step = profile.AnchorDriftMps * Time.deltaTime;

        for (int i = 0; i < _swarms.Count; i++)
        {
            Swarm swarm = _swarms[i];
            if (swarm.Kind != profile.Kind) continue;

            Vector3 moved = swarm.Anchor + swarm.Heading * step;
            Vector3 dir = (moved - _center).normalized;
            if (dir.sqrMagnitude < 1e-6f || !_sampler.TryGetSurfaceRadius(dir, out float radius)) continue;

            swarm.Anchor = _center + dir * (Mathf.Max(radius, _seaLevelRadius) + profile.HeightMeters);
            swarm.Heading = HeadingAt(swarm.Anchor, swarm.Heading);
            swarm.System.transform.position = swarm.Anchor;
        }
    }

    // A heading is a tangent, and a tangent stops being one the moment the thing holding it has moved around
    // the sphere. Re-projecting each step is what keeps a long drift from curving into the ground.
    Vector3 HeadingAt(Vector3 anchor, Vector3 previous = default)
    {
        Vector3 up = (anchor - _center).normalized;
        if (previous != default && CharacterMath.TryProjectOntoTangent(previous, up, out Vector3 kept))
            return kept;

        uint h = ScatterHash.Mix(_draw++ ^ 0x85ebca6bu);
        Vector3 tangent = CharacterMath.ArbitraryTangent(up);
        return Quaternion.AngleAxis(ScatterHash.To01(h) * 360f, up) * tangent;
    }

    bool TryPlaceAmbientAnchor(AmbientSwarmProfile profile, Vector3 observerWorldPos, out Vector3 anchor)
    {
        anchor = default;
        Vector3 up = (observerWorldPos - _center).normalized;
        if (up.sqrMagnitude < 1e-6f) return false;

        for (int attempt = 0; attempt < 4; attempt++)
        {
            uint h = ScatterHash.Mix(_draw++ ^ ((uint)profile.Kind * 0x9e3779b1u));
            float angle = ScatterHash.To01(h) * Mathf.PI * 2f;
            float distance = Mathf.Lerp(AnchorMinMeters, AnchorRadiusMeters, ScatterHash.To01(ScatterHash.Slot(h, 1)));

            Vector3 tangent = CharacterMath.ArbitraryTangent(up);
            Vector3 offset = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, up) * tangent * distance;

            Vector3 dir = (observerWorldPos + offset - _center).normalized;
            if (!_sampler.TryGetSurfaceRadius(dir, out float radius)) continue;

            if (_biome != null && profile.Biomes.Length > 0)
            {
                BiomeType biome = _biome.EvaluateBiome(dir, radius / _planetRadius - 1f).PrimaryBiome;
                if (!profile.LivesIn(biome)) continue;
            }

            // Above the waterline. A cloud of butterflies bobbing over open ocean is the giveaway that
            // placement never asked what was underneath it.
            if (radius <= _seaLevelRadius) continue;

            // Off the ground by its own height, or by roughly its own size when it has none, so the cloud is at
            // eye level rather than buried in the hill it was placed on.
            anchor = _center + dir * (radius + Mathf.Max(profile.HeightMeters, profile.SwarmRadiusMeters * 0.6f));
            return true;
        }
        return false;
    }

    // --- reacting to something walking up ---------------------------------

    void UpdateScatter(long nowUnixSeconds)
    {
        if (_threats == null) return;
        float now = Time.unscaledTime;

        for (int i = 0; i < _swarms.Count; i++)
        {
            Swarm swarm = _swarms[i];
            AmbientSwarmProfile profile = ProfileOf(swarm.Kind);

            // Read through the WILDLIFE lens, so a swarm is startled by whatever a deer would be startled by.
            // That is what keeps the debug camera out of it without a special case for cameras.
            bool nearby = _threats.TryFindThreat(swarm.Anchor, EntityId.None, CreatureFaction.Wildlife,
                profile.ScatterRadiusMeters, nowUnixSeconds, out _);

            if (nearby)
            {
                swarm.SettleAtTime = now + profile.ReturnAfterSeconds;
                if (!swarm.Scattered) ApplyScatter(swarm, profile, scattered: true);
            }
            else if (swarm.Scattered && now >= swarm.SettleAtTime)
            {
                ApplyScatter(swarm, profile, scattered: false);
            }
        }
    }

    static void ApplyScatter(Swarm swarm, AmbientSwarmProfile profile, bool scattered)
    {
        swarm.Scattered = scattered;
        if (swarm.System == null) return;

        ParticleSystem.ShapeModule shape = swarm.System.shape;
        shape.radius = scattered ? profile.ScatterRadiusMeters : profile.SwarmRadiusMeters;

        ParticleSystem.MainModule main = swarm.System.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(
            profile.SpeedMps * (scattered ? 3.5f : 0.4f),
            profile.SpeedMps * (scattered ? 6f : 1f));

        // The lift-off itself. Without the burst the cloud only widens as it re-emits, which reads as fog
        // rolling out rather than as flies being disturbed.
        if (scattered) swarm.System.Emit(profile.Particles / 2);
    }

    // --- particle systems --------------------------------------------------

    Swarm Spawn(AmbientSwarmProfile profile, ulong anchorId)
    {
        Material material = EnsureMaterial(profile);
        if (material == null) return null;

        var go = new GameObject(profile.DisplayName);
        go.transform.SetParent(_parent, worldPositionStays: true);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(profile.SpeedMps * 0.4f, profile.SpeedMps);
        main.startSize = new ParticleSystem.MinMaxCurve(profile.ParticleSize * 0.6f, profile.ParticleSize);
        main.startColor = profile.Color;
        main.maxParticles = profile.Particles * 3;

        // World space, so a swarm does not slide when its anchor is nudged - and so the scatter burst leaves
        // particles behind it instead of dragging them along.
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = profile.Particles / 4f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = profile.SwarmRadiusMeters;

        // The flicker. For fireflies it is the whole effect; for the others it reads as wingbeat.
        ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f),
                new GradientAlphaKey(0.7f, 0.7f), new GradientAlphaKey(0f, 1f),
            },
        });

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = profile.SpeedMps;
        noise.frequency = profile.Kind == AmbientSwarmKind.Flies ? 2.5f : 0.6f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        ps.Play();

        var swarm = new Swarm { Kind = profile.Kind, AnchorId = anchorId, System = ps };
        _swarms.Add(swarm);
        return swarm;
    }

    Material EnsureMaterial(AmbientSwarmProfile profile)
    {
        int key = (int)profile.Kind;
        if (_materials.TryGetValue(key, out Material cached) && cached != null) return cached;

        Shader shader = Shader.Find("Hidden/SwarmParticles");
        if (shader == null)
        {
            _log?.Log(LogLevel.Warning, "Creature", "Hidden/SwarmParticles not found; swarms disabled.");
            return null;
        }

        var material = new Material(shader) { name = profile.DisplayName + " (runtime)" };
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)(profile.Additive
            ? UnityEngine.Rendering.BlendMode.One
            : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        material.SetFloat("_Intensity", profile.Additive ? 2.2f : 1f);
        material.SetFloat("_Softness", profile.Additive ? 0.85f : 0.4f);
        _materials[key] = material;
        return material;
    }

    AmbientSwarmProfile ProfileOf(AmbientSwarmKind kind)
    {
        for (int i = 0; i < _profiles.Length; i++)
            if (_profiles[i].Kind == kind) return _profiles[i];
        return _profiles[0];
    }

    int CountOf(AmbientSwarmKind kind)
    {
        int n = 0;
        for (int i = 0; i < _swarms.Count; i++)
            if (_swarms[i].Kind == kind) n++;
        return n;
    }

    Swarm FindByAnchorId(ulong anchorId)
    {
        if (anchorId == 0UL) return null;   // zero is the ambient swarms, which are anchored to nothing
        for (int i = 0; i < _swarms.Count; i++)
            if (_swarms[i].AnchorId == anchorId) return _swarms[i];
        return null;
    }

    void RetireCollected()
    {
        for (int i = 0; i < _retired.Count; i++)
        {
            Swarm swarm = _retired[i];
            if (swarm.System != null) Object.Destroy(swarm.System.gameObject);
            _swarms.Remove(swarm);
        }
        _retired.Clear();
    }

    void ClearSwarms()
    {
        for (int i = 0; i < _swarms.Count; i++)
            if (_swarms[i].System != null) Object.Destroy(_swarms[i].System.gameObject);
        _swarms.Clear();
    }

    public void Dispose()
    {
        ClearSwarms();
        foreach (KeyValuePair<int, Material> kv in _materials)
            if (kv.Value != null) Object.Destroy(kv.Value);
        _materials.Clear();
        _configured = false;
    }
}
