using System;
using UnityEngine;

/// <summary>Prefab authoring only. The presenter snapshots these bindings once per instance.</summary>
public sealed class ProceduralRigDefinition : MonoBehaviour
{
    public Transform Body;
    public Transform[] Spine = Array.Empty<Transform>();
    public Transform[] Look = Array.Empty<Transform>();
    public SpringChainDefinition[] Chains = Array.Empty<SpringChainDefinition>();
    public FootDefinition[] Feet = Array.Empty<FootDefinition>();
    [Range(0f, 35f)] public float SpineLimit = 12f;
    [Range(0f, 90f)] public float LookYawLimit = 50f;
    [Range(0f, 60f)] public float LookPitchLimit = 25f;
}

[Serializable]
public sealed class SpringChainDefinition
{
    // Include an end transform. The first transform is anchored; the last supplies the tip position.
    public Transform[] Bones = Array.Empty<Transform>();
    [Min(0.1f)] public float Frequency = 3f;
    [Range(0f, 1f)] public float Damping = 0.7f;
    [Range(0f, 90f)] public float AngleLimit = 35f;
    [Range(0f, 1f)] public float Weight = 1f;
    [Min(0f)] public float GravityScale = 0.1f;
}

[Serializable]
public sealed class FootDefinition
{
    // Main articulation joints only. Keep toes out of the solve so the hoof retains its authored shape.
    public Transform[] Bones = Array.Empty<Transform>();
    public Transform Contact;
    public Vector3 BendDirection = Vector3.forward;
    public AnimationCurve WalkContact = AnimationCurve.Constant(0f, 1f, 1f);
    public AnimationCurve RunContact = AnimationCurve.Constant(0f, 1f, 1f);
    [Min(0f)] public float SoleOffset = 0.04f;
    [Min(0.01f)] public float MaxCorrection = 0.3f;
    [Range(1f, 90f)] public float JointLimit = 35f;
}
