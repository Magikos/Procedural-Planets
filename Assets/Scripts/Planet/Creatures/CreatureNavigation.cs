using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Authority-side route following. Does not move transforms or drive animation.</summary>
public sealed class CreatureNavigation
{
    readonly NavMeshPath _path = new();
    readonly NavMeshPath _probe = new();
    Vector3[] _corners = Array.Empty<Vector3>();
    Vector3 _goal, _progressPosition;
    int _corner;
    float _repath, _stalled;
    bool _initialized, _escaping;
    Vector3 _escapeGoal;
    public string Status { get; private set; } = "Idle";
    public bool Failed { get; private set; }

    public bool TryApproach(Vector3 position, Vector3 approach, Vector3 target, out Vector3 accepted)
    {
        accepted = target;
        if (!TryRoute(position, target, out _, out float directLength) ||
            !TryRoute(position, approach, out var point, out float approachLength) ||
            approachLength > directLength * 1.5f + 2f) return false;
        accepted = point; return true;
    }
    bool TryRoute(Vector3 position, Vector3 goal, out Vector3 end, out float length)
    {
        length = 0; end = goal;
        if (!TryPoint(position, out var start) || !TryPoint(goal, out end) ||
            !NavMesh.CalculatePath(start, end, NavMesh.AllAreas, _probe) || _probe.status != NavMeshPathStatus.PathComplete) return false;
        var corners = _probe.corners;
        for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
        return true;
    }

    public void Reset()
    {
        _escaping = false;
        ResetRoute();
    }
    void ResetRoute()
    {
        _initialized = Failed = false; _repath = _stalled = 0f;
        _corners = Array.Empty<Vector3>(); Status = "Idle";
    }

    public bool Steer(Vector3 position, Vector3 goal, float dt, out Vector3 direction)
    {
        direction = Vector3.zero;
        if (!float.IsFinite(dt) || dt <= 0f) return false;
        bool changed = !_initialized || (goal - _goal).sqrMagnitude > 1f;
        _repath -= dt;
        if (changed || _repath <= 0f)
        {
            _repath = .5f; _goal = goal;
            if (!_initialized) { _progressPosition = position; _initialized = true; }
            if (!TryPoint(position, out var start) || !TryPoint(goal, out var end) ||
                !NavMesh.CalculatePath(start, end, NavMesh.AllAreas, _path) || _path.status != NavMeshPathStatus.PathComplete)
            { Failed = true; Status = "Unreachable"; return false; }
            _corners = _path.corners; _corner = _corners.Length > 1 ? 1 : 0;
            Failed = false;
        }
        if (Failed || _corners.Length == 0) return false;
        // A proximity threshold alone cuts across convex corners and pins the mover against the boundary.
        if (TryPoint(position, out var current))
            while (_corner < _corners.Length - 1 &&
                !NavMesh.Raycast(current, _corners[_corner + 1], out _, NavMesh.AllAreas)) _corner++;
        direction = Vector3.ProjectOnPlane(_corners[_corner] - position, Vector3.up);
        if (_corner == _corners.Length - 1 && direction.magnitude < .2f) { Status = "Arrived"; _stalled = 0; return false; }
        if (Vector3.Distance(position, _progressPosition) >= .2f)
        { _progressPosition = position; _stalled = 0f; }
        else _stalled += dt;
        if (_stalled >= 4f) { Failed = true; Status = "No progress"; return false; }
        Status = "Following"; direction.Normalize(); return true;
    }

    public bool Escape(Vector3 position, Vector3 threat, Vector3 forward, float dt, out Vector3 direction)
    {
        direction = Vector3.zero;
        if (!float.IsFinite(dt) || dt <= 0f) return false;
        if (_escaping && !Failed && Vector3.Distance(position, _escapeGoal) > 1f &&
            Vector3.Distance(_escapeGoal, threat) > Vector3.Distance(position, threat))
        {
            if (Steer(position, _escapeGoal, dt, out direction)) return true;
            if (!Failed) return false;
        }
        Vector3 previous = _escapeGoal;
        bool retry = _escaping && Failed;
        Vector3 away = Vector3.ProjectOnPlane(position - threat, Vector3.up).normalized;
        if (away.sqrMagnitude < .01f) away = forward;
        if (!TryPoint(position, out var start)) { Failed = true; Status = "Outside navigation"; return false; }
        for (int i = 0; i < 11; i++)
        {
            float angle = i == 0 ? 0 : ((i + 1) / 2) * 30f * (i % 2 == 0 ? -1 : 1);
            Vector3 candidate = position + Quaternion.AngleAxis(angle, Vector3.up) * away * 6f;
            if (!NavMesh.SamplePosition(candidate, out var end, 2f, NavMesh.AllAreas) ||
                Vector3.Distance(end.position, threat) <= Vector3.Distance(position, threat) + .5f ||
                retry && Vector3.Distance(end.position, previous) < 2f ||
                !NavMesh.CalculatePath(start, end.position, NavMesh.AllAreas, _path) || _path.status != NavMeshPathStatus.PathComplete) continue;
            // Route trials never advance the stuck timer. One accepted route gets one tick.
            ResetRoute(); _escapeGoal = end.position; _escaping = true;
            return Steer(position, _escapeGoal, dt, out direction);
        }
        Failed = true; Status = "No escape route"; return false;
    }

    public static bool TryPoint(Vector3 position, out Vector3 point)
    {
        bool found = NavMesh.SamplePosition(position, out var hit, .75f, NavMesh.AllAreas);
        point = hit.position;
        // Do not project an elevated refuge onto the reachable ground below it.
        return found && Mathf.Abs(point.y - position.y) <= .45f;
    }

    public static Vector3 Constrain(Vector3 from, Vector3 to)
    {
        if (!TryPoint(from, out var start)) return from;
        if (NavMesh.Raycast(start, to, out var hit, NavMesh.AllAreas))
        {
            // Preserve motion along a wall instead of discarding the whole displacement at first contact.
            Vector3 slide = Vector3.ProjectOnPlane(to - hit.position, hit.normal);
            if (slide.sqrMagnitude < .000001f || hit.normal.sqrMagnitude < .01f) return hit.position;
            Vector3 inside = Vector3.MoveTowards(hit.position, start, .005f);
            Vector3 destination = inside + slide;
            return NavMesh.Raycast(inside, destination, out var boundary, NavMesh.AllAreas) ? boundary.position : destination;
        }
        return TryPoint(to, out var end) ? end : from;
    }

    public static bool ClearContact(Vector3 from, Vector3 to) =>
        TryPoint(from, out var start) && TryPoint(to, out var end) &&
        !NavMesh.Raycast(start, end, out _, NavMesh.AllAreas);
}
