using UnityEngine;

public class TerrainFace
{
    Mesh mesh;
    public int resolution;
    Vector3 localUp;
    Vector3 axisA;
    Vector3 axisB;
    ShapeSettings shapeSettings;

    public float elevationMin { get; private set; }
    public float elevationMax { get; private set; }

    public TerrainFace(Mesh mesh, int resolution, Vector3 localUp, ShapeSettings shapeSettings)
    {
        this.mesh = mesh;
        this.resolution = resolution;
        this.localUp = localUp;
        this.shapeSettings = shapeSettings;

        axisA = new Vector3(localUp.y, localUp.z, localUp.x);
        axisB = Vector3.Cross(localUp, axisA);
    }

    public void ConstructMesh()
    {
        if (shapeSettings == null || shapeSettings.noiseLayers == null || shapeSettings.noiseLayers.Length == 0)
        {
            Debug.LogError("ShapeSettings or noise layers are not set up correctly.");
            return;
        }

        Vector3[] vertices = new Vector3[resolution * resolution];
        int[] triangles = new int[(resolution - 1) * (resolution - 1) * 6];
        int triIndex = 0;

        elevationMin = float.MaxValue;
        elevationMax = float.MinValue;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i = x + y * resolution;
                Vector2 percent = new Vector2(x, y) / (resolution - 1);
                Vector3 pointOnUnitCube = localUp + (percent.x - 0.5f) * 2 * axisA + (percent.y - 0.5f) * 2 * axisB;
                Vector3 pointOnUnitSphere = pointOnUnitCube.normalized;

                float unscaledElevation = 0;
                float firstLayerValue = 0;

                if (shapeSettings.noiseLayers.Length > 0 && shapeSettings.noiseLayers[0].enabled)
                {
                    firstLayerValue = Noise.Get3DNoise(pointOnUnitSphere, shapeSettings.noiseLayers[0].noiseSettings);
                    unscaledElevation = firstLayerValue;
                }

                for (int layer = 1; layer < shapeSettings.noiseLayers.Length; layer++)
                {
                    var noiseLayer = shapeSettings.noiseLayers[layer];
                    if (noiseLayer.enabled)
                    {
                        float mask = noiseLayer.useFirstLayerAsMask ? firstLayerValue : 1;
                        unscaledElevation += Noise.Get3DNoise(pointOnUnitSphere, noiseLayer.noiseSettings) * mask;
                    }
                }

                float elevation = shapeSettings.planetRadius + unscaledElevation;
                vertices[i] = pointOnUnitSphere * elevation;

                if (elevation < elevationMin) elevationMin = elevation;
                if (elevation > elevationMax) elevationMax = elevation;

                if (x != resolution - 1 && y != resolution - 1)
                {
                    triangles[triIndex] = i;
                    triangles[triIndex + 1] = i + resolution + 1;
                    triangles[triIndex + 2] = i + resolution;

                    triangles[triIndex + 3] = i;
                    triangles[triIndex + 4] = i + 1;
                    triangles[triIndex + 5] = i + resolution + 1;
                    triIndex += 6;
                }
            }
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        // Adjust min/max to be unscaled for coloring later
        elevationMin -= shapeSettings.planetRadius;
        elevationMax -= shapeSettings.planetRadius;
    }

    public void UpdateColors(Color[] colors, int startIndex, float minElev, float maxElev, ColorSettings settings)
    {
        var biomeSettings = settings.biomeColorSettings;
        int index = startIndex;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i = x + y * resolution;
                Vector2 percent = new Vector2(x, y) / (resolution - 1);
                Vector3 pointOnUnitCube = localUp + (percent.x - 0.5f) * 2 * axisA + (percent.y - 0.5f) * 2 * axisB;
                Vector3 pointOnUnitSphere = pointOnUnitCube.normalized;

                float unscaledElevation = 0;
                float firstLayerValue = 0;

                if (shapeSettings.noiseLayers.Length > 0 && shapeSettings.noiseLayers[0].enabled)
                {
                    firstLayerValue = Noise.Get3DNoise(pointOnUnitSphere, shapeSettings.noiseLayers[0].noiseSettings);
                    unscaledElevation = firstLayerValue;
                }

                for (int layer = 1; layer < shapeSettings.noiseLayers.Length; layer++)
                {
                    var noiseLayer = shapeSettings.noiseLayers[layer];
                    if (noiseLayer.enabled)
                    {
                        float mask = noiseLayer.useFirstLayerAsMask ? firstLayerValue : 1;
                        unscaledElevation += Noise.Get3DNoise(pointOnUnitSphere, noiseLayer.noiseSettings) * mask;
                    }
                }

                float heightPercent = (maxElev - minElev) > 0.001f ? (unscaledElevation - minElev) / (maxElev - minElev) : 0;
                if (index == startIndex) // Log first vertex for debugging
                {
                    Debug.Log($"Face {localUp}: heightPercent={heightPercent}, unscaledElevation={unscaledElevation}, minElev={minElev}, maxElev={maxElev}");
                }

                float biomeIndex = 0;
                int numBiomes = biomeSettings.biomes.Length;
                float blendRange = biomeSettings.blendAmount / 2f + 0.001f;

                for (int bi = 0; bi < numBiomes; bi++)
                {
                    var biome = biomeSettings.biomes[bi];
                    float dst = heightPercent - biome.startHeight;
                    float weight = Mathf.InverseLerp(-blendRange, blendRange, dst);
                    biomeIndex += weight * bi;
                }

                float noiseValue = 0;
                if (biomeSettings.noise != null)
                {
                    noiseValue = Noise.Get3DNoise(pointOnUnitSphere, biomeSettings.noise);
                    noiseValue = (noiseValue - 0.5f) * biomeSettings.noiseStrength + biomeSettings.noiseOffset;
                }

                biomeIndex = Mathf.Clamp(biomeIndex + noiseValue, 0, numBiomes - 1);

                int biome1 = Mathf.FloorToInt(biomeIndex);
                int biome2 = Mathf.Min(biome1 + 1, numBiomes - 1);
                float blend = biomeIndex - biome1;

                Color color1 = biomeSettings.biomes[biome1].gradient.Evaluate(heightPercent);
                Color tint1 = biomeSettings.biomes[biome1].tint;
                color1 = Color.Lerp(color1, tint1, biomeSettings.biomes[biome1].tintPercent);

                Color color2 = biomeSettings.biomes[biome2].gradient.Evaluate(heightPercent);
                Color tint2 = biomeSettings.biomes[biome2].tint;
                color2 = Color.Lerp(color2, tint2, biomeSettings.biomes[biome2].tintPercent);

                colors[index] = Color.Lerp(color1, color2, blend);
                index++;
            }
        }
    }
}