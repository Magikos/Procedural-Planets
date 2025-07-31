using UnityEngine;

[System.Serializable]
public class NoiseSettings
{
    public enum FilterType { Simple, Rigid }
    public FilterType Filter = FilterType.Simple;

    public float Strength = 1f;
    [Range(1, 8)]
    public int Layers = 1;
    public float BaseRoughness = 1f;
    public float Roughness = 2f;
    public float Persistence = 0.5f;
    public Vector3 Center = Vector3.zero;
    public float MinValue = 0f;
}
