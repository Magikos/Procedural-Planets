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

    public bool TrySampleGround(Vector3 localUnitDirection, out float localRadius, out Vector3 localNormal)
    {
        localRadius = 0f;
        localNormal = localUnitDirection;
        if (localUnitDirection.sqrMagnitude < 1e-8f) return false;

        Vector3 dir = localUnitDirection.normalized;
        float r0 = RadiusAt(dir);
        if (!(r0 > 0f)) return false;

        // Two tangent probes -> surface triangle -> outward normal.
        float arc = NormalProbeMeters / r0;
        Vector3 t1 = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Vector3 t2 = Vector3.Cross(dir, t1);
        Vector3 dA = (dir + t1 * arc).normalized;
        Vector3 dB = (dir + t2 * arc).normalized;

        Vector3 p0 = dir * r0;
        Vector3 pA = dA * RadiusAt(dA);
        Vector3 pB = dB * RadiusAt(dB);
        Vector3 n = Vector3.Cross(pA - p0, pB - p0).normalized;
        if (Vector3.Dot(n, dir) < 0f) n = -n;

        localRadius = r0;
        localNormal = n;
        return true;
    }

    float RadiusAt(Vector3 dir) => _shape.GetScaledElevation(_shape.SampleElevation(dir));
}
