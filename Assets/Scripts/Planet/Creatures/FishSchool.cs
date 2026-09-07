using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Water-constrained groups. A single member uses the same swimming rules.</summary>
public sealed class FishSchool
{
    readonly IWaterQueryService _water;
    readonly ushort _body;
    readonly FishSpecies _species;
    readonly EntityId _id;
    readonly Vector3[] _positions, _next, _headings, _normals;
    readonly float[] _depths;
    readonly float _clearance, _speed;
    Vector3 _heading;
    double _seconds;
    public IReadOnlyList<Vector3> Positions { get; }
    public IReadOnlyList<Vector3> Headings { get; }
    public IReadOnlyList<Vector3> Normals { get; }
    public ushort BodyId => _body;
    public bool IsViable { get; private set; } = true;

    public FishSchool(IWaterQueryService water, IReadOnlyList<Vector3> positions, Vector3 forward,
        FishSpecies species = null, EntityId id = default)
    {
        if (positions == null || positions.Count < 1 || positions.Count > 64)
            throw new ArgumentException("A fish group needs between one and 64 initial positions.", nameof(positions));
        if (water == null || !CharacterMath.IsFinite(positions[0]) || !water.TryGetWaterSurface(positions[0], out WaterSample sample))
            throw new ArgumentException("Fish must start in water.", nameof(water));
        if (!CharacterMath.IsFinite(forward) || forward.sqrMagnitude < 0.001f)
            throw new ArgumentException("Fish need a finite initial heading.", nameof(forward));
        _water = water;
        _body = sample.BodyId;
        _species = species;
        _id = id;
        _clearance = species?.Clearance ?? 0.2f;
        _speed = species?.SwimSpeed ?? 1.5f;
        if (!(_speed > 0f) || float.IsInfinity(_speed)) throw new ArgumentException("Fish need a finite positive speed.", nameof(species));
        _heading = forward.normalized;
        _positions = new Vector3[positions.Count];
        _next = new Vector3[positions.Count];
        _headings = new Vector3[positions.Count];
        _normals = new Vector3[positions.Count];
        _depths = new float[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            if (!FishMovement.IsHabitat(water, positions[i], _body, _clearance, species))
                throw new ArgumentException("Every fish must start inside its species habitat and the same water body.", nameof(positions));
            water.TryGetWaterSurface(positions[i], out var local);
            _positions[i] = positions[i];
            _headings[i] = _heading;
            _normals[i] = local.Normal;
            _depths[i] = local.SignedDepth;
        }
        Positions = Array.AsReadOnly(_positions);
        Headings = Array.AsReadOnly(_headings);
        Normals = Array.AsReadOnly(_normals);
    }

    public void Tick(float dt, ThreatRegistry threats, long now)
    {
        if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f || dt > 10f)
            throw new ArgumentOutOfRangeException(nameof(dt));
        if (dt == 0f || !IsViable) return;
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 30f));
        float step = dt / steps;
        for (int tick = 0; tick < steps; tick++)
        {
            _seconds += step;
            Vector3 center = Vector3.zero;
            foreach (Vector3 position in _positions)
            {
                if (!FishMovement.IsHabitat(_water, position, _body, _clearance, _species))
                {
                    IsViable = false; // The population owner removes presentation when water disappears.
                    return;
                }
                center += position;
            }
            center /= _positions.Length;
            _heading = Quaternion.AngleAxis(Mathf.Sin((float)_seconds * 0.3f) * step * 20f, _normals[0]) * _heading;
            bool blocked = false;
            for (int i = 0; i < _positions.Length; i++)
            {
                Vector3 position = _positions[i];
                _water.TryGetWaterSurface(position, out var water);
                _normals[i] = water.Normal;
                Vector3 steering = Vector3.ProjectOnPlane(_heading, water.Normal).normalized +
                    Vector3.ClampMagnitude(center - position, 2f) * 0.5f;
                float desiredDepth = Mathf.Clamp(_depths[i] + Mathf.Sin((float)_seconds * 0.25f + i) * 0.3f,
                    _clearance * 1.5f, water.BodyDepth - _clearance * 1.5f);
                steering += water.Normal * Mathf.Clamp(water.SignedDepth - desiredDepth, -1f, 1f);
                for (int j = 0; j < _positions.Length; j++)
                {
                    Vector3 delta = position - _positions[j];
                    if (j != i && delta.sqrMagnitude > 0.001f && delta.sqrMagnitude < 1f)
                        steering += delta / delta.sqrMagnitude;
                }
                float speed = _speed;
                if (threats != null && threats.TryFindThreat(position, _id, _species?.Faction ?? CreatureFaction.Wildlife,
                    _species?.Awareness ?? 8f, now, out var threat))
                {
                    steering = position - threat.Position;
                    speed *= 2f;
                }
                Vector3 heading = Vector3.RotateTowards(_headings[i], steering.normalized, step * 2f, 0f);
                Vector3 desired = position + heading * (speed * step);
                if (!FishMovement.TryMove(_water, position, desired, _body, _clearance, out _next[i], _species))
                {
                    // Turn within the enclosing clearance sphere before trying forward travel again.
                    blocked = true;
                    heading = Quaternion.AngleAxis(step * 120f, water.Normal) * _headings[i];
                }
                _headings[i] = heading;
            }
            Array.Copy(_next, _positions, _positions.Length);
            if (blocked) _heading = Quaternion.AngleAxis(step * 120f, _normals[0]) * _heading;
        }
    }
}
