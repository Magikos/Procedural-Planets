using UnityEngine;

public static class ScatterQuadtree
{
    const float JitterMargin = 0.12f;

    public static float CellUvWidth(int level) => 1f / (1 << Mathf.Clamp(level, 0, ScatterId.MaxLevel));

    // Fixed per generated world from the canonical face span (2 * planetWorldRadius), independent
    // of the query origin, so a prototype's cells never move as the camera moves. Ceil so the chosen
    // cell is never LARGER than the target spacing (rounding down would be permanently under-dense —
    // one candidate per cell cannot recover missing density). Throws rather than clamp: the level is
    // baked into the persistence key.
    public static int LevelForSpacing(float planetWorldRadius, float spacingMeters)
    {
        float span = 2f * Mathf.Max(planetWorldRadius, 1f);
        float ratio = Mathf.Max(1f, span / Mathf.Max(spacingMeters, 0.05f));
        int raw = Mathf.Max(0, Mathf.CeilToInt(Mathf.Log(ratio, 2f)));
        if (raw > ScatterId.MaxLevel)
            throw new System.InvalidOperationException(
                $"Scatter spacing {spacingMeters} m needs quadtree level {raw} > max {ScatterId.MaxLevel} " +
                $"on world radius {planetWorldRadius} m. Increase spacing.");
        return raw;
    }

    // Candidate jitter is slot-seeded so two same-level prototypes never share a point.
    public static Vector2 CandidateUv(int x, int y, float cellUvWidth, uint slotSeed)
    {
        float jx = Mathf.Lerp(JitterMargin, 1f - JitterMargin, ScatterHash.To01(ScatterHash.Slot(slotSeed, 101)));
        float jy = Mathf.Lerp(JitterMargin, 1f - JitterMargin, ScatterHash.To01(ScatterHash.Slot(slotSeed, 202)));
        return new Vector2((x + jx) * cellUvWidth, (y + jy) * cellUvWidth);
    }

    // Cube-to-sphere area probability, IDENTICAL to grass CubeFaceAreaKeep (GrassNearFieldPlace.compute)
    // so the SP3 GPU mirror matches exactly: with signedUv = uv*2-1, distortion = (1 + |signedUv|^2)^-1.5.
    // The centre-cell ratio scales for the fixed cell being larger/smaller than the target spacing.
    public static float AreaKeep(Vector2 faceUv, float cellUvWidth, float spacingMeters, float planetWorldRadius)
    {
        Vector2 s = faceUv * 2f - Vector2.one;
        float denom = 1f + Vector2.Dot(s, s);
        float distortion = 1f / Mathf.Max(Mathf.Sqrt(denom * denom * denom), 1e-6f);
        float centerCellWorld = 2f * planetWorldRadius * cellUvWidth;
        float ratio = centerCellWorld / Mathf.Max(spacingMeters, 0.05f);
        return Mathf.Clamp01(ratio * ratio * distortion);
    }
}
