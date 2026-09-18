using UnityEngine;

// Owns felling and subsequent settling while the persistent store owns remaining tree parts.
public sealed class TreeFallSystem : System.IDisposable
{
    readonly IPlanetSurfaceSampler _surface;
    readonly System.Collections.Generic.List<GameObject> _active = new();

    readonly Transform _planetTransform;
    readonly System.Func<ScatterLibraryDto> _libraryFn;
    readonly ScatterHarvestStore _store;

    // Resolved on the first fell rather than in the constructor: Planet builds this system in Awake, and
    // SceneBootstrap does not register ISeedProvider until EarlyInitialize. A chop is a discrete player
    // action, so caching it here is not a per-frame resolve.
    ISeedProvider _seeds;

    public TreeFallSystem(Transform planetTransform, System.Func<ScatterLibraryDto> libraryFn, ScatterHarvestStore store,
        IPlanetSurfaceSampler surface = null)
    {
        _planetTransform = planetTransform;
        _libraryFn = libraryFn;
        _store = store;
        _surface = surface;
        EventBus<ScatterHarvestedEvent>.Listen(OnHarvested);
    }

    void OnHarvested(ScatterHarvestedEvent e)
    {
        if (e.ProtoIndex < 0) return; // a dig, not a fell
        ScatterLibraryDto lib = _libraryFn?.Invoke();
        if (lib?.Prototypes == null || (uint)e.ProtoIndex >= (uint)lib.Prototypes.Length) return;
        ScatterPrototypeDto proto = lib.Prototypes[e.ProtoIndex];
        if (proto.Interaction != ScatterInteraction.Chop || proto.Tree == null) return;
        GeneratedTree tree = proto.Tree;
        if (tree.IsSapling) { _store?.RecordDug(e.Id); return; }
        if (proto.Parts == null || proto.Parts.Length == 0) return;

        Vector3 up = e.WorldPos - _planetTransform.position;
        up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;

        // The felled instance's own yaw and size, read back from the record HarvestService just wrote. Without
        // them the falling tree is the mesh's authored size and facing, and does not match either the tree the
        // player was looking at or the stump it leaves.
        Quaternion standing = Quaternion.FromToRotation(Vector3.up, up);
        float scale = 1f;
        if (_store != null && _store.TryGetStump(e.Id, out ScatterHarvestStore.HarvestNode felled))
        {
            if (felled.HasStoredTransform && ScatterHarvestStore.HasStoredRotation(felled.Rotation)) standing = felled.Rotation;
            scale = ScatterHarvestStore.StoredScaleOr(felled.Scale);
        }

        var root = new GameObject("Falling Tree");
        Vector3 cut = e.WorldPos + standing * (tree.CutPosition * scale);
        root.transform.SetPositionAndRotation(cut, standing);
        root.transform.localScale = Vector3.one * scale;
        for (int i = 0; i < tree.LogSections.Length; i++) AddPart(root.transform, tree.LogSections[i], proto.TrunkMaterial, proto.CutMaterial);
        var branches = new GameObject[tree.BranchBark.Length];
        for (int i = 0; i < branches.Length; i++)
        {
            branches[i] = new GameObject("Branch group");
            branches[i].transform.SetParent(root.transform, false);
            AddPart(branches[i].transform, tree.BranchBark[i], proto.TrunkMaterial);
            AddPart(branches[i].transform, tree.BranchFoliage[i], proto.Parts.Length > 1 ? proto.Parts[1].Material : proto.TrunkMaterial);
        }
        if (root.transform.childCount == 0) { Object.Destroy(root); return; }

        int protoIndex = e.ProtoIndex;
        _seeds ??= ServiceLocator.TryGet(out ISeedProvider seeds) ? seeds : null;
        int fallSeed = _seeds?.GetSeedForEntity(e.Id) ?? (int)e.Id;
        Vector3 topple = _planetTransform.TransformDirection(
            ToppleDirection(fallSeed, _planetTransform.InverseTransformDirection(up)));
        SolveRest(tree, cut, standing, scale, up, topple, GroundClearance, out Vector3 restingPosition, out Quaternion restingRotation);
        // Persist before animation: reloading during the fall restores a complete fallen tree.
        ulong logId = _store.RecordLog(restingPosition, restingRotation, scale, protoIndex, true);
        _store.TryGetLog(logId, out var resting);
        resting.RemovedBranches = GroundContactBranches(tree, restingPosition, restingRotation, scale, Clearance);
        _store.UpdateLog(resting);
        uint broken = 0;
        float nextShed = 0f;
        float Clearance(Vector3 point)
        {
            float value = GroundClearance(point);
            return float.IsFinite(value) ? value : Vector3.Dot(point - e.WorldPos, up);
        }
        void Moving(float progress)
        {
            var pose = resting;
            pose.Position = root.transform.position; pose.Rotation = root.transform.rotation;
            pose.RemovedBranches = broken;
            uint contact = GroundContactBranches(tree, pose.Position, pose.Rotation, scale, Clearance) & ~broken;
            if (progress >= 1f) contact |= resting.RemovedBranches & ~broken;
            for (int i = 0; i < branches.Length; i++)
            {
                uint bit = 1u << i;
                if ((contact & bit) == 0) continue;
                Vector3 point = pose.Position + pose.Rotation * (tree.BranchAnchors[i] * scale);
                float lowest = float.MaxValue;
                foreach (Vector3 support in tree.BranchSupport[i])
                {
                    Vector3 candidate = pose.Position + pose.Rotation * (support * scale);
                    float height = Clearance(candidate);
                    if (height >= lowest) continue;
                    lowest = height; point = candidate - up * height;
                }
                EventBus<TreePieceHitEvent>.Raise(new TreePieceHitEvent(pose, i, true, true, point));
                branches[i].SetActive(false); // the event retains this geometry as outgoing debris at the same pose
                broken |= bit;
            }
            if ((resting.RemovedBranches | broken) != resting.RemovedBranches)
            {
                resting.RemovedBranches |= broken;
                _store.UpdateLog(resting);
            }
            if (progress >= nextShed)
            {
                pose.RemovedBranches = broken;
                EventBus<TreeFallMotionEvent>.Raise(new TreeFallMotionEvent(pose, up));
                nextShed = progress + .1f;
            }
        }
        int world = _store.WorldRevision;
        _store.SetFalling(logId, true);
        _active.RemoveAll(go => go == null);
        _active.Add(root);
        root.AddComponent<FallingTree>().Launch(restingPosition, restingRotation,
            Mathf.Clamp(Mathf.Sqrt(tree.Height * scale) * 0.65f, 1.4f, 4.8f),
            () => { _store.SetFalling(logId, false); EventBus<TreeLandedEvent>.Raise(new TreeLandedEvent(resting, up)); },
            () => _store.WorldRevision == world, Moving);
    }

    float GroundClearance(Vector3 point)
    {
        Vector3 offset = point - _planetTransform.position;
        if (_surface != null && _surface.TryGetSurfaceRadius(offset.normalized, out float radius))
            return offset.magnitude - radius;
        return float.NaN;
    }

    public static void SolveRest(GeneratedTree tree, Vector3 cut, Quaternion standing, float scale,
        Vector3 up, Vector3 direction, System.Func<Vector3, float> clearance,
        out Vector3 position, out Quaternion rotation)
    {
        up = up.sqrMagnitude > .001f ? up.normalized : Vector3.up;
        direction = Vector3.ProjectOnPlane(direction, up).normalized;
        if (direction.sqrMagnitude < .001f) direction = CharacterMath.ArbitraryTangent(up);
        int last = tree.TrunkPoints.Length - 1;
        Vector3 trunk = tree.TrunkPoints[last] - tree.CutPosition;
        float length = Mathf.Max(.1f, trunk.magnitude * scale);
        float baseRadius = tree.TrunkRadii[0] * scale;
        float tipRadius = tree.TrunkRadii[last] * scale;
        float Ground(Vector3 point)
        {
            float value = clearance?.Invoke(point) ?? float.NaN;
            return float.IsFinite(value) ? value : Vector3.Dot(point - cut, up) + tree.CutPosition.y * scale;
        }
        Vector3 target = direction;
        // Fit the tapered trunk's underside to terrain. Branches never determine the resting pose.
        for (int iteration = 0; iteration < 3; iteration++)
        {
            Vector3 tip = cut + target * length;
            float terrainRise = Vector3.Dot(tip - cut, up) + Ground(cut) - Ground(tip);
            float rise = Mathf.Clamp(terrainRise + tipRadius - baseRadius, -length * .65f, length * .65f);
            target = direction * Mathf.Sqrt(length * length - rise * rise) + up * rise;
            target.Normalize();
        }
        rotation = Quaternion.FromToRotation(standing * trunk.normalized, target) * standing;
        float lift = float.NegativeInfinity;
        for (int i = 0; i < tree.TrunkPoints.Length; i++)
        {
            if (tree.TrunkPoints[i].y < tree.CutPosition.y) continue;
            Vector3 point = cut + rotation * ((tree.TrunkPoints[i] - tree.CutPosition) * scale);
            lift = Mathf.Max(lift, tree.TrunkRadii[i] * scale - Ground(point));
        }
        position = cut + up * (float.IsFinite(lift) ? lift : 0f);
    }

    public static uint GroundContactBranches(GeneratedTree tree, Vector3 position, Quaternion rotation, float scale,
        System.Func<Vector3, float> clearance)
    {
        uint contact = 0;
        for (int group = 0; group < tree.BranchSupport.Length; group++)
        foreach (Vector3 local in tree.BranchSupport[group])
        {
            float height = clearance(position + rotation * (local * scale));
            if (!float.IsFinite(height) || height > .035f * scale) continue;
            contact |= 1u << group;
            break;
        }
        return contact;
    }

    public static void AddPart(Transform parent, Mesh mesh, Material material, Material cutMaterial = null)
    {
        if (mesh == null || mesh.vertexCount == 0 || material == null) return;
        var child = new GameObject(mesh.name);
        child.transform.SetParent(parent, false);
        child.AddComponent<MeshFilter>().sharedMesh = mesh;
        child.AddComponent<MeshRenderer>().sharedMaterials = mesh.subMeshCount > 1
            ? new[] { material, cutMaterial != null ? cutMaterial : material } : new[] { material };
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

    public void Dispose()
    {
        EventBus<ScatterHarvestedEvent>.Unlisten(OnHarvested);
        foreach (var go in _active) if (go != null) Object.Destroy(go);
        _active.Clear();
    }
}

public readonly struct TreeLandedEvent : IGameEvent
{
    public readonly ScatterHarvestStore.LogRecord Tree;
    public readonly Vector3 Up;
    public TreeLandedEvent(ScatterHarvestStore.LogRecord tree, Vector3 up) { Tree = tree; Up = up; }
}

public readonly struct TreeFallMotionEvent : IGameEvent
{
    public readonly ScatterHarvestStore.LogRecord Tree;
    public readonly Vector3 Up;
    public TreeFallMotionEvent(ScatterHarvestStore.LogRecord tree, Vector3 up) { Tree = tree; Up = up; }
}
