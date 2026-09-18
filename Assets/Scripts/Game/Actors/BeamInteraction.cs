using UnityEngine;

/// <summary>Geometry and editable authored motion for one fixed straight balance beam.</summary>
public sealed class BeamInteraction : MonoBehaviour
{
    public static string PhaseName(int index) => index switch { 0 => "Forward_Start", 1 => "Forward_Loop", 2 => "Forward_End", 3 => "Back_Loop", 4 => "Back_End", 5 => "Idle", 6 => "Turn_180", _ => throw new System.ArgumentOutOfRangeException(nameof(index)) };
    public Collider Surface;
    public Collider Wall;
    public bool WallSideWalk;
    [Min(.5f)] public float Length = 4f;
    public ActorTraversalMotionAsset[] Motions;
    public bool Valid => isActiveAndEnabled && Surface != null && Surface.enabled && Surface.gameObject.activeInHierarchy &&
        float.IsFinite(Length) && Length >= .5f && Motions != null && Motions.Length == 7 && System.Array.TrueForAll(Motions, m => m != null);
    public Vector3 Entry(Vector3 forward)
    {
        var displacement = Motions[0].CreateMotion().Sample(1f).Root;
        return transform.position - forward * (Length * .5f + displacement.z) - Vector3.Cross(transform.up, forward) * displacement.x + transform.up * .025f;
    }
}
