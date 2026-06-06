public interface ISeedProvider
{
    int WorldSeed { get; }
    int GetSeedForSystem(string systemName);
    int GetSeedForChunk(ChunkCoord coord);
    int GetSeedForEntity(ChunkCoord coord, int entityIndex);

    /// <summary>
    /// Replace the world seed. Existing cached derived seeds inside subsystems are NOT invalidated;
    /// they refresh on their next re-initialization (e.g. when Planet regenerates). Intended for
    /// console-driven runtime tweaking, not normal gameplay flow.
    /// </summary>
    void SetWorldSeed(int seed);
}
