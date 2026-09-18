using UnityEngine;

/// <summary>Land-animal water policy. Swimming remains a movement capability, not permission to live offshore.</summary>
public static class CreatureWaterAvoidance
{
    public static bool IsDeep(IWaterQueryService water, Vector3 position) =>
        water != null && water.TryGetWaterSurface(position, out var sample) &&
        sample.BodyDepth > PlanetCreatureResources.MaximumWadingDepth;

    public static bool CanEnter(IWaterQueryService water, Vector3 from, Vector3 to, bool fleeing)
    {
        if (!CharacterMath.IsFinite(from) || !CharacterMath.IsFinite(to)) return false;
        if (water == null) return true;
        float distance = Vector3.Distance(from, to);
        if (distance > 32f) return false;
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / .25f));
        for (int i = 1; i <= steps; i++)
        {
            if (!water.TryGetWaterSurface(Vector3.Lerp(from, to, i / (float)steps), out var sample)) continue;
            if (!float.IsFinite(sample.BodyDepth)) return false;
            if (sample.BodyDepth > PlanetCreatureResources.MaximumWadingDepth && !fleeing) return false;
        }
        return true;
    }
}
