using UnityEngine;
using Shapes;

[System.Serializable]
public class PlanetDebugSettings
{
    [Header("Chunk Bounds")]
    public bool showChunkBounds = false;
    public Color boundsColor = Color.yellow;

    [Header("Elevation Visualization")]
    public bool showElevationGradient = false;
    public float elevationLineLength = 10f;

    [Header("Color Debugging")]
    public bool showColorSamples = false;
    public int colorSampleCount = 20;
    public float colorSampleSize = 5f;

    [Header("LOD Visualization")]
    public bool showLODLevels = false;
    public Color[] lodColors = new Color[] { Color.red, Color.orange, Color.yellow, Color.green, Color.blue, Color.purple };
}

public class PlanetDebugVisualizer : ImmediateModeShapeDrawer
{
    public PlanetDebugSettings settings;
    public Planet targetPlanet;

    public override void DrawShapes(Camera cam)
    {
        if (targetPlanet == null || settings == null) return;

        using (Draw.Command(cam))
        {
            Draw.LineGeometry = LineGeometry.Volumetric3D;
            Draw.ThicknessSpace = ThicknessSpace.Pixels;
            Draw.Thickness = 2f;

            if (settings.showChunkBounds)
            {
                DrawChunkBounds();
            }

            if (settings.showElevationGradient)
            {
                DrawElevationGradient();
            }

            if (settings.showColorSamples)
            {
                DrawColorSamples();
            }

            if (settings.showLODLevels)
            {
                DrawLODLevels();
            }
        }
    }

    void DrawChunkBounds()
    {
        Draw.Color = settings.boundsColor;

        // Draw planet bounds as wireframe sphere using multiple rings
        Vector3 center = targetPlanet.transform.position;
        float radius = targetPlanet.shapeSettings?.planetRadius ?? 1000f;

        // Draw multiple rings to create wireframe sphere effect
        Draw.Ring(center, Vector3.up, radius, 2f);
        Draw.Ring(center, Vector3.right, radius, 2f);
        Draw.Ring(center, Vector3.forward, radius, 2f);

        // Add some diagonal rings for better sphere visualization
        Vector3 diagonal1 = (Vector3.up + Vector3.right).normalized;
        Vector3 diagonal2 = (Vector3.up + Vector3.forward).normalized;
        Draw.Ring(center, diagonal1, radius, 1f);
        Draw.Ring(center, diagonal2, radius, 1f);

        // Draw actual terrain chunk bounds if we can access them
        if (targetPlanet != null)
        {
            // Get all mesh renderers (terrain chunks) under the planet
            MeshRenderer[] chunks = targetPlanet.GetComponentsInChildren<MeshRenderer>();

            Draw.Color = settings.boundsColor * 0.7f; // Slightly dimmer for individual chunks
            foreach (var chunk in chunks)
            {
                if (chunk.gameObject.activeInHierarchy)
                {
                    Bounds bounds = chunk.bounds;
                    // Draw chunk bounds as wireframe box
                    DrawWireframeCube(bounds.center, bounds.size);
                }
            }
        }
    }

    void DrawElevationGradient()
    {
        if (targetPlanet.shapeSettings == null) return;

        Vector3 center = targetPlanet.transform.position;
        float radius = targetPlanet.shapeSettings.planetRadius;

        // Draw elevation sample lines radiating from planet center
        int sampleCount = 24; // More samples for better visualization

        for (int i = 0; i < sampleCount; i++)
        {
            // Create samples in a sphere around the planet
            float theta = (i / (float)sampleCount) * Mathf.PI * 2f;
            float phi = Mathf.Acos(1f - 2f * (i % 8) / 8f); // Distribute vertically

            Vector3 direction = new Vector3(
                Mathf.Sin(phi) * Mathf.Cos(theta),
                Mathf.Cos(phi),
                Mathf.Sin(phi) * Mathf.Sin(theta)
            );

            // Sample actual elevation using the shape generator if available
            float actualElevation = radius;
            if (targetPlanet.shapeGenerator != null)
            {
                float unscaledElevation = targetPlanet.shapeGenerator.CalculateUnscaledElevation(direction);
                actualElevation = radius + unscaledElevation;
            }

            // Calculate height percentage for coloring
            float baseRadius = radius * 0.95f; // Assume minimum elevation
            float maxRadius = radius * 1.1f;   // Assume maximum elevation
            float heightPercent = Mathf.InverseLerp(baseRadius, maxRadius, actualElevation);

            // Color code by elevation
            Draw.Color = Color.Lerp(Color.blue, Color.white, heightPercent);

            // Draw line from planet center to surface point
            Vector3 surfacePoint = direction * actualElevation;
            Draw.Line(center, center + surfacePoint.normalized * (actualElevation + settings.elevationLineLength));

            // Draw a small sphere at the surface point
            Draw.Sphere(center + surfacePoint, 2f);
        }
    }

    void DrawColorSamples()
    {
        if (targetPlanet.colorSettings?.biomeColorSettings?.biomes == null) return;

        Vector3 center = targetPlanet.transform.position;
        float radius = targetPlanet.shapeSettings?.planetRadius ?? 1000f;

        // Draw color samples as a vertical column showing biome transitions
        float startHeight = radius + 100f;
        float endHeight = radius + 500f;

        for (int i = 0; i < settings.colorSampleCount; i++)
        {
            float t = i / (float)(settings.colorSampleCount - 1);
            float currentHeight = Mathf.Lerp(startHeight, endHeight, t);
            Vector3 samplePos = center + Vector3.up * currentHeight;

            // Get color for this height percentage using the actual color generator
            Color sampleColor = GetColorForHeight(t);
            Draw.Color = sampleColor;
            Draw.Sphere(samplePos, settings.colorSampleSize);

            // Draw a line connecting the samples to show the gradient
            if (i > 0)
            {
                float prevHeight = Mathf.Lerp(startHeight, endHeight, (i - 1) / (float)(settings.colorSampleCount - 1));
                Vector3 prevPos = center + Vector3.up * prevHeight;

                Draw.Color = Color.Lerp(GetColorForHeight((i - 1) / (float)(settings.colorSampleCount - 1)), sampleColor, 0.5f);
                Draw.Line(prevPos, samplePos);
            }
        }

        // Draw additional samples around the planet at different positions
        int radialSamples = 8;
        float sampleRadius = radius + 200f;

        for (int i = 0; i < radialSamples; i++)
        {
            float angle = (i / (float)radialSamples) * Mathf.PI * 2f;
            Vector3 radialPos = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * sampleRadius;

            // Sample at medium height (50% up the biome range)
            Color radialColor = GetColorForHeight(0.5f);
            Draw.Color = radialColor;
            Draw.Sphere(radialPos, settings.colorSampleSize * 0.7f);

            // Connect to center with a thin line
            Draw.Color = radialColor * 0.5f;
            Draw.Line(center + Vector3.up * (radius + 150f), radialPos);
        }
    }

    void DrawLODLevels()
    {
        Vector3 center = targetPlanet.transform.position;
        float radius = targetPlanet.shapeSettings?.planetRadius ?? 1000f;

        // Draw LOD distance rings based on the planet's LOD settings
        if (targetPlanet.lodSettings != null)
        {
            var lodDistances = targetPlanet.lodSettings.lodDistances;

            for (int i = 0; i < lodDistances.Length && i < settings.lodColors.Length; i++)
            {
                Draw.Color = settings.lodColors[i];

                // Convert normalized LOD distance to actual world distance
                float actualDistance = radius * lodDistances[i];

                // Draw rings in multiple orientations for better 3D visibility
                Draw.Ring(center, Vector3.up, actualDistance, 3f);
                Draw.Ring(center, Vector3.right, actualDistance, 2f);
                Draw.Ring(center, Vector3.forward, actualDistance, 2f);

                // Draw some radial lines to show LOD boundaries more clearly
                int spokes = 8;
                for (int spoke = 0; spoke < spokes; spoke++)
                {
                    float spokeAngle = (spoke / (float)spokes) * Mathf.PI * 2f;
                    Vector3 spokeDir = new Vector3(Mathf.Cos(spokeAngle), 0, Mathf.Sin(spokeAngle));

                    Vector3 innerPoint = center + spokeDir * (i > 0 ? radius * lodDistances[i - 1] : radius);
                    Vector3 outerPoint = center + spokeDir * actualDistance;

                    Draw.Color = settings.lodColors[i] * 0.6f;
                    Draw.Line(innerPoint, outerPoint);
                }
            }
        }
        else
        {
            // Fallback: draw some default LOD rings
            for (int i = 0; i < settings.lodColors.Length && i < 6; i++)
            {
                Draw.Color = settings.lodColors[i];
                float lodRadius = radius + (i + 1) * 200f;
                Draw.Ring(center, Vector3.up, lodRadius, 2f);
            }
        }

        // Draw viewer position indicator if we can find a camera
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            Vector3 viewerPos = mainCam.transform.position;
            Draw.Color = Color.white;
            Draw.Sphere(viewerPos, 10f);

            // Draw line from viewer to planet center
            Draw.Color = Color.white * 0.5f;
            Draw.Line(viewerPos, center);

            // Show current distance
            float currentDistance = Vector3.Distance(viewerPos, center);
            Draw.Color = Color.yellow;
            Draw.Ring(center, Vector3.up, currentDistance, 4f);
        }
    }

    Color GetColorForHeight(float heightPercent)
    {
        if (targetPlanet.colorSettings?.biomeColorSettings?.biomes == null)
        {
            // Fallback gradient
            if (heightPercent < 0.3f) return Color.blue;
            else if (heightPercent < 0.7f) return Color.green;
            else return Color.gray;
        }

        // Use the planet's color generator logic
        var biomes = targetPlanet.colorSettings.biomeColorSettings.biomes;
        if (biomes.Length > 0)
        {
            // Simplified color calculation
            for (int i = biomes.Length - 1; i >= 0; i--)
            {
                if (heightPercent >= biomes[i].startHeight)
                {
                    return biomes[i].gradient.Evaluate(heightPercent);
                }
            }
            return biomes[0].gradient.Evaluate(heightPercent);
        }

        return Color.white;
    }

    // Helper method to draw wireframe cube using Shapes
    void DrawWireframeCube(Vector3 center, Vector3 size)
    {
        Vector3 halfSize = size * 0.5f;

        // Define the 8 corners of the cube
        Vector3[] corners = new Vector3[8]
        {
            center + new Vector3(-halfSize.x, -halfSize.y, -halfSize.z), // Bottom-left-back
            center + new Vector3( halfSize.x, -halfSize.y, -halfSize.z), // Bottom-right-back
            center + new Vector3( halfSize.x,  halfSize.y, -halfSize.z), // Top-right-back
            center + new Vector3(-halfSize.x,  halfSize.y, -halfSize.z), // Top-left-back
            center + new Vector3(-halfSize.x, -halfSize.y,  halfSize.z), // Bottom-left-front
            center + new Vector3( halfSize.x, -halfSize.y,  halfSize.z), // Bottom-right-front
            center + new Vector3( halfSize.x,  halfSize.y,  halfSize.z), // Top-right-front
            center + new Vector3(-halfSize.x,  halfSize.y,  halfSize.z)  // Top-left-front
        };

        // Draw the 12 edges of the cube
        // Back face
        Draw.Line(corners[0], corners[1]);
        Draw.Line(corners[1], corners[2]);
        Draw.Line(corners[2], corners[3]);
        Draw.Line(corners[3], corners[0]);

        // Front face
        Draw.Line(corners[4], corners[5]);
        Draw.Line(corners[5], corners[6]);
        Draw.Line(corners[6], corners[7]);
        Draw.Line(corners[7], corners[4]);

        // Connecting edges
        Draw.Line(corners[0], corners[4]);
        Draw.Line(corners[1], corners[5]);
        Draw.Line(corners[2], corners[6]);
        Draw.Line(corners[3], corners[7]);
    }
}

// Component to attach to Planet for debugging
[RequireComponent(typeof(Planet))]
public class PlanetDebugComponent : MonoBehaviour
{
    [Header("Debug Visualization")]
    public PlanetDebugSettings debugSettings = new PlanetDebugSettings();

    [Header("Controls")]
    [Space]
    public KeyCode toggleBoundsKey = KeyCode.B;
    public KeyCode toggleElevationKey = KeyCode.E;
    public KeyCode toggleColorsKey = KeyCode.C;
    public KeyCode toggleLODKey = KeyCode.L;

    private PlanetDebugVisualizer visualizer;
    private Planet planet;

    void Start()
    {
        planet = GetComponent<Planet>();

        // Create debug visualizer
        GameObject debugObj = new GameObject("Planet Debug Visualizer");
        debugObj.transform.SetParent(transform);

        visualizer = debugObj.AddComponent<PlanetDebugVisualizer>();
        visualizer.settings = debugSettings;
        visualizer.targetPlanet = planet;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleBoundsKey))
        {
            debugSettings.showChunkBounds = !debugSettings.showChunkBounds;
            Debug.Log($"Planet chunk bounds: {(debugSettings.showChunkBounds ? "ON" : "OFF")}");
        }

        if (Input.GetKeyDown(toggleElevationKey))
        {
            debugSettings.showElevationGradient = !debugSettings.showElevationGradient;
            Debug.Log($"Planet elevation gradient: {(debugSettings.showElevationGradient ? "ON" : "OFF")}");
        }

        if (Input.GetKeyDown(toggleColorsKey))
        {
            debugSettings.showColorSamples = !debugSettings.showColorSamples;
            Debug.Log($"Planet color samples: {(debugSettings.showColorSamples ? "ON" : "OFF")}");
        }

        if (Input.GetKeyDown(toggleLODKey))
        {
            debugSettings.showLODLevels = !debugSettings.showLODLevels;
            Debug.Log($"Planet LOD levels: {(debugSettings.showLODLevels ? "ON" : "OFF")}");
        }
    }

    void OnValidate()
    {
        if (visualizer != null)
        {
            visualizer.settings = debugSettings;
        }
    }
}
