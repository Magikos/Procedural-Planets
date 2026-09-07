using System;

/// <summary>
/// Identity for an explicit object - one the seed did not create. A dropped log, a placed workbench, an orb
/// on the ground.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a separate space from <c>ScatterId</c> and never interchangeable with it. A ScatterId encodes
/// a derived cell address; a dropped item has no cell address to encode. Reading one as the other addresses
/// the wrong object silently rather than failing, which is why the two are distinct types rather than two
/// uses of <c>ulong</c>.
/// </para>
/// <para>
/// The layout is owner-tagged so it survives the arrival of multiplayer without a schema break: the high 16
/// bits name who minted the id, the low 48 bits are that owner's monotonic counter. The host mints under
/// <see cref="HostOwner"/>; a client predicting a spawn mints under its own tag, and the host's authoritative
/// id replaces it on confirmation. Two owners can therefore allocate at once without coordinating and without
/// ever colliding.
/// </para>
/// </remarks>
public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
{
    public const int CounterBits = 48;
    public const ulong CounterMask = (1UL << CounterBits) - 1;

    /// <summary>Largest counter an owner can reach - 2.8e14, so overflow is a bug rather than a lifetime.</summary>
    public const ulong MaxCounter = CounterMask;

    /// <summary>The host's tag. Ids under it are authoritative; every other tag is a client's prediction.</summary>
    public const ushort HostOwner = 0;

    /// <summary>
    /// Reserved for ids DERIVED from the world seed rather than allocated - a wild creature's
    /// (spawner, slot, generation) address. A derived id must sit in the same key space as a minted one,
    /// because the delta log's entity records address both; reserving an owner tag is what stops the two
    /// ever being the same number. No allocator mints under it.
    /// </summary>
    public const ushort DerivedOwner = 0xFFFF;

    /// <summary>
    /// Reserved for carcasses. They ARE minted rather than derived, so they would normally sit under
    /// <see cref="HostOwner"/> - but a carcass shares the delta log's entity space with the seed-derived
    /// creature slot it came from, and a slot key at generation zero is numerically the same as the
    /// individual that died in it. A separate owner tag is what keeps the two apart.
    /// </summary>
    public const ushort CorpseOwner = 0xFFFE;

    /// <summary>Reserved for the world's transient aquatic population; never mixed with land slots or carcasses.</summary>
    public const ushort AquaticOwner = 0xFFFD;

    public readonly ulong Value;

    public EntityId(ulong value) => Value = value;

    public EntityId(ushort owner, ulong counter)
    {
        if (counter == 0 || counter > MaxCounter)
            throw new ArgumentOutOfRangeException(nameof(counter), counter, $"counter must be 1..{MaxCounter}");
        Value = ((ulong)owner << CounterBits) | counter;
    }

    /// <summary>No entity. Counters start at 1, so a zero value can never be a real id.</summary>
    public static readonly EntityId None = default;

    public ushort Owner => (ushort)(Value >> CounterBits);
    public ulong Counter => Value & CounterMask;
    public bool IsNone => Value == 0;

    /// <summary>False while an id is a client's local prediction awaiting the host's confirmation.</summary>
    public bool IsAuthoritative => Owner == HostOwner;

    public bool Equals(EntityId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is EntityId other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(EntityId other) => Value.CompareTo(other.Value);
    public static bool operator ==(EntityId a, EntityId b) => a.Value == b.Value;
    public static bool operator !=(EntityId a, EntityId b) => a.Value != b.Value;

    public override string ToString() => IsNone ? "EntityId.None" : $"E{Owner}:{Counter}";
}

/// <summary>Mints <see cref="EntityId"/>s for one owner.</summary>
/// <remarks>
/// One allocator per owner, and the owner tag is fixed at construction: that is what lets a client allocate
/// during prediction without asking the host. On load the allocator must be shown every id already in the
/// save through <see cref="Observe"/>, or the next mint reuses an id that a live object still holds.
/// </remarks>
public sealed class EntityIdAllocator
{
    readonly ushort _owner;
    ulong _nextCounter = 1;

    public EntityIdAllocator(ushort owner = EntityId.HostOwner)
    {
        if (owner == EntityId.DerivedOwner)
            throw new ArgumentOutOfRangeException(nameof(owner), owner,
                $"owner {EntityId.DerivedOwner:X} is reserved for seed-derived ids and cannot be minted from");
        _owner = owner;
    }

    public ushort Owner => _owner;

    /// <summary>The counter the next mint will use. Persist this, or replay every id through <see cref="Observe"/>.</summary>
    public ulong NextCounter => _nextCounter;

    public EntityId Next()
    {
        if (_nextCounter > EntityId.MaxCounter)
            throw new InvalidOperationException($"EntityId counter exhausted for owner {_owner}");
        return new EntityId(_owner, _nextCounter++);
    }

    /// <summary>Records an id loaded from a save so the next mint clears it. Ids from other owners are ignored.</summary>
    public void Observe(EntityId id)
    {
        if (id.IsNone || id.Owner != _owner) return;
        if (id.Counter >= _nextCounter) _nextCounter = id.Counter + 1;
    }

    public void Reset() => _nextCounter = 1;
}
