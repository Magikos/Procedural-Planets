using System;
using UnityEngine;

/// <summary>Moves the severed tree from its cut pivot to a terrain-supported resting pose.</summary>
public sealed class FallingTree : MonoBehaviour
{
    Vector3 _fromPosition, _toPosition;
    Quaternion _fromRotation, _toRotation;
    float _duration, _elapsed;
    Action _settled;
    Func<bool> _valid;
    Action<float> _moving;

    public void Launch(Vector3 position, Quaternion rotation, float seconds, Action settled, Func<bool> valid = null,
        Action<float> moving = null)
    {
        _fromPosition = transform.position; _fromRotation = transform.rotation;
        _toPosition = position; _toRotation = rotation;
        _duration = Mathf.Max(0.1f, seconds); _settled = settled; _valid = valid; _moving = moving; _elapsed = 0f;
    }

    public void Advance(float seconds)
    {
        if (_valid != null && !_valid()) { Destroy(gameObject); return; }
        _elapsed += Mathf.Max(0f, seconds);
        float t = Mathf.Clamp01(_elapsed / _duration);
        float rotation = t * t * (3f - 2f * t);
        float release = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 1f, t));
        transform.SetPositionAndRotation(Vector3.Lerp(_fromPosition, _toPosition, release),
            Quaternion.Slerp(_fromRotation, _toRotation, rotation));
        _moving?.Invoke(t);
        if (t < 1f) return;
        var callback = _settled; _settled = null;
        callback?.Invoke();
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    void Update() => Advance(Time.deltaTime);
}
