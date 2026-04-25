using System;

public class SeedProvider : ISeedProvider
{
    public int WorldSeed { get; }

    public SeedProvider(int worldSeed)
    {
        WorldSeed = worldSeed;
    }

    public int GetSeedForSystem(string systemName)
    {
        return HashCode.Combine(WorldSeed, systemName.GetHashCode());
    }

    public int GetSeedForChunk(ChunkCoord coord)
    {
        return HashCode.Combine(WorldSeed, coord.GetHashCode());
    }

    public int GetSeedForEntity(ChunkCoord coord, int entityIndex)
    {
        return HashCode.Combine(WorldSeed, coord.GetHashCode(), entityIndex);
    }
}
