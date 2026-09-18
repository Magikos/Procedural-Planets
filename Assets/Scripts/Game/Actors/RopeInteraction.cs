using UnityEngine;

/// <summary>A fixed rope route with editable authored mount, gait, idle, and ground exit.</summary>
public sealed class RopeInteraction : MonoBehaviour
{
    public float Height = 5f;
    public float BodyDistance = .22f;
    public float Radius = .08f;
    public float MaxRequestedCorrection { get; private set; }
    public ActorTraversalMotionAsset[] Motions;
    public bool Valid => isActiveAndEnabled && float.IsFinite(Height) && Height >= 2f &&
        float.IsFinite(BodyDistance) && BodyDistance >= .2f && BodyDistance <= .5f &&
        float.IsFinite(Radius) && Radius >= .02f && Radius <= .12f &&
        Motions != null && Motions.Length == 5 && System.Array.TrueForAll(Motions, m => m != null);
    public Vector3 Bottom => transform.position - transform.forward * BodyDistance - transform.up * ActorCollision.Skin;
    public Vector3 Entry => Bottom - transform.forward * Motions[0].CreateMotion().Sample(1f).Root.z;
    public static string PhaseName(int phase) => phase switch
    { 0 => "Mount", 1 => "Up", 2 => "Down", 3 => "Idle", 4 => "Exit", _ => throw new System.ArgumentOutOfRangeException(nameof(phase)) };

    public void ApplyContacts(ProceduralPoseRig pose, ActorRope actor)
    {
        MaxRequestedCorrection = 0;
        float weight = actor.Phase == 0 ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.6f, 1f, actor.Progress)) :
            actor.Phase == 4 ? 1f - Mathf.SmoothStep(0, 1, actor.Progress / .35f) : 1f;
        foreach (string id in new[] { "LeftHand", "RightHand" })
        {
            if (!pose.TryGetInteractionContactPose(id, out var palm)) continue;
            var axis = transform.position + transform.up * Vector3.Dot(palm.position - transform.position, transform.up);
            var radial = palm.position - axis;
            var target = axis + radial.normalized * Radius;
            float correction = Vector3.Distance(palm.position, target);
            // Preserve the authored release/reach arc when the hand leaves the rope.
            float contact = weight * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.06f, .09f, correction)));
            MaxRequestedCorrection = Mathf.Max(MaxRequestedCorrection, correction * contact);
            pose.SetInteractionTarget(id, new InteractionPoseTarget(target, palm.rotation, contact, useContact: true));
        }
    }
}
