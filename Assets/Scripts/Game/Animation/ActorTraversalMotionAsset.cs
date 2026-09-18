using UnityEngine;

/// <summary>Authored clip motion and the matching collision envelope used by traversal authority.</summary>
[CreateAssetMenu(menuName = "Actors/Traversal Motion")]
public sealed class ActorTraversalMotionAsset : ScriptableObject
{
    public AnimationClip Clip;
    [Range(0f, 1f), Tooltip("Earliest supported phase where an evasive action can blend into requested locomotion. One retains the complete action.")]
    public float LocomotionExitNormalized = 1f;
    public bool ElevatedLanding;
    public string SupportContact = "LeftHand";
    public Vector3 ReferenceEdge;
    public float MaxHeightAdjustment = .2f;
    public float MaxPlanarAdjustment = .7f;
    public ActorTraversalMotion.Frame[] Frames;

    public ActorTraversalMotion CreateMotion()
    {
        if (Clip == null || !Clip.isHumanMotion || Clip.length <= 0f)
            throw new System.InvalidOperationException("Traversal motion requires a Humanoid clip.");
        if (SupportContact != "LeftHand" && SupportContact != "RightHand")
            throw new System.InvalidOperationException("Vault support must identify a hand.");
        return new ActorTraversalMotion(Clip.length, ReferenceEdge, MaxHeightAdjustment, MaxPlanarAdjustment, Frames, ElevatedLanding);
    }
}
