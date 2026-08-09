using UnityEngine;

namespace ProceduralPlanets.Tests
{
    /// <summary>Constant-acceleration gravity — the "flat world" / anti-grav-magnitude test double.
    /// Returns false when the acceleration is zero, matching the non-zero success contract.</summary>
    sealed class ConstantGravityProvider : IGravityProvider
    {
        readonly Vector3 _accel;
        public ConstantGravityProvider(Vector3 accel) { _accel = accel; }

        public bool TryGetGravity(Vector3 worldPos, out Vector3 acceleration)
        {
            acceleration = _accel;
            return _accel.sqrMagnitude > 1e-10f;
        }
    }

    /// <summary>An infinite plane through the origin with normal = up (= -downDir). Grounds a body to sit
    /// footOffset above the plane along up. The "planar ground" test double — proves the driver runs with
    /// no planet.</summary>
    sealed class PlanarGroundingProvider : IGroundingProvider
    {
        public bool TryGround(Vector3 worldPos, Vector3 downDir, float footOffset, out GroundResult result)
        {
            Vector3 up = (-downDir).normalized;
            Vector3 onPlane = worldPos - up * Vector3.Dot(worldPos, up);
            result = new GroundResult(onPlane + up * footOffset, up);
            return true;
        }
    }

    /// <summary>A sphere of fixed radius — stubs <see cref="IPlanetSurfaceSampler"/> for grounding tests.</summary>
    sealed class FixedRadiusSampler : IPlanetSurfaceSampler
    {
        readonly float _radius;
        public FixedRadiusSampler(float radius) { _radius = radius; }

        public bool TryGetSurfaceRadius(Vector3 worldUnitDirection, out float surfaceRadius)
        {
            surfaceRadius = _radius;
            return true;
        }
    }
}
