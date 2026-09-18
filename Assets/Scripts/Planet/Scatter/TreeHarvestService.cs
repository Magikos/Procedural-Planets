using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct TreePieceHitEvent : IGameEvent
{
    public readonly ScatterHarvestStore.LogRecord Tree;
    public readonly int Piece;
    public readonly bool Branch, Broken;
    public readonly Vector3 Position;
    public TreePieceHitEvent(ScatterHarvestStore.LogRecord tree, int piece, bool branch, bool broken, Vector3 position)
    { Tree = tree; Piece = piece; Branch = branch; Broken = broken; Position = position; }
}

/// <summary>The same strike processes a branch group or a remaining trunk section.</summary>
public sealed class TreeHarvestService
{
    readonly ScatterHarvestStore _store;
    readonly Func<ScatterLibraryDto> _library;
    readonly Action<string, int> _grant;
    readonly List<ScatterHarvestStore.LogRecord> _scratch = new();

    public TreeHarvestService(ScatterHarvestStore store, Func<ScatterLibraryDto> library, Action<string, int> grant)
    { _store = store; _library = library; _grant = grant; }

    public bool TryPick(Ray ray, float reach, float tolerance, out ulong id, out Vector3 position)
    {
        id = 0; position = default;
        float nearest = float.MaxValue;
        _store.CollectLogs(_scratch);
        foreach (var record in _scratch)
        {
            if (_store.IsFalling(record.Id)) continue;
            GeneratedTree tree = Prototype(record.ProtoIndex)?.Tree;
            if (tree == null) continue;
            Matrix4x4 transform = Matrix4x4.TRS(record.Position, record.Rotation,
                Vector3.one * ScatterHarvestStore.StoredScaleOr(record.Scale));
            Matrix4x4 inverse = transform.inverse;
            Vector3 localOrigin = inverse.MultiplyPoint3x4(ray.origin);
            Vector3 localDirection = inverse.MultiplyVector(ray.direction).normalized;
            for (int i = 0; i < tree.LogSections.Length; i++)
            {
                if ((record.RemovedSections & (1u << i)) != 0) continue;
                Bounds bounds = tree.LogSections[i].bounds;
                bounds.Expand(tolerance * 2f / ScatterHarvestStore.StoredScaleOr(record.Scale));
                if (!bounds.IntersectRay(new Ray(localOrigin, localDirection), out float localDistance)) continue;
                Vector3 point = transform.MultiplyPoint3x4(localOrigin + localDirection * localDistance);
                float distance = Vector3.Dot(point - ray.origin, ray.direction);
                if (distance < 0 || distance > reach || distance >= nearest) continue;
                nearest = distance; id = record.Id; position = point;
            }
        }
        return id != 0;
    }

    public HarvestResult Strike(ulong id, Vector3 point, in ToolTier tool)
    {
        if (tool.Damage <= 0) throw new ArgumentOutOfRangeException(nameof(tool));
        if (!_store.TryGetLog(id, out var record)) return HarvestResult.AlreadyHarvested;
        if (_store.IsFalling(id)) return HarvestResult.NotHarvestable;
        GeneratedTree tree = Prototype(record.ProtoIndex)?.Tree;
        if (tree == null) return HarvestResult.NotHarvestable;
        Matrix4x4 transform = Matrix4x4.TRS(record.Position, record.Rotation,
            Vector3.one * ScatterHarvestStore.StoredScaleOr(record.Scale));
        Vector3 localPoint = transform.inverse.MultiplyPoint3x4(point);
        int branch = NearestBranch(tree, record, localPoint);
        if (branch >= 0)
        {
            var before = record;
            record.RemovedBranches |= 1u << branch;
            if (!_store.UpdateLog(record)) return HarvestResult.AlreadyHarvested;
            EventBus<TreePieceHitEvent>.Raise(new TreePieceHitEvent(before, branch, true, true, point));
            return HarvestResult.Hit;
        }
        int section = -1;
        float nearest = float.MaxValue;
        for (int i = 0; i < tree.SectionAnchors.Length; i++)
        {
            if ((record.RemovedSections & (1u << i)) != 0) continue;
            float distance = tree.LogSections[i].bounds.SqrDistance(localPoint);
            if (distance >= nearest) continue;
            nearest = distance; section = i;
        }
        if (section < 0) return HarvestResult.AlreadyHarvested;
        int hp = Mathf.Clamp(Mathf.CeilToInt(tree.TrunkBaseGirth * record.Scale * 3f), 2, 6);
        var previous = record;
        record.SectionDamage = record.SectionDamage == null ? new int[TreeHarvestGeometry.MaxSections]
            : (int[])record.SectionDamage.Clone();
        record.SectionDamage[section] = Math.Min(hp, (int)Math.Min(int.MaxValue, (long)record.SectionDamage[section] + tool.Damage));
        bool broken = record.SectionDamage[section] >= hp;
        if (broken) record.RemovedSections |= 1u << section;
        if (!_store.UpdateLog(record)) return HarvestResult.AlreadyHarvested;
        HarvestYield yield = default;
        if (broken)
        {
            int total = Mathf.Max(tree.LogSections.Length, Mathf.RoundToInt(tree.WoodYield * Mathf.Pow(record.Scale, 3f)));
            int count = total / tree.LogSections.Length + (section < total % tree.LogSections.Length ? 1 : 0);
            yield = new HarvestYield("Wood", count);
            _grant?.Invoke(yield.ItemId, yield.Count);
        }
        EventBus<TreePieceHitEvent>.Raise(new TreePieceHitEvent(previous, section, false, broken, point));
        return broken ? HarvestResult.Felled(yield) : HarvestResult.Hit;
    }

    static int NearestBranch(GeneratedTree tree, ScatterHarvestStore.LogRecord record, Vector3 point)
    {
        if (!record.HasHarvestGeometry) return -1;
        int result = -1;
        float nearest = float.MaxValue;
        for (int i = 0; i < tree.BranchAnchors.Length; i++)
        {
            if ((record.RemovedBranches & (1u << i)) != 0) continue;
            if (tree.BranchBark[i].vertexCount == 0 && tree.BranchFoliage[i].GetIndexCount(0) == 0) continue;
            float distance = (point - tree.BranchAnchors[i]).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance; result = i;
        }
        return result;
    }

    public ScatterPrototypeDto Prototype(int index)
    {
        var prototypes = _library()?.Prototypes;
        return prototypes != null && (uint)index < (uint)prototypes.Length ? prototypes[index] : null;
    }
}
