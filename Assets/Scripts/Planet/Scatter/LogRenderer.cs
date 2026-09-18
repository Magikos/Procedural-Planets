using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Renders the remaining generated parts at their persistent transform.</summary>
public sealed class LogRenderer : IDisposable
{
    readonly ScatterHarvestStore _store;
    readonly Func<ScatterLibraryDto> _library;
    readonly List<ScatterHarvestStore.LogRecord> _logs = new();
    int _revision = -1;

    public LogRenderer(ScatterHarvestStore store, Transform planetTransform, Func<ScatterLibraryDto> library = null)
    { _store = store; _library = library; }

    public void Render(Camera camera)
    {
        if (_store == null || camera == null) return;
        if (_revision != _store.Revision) { _store.CollectLogs(_logs); _revision = _store.Revision; }
        var prototypes = _library?.Invoke()?.Prototypes;
        if (prototypes == null) return;
        foreach (var record in _logs)
        {
            if (_store.IsFalling(record.Id) || (uint)record.ProtoIndex >= (uint)prototypes.Length) continue;
            var proto = prototypes[record.ProtoIndex];
            var tree = proto.Tree;
            if (tree == null) continue;
            var matrix = Matrix4x4.TRS(record.Position, record.Rotation,
                Vector3.one * ScatterHarvestStore.StoredScaleOr(record.Scale));
            for (int i = 0; i < tree.LogSections.Length; i++)
                if ((record.RemovedSections & (1u << i)) == 0) Draw(tree.LogSections[i], proto.TrunkMaterial, matrix, camera, proto.CutMaterial);
            if (!record.HasHarvestGeometry) continue;
            for (int i = 0; i < tree.BranchBark.Length; i++)
            {
                if ((record.RemovedBranches & (1u << i)) != 0) continue;
                Draw(tree.BranchBark[i], proto.TrunkMaterial, matrix, camera);
                Draw(tree.BranchFoliage[i], proto.Parts.Length > 1 ? proto.Parts[1].Material : proto.TrunkMaterial, matrix, camera);
            }
        }
    }

    static void Draw(Mesh mesh, Material material, Matrix4x4 matrix, Camera camera, Material cutMaterial = null)
    {
        if (mesh == null || mesh.vertexCount == 0 || material == null) return;
        Graphics.DrawMesh(mesh, matrix, material, 0, camera, 0, null, ShadowCastingMode.On, true);
        if (mesh.subMeshCount > 1) Graphics.DrawMesh(mesh, matrix, cutMaterial != null ? cutMaterial : material, 0, camera, 1, null, ShadowCastingMode.On, true);
    }

    public void Dispose() { _logs.Clear(); }
}
