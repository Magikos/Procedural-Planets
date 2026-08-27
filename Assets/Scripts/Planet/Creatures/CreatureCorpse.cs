using System.Collections.Generic;
using UnityEngine;

/// <summary>How far along a carcass is. Derived from elapsed time, never stored.</summary>
public enum CorpseStage : byte
{
    Fresh = 0,
    Bloated = 1,
    Rotting = 2,
    Bones = 3,

    /// <summary>Past the last stage. A corpse in this stage is waiting to be removed, not drawn.</summary>
    Gone = 4,
}

/// <summary>
/// A body on the ground. Unlike a creature it is not derived from the seed at all — a player put it there —
/// so it is an ordinary minted world entity and it costs real bytes, which is the correct trade: section 5's
/// zero-bytes promise is about animals that did nothing, and a kill is the definition of something notable.
/// </summary>
/// <remarks>
/// The only thing stored about decay is <see cref="DiedUnixSeconds"/>. Every stage is a pure function of
/// elapsed time, so a carcass fast-forwards for free across a save, a reload, or a week away — the same
/// derived-cache shape path wear already uses, where saved stamps are the truth and the mask is rebuilt.
/// </remarks>
public readonly struct CreatureCorpse
{
    public readonly EntityId Id;
    public readonly int SpeciesIndex;
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;
    public readonly long DiedUnixSeconds;

    /// <summary>Its yield has already been taken. A looted carcass still rots and still draws flies.</summary>
    public readonly bool Looted;

    public CreatureCorpse(EntityId id, int speciesIndex, Vector3 position, Quaternion rotation,
        long diedUnixSeconds, bool looted)
    {
        Id = id;
        SpeciesIndex = speciesIndex;
        Position = position;
        Rotation = rotation;
        DiedUnixSeconds = diedUnixSeconds;
        Looted = looted;
    }

    public CreatureCorpse AsLooted() =>
        new(Id, SpeciesIndex, Position, Rotation, DiedUnixSeconds, looted: true);
}

/// <summary>
/// When a carcass moves from one stage to the next, in real seconds since death.
/// </summary>
/// <remarks>
/// REAL seconds, not game-clock days, because this has to survive the game being closed: a save reopened a
/// week later should find bones, and the celestial clock does not run while nobody is playing. The numbers are
/// authored so a whole carcass life is about three days, per Bryan's Red Dead reference.
/// </remarks>
public readonly struct CorpseDecay
{
    public readonly float BloatedAfterSeconds;
    public readonly float RottingAfterSeconds;
    public readonly float BonesAfterSeconds;
    public readonly float GoneAfterSeconds;

    /// <summary>Seconds after death before flies find it. They leave again once there is nothing left to eat.</summary>
    public readonly float FliesAfterSeconds;

    /// <summary>Debug lever. Multiplies elapsed time, so a three-day decay is watchable inside a play session.</summary>
    public readonly float TimeScale;

    public CorpseDecay(float bloated, float rotting, float bones, float gone, float flies, float timeScale)
    {
        BloatedAfterSeconds = bloated;
        RottingAfterSeconds = rotting;
        BonesAfterSeconds = bones;
        GoneAfterSeconds = gone;
        FliesAfterSeconds = flies;
        TimeScale = Mathf.Max(0.001f, timeScale);
    }

    public static readonly CorpseDecay Default = new(
        bloated: 1f * 3600f,
        rotting: 6f * 3600f,
        bones: 24f * 3600f,
        gone: 72f * 3600f,
        flies: 120f,
        timeScale: 1f);

    public CorpseDecay WithTimeScale(float scale) => new(
        BloatedAfterSeconds, RottingAfterSeconds, BonesAfterSeconds, GoneAfterSeconds, FliesAfterSeconds, scale);

    /// <summary>Elapsed seconds since death, after the debug time scale.</summary>
    public float AgeSeconds(in CreatureCorpse corpse, long nowUnixSeconds) =>
        Mathf.Max(0f, (nowUnixSeconds - corpse.DiedUnixSeconds) * TimeScale);

    public CorpseStage StageOf(in CreatureCorpse corpse, long nowUnixSeconds)
    {
        float age = AgeSeconds(corpse, nowUnixSeconds);
        if (age >= GoneAfterSeconds) return CorpseStage.Gone;
        if (age >= BonesAfterSeconds) return CorpseStage.Bones;
        if (age >= RottingAfterSeconds) return CorpseStage.Rotting;
        if (age >= BloatedAfterSeconds) return CorpseStage.Bloated;
        return CorpseStage.Fresh;
    }

    /// <summary>Flies arrive once it has been dead a while and leave once it is down to bones.</summary>
    public bool HasFlies(in CreatureCorpse corpse, long nowUnixSeconds)
    {
        float age = AgeSeconds(corpse, nowUnixSeconds);
        return age >= FliesAfterSeconds && age < BonesAfterSeconds;
    }
}

/// <summary>
/// Every carcass in the world, backed by the delta log. Modelled directly on the fallen-log half of
/// <see cref="ScatterHarvestStore"/>, which is the same problem already solved: a minted entity the player
/// created, persisted as one live record, removed when it is done.
/// </summary>
/// <remarks>
/// <para>
/// Ids are minted from <see cref="EntityId.CorpseOwner"/> rather than the host owner. The owner tag is what
/// keeps three id spaces apart in one delta space: host-minted logs, seed-derived creature slots, and these.
/// Without it a corpse at generation 0 would land on the very key its own species' death record uses.
/// </para>
/// <para>
/// Authority code. It never reads a camera; the observer position it is given decides only when a spent
/// carcass may be taken away.
/// </para>
/// </remarks>
public sealed class CreatureCorpseStore
{
    /// <summary>
    /// Metres around the observer inside which a spent carcass is NOT removed. Valheim's rule, and Bryan's:
    /// nothing vanishes while you are looking at it. Buildings will extend this.
    /// </summary>
    // planned: a placed-structure radius joins this test once buildings exist,
    // docs/design/2026-08-20-magikos-game-architecture.md
    public const float KeepAliveMeters = 120f;

    const byte PayloadFormat = 10;   // deliberately outside CreatureRecordCodec's formats: same delta space
    const int PayloadBytes = 9;      // format 1 | died 8

    readonly Dictionary<ulong, CreatureCorpse> _corpses = new();
    readonly EntityIdAllocator _ids = new(EntityId.CorpseOwner);
    readonly List<ulong> _spent = new();
    readonly ILogger _log;

    IWorldDeltaLog _delta;
    CorpseDecay _decay = CorpseDecay.Default;

    public CreatureCorpseStore(ILogger log = null) => _log = log;

    public int Count => _corpses.Count;
    public CorpseDecay Decay => _decay;
    public IEnumerable<CreatureCorpse> All => _corpses.Values;

    public void SetTimeScale(float scale) => _decay = _decay.WithTimeScale(scale);

    /// <summary>Point the store at a world's log and rebuild its view from what is already recorded.</summary>
    public void Configure(IWorldDeltaLog deltaLog)
    {
        if (ReferenceEquals(deltaLog, _delta)) return;
        _delta = deltaLog;
        Rebuild();
    }

    public bool TryGet(EntityId id, out CreatureCorpse corpse) => _corpses.TryGetValue(id.Value, out corpse);

    /// <summary>Lay a body down where a creature died. Returns its id.</summary>
    public EntityId Record(int speciesIndex, Vector3 position, Quaternion rotation, long diedUnixSeconds)
    {
        EntityId id = _ids.Next();
        Apply(Encode(new CreatureCorpse(id, speciesIndex, position, rotation, diedUnixSeconds, looted: false)));
        return id;
    }

    /// <summary>Take its yield. False when there is no such carcass, or it has already been looted.</summary>
    public bool MarkLooted(EntityId id)
    {
        if (!_corpses.TryGetValue(id.Value, out CreatureCorpse corpse) || corpse.Looted) return false;
        Apply(Encode(corpse.AsLooted()));
        return true;
    }

    public CorpseStage StageOf(in CreatureCorpse corpse, long nowUnixSeconds) => _decay.StageOf(corpse, nowUnixSeconds);

    public void CollectNear(Vector3 worldPos, float radiusMeters, long nowUnixSeconds, List<CreatureCorpse> into)
    {
        into.Clear();
        float sqr = radiusMeters * radiusMeters;
        foreach (KeyValuePair<ulong, CreatureCorpse> kv in _corpses)
        {
            if ((kv.Value.Position - worldPos).sqrMagnitude > sqr) continue;
            if (_decay.StageOf(kv.Value, nowUnixSeconds) == CorpseStage.Gone) continue;
            into.Add(kv.Value);
        }
    }

    /// <summary>
    /// Remove the carcasses that are finished — but never one the observer could be looking at. A pile of
    /// bones blinking out from under your feet is worse than one that outstays its welcome by a minute.
    /// </summary>
    public void Tick(Vector3 observerWorldPos, long nowUnixSeconds)
    {
        if (_corpses.Count == 0) return;

        _spent.Clear();
        float keepSqr = KeepAliveMeters * KeepAliveMeters;
        foreach (KeyValuePair<ulong, CreatureCorpse> kv in _corpses)
        {
            if (_decay.StageOf(kv.Value, nowUnixSeconds) != CorpseStage.Gone) continue;
            if ((kv.Value.Position - observerWorldPos).sqrMagnitude <= keepSqr) continue;
            _spent.Add(kv.Key);
        }

        for (int i = 0; i < _spent.Count; i++)
            Apply(new WorldDelta(0, DeltaKind.EntityRemoved, _spent[i]));
    }

    // --- delta log view ---

    void Apply(in WorldDelta delta)
    {
        if (_delta == null)
        {
            _log?.Log(LogLevel.Warning, "Creature", "No delta log configured; the carcass will not persist.");
            Ingest(delta);
            return;
        }
        Ingest(_delta.Append(delta));
    }

    void Rebuild()
    {
        _corpses.Clear();
        _ids.Reset();
        if (_delta == null) return;
        foreach (WorldDelta d in _delta.Snapshot()) Ingest(d);
    }

    void Ingest(in WorldDelta d)
    {
        var id = new EntityId(d.Key);
        if (id.Owner != EntityId.CorpseOwner) return;   // creature slots and dropped logs share this space

        switch (d.Kind)
        {
            case DeltaKind.EntitySpawned when TryDecode(d, id, out CreatureCorpse corpse):
                _ids.Observe(id);
                _corpses[d.Key] = corpse;
                break;

            case DeltaKind.EntityRemoved:
                _corpses.Remove(d.Key);
                break;
        }
    }

    static WorldDelta Encode(in CreatureCorpse corpse)
    {
        var payload = new byte[PayloadBytes];
        payload[0] = PayloadFormat;
        System.BitConverter.GetBytes(corpse.DiedUnixSeconds).CopyTo(payload, 1);

        return new WorldDelta(0, DeltaKind.EntitySpawned, corpse.Id.Value, corpse.Position, corpse.Rotation,
            typeIndex: corpse.SpeciesIndex, state: (byte)(corpse.Looted ? 1 : 0), payload: payload);
    }

    static bool TryDecode(in WorldDelta d, EntityId id, out CreatureCorpse corpse)
    {
        corpse = default;
        if (d.Payload == null || d.Payload.Length < PayloadBytes || d.Payload[0] != PayloadFormat) return false;

        corpse = new CreatureCorpse(id, d.TypeIndex, d.Position, d.Rotation,
            System.BitConverter.ToInt64(d.Payload, 1), d.State != 0);
        return true;
    }
}
