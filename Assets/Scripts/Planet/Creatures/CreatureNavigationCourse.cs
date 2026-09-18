using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Local obstacle course. Owns only its own navigation data and temporary geometry.</summary>
public sealed class CreatureNavigationCourse : IDisposable
{
    readonly GameObject _root;
    readonly Mesh _mesh;
    readonly Material _material;
    NavMeshData _data;
    NavMeshDataInstance _instance;
    public static readonly Vector3 Refuge = new(0f, 3f, 9f);

    public CreatureNavigationCourse(Transform parent)
    {
        _root = new GameObject("Navigation obstacle course"); _root.transform.SetParent(parent, false);
        _material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.3f, .32f, .3f) };
        _mesh = new Mesh { name = "Encounter navigation ground" };
        const int side = 81;
        var vertices = new Vector3[side * side]; var triangles = new int[(side - 1) * (side - 1) * 6];
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
            vertices[z * side + x] = new Vector3(x - 40, CreatureAnimationPrototype.GroundHeight(x - 40, z - 40), z - 40);
        int t = 0;
        for (int z = 0; z < side - 1; z++) for (int x = 0; x < side - 1; x++)
        { int i = z * side + x; triangles[t++] = i; triangles[t++] = i + side; triangles[t++] = i + 1;
          triangles[t++] = i + 1; triangles[t++] = i + side; triangles[t++] = i + side + 1; }
        _mesh.vertices = vertices; _mesh.triangles = triangles; _mesh.RecalculateNormals();
        var sources = new List<NavMeshBuildSource> { new() { shape = NavMeshBuildSourceShape.Mesh, sourceObject = _mesh, transform = Matrix4x4.identity, area = 0 } };
        AddBox("Rock barrier (route around ends)", new(-4, 1.5f, -1), new(1.5f, 3, 6), sources);
        AddBox("Refuge (unreachable top)", new(0, 1.5f, 9), new(4, 3, 4), sources);
        AddBox("Escape passage left", new(10, 1.5f, 5), new(2, 3, 5), sources);
        AddBox("Escape passage right", new(15, 1.5f, 5), new(2, 3, 5), sources);
        var settings = NavMesh.GetSettingsByIndex(0);
        settings.agentRadius = .65f; settings.agentHeight = 2f; settings.agentClimb = .3f; settings.agentSlope = 35f;
        _data = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(Vector3.zero, new Vector3(84, 12, 84)), Vector3.zero, Quaternion.identity);
        if (_data == null) { Dispose(); throw new InvalidOperationException("Encounter navigation build failed."); }
        _instance = NavMesh.AddNavMeshData(_data);
    }
    void AddBox(string name, Vector3 position, Vector3 size, List<NavMeshBuildSource> sources)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.SetParent(_root.transform, false); go.transform.position = position; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = _material;
        sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, transform = Matrix4x4.TRS(position, Quaternion.identity, Vector3.one), size = size, area = 1 });
    }
    public void Dispose()
    {
        if (_instance.valid) _instance.Remove();
        if (_data != null) UnityEngine.Object.Destroy(_data);
        _root.SetActive(false); UnityEngine.Object.Destroy(_root);
        UnityEngine.Object.Destroy(_mesh); UnityEngine.Object.Destroy(_material);
    }
}
