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

    // Resolved on the first fell rather than in the constructor: Planet builds this system in Awake, and
    // SceneBootstrap does not register ISeedProvider until EarlyInitialize. A chop is a discrete player
    // action, so caching it here is not a per-frame resolve.
    ISeedProvider _seeds;

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
        _seeds ??= ServiceLocator.TryGet(out ISeedProvider seeds) ? seeds : null;
        int fallSeed = _seeds?.GetSeedForEntity(e.Id) ?? (int)e.Id;
        Vector3 topple = _planetTransform.TransformDirection(
            ToppleDirection(fallSeed, _planetTransform.InverseTransformDirection(up)));
        root.AddComponent<FallingTree>().Launch(up, topple, FallSeconds,
            (pos, rot) => _store?.RecordLog(pos, rot, protoIndex));
    }

    // Which way a felled tree goes over, from a seed derived off the tree's own id so two processes agree.
    // It was Random.onUnitSphere, which reads global RNG state that tree generation had already stirred:
    // the same tree fell a different way every session, and under multiplayer every client would watch a
    // different fall and save a different resting log.
    //
    // The basis is built in PLANET-LOCAL space on purpose. Crossing against world up would fold the planet's
    // own rotation into the answer, and two processes holding the planet at different rotations would then
    // disagree again for a subtler reason.
    public static Vector3 ToppleDirection(int fallSeed, Vector3 localUp)
    {
        localUp = localUp.sqrMagnitude > 1e-6f ? localUp.normalized : Vector3.up;

        // The seed is already FNV-mixed by ISeedProvider; spreading the low bits over a full turn is all
        // that is left to do.
        float angle = (uint)fallSeed * (2f * Mathf.PI / 4294967296f);

        Vector3 reference = Mathf.Abs(localUp.y) < 0.99f ? Vector3.up : Vector3.right;
        Vector3 tangent = Vector3.Cross(localUp, reference).normalized;
        Vector3 bitangent = Vector3.Cross(localUp, tangent);
        return tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle);
    }

    public void Dispose() => EventBus<ScatterHarvestedEvent>.Unlisten(OnHarvested);
}
