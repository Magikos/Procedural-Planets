using UnityEngine;

public interface IMeshBuilder
{
    void BuildMesh(Mesh mesh, int resolution, Vector3 localUp, ITerrainProvider terrainProvider);
    void UpdateUVs(Mesh mesh, int resolution, Vector3 localUp, IBiomeProvider biomeProvider);
}
