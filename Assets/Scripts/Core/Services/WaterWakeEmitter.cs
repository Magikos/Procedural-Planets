using System.Collections.Generic;
using UnityEngine;

public sealed class WaterWakeEmitter : MonoBehaviour
{
    static readonly List<WaterWakeEmitter> _activeEmitters = new List<WaterWakeEmitter>();

    [Header("Wake Shape")]
    [SerializeField, Min(0.25f)] float _radius = 18f;
    [SerializeField, Min(1f)] float _length = 145f;

    [Header("Wake Response")]
    [SerializeField, Range(0f, 1f)] float _strength = 0.85f;
    [SerializeField, Range(0f, 1f)] float _foamStrength = 0.72f;
    [SerializeField, Min(0f)] float _minSpeed = 0.25f;
    [SerializeField, Min(0.01f)] float _speedForFullWake = 12f;

    Vector3 _lastPosition;
    Vector3 _lastDirection = Vector3.forward;
    bool _hasLastPosition;

    public static IReadOnlyList<WaterWakeEmitter> ActiveEmitters => _activeEmitters;

    void OnEnable()
    {
        if (!_activeEmitters.Contains(this))
            _activeEmitters.Add(this);

        _lastPosition = transform.position;
        _lastDirection = transform.forward.sqrMagnitude > 0.0001f ? transform.forward.normalized : Vector3.forward;
        _hasLastPosition = false;
    }

    void OnDisable()
    {
        _activeEmitters.Remove(this);
        _hasLastPosition = false;
    }

    public bool TryGetWakeSource(out WaterWakeController.WakeSource source)
    {
        source = default;

        Vector3 position = transform.position;
        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 delta = _hasLastPosition ? position - _lastPosition : Vector3.zero;
        float speed = delta.magnitude / deltaTime;

        _lastPosition = position;
        _hasLastPosition = true;

        if (speed < _minSpeed)
            return false;

        Vector3 direction = delta.sqrMagnitude > 0.0001f ? delta.normalized : _lastDirection;
        _lastDirection = direction;
        float speed01 = Mathf.Clamp01((speed - _minSpeed) / Mathf.Max(_speedForFullWake - _minSpeed, 0.01f));

        source = new WaterWakeController.WakeSource
        {
            Position = position,
            Direction = direction,
            Radius = _radius,
            Length = _length,
            Strength = _strength,
            FoamStrength = _foamStrength,
            Speed01 = speed01
        };
        return true;
    }
}
