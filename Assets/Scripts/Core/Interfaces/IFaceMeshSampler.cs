using UnityEngine;

// Minimal contract WaterMeshBuilder needs from a planet face — a regular Resolution × Resolution
// grid of unit-sphere directions + elevations. Both the per-face provider (TerrainFace) and the
// chunked provider (root-chunk adapter) implement this so the water builder is agnostic to
// which surface-generation path is in use.
public interface IFaceMeshSampler
{
    Vector3[] UnitSpherePoints { get; }
    float[] Elevations { get; }
    int Resolution { get; }
}
