using System;
using UnityEngine;

/// <summary>A group's shelter or one actor's resting site. Ownership never treats group zero as an alliance.</summary>
public sealed class ActorHomeSite
{
    public ulong Id { get; }
    public Vector3 Position { get; }
    public Vector3 Up { get; }
    public float Radius { get; }
    public uint OwnerGroup { get; }
    public ulong OwnerActor { get; }
    public bool Sheltered { get; }
    public int Capacity => _residents.Length;
    public ActorKnowledge Knowledge { get; } = new();
    readonly ulong[] _residents;
    public bool Gathering { get; private set; }
    public double GatherUntil { get; private set; }
    double _gatherStarted, _departUntil;
    public ActorHomeSite(ulong id, Vector3 position, Vector3 up, float radius, int capacity,
        uint ownerGroup, ulong ownerActor, bool sheltered)
    {
        if (id == 0 || !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z) ||
            !float.IsFinite(up.x) || !float.IsFinite(up.y) || !float.IsFinite(up.z) || up.sqrMagnitude < .001f ||
            !float.IsFinite(radius) || radius <= 0 || capacity < 1 || capacity > 64 ||
            (ownerGroup == 0) == (ownerActor == 0)) throw new ArgumentOutOfRangeException(nameof(capacity));
        Id = id; Position = position; Up = up.normalized; Radius = radius; OwnerGroup = ownerGroup;
        OwnerActor = ownerActor; Sheltered = sheltered; _residents = new ulong[capacity];
    }
    public bool TryReserve(ulong actor, uint group)
    {
        if (actor == 0 || !(OwnerGroup != 0 ? ActorGroup.Allied(OwnerGroup, group) : actor == OwnerActor)) return false;
        if (Array.IndexOf(_residents, actor) >= 0) return true;
        int free = Array.IndexOf(_residents, 0ul);
        if (free < 0) return false;
        _residents[free] = actor; return true;
    }
    public void Release(ulong actor)
    { int slot = Array.IndexOf(_residents, actor); if (slot >= 0) _residents[slot] = 0; }
    public bool TryRestPosition(ulong actor, out Vector3 position)
    {
        position = Position;
        int slot = actor == 0 ? -1 : Array.IndexOf(_residents, actor);
        if (slot < 0) return false;
        if (Capacity == 1) return true;
        Vector3 tangent = Vector3.Cross(Up, Mathf.Abs(Up.z) < .9f ? Vector3.forward : Vector3.right).normalized;
        position += Quaternion.AngleAxis(360f * slot / Capacity, Up) * tangent * Radius * .7f;
        return true;
    }
    public void UpdateGather(double now, bool requested, int willing, int present, bool safe, double waitSeconds)
    {
        if (!double.IsFinite(now) || now < 0 || !double.IsFinite(waitSeconds) || waitSeconds <= 0 ||
            !double.IsFinite(now + waitSeconds) || willing < 0 || present < 0 || present > willing)
            throw new ArgumentOutOfRangeException(nameof(now));
        if (!safe || OwnerGroup == 0 || willing < 2) { Gathering = false; return; }
        if (Gathering)
        {
            if (now >= GatherUntil || present == willing && now - _gatherStarted >= Math.Min(2d, waitSeconds))
            { Gathering = false; _departUntil = now + 30d; }
            return;
        }
        if (requested && now >= _departUntil)
        { Gathering = true; _gatherStarted = now; GatherUntil = now + waitSeconds; }
    }
}
