using System;
using System.Collections.Generic;
using UnityEngine;

public delegate bool WildlifeAnchorPlacement(AmbientSwarmProfile profile, Vector3 observer, out Vector3 position);

/// <summary>A presentation snapshot. Rendering cannot change simulation state through it.</summary>
public readonly struct AmbientWildlifePose
{
    public readonly EntityId Id;
    public readonly AmbientSwarmKind Kind;
    public readonly Vector3 Position, Forward;
    public readonly WildlifeActivity Activity;
    public readonly float Age, Alpha;
    public readonly uint Seed;
    public readonly int ArtVariant;
    public readonly bool Retiring;
    public readonly Vector3? SupportPoint;
    public bool Resting => Activity == WildlifeActivity.Rest;
    public bool Fleeing => Activity == WildlifeActivity.Escape;

    public AmbientWildlifePose(EntityId id, AmbientSwarmKind kind, Vector3 position, Vector3 forward,
        WildlifeActivity activity, float age, float alpha, uint seed, int artVariant, bool retiring, Vector3? supportPoint = null)
    {
        Id = id; Kind = kind; Position = position; Forward = forward; Activity = activity;
        Age = age; Alpha = alpha; Seed = seed; ArtVariant = artVariant; Retiring = retiring; SupportPoint = supportPoint;
    }
}

/// <summary>Transient bird and pollinator authority. No scene objects, animation, camera, or Unity clock.</summary>
public sealed partial class AmbientWildlifeSimulation
{
    sealed class Animal
    {
        public readonly EntityId Id;
        public readonly WildlifeBrain Brain = new();
        public readonly WildlifeLandingReservation Landing;
        public readonly AmbientSwarmProfile Profile;
        public readonly int ArtVariant;
        public Vector3 Position, Forward, From, Target;
        public uint Seed;
        public float Age, Alpha, Travel, Duration, NextLanding;
        public bool Retiring, HasTarget;
        public ulong PreviousSite;
        public Animal(EntityId id, AmbientSwarmProfile profile, Vector3 position, uint seed, Vector3 up)
        {
            Id = id; Profile = profile; Position = position; Seed = seed;
            ArtVariant = (int)(seed % 4);
            Landing = new WildlifeLandingReservation(id);
            Forward = Quaternion.AngleAxis(ScatterHash.To01(seed) * 360f, up) * CharacterMath.ArbitraryTangent(up);
            NextLanding = 15f + ScatterHash.To01(seed) * 20f;
        }
        public AmbientWildlifePose Pose => new(Id, Profile.Kind, Position, Forward, Brain.Activity,
            Age, Alpha, Seed, ArtVariant, Retiring, Landing.Active ? Landing.Target.Position : null);
        public void ReleaseTarget()
        {
            if (Landing.Active) PreviousSite = Landing.Target.Id;
            Landing.Release();
            HasTarget = false;
        }
    }

    readonly IPlanetSurfaceSampler _surface;
    readonly ThreatRegistry _threats;
    readonly WildlifeLandingTargets _sites;
    readonly WildlifeLandingTargets _flowers = new();
    readonly BirdLandingGround _ground;
    readonly WildlifeAnchorPlacement _place;
    readonly AmbientSwarmProfile[] _profiles;
    readonly Vector3 _center;
    readonly float _seaRadius;
    readonly CharacterWaterFloor _water;
    readonly EntityIdAllocator _ids = new(EntityId.AmbientWildlifeOwner);
    readonly List<Animal> _insects = new();
    readonly List<AmbientWildlifePose> _poses = new();
    readonly System.Collections.ObjectModel.ReadOnlyCollection<AmbientWildlifePose> _readOnlyPoses;
    uint _sequence;
    float _spawnIn;
    public IReadOnlyList<AmbientWildlifePose> Poses => _readOnlyPoses;
    public int FlockCount => _flocks.Count;

    public AmbientWildlifeSimulation(IPlanetSurfaceSampler surface, ThreatRegistry threats,
        WildlifeLandingTargets sites, Vector3 center, float seaRadius, WildlifeAnchorPlacement place,
        AmbientSwarmProfile[] profiles = null, CharacterWaterFloor water = default)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _place = place ?? throw new ArgumentNullException(nameof(place));
        _threats = threats; _sites = sites; _center = center; _seaRadius = seaRadius;
        _water = water;
        _profiles = profiles ?? AmbientSwarmProfile.Defaults;
        _ground = new BirdLandingGround(surface, center, seaRadius, water);
        _readOnlyPoses = _poses.AsReadOnly();
    }

    public void PublishFlowers(IReadOnlyList<WildlifeLandingTarget> flowers) => _flowers.Publish(flowers);

    public void Tick(Vector3 observer, float sun, long now, float dt)
    {
        if (!CharacterMath.IsFinite(observer) || !float.IsFinite(sun) || !float.IsFinite(dt) || dt < 0f)
            throw new ArgumentOutOfRangeException(nameof(dt));
        // Limit catch-up after a long frame. Normal frames use at most 1/30-second movement steps.
        float remaining = Mathf.Min(dt, 1f);
        while (remaining > 0f)
        {
            float step = Mathf.Min(remaining, 1f / 30f);
            TickPollinators(observer, sun, now, step);
            TickFlocks(observer, sun, now, step);
            remaining = Mathf.Max(0f, remaining - step);
        }
        _poses.Clear();
        foreach (Animal insect in _insects) _poses.Add(insect.Pose);
        foreach (Flock flock in _flocks)
            foreach (Animal bird in flock.Birds) _poses.Add(bird.Pose);
    }

    public void Clear()
    {
        foreach (Animal insect in _insects) insect.ReleaseTarget();
        foreach (Flock flock in _flocks)
            foreach (Animal bird in flock.Birds) bird.ReleaseTarget();
        _insects.Clear(); _flocks.Clear(); _flowers.Clear(); _poses.Clear(); _spawnIn = 0f;
    }

    bool Threatened(Animal animal, float range, long now, out ThreatSource threat)
    {
        threat = default;
        return _threats != null && _threats.TryFindThreat(animal.Position, animal.Id,
            CreatureFaction.Wildlife, range, now, out threat);
    }

    bool Move(Animal animal, Vector3 next, float clearance, float dt)
    {
        Vector3 up = (next - _center).normalized;
        if (!_surface.TryGetSurfaceRadius(up, out float floor) || !float.IsFinite(floor)) return false;
        float water = Mathf.Max(_seaRadius, _water.RadiusAt(next, up));
        next = _center + up * Mathf.Max((next - _center).magnitude, Mathf.Max(floor, water) + clearance);
        if (CharacterMath.TryProjectOntoTangent(next - animal.Position, up, out Vector3 heading))
            animal.Forward = Vector3.Slerp(animal.Forward, heading, 1f - Mathf.Exp(-6f * dt));
        animal.Position = next;
        return true;
    }

    bool ValidSite(WildlifeLandingTarget target)
    {
        Vector3 up = (target.Position - _center).normalized;
        return Vector3.Dot(up, target.Normal) >= 0.866f &&
            _surface.TryGetSurfaceRadius(up, out float floor) &&
            (target.Position - _center).magnitude >= floor - 0.01f &&
            (target.Position - _center).magnitude > Mathf.Max(_seaRadius, _water.RadiusAt(target.Position, up)) + 0.01f;
    }

    void Escape(Animal animal, ThreatSource threat, float speed, float clearance, float dt)
    {
        animal.ReleaseTarget();
        Vector3 up = (animal.Position - _center).normalized;
        Vector3 away = Vector3.ProjectOnPlane(animal.Position - threat.Position, up);
        if (away.sqrMagnitude < 0.001f) away = animal.Forward;
        Move(animal, animal.Position + (away.normalized + up * 0.4f).normalized * (speed * dt), clearance, dt);
    }
}
