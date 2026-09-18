using UnityEngine;

public sealed class CharacterSwimmingWater : ISwimmingProvider
{
    readonly IWaterQueryService _water;
    public CharacterSwimmingWater(IWaterQueryService water) => _water = water;
    public bool TryGetDepth(Vector3 position, out float signedDepth, out float bodyDepth)
    {
        signedDepth = bodyDepth = 0f;
        if (_water == null || !_water.TryGetWaterSurface(position, out var sample)) return false;
        signedDepth = sample.SignedDepth;
        bodyDepth = sample.BodyDepth;
        return true;
    }
}
