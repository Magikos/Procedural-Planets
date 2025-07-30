using UnityEngine;
using UnityEditor;

public static class PlanetPresets
{
    [MenuItem("Tools/Planet Presets/Earth-like")]
    public static void CreateEarthLike()
    {
        var planet = Selection.activeGameObject?.GetComponent<Planet>();
        if (planet == null)
        {
            Debug.LogWarning("Select a Planet GameObject first!");
            return;
        }

        planet.resolution = 50;

        if (planet.shapeSettings != null)
        {
            planet.shapeSettings.planetRadius = 1f;
            // You could set up typical noise layers here
        }

        if (planet.lodSettings != null)
        {
            planet.lodSettings.maxLOD = 5;
            // Typical LOD distances for Earth-like planet
        }

        planet.GeneratePlanet();
        Debug.Log("Applied Earth-like preset to " + planet.name);
    }

    [MenuItem("Tools/Planet Presets/Rocky")]
    public static void CreateRocky()
    {
        var planet = Selection.activeGameObject?.GetComponent<Planet>();
        if (planet == null)
        {
            Debug.LogWarning("Select a Planet GameObject first!");
            return;
        }

        planet.resolution = 75;

        if (planet.shapeSettings != null)
        {
            planet.shapeSettings.planetRadius = 0.8f;
            // Add more rocky characteristics
        }

        if (planet.lodSettings != null)
        {
            planet.lodSettings.maxLOD = 6; // Higher detail for rocky surfaces
        }

        planet.GeneratePlanet();
        Debug.Log("Applied Rocky preset to " + planet.name);
    }

    [MenuItem("Tools/Planet Presets/Gas Giant")]
    public static void CreateGasGiant()
    {
        var planet = Selection.activeGameObject?.GetComponent<Planet>();
        if (planet == null)
        {
            Debug.LogWarning("Select a Planet GameObject first!");
            return;
        }

        planet.resolution = 30; // Lower res for smoother surface

        if (planet.shapeSettings != null)
        {
            planet.shapeSettings.planetRadius = 2f;
            // Minimal noise for smooth gas giant look
        }

        if (planet.lodSettings != null)
        {
            planet.lodSettings.maxLOD = 4; // Lower LOD for smooth gas giants
        }

        planet.GeneratePlanet();
        Debug.Log("Applied Gas Giant preset to " + planet.name);
    }

    // Validate menu items - only show when a Planet is selected
    [MenuItem("Tools/Planet Presets/Earth-like", true)]
    [MenuItem("Tools/Planet Presets/Rocky", true)]
    [MenuItem("Tools/Planet Presets/Gas Giant", true)]
    public static bool ValidatePresetMenus()
    {
        return Selection.activeGameObject?.GetComponent<Planet>() != null;
    }
}
