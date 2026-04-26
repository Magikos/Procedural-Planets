public interface IBiomeRegistry
{
    BiomeResult Resolve(float temperature, float moisture, float elevation);
    int BiomeCount { get; }
}
