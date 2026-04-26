using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Color Settings")]
public class ColorSettings : ScriptableObject
{
    public Material PlanetMaterial;
    public BiomeSettings BiomeSettings;
    public Gradient OceanColorGradient;
}
