using System;
using UnityEngine;

/// <summary>Water-plane fish motion and an elastic line. Rendering consumes this shared state.</summary>
public sealed class ActorFishingFight
{
    public Vector3 Position { get; private set; }
    public Vector3 Velocity { get; private set; }
    public float LineLength { get; private set; }
    public float Tension { get; private set; }
    public float Elapsed { get; private set; }
    public bool ReadyToLand => LineLength <= 2.1f && Elapsed >= 3;
    Vector3 _waterOrigin, _up, _forward, _right;
    bool _ready;

    public void Begin(Vector3 fish, Vector3 tip, Vector3 up, Vector3 forward)
    {
        RequireFinite(fish); RequireFinite(tip); RequireFinite(up); RequireFinite(forward);
        if (up.sqrMagnitude < .001f || Vector3.ProjectOnPlane(forward, up).sqrMagnitude < .001f)
            throw new ArgumentException("Fishing requires an up axis and a water-plane forward direction.");
        Position = _waterOrigin = fish; Velocity = Vector3.zero;
        _up = up.normalized; _forward = Vector3.ProjectOnPlane(forward, _up).normalized;
        _right = Vector3.Cross(_up, _forward);
        LineLength = Mathf.Clamp(Vector3.Distance(tip, fish), 2.5f, 8f);
        Elapsed = Tension = 0; _ready = true;
    }

    public void Tick(float dt, Vector3 tip, float reel, float giveLine, float lateral = 1)
    {
        if (!_ready) throw new InvalidOperationException("Begin the fishing fight before advancing it.");
        RequireFinite(tip);
        if (!float.IsFinite(dt) || dt < 0 || dt > 1) throw new ArgumentOutOfRangeException(nameof(dt));
        if (!float.IsFinite(reel) || reel < 0 || reel > 1 || !float.IsFinite(giveLine) || giveLine < 0 || giveLine > 1 ||
            !float.IsFinite(lateral) || lateral < 0 || lateral > 1) throw new ArgumentOutOfRangeException(nameof(reel));
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 120));
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            Elapsed += h;
            // Giving line wins when both controls are held.
            LineLength = Mathf.Clamp(LineLength + (giveLine * 1.5f - reel * (1 - giveLine) * .55f) * h, 1.5f, 8f);
            Vector3 delta = Position - tip;
            float extension = Mathf.Max(0, delta.magnitude - LineLength);
            Tension = Mathf.Clamp01(extension * 2.5f);
            Vector3 swim = _forward * (1.3f + .6f * Mathf.Sin(Elapsed * 1.7f)) +
                _right * (lateral * 3f * Mathf.Sin(Elapsed * 1.1f));
            Vector3 pull = delta.sqrMagnitude > .00001f ? -delta.normalized * extension * 14f : Vector3.zero;
            Vector3 boundary = Vector3.ProjectOnPlane(Position - _waterOrigin, _up);
            if (boundary.magnitude > 2.2f) swim -= boundary.normalized * (boundary.magnitude - 2.2f) * 12;
            Velocity += Vector3.ProjectOnPlane(swim + pull, _up) * h;
            Velocity *= Mathf.Exp(-1.8f * h);
            Position += Velocity * h;
            Position -= _up * Vector3.Dot(Position - _waterOrigin, _up);
        }
    }

    static void RequireFinite(Vector3 value)
    {
        if (!CharacterMath.IsFinite(value)) throw new ArgumentException("Fishing coordinates must be finite.");
    }
}
