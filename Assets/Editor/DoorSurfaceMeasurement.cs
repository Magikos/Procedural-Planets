using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Measures rendered hand triangles against the actor-facing panel, independently of the IK marker error.</summary>
public sealed class DoorSurfaceMeasurement : IDisposable
{
    readonly List<(SkinnedMeshRenderer renderer, Mesh baked, int[] triangles)> _hands = new();
    public Vector3 LastSkinNormal { get; private set; }
    public bool HasPanel { get; private set; }
    public int HandMeshCount => _hands.Count;
    public DoorSurfaceMeasurement(Transform root, Transform wrist)
    {
        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (renderer.sharedMesh == null || renderer.bones.Length == 0) continue;
            var handBones = new bool[renderer.bones.Length];
            for (int i = 0; i < handBones.Length; i++)
                handBones[i] = renderer.bones[i] == wrist || renderer.bones[i] != null && renderer.bones[i].IsChildOf(wrist);
            var weights = renderer.sharedMesh.boneWeights;
            var hand = new float[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                var w = weights[i];
                hand[i] = (handBones[w.boneIndex0] ? w.weight0 : 0f) + (handBones[w.boneIndex1] ? w.weight1 : 0f) +
                    (handBones[w.boneIndex2] ? w.weight2 : 0f) + (handBones[w.boneIndex3] ? w.weight3 : 0f);
            }
            var indices = renderer.sharedMesh.triangles;
            var triangles = new List<int>();
            for (int i = 0; i < indices.Length; i += 3)
                if (hand[indices[i]] + hand[indices[i + 1]] + hand[indices[i + 2]] >= 1.5f)
                { triangles.Add(indices[i]); triangles.Add(indices[i + 1]); triangles.Add(indices[i + 2]); }
            if (triangles.Count > 0) _hands.Add((renderer, new Mesh(), triangles.ToArray()));
        }
    }

    public bool Sample(HumanInteractionReview.Target door, Vector3 palm, Vector3 forward,
        out Vector3 skin, out Vector3 panel, out Vector3 normal)
    {
        skin = panel = normal = Vector3.zero; LastSkinNormal = Vector3.zero;
        HasPanel = HumanInteractionReview.TryProjectContactSurface(door, palm - forward * .2f, forward, out panel, out normal);
        if (!HasPanel) return false;
        float furthest = float.NegativeInfinity;
        foreach (var hand in _hands)
        {
            hand.renderer.BakeMesh(hand.baked);
            var surface = new HumanInteractionReview.Target { Hinge = hand.renderer.transform, Contact = hand.renderer.transform,
                LidVertices = hand.baked.vertices, LidTriangles = hand.triangles };
            if (!HumanInteractionReview.TryProjectContactSurface(surface, palm + forward * .15f, -forward, out var point, out var skinNormal)) continue;
            float distance = Vector3.Dot(point - palm, forward);
            if (distance < -.01f || distance > .1f || distance <= furthest) continue;
            furthest = distance; skin = point; LastSkinNormal = skinNormal;
        }
        return float.IsFinite(furthest);
    }

    public void Dispose()
    {
        foreach (var hand in _hands) UnityEngine.Object.DestroyImmediate(hand.baked);
        _hands.Clear();
    }
}



