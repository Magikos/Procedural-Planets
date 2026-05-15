public class SeedProvider : ISeedProvider
{
    const uint FnvOffset = 2166136261u;
    const uint FnvPrime = 16777619u;

    public int WorldSeed { get; }

    public SeedProvider(int worldSeed)
    {
        WorldSeed = worldSeed;
    }

    public int GetSeedForSystem(string systemName)
    {
        return Combine(WorldSeed, StableHash(systemName));
    }

    public int GetSeedForChunk(ChunkCoord coord)
    {
        return Combine(WorldSeed, coord.Face, coord.X, coord.Y);
    }

    public int GetSeedForEntity(ChunkCoord coord, int entityIndex)
    {
        return Combine(WorldSeed, coord.Face, coord.X, coord.Y, entityIndex);
    }

    static int StableHash(string text)
    {
        unchecked
        {
            uint hash = FnvOffset;
            if (text == null)
                return (int)hash;

            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= FnvPrime;
            }

            return (int)hash;
        }
    }

    static int Combine(params int[] values)
    {
        unchecked
        {
            uint hash = FnvOffset;
            for (int i = 0; i < values.Length; i++)
            {
                uint value = (uint)values[i];
                hash ^= value;
                hash *= FnvPrime;
                hash ^= value >> 16;
                hash *= FnvPrime;
            }

            return (int)hash;
        }
    }
}
