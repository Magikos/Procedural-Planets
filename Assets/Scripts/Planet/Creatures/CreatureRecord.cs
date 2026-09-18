using UnityEngine;

/// <summary>A saved value copy of the authority's reserves; default means the species has no endurance simulation.</summary>
public readonly struct CreatureEnduranceState : System.IEquatable<CreatureEnduranceState>
{
    public readonly bool Enabled, Recovering, NeedsSleep;
    public readonly double Stamina, Fatigue, Capacity;
    public CreatureEnduranceState(double stamina, double fatigue, double capacity, bool recovering, bool needsSleep)
    {
        if (!ActorNeeds.IsLevel(stamina) || !ActorNeeds.IsLevel(fatigue) ||
            !double.IsFinite(capacity) || capacity < .55d || capacity > 1d || stamina > capacity)
            throw new System.ArgumentOutOfRangeException(nameof(stamina));
        Enabled = true; Stamina = stamina; Fatigue = fatigue; Capacity = capacity;
        Recovering = recovering; NeedsSleep = needsSleep;
    }
    public static CreatureEnduranceState From(ActorEndurance endurance) => endurance == null ? default :
        new(endurance.Stamina, endurance.Fatigue, endurance.Capacity, endurance.Recovering, endurance.NeedsSleep);
    public ActorEndurance Restore() => Enabled ? new(Stamina, Fatigue, Capacity, Recovering, NeedsSleep) : null;
    public bool Equals(CreatureEnduranceState other) => Enabled == other.Enabled && Stamina == other.Stamina &&
        Fatigue == other.Fatigue && Capacity == other.Capacity && Recovering == other.Recovering && NeedsSleep == other.NeedsSleep;
    public override bool Equals(object obj) => obj is CreatureEnduranceState other && Equals(other);
    public override int GetHashCode() => System.HashCode.Combine(Enabled, Stamina, Fatigue, Capacity, Recovering, NeedsSleep);
}

/// <summary>What a creature record is saying about its slot.</summary>
public enum CreatureRecordState : byte
{
    /// <summary>The slot is filled, and the creature is somewhere or doing something the seed does not predict.</summary>
    Displaced = 0,

    /// <summary>The slot is empty until the expiry lapses. Never lapses when the expiry is zero or less.</summary>
    Dead = 1,
}

/// <summary>
/// The one thing written down about a territory slot. A slot has AT MOST ONE of these, because the delta log
/// collapses every entity kind into a single live record per key - and that is the right shape here, since a
/// creature cannot be both dead and out wandering.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one record rather than two.</b> A death and a displacement would share a key and a space, so the
/// later write replaces the earlier one. Once a death lapses, a displacement written for the slot's NEXT
/// occupant would overwrite it and take the generation counter with it - and a repopulated slot silently
/// falling back to generation 0 is precisely what §5 says must never happen. Carrying the generation on every
/// record, whatever it says, is what makes the collapse safe.
/// </para>
/// <para>
/// A creature that has done nothing interesting still has NO record: it is at home, calm, and identical to
/// what the seed predicts, so there is nothing to write down. That is §5's zero-bytes promise.
/// </para>
/// </remarks>
public readonly struct CreatureRecord
{
    /// <summary>The territory slot. The generation lives in <see cref="Generation"/>, never in the key, so the
    /// record can be found without knowing which generation is current.</summary>
    public readonly EntityId Slot;

    public readonly int SpeciesIndex;

    /// <summary>
    /// The generation this record is about. Present on EVERY record, including a displacement, because the
    /// record is the only place the counter survives once a death lapses.
    /// </summary>
    public readonly int Generation;

    public readonly CreatureRecordState State;

    /// <summary>Where it died, or where it was last left standing.</summary>
    public readonly Vector3 Position;

    /// <summary>When it died, or when its position was last true.</summary>
    public readonly long UnixSeconds;

    /// <summary>Seconds a death suppresses the slot. Zero or less never lapses. Meaningless when displaced.</summary>
    public readonly float RespawnSeconds;

    /// <summary>What it was doing when it stopped being simulated. Meaningless when dead.</summary>
    public readonly CreatureBehaviour Behaviour;

    /// <summary>-1 uses the species maximum, including records written before health was persisted.</summary>
    public readonly int Health;
    public readonly ActorNeeds Needs;
    public readonly CreatureEnduranceState Endurance;

    CreatureRecord(EntityId slot, int speciesIndex, int generation, CreatureRecordState state,
        Vector3 position, long unixSeconds, float respawnSeconds, CreatureBehaviour behaviour, int health = -1,
        ActorNeeds needs = default, CreatureEnduranceState endurance = default)
    {
        // Normalised, not asserted: handing this the individual's id is the natural mistake, and a key with
        // generation bits set would file the record where no lookup goes looking for it.
        Slot = CreatureKey.SlotOf(slot);
        SpeciesIndex = speciesIndex;
        Generation = generation;
        State = state;
        Position = position;
        UnixSeconds = unixSeconds;
        RespawnSeconds = respawnSeconds;
        Behaviour = behaviour;
        Health = health;
        Needs = needs;
        Endurance = endurance;
    }

    public static CreatureRecord Death(EntityId slot, int speciesIndex, int generation, Vector3 diedAt,
        long diedUnixSeconds, float respawnSeconds) =>
        new(slot, speciesIndex, generation, CreatureRecordState.Dead, diedAt, diedUnixSeconds,
            respawnSeconds, CreatureBehaviour.Wander);

    public static CreatureRecord Displacement(EntityId slot, int speciesIndex, int generation,
        Vector3 position, long lastSimulatedUnixSeconds, CreatureBehaviour behaviour, int health = -1,
        ActorNeeds needs = default, CreatureEnduranceState endurance = default)
    {
        if (health != -1 && health <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(health), "living health must be positive or unspecified");
        return new(slot, speciesIndex, generation, CreatureRecordState.Displaced, position,
            lastSimulatedUnixSeconds, 0f, behaviour, health, needs, endurance);
    }

    public bool IsDead => State == CreatureRecordState.Dead;

    /// <summary>Catches NaN too: an unreadable expiry is permanent rather than accidentally instant.</summary>
    public bool NeverLapses => IsDead && !(RespawnSeconds > 0f);

    /// <summary>True while the slot must stay empty.</summary>
    public bool Suppresses(long nowUnixSeconds) =>
        IsDead && (NeverLapses || nowUnixSeconds < UnixSeconds + (long)RespawnSeconds);

    /// <summary>Seconds until the slot repopulates; <see cref="float.PositiveInfinity"/> when it never does.</summary>
    /// <remarks>
    /// Subtracted as whole seconds BEFORE the result becomes a float. A unix timestamp needs 31 bits and a
    /// float carries 24, so doing this arithmetic in float rounds the epoch to the nearest ~128 s - the
    /// countdown then reads as a constant while the clock is plainly moving.
    /// </remarks>
    public float SecondsUntilRespawn(long nowUnixSeconds)
    {
        if (!IsDead) return 0f;
        return NeverLapses
            ? float.PositiveInfinity
            : Mathf.Max(0f, UnixSeconds + (long)RespawnSeconds - nowUnixSeconds);
    }

    /// <summary>The generation filling the slot once this record's death has lapsed.</summary>
    public int NextGeneration => (Generation + 1) % CreatureKey.GenerationWrap;
}

/// <summary>What to do with a slot's record when its creature stops being simulated.</summary>
public enum CreatureRecordAction
{
    /// <summary>Nothing changed that is worth a write.</summary>
    Keep,

    /// <summary>Write down where it is and what it was doing.</summary>
    Write,

    /// <summary>Drop the record: the seed describes this slot again.</summary>
    Forget,
}

/// <summary>
/// When a creature is worth a byte. Pure, because it is the rule that decides whether the save grows.
/// </summary>
/// <remarks>
/// Two promises meet here. §5 says a creature that has done nothing interesting costs zero bytes, so an
/// animal standing near its home writes nothing. §6 says the log shrinks back toward the seed, so one that
/// wandered off and later drifted home has its record DROPPED rather than left behind. The exception is a slot
/// whose generation has moved on: that counter lives nowhere else, so its record survives even when it has
/// nothing else to say.
/// </remarks>
public static class CreatureRecordPolicy
{
    /// <summary>Fraction of the home range beyond which being left somewhere counts as displaced. Inside it,
    /// the creature is roughly where the seed would have put it anyway.</summary>
    public const float NotableDisplacementFraction = 0.5f;

    /// <summary>How far it must have moved since the last write to justify rewriting. Without this, a creature
    /// crossing the bubble boundary appends a record every time it leaves.</summary>
    public const float RewriteMoveMeters = 10f;

    public static bool IsNotable(float metresFromHome, float homeRangeMeters, CreatureBehaviour behaviour) =>
        metresFromHome > Mathf.Max(0f, homeRangeMeters) * NotableDisplacementFraction ||
        behaviour != CreatureBehaviour.Wander;

    public static CreatureRecordAction Decide(
        float metresFromHome, float homeRangeMeters, CreatureBehaviour behaviour, int generation,
        bool alreadyWritten, float metresSinceWritten, CreatureBehaviour writtenBehaviour,
        bool hasPersistentState = false, bool persistentStateChanged = false)
    {
        bool notable = hasPersistentState || IsNotable(metresFromHome, homeRangeMeters, behaviour);

        // Nothing to say, and no generation to protect: the seed covers this slot completely.
        if (!notable && generation == 0)
            return alreadyWritten ? CreatureRecordAction.Forget : CreatureRecordAction.Keep;

        if (!alreadyWritten)
            return CreatureRecordAction.Write;

        return persistentStateChanged || behaviour != writtenBehaviour || metresSinceWritten >= RewriteMoveMeters
            ? CreatureRecordAction.Write
            : CreatureRecordAction.Keep;
    }
}

/// <summary>
/// Encodes a <see cref="CreatureRecord"/> as a delta record. It reuses the existing entity kinds rather than
/// adding one, so the log's collapse-to-one-live-record-per-entity does the work: a death replaces a
/// displacement, and a later displacement replaces the death once it has lapsed.
/// </summary>
/// <remarks>
/// The position is hoisted into <see cref="WorldDelta.Position"/> so spatial code - a replication interest
/// bucket, a log inspector - can place a record without knowing this payload's encoding. The payload carries
/// its own format byte; format 1 was death-only and is still read.
/// </remarks>
public static class CreatureRecordCodec
{
    const byte PayloadFormatDeathOnly = 1;   // generation 4 | died 8 | respawn 4
    const int PayloadBytesDeathOnly = 17;

    const byte PayloadFormat2 = 2;           // generation 4 | unix 8 | respawn 4 | behaviour 1
    const int PayloadBytes2 = 18;

    const byte PayloadFormat3 = 3;
    const int PayloadBytes3 = 22;
    const byte PayloadFormat4 = 4;
    const int PayloadBytes4 = 38;
    const byte PayloadFormat5 = 5;
    const int PayloadBytes5 = 63;

    public static WorldDelta Encode(in CreatureRecord record)
    {
        var payload = new byte[PayloadBytes5];
        payload[0] = PayloadFormat5;
        System.BitConverter.GetBytes(record.Generation).CopyTo(payload, 1);
        System.BitConverter.GetBytes(record.UnixSeconds).CopyTo(payload, 5);
        System.BitConverter.GetBytes(record.RespawnSeconds).CopyTo(payload, 13);
        payload[17] = (byte)record.Behaviour;
        System.BitConverter.GetBytes(record.Health).CopyTo(payload, 18);
        System.BitConverter.GetBytes(record.Needs.Hunger).CopyTo(payload, 22);
        System.BitConverter.GetBytes(record.Needs.Thirst).CopyTo(payload, 30);
        System.BitConverter.GetBytes(record.Endurance.Stamina).CopyTo(payload, 38);
        System.BitConverter.GetBytes(record.Endurance.Fatigue).CopyTo(payload, 46);
        System.BitConverter.GetBytes(record.Endurance.Capacity).CopyTo(payload, 54);
        payload[62] = (byte)((record.Endurance.Enabled ? 1 : 0) | (record.Endurance.Recovering ? 2 : 0) | (record.Endurance.NeedsSleep ? 4 : 0));

        return new WorldDelta(0,
            record.IsDead ? DeltaKind.EntityRemoved : DeltaKind.EntityMoved,
            record.Slot.Value, record.Position,
            typeIndex: record.SpeciesIndex, state: (byte)record.State, payload: payload);
    }

    /// <summary>A slot with nothing worth saying about it. Last write per key wins, so this REPLACES the
    /// record rather than adding one, and the log shrinks back toward the seed.</summary>
    public static WorldDelta Forget(EntityId slot) =>
        new(0, DeltaKind.EntityMoved, CreatureKey.SlotOf(slot).Value);

    public static bool TryDecode(in WorldDelta delta, out CreatureRecord record)
    {
        record = default;
        if (delta.Kind != DeltaKind.EntityRemoved && delta.Kind != DeltaKind.EntityMoved)
            return false;

        var id = new EntityId(delta.Key);
        if (!CreatureKey.IsCreature(id))
            return false;   // a dropped log shares these kinds; the owner tag is what tells them apart

        byte[] payload = delta.Payload;
        if (payload == null || payload.Length < PayloadBytesDeathOnly)
            return false;   // a forget-record, or something else entirely

        switch (payload[0])
        {
            case PayloadFormat5 when payload.Length == PayloadBytes5:
            case PayloadFormat4 when payload.Length == PayloadBytes4:
            case PayloadFormat3 when payload.Length == PayloadBytes3:
                ActorNeeds needs = default;
                if (payload[0] >= PayloadFormat4)
                {
                    double hunger = System.BitConverter.ToDouble(payload, 22);
                    double thirst = System.BitConverter.ToDouble(payload, 30);
                    if (!ActorNeeds.IsLevel(hunger) || !ActorNeeds.IsLevel(thirst)) return false;
                    needs = new ActorNeeds(hunger, thirst);
                }
                CreatureEnduranceState endurance = default;
                if (payload[0] == PayloadFormat5)
                {
                    byte flags = payload[62];
                    double stamina = System.BitConverter.ToDouble(payload, 38), fatigue = System.BitConverter.ToDouble(payload, 46),
                        capacity = System.BitConverter.ToDouble(payload, 54);
                    if ((flags & ~7) != 0) return false;
                    if ((flags & 1) != 0)
                    {
                        if (!ActorNeeds.IsLevel(stamina) || !ActorNeeds.IsLevel(fatigue) ||
                            !double.IsFinite(capacity) || capacity < .55d || capacity > 1d || stamina > capacity) return false;
                        endurance = new CreatureEnduranceState(stamina, fatigue, capacity, (flags & 2) != 0, (flags & 4) != 0);
                    }
                    else if (flags != 0 || stamina != 0d || fatigue != 0d || capacity != 0d) return false;
                }
                int health = System.BitConverter.ToInt32(payload, 18);
                int generation = System.BitConverter.ToInt32(payload, 1);
                if (generation < 0 || generation >= CreatureKey.GenerationWrap || delta.TypeIndex < 0 ||
                    (delta.State == (byte)CreatureRecordState.Dead && endurance.Enabled) ||
                    (delta.State != (byte)CreatureRecordState.Dead && delta.State != (byte)CreatureRecordState.Displaced) ||
                    (delta.State == (byte)CreatureRecordState.Dead) != (delta.Kind == DeltaKind.EntityRemoved) ||
                    (health != -1 && (health <= 0 || delta.State == (byte)CreatureRecordState.Dead)))
                    return false;
                record = Rebuild(delta, id, (CreatureRecordState)delta.State, (CreatureBehaviour)payload[17], health, needs, endurance);
                return true;

            case PayloadFormat2 when payload.Length >= PayloadBytes2:
                record = Rebuild(delta, id, (CreatureRecordState)delta.State, (CreatureBehaviour)payload[17]);
                return true;

            // Format 1 predates displacement records, so everything it describes is a death, and its
            // behaviour is whatever a fresh creature starts as.
            case PayloadFormatDeathOnly:
                record = Rebuild(delta, id, CreatureRecordState.Dead, CreatureBehaviour.Wander);
                return true;

            default:
                return false;
        }
    }

    static CreatureRecord Rebuild(in WorldDelta delta, EntityId slot, CreatureRecordState state,
        CreatureBehaviour behaviour, int health = -1, ActorNeeds needs = default, CreatureEnduranceState endurance = default)
    {
        int generation = System.BitConverter.ToInt32(delta.Payload, 1);
        long unix = System.BitConverter.ToInt64(delta.Payload, 5);
        float respawn = System.BitConverter.ToSingle(delta.Payload, 13);

        return state == CreatureRecordState.Dead
            ? CreatureRecord.Death(slot, delta.TypeIndex, generation, delta.Position, unix, respawn)
            : CreatureRecord.Displacement(slot, delta.TypeIndex, generation, delta.Position, unix, behaviour, health, needs, endurance);
    }
}
