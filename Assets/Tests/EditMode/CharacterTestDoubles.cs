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

    /// <summary>One body of a fixed surface radius, present only inside a cone around a chosen direction —
    /// stubs <see cref="IWaterQueryService"/> so a lake can sit above the terrain in exactly one place.</summary>
    sealed class FixedBodyWaterQuery : IWaterQueryService
    {
        readonly Vector3 _center;
        readonly Vector3 _bodyDir;
        readonly float _surfaceRadius;
        readonly float _minDot;

        public FixedBodyWaterQuery(Vector3 center, Vector3 bodyDir, float surfaceRadius, float minDot = 0.999f)
        {
            _center = center;
            _bodyDir = bodyDir.normalized;
            _surfaceRadius = surfaceRadius;
            _minDot = minDot;
        }

        public bool TryGetWaterSurface(Vector3 worldPosition, out WaterSample sample)
        {
            sample = default;
            Vector3 fromCenter = worldPosition - _center;
            if (fromCenter.sqrMagnitude < 1e-8f) return false;
            Vector3 dir = fromCenter.normalized;
            if (Vector3.Dot(dir, _bodyDir) < _minDot) return false;

            sample = new WaterSample(_center + dir * _surfaceRadius, dir,
                _surfaceRadius - fromCenter.magnitude, 10f, 48, false);
            return true;
        }

        public bool IsUnderwater(Vector3 worldPosition) =>
            TryGetWaterSurface(worldPosition, out WaterSample s) && s.IsSubmerged;
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
