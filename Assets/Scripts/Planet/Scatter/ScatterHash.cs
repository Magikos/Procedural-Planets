// Deterministic integer hashing for scatter placement. Node/slot seeds are pure functions of
// (worldSeed, face, level, cell, slot) with no cross-node state, so a gather's output is invariant
// to query order or region. Mirrors the fixed-multiplier style used by the grass placement compute.
public static class ScatterHash
{
    public static uint Mix(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352du;
        x ^= x >> 15; x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }

    public static uint Node(int worldSeed, int face, int level, int x, int y)
    {
        uint h = (uint)worldSeed * 747796405u + 2891336453u;
        h = Mix(h ^ ((uint)face * 0x9e3779b1u));
        h = Mix(h ^ ((uint)level * 0x85ebca77u));
        h = Mix(h ^ ((uint)x * 0xc2b2ae3du));
        h = Mix(h ^ ((uint)y * 0x27d4eb2fu));
        return h;
    }

    public static uint Slot(uint nodeSeed, int slot) => Mix(nodeSeed ^ ((uint)slot * 0x9e3779b1u));

    public static float To01(uint h) => (h & 0x00FFFFFFu) / 16777216f;
}
