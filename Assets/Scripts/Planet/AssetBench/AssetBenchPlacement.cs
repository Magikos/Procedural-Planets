using UnityEngine;

/// <summary>One bench slot: a candidate and its reference, as unit directions from the planet centre.</summary>
public readonly struct BenchSlot
{
    public readonly Vector3 CandidateDir;
    public readonly Vector3 ReferenceDir;

    public BenchSlot(Vector3 candidateDir, Vector3 referenceDir)
    {
        CandidateDir = candidateDir;
        ReferenceDir = referenceDir;
    }
}

/// <summary>
/// Bench layout maths. Offsets are angular (radians of arc) rather than world distances, so a layout is
/// independent of planet radius and stays evenly spaced as it wraps around the curve.
/// </summary>
public static class AssetBenchPlacement
{
    public static void BuildTangentBasis(Vector3 radialUp, out Vector3 tangentRight, out Vector3 tangentForward)
    {
        Vector3 up = radialUp.normalized;

        // Cross against whichever world axis is least parallel to up, or the basis degenerates at the poles.
        Vector3 seed = Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right;

        tangentRight = Vector3.Cross(seed, up).normalized;
        tangentForward = Vector3.Cross(up, tangentRight).normalized;
    }

    public static BenchSlot SlotDirection(
        Vector3 originDir, int index, float pairStrideRad, float pairGapRad, float forwardOffsetRad)
    {
        Vector3 origin = originDir.normalized;
        BuildTangentBasis(origin, out Vector3 right, out Vector3 forward);

        float alongArc = index * pairStrideRad;
        float half = pairGapRad * 0.5f;

        Vector3 candidate = Offset(origin, right, forward, alongArc - half, forwardOffsetRad);
        Vector3 reference = Offset(origin, right, forward, alongArc + half, forwardOffsetRad);
        return new BenchSlot(candidate, reference);
    }

    /// <summary>A viewing direction set back from the pair, centred so neither member is favoured by proximity.</summary>
    public static Vector3 ViewpointDir(BenchSlot slot, float backOffRad)
    {
        Vector3 mid = (slot.CandidateDir + slot.ReferenceDir).normalized;
        BuildTangentBasis(mid, out Vector3 right, out Vector3 forward);
        return Offset(mid, right, forward, 0f, -backOffRad);
    }

    /// <summary>
    /// Rotate <paramref name="dir"/> across the tangent plane by angular amounts.
    /// With an orthonormal (right, forward, up) basis, rotating about <c>forward</c> carries up toward right,
    /// and rotating about <c>right</c> carries it toward -forward — hence the negated forward term.
    /// </summary>
    static Vector3 Offset(Vector3 dir, Vector3 right, Vector3 forward, float rightRad, float forwardRad)
    {
        Quaternion towardRight = Quaternion.AngleAxis(rightRad * Mathf.Rad2Deg, forward);
        Quaternion towardForward = Quaternion.AngleAxis(-forwardRad * Mathf.Rad2Deg, right);
        return (towardForward * towardRight * dir).normalized;
    }
}
