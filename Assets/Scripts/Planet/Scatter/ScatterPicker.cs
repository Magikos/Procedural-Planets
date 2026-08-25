using System;
using System.Collections.Generic;
using UnityEngine;

// Pure geometry for picking the scatter instance the player is looking at. Scatter has no colliders (GPU
// matrices only), so picking is a ray-vs-points test over the live draw buckets' world positions.
public static class ScatterPickMath
{
    // Index of the nearest position that lies ahead of the ray origin within `reach`, and within `maxPerp`
    // perpendicular distance of the ray line; -1 if none. `t` returns the winner's along-ray distance.
    // ray.direction is assumed unit length (Unity Ray normalizes on construction).
    public static int NearestAlongRay(Ray ray, IReadOnlyList<Vector3> positions, float reach, float maxPerp, out float t)
    {
        int best = -1;
        float bestT = float.MaxValue;
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 d = positions[i] - ray.origin;
            float along = Vector3.Dot(d, ray.direction);
            if (along < 0f || along > reach) continue;
            float perp = Vector3.Distance(d, ray.direction * along);
            if (perp > maxPerp) continue;
            if (along < bestT) { bestT = along; best = i; }
        }
        t = best >= 0 ? bestT : 0f;
        return best;
    }
}

// One picked scatter instance, with the whole transform the draw bucket held rather than only where it stood.
// A stump and a fallen log are placed from this: without the yaw and scale they wear the mesh's authored size
// and facing, and visibly do not match the tree that was standing there.
public readonly struct ScatterPick
{
    public readonly ulong Id;
    public readonly int ProtoIndex;
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;

    /// <summary>Uniform: placement scales scatter by one factor, never per axis.</summary>
    public readonly float Scale;

    public ScatterPick(ulong id, int protoIndex, Vector3 position, Quaternion rotation, float scale)
    {
        Id = id;
        ProtoIndex = protoIndex;
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    public static ScatterPick FromMatrix(ulong id, int protoIndex, in Matrix4x4 draw) =>
        new(id, protoIndex, draw.GetPosition(), draw.rotation, draw.lossyScale.x);
}

// Finds the nearest HARVESTABLE scatter instance (Interaction != None) along a camera ray, across every
// prototype's live draw bucket, and returns its stable ScatterId + prototype index + world transform.
public sealed class ScatterPicker
{
    readonly ScatterTileCache _cache;
    // Lazy: the ScatterLibraryDto is not registered when this is constructed (world-service registration runs
    // before settings freeze), so resolve it at pick time when it exists.
    readonly Func<ScatterLibraryDto> _libraryFn;

    public ScatterPicker(ScatterTileCache cache, Func<ScatterLibraryDto> libraryFn)
    {
        _cache = cache;
        _libraryFn = libraryFn;
    }

    public bool TryPick(Ray ray, float reachMeters, float maxPerpMeters, out ScatterPick pick)
    {
        pick = default;
        ScatterLibraryDto library = _libraryFn?.Invoke();
        if (_cache == null || library?.Prototypes == null) return false;

        float bestT = float.MaxValue;
        bool found = false;
        for (int p = 0; p < library.Prototypes.Length; p++)
        {
            if (library.Prototypes[p].Interaction == ScatterInteraction.None) continue;
            var positions = _cache.Positions(p);
            int i = ScatterPickMath.NearestAlongRay(ray, positions, reachMeters, maxPerpMeters, out float t);
            if (i < 0 || t >= bestT) continue;
            bestT = t;
            // Matrices, positions and ids are parallel by ScatterDrawBuckets' invariant, so one index reads
            // the whole instance.
            pick = ScatterPick.FromMatrix(_cache.Ids(p)[i], p, _cache.Matrices(p)[i]);
            found = true;
        }
        return found;
    }
}
