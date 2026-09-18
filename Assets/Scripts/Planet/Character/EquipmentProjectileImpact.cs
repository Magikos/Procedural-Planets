using UnityEngine;

/// <summary>Leaves the same physical projectile at its first impact.</summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class EquipmentProjectileImpact : MonoBehaviour
{
    public bool Impacted { get; private set; }
    void OnCollisionEnter(Collision collision)
    {
        var body = GetComponent<Rigidbody>();
        if (body.isKinematic) return;
        body.linearVelocity = body.angularVelocity = Vector3.zero;
        body.isKinematic = true; Impacted = true;
    }
}
