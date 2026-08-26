using UnityEngine;

/// <summary>
/// One creature's death, as it is written down. This is the only thing about a wild creature that costs
/// storage, and it is also the whole of population control: while the record suppresses its slot the territory
/// is one animal short, and when it lapses the slot repopulates on its own with the next generation.
/// </summary>
/// <remarks>
/// A species that never respawns is the same record with <see cref="RespawnSeconds"/> at or below zero - a
/// unique boss is a territory of one that never lapses, not a second system.
/// </remarks>
public readonly struct CreatureDeathRecord
{
    /// <summary>The territory slot that died. The generation lives in <see cref="Generation"/>, not the key,
    /// so the record can be found without knowing which generation is current.</summary>
    public readonly EntityId Slot;

    public readonly int SpeciesIndex;

    /// <summary>The generation that died. The slot's next occupant is this plus one.</summary>
    public readonly int Generation;

    public readonly Vector3 DiedAt;
    public readonly long DiedUnixSeconds;

    /// <summary>Seconds the death suppresses the slot. Zero or less never lapses.</summary>
    public readonly float RespawnSeconds;

    public CreatureDeathRecord(EntityId slot, int speciesIndex, int generation, Vector3 diedAt,
        long diedUnixSeconds, float respawnSeconds)
    {
        // Normalised, not asserted: handing this the individual's id is the natural mistake, and a key with
        // generation bits set would file the death where no lookup goes looking for it.
        Slot = CreatureKey.SlotOf(slot);
        SpeciesIndex = speciesIndex;
        Generation = generation;
        DiedAt = diedAt;
        DiedUnixSeconds = diedUnixSeconds;
        RespawnSeconds = respawnSeconds;
    }

    public bool NeverLapses => !(RespawnSeconds > 0f);   // catches NaN too: an unreadable expiry is permanent

    /// <summary>True while the slot must stay empty.</summary>
    public bool Suppresses(long nowUnixSeconds) =>
        NeverLapses || nowUnixSeconds < DiedUnixSeconds + (long)RespawnSeconds;

    /// <summary>Seconds until the slot repopulates; <see cref="float.PositiveInfinity"/> when it never does.</summary>
    /// <remarks>
    /// Subtracted as whole seconds BEFORE the result becomes a float. A unix timestamp needs 31 bits and a
    /// float carries 24, so doing this arithmetic in float rounds the epoch to the nearest ~128 s - the
    /// countdown then reads as a constant while the clock is plainly moving.
    /// </remarks>
    public float SecondsUntilRespawn(long nowUnixSeconds) =>
        NeverLapses
            ? float.PositiveInfinity
            : Mathf.Max(0f, DiedUnixSeconds + (long)RespawnSeconds - nowUnixSeconds);

    /// <summary>
    /// The generation now filling the slot. Only meaningful once the record has lapsed - while it suppresses,
    /// nothing fills the slot at all.
    /// </summary>
    public int NextGeneration => (Generation + 1) % CreatureKey.GenerationWrap;
}

/// <summary>
/// Encodes a <see cref="CreatureDeathRecord"/> as a delta record. It reuses <see cref="DeltaKind.EntityRemoved"/>
/// rather than adding a kind, so the log's existing collapse-to-one-live-record-per-entity applies: killing the
/// same slot twice leaves one record, not two.
/// </summary>
/// <remarks>
/// The death position is hoisted into the record's <see cref="WorldDelta.Position"/> so spatial code - a
/// replication interest bucket, a log inspector - can place a death without knowing this payload's encoding.
/// The payload carries its own format byte; a later field is added by writing a new format and keeping a read
/// path for the old one, the same way the log itself versions.
/// </remarks>
public static class CreatureDeathCodec
{
    const byte PayloadFormat1 = 1;
    const int PayloadBytes1 = 17;   // format 1 | generation 4 | died 8 | respawn 4

    public static WorldDelta Encode(in CreatureDeathRecord record)
    {
        var payload = new byte[PayloadBytes1];
        payload[0] = PayloadFormat1;
        System.BitConverter.GetBytes(record.Generation).CopyTo(payload, 1);
        System.BitConverter.GetBytes(record.DiedUnixSeconds).CopyTo(payload, 5);
        System.BitConverter.GetBytes(record.RespawnSeconds).CopyTo(payload, 13);
        return new WorldDelta(0, DeltaKind.EntityRemoved, record.Slot.Value, record.DiedAt,
            typeIndex: record.SpeciesIndex, payload: payload);
    }

    public static bool TryDecode(in WorldDelta delta, out CreatureDeathRecord record)
    {
        record = default;
        if (delta.Kind != DeltaKind.EntityRemoved)
            return false;

        var id = new EntityId(delta.Key);
        if (!CreatureKey.IsCreature(id))
            return false;   // a removed dropped log shares this kind; the owner tag is what tells them apart

        byte[] payload = delta.Payload;
        if (payload == null || payload.Length < PayloadBytes1 || payload[0] != PayloadFormat1)
            return false;

        record = new CreatureDeathRecord(
            id,
            delta.TypeIndex,
            System.BitConverter.ToInt32(payload, 1),
            delta.Position,
            System.BitConverter.ToInt64(payload, 5),
            System.BitConverter.ToSingle(payload, 13));
        return true;
    }
}
