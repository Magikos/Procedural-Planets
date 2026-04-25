using System.Collections.Generic;
using UnityEngine;

public static class PoissonDiscSphereSampling
{
    public struct SpawnLocation
    {
        public Vector3 Position;
        public float Elevation;
        public Vector3 Normal;
        public int BiomeIndex;

        public SpawnLocation(Vector3 position, float elevation, Vector3 normal, int biomeIndex)
        {
            Position = position;
            Elevation = elevation;
            Normal = normal;
            BiomeIndex = biomeIndex;
        }
    }

    public static List<SpawnLocation> GeneratePoints(
        float minimumSpacing,
        int maxAttempts,
        ITerrainProvider terrainProvider,
        int seed,
        System.Func<Vector3, int> biomeSelector = null)
    {
        var points = new List<SpawnLocation>();
        var spawnPoints = new List<Vector3>();
        var rand = new System.Random(seed);

        Vector3 startingDirection = RandomUnitVector(rand);
        float unscaled = terrainProvider.EvaluateElevation(startingDirection);
        float initialElevation = terrainProvider.GetScaledElevation(unscaled);
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
                float elev = terrainProvider.GetScaledElevation(terrainProvider.EvaluateElevation(sampleDirection));
                Vector3 candidate = sampleDirection * elev;
                int biome = biomeSelector != null ? biomeSelector(sampleDirection) : 0;

                if (IsValid(candidate, minimumSpacing, points))
                {
                    points.Add(new SpawnLocation(candidate, elev, sampleDirection, biome));
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

    static bool IsValid(Vector3 candidate, float minDist, List<SpawnLocation> points)
    {
        foreach (var pt in points)
        {
            if (Vector3.Distance(candidate, pt.Position) < minDist)
                return false;
        }
        return true;
    }

    static Vector3 RandomUnitVector(System.Random rand)
    {
        float z = 2f * (float)rand.NextDouble() - 1f;
        float t = 2f * Mathf.PI * (float)rand.NextDouble();
        float r = Mathf.Sqrt(1f - z * z);
        return new Vector3(r * Mathf.Cos(t), r * Mathf.Sin(t), z);
    }
}
