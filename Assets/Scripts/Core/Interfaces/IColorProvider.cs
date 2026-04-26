using UnityEngine;

public interface IColorProvider
{
    void Initialize();
    void UpdateElevation(float min, float max);
    void UpdateColors();
}
