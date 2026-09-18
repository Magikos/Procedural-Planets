using System;
using UnityEngine;

/// <summary>A bounded presentation pool; the authority simulation does not register grass sources.</summary>
public sealed class CreatureGrassInteraction : IDisposable
{
    public const int Capacity = 3;
    public const float RangeMeters = 30f;
    readonly Source[] _sources = new Source[Capacity];
    readonly Candidate[] _nearest = new Candidate[Capacity];
    Vector3 _observer;
    int _count;
    bool _visible;

    sealed class Source : IGrassInteractor
    {
        public ulong Id;
        public Vector3 WorldPosition { get; set; }
        public float Radius { get; set; }
        public float Strength => .7f;
        public float ReleaseSeconds => .65f;
        public bool IsActive => true;
    }

    struct Candidate
    {
        public ulong Id;
        public Vector3 Position;
        public float DistanceSquared, Radius;
    }

    public void Begin(Vector3 observer, bool visible)
    {
        _observer = observer;
        _visible = visible;
        _count = 0;
    }

    public void Consider(in CreatureResidencyService.LiveCreature creature, CreatureSpeciesDto species)
    {
        if (!_visible || species == null || !creature.Grounded || creature.Swimming || creature.AltitudeMeters > .01f ||
            (species.CruiseAltitudeMeters > 0f && !creature.SupportPoint.HasValue)) return;
        float distance = (creature.Position - _observer).sqrMagnitude;
        if (!float.IsFinite(distance) || distance > RangeMeters * RangeMeters) return;
        int insertion = _count;
        for (int i = 0; i < _count; i++)
        {
            if (_nearest[i].Id == creature.Id.Value) return;
            if (distance < _nearest[i].DistanceSquared ||
                (distance == _nearest[i].DistanceSquared && creature.Id.Value < _nearest[i].Id))
            { insertion = i; break; }
        }
        if (insertion >= Capacity) return;
        for (int i = Mathf.Min(_count, Capacity - 1); i > insertion; i--) _nearest[i] = _nearest[i - 1];
        _nearest[insertion] = new Candidate
        {
            Id = creature.Id.Value,
            Position = creature.SupportPoint ?? creature.Position - creature.Up * (species.BodyHeightMeters * .5f),
            DistanceSquared = distance,
            Radius = Mathf.Clamp(species.BodyHeightMeters * .45f, .15f, 1.2f),
        };
        _count = Mathf.Min(_count + 1, Capacity);
    }

    public void End()
    {
        for (int i = 0; i < Capacity; i++)
        {
            Source source = _sources[i];
            if (source == null) continue;
            bool retained = false;
            for (int j = 0; j < _count; j++) retained |= _nearest[j].Id == source.Id;
            if (!retained) { GrassInteractorRegistry.Unregister(source); _sources[i] = null; }
        }
        for (int i = 0; i < _count; i++)
        {
            Candidate candidate = _nearest[i];
            Source source = null;
            for (int j = 0; j < Capacity; j++)
                if (_sources[j]?.Id == candidate.Id) { source = _sources[j]; break; }
            bool added = source == null;
            if (added)
            {
                source = new Source { Id = candidate.Id };
                for (int j = 0; j < Capacity; j++)
                    if (_sources[j] == null) { _sources[j] = source; break; }
            }
            source.WorldPosition = candidate.Position;
            source.Radius = candidate.Radius;
            if (added) GrassInteractorRegistry.Register(source, priority: -1, slotLimit: GrassInteractorRegistry.MaxInteractors - 2);
        }
    }

    public void Dispose()
    {
        for (int i = 0; i < Capacity; i++)
        {
            if (_sources[i] != null) GrassInteractorRegistry.Unregister(_sources[i]);
            _sources[i] = null;
        }
        _count = 0;
    }
}
