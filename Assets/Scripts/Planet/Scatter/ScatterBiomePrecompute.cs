using Unity.Collections;
using UnityEngine;

// Managed biome precompute for the Burst gather. The gather quantizes biome to a coarse
// (face, sampleLevel = min(level, 9), cell) memo (ScatterField.TryGatherCandidate); the Voronoi biome
// field + climate can't run in a Burst job, so evaluate EvaluateBiome for exactly the coarse cells a
// batch of pairs will query — the SAME cell centers the per-candidate memo hits — and hand the result to
// the job as a lookup. Because the cell centers and the ground elevation used for `celev` are identical
// to the managed path, the job reads byte-identical biome membership.
public static class ScatterBiomePrecompute
{
    const int BiomeSampleLevel = 9;

    // Worst-case distinct-key count for `pairs`, to size the hashmap before Build.
    public static int CountCells(NativeArray<ScatterPairInput> pairs, int pairCount, int tileLevel)
    {
        int total = 0;
        for (int i = 0; i < pairCount; i++)
        {
            int level = pairs[i].Level;
            int sampleLevel = level < BiomeSampleLevel ? level : BiomeSampleLevel;
            int sh = sampleLevel - tileLevel;
            int cnt = 1 << sh;
            total += cnt * cnt;
        }
        return total;
    }

    public static void Build(NativeArray<ScatterPairInput> pairs, int pairCount, int tileLevel,
        ISurfaceGroundSampler ground, IBiomeProvider biome, float baseRadiusLocal,
        NativeParallelHashMap<long, ScatterBiomeSample> map)
    {
        for (int i = 0; i < pairCount; i++)
        {
            ScatterPairInput pair = pairs[i];
            int level = pair.Level;
            int sampleLevel = level < BiomeSampleLevel ? level : BiomeSampleLevel;
            int sh = sampleLevel - tileLevel;
            int cnt = 1 << sh;
            int xb0 = pair.TileX << sh, yb0 = pair.TileY << sh;
            float cellUvB = ScatterQuadtree.CellUvWidth(sampleLevel);
            for (int dy = 0; dy < cnt; dy++)
            for (int dx = 0; dx < cnt; dx++)
            {
                int xb = xb0 + dx, yb = yb0 + dy;
                long key = ((long)pair.Face << 58) | ((long)sampleLevel << 50) | ((long)xb << 25) | (long)yb;
                if (map.ContainsKey(key)) continue;
                Vector2 cuv = new Vector2((xb + 0.5f) * cellUvB, (yb + 0.5f) * cellUvB);
                Vector3 cdir = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(pair.Face, cuv);
                float celev = ground.TrySampleRadius(cdir, out float crad) ? crad / baseRadiusLocal - 1f : -1f;
                BiomeResult b = biome.EvaluateBiome(cdir, celev);
                map.Add(key, new ScatterBiomeSample
                {
                    PrimaryBiome = (int)b.PrimaryBiome,
                    SecondaryBiome = (int)b.SecondaryBiome,
                    BlendWeight = b.BlendWeight,
                });
            }
        }
    }
}
