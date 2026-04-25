using UnityEngine;

public interface IColorProvider
{
    void Initialize(ColorSettings settings);
    void UpdateElevation(MinMax elevationMinMax);
    void UpdateColors();
    Texture2D BiomeTexture { get; }
}
