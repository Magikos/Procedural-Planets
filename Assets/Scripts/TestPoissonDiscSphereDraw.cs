using System.Collections.Generic;
using UnityEngine;

public class TestPoissonDiscSphereDraw : MonoBehaviour
{
    public Planet planet;
    public float minDistance = 10f;
    public int maxAttempts = 30;
    public int seed = 12345;
    public Color[] biomeColors = { Color.blue, Color.green, Color.yellow, Color.gray, Color.white };
    public float drawSize = 0.5f;

    List<PoissonDiscSphereSampling.SpawnLocation> _points;

    public void Generate()
    {
        if (planet == null || planet.PlanetSettingsAsset == null) return;

        planet.GeneratePlanetAsync();
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
        float height = position.y;
        if (height < -0.3f) return 0;
        if (height < 0.1f) return 1;
        if (height < 0.5f) return 2;
        if (height < 0.8f) return 3;
        return 4;
    }

    void OnDrawGizmos()
    {
        if (_points == null || _points.Count == 0) return;
        foreach (var pt in _points)
        {
            Gizmos.color = biomeColors[Mathf.Clamp(pt.BiomeIndex, 0, biomeColors.Length - 1)];
            Gizmos.DrawSphere(pt.Position, drawSize);
        }
    }
}
