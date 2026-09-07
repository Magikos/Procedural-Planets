using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wildlife that stays where you left it. Three questions are kept apart on purpose, because conflating them
/// is what makes creatures behave like traffic in a driving game:
/// <list type="bullet">
/// <item>BIRTH is owned by <see cref="CreatureTerritory"/> - derived from the seed, zero storage.</item>
/// <item>LIFE SUPPORT is owned by observation. A creature is simulated while it is inside an observer's
/// bubble, and NOT while it is near its spawner. Chasing an animal a kilometre from its valley is therefore
/// not a special case: it is in your bubble, so it is simulated.</item>
/// <item>ABSENCE demotes, never deletes. An unobserved creature stops being ticked and is fast-forwarded on
/// re-observation - it drifted home while you were away - so it costs nothing and still feels continuous.</item>
/// </list>
/// Existence is decided by the slot's death record alone. Nothing here may delete a creature to save work.
/// </summary>
/// <remarks>
/// <para>
/// This is authority code: it reads no camera, no input device and no GPU. The observer is a POSITION, handed
/// in by the host, because a dedicated server has player positions and no cameras.
/// </para>
/// <para>
/// Untouched creatures need no record. Displacement, behavior, damage, and death use the existing delta log.
/// </para>
/// </remarks>
[CommandPrefix("creature")]
public sealed class CreatureResidencyService : IDisposable
{
    /// <summary>What a renderer needs, and nothing more. The service never touches a GameObject itself.</summary>
    public readonly struct LiveCreature
    {
        public readonly EntityId Id;
        public readonly int SpeciesIndex;
        public readonly Vector3 Position;
        public readonly Vector3 Up;
        public readonly Vector3 Forward;
        public readonly Vector3 Velocity;
        public readonly CreatureBehaviour Behaviour;
        public readonly float AltitudeMeters;

        public LiveCreature(EntityId id, int speciesIndex, Vector3 position, Vector3 up, Vector3 forward, Vector3 velocity = default,
            CreatureBehaviour behaviour = CreatureBehaviour.Wander, float altitudeMeters = 0f)
        {
            Id = id;
            SpeciesIndex = speciesIndex;
            Position = position;
            Up = up;
            Forward = forward;
            Velocity = velocity;
            Behaviour = behaviour;
            AltitudeMeters = altitudeMeters;
        }
    }

    sealed class Resident
    {
        public EntityId Slot;
        public EntityId Id;             // slot plus the generation currently filling it
        public int SpeciesIndex;
        public int Generation;
        public Vector3 Home;            // world position, on the surface
        public Vector3 Position;        // last known world position, wherever it was left
        public Vector3 Forward;
        public long LastSimulatedUnix;  // when the position above was last true

        // What it was DOING, kept as a value across demotion. The brain is a live object and dies with the
        // simulation; this survives it and rebuilds the brain on promotion.
        public CreatureBehaviour Behaviour;

        public SurfaceCharacterController Driver;   // non-null only while simulated
        public FlightGrounding Flight;              // non-null only for a flier, and only while simulated
        public CreatureBrain Brain;                 // rebuilt from Behaviour at promotion
        public bool HasLandingTarget;
        public Vector3 LandingTarget;
        public ulong LandingSiteId;
        public Vector3 TakeoffFrom;
        public float TakeoffAltitude;
        public double LandingScanSeconds;
        public uint Tick;

        public int Health;
        public ActorNeeds Needs;
        public double SecondsSinceCheckpoint;

        // Being hit makes the attacker a threat for a while whatever the faction table says. Without it an
        // animal the friendly spell has disarmed stands still while you club it.
        public long AlarmUntilUnix;
        public Vector3 AlarmFrom;

        // What the log currently says about this slot, so a demote that would rewrite the same thing does not.
        public bool Written;
        public Vector3 WrittenPosition;
        public CreatureBehaviour WrittenBehaviour;
        public int WrittenHealth;
        public ActorNeeds WrittenNeeds;

        public bool IsLive => Driver != null;
    }

    // The bubble is re-planned on this cadence even when nobody moves, so a death that lapses while you stand
    // still repopulates its slot without waiting for you to walk.
    const float PlanIntervalSeconds = 1f;
    const float PlanMoveMeters = 40f;          // the same travel gate ScatterTileCache re-plans on

    // Metres per second a flier changes altitude by. Slow enough to read as a descent rather than a drop.
    const float ClimbSpeedMps = 3.5f;

    // Seconds a struck animal treats its attacker as a threat. Long enough to clear the awareness radius at
    // panic speed, short enough that it goes back to grazing rather than running forever.
    const long AlarmSeconds = 8L;
    const float DemoteHysteresis = 1.15f;      // demote further out than we promote, so a creature on the
                                               // boundary does not flicker between tiers
    const float DefaultBubbleMeters = 300f;

    readonly Transform _planetTransform;
    readonly IPlanetSurfaceSampler _sampler;
    readonly ILogger _log;
    readonly Func<long> _unixClock;

    // A resident is kept once its territory has been visited, forever: it remembers where the creature was
    // left, which is what a later re-observation fast-forwards from. Dropping it would silently teleport the
    // animal home. The ceiling is the lattice - 1,536 territories times the species' slots - which is a few
    // thousand small objects for a player who has walked the whole planet.
    readonly Dictionary<ulong, Resident> _bySlot = new();
    readonly List<Resident> _all = new();
    readonly List<LiveCreature> _live = new();
    readonly List<Vector3Int> _cells = new();
    readonly FaceSpaceCell[] _ranges = new FaceSpaceCell[FaceSpaceCellRangeBuilder.MaxRanges];

    // Slots whose territory holds nowhere the species will settle. Suitability is seed-derived and never
    // changes, so a rejection is permanent and worth remembering - see the note in ResolveSlot.
    readonly HashSet<ulong> _barren = new();

    // Where each species' slots start inside the territory's shared slot space. Rebuilt whenever the library
    // changes; see BuildSlotBases for why species cannot simply all count from zero.
    int[] _slotBase = System.Array.Empty<int>();

    IWorldDeltaLog _delta;
    ISeedProvider _seeds;
    IBiomeProvider _biome;
    ThreatRegistry _threats = new();
    CreatureCorpseStore _corpses;
    CreatureLibraryDto _library;

    // Both capabilities are stateless positional queries, so every creature shares one pair rather than
    // allocating its own on each promotion.
    IGravityProvider _gravity;
    IGroundingProvider _grounding;
    BirdLandingGround _birdLandingGround;
    public WildlifeLandingTargets LandingTargets { get; } = new();

    Vector3 _center;
    float _planetRadius;
    float _seaLevelRadius;
    int _seed;
    bool _configured;

    double _secondsSincePlan = double.PositiveInfinity;

    // Where the observer is now, and where the standing plan was built from. They are separate because
    // forcing a re-plan must not blind the console to where the player is standing.
    Vector3 _observerPos;
    Vector3 _planAnchor = new(float.MaxValue, float.MaxValue, float.MaxValue);

    float _lastPlanMs;
    int _lastPlanCells;
    long _debugThreatUntil;

    /// <summary>Creatures currently simulated, for a renderer to draw. Rebuilt each tick.</summary>
    public IReadOnlyList<LiveCreature> Live => _live;

    /// <summary>The species snapshot this service is running on. Cached at configure and on a settings change,
    /// so a per-frame consumer does not re-resolve it.</summary>
    public CreatureLibraryDto Library => _library;

    /// <summary>The carcasses this world is carrying. Null until Configure is given one.</summary>
    public CreatureCorpseStore Corpses => _corpses;

    public int ResidentCount => _all.Count;
    public int LiveCount => _live.Count;

    public CreatureResidencyService(Transform planetTransform, IPlanetSurfaceSampler sampler, ILogger log,
        Func<long> unixClock = null)
    {
        _planetTransform = planetTransform;
        _sampler = sampler;
        _log = log;
        _unixClock = unixClock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        EventBus<SettingsChangedEvent>.Listen(OnSettingsChanged);
        ConsoleRegistry.RegisterInstance(this);
    }

    public void Dispose()
    {
        FlushState();
        EventBus<SettingsChangedEvent>.Unlisten(OnSettingsChanged);
        ConsoleRegistry.UnregisterInstance(typeof(CreatureResidencyService));
        _bySlot.Clear();
        _all.Clear();
        _live.Clear();
        _configured = false;
        LandingTargets.Clear();
    }

    /// <summary>Checkpoint actor values before a clean save or shutdown.</summary>
    public void FlushState()
    {
        if (!_configured) return;
        foreach (Resident resident in _all) Remember(resident, resident.LastSimulatedUnix);
    }

    /// <summary>
    /// Point the service at a generated world. Every resident is dropped: a new world's seed puts different
    /// creatures in different places, and carrying one world's residents into another is the bug that makes a
    /// regenerated planet keep the old planet's wildlife.
    /// </summary>
    public void Configure(int seed, IWorldDeltaLog deltaLog, IBiomeProvider biome, ThreatRegistry threats,
        float planetRadius, float seaLevelRadius, CreatureCorpseStore corpses = null)
    {
        _seed = seed;
        LandingTargets.Clear();
        _delta = deltaLog;
        _biome = biome;
        _threats = threats ?? new ThreatRegistry();
        _corpses = corpses;
        _planetRadius = planetRadius;
        _seaLevelRadius = seaLevelRadius;
        _center = _planetTransform != null ? _planetTransform.position : Vector3.zero;
        _threats.ConfigureTerrain(_sampler, _center);
        _seeds = ServiceLocator.Get<ISeedProvider>();
        _gravity = new RadialGravityProvider(_center);
        ServiceLocator.TryGet(out IWaterQueryService water);
        _grounding = new PlanetSurfaceGrounding(_sampler, _center, new CharacterWaterFloor(water, _center));
        _birdLandingGround = new BirdLandingGround(_sampler, _center, seaLevelRadius,
            new CharacterWaterFloor(water, _center));
        _library = SettingsProvider.IsRegistered<CreatureLibraryDto>()
            ? SettingsProvider.GetSettings<CreatureLibraryDto>()
            : CreatureLibraryDto.Placeholder;
        _threats.SetRelations(SettingsProvider.IsRegistered<FactionRelationsDto>()
            ? SettingsProvider.GetSettings<FactionRelationsDto>()
            : FactionRelationsDto.Default);
        BuildSlotBases();

        _bySlot.Clear();
        _all.Clear();
        _live.Clear();
        _barren.Clear();
        _configured = _sampler != null && _planetRadius > 0f && _library.Count > 0;
        Invalidate();

        if (!_configured)
            _log?.Log(LogLevel.Info, "Creature", "Residency inactive: no surface sampler, radius or species.");
    }

    void OnSettingsChanged(SettingsChangedEvent evt)
    {
        if (evt.DtoType != typeof(CreatureLibraryDto)) return;
        _library = SettingsProvider.GetSettings<CreatureLibraryDto>();
        BuildSlotBases();
        // Barren slots are cached against the OLD species data. Retuning an altitude band or a biome list
        // makes a rejection stale, so the cache goes with the settings that produced it.
        _barren.Clear();
        Invalidate();   // an expiry change must take effect now, not at the next 40 m of travel
    }

    /// <summary>Force the next tick to re-resolve every territory in the bubble.</summary>
    public void Invalidate() => _secondsSincePlan = double.PositiveInfinity;

    /// <summary>
    /// Advance one frame. <paramref name="observerWorldPos"/> is a player position - the host resolves it, so
    /// nothing below this line knows a camera exists.
    /// </summary>
    public void Tick(Vector3 observerWorldPos, float deltaTime)
    {
        if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (!_configured) return;

        long now = NowUnixSeconds();
        float bubble = BubbleMeters;
        _observerPos = observerWorldPos;
        _threats.PruneExpired(now);
        _corpses?.Tick(observerWorldPos, now);

        // The debug threat follows the camera while it lasts, so you can walk it at a herd.
        if (_debugThreatUntil > 0L)
        {
            if (now < _debugThreatUntil) _threats.Report(DebugThreatId, observerWorldPos, CreatureFaction.Player);
            else { _threats.Withdraw(DebugThreatId); _debugThreatUntil = 0L; }
        }

        _secondsSincePlan += deltaTime;
        if (_secondsSincePlan >= PlanIntervalSeconds ||
            (observerWorldPos - _planAnchor).sqrMagnitude > PlanMoveMeters * PlanMoveMeters)
        {
            Plan(observerWorldPos, bubble, now);
            _secondsSincePlan = 0d;
            _planAnchor = observerWorldPos;
        }

        float demoteRadius = bubble * DemoteHysteresis;
        _live.Clear();

        for (int i = 0; i < _all.Count; i++)
        {
            Resident r = _all[i];

            // Distance ALONG THE SURFACE, not through the air. The planner already collects territories by
            // direction from the planet centre, so measuring promotion straight-line disagreed with it: an
            // observer 300 m up is a full bubble radius from a creature directly beneath them, and nothing
            // within the planned territories ever promoted. Flying over a herd showed an empty world.
            float distance = CreatureTerritory.SurfaceDistance(_center, r.Position, observerWorldPos);

            if (!r.IsLive && distance <= bubble)
                Promote(r, now);
            else if (r.IsLive && distance > demoteRadius)
                Demote(r, now);

            if (!r.IsLive)
                continue;

            Vector3 previous = r.Position;
            Simulate(r, deltaTime, now);
            Vector3 velocity = deltaTime > 0f ? (r.Position - previous) / deltaTime : Vector3.zero;
            float altitude = BirdClearance(r, _library.At(r.SpeciesIndex));
            _live.Add(new LiveCreature(r.Id, r.SpeciesIndex, r.Position, r.Driver.Pose.Up, r.Forward, velocity, r.Behaviour, altitude));
        }
    }

    float BubbleMeters => _library != null && _library.ObserverBubbleMeters > 0f
        ? _library.ObserverBubbleMeters
        : DefaultBubbleMeters;

    // --- residency ladder -------------------------------------------------

    void Plan(Vector3 observerWorldPos, float bubbleMeters, long now)
    {
        // Timed because siting runs the biome field, which is the one genuinely expensive call in here, and
        // the first plan after entering fresh ground pays for every slot at once. `creature.status` reports
        // it so the cost is a measurement rather than a guess.
        var timer = System.Diagnostics.Stopwatch.StartNew();
        PlanetTransformSnapshot planet = PlanetTransformSnapshot.Capture(_planetTransform);
        _center = planet.Center;
        CreatureTerritory.CollectNear(observerWorldPos, planet, _planetRadius, bubbleMeters, _ranges, _cells);

        for (int c = 0; c < _cells.Count; c++)
        {
            // CollectNear packs a territory as (face, cellX, cellY) in a Vector3Int.
            int face = _cells[c].x, cellX = _cells[c].y, cellY = _cells[c].z;
            for (int species = 0; species < _library.Count; species++)
            {
                CreatureSpeciesDto dto = _library.At(species);
                if (dto == null || dto.PerTerritory <= 0) continue;

                for (int slot = 0; slot < dto.PerTerritory; slot++)
                    ResolveSlot(CreatureKey.Slot(face, CreatureTerritory.Level, cellX, cellY, _slotBase[species] + slot),
                        species, dto, planet, now);
            }
        }

        _lastPlanMs = (float)timer.Elapsed.TotalMilliseconds;
        _lastPlanCells = _cells.Count;
    }

    // The population question, in one place: is this slot filled, and by whom. Everything else follows.
    void ResolveSlot(EntityId slotKey, int speciesIndex, CreatureSpeciesDto species,
        in PlanetTransformSnapshot planet, long now)
    {
        bool hasRecord = TryGetRecord(slotKey, out CreatureRecord record);
        bool occupied = CreatureTerritory.TryResolveOccupant(hasRecord, record, now, out int generation);

        _bySlot.TryGetValue(slotKey.Value, out Resident existing);

        if (!occupied)
        {
            if (existing != null) Retire(existing);
            return;
        }
        if (existing != null)
        {
            if (existing.Generation == generation) return;
            Retire(existing);   // the slot repopulated: the occupant is a NEW animal, not the one that died
        }

        // A slot's suitability is a pure function of the seed, so a failure is permanent. Remembering it is
        // not an optimisation: without it every unsuitable slot in the bubble re-runs the biome field on
        // every plan tick, forever, and the biome field is the expensive part.
        if (_barren.Contains(slotKey.Value))
            return;

        if (!TryFindHome(slotKey, species, planet, out Vector3 home))
        {
            _barren.Add(slotKey.Value);
            return;
        }

        EntityId id = CreatureKey.AtGeneration(slotKey, generation);

        // A saved displacement describes THIS occupant, so it is where the creature actually is and what it
        // was doing - the fast-forward on promotion then runs from there. Anything else (no record, or a
        // lapsed death describing the previous occupant) starts a fresh animal at its home.
        bool resume = hasRecord && !record.IsDead && record.Generation == generation;
        Vector3 position = resume ? record.Position : home;
        int health = resume && record.Health > 0 ? Mathf.Min(record.Health, species.MaxHealth) : species.MaxHealth;

        var resident = new Resident
        {
            Slot = slotKey,
            Id = id,
            SpeciesIndex = speciesIndex,
            Generation = generation,
            Home = home,
            Position = position,
            Forward = CharacterMath.ArbitraryTangent((position - _center).normalized),
            LastSimulatedUnix = resume ? record.UnixSeconds : now,
            Behaviour = resume ? record.Behaviour : CreatureBehaviour.Wander,
            Health = health,
            Needs = resume ? record.Needs : default,
            Written = resume,
            WrittenPosition = position,
            WrittenBehaviour = resume ? record.Behaviour : CreatureBehaviour.Wander,
            WrittenHealth = health,
            WrittenNeeds = resume ? record.Needs : default,
        };
        _bySlot[slotKey.Value] = resident;
        _all.Add(resident);
    }

    /// <summary>
    /// The first place inside the territory whose ground and biome the species accepts. Both gates are
    /// evaluated at the SAME point - checking altitude at one candidate and biome at another would let a
    /// creature settle on ground that passes neither test whole.
    /// </summary>
    bool TryFindHome(EntityId slotKey, CreatureSpeciesDto species, in PlanetTransformSnapshot planet,
        out Vector3 home)
    {
        home = default;
        float scale = Mathf.Max(planet.UniformScale, 1e-4f);

        for (int attempt = 0; attempt < CreatureTerritory.HomeAttempts; attempt++)
        {
            Vector3 localDir = CreatureTerritory.HomeCandidate(_seeds, slotKey, attempt);
            Vector3 worldDir = planet.Rotation * localDir;
            if (!_sampler.TryGetSurfaceRadius(worldDir, out float worldRadius))
                continue;

            // The sampler answers in world units; the biome field and the sea-level radius are both
            // planet-local, so convert once here rather than mixing the two conventions per gate.
            float localRadius = worldRadius / scale;
            float altitudeMeters = (localRadius - _seaLevelRadius) * scale;

            BiomeType biome = _biome != null
                ? _biome.EvaluateBiome(localDir, localRadius / _planetRadius - 1f).PrimaryBiome
                : BiomeType.Grassland;

            if (!species.Suits(altitudeMeters, biome))
                continue;

            home = _center + worldDir * worldRadius;
            return true;
        }
        return false;
    }

    void Retire(Resident r)
    {
        LandingTargets.Release(r.LandingSiteId, r.Id);
        _threats.Withdraw(r.Id);
        r.Driver = null;
        _bySlot.Remove(r.Slot.Value);
        _all.Remove(r);
    }

    // Re-observation. The elapsed absence is spent in one step rather than simulated, which is the whole
    // saving: an hour away costs the same as a frame away.
    void Promote(Resident r, long now)
    {
        CreatureSpeciesDto species = _library.At(r.SpeciesIndex);
        if (species == null) return;

        float elapsed = Mathf.Max(0f, now - r.LastSimulatedUnix);
        r.Position = CreatureTerritory.DriftToward(_center, r.Position, r.Home, species.DriftHomeSpeedMps * elapsed);
        r.LastSimulatedUnix = now;

        Vector3 up = (r.Position - _center).normalized;
        if (!CharacterMath.TryProjectOntoTangent(r.Forward, up, out Vector3 forward))
            forward = CharacterMath.ArbitraryTangent(up);
        r.Forward = forward;


        // A flier is grounded through a wrapper that adds its altitude. Nothing below this line knows the
        // difference: the motor holds a body above whatever surface it is told about, and for a bird that
        // surface is simply higher up.
        IGroundingProvider grounding = _grounding;
        r.Flight = null;
        r.HasLandingTarget = false;
        r.LandingScanSeconds = 0f;
        r.TakeoffAltitude = 0f;
        if (species.CruiseAltitudeMeters > 0f)
        {
            if (r.Behaviour == CreatureBehaviour.Perch)
            {
                r.HasLandingTarget = _birdLandingGround.TryFind(r.Position, species.BodyHeightMeters, out r.LandingTarget);
                if (!r.HasLandingTarget) r.Behaviour = CreatureBehaviour.Wander;
            }
            r.Flight = new FlightGrounding(_grounding)
            {
                AltitudeMeters = r.Behaviour == CreatureBehaviour.Perch ? 0f : species.CruiseAltitudeMeters,
            };
            grounding = r.Flight;
        }

        if (!grounding.TryGround(r.Position, -up, FootOffset(species), out GroundResult ground))
            return;   // no surface under it this frame; stay a record and try again next tick

        r.Position = ground.Position;
        r.Driver = new SurfaceCharacterController(
            _gravity, grounding, FootOffset(species),
            new CharacterPose(r.Position, ground.Normal, forward));

        // The brain is rebuilt from the remembered behaviour, never carried across the gap as a live object.
        // Without this an animal that was fleeing when you walked away is grazing when you come back.
        r.Brain = new CreatureBrain(_seeds.GetSeedForEntity(r.Id.Value), species, r.Behaviour);
    }

    // Demotion drops the simulation, never the creature. What it was doing and where it was left are kept as
    // VALUES - in memory always, and in the log when they are worth saying.
    void Demote(Resident r, long now)
    {
        r.LastSimulatedUnix = now;
        r.Behaviour = r.Brain?.Behaviour ?? r.Behaviour;
        // Scene sockets are ephemeral. A returning bird resumes flight, not a stale branch reservation.
        if (r.LandingSiteId != 0)
        {
            LandingTargets.Release(r.LandingSiteId, r.Id);
            r.LandingSiteId = 0;
            if (r.Behaviour == CreatureBehaviour.Perch) r.Behaviour = CreatureBehaviour.Wander;
        }
        r.Driver = null;
        r.Brain = null;
        r.Flight = null;
        _threats.Withdraw(r.Id);
        Remember(r, now);
    }

    /// <summary>
    /// Write down a creature that the seed no longer describes, or forget one it does again.
    /// </summary>
    /// <remarks>
    /// The forget path is what keeps §6's promise that the log shrinks back toward the seed: an animal that
    /// wandered off and later drifted home stops costing anything. The exception is a slot whose generation
    /// has moved on - that counter lives nowhere else, so its record stays even when it says nothing else.
    /// </remarks>
    void Remember(Resident r, long now, int? healthOverride = null)
    {
        if (_delta == null) return;

        CreatureSpeciesDto species = _library.At(r.SpeciesIndex);
        if (species == null) return;

        int health = healthOverride ?? r.Health;

        CreatureRecordAction action = CreatureRecordPolicy.Decide(
            CreatureTerritory.SurfaceDistance(_center, r.Position, r.Home),
            species.HomeRangeMeters,
            r.Behaviour,
            r.Generation,
            r.Written,
            Vector3.Distance(r.Position, r.WrittenPosition),
            r.WrittenBehaviour,
            hasPersistentState: health != species.MaxHealth || !r.Needs.Equals(default(ActorNeeds)),
            persistentStateChanged: health != r.WrittenHealth || !r.Needs.Equals(r.WrittenNeeds));

        switch (action)
        {
            case CreatureRecordAction.Forget:
                _delta.Append(CreatureRecordCodec.Forget(r.Slot));
                r.Written = false;
                break;

            case CreatureRecordAction.Write:
                _delta.Append(CreatureRecordCodec.Encode(CreatureRecord.Displacement(
                    r.Slot, r.SpeciesIndex, r.Generation, r.Position, now, r.Behaviour,
                    health == species.MaxHealth ? -1 : health, r.Needs)));
                r.Written = true;
                r.WrittenPosition = r.Position;
                r.WrittenBehaviour = r.Behaviour;
                r.WrittenHealth = health;
                r.WrittenNeeds = r.Needs;
                break;
        }
    }

    void Simulate(Resident r, float deltaTime, long now)
    {
        CreatureSpeciesDto species = _library.At(r.SpeciesIndex);
        if (species == null) return;

        r.Needs = r.Needs.Advance(deltaTime, species.HungerSeconds, species.ThirstSeconds);

        Vector3 up = r.Driver.Pose.Up;
        UpdateBirdLanding(r, species, deltaTime);
        float actualAltitude = BirdClearance(r, species);

        // Perception, then decision, then movement - in that order, and the creature reaches for nothing
        // itself. What it may react to comes from the THREAT registry, which carries identities; the observer
        // positions that decide what is simulated never appear here, which is why a debug camera is invisible
        // to wildlife without a special case for cameras.
        var senses = new CreatureSenses
        {
            Position = r.Position,
            Up = up,
            Forward = r.Forward,
            Home = r.Home,
            DistanceToHome = CreatureTerritory.SurfaceDistance(_center, r.Position, r.Home),
            DeltaTime = deltaTime,
            AltitudeMeters = actualAltitude,
            HasLandingTarget = r.HasLandingTarget,
            LandingTarget = r.LandingTarget,
            Tick = r.Tick,
            Species = species,
            Needs = r.Needs,
            Health = r.Health,
            MaxHealth = species.MaxHealth,
        };
        if (_threats.TryFindThreat(r.Position, r.Id, species.Faction, species.AwarenessMeters, now,
                out ThreatSource threat))
        {
            senses.HasThreat = true;
            senses.ThreatPosition = threat.Position;
            senses.ThreatDistance = Vector3.Distance(r.Position, threat.Position);
        }
        else if (now < r.AlarmUntilUnix)
        {
            senses.HasThreat = true;
            senses.ThreatPosition = r.AlarmFrom;
            senses.ThreatDistance = Vector3.Distance(r.Position, r.AlarmFrom);
        }
        r.Brain.Observe(senses);

        // Look.x is a turn RATE in degrees per second here. ActorIntent carries raw device units and leaves
        // the scaling to whatever hosts the actor; for an animal the host is this service.
        ActorIntent intent = r.Brain.Sample(r.Tick++);
        if (r.Flight != null && r.LandingSiteId != 0)
        {
            if (r.Brain.Behaviour == CreatureBehaviour.Perch &&
                LandingTargets.TryClaim(r.LandingSiteId, r.Id))
            {
                if (!r.Flight.HasSupport)
                {
                    r.Flight.SupportPoint = r.LandingTarget;
                    r.Flight.HasSupport = true;
                    r.Flight.AltitudeMeters = Mathf.Max(0f,
                        Vector3.Dot(r.Position - r.LandingTarget, up) - FootOffset(species));
                }
            }
            else if (r.Flight.HasSupport || r.Brain.Behaviour == CreatureBehaviour.Perch) ReleaseBirdSite(r, species);
        }
        Vector3 turned = Quaternion.AngleAxis(intent.Look.x * deltaTime, up) * r.Forward;
        Vector3 forward = CharacterMath.TryProjectOntoTangent(turned, up, out Vector3 tangent)
            ? tangent
            : r.Forward;

        // A shallow approach keeps forward motion while altitude converges on the selected site.
        // Update the support before movement so takeoff does not spend another tick on the ground.
        if (r.Flight != null)
        {
            float distance = Vector3.ProjectOnPlane(r.LandingTarget - r.Position, up).magnitude;
            float target = r.Brain.Behaviour == CreatureBehaviour.Perch
                ? Mathf.Min(species.CruiseAltitudeMeters, Mathf.Max(0f, distance - PerchState.ArrivalMeters) * 0.5f)
                : Mathf.Max(species.CruiseAltitudeMeters, r.TakeoffAltitude);
            if (r.TakeoffAltitude > 0f && Vector3.ProjectOnPlane(r.Position - r.TakeoffFrom, up).sqrMagnitude > 100f)
                r.TakeoffAltitude = 0f;
            r.Flight.AltitudeMeters = Mathf.MoveTowards(r.Flight.AltitudeMeters, target, ClimbSpeedMps * deltaTime);
        }

        CharacterPose pose = r.Driver.Tick(
            intent.Move, forward, species.WalkSpeedMps * r.Brain.SpeedScale, deltaTime);
        r.Position = pose.Position;
        r.Forward = pose.Forward;
        r.Behaviour = r.Brain.Behaviour;
        r.LastSimulatedUnix = now;

        r.SecondsSinceCheckpoint += deltaTime;
        if (r.SecondsSinceCheckpoint >= 30d)
        {
            Remember(r, now);
            r.SecondsSinceCheckpoint = 0d;
        }

        // A live animal is something other animals can react to. Costs nothing today (Wildlife ignores
        // Wildlife) and is what a predator will read when one exists.
        _threats.Report(r.Id, r.Position, species.Faction);
    }

    void UpdateBirdLanding(Resident r, CreatureSpeciesDto species, float dt)
    {
        if (r.Flight == null) return;
        r.LandingScanSeconds -= Mathf.Max(0f, dt);
        if (r.Behaviour != CreatureBehaviour.Perch && r.LandingSiteId != 0 &&
            (!LandingTargets.TryGet(r.LandingSiteId, out var pending) ||
             !ValidBirdSite(r, species, pending, true) ||
             (pending.Position - r.LandingTarget).sqrMagnitude > 0.0001f))
        {
            r.LandingSiteId = 0;
            r.HasLandingTarget = false;
            r.LandingScanSeconds = 0d;
        }
        if (r.Behaviour == CreatureBehaviour.Perch)
        {
            if (r.LandingSiteId != 0)
            {
                r.HasLandingTarget = LandingTargets.TryGet(r.LandingSiteId, out var site) &&
                    ValidBirdSite(r, species, site, false) &&
                    (site.Position - r.LandingTarget).sqrMagnitude <= 0.0001f &&
                    LandingTargets.TryClaim(r.LandingSiteId, r.Id);
                if (!r.HasLandingTarget) ReleaseBirdSite(r, species);
                return;
            }
            // A terrain edit or changed water level invalidates support even during a rest.
            if (r.HasLandingTarget)
                r.HasLandingTarget = _birdLandingGround.TryFind(r.LandingTarget,
                    species.BodyHeightMeters, out r.LandingTarget);
            return;
        }
        if (r.LandingScanSeconds > 0f) return;
        r.LandingScanSeconds += Math.Floor(-r.LandingScanSeconds) + 1d;
        r.LandingSiteId = 0;
        if (LandingTargets.TryFind(r.Position, 25f, species.BodyHeightMeters, WildlifeLandingUse.Bird, r.Id,
            out var candidate, site => ValidBirdSite(r, species, site, true)))
        {
            r.HasLandingTarget = true;
            r.LandingSiteId = candidate.Id;
            r.LandingTarget = candidate.Position;
            return;
        }
        Vector3 ahead = r.Position + r.Forward * Mathf.Max(species.CruiseAltitudeMeters * 2f, species.WalkSpeedMps * 2f);
        r.HasLandingTarget = _birdLandingGround.TryFind(ahead, species.BodyHeightMeters, out r.LandingTarget);
    }

    bool ValidBirdSite(Resident r, CreatureSpeciesDto species, WildlifeLandingTarget site, bool approaching)
    {
        Vector3 up = (site.Position - _center).normalized;
        if (!site.IsValid || (site.Uses & WildlifeLandingUse.Bird) == 0 || site.Clearance < species.BodyHeightMeters ||
            Vector3.Dot(site.Normal, up) < 0.8660254f ||
            !_grounding.TryGround(site.Position, -up, 0f, out var floor)) return false;
        // Grounding includes local lakes. The contact point must remain above both water and terrain.
        if (Vector3.Dot(site.Position - floor.Position, up) < 0.1f ||
            (site.Position - _center).magnitude <= _seaLevelRadius + 0.1f) return false;
        return !approaching || Vector3.Dot(r.Position - site.Position, up) >= FootOffset(species);
    }

    float BirdClearance(Resident r, CreatureSpeciesDto species)
    {
        if (r.Flight == null) return 0f;
        Vector3 up = r.Driver.Pose.Up;
        if (r.Flight.HasSupport)
            return Mathf.Abs(Vector3.Dot(r.Position - r.Flight.SupportPoint, up) - FootOffset(species));
        return _grounding.TryGround(r.Position, -up, FootOffset(species), out var support)
            ? Mathf.Max(0f, Vector3.Dot(r.Position - support.Position, up)) : float.PositiveInfinity;
    }

    void ReleaseBirdSite(Resident r, CreatureSpeciesDto species)
    {
        LandingTargets.Release(r.LandingSiteId, r.Id);
        r.LandingSiteId = 0;
        r.HasLandingTarget = false;
        if (!r.Flight.HasSupport) return;
        r.Flight.HasSupport = false;
        r.Flight.AltitudeMeters = BirdClearance(r, species);
        if (float.IsInfinity(r.Flight.AltitudeMeters)) r.Flight.AltitudeMeters = species.CruiseAltitudeMeters;
        r.TakeoffFrom = r.Position;
        r.TakeoffAltitude = r.Flight.AltitudeMeters + 3f;
    }

    // --- death ------------------------------------------------------------

    // Either entity kind hashes to the same space, so one lookup finds whichever the slot last wrote.
    bool TryGetRecord(EntityId slotKey, out CreatureRecord record)
    {
        record = default;
        return _delta != null
            && _delta.TryGet(DeltaKind.EntityRemoved, slotKey.Value, out WorldDelta d)
            && CreatureRecordCodec.TryDecode(d, out record);
    }

    /// <summary>
    /// Kill a creature. The record suppresses its slot until the species' expiry lapses, at which point the
    /// slot repopulates with the next generation on its own.
    /// </summary>
    /// <remarks>
    /// Two records, two purposes, two keys. The DEATH record is about the slot and decides when a new animal
    /// arrives; the CARCASS is about the body and decides how long it lies there. They must not be one record:
    /// a slot repopulates in minutes and a carcass lasts days, so a single record would either resurrect the
    /// animal early or hold the slot empty for three days.
    /// </remarks>
    public bool Kill(EntityId id)
    {
        if (!_configured || !CreatureKey.IsCreature(id)) return false;
        EntityId slotKey = CreatureKey.SlotOf(id);
        if (!_bySlot.TryGetValue(slotKey.Value, out Resident r) || r.Id != id) return false;

        CreatureSpeciesDto species = _library.At(r.SpeciesIndex);
        if (species == null) return false;

        long now = NowUnixSeconds();

        // Replaces whatever the slot last said, including a displacement: the log keeps one live record per
        // slot, and a dead creature is not also out wandering.
        CreatureRecord record = CreatureRecord.Death(slotKey, r.SpeciesIndex, r.Generation, r.Position,
            now, species.RespawnSeconds);
        if (_delta != null) _delta.Append(CreatureRecordCodec.Encode(record));
        else _log?.Log(LogLevel.Warning, "Creature", "No delta log configured; the death will not persist.");

        _corpses?.Record(r.SpeciesIndex, r.Position, LyingRotation(r), now);

        Retire(r);
        return true;
    }

    /// <summary>
    /// How far above the surface the BODY sits. A flier adds its cruise altitude on top through
    /// <see cref="FlightGrounding"/>, which is where the number can change while it is flying - perching
    /// lowers it to zero and takes off raises it again, with no second driver and no second motor.
    /// </summary>
    static float FootOffset(CreatureSpeciesDto species) => species.BodyHeightMeters * 0.5f;

    // Face-down along the direction it was travelling. Its own forward is already tangent to the surface, so
    // this cannot degenerate the way an arbitrary forward against the surface normal would.
    Quaternion LyingRotation(Resident r)
    {
        Vector3 up = (r.Position - _center).normalized;
        return CharacterMath.TryProjectOntoTangent(r.Forward, up, out Vector3 forward)
            ? Quaternion.LookRotation(forward, up)
            : Quaternion.LookRotation(CharacterMath.ArbitraryTangent(up), up);
    }

    /// <summary>
    /// Damage a creature, or take the yield off a carcass — whichever the id names. One verb, because from
    /// the player's side it is one action: hit the thing you are aiming at.
    /// </summary>
    /// <remarks>
    /// Grants nothing and raises nothing: the caller owns the inventory. Keeping the credit out of here is
    /// what lets the harvest choke point stay the only place an item is created.
    /// </remarks>
    public CreatureStrike Strike(EntityId id, int damage, Vector3 fromWorldPos)
    {
        if (!_configured) return default;
        if (id.Owner == EntityId.CorpseOwner) return Loot(id);
        if (!CreatureKey.IsCreature(id)) return default;

        EntityId slotKey = CreatureKey.SlotOf(id);
        if (!_bySlot.TryGetValue(slotKey.Value, out Resident r) || r.Id != id || !r.IsLive) return default;

        CreatureSpeciesDto species = _library.At(r.SpeciesIndex);
        if (species == null) return default;

        int remainingHealth = r.Health - Mathf.Max(1, damage);
        if (remainingHealth > 0)
        {
            long now = NowUnixSeconds();
            Remember(r, now, remainingHealth);
            r.Health = remainingHealth;
            r.AlarmUntilUnix = now + AlarmSeconds;
            r.AlarmFrom = fromWorldPos;
            return CreatureStrike.Wounded(species.DisplayName, r.Position, r.Health);
        }

        // Read the position out BEFORE the kill: Kill retires the resident, and a report of the origin would
        // put the body at the centre of the planet.
        Vector3 died = r.Position;

        // No yield here. The hide is on the body now, and taking it is a second action against the carcass.
        return Kill(id) ? CreatureStrike.Fatal(species.DisplayName, died) : default;
    }

    CreatureStrike Loot(EntityId corpseId)
    {
        if (_corpses == null || !_corpses.TryGet(corpseId, out CreatureCorpse corpse)) return default;

        CreatureSpeciesDto species = _library.At(corpse.SpeciesIndex);
        string name = species?.DisplayName ?? "carcass";

        return _corpses.MarkLooted(corpseId)
            ? CreatureStrike.Looted(name, corpse.Position, species?.Yield ?? default)
            : CreatureStrike.NothingLeft(name, corpse.Position);
    }

    /// <summary>
    /// Give every species its own run of slot numbers inside a territory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A slot key is (face, level, cell, slot) and carries NO species - there are no spare bits in the 48 for
    /// one. So every species counting its slots from zero puts them all on the same keys: the first species
    /// resolves slot 0, the second finds that key occupied and returns, and a third species whose count is
    /// covered by the first two gets NOTHING. That is exactly what happened - the bird never spawned, and the
    /// rabbit had only ever been getting the slots the deer's count did not reach.
    /// </para>
    /// <para>
    /// Bases are cumulative, so APPENDING a species leaves every existing species' slot numbers untouched and
    /// saved records keep pointing at the same animals. Changing an earlier species' PerTerritory shifts every
    /// later species and orphans their records - that is the constraint this layout buys the simplicity with.
    /// </para>
    /// </remarks>
    public static int[] SlotBases(CreatureLibraryDto library)
    {
        int count = library?.Count ?? 0;
        var bases = new int[count];

        int next = 0;
        for (int i = 0; i < count; i++)
        {
            bases[i] = next;
            next += Mathf.Max(0, library.At(i)?.PerTerritory ?? 0);
        }
        return bases;
    }

    /// <summary>Slots every species needs together. More than a key can hold means the last ones collide.</summary>
    public static int SlotsNeeded(CreatureLibraryDto library)
    {
        int[] bases = SlotBases(library);
        int count = library?.Count ?? 0;
        return count == 0 ? 0 : bases[count - 1] + Mathf.Max(0, library.At(count - 1)?.PerTerritory ?? 0);
    }

    void BuildSlotBases()
    {
        _slotBase = SlotBases(_library);

        int needed = SlotsNeeded(_library);
        if (needed > CreatureKey.MaxSlot + 1)
            _log?.Log(LogLevel.Warning, "Creature",
                $"Species need {needed} slots per territory but a key holds {CreatureKey.MaxSlot + 1}; " +
                "the last species will collide. Reduce PerTerritory or widen CreatureKey.SlotBits.");
    }

    long NowUnixSeconds() => _unixClock();

    // --- console ----------------------------------------------------------

    int LiveOf(int speciesIndex)
    {
        int count = 0;
        for (int i = 0; i < _live.Count; i++)
            if (_live[i].SpeciesIndex == speciesIndex) count++;
        return count;
    }

    // Surface distance, matching what the bubble measures - otherwise the console reports a creature as 350 m
    // away while it is live inside a 300 m bubble, which reads as a bug in the residency rather than in the
    // readout.
    float DistanceFromObserver(Resident r) =>
        CreatureTerritory.SurfaceDistance(_center, r.Position, _observerPos);

    Resident Nearest(Vector3 from, bool liveOnly)
    {
        Resident best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < _all.Count; i++)
        {
            Resident r = _all[i];
            if (liveOnly && !r.IsLive) continue;
            float d = CreatureTerritory.SurfaceDistance(_center, r.Position, from);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = r;
        }
        return best;
    }

    [ConsoleCommand("status", "Creature residency: species, territory lattice, resident and live counts.",
        MonoTargetType.Registry)]
    string StatusCmd()
    {
        if (!_configured) return "creature residency inactive (no world configured)";
        var sb = new System.Text.StringBuilder();
        sb.Append("seed=").Append(_seed)
          .Append(" lattice=L").Append(CreatureTerritory.Level)
          .Append(" (").Append(6 * CreatureTerritory.CellsPerFace * CreatureTerritory.CellsPerFace)
          .Append(" territories) bubble=").Append(BubbleMeters.ToString("F0")).Append('m')
          .Append(" residents=").Append(_all.Count)
          .Append(" live=").Append(_live.Count)
          .Append(" barren=").Append(_barren.Count)
          .Append(" slotsUsed=").Append(SlotsNeeded(_library)).Append('/').Append(CreatureKey.MaxSlot + 1)
          .Append("\nlast plan: ").Append(_lastPlanCells).Append(" territories in ")
          .Append(_lastPlanMs.ToString("F2")).Append(" ms");
        for (int i = 0; i < _library.Count; i++)
        {
            CreatureSpeciesDto s = _library.At(i);

            // The slot RANGE, not just the count. Species share one slot space and a collision produces no
            // error at all - it just means a species silently never spawns - so the ranges have to be
            // readable somewhere. They were not, which is how a whole species stayed missing.
            int from = i < _slotBase.Length ? _slotBase[i] : 0;
            int to = from + Mathf.Max(0, s.PerTerritory) - 1;

            sb.Append("\n  [").Append(i).Append("] ").Append(s.DisplayName)
              .Append(" slots=").Append(s.PerTerritory > 0 ? from + ".." + to : "none")
              .Append(" live=").Append(LiveOf(i))
              .Append(" hp=").Append(s.MaxHealth)
              .Append(" yield=").Append(s.YieldCount).Append('x').Append(s.YieldItemId)
              .Append(" awareness=").Append(s.AwarenessMeters.ToString("F0")).Append('m')
              .Append(s.CruiseAltitudeMeters > 0f ? " cruise=" + s.CruiseAltitudeMeters.ToString("F0") + "m" : "")
              .Append(" home=").Append(s.HomeRangeMeters.ToString("F0")).Append('m')
              .Append(" walk=").Append(s.WalkSpeedMps.ToString("F1"))
              .Append(" drift=").Append(s.DriftHomeSpeedMps.ToString("F2"))
              .Append(" respawn=").Append(s.NeverRespawns ? "never" : s.RespawnSeconds.ToString("F0") + "s")
              .Append(" altitude=").Append(s.MinAltitudeMeters.ToString("F0")).Append("..")
              .Append(s.MaxAltitudeMeters.ToString("F0"))
              .Append(" biomes=")
              .Append(s.Biomes == null || s.Biomes.Length == 0 ? "any" : string.Join("/", s.Biomes));
        }
        return sb.ToString();
    }

    [ConsoleCommand("list", "List residents by distance from the observer: id, tier, distance, distance from home.",
        MonoTargetType.Registry)]
    string ListCmd(int max = 12)
    {
        if (!_configured) return "creature residency inactive";
        if (_all.Count == 0) return "no residents in the bubble";

        Vector3 from = _observerPos;
        var sorted = new List<Resident>(_all);
        sorted.Sort((a, b) => DistanceFromObserver(a).CompareTo(DistanceFromObserver(b)));

        var sb = new System.Text.StringBuilder();
        sb.Append(_all.Count).Append(" resident(s), ").Append(_live.Count).Append(" live:");
        for (int i = 0; i < sorted.Count && i < Mathf.Max(1, max); i++)
        {
            Resident r = sorted[i];
            sb.Append('\n').Append(CreatureKey.Describe(r.Id))
              .Append(' ').Append(_library.At(r.SpeciesIndex)?.DisplayName ?? "?")
              .Append(r.IsLive ? " live " : " record ").Append(r.Behaviour)
              .Append(" goal=").Append(r.Brain?.Objective.ToString() ?? "inactive")
              .Append(" hunger=").Append(r.Needs.Hunger.ToString("F2"))
              .Append(" thirst=").Append(r.Needs.Thirst.ToString("F2"))
              .Append(" d=").Append(DistanceFromObserver(r).ToString("F0")).Append('m')
              .Append(" fromHome=")
              .Append(CreatureTerritory.SurfaceDistance(_center, r.Position, r.Home).ToString("F0")).Append('m');
        }
        return sb.ToString();
    }

    /// <summary>Where to stand to look at a creature: just above the ground, a few metres to its side.</summary>
    /// <remarks>
    /// Exists because "I flew around and never saw one" is a question the console could not answer. It gives
    /// back a POSITION rather than moving anything, so the service still touches no camera - the caller does
    /// the moving.
    /// </remarks>
    public bool TryGetViewpointOfNearest(out Vector3 viewpoint, out Vector3 lookAt, out string described)
    {
        viewpoint = default;
        lookAt = default;
        described = null;
        if (!_configured) return false;

        Resident target = Nearest(_observerPos, liveOnly: true) ?? Nearest(_observerPos, liveOnly: false);
        if (target == null) return false;

        Vector3 up = (target.Position - _center).normalized;
        Vector3 side = Vector3.Cross(up, target.Forward);
        if (side.sqrMagnitude < 1e-6f) side = CharacterMath.ArbitraryTangent(up);

        CreatureSpeciesDto species = _library.At(target.SpeciesIndex);
        lookAt = target.Position + up * (species?.BodyHeightMeters ?? 1f) * 0.5f;
        viewpoint = target.Position + up * 3f + side.normalized * 6f;
        described = $"{CreatureKey.Describe(target.Id)} {species?.DisplayName ?? "?"} " +
                    $"({(target.IsLive ? "live " + target.Behaviour : "a record, too far to be simulated")}), " +
                    $"{DistanceFromObserver(target):F0} m away";
        return true;
    }

    [ConsoleCommand("loot", "Take the yield off the nearest carcass. Reports what it would grant - the " +
        "inventory credit belongs to the harvest path, not here.", MonoTargetType.Registry)]
    string LootCmd()
    {
        if (_corpses == null) return "no carcass store";

        long now = NowUnixSeconds();
        CreatureCorpse best = default;
        float bestDistance = float.MaxValue;
        bool found = false;
        foreach (CreatureCorpse c in _corpses.All)
        {
            float d = CreatureTerritory.SurfaceDistance(_center, c.Position, _observerPos);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = c;
            found = true;
        }
        if (!found) return "no carcass in the world";

        CreatureStrike s = Strike(best.Id, 1, _observerPos);
        string stage = _corpses.StageOf(best, now).ToString();
        return s.Outcome switch
        {
            CreatureStrikeOutcome.Looted =>
                $"took {s.Yield.Count}x {s.Yield.ItemId} off the {s.DisplayName} ({stage}, {bestDistance:F0} m)",
            CreatureStrikeOutcome.NothingLeft =>
                $"that {s.DisplayName} has already been taken ({stage}, {bestDistance:F0} m)",
            _ => "loot failed",
        };
    }

    [ConsoleCommand("corpses", "Every carcass in the world: species, decomposition stage, age and whether it " +
        "has been looted.", MonoTargetType.Registry)]
    string CorpsesCmd()
    {
        if (_corpses == null) return "no carcass store";
        if (_corpses.Count == 0) return "no carcasses";

        long now = NowUnixSeconds();
        CorpseDecay decay = _corpses.Decay;
        var sb = new System.Text.StringBuilder();
        sb.Append(_corpses.Count).Append(" carcass(es):");
        foreach (CreatureCorpse c in _corpses.All)
        {
            CreatureSpeciesDto species = _library?.At(c.SpeciesIndex);
            sb.Append("\n  ").Append(species?.DisplayName ?? "?")
              .Append(' ').Append(decay.StageOf(c, now))
              .Append("  age ").Append(FormatDuration(decay.AgeSeconds(c, now)))
              .Append(c.Looted ? "  looted" : "  unlooted")
              .Append("  ").Append(CreatureTerritory.SurfaceDistance(_center, c.Position, _observerPos).ToString("F0"))
              .Append(" m");
        }
        return sb.ToString();
    }

    [ConsoleCommand("decay", "Multiply how fast carcasses decompose. A whole carcass life is three real days, " +
        "so 500 or so is what makes the stages watchable inside one session.", MonoTargetType.Registry)]
    string DecayCmd(float multiplier = 1f)
    {
        if (_corpses == null) return "no carcass store";
        _corpses.SetTimeScale(multiplier);

        CorpseDecay d = _corpses.Decay;
        return $"decay x{d.TimeScale:0.##} - flies at {FormatDuration(d.FliesAfterSeconds / d.TimeScale)}, " +
               $"bloated {FormatDuration(d.BloatedAfterSeconds / d.TimeScale)}, " +
               $"rotting {FormatDuration(d.RottingAfterSeconds / d.TimeScale)}, " +
               $"bones {FormatDuration(d.BonesAfterSeconds / d.TimeScale)}, " +
               $"gone {FormatDuration(d.GoneAfterSeconds / d.TimeScale)}";
    }

    static string FormatDuration(float seconds) =>
        seconds >= 86400f ? (seconds / 86400f).ToString("0.#") + "d"
        : seconds >= 3600f ? (seconds / 3600f).ToString("0.#") + "h"
        : seconds >= 60f ? (seconds / 60f).ToString("0.#") + "m"
        : seconds.ToString("0.#") + "s";

    [ConsoleCommand("strike", "Hit the nearest live creature once, the way a tool does. No loot - that is the " +
        "harvest path; this only proves health and death.", MonoTargetType.Registry)]
    string StrikeCmd(int damage = 1)
    {
        if (!_configured) return "creature residency inactive";
        Resident target = Nearest(_observerPos, liveOnly: true);
        if (target == null) return "no live creature in range";

        string id = CreatureKey.Describe(target.Id);
        CreatureStrike s = Strike(target.Id, damage, _observerPos);
        if (!s.Hit) return "strike failed for " + id;
        Invalidate();
        return s.Killed
            ? $"killed {id} {s.DisplayName} - the carcass is on the ground. creature.loot takes the hide"
            : $"hit {id} {s.DisplayName}, {s.RemainingHealth} health left - it bolts for {AlarmSeconds}s";
    }

    [ConsoleCommand("kill", "Kill the nearest live creature. It stays dead until its species' respawn expiry lapses.",
        MonoTargetType.Registry)]
    string KillCmd()
    {
        if (!_configured) return "creature residency inactive";
        Resident target = Nearest(_observerPos, liveOnly: true) ?? Nearest(_observerPos, liveOnly: false);
        if (target == null) return "no creature in range";

        string id = CreatureKey.Describe(target.Id);
        CreatureSpeciesDto species = _library.At(target.SpeciesIndex);
        if (!Kill(target.Id)) return "kill failed for " + id;
        Invalidate();
        return species != null && species.NeverRespawns
            ? id + " killed; this species never respawns"
            : $"{id} killed; slot repopulates in {species?.RespawnSeconds ?? 0f:F0}s with generation " +
              $"{(target.Generation + 1) % CreatureKey.GenerationWrap}";
    }

    [ConsoleCommand("respawn", "Seconds a death suppresses its slot, for species 0. 0 or less means NEVER (the boss case).",
        MonoTargetType.Registry)]
    string RespawnCmd(float seconds)
    {
        if (!_configured) return "creature residency inactive";
        if (!SettingsProvider.IsRegistered<CreatureLibraryDto>())
            return "no CreatureLibraryDto registered";

        // The runtime DTO, never the asset: a console tweak must not write the authored library to disk.
        SettingsProvider.Update(SettingsProvider.GetSettings<CreatureLibraryDto>().WithRespawnSeconds(0, seconds));
        return seconds > 0f
            ? $"species 0 respawn = {seconds:F0}s (existing death records use the value they were written with)"
            : "species 0 respawn = never (existing death records use the value they were written with)";
    }

    [ConsoleCommand("bubble", "Observer bubble radius in metres. Inside it a creature is simulated; outside it is a record.",
        MonoTargetType.Registry)]
    string BubbleCmd(float meters)
    {
        if (!_configured) return "creature residency inactive";
        if (!SettingsProvider.IsRegistered<CreatureLibraryDto>())
            return "no CreatureLibraryDto registered";

        SettingsProvider.Update(SettingsProvider.GetSettings<CreatureLibraryDto>() with
        {
            ObserverBubbleMeters = Mathf.Max(1f, meters),
        });
        return $"bubble = {BubbleMeters:F0}m (demotes at {BubbleMeters * DemoteHysteresis:F0}m)";
    }

    [ConsoleCommand("records", "Everything the world log says about creatures: deaths with their expiry, and creatures left somewhere the seed does not predict.",
        MonoTargetType.Registry)]
    string RecordsCmd()
    {
        if (_delta == null) return "no delta log";
        long now = NowUnixSeconds();
        var sb = new System.Text.StringBuilder();
        int count = 0;
        IReadOnlyList<WorldDelta> snapshot = _delta.Snapshot();
        for (int i = 0; i < snapshot.Count; i++)
        {
            if (!CreatureRecordCodec.TryDecode(snapshot[i], out CreatureRecord d)) continue;
            count++;
            sb.Append('\n').Append(CreatureKey.Describe(CreatureKey.AtGeneration(d.Slot, d.Generation)))
              .Append(" species=").Append(d.SpeciesIndex);

            if (!d.IsDead)
            {
                sb.Append(" displaced ").Append(d.Behaviour)
                  .Append(" at ").Append(d.Position.ToString("F0"));
                continue;
            }
            float left = d.SecondsUntilRespawn(now);
            sb.Append(d.Suppresses(now)
                ? float.IsPositiveInfinity(left) ? " dead=forever" : $" dead, respawns in {left:F0}s"
                : $" dead but lapsed -> generation {d.NextGeneration}");
        }
        return count == 0 ? "no creature records; the world is exactly what the seed says" : count + " record(s):" + sb;
    }

    // A fake threat parked at the observer. The free camera is a POSITION and can never be a threat by
    // design, so provoking a flee otherwise means spawning the character every time you want to check a
    // tweak. This is the debug affordance that buys back the convenience without weakening the rule.
    static readonly EntityId DebugThreatId = new(EntityId.DerivedOwner, 2);

    [ConsoleCommand("threat", "Park a fake Player-faction threat at the camera for N seconds so wildlife reacts. 0 removes it.",
        MonoTargetType.Registry)]
    string ThreatCmd(float seconds = 20f)
    {
        if (!_configured) return "creature residency inactive";
        if (seconds <= 0f)
        {
            _threats.Withdraw(DebugThreatId);
            return "debug threat removed";
        }
        _threats.Report(DebugThreatId, _observerPos, CreatureFaction.Player);
        // Expiry rides on the same disguise clock: after it lapses the source is withdrawn on the next tick.
        _debugThreatUntil = NowUnixSeconds() + (long)seconds;
        return $"debug threat at the camera for {seconds:F0}s; wildlife within its awareness radius will run";
    }

    [ConsoleCommand("friendly", "Make the debug threat (and the player) be SEEN as wildlife for N seconds - the friendly-to-animals spell.",
        MonoTargetType.Registry)]
    string FriendlyCmd(float seconds = 30f)
    {
        if (!_configured) return "creature residency inactive";
        long now = NowUnixSeconds();
        _threats.SetDisguise(DebugThreatId, CreatureFaction.Wildlife, seconds, now);
        _threats.SetDisguise(ThreatRegistry.LocalPlayer, CreatureFaction.Wildlife, seconds, now);
        return seconds > 0f
            ? $"seen as wildlife for {seconds:F0}s; nothing flees from you, and no species data changed"
            : "friendly effect cleared";
    }

    [ConsoleCommand("threats", "List what wildlife can currently react to, and any active friendly effects.",
        MonoTargetType.Registry)]
    string ThreatsCmd()
    {
        long now = NowUnixSeconds();
        var sb = new System.Text.StringBuilder();
        sb.Append(_threats.Sources.Count).Append(" threat source(s), ")
          .Append(_threats.DisguiseCount).Append(" active effect(s):");
        for (int i = 0; i < _threats.Sources.Count; i++)
        {
            ThreatSource s = _threats.Sources[i];
            CreatureFaction seen = _threats.SeenAs(s, now);
            sb.Append('\n').Append(CreatureKey.IsCreature(s.Id) ? CreatureKey.Describe(s.Id) : s.Id.ToString())
              .Append(' ').Append(s.Faction);
            if (seen != s.Faction)
                sb.Append(" seen-as ").Append(seen)
                  .Append(" (").Append(_threats.DisguiseSecondsLeft(s.Id, now).ToString("F0")).Append("s left)");
            sb.Append(" d=").Append(Vector3.Distance(_observerPos, s.Position).ToString("F0")).Append('m');
        }
        return sb.ToString();
    }

    [ConsoleCommand("clear-records", "Forget every creature record, putting the whole population back to what the seed says.",
        MonoTargetType.Registry)]
    string ClearRecordsCmd()
    {
        if (_delta == null) return "no delta log";

        // Removal must be a record of its own. Dropping the entry from memory would leave it in the file, and
        // a client replaying the log - or this world on its next load - would apply it again.
        var slots = new List<ulong>();
        IReadOnlyList<WorldDelta> snapshot = _delta.Snapshot();
        for (int i = 0; i < snapshot.Count; i++)
            if (CreatureRecordCodec.TryDecode(snapshot[i], out CreatureRecord d))
                slots.Add(d.Slot.Value);

        foreach (ulong slot in slots)
            _delta.Append(CreatureRecordCodec.Forget(new EntityId(slot)));

        foreach (Resident r in _all) r.Written = false;
        Invalidate();
        return slots.Count == 0
            ? "no creature records"
            : $"forgot {slots.Count} record(s); every slot is back to generation 0 at its home";
    }
}


/// <summary>What one blow did.</summary>
public enum CreatureStrikeOutcome : byte
{
    /// <summary>Nothing there to hit — a stale id, or something that has already demoted.</summary>
    Missed = 0,

    Wounded = 1,

    /// <summary>It died. The yield is NOT here: it stays on the body until someone takes it.</summary>
    Killed = 2,

    /// <summary>A carcass gave up its yield.</summary>
    Looted = 3,

    /// <summary>A carcass someone already took. Aimed at a real thing, so not a miss.</summary>
    NothingLeft = 4,
}

/// <summary>The outcome of one blow, whether it landed on an animal or on a body.</summary>
public readonly struct CreatureStrike
{
    public readonly CreatureStrikeOutcome Outcome;
    public readonly int RemainingHealth;
    public readonly string DisplayName;
    public readonly Vector3 Position;
    public readonly HarvestYield Yield;

    CreatureStrike(CreatureStrikeOutcome outcome, int remainingHealth, string displayName, Vector3 position,
        HarvestYield yield)
    {
        Outcome = outcome;
        RemainingHealth = remainingHealth;
        DisplayName = displayName;
        Position = position;
        Yield = yield;
    }

    public bool Hit => Outcome != CreatureStrikeOutcome.Missed;
    public bool Killed => Outcome == CreatureStrikeOutcome.Killed;

    public static CreatureStrike Wounded(string displayName, Vector3 position, int remainingHealth) =>
        new(CreatureStrikeOutcome.Wounded, remainingHealth, displayName, position, default);

    public static CreatureStrike Fatal(string displayName, Vector3 position) =>
        new(CreatureStrikeOutcome.Killed, 0, displayName, position, default);

    public static CreatureStrike Looted(string displayName, Vector3 position, HarvestYield yield) =>
        new(CreatureStrikeOutcome.Looted, 0, displayName, position, yield);

    public static CreatureStrike NothingLeft(string displayName, Vector3 position) =>
        new(CreatureStrikeOutcome.NothingLeft, 0, displayName, position, default);
}

/// <summary>
/// Grounding, raised. Wraps the real provider and adds an altitude the host can change while the creature is
/// flying, which is the whole of what makes a bird a bird here.
/// </summary>
/// <remarks>
/// One per LIVE flier, because the altitude is per-individual and changes as it lands and takes off. That is
/// the only reason it is not shared the way the plain grounding provider is.
/// </remarks>
public sealed class FlightGrounding : IGroundingProvider
{
    readonly IGroundingProvider _surface;

    /// <summary>Metres above the ground the body is held. Zero is perched.</summary>
    public float AltitudeMeters;
    public bool HasSupport;
    public Vector3 SupportPoint;

    public FlightGrounding(IGroundingProvider surface) => _surface = surface;

    public bool TryGround(Vector3 worldPos, Vector3 downDir, float footOffset, out GroundResult result)
    {
        if (!_surface.TryGround(worldPos, downDir, footOffset + Mathf.Max(0f, AltitudeMeters), out result)) return false;
        if (HasSupport)
        {
            Vector3 up = result.Normal;
            Vector3 contact = worldPos + up * (Vector3.Dot(SupportPoint - worldPos, up) + footOffset + Mathf.Max(0f, AltitudeMeters));
            if (Vector3.Dot(contact - result.Position, up) > 0f) result = new GroundResult(contact, up);
        }
        return true;
    }
}
