using System.Collections.Generic;
using UnityEngine;

// Registration is paired with the owning water mesh lifetime. No per-frame scene searches.
public static class WaterSurfaceRegistry
{
    static readonly List<MeshFilter> Surfaces = new();
    public static MeshFilter[] Snapshot { get; private set; } = System.Array.Empty<MeshFilter>();

    public static void Register(MeshFilter surface)
    {
        if (surface == null || Surfaces.Contains(surface)) return;
        Surfaces.RemoveAll(item => item == null);
        Surfaces.Add(surface);
        Snapshot = Surfaces.ToArray();
    }

    public static void Unregister(MeshFilter surface)
    {
        Surfaces.RemoveAll(item => item == null || item == surface);
        Snapshot = Surfaces.ToArray();
    }
}
