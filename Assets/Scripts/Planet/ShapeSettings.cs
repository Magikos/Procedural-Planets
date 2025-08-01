using System.Runtime.InteropServices;
using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Shape Settings")]
public class ShapeSettings : ScriptableObject
{
    [Range(1, 100)]
    public float PlanetRadius = 1;
    public NoiseLayer[] NoiseLayers;

    [System.Serializable]
    public class NoiseLayer
    {
        public bool UseFirstLayerAsMask = false;
        public bool Enabled = true;
        public NoiseSettings NoiseSettings;
    }
}