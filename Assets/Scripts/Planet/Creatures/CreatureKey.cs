using System;

/// <summary>
/// The derived identity of a wild creature: which territory it belongs to, which of that territory's slots it
/// fills, and which generation of that slot it is. Every part is computed from the world seed, so the id costs
/// no storage and two processes agree on it without talking.
/// </summary>
/// <remarks>
/// <para>
/// The value is an <see cref="EntityId"/> under <see cref="EntityId.DerivedOwner"/>. That matters: a creature's
/// death record shares the delta log's entity space with a dropped log's minted id, and nothing else stops the
/// two being the same number. The owner tag is the split.
/// </para>
/// <para>
/// <b>The generation is part of the id, not of the address.</b> <see cref="Slot"/> - the same packing with
/// generation 0 - is the persistence key, so a slot's death record can be found without knowing which
/// generation is current. <see cref="Individual"/> adds the generation and identifies one animal, which is what
/// makes a repopulated slot a NEW creature rather than the one you killed.
/// </para>
/// <para>
/// Level is packed even though the spawner lattice level is a constant today. If that constant ever changes,
/// old keys decode to a different level and are simply orphaned, rather than silently rebinding a saved death
/// to a territory that is no longer the same patch of ground.
/// </para>
/// </remarks>
public static class CreatureKey
{
    // Bit 47 is always 1: EntityId.None is value 0, and the all-zero territory (face 0, level 0, cell 0,0,
    // slot 0, generation 0) is a real address that would otherwise pack to it.
    public const int GenerationBits = 9;
    public const int SlotBits = 6;
    public const int CoordBits = 12;
    public const int LevelBits = 5;
    public const int FaceBits = 3;

    const int GenerationShift = 0;
    const int SlotShift = GenerationShift + GenerationBits;   // 9
    const int YShift = SlotShift + SlotBits;                  // 15
    const int XShift = YShift + CoordBits;                    // 27
    const int LevelShift = XShift + CoordBits;                // 39
    const int FaceShift = LevelShift + LevelBits;             // 44
    const int MarkerShift = FaceShift + FaceBits;             // 47

    const ulong GenerationMask = (1UL << GenerationBits) - 1;
    const ulong SlotMask = (1UL << SlotBits) - 1;
    const ulong CoordMask = (1UL << CoordBits) - 1;
    const ulong LevelMask = (1UL << LevelBits) - 1;
    const ulong FaceMask = (1UL << FaceBits) - 1;

    public const int FaceCount = 6;
    public const int MaxLevel = (1 << LevelBits) - 1;
    public const int MaxCoord = (1 << CoordBits) - 1;
    public const int MaxSlot = (1 << SlotBits) - 1;

    /// <summary>Generations wrap here. 512 deaths of one slot before an id repeats is far past any session.</summary>
    public const int GenerationWrap = 1 << GenerationBits;

    /// <summary>The persistence key for a territory slot: the individual id with the generation zeroed.</summary>
    public static EntityId Slot(int face, int level, int x, int y, int slot) =>
        Individual(face, level, x, y, slot, 0);

    /// <summary>The id of one animal - a slot plus the generation currently filling it.</summary>
    public static EntityId Individual(int face, int level, int x, int y, int slot, int generation)
    {
        // Validated rather than masked: this is a persistence key, and a masked-in out-of-range value would
        // rebind a saved death to a different territory.
        if ((uint)face >= FaceCount)
            throw new ArgumentOutOfRangeException(nameof(face), face, $"creature face must be 0..{FaceCount - 1}");
        if ((uint)level > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level), level, $"creature level must be 0..{MaxLevel}");
        if ((uint)x > MaxCoord || (uint)y > MaxCoord)
            throw new ArgumentOutOfRangeException(nameof(x), $"creature cell ({x},{y}) overflows {CoordBits} bits");
        if ((uint)slot > MaxSlot)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, $"creature slot must be 0..{MaxSlot}");
        if ((uint)generation >= GenerationWrap)
            throw new ArgumentOutOfRangeException(nameof(generation), generation,
                $"creature generation must be 0..{GenerationWrap - 1}");

        ulong counter =
            (1UL << MarkerShift)
            | ((ulong)face << FaceShift)
            | ((ulong)level << LevelShift)
            | ((ulong)(uint)x << XShift)
            | ((ulong)(uint)y << YShift)
            | ((ulong)slot << SlotShift)
            | ((ulong)generation << GenerationShift);
        return new EntityId(EntityId.DerivedOwner, counter);
    }

    const ulong MarkerMask = 1UL << MarkerShift;

    /// <summary>
    /// True when the id is a well-formed creature address: the derived owner tag AND the marker bit every
    /// packed address carries.
    /// </summary>
    /// <remarks>
    /// The marker is part of the test, not just of the packing. The owner tag alone is a namespace, and other
    /// well-known derived ids live in it - the local player's threat-registry id is one. Checking only the tag
    /// reads those as creatures and hands them to <see cref="Unpack"/>, which then produces a plausible
    /// territory that does not exist.
    /// </remarks>
    public static bool IsCreature(EntityId id) =>
        id.Owner == EntityId.DerivedOwner && (id.Counter & MarkerMask) != 0;

    public static void Unpack(EntityId id, out int face, out int level, out int x, out int y,
        out int slot, out int generation)
    {
        ulong c = id.Counter;
        face = (int)((c >> FaceShift) & FaceMask);
        level = (int)((c >> LevelShift) & LevelMask);
        x = (int)((c >> XShift) & CoordMask);
        y = (int)((c >> YShift) & CoordMask);
        slot = (int)((c >> SlotShift) & SlotMask);
        generation = (int)((c >> GenerationShift) & GenerationMask);
    }

    public static int GenerationOf(EntityId id) => (int)(id.Counter & GenerationMask);

    /// <summary>The slot address an individual belongs to.</summary>
    public static EntityId SlotOf(EntityId individual) =>
        new(EntityId.DerivedOwner, individual.Counter & ~GenerationMask);

    /// <summary>The individual filling a slot at a given generation.</summary>
    public static EntityId AtGeneration(EntityId slot, int generation)
    {
        if ((uint)generation >= GenerationWrap)
            throw new ArgumentOutOfRangeException(nameof(generation), generation,
                $"creature generation must be 0..{GenerationWrap - 1}");
        return new EntityId(EntityId.DerivedOwner, (slot.Counter & ~GenerationMask) | (ulong)(uint)generation);
    }

    public static string Describe(EntityId id)
    {
        if (!IsCreature(id)) return id.ToString();
        Unpack(id, out int face, out int level, out int x, out int y, out int slot, out int generation);
        return $"C{face}/{level}:{x},{y}#{slot}g{generation}";
    }
}
