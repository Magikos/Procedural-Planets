public interface ISeedProvider
{
    int WorldSeed { get; }
    int GetSeedForSystem(string systemName);
    int GetSeedForChunk(ChunkCoord coord);
    int GetSeedForEntity(ChunkCoord coord, int entityIndex);
}
