using System;
using UnityEngine;

/// <summary>Validated, actor-local trajectory and body envelope sampled from one traversal clip.</summary>
public sealed class ActorTraversalMotion
{
    [Serializable]
    public struct Frame
    {
        public Vector3 Root, CapsuleA, CapsuleB, PoseOffset;
        public float Radius, AnchorWeight, PlanarFitWeight, RootYawDegrees;
    }

    readonly Frame[] _frames;
    public float Duration { get; }
    public Vector3 ReferenceEdge { get; }
    public float MaxHeightAdjustment { get; }
    public float MaxPlanarAdjustment { get; }
    public int FrameCount => _frames.Length;

    public ActorTraversalMotion(float duration, Vector3 referenceEdge, float maxHeightAdjustment,
        float maxPlanarAdjustment, Frame[] frames, bool elevatedLanding = false)
    {
        if (!float.IsFinite(duration) || duration <= 0f || !CharacterMath.IsFinite(referenceEdge) ||
            !float.IsFinite(maxHeightAdjustment) || maxHeightAdjustment < 0f ||
            !float.IsFinite(maxPlanarAdjustment) || maxPlanarAdjustment < 0f)
            throw new ArgumentException("Traversal motion requires finite dimensions and a positive duration.");
        if (frames == null || frames.Length < 2) throw new ArgumentException("Traversal motion requires at least two frames.", nameof(frames));
        foreach (var frame in frames)
            if (!float.IsFinite(frame.RootYawDegrees) || !CharacterMath.IsFinite(frame.PoseOffset) || !CharacterMath.IsFinite(frame.Root) || !CharacterMath.IsFinite(frame.CapsuleA) ||
                !CharacterMath.IsFinite(frame.CapsuleB) || !float.IsFinite(frame.Radius) || frame.Radius <= 0f ||
                !float.IsFinite(frame.AnchorWeight) || frame.AnchorWeight < 0f || frame.AnchorWeight > 1f ||
                !float.IsFinite(frame.PlanarFitWeight) || frame.PlanarFitWeight < 0f || frame.PlanarFitWeight > 1f)
                throw new ArgumentException("Traversal frames require finite geometry and weights in [0,1].", nameof(frames));
        var first = frames[0]; var last = frames[frames.Length - 1];
        if (first.Root.sqrMagnitude > .000001f || first.AnchorWeight != 0f || first.PlanarFitWeight != 0f ||
            last.AnchorWeight != 0f || last.PlanarFitWeight != 1f || (elevatedLanding ? Mathf.Abs(last.Root.y) <= .001f : Mathf.Abs(last.Root.y) > .001f))
            throw new ArgumentException("Traversal must start at zero, release vertical fitting, and finish planar fitting at its declared landing elevation.", nameof(frames));
        Duration = duration; ReferenceEdge = referenceEdge; MaxHeightAdjustment = maxHeightAdjustment;
        MaxPlanarAdjustment = maxPlanarAdjustment; _frames = (Frame[])frames.Clone();
    }

    public Frame Sample(float normalizedTime)
    {
        if (!float.IsFinite(normalizedTime)) throw new ArgumentOutOfRangeException(nameof(normalizedTime));
        float position = Mathf.Clamp01(normalizedTime) * (_frames.Length - 1);
        int index = Mathf.Min((int)position, _frames.Length - 2);
        float t = position - index; var a = _frames[index]; var b = _frames[index + 1];
        return new Frame { Root = Vector3.Lerp(a.Root, b.Root, t), CapsuleA = Vector3.Lerp(a.CapsuleA, b.CapsuleA, t),
            PoseOffset = Vector3.Lerp(a.PoseOffset, b.PoseOffset, t), CapsuleB = Vector3.Lerp(a.CapsuleB, b.CapsuleB, t), Radius = Mathf.Lerp(a.Radius, b.Radius, t),
            RootYawDegrees = Mathf.LerpAngle(a.RootYawDegrees, b.RootYawDegrees, t),
            AnchorWeight = Mathf.Lerp(a.AnchorWeight, b.AnchorWeight, t), PlanarFitWeight = Mathf.Lerp(a.PlanarFitWeight, b.PlanarFitWeight, t) };
    }
}
