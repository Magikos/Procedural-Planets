using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Species debris, falling leaves, and ground dust preserve each tree's break transitions.</summary>
public sealed class ChopFxSystem : IDisposable
{
    enum DebrisKind { Chips, Leaves, Dust }
    readonly Transform _planet;
    readonly Func<ScatterLibraryDto> _library;
    readonly ScatterHarvestStore _store;
    readonly List<GameObject> _active = new();
    readonly Material _particleMat, _dustMat;

    public ChopFxSystem(Transform planetTransform, Func<ScatterLibraryDto> library = null, ScatterHarvestStore store = null)
    {
        _planet = planetTransform; _library = library; _store = store;
        var source = Resources.Load<Material>("TreeHarvestParticles");
        Shader shader = source != null ? source.shader : Shader.Find("Hidden/SwarmParticles");
        if (shader != null)
        {
            _particleMat = new Material(shader) { name = "Tree fragments", hideFlags = HideFlags.HideAndDontSave };
            _particleMat.SetFloat("_Softness", .08f);
            _dustMat = new Material(_particleMat) { name = "Tree dust" };
            _dustMat.SetFloat("_Softness", 1f);
        }
        EventBus<HarvestHitEvent>.Listen(OnHit);
        EventBus<ScatterHarvestedEvent>.Listen(OnHarvested);
        EventBus<TreePieceHitEvent>.Listen(OnPiece);
        EventBus<TreeLandedEvent>.Listen(OnLanded);
        EventBus<TreeFallMotionEvent>.Listen(OnFalling);
    }

    ScatterPrototypeDto Prototype(int index)
    {
        var protos = _library?.Invoke()?.Prototypes;
        return protos != null && (uint)index < (uint)protos.Length ? protos[index] : null;
    }

    Vector3 Up(Vector3 point)
    {
        Vector3 up = point - _planet.position;
        return up.sqrMagnitude > .001f ? up.normalized : Vector3.up;
    }

    static Color Tint(Material material, Color fallback) => material != null && material.HasProperty("_BaseColor")
        ? material.GetColor("_BaseColor") : fallback;
    static Color Bark(ScatterPrototypeDto proto) => Tint(proto?.TrunkMaterial, new Color(.35f,.24f,.14f));
    static Color Leaves(ScatterPrototypeDto proto) => Tint(proto?.Parts.Length > 1 ? proto.Parts[1].Material : null,
        new Color(.3f,.46f,.18f));

    void OnHit(HarvestHitEvent e)
    {
        var proto = Prototype(e.ProtoIndex);
        if (proto?.Interaction != ScatterInteraction.Chop) return;
        Vector3 up = Up(e.WorldPos);
        Vector3 point = e.HasImpactPoint ? e.WorldPos : e.WorldPos + up * (proto.Tree?.CutPosition.y ?? .75f);
        Burst(point, up, Bark(proto), 28, .16f, DebrisKind.Chips);
        Burst(point, up, Bark(proto), 12, .6f, DebrisKind.Dust);
    }

    void OnHarvested(ScatterHarvestedEvent e)
    {
        var proto = Prototype(e.ProtoIndex);
        if (proto?.Interaction != ScatterInteraction.Chop) return;
        Vector3 up = Up(e.WorldPos);
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up);
        if (proto.Tree == null) { Burst(e.WorldPos + up * .75f, up, Bark(proto), 48, .2f, DebrisKind.Chips); return; }
        float scale = 1f;
        if (_store != null && _store.TryGetStump(e.Id, out var node))
        { if (node.HasStoredTransform) rotation = node.Rotation; scale = ScatterHarvestStore.StoredScaleOr(node.Scale); }
        Vector3 cut = e.WorldPos + rotation * (proto.Tree.CutPosition * scale);
        Burst(cut, up, Bark(proto), 70, .22f, DebrisKind.Chips);
        Burst(cut, up, Bark(proto), 30, 1.1f, DebrisKind.Dust);
        if (!proto.Tree.IsSapling) return;
        var root = NewDebris(e.WorldPos, rotation, scale);
        foreach (var part in proto.Parts) if (part.CanRender) TreeFallSystem.AddPart(root.transform, part.LodMeshes[0], part.Material);
        root.AddComponent<TreeDebris>().Launch(up, Vector3.Cross(up, rotation * Vector3.forward) * .5f, .8f);
        var pose = new ScatterHarvestStore.LogRecord { Position = cut, Rotation = rotation, Scale = scale };
        for (int i = 0; i < proto.Tree.BranchFoliage.Length; i++) ShedLeaves(proto, pose, i, up, 35);
    }

    void OnPiece(TreePieceHitEvent e)
    {
        var proto = Prototype(e.Tree.ProtoIndex);
        if (proto?.Tree == null) return;
        Vector3 up = Up(e.Position);
        Burst(e.Position, up, Bark(proto), e.Broken ? 65 : 26, .18f, DebrisKind.Chips);
        Burst(e.Position, up, Bark(proto), e.Broken ? 26 : 10, e.Broken ? 1.1f : .55f, DebrisKind.Dust);
        if (!e.Broken) return;
        if (e.Branch) ShedLeaves(proto, e.Tree, e.Piece, up, 110);
        var root = NewDebris(e.Tree.Position, e.Tree.Rotation, e.Tree.Scale);
        var tree = proto.Tree;
        Vector3 anchor = e.Branch ? tree.BranchAnchors[e.Piece] : tree.SectionAnchors[e.Piece];
        root.transform.position += e.Tree.Rotation * (anchor * e.Tree.Scale);
        var parts = new GameObject("broken parts"); parts.transform.SetParent(root.transform, false); parts.transform.localPosition = -anchor;
        TreeFallSystem.AddPart(parts.transform, e.Branch ? tree.BranchBark[e.Piece] : tree.LogSections[e.Piece], proto.TrunkMaterial, proto.CutMaterial);
        if (e.Branch && proto.Parts.Length > 1) TreeFallSystem.AddPart(parts.transform, tree.BranchFoliage[e.Piece], proto.Parts[1].Material);
        root.AddComponent<TreeDebris>().Launch(up, up * .7f, e.Branch ? 1.1f : .65f);
    }

    void OnFalling(TreeFallMotionEvent e)
    {
        var proto = Prototype(e.Tree.ProtoIndex);
        if (proto?.Tree == null) return;
        for (int i = 0; i < proto.Tree.BranchFoliage.Length; i++)
            if ((e.Tree.RemovedBranches & (1u << i)) == 0) ShedLeaves(proto, e.Tree, i, e.Up, 14);
    }

    void ShedLeaves(ScatterPrototypeDto proto, ScatterHarvestStore.LogRecord pose, int group, Vector3 up, int count)
    {
        Mesh foliage = proto.Tree.BranchFoliage[group];
        if (foliage == null || foliage.vertexCount == 0 || foliage.GetIndexCount(0) == 0) return;
        Vector3[] points = proto.Tree.BranchSupport[group];
        if (points.Length == 0) return;
        // Support points already sample referenced geometry, including conifer meshes with unused vertices.
        const int sites = 3;
        for (int i = 0; i < sites; i++)
        {
            Vector3 local = points[Mathf.Min(points.Length - 1, (i * 2 + 1) * points.Length / (sites * 2))];
            Vector3 point = pose.Position + pose.Rotation * (local * pose.Scale);
            Burst(point, up, Leaves(proto), Mathf.CeilToInt(count / (float)sites), .22f * pose.Scale,
                DebrisKind.Leaves, .55f * pose.Scale);
        }
    }

    void OnLanded(TreeLandedEvent e)
    {
        var proto = Prototype(e.Tree.ProtoIndex);
        if (proto?.Tree == null) return;
        var tree = proto.Tree;
        foreach (Vector3 anchor in tree.SectionAnchors)
        {
            Vector3 point = e.Tree.Position + e.Tree.Rotation * (anchor * e.Tree.Scale);
            Burst(point, e.Up, new Color(.46f,.40f,.30f), 38, 2.2f * e.Tree.Scale, DebrisKind.Dust, 1f * e.Tree.Scale);
            Burst(point, e.Up, Bark(proto), 20, .2f, DebrisKind.Chips, .45f);
        }
        for (int i = 0; i < tree.BranchFoliage.Length; i++)
            if ((e.Tree.RemovedBranches & (1u << i)) == 0) ShedLeaves(proto, e.Tree, i, e.Up, 60);
    }

    GameObject NewDebris(Vector3 position, Quaternion rotation, float scale)
    {
        var root = new GameObject("Tree break");
        root.transform.SetPositionAndRotation(position, rotation); root.transform.localScale = Vector3.one * scale;
        Track(root); return root;
    }

    void Burst(Vector3 position, Vector3 up, Color color, int count, float size, DebrisKind kind, float radius = .18f)
    {
        if (_particleMat == null) return;
        _active.RemoveAll(x => x == null);
        // Reserve room for impact dust/chips when several crowns shed leaves at once.
        if (_active.Count >= (kind == DebrisKind.Leaves ? 110 : 160)) return;
        bool dust = kind == DebrisKind.Dust, leaves = kind == DebrisKind.Leaves;
        var go = new GameObject("Tree " + kind);
        go.transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, up));
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.duration = .2f; main.loop = false; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(leaves ? 2.5f : dust ? 1.8f : .65f, leaves ? 4.2f : dust ? 3.2f : 1.25f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(dust ? .4f : leaves ? .25f : 1.4f, dust ? 1.5f : leaves ? 1.3f : 4.2f);
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(size * .4f, size);
        main.startSizeY = new ParticleSystem.MinMaxCurve(size * (dust ? .4f : .7f), size * (dust ? 1f : 1.8f));
        main.startSizeZ = size;
        main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
        if (dust) color = Color.Lerp(color, new Color(.65f,.56f,.42f), .7f);
        color.a = dust ? .4f : 1f; main.startColor = color;
        main.maxParticles = count; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var force = ps.forceOverLifetime; force.enabled = true; force.space = ParticleSystemSimulationSpace.World;
        float gravity = dust ? -.12f : leaves ? .65f : 7f;
        force.x = -up.x * gravity; force.y = -up.y * gravity; force.z = -up.z * gravity;
        var emission = ps.emission; emission.rateOverTime = 0; emission.SetBursts(new[]{new ParticleSystem.Burst(0,(short)count)});
        var shape = ps.shape; shape.shapeType = dust ? ParticleSystemShapeType.Hemisphere : ParticleSystemShapeType.Sphere;
        shape.radius = radius;
        if (dust) shape.rotation = new Vector3(-90f,0f,0f);
        var fade = ps.colorOverLifetime; fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(dust ? 0f : 1f,0),new GradientAlphaKey(1f,.12f),new GradientAlphaKey(1f,.55f),new GradientAlphaKey(0f,1)});
        fade.color = gradient;
        var grow = ps.sizeOverLifetime; grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f,dust ? .45f : 1f,1f,dust ? 2f : .65f));
        var spin = ps.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-2.8f,2.8f);
        if (leaves || dust)
        {
            var noise = ps.noise; noise.enabled = true; noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = leaves ? .6f : .3f; noise.frequency = .5f; noise.scrollSpeed = .3f;
        }
        var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = dust ? _dustMat : _particleMat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        Track(go); ps.Play();
    }

    void Track(GameObject go) { _active.RemoveAll(x => x == null); _active.Add(go); }

    public void Dispose()
    {
        EventBus<HarvestHitEvent>.Unlisten(OnHit); EventBus<ScatterHarvestedEvent>.Unlisten(OnHarvested);
        EventBus<TreePieceHitEvent>.Unlisten(OnPiece); EventBus<TreeLandedEvent>.Unlisten(OnLanded);
        EventBus<TreeFallMotionEvent>.Unlisten(OnFalling);
        foreach(var go in _active) if(go != null) UnityEngine.Object.Destroy(go);
        _active.Clear();
        if (_particleMat != null) UnityEngine.Object.Destroy(_particleMat);
        if (_dustMat != null) UnityEngine.Object.Destroy(_dustMat);
    }
}
