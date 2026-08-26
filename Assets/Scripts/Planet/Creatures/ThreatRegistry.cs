using System.Collections.Generic;
using UnityEngine;

/// <summary>One thing a creature can have an opinion about: where it is, and whose side it is on.</summary>
public readonly struct ThreatSource
{
    public readonly EntityId Id;
    public readonly Vector3 Position;
    public readonly CreatureFaction Faction;

    public ThreatSource(EntityId id, Vector3 position, CreatureFaction faction)
    {
        Id = id;
        Position = position;
        Faction = faction;
    }
}

/// <summary>
/// Everything in the world a creature could react to, and the temporary effects that change how it is seen.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is deliberately NOT the observer list.</b> Observation is a set of POSITIONS and decides what gets
/// simulated; threat is a set of ENTITIES and decides what gets feared. A debug free camera is a position with
/// no identity, so it can never appear here, and wildlife ignores it without anybody writing a special case
/// for cameras. Keeping the two lists apart is the whole mechanism - see the design doc, section 16.
/// </para>
/// <para>
/// The invariant worth protecting: the observer list must never grow an identity. The moment it does, the two
/// collapse together and the camera becomes a predator again.
/// </para>
/// </remarks>
public sealed class ThreatRegistry
{
    /// <summary>
    /// The local player's identity in this registry, until players are given real minted ids on join.
    /// </summary>
    /// <remarks>
    /// Provably collision-free rather than merely unlikely: the owner is not <see cref="EntityId.HostOwner"/>
    /// so it cannot be a minted id, and a creature address always sets bit 47 of its counter (see
    /// <see cref="CreatureKey"/>) while this counter is 1, so it cannot be a creature either.
    /// </remarks>
    public static readonly EntityId LocalPlayer = new(EntityId.DerivedOwner, 1);

    // How an entity is SEEN, overriding its own faction, until it expires. A friendly-to-animals spell is one
    // of these on the caster: the caster counts as Wildlife, so every relation lookup answers differently
    // without species data being touched anywhere.
    readonly struct Disguise
    {
        public readonly CreatureFaction SeenAs;
        public readonly long StartedUnixSeconds;
        public readonly float DurationSeconds;

        public Disguise(CreatureFaction seenAs, long startedUnixSeconds, float durationSeconds)
        {
            SeenAs = seenAs;
            StartedUnixSeconds = startedUnixSeconds;
            DurationSeconds = durationSeconds;
        }

        // Whole seconds subtracted BEFORE the result becomes a float: a unix timestamp needs 31 bits and a
        // float carries 24, so doing this in float rounds the epoch to the nearest ~128 s.
        public bool Active(long now) => now < StartedUnixSeconds + (long)DurationSeconds;

        public float SecondsLeft(long now) =>
            Mathf.Max(0f, StartedUnixSeconds + (long)DurationSeconds - now);
    }

    readonly List<ThreatSource> _sources = new();
    readonly Dictionary<ulong, Disguise> _disguises = new();
    readonly List<ulong> _lapsed = new();

    FactionRelationsDto _relations = FactionRelationsDto.Default;

    public IReadOnlyList<ThreatSource> Sources => _sources;
    public int DisguiseCount => _disguises.Count;

    public void SetRelations(FactionRelationsDto relations) =>
        _relations = relations ?? FactionRelationsDto.Default;

    /// <summary>Add or move a threat-bearing entity. Called every tick by whatever owns the entity.</summary>
    public void Report(EntityId id, Vector3 position, CreatureFaction faction)
    {
        if (id.IsNone) return;
        var source = new ThreatSource(id, position, faction);
        for (int i = 0; i < _sources.Count; i++)
        {
            if (_sources[i].Id != id) continue;
            _sources[i] = source;
            return;
        }
        _sources.Add(source);
    }

    public void Withdraw(EntityId id)
    {
        for (int i = 0; i < _sources.Count; i++)
        {
            if (_sources[i].Id != id) continue;
            _sources.RemoveAt(i);
            return;
        }
    }

    public void Clear()
    {
        _sources.Clear();
        _disguises.Clear();
    }

    /// <summary>
    /// Make an entity be SEEN as another faction for a while - the mechanism a friendly-to-animals spell uses.
    /// The effect sits on the entity being looked at, never on the species doing the looking, which is what
    /// lets one caster be friendly without changing what deer think of players in general.
    /// </summary>
    /// <remarks>
    /// Stored as a record with a start and a duration rather than as a live flag, so persisting it later is an
    /// append against <see cref="DeltaKind.EntityState"/> rather than a rewrite. Nothing writes it to the log
    /// today - an active effect does not survive a reload yet, and that is Bryan's call to make when spells
    /// are designed.
    /// </remarks>
    public void SetDisguise(EntityId id, CreatureFaction seenAs, float durationSeconds, long nowUnixSeconds)
    {
        if (id.IsNone) return;
        if (durationSeconds <= 0f) { _disguises.Remove(id.Value); return; }
        _disguises[id.Value] = new Disguise(seenAs, nowUnixSeconds, durationSeconds);
    }

    /// <summary>The faction an entity is CURRENTLY seen as, which is its own unless an effect says otherwise.</summary>
    public CreatureFaction SeenAs(in ThreatSource source, long nowUnixSeconds) =>
        _disguises.TryGetValue(source.Id.Value, out Disguise d) && d.Active(nowUnixSeconds)
            ? d.SeenAs
            : source.Faction;

    /// <summary>Seconds an entity's disguise still has to run; 0 when it has none.</summary>
    public float DisguiseSecondsLeft(EntityId id, long nowUnixSeconds) =>
        _disguises.TryGetValue(id.Value, out Disguise d) ? d.SecondsLeft(nowUnixSeconds) : 0f;

    /// <summary>Drop lapsed disguises. Cheap, and keeps the dictionary from growing across a long session.</summary>
    public void PruneExpired(long nowUnixSeconds)
    {
        if (_disguises.Count == 0) return;
        _lapsed.Clear();
        foreach (KeyValuePair<ulong, Disguise> kv in _disguises)
            if (!kv.Value.Active(nowUnixSeconds)) _lapsed.Add(kv.Key);
        for (int i = 0; i < _lapsed.Count; i++) _disguises.Remove(_lapsed[i]);
    }

    /// <summary>
    /// The nearest thing within <paramref name="awarenessMeters"/> that <paramref name="observerFaction"/> is
    /// afraid of. Distance only: line of sight would need raycasts against terrain that has no colliders.
    /// </summary>
    // ponytail: distance-only detection, so a creature notices a threat through a hill. An analytic horizon
    // test against the surface sampler is the upgrade, and it is real work rather than a line.
    // ponytail: linear over every source per asking creature, so this is O(live x sources). Live is bubble-
    // bounded (tens) and sources are a player plus nearby animals, which is a few hundred checks a tick -
    // nothing. A spatial hash is the upgrade if either number grows by an order of magnitude.
    public bool TryFindThreat(Vector3 position, EntityId self, CreatureFaction observerFaction,
        float awarenessMeters, long nowUnixSeconds, out ThreatSource threat)
    {
        threat = default;
        float bestSq = awarenessMeters * awarenessMeters;
        bool found = false;

        for (int i = 0; i < _sources.Count; i++)
        {
            ThreatSource s = _sources[i];
            if (s.Id == self) continue;   // an animal reports itself; it must not frighten itself
            float sq = (s.Position - position).sqrMagnitude;
            if (sq > bestSq) continue;
            if (!_relations.IsThreat(observerFaction, SeenAs(s, nowUnixSeconds))) continue;

            bestSq = sq;
            threat = s;
            found = true;
        }
        return found;
    }
}
