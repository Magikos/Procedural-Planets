using UnityEngine;

// A felled tree tipping over (plan 005 Inc 3). Scripted, not physics — terrain chunks have no colliders, so a
// rigidbody would have nothing to land on. Eases an accelerating rotation about a horizontal axis around the
// base pivot; when it settles it hands its final transform to a persistent log record and removes itself, so
// the LogRenderer draws the resting log seamlessly (no pop). A real rigidbody fall comes later.
public sealed class FallingTree : MonoBehaviour
{
    Quaternion _from, _to;
    float _fallDuration = 1.1f;
    float _t;
    System.Action<Vector3, Quaternion> _onSettled;
    bool _settled;

    // `up` = the surface radial at the tree; `axisSeed` varies the topple direction; `onSettled` receives the
    // final (position, rotation) so the caller can create the persistent log.
    public void Launch(Vector3 up, Vector3 axisSeed, float fallSeconds, System.Action<Vector3, Quaternion> onSettled)
    {
        _fallDuration = Mathf.Max(0.1f, fallSeconds);
        _onSettled = onSettled;
        _from = transform.rotation;

        Vector3 axis = Vector3.Cross(up, axisSeed);
        if (axis.sqrMagnitude < 1e-4f) axis = Vector3.Cross(up, Vector3.right);
        _to = Quaternion.AngleAxis(82f, axis.normalized) * _from; // tip ~82 degrees onto the ground
    }

    void Update()
    {
        _t += Time.deltaTime;
        float f = Mathf.Clamp01(_t / _fallDuration);
        transform.rotation = Quaternion.Slerp(_from, _to, f * f); // ease-in: accelerating topple
        if (f >= 1f && !_settled)
        {
            _settled = true;
            _onSettled?.Invoke(transform.position, transform.rotation);
            Destroy(gameObject);
        }
    }
}
