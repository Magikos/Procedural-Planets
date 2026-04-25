using System.Collections.Generic;
using UnityEngine;

public static class PoissonDiscSampling
{
    public static List<Vector2> GeneratePoints(float radius, Vector2 sampleRegionSize, int maxAttempts = 30, int seed = 0)
    {
        var rand = new System.Random(seed);
        float cellSize = radius / Mathf.Sqrt(2);
        int[,] grid = new int[Mathf.CeilToInt(sampleRegionSize.x / cellSize), Mathf.CeilToInt(sampleRegionSize.y / cellSize)];
        List<Vector2> points = new List<Vector2>();
        List<Vector2> spawnPoints = new List<Vector2>();

        spawnPoints.Add(sampleRegionSize * 0.5f);
        while (spawnPoints.Count > 0)
        {
            int spawnIndex = rand.Next(0, spawnPoints.Count);
            Vector2 spawnCenter = spawnPoints[spawnIndex];
            bool candidateFound = false;

            for (int i = 0; i < maxAttempts; i++)
            {
                float angle = (float)rand.NextDouble() * Mathf.PI * 2;
                Vector2 direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                float distance = radius + (float)rand.NextDouble() * radius;
                Vector2 candidate = spawnCenter + direction * distance;

                if (IsValid(candidate, sampleRegionSize, cellSize, radius, points, grid))
                {
                    points.Add(candidate);
                    spawnPoints.Add(candidate);
                    grid[Mathf.FloorToInt(candidate.x / cellSize), Mathf.FloorToInt(candidate.y / cellSize)] = points.Count;
                    candidateFound = true;
                    break;
                }
            }

            if (!candidateFound)
            {
                spawnPoints.RemoveAt(spawnIndex);
            }
        }

        return points;
    }

    private static bool IsValid(Vector2 candidate, Vector2 sampleRegionSize, float cellSize, float radius, List<Vector2> points, int[,] grid)
    {
        if (candidate.x < 0 || candidate.x >= sampleRegionSize.x || candidate.y < 0 || candidate.y >= sampleRegionSize.y)
            return false;

        int cellX = Mathf.FloorToInt(candidate.x / cellSize);
        int cellY = Mathf.FloorToInt(candidate.y / cellSize);

        int startX = Mathf.Max(0, cellX - 2);
        int endX = Mathf.Min(grid.GetLength(0) - 1, cellX + 2);

        int startY = Mathf.Max(0, cellY - 2);
        int endY = Mathf.Min(grid.GetLength(1) - 1, cellY + 2);

        for (int x = startX; x <= endX; x++)
        {
            for (int y = startY; y <= endY; y++)
            {
                int pointIndex = grid[x, y] - 1;
                if (pointIndex != -1)
                {
                    Vector2 point = points[pointIndex];
                    if (Vector2.Distance(candidate, point) < radius)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}
