using UnityEngine;

/// <summary>Preserves outgoing geometry while a broken piece tumbles and contracts into debris.</summary>
public sealed class TreeDebris : MonoBehaviour
{
    Vector3 _position, _scale, _velocity, _up;
    Quaternion _rotation;
    float _time, _duration;

    public void Launch(Vector3 up, Vector3 velocity, float duration)
    {
        _position = transform.position; _rotation = transform.rotation; _scale = transform.localScale;
        _up = up; _velocity = velocity; _duration = Mathf.Max(0.1f, duration);
    }

    public void Advance(float delta)
    {
        _time += Mathf.Max(0f, delta);
        float t = Mathf.Clamp01(_time / _duration);
        transform.position = _position + _velocity * _time - _up * (1.5f * _time * _time);
        Vector3 axis = Vector3.Cross(_up, _velocity);
        if (axis.sqrMagnitude < 0.01f) axis = Vector3.Cross(_up, Vector3.right);
        transform.rotation = Quaternion.AngleAxis(t * 24f, axis.normalized) * _rotation;
        transform.localScale = _scale * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 1f, t)));
        if (t >= 1f) Destroy(gameObject);
    }

    void Update() => Advance(Time.deltaTime);
}
