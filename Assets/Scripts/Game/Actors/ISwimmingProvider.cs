using UnityEngine;

// The locomotion assembly needs depths, not world services or water body records.
public interface ISwimmingProvider
{
    bool TryGetDepth(Vector3 position, out float signedDepth, out float bodyDepth);
}

/// <summary>Authority-owned buoyancy dimensions. Depths are measured from the actor origin.</summary>
public readonly struct SurfaceSwimProfile
{
    public readonly float RootDepth, MinimumBodyDepth, EntryClearance;
    public readonly bool HoldDiveDepth;
    public readonly float WadingSpeedMultiplier;
    public SurfaceSwimProfile(float rootDepth, float minimumBodyDepth, float entryClearance, bool holdDiveDepth = false,
        float wadingSpeedMultiplier = 1f)
    {
        if (!float.IsFinite(rootDepth) || !float.IsFinite(minimumBodyDepth) || minimumBodyDepth <= 0f ||
            minimumBodyDepth <= rootDepth || !float.IsFinite(entryClearance) || entryClearance < 0f)
            throw new System.ArgumentOutOfRangeException(nameof(rootDepth));
        if (!float.IsFinite(wadingSpeedMultiplier) || wadingSpeedMultiplier <= 0f || wadingSpeedMultiplier > 1f)
            throw new System.ArgumentOutOfRangeException(nameof(wadingSpeedMultiplier));
        RootDepth = rootDepth; MinimumBodyDepth = minimumBodyDepth; EntryClearance = entryClearance;
        HoldDiveDepth = holdDiveDepth;
        WadingSpeedMultiplier = wadingSpeedMultiplier;
    }
}
