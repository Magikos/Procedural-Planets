using UnityEngine;

/// <summary>Place at the foot contact point on a branch, rail, or rock beneath the owning Planet.</summary>
[DisallowMultipleComponent]
public sealed class WildlifeLandingSite : MonoBehaviour
{
    [Tooltip("Species allowed to use this point. Insect consumers are not connected yet.")]
    public WildlifeLandingUse Uses = WildlifeLandingUse.Bird;
    [Min(0.05f), Tooltip("Available body height and wing clearance in metres. One animal per point.")]
    public float Clearance = 1f;

    public WildlifeLandingTarget Snapshot => new(UnityEngine.EntityId.ToULong(GetEntityId()), transform.position, transform.up, Clearance, Uses);

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.15f);
        Gizmos.DrawLine(transform.position, transform.position + transform.up * Clearance);
    }
}
