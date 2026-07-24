using System;

// Packs a scatter instance's stable identity into a u64: the persistence key SP5 writes
// chop/collect overrides against, and the identity that lets a GPU far-drawn instance and a CPU
// near-spawned GameObject agree they are the same object. `slot` is the prototype's immutable
// SlotId, never the library array index, so reordering the library never moves an id.
public static class ScatterId
{
    const int FaceBits = 3, LevelBits = 5, CoordBits = 24, SlotBits = 4;
    const int LevelShift = FaceBits;                 // 3
    const int XShift = LevelShift + LevelBits;       // 8
    const int YShift = XShift + CoordBits;           // 32
    const int SlotShift = YShift + CoordBits;        // 56
    const int PlayerShift = SlotShift + SlotBits;    // 60

    const ulong FaceMask = (1UL << FaceBits) - 1;
    const ulong LevelMask = (1UL << LevelBits) - 1;
    const ulong CoordMask = (1UL << CoordBits) - 1;
    const ulong SlotMask = (1UL << SlotBits) - 1;

    // Operational max placement level = coordinate-bit count: at level L, cell coords span
    // 0..2^L-1, which must fit CoordBits. Also fits LevelBits (max 31). Single source of truth.
    public const int MaxLevel = CoordBits;           // 24
    public const int MaxSlot = (1 << SlotBits) - 1;  // 15
    public const int FaceCount = 6;

    // Unconditional validation (not #if): the id is a persistence key — a masked-in invalid value
    // in a shipped build would rebind a saved chop/collect to the wrong object. Masks are packing
    // mechanics only.
    public static ulong Pack(int face, int level, int x, int y, int slot, bool player = false)
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
             | (((ulong)slot & SlotMask) << SlotShift)
             | ((player ? 1UL : 0UL) << PlayerShift);
    }

    public static bool IsPlayer(ulong id) => ((id >> PlayerShift) & 1UL) != 0UL;

    public static void Unpack(ulong id, out int face, out int level, out int x, out int y, out int slot)
    {
        face = (int)(id & FaceMask);
        level = (int)((id >> LevelShift) & LevelMask);
        x = (int)((id >> XShift) & CoordMask);
        y = (int)((id >> YShift) & CoordMask);
        slot = (int)((id >> SlotShift) & SlotMask);
    }
}
