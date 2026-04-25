using System.Collections.Generic;
using UnityEngine;
using Shapes;

[ExecuteAlways]
public class TestPoissonDiscSphereDraw : MonoBehaviour
{
    public Planet planet;
    public float minDistance = 10f;
    public int maxAttempts = 30;
    public int seed = 12345;
    public Color[] biomeColors = { Color.blue, Color.green, Color.yellow, Color.gray, Color.white };
    public float drawSize = 2f;
    public bool autoUpdate = true;

    private List<PoissonDiscSphereSampling.SpawnLocation> _points;

    void OnValidate()
    {
        if (autoUpdate)
            Generate();
    }

    public void Generate()
    {
        if (planet == null) return;
        _points = PoissonDiscSphereSampling.GeneratePoints(
            minDistance,
            maxAttempts,
            planet.ShapeGenerator,
            seed,
            BiomeSelector
        );
    }

    int BiomeSelector(Vector3 position)
    {
        // Example: Use y (up) to pick a biome (replace with your own logic)
        float height = position.y;
        if (height < -0.3f) return 0; // ocean
        if (height < 0.1f) return 1; // beach/grass
        if (height < 0.5f) return 2; // forest
        if (height < 0.8f) return 3; // mountain
        return 4; // snow
    }

    void OnDrawGizmos()
    {
        if (_points == null || _points.Count == 0) return;
        foreach (var pt in _points)
        {
            Draw.Color = biomeColors[Mathf.Clamp(pt.biomeIndex, 0, biomeColors.Length - 1)];
            Draw.Sphere(pt.position, drawSize);
        }
    }
}
