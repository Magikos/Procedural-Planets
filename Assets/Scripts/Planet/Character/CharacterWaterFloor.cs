using UnityEngine;

/// <summary>
/// The water surface a character stands on instead of sinking to the bed, asked PER POSITION. Every body has
/// its own solved level — a lake that spilled 41 m above the ocean is 41 m of walkable difference — so one
/// planet-wide sea radius grounds the character on an invisible plane inside every raised lake, under the
/// lake he can see. Default (no service) means no water anywhere, which grounds on terrain alone.
/// </summary>
public readonly struct CharacterWaterFloor
{
    readonly IWaterQueryService _water;
    readonly Vector3 _center;

    public CharacterWaterFloor(IWaterQueryService water, Vector3 center)
    {
        _water = water;
        _center = center;
    }

    /// <summary>Radius of the water surface over this point, 0 where the point is not in a body.</summary>
    public float RadiusAt(Vector3 worldPos, Vector3 up) =>
        _water != null && _water.TryGetWaterSurface(worldPos, out WaterSample sample)
            ? Vector3.Dot(sample.SurfacePoint - _center, up)
            : 0f;
}
