using UnityEngine;

// Deterministic, LOD-independent ground query in planet-local space: given a unit direction from
// the planet centre, return the ground point's local radius and the outward surface normal.
//
// Deliberately returns radius + normal rather than a bare radius so the contract survives the move
// to marching-cubes / SDF terrain, where a ray can cross solid ground several times and "the
// surface" is no longer a single f(direction) value — an implementation can then raymarch a density
// field and hand back an arbitrary point + normal without changing any caller.
public interface ISurfaceGroundSampler
{
    bool TrySampleGround(Vector3 localUnitDirection, out float localRadius, out Vector3 localNormal);
}
