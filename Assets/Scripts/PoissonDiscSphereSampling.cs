using System.Collections.Generic;
using UnityEngine;

public static class PoissonDiscSphereSampling
{
    public struct SpawnLocation
    {
        public Vector3 position;
        public float elevation;
        public Vector3 normal;
        public int biomeIndex;

        public SpawnLocation(Vector3 position, float elevation, Vector3 normal, int biomeIndex)
        {
            this.position = position;
            this.elevation = elevation;
            this.normal = normal;
            this.biomeIndex = biomeIndex;
        }
    }

    public static List<SpawnLocation> GeneratePoints(
        float minimumSpacing,
        int maxAttempts,
        ShapeGenerator shapeGenerator,
        int seed,
        System.Func<Vector3, int> biomeSelector = null)
    {
        var points = new List<SpawnLocation>();
        var spawnPoints = new List<Vector3>();
        var rand = new System.Random(seed);

        // Start with a random point on the sphere
        Vector3 startingDirection = RandomUnitVector(rand);
        float initialElevation = shapeGenerator.GetScaledElevation(shapeGenerator.CalculateUnscaledElevation(startingDirection));
        Vector3 startingSamplePosition = startingDirection * initialElevation;
        int startingBiomeId = biomeSelector != null ? biomeSelector(startingDirection) : 0;

        points.Add(new SpawnLocation(startingSamplePosition, initialElevation, startingDirection, startingBiomeId));
        spawnPoints.Add(startingSamplePosition);

        while (spawnPoints.Count > 0)
        {
            int spawnIndex = rand.Next(spawnPoints.Count);
            bool candidateAccepted = false;

            for (int i = 0; i < maxAttempts; i++)
            {
                Vector3 sampleDirection = RandomUnitVector(rand);
                float elevation = shapeGenerator.GetScaledElevation(shapeGenerator.CalculateUnscaledElevation(sampleDirection));
                Vector3 candidate = sampleDirection * elevation;
                int biome = biomeSelector != null ? biomeSelector(sampleDirection) : 0;

                if (IsValid(candidate, minimumSpacing, points))
                {
                    points.Add(new SpawnLocation(candidate, elevation, sampleDirection, biome));
                    spawnPoints.Add(candidate);
                    candidateAccepted = true;
                    break;
                }
            }

            if (!candidateAccepted)
            {
                spawnPoints.RemoveAt(spawnIndex);
            }
        }

        return points;
    }

    private static bool IsValid(Vector3 candidate, float minDist, List<SpawnLocation> points)
    {
        foreach (var pt in points)
        {
            if (Vector3.Distance(candidate, pt.position) < minDist)
                return false;
        }
        return true;
    }

    private static Vector3 RandomUnitVector(System.Random rand)
    {
        // Uniformly sample a point on a sphere
        float z = 2f * (float)rand.NextDouble() - 1f;
        float t = 2f * Mathf.PI * (float)rand.NextDouble();
        float r = Mathf.Sqrt(1f - z * z);
        return new Vector3(r * Mathf.Cos(t), r * Mathf.Sin(t), z);
    }
}
