using System;

// Packs a scatter instance's stable identity into a u64: the persistence key SP5 writes
// chop/collect overrides against, and the identity that lets a GPU far-drawn instance and a CPU
// near-spawned GameObject agree they are the same object. `slot` is the prototype's immutable
// SlotId, never the library array index, so reordering the library never moves an id.
public static class ScatterId
{
    // Public so the Burst packer (ScatterGatherBurst.PackUnchecked) derives its shifts/masks from
    // the same bit counts instead of re-declaring them — a re-declared SlotBits once drifted to 6
    // and aliased slots 64..127. Single source of truth for the layout.
    // Bit 63 was a reserved "player-placed" flag. Nothing ever set it — only the verify self-test — and it was
    // the wrong mechanism anyway: a player-placed object has no cell address, because its position is chosen
    // rather than derived from the seed, so the other 63 bits would carry no meaning for it. Player objects
    // need their own store. Reclaiming the bit widened the slot field instead.
    //
    // Safe for existing saves: the flag was always 0 in every id the game wrote, so slots 0..127 decode
    // identically under the wider mask. If a unified handle is ever wanted, reserve a SLOT VALUE to mean
    // "look this up elsewhere" rather than taking a bit back.
    public const int FaceBits = 3, LevelBits = 5, CoordBits = 24, SlotBits = 8;
    const int LevelShift = FaceBits;                 // 3
    const int XShift = LevelShift + LevelBits;       // 8
    const int YShift = XShift + CoordBits;           // 32
    const int SlotShift = YShift + CoordBits;        // 56 (slot 56..63 — all 64 bits used)

    const ulong FaceMask = (1UL << FaceBits) - 1;
    const ulong LevelMask = (1UL << LevelBits) - 1;
    const ulong CoordMask = (1UL << CoordBits) - 1;
    const ulong SlotMask = (1UL << SlotBits) - 1;

    // Operational max placement level = coordinate-bit count: at level L, cell coords span
    // 0..2^L-1, which must fit CoordBits. Also fits LevelBits (max 31). Single source of truth.
    public const int MaxLevel = CoordBits;           // 24
    public const int MaxSlot = (1 << SlotBits) - 1;  // 127
    public const int FaceCount = 6;

    // Unconditional validation (not #if): the id is a persistence key — a masked-in invalid value
    // in a shipped build would rebind a saved chop/collect to the wrong object. Masks are packing
    // mechanics only.
    public static ulong Pack(int face, int level, int x, int y, int slot)
    {
        if ((uint)face >= FaceCount)
            throw new ArgumentOutOfRangeException(nameof(face), face, "scatter face must be 0..5");
        if (level < 0 || level > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level), level, $"scatter level must be 0..{MaxLevel}");
        if ((uint)x >= (1u << CoordBits) || (uint)y >= (1u << CoordBits))
            throw new ArgumentOutOfRangeException(nameof(x), $"scatter cell ({x},{y}) overflows {CoordBits}-bit id");
        if ((uint)slot > MaxSlot)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, $"scatter slot must be 0..{MaxSlot}");
        return ((ulong)face & FaceMask)
             | (((ulong)level & LevelMask) << LevelShift)
             | (((ulong)(uint)x & CoordMask) << XShift)
             | (((ulong)(uint)y & CoordMask) << YShift)
             | (((ulong)slot & SlotMask) << SlotShift);
    }

    public static void Unpack(ulong id, out int face, out int level, out int x, out int y, out int slot)
    {
        face = (int)(id & FaceMask);
        level = (int)((id >> LevelShift) & LevelMask);
        x = (int)((id >> XShift) & CoordMask);
        y = (int)((id >> YShift) & CoordMask);
        slot = (int)((id >> SlotShift) & SlotMask);
    }
}
