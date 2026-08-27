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
    float AnchorDriftMps,
    float FadeBand)
{
    /// <summary>
    /// HOW MUCH of this kind is out, 0 to 1, given how high the sun stands WHERE THE OBSERVER IS: the dot of
    /// the local up against the sun direction, so 1 is overhead, 0 is the horizon and negative is night.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ramp rather than a switch. As a boolean this turned every firefly in the world on at one instant of
    /// dusk and off again at one instant of dawn, which reads as a bug however good the particles are. Inside
    /// <see cref="FadeBand"/> of either edge the count is scaled, so they arrive a few at a time and thin out
    /// the same way.
    /// </para>
    /// <para>
    /// Deliberately not a clock reading. On a sphere the global time of day says nothing about whether it is
    /// dark here, and fireflies coming out at noon on the far side of the planet is exactly the bug that would
    /// produce. This is the same quantity the terrain shader lights from.
    /// </para>
    /// </remarks>
    public float ActivityAt(float localSun)
    {
        if (localSun < MinLocalSun || localSun > MaxLocalSun) return 0f;
        if (FadeBand <= 0f) return 1f;

        // Distance inside whichever edge is nearer, as a fraction of the band. An edge at the extreme of the
        // range is NOT a boundary - dot() cannot exceed 1 - so it gets no ramp. Without that exception the
        // butterflies faded out at noon, which is the one moment they should be thickest.
        float fromLow = MinLocalSun <= -1f ? 1f : (localSun - MinLocalSun) / FadeBand;
        float fromHigh = MaxLocalSun >= 1f ? 1f : (MaxLocalSun - localSun) / FadeBand;
        return Mathf.Clamp01(Mathf.Min(fromLow, fromHigh));
    }

    /// <summary>True while any of this kind should be out at all.</summary>
    public bool ActiveAt(float localSun) => ActivityAt(localSun) > 0f;

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
            ScatterRadiusMeters: 4f, ReturnAfterSeconds: 4f, HeightMeters: 0f, AnchorDriftMps: 0f,
            FadeBand: 0.20f),

        new(AmbientSwarmKind.Fireflies, "Fireflies", SwarmCount: 9, Particles: 18,
            SwarmRadiusMeters: 5f, ParticleSize: 0.12f, SpeedMps: 0.5f,
            new Color(0.75f, 1.00f, 0.35f), Additive: true,
            MinLocalSun: -1f, MaxLocalSun: 0.02f,
            new[] { BiomeType.Forest, BiomeType.Swamp, BiomeType.Tropical, BiomeType.Taiga, BiomeType.Grassland },
            ScatterRadiusMeters: 3f, ReturnAfterSeconds: 6f, HeightMeters: 0f, AnchorDriftMps: 0f,
            FadeBand: 0.22f),

        // No biome list and no daylight window: flies go wherever a body is, whenever there is one.
        new(AmbientSwarmKind.Flies, "Flies", SwarmCount: 0, Particles: 26,
            SwarmRadiusMeters: 0.7f, ParticleSize: 0.11f, SpeedMps: 1.6f,
            new Color(0.13f, 0.12f, 0.10f), Additive: false,
            MinLocalSun: -1f, MaxLocalSun: 1f,
            System.Array.Empty<BiomeType>(),
            ScatterRadiusMeters: 6f, ReturnAfterSeconds: 8f, HeightMeters: 0f, AnchorDriftMps: 0f,
            FadeBand: 0f),

        // Overhead, all day, anywhere, and crossing the sky rather than hovering. ScatterRadius 0 means they
        // are never startled: nothing on the ground reaches them, and a flock that panicked at a footstep
        // eighty metres below would read as a bug rather than as wildlife.
        new(AmbientSwarmKind.Birds, "Birds", SwarmCount: 2, Particles: 11,
            SwarmRadiusMeters: 14f, ParticleSize: 0.55f, SpeedMps: 2.2f,
            new Color(0.20f, 0.19f, 0.22f), Additive: false,
            MinLocalSun: -0.05f, MaxLocalSun: 1f,
            System.Array.Empty<BiomeType>(),
            ScatterRadiusMeters: 0f, ReturnAfterSeconds: 1f,
            HeightMeters: 55f, AnchorDriftMps: 5f, FadeBand: 0.15f),
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
        public bool Retiring;           // no longer emitting, waiting for its last particle to fade
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

    /// <summary>The profiles this instance is running, so a readout can say what each kind WANTS.</summary>
    public AmbientSwarmProfile[] Profiles => _profiles;

    /// <summary>How many of a kind are up and emitting. Excludes the ones fading out.</summary>
    public int CountLive(AmbientSwarmKind kind) => CountOf(kind);

    /// <summary>
    /// The biome under a world position, evaluated through the SAME provider placement uses.
    /// </summary>
    /// <remarks>
    /// Exposed because a readout that resolves its own biome provider can disagree with the one that actually
    /// decides where swarms go. `ServiceLocator` does not carry an `IBiomeProvider` at all - this one is
    /// injected at Configure - so a console command that asked the locator got a silent fallback and reported
    /// a biome that was not the one being tested against.
    /// </remarks>
    public bool TryBiomeAt(Vector3 worldPos, out BiomeType biome)
    {
        biome = default;
        if (_biome == null || _sampler == null) return false;

        PlanetTransformSnapshot planet = PlanetTransformSnapshot.Capture(_parent);
        Vector3 dir = (worldPos - _center).normalized;
        if (dir.sqrMagnitude < 1e-6f || !_sampler.TryGetSurfaceRadius(dir, out float radius)) return false;

        float localRadius = radius / Mathf.Max(planet.UniformScale, 1e-4f);
        biome = _biome.EvaluateBiome(planet.InverseTransformDirection(dir),
            localRadius / _planetRadius - 1f).PrimaryBiome;
        return true;
    }

    public int CountParticles(AmbientSwarmKind kind)
    {
        int n = 0;
        for (int i = 0; i < _swarms.Count; i++)
            if (_swarms[i].Kind == kind && _swarms[i].System != null) n += _swarms[i].System.particleCount;
        return n;
    }

    /// <summary>
    /// Distance to the nearest of a kind, and how far above the ground it sits. Both are what someone standing
    /// in the world needs to be told: which way to walk, and whether to look up.
    /// </summary>
    public bool TryNearest(AmbientSwarmKind kind, Vector3 from, out float metres, out float heightMeters)
    {
        metres = float.MaxValue;
        heightMeters = 0f;
        bool found = false;

        for (int i = 0; i < _swarms.Count; i++)
        {
            Swarm s = _swarms[i];
            if (s.Kind != kind || s.Retiring) continue;

            float d = CreatureTerritory.SurfaceDistance(_center, s.Anchor, from);
            if (d >= metres) continue;

            metres = d;
            heightMeters = (s.Anchor - _center).magnitude - (from - _center).magnitude;
            found = true;
        }
        return found;
    }

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

        SyncFlies(corpses, observerWorldPos, nowUnixSeconds);
        SyncAmbient(observerWorldPos, localSun);
        UpdateScatter(nowUnixSeconds);
        SweepFaded();
    }

    // --- flies: anchored to what died ------------------------------------

    void SyncFlies(CreatureCorpseStore corpses, Vector3 observerWorldPos, long nowUnixSeconds)
    {
        AmbientSwarmProfile profile = ProfileOf(AmbientSwarmKind.Flies);
        _corpseScratch.Clear();

        if (corpses != null)
        {
            // Bounded by the same radius the BODY is drawn within. Without it every carcass in the save keeps
            // a live particle system, and a cloud of flies hangs in the air a quarter of a kilometre away with
            // nothing underneath it, because the body it belongs to is out of draw range.
            foreach (CreatureCorpse c in corpses.All)
                if (corpses.Decay.HasFlies(c, nowUnixSeconds) &&
                    KeptDistance(c.Position, observerWorldPos) <= CreatureCorpseStore.KeepAliveMeters)
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
            PlaceAt(swarm, c.Position + (c.Position - _center).normalized * 0.4f);
        }

        // A fly swarm whose body is gone or picked clean is destroyed rather than parked: the count is
        // unbounded in time otherwise, one dead swarm per animal the player has ever killed.
        _retired.Clear();
        for (int i = 0; i < _swarms.Count; i++)
        {
            if (_swarms[i].Kind != AmbientSwarmKind.Flies || _swarms[i].Retiring) continue;
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

            // How many of this kind the sun currently wants. Rounding a ramped count is what makes them
            // arrive a few at a time: one swarm, then two, then the lot, and back down the same way.
            int want = Mathf.RoundToInt(profile.SwarmCount * profile.ActivityAt(localSun));

            // Something 55 m up is a long way away before it is out of sight, so its keep radius grows with
            // its height. Without this a flock is retired and replaced every few seconds of drift.
            float keep = KeepAnchorMeters + profile.HeightMeters * 2f;

            // Left behind: retire rather than follow. A butterfly that jumps thirty metres to keep up with you
            // is far more noticeable than one that simply is not there.
            _retired.Clear();
            int live = 0;
            for (int i = 0; i < _swarms.Count; i++)
            {
                Swarm s = _swarms[i];
                if (s.Kind != profile.Kind || s.Retiring) continue;
                if (KeptDistance(s.Anchor, observerWorldPos) > keep) { _retired.Add(s); continue; }
                live++;
            }

            // Then thin down to what the sun wants, oldest first, so the last few out are the last few in.
            for (int i = 0; i < _swarms.Count && live > want; i++)
            {
                Swarm s = _swarms[i];
                if (s.Kind != profile.Kind || s.Retiring || _retired.Contains(s)) continue;
                _retired.Add(s);
                live--;
            }
            RetireCollected();

            for (int have = live; have < want; have++)
            {
                if (!TryPlaceAmbientAnchor(profile, observerWorldPos, out Vector3 anchor)) break;

                // Placement finds ground; it does not know how far above that ground the observer is. From
                // orbit every anchor is thousands of metres below, so without this the loop spawned a swarm,
                // the keep test retired it on the same tick, and the pair repeated every frame forever.
                if (KeptDistance(anchor, observerWorldPos) > keep) break;

                Swarm swarm = Spawn(profile, anchorId: 0UL);
                if (swarm == null) break;
                PlaceAt(swarm, anchor);
                swarm.Heading = HeadingAt(anchor);
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
            if (swarm.Kind != profile.Kind || swarm.Retiring) continue;

            Vector3 moved = swarm.Anchor + swarm.Heading * step;
            Vector3 dir = (moved - _center).normalized;
            if (dir.sqrMagnitude < 1e-6f || !_sampler.TryGetSurfaceRadius(dir, out float radius)) continue;

            PlaceAt(swarm, _center + dir * (Mathf.Max(radius, _seaLevelRadius) + profile.HeightMeters));
            swarm.Heading = HeadingAt(swarm.Anchor, swarm.Heading);
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

        // The sampler answers in WORLD units and the biome field is planet-LOCAL. Converting once here rather
        // than mixing the two is what CreatureResidencyService.TryFindHome does, and a swarm that disagreed
        // with the ground about which biome it is over would be a silent wrong answer, not a visible failure.
        PlanetTransformSnapshot planet = PlanetTransformSnapshot.Capture(_parent);
        float scale = Mathf.Max(planet.UniformScale, 1e-4f);

        for (int attempt = 0; attempt < 4; attempt++)
        {
            uint h = ScatterHash.Mix(_draw++ ^ ((uint)profile.Kind * 0x9e3779b1u));
            float angle = ScatterHash.To01(h) * Mathf.PI * 2f;
            float distance = Mathf.Lerp(AnchorMinMeters, AnchorRadiusMeters, ScatterHash.To01(ScatterHash.Slot(h, 1)));

            Vector3 tangent = CharacterMath.ArbitraryTangent(up);
            Vector3 offset = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, up) * tangent * distance;

            Vector3 dir = (observerWorldPos + offset - _center).normalized;
            if (!_sampler.TryGetSurfaceRadius(dir, out float radius)) continue;

            float localRadius = radius / scale;

            // Above the waterline. A cloud of butterflies bobbing over open ocean is the giveaway that
            // placement never asked what was underneath it. Compared against the WORLD radius, which is what
            // PlanetSurfaceGrounding does with the same value — the two must agree or a swarm hovers over
            // water the character is walking on.
            if (radius <= _seaLevelRadius) continue;

            if (_biome != null && profile.Biomes.Length > 0)
            {
                Vector3 localDir = planet.InverseTransformDirection(dir);
                BiomeType biome = _biome.EvaluateBiome(localDir, localRadius / _planetRadius - 1f).PrimaryBiome;
                if (!profile.LivesIn(biome)) continue;
            }

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
            if (swarm.Retiring) continue;
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

    /// <summary>
    /// How far a swarm is from the observer for the purpose of keeping it, measured ALONG THE SURFACE plus
    /// whatever height it holds.
    /// </summary>
    /// <remarks>
    /// Straight-line distance is wrong here for the same reason it was wrong for creature promotion: an
    /// observer three hundred metres up is a full keep-radius from a swarm directly beneath them, so every
    /// ambient swarm was placed and retired again on the frame it was born. Flying over a meadow showed a
    /// handful of fireflies churning instead of a meadow full of them.
    /// </remarks>
    float KeptDistance(Vector3 anchor, Vector3 observerWorldPos)
    {
        float along = CreatureTerritory.SurfaceDistance(_center, anchor, observerWorldPos);
        float vertical = Mathf.Abs((anchor - _center).magnitude - (observerWorldPos - _center).magnitude);
        return Mathf.Max(along, vertical);
    }

    /// <summary>
    /// Move a swarm and ORIENT it to the surface, so its local +Y is the local up. The particle motion is
    /// authored in local space - rising, circling overhead - and without this every one of those axes is
    /// wrong everywhere except the one point on the planet where local up happens to be world up.
    /// </summary>
    void PlaceAt(Swarm swarm, Vector3 anchor)
    {
        swarm.Anchor = anchor;
        if (swarm.System == null) return;

        Vector3 up = (anchor - _center).normalized;
        Vector3 forward = CharacterMath.ArbitraryTangent(up);
        swarm.System.transform.SetPositionAndRotation(anchor, Quaternion.LookRotation(forward, up));
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
        main.startSpeed = new ParticleSystem.MinMaxCurve(profile.SpeedMps * 0.4f, profile.SpeedMps);
        main.startSize = new ParticleSystem.MinMaxCurve(profile.ParticleSize * 0.6f, profile.ParticleSize);
        main.startColor = profile.Color;
        main.maxParticles = profile.Particles * 3;
        main.gravityModifier = 0f;

        // LOCAL space, so orbital velocity has a centre to orbit and so a drifting flock moves as one body.
        // The swarm's transform is oriented to the surface at its anchor, which makes local +Y the local UP -
        // without that, "rise slowly" and "circle overhead" both mean the wrong axis everywhere but one spot
        // on the planet.
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        ParticleSystem.EmissionModule emission = ps.emission;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = profile.SwarmRadiusMeters;

        ApplyMotion(ps, profile);

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

    /// <summary>
    /// How one kind moves. A switch rather than six more numbers on the profile, because these are not four
    /// settings of one behaviour — a firefly hovers and blinks, a butterfly bobs and wanders, a fly jitters,
    /// a flock wheels. Sharing one emitter and one noise value is what made all four read as particles.
    /// </summary>
    static void ApplyMotion(ParticleSystem ps, AmbientSwarmProfile profile)
    {
        ParticleSystem.MainModule main = ps.main;
        ParticleSystem.EmissionModule emission = ps.emission;
        ParticleSystem.ShapeModule shape = ps.shape;
        ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
        ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
        ParticleSystem.NoiseModule noise = ps.noise;
        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;

        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        noise.enabled = true;
        noise.quality = ParticleSystemNoiseQuality.Medium;
        color.enabled = true;

        // Every axis of a velocity group is set together, through the helpers below. Unity validates the three
        // as a UNIT: give one a two-constant range and leave the others at their default single constant and
        // it rejects the WHOLE module - logging "Particle Velocity curves must all be in the same mode" every
        // frame and silently applying none of the motion. That is exactly what happened the first time.

        switch (profile.Kind)
        {
            case AmbientSwarmKind.Fireflies:
                // Long-lived, nearly still, and BLINKING. The blink is what stops it reading as a particle
                // system: a dot that pulses on its own schedule is doing something, and randomised lifetimes
                // put every one of them out of phase with the others.
                main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 11f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
                emission.rateOverTime = profile.Particles / 7f;

                // A slow circle around the anchor with a slight inward pull, so they hold together as a
                // cluster near one spot instead of dispersing the way a plain emitter does.
                Orbit(ps, -0.22f, 0.22f);
                velocity.radial = new ParticleSystem.MinMaxCurve(-0.06f, 0.02f);
                Lift(ps, 0.02f, 0.16f);                                      // they drift upward, gently

                limit.enabled = true;
                limit.limit = new ParticleSystem.MinMaxCurve(0.5f);
                limit.dampen = 0.35f;                                        // hover rather than fly off

                noise.strength = 0.35f;
                noise.frequency = 0.18f;                                     // long, lazy wander
                noise.octaveCount = 2;
                noise.scrollSpeed = 0.12f;

                color.color = Blink();
                size.enabled = true;
                size.size = BlinkSize();
                break;

            case AmbientSwarmKind.Butterflies:
                // Erratic but going somewhere: a bobbing vertical, a wide wander, and a pull back in so they
                // circle a patch instead of leaving it.
                main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 12f);
                emission.rateOverTime = profile.Particles / 6f;

                Orbit(ps, -0.5f, 0.5f);
                velocity.radial = new ParticleSystem.MinMaxCurve(-0.15f, 0.1f);
                Flutter(ps, Bob());                                          // the bob, on all three axes

                limit.enabled = true;
                limit.limit = new ParticleSystem.MinMaxCurve(1.6f);
                limit.dampen = 0.2f;

                noise.strength = 0.9f;
                noise.frequency = 0.9f;
                noise.octaveCount = 2;
                noise.scrollSpeed = 0.5f;

                // The wingbeat: the two-lobed sprite spun about its own axis flashes edge-on and back. Each
                // starts at its own angle and turns at its own rate, so a cluster never pulses in unison.
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                ParticleSystem.RotationOverLifetimeModule spin = ps.rotationOverLifetime;
                spin.enabled = true;
                spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

                color.color = FadeInOut();
                break;

            case AmbientSwarmKind.Birds:
                // A wheeling flock: almost all orbit, almost no noise. Noise is what makes a flock look like
                // litter blowing about.
                main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 26f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.2f);
                emission.rateOverTime = profile.Particles / 12f;

                Orbit(ps, 0.35f, 0.7f);                                      // all one way round
                velocity.radial = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
                Lift(ps, -0.15f, 0.15f);

                noise.strength = 0.25f;
                noise.frequency = 0.1f;
                noise.octaveCount = 1;

                color.color = FadeInOut();
                break;

            default:
                // Flies. Jitter IS the correct read here — the only kind that should look like noise.
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 3f);
                emission.rateOverTime = profile.Particles / 2f;

                Orbit(ps, -1.5f, 1.5f);
                velocity.radial = new ParticleSystem.MinMaxCurve(-0.4f, 0.2f);
                Lift(ps, -0.3f, 0.3f);

                limit.enabled = true;
                limit.limit = new ParticleSystem.MinMaxCurve(2.5f);
                limit.dampen = 0.1f;

                noise.strength = 1.8f;
                noise.frequency = 3.5f;
                noise.octaveCount = 2;
                noise.scrollSpeed = 1.5f;

                color.color = FadeInOut();
                break;
        }
    }

    // All three orbital axes together, in one mode. Only Y is ever non-zero: the swarm's transform is oriented
    // to the surface, so local Y is the local up and orbiting it is "circling above this spot".
    static void Orbit(ParticleSystem ps, float min, float max)
    {
        ParticleSystem.VelocityOverLifetimeModule v = ps.velocityOverLifetime;
        v.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        v.orbitalY = new ParticleSystem.MinMaxCurve(min, max);
        v.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
    }

    // All three linear axes together, in one mode. Y is up because of that same orientation.
    static void Lift(ParticleSystem ps, float min, float max)
    {
        ParticleSystem.VelocityOverLifetimeModule v = ps.velocityOverLifetime;
        v.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        v.y = new ParticleSystem.MinMaxCurve(min, max);
        v.z = new ParticleSystem.MinMaxCurve(0f, 0f);
    }

    // A curve on the vertical needs curves on the other two as well, or the mode disagrees and the module is
    // thrown away whole. The flat ones are genuinely zero, they are just spelled as curves.
    static void Flutter(ParticleSystem ps, AnimationCurve vertical)
    {
        ParticleSystem.VelocityOverLifetimeModule v = ps.velocityOverLifetime;
        AnimationCurve flat = AnimationCurve.Constant(0f, 1f, 0f);
        v.x = new ParticleSystem.MinMaxCurve(1f, flat);
        v.y = new ParticleSystem.MinMaxCurve(1f, vertical);
        v.z = new ParticleSystem.MinMaxCurve(1f, flat);
    }

    // Ordinary appear-and-vanish, so nothing pops into existence at full brightness.
    static ParticleSystem.MinMaxGradient FadeInOut() => new(new Gradient
    {
        colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
        alphaKeys = new[]
        {
            new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f),
            new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f),
        },
    });

    // Three pulses across a lifetime. Eight keys is Unity's limit for a gradient, and this spends all of them.
    static ParticleSystem.MinMaxGradient Blink() => new(new Gradient
    {
        colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
        alphaKeys = new[]
        {
            new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.10f),
            new GradientAlphaKey(0.06f, 0.28f), new GradientAlphaKey(1f, 0.45f),
            new GradientAlphaKey(0.06f, 0.62f), new GradientAlphaKey(1f, 0.78f),
            new GradientAlphaKey(0.06f, 0.92f), new GradientAlphaKey(0f, 1f),
        },
    });

    // Swelling with the blink. A firefly's glow grows as well as brightens, and matching the two is most of
    // what sells it as a light rather than as a dot being faded.
    static ParticleSystem.MinMaxCurve BlinkSize() => new(1f, new AnimationCurve(
        new Keyframe(0f, 0.35f), new Keyframe(0.10f, 1f), new Keyframe(0.28f, 0.45f),
        new Keyframe(0.45f, 1f), new Keyframe(0.62f, 0.45f), new Keyframe(0.78f, 1f),
        new Keyframe(0.92f, 0.45f), new Keyframe(1f, 0.3f)));

    // A slow up-and-down over the whole life, which is the flutter without a per-particle wing simulation.
    static AnimationCurve Bob() => new(
        new Keyframe(0f, 0.2f), new Keyframe(0.2f, -0.5f), new Keyframe(0.45f, 0.6f),
        new Keyframe(0.7f, -0.4f), new Keyframe(1f, 0.3f));

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

        // A butterfly at this size is a bead unless its silhouette says otherwise. Two lobes plus the tumble
        // below is the cheapest thing that reads as wings rather than as a dot.
        material.SetFloat("_Wings", profile.Kind == AmbientSwarmKind.Butterflies ? 1f : 0f);
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
            if (_swarms[i].Kind == kind && !_swarms[i].Retiring) n++;
        return n;
    }

    Swarm FindByAnchorId(ulong anchorId)
    {
        if (anchorId == 0UL) return null;   // zero is the ambient swarms, which are anchored to nothing
        for (int i = 0; i < _swarms.Count; i++)
            if (_swarms[i].AnchorId == anchorId && !_swarms[i].Retiring) return _swarms[i];
        return null;
    }

    /// <summary>
    /// Stop a swarm emitting and let what is already in the air live out its lifetime. Destroying the object
    /// outright deletes every particle in the same frame, which is the pop-out half of the problem the sun
    /// ramp fixes the other half of.
    /// </summary>
    void RetireCollected()
    {
        for (int i = 0; i < _retired.Count; i++)
        {
            Swarm swarm = _retired[i];
            swarm.Retiring = true;
            if (swarm.System != null) swarm.System.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        _retired.Clear();
    }

    // Destroy the retiring swarms whose last particle has faded. Cheap, and it is the only place a swarm
    // object is actually deleted while the system is running.
    void SweepFaded()
    {
        for (int i = _swarms.Count - 1; i >= 0; i--)
        {
            Swarm swarm = _swarms[i];
            if (!swarm.Retiring) continue;
            if (swarm.System != null && swarm.System.particleCount > 0) continue;

            if (swarm.System != null) Object.Destroy(swarm.System.gameObject);
            _swarms.RemoveAt(i);
        }
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
