public interface ISeedProvider
{
    int WorldSeed { get; }
    int GetSeedForSystem(string systemName);
    int GetSeedForChunk(ChunkCoord coord);
    int GetSeedForEntity(ChunkCoord coord, int entityIndex);

    /// <summary>
    /// A seed for one identified thing - a ScatterId, an EntityId - folded with the world seed. Use this
    /// wherever gameplay needs a random-looking value that two processes must agree on: the same key in the
    /// same world always gives the same seed, and the same key in a different world does not.
    /// </summary>
    int GetSeedForEntity(ulong entityKey);

    /// <summary>
    /// Replace the world seed. Existing cached derived seeds inside subsystems are NOT invalidated;
    /// they refresh on their next re-initialization (e.g. when Planet regenerates). Intended for
    /// console-driven runtime tweaking, not normal gameplay flow.
    /// </summary>
    void SetWorldSeed(int seed);
}
