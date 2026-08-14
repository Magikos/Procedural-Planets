using UnityEngine;

// Spawns a tipping-over falling tree when a tree is FELLED (subscribes to ScatterHarvestedEvent, the fall
// seam from plan 005 §3b). Uses the felled tree's own part meshes so the falling top matches the stump left
// behind. Dig events (ProtoIndex < 0) are ignored. The spawned GameObject self-animates + self-destroys via
// FallingTree; this system only listens and spawns.
public sealed class TreeFallSystem : System.IDisposable
{
    const float FallSeconds = 1.1f;

    readonly Transform _planetTransform;
    readonly System.Func<ScatterLibraryDto> _libraryFn;
    readonly ScatterHarvestStore _store;

    public TreeFallSystem(Transform planetTransform, System.Func<ScatterLibraryDto> libraryFn, ScatterHarvestStore store)
    {
        _planetTransform = planetTransform;
        _libraryFn = libraryFn;
        _store = store;
        EventBus<ScatterHarvestedEvent>.Listen(OnHarvested);
    }

    void OnHarvested(ScatterHarvestedEvent e)
    {
        if (e.ProtoIndex < 0) return; // a dig, not a fell
        ScatterLibraryDto lib = _libraryFn?.Invoke();
        if (lib?.Prototypes == null || (uint)e.ProtoIndex >= (uint)lib.Prototypes.Length) return;
        ScatterPrototypeDto proto = lib.Prototypes[e.ProtoIndex];
        if (proto.Parts == null || proto.Parts.Length == 0) return;

        Vector3 up = e.WorldPos - _planetTransform.position;
        up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;

        var root = new GameObject("Falling Tree");
        root.transform.SetPositionAndRotation(e.WorldPos, Quaternion.FromToRotation(Vector3.up, up));
        foreach (ScatterPartDto part in proto.Parts)
        {
            if (part?.Material == null || part.LodMeshes == null || part.LodMeshes.Length == 0 || part.LodMeshes[0] == null)
                continue;
            var child = new GameObject(part.Material.name);
            child.transform.SetParent(root.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = part.LodMeshes[0];
            child.AddComponent<MeshRenderer>().sharedMaterial = part.Material;
        }
        if (root.transform.childCount == 0) { Object.Destroy(root); return; }

        int protoIndex = e.ProtoIndex;
        root.AddComponent<FallingTree>().Launch(up, Random.onUnitSphere, FallSeconds,
            (pos, rot) => _store?.RecordLog(pos, rot, protoIndex));
    }

    public void Dispose() => EventBus<ScatterHarvestedEvent>.Unlisten(OnHarvested);
}
