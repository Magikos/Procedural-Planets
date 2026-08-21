using UnityEngine;

/// <summary>Every kind of persistent world change. The value is written to disk and the wire; never renumber.</summary>
public enum DeltaKind : byte
{
    None = 0,
    ScatterRemoved = 1,   // a chopped tree
    ScatterState = 2,     // stump, dug
    SurfaceStamp = 3,     // path wear, scorch
    EntitySpawned = 4,    // a dropped log, a placed workbench
    EntityMoved = 5,
    EntityRemoved = 6,
    EntityState = 7,      // imbue records, container contents, ritual progress
    PlayerState = 8,      // position, mana, bag - one per player
    TerrainDeform = 9,    // planned: digging and caves, docs/design/2026-08-20-magikos-game-architecture.md section 6.5
}

/// <summary>
/// One persistent world change. The same record serves the save file, the join snapshot and the replication
/// stream, which is the point: a client that replays the log arrives at the state the host has.
/// </summary>
/// <remarks>
/// <para>
/// <b>Key is a ScatterId or an EntityId depending on Kind, and the two never mix.</b> A ScatterId encodes a
/// derived cell address; an explicit object has no cell address. Reading one as the other silently addresses
/// the wrong thing rather than failing.
/// </para>
/// <para>
/// The common fields cover the kinds that are only ever a transform plus a small type - a stump, a dropped
/// log, a move - so those records allocate nothing beyond the struct. Kinds that carry more than a transform
/// (a surface stamp, a container's contents) put the rest in <see cref="Payload"/> and own its encoding.
/// Both shapes are needed: a fixed record cannot express container contents, and forcing every chop through
/// a byte array would allocate for the common case.
/// </para>
/// <para>
/// <b><see cref="Payload"/> is treated as immutable.</b> The struct copies by value but the array is shared,
/// so a caller that mutates a payload after appending corrupts a record that is already written.
/// </para>
/// </remarks>
public readonly struct WorldDelta
{
    /// <summary>Bytes before the payload: sequence, kind, key, payload length, transform, type and state.</summary>
    public const int FixedBytes = 48;

    /// <summary>Bytes after the payload. A checksum, so a torn tail is detected rather than trusted.</summary>
    public const int ChecksumBytes = 4;

    public const int MaxPayloadBytes = ushort.MaxValue;

    /// <summary>Host-assigned and monotonic. The ordering authority - replay order, not wall-clock order.</summary>
    public readonly uint Sequence;
    public readonly DeltaKind Kind;

    /// <summary>A ScatterId or an EntityId, per <see cref="Kind"/>.</summary>
    public readonly ulong Key;

    public readonly Vector3 Position;
    public readonly Quaternion Rotation;

    /// <summary>Prototype index, item type, or stamp type, per <see cref="Kind"/>.</summary>
    public readonly int TypeIndex;

    /// <summary>Small per-kind enum - stump vs dug, and so on.</summary>
    public readonly byte State;

    /// <summary>Extra bytes for kinds the common fields cannot express. Null when there are none.</summary>
    public readonly byte[] Payload;

    public WorldDelta(uint sequence, DeltaKind kind, ulong key,
        Vector3 position = default, Quaternion rotation = default, int typeIndex = 0, byte state = 0,
        byte[] payload = null)
    {
        if (payload != null && payload.Length > MaxPayloadBytes)
            throw new System.ArgumentOutOfRangeException(nameof(payload), payload.Length,
                $"payload must be at most {MaxPayloadBytes} bytes");
        Sequence = sequence;
        Kind = kind;
        Key = key;
        Position = position;
        Rotation = rotation;
        TypeIndex = typeIndex;
        State = state;
        Payload = payload != null && payload.Length > 0 ? payload : null;
    }

    public int PayloadLength => Payload?.Length ?? 0;

    /// <summary>Bytes this record occupies on disk.</summary>
    public int SerializedSize => FixedBytes + PayloadLength + ChecksumBytes;

    public WorldDelta WithSequence(uint sequence) =>
        new(sequence, Kind, Key, Position, Rotation, TypeIndex, State, Payload);

    public override string ToString() =>
        $"#{Sequence} {Kind} key={Key:X} type={TypeIndex} state={State} payload={PayloadLength}B";
}
