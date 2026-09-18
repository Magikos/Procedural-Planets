using System.Collections.Generic;
using UnityEngine;

// Opt-in for transform-driven objects. Nearby rigidbodies are also discovered by WaterPresentationController.
[DisallowMultipleComponent]
public sealed class WaterInteractor : MonoBehaviour
{
    [Min(.05f)] public float Radius = .45f;
    internal static readonly List<WaterInteractor> Active = new();
    internal readonly WaterContactTracker Contact = new();
    void OnEnable() { Contact.Reset(); if (!Active.Contains(this)) Active.Add(this); }
    void OnDisable() { Active.Remove(this); Contact.Reset(); }
}

public readonly struct WaterImpulse
{
    public readonly Vector3 Position, Normal;
    public readonly float Radius, Strength;
    public readonly bool Splash;
    public WaterImpulse(Vector3 position, Vector3 normal, float radius, float strength, bool splash)
    { Position = position; Normal = normal; Radius = radius; Strength = strength; Splash = splash; }
}

public sealed class WaterContactTracker
{
    Vector3 _lastPosition;
    float _lastDepth, _cooldown;
    ushort _body;
    bool _initialized;

    public void Reset() { _initialized = false; _cooldown = 0f; }

    public bool Step(Vector3 position, WaterSample surface, float radius, float deltaTime, out WaterImpulse impulse)
    {
        impulse = default;
        if (!CharacterMath.IsFinite(position) || !float.IsFinite(radius) || radius <= 0f
            || !float.IsFinite(deltaTime) || deltaTime <= 0f || !float.IsFinite(surface.SignedDepth))
        { Reset(); return false; }
        Vector3 travel = position - _lastPosition;
        bool reset = !_initialized || _body != surface.BodyId || deltaTime > .5f || travel.sqrMagnitude > 400f;
        bool crossing = !reset && _lastDepth + radius < 0f && surface.SignedDepth + radius >= 0f;
        float speed = reset ? 0f : travel.magnitude / deltaTime;
        float lateralSpeed = reset ? 0f : Vector3.ProjectOnPlane(travel, surface.Normal).magnitude / deltaTime;
        _initialized = true;
        _lastPosition = position;
        _lastDepth = surface.SignedDepth;
        _body = surface.BodyId;
        _cooldown = Mathf.Max(0f, _cooldown - deltaTime);
        if (reset || surface.BodyId == 0 || surface.BodyDepth <= .04f) return false;
        bool wake = Mathf.Abs(surface.SignedDepth) < radius + .35f && lateralSpeed > .3f && _cooldown <= 0f;
        if (!crossing && !wake) return false;
        _cooldown = .16f;
        float strength = Mathf.Clamp((crossing ? speed : lateralSpeed) * .12f, .12f, 1f);
        impulse = new WaterImpulse(surface.SurfacePoint, surface.Normal, Mathf.Clamp(radius, .1f, 4f), strength, crossing);
        return true;
    }
}
