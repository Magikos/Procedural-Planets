using UnityEngine;

// Heightfield ground sampler backed by the analytic shape function every terrain mesh is built
// from. LOD-independent and deterministic (same radius for a direction regardless of what is
// streamed), and pure — no Transform, no chunk lookup — so a gather using it can run off the main
// thread. This is the fast path for pure-heightfield terrain; the marching-cubes/SDF path will be a
// separate ISurfaceGroundSampler that raymarches a density field behind the same seam.
public sealed class AnalyticGroundSampler : ISurfaceGroundSampler
{
    // Tangent offset used to estimate the surface normal, in metres of ground distance. Small
    // enough to read local slope, large enough not to alias high-frequency noise.
    const float NormalProbeMeters = 2f;

    readonly ShapeGenerator _shape;

    public AnalyticGroundSampler(ShapeGenerator shape)
    {
        _shape = shape ?? throw new System.ArgumentNullException(nameof(shape));
    }

    // Reference composition: radius (1 elevation sample) + normal (2 more). TrySampleRadius +
    // SampleNormalAt are the two stages the scatter hot path uses to skip the normal for rejected
    // candidates; keeping this as their composition guarantees identical results to the split path.
    public bool TrySampleGround(Vector3 localUnitDirection, out float localRadius, out Vector3 localNormal)
    {
        localNormal = localUnitDirection;
        if (!TrySampleRadius(localUnitDirection, out localRadius)) return false;
        localNormal = SampleNormalAt(localUnitDirection, localRadius);
        return true;
    }

    public bool TrySampleRadius(Vector3 localUnitDirection, out float localRadius)
    {
        localRadius = 0f;
        if (localUnitDirection.sqrMagnitude < 1e-8f) return false;
        float r0 = RadiusAt(localUnitDirection.normalized);
        if (!(r0 > 0f)) return false;
        localRadius = r0;
        return true;
    }

    // Outward normal at the already-selected hit (localRadius): two tangent probes -> surface triangle.
    public Vector3 SampleNormalAt(Vector3 localUnitDirection, float localRadius)
    {
        Vector3 dir = localUnitDirection.normalized;
        float arc = NormalProbeMeters / localRadius;
        Vector3 t1 = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Vector3 t2 = Vector3.Cross(dir, t1);
        Vector3 dA = (dir + t1 * arc).normalized;
        Vector3 dB = (dir + t2 * arc).normalized;

        Vector3 p0 = dir * localRadius;
        Vector3 pA = dA * RadiusAt(dA);
        Vector3 pB = dB * RadiusAt(dB);
        Vector3 n = Vector3.Cross(pA - p0, pB - p0).normalized;
        if (Vector3.Dot(n, dir) < 0f) n = -n;
        return n;
    }

    float RadiusAt(Vector3 dir) => _shape.GetScaledElevation(_shape.SampleElevation(dir));
}
