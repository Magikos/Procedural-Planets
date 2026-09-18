using System;
using System.Collections.Generic;
using UnityEngine;

public enum ActorObservationKind { Food, Water, Threat, Actor }

/// <summary>Bounded observations. Sharing preserves the original expiry rather than refreshing rumours.</summary>
public sealed class ActorKnowledge
{
    public readonly struct Observation
    {
        public readonly ulong Id;
        public readonly ActorObservationKind Kind;
        public readonly Vector3 Position;
        public readonly double Expires;
        public readonly double Observed;
        public readonly double Emitted;
        public readonly ActorSense Sense;
        public readonly float Uncertainty, Confidence, IdentityConfidence, UncertaintyGrowth;
        public Observation(ulong id, ActorObservationKind kind, Vector3 position, double expires,
            double observed = 0, ActorSense sense = ActorSense.Sight, float uncertainty = 0,
            float confidence = 1, float identityConfidence = 1, float uncertaintyGrowth = 0, double emitted = -1)
        { Id = id; Kind = kind; Position = position; Expires = expires; Observed = observed; Sense = sense;
          Uncertainty = uncertainty; Confidence = confidence; IdentityConfidence = identityConfidence; UncertaintyGrowth = uncertaintyGrowth;
          Emitted = emitted < 0 ? observed : emitted; }
        public Observation At(double now)
        {
            float age = (float)Math.Max(0, now - Observed);
            float remaining = Mathf.Clamp01((float)((Expires - now) / Math.Max(.001, Expires - Observed)));
            return new(Id, Kind, Position, Expires, Observed, Sense, Uncertainty + age * UncertaintyGrowth,
                Confidence * remaining, IdentityConfidence * remaining, UncertaintyGrowth, Emitted);
        }
    }
    readonly List<Observation> _items = new();
    public int Count => _items.Count;
    public void Expire(double now)
    {
        if (!double.IsFinite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
        for (int i = _items.Count - 1; i >= 0; i--) if (_items[i].Expires <= now) _items.RemoveAt(i);
    }
    public void Remember(ulong id, ActorObservationKind kind, Vector3 position, double now, double lifetime)
    {
        if (id == 0 || !Enum.IsDefined(typeof(ActorObservationKind), kind) ||
            !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z) ||
            !double.IsFinite(lifetime) || lifetime <= 0 || !double.IsFinite(now + lifetime))
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        Remember(new Observation(id, kind, position, now + lifetime, now), now);
    }
    public void Remember(Observation observation, double now)
    {
        if (observation.Id == 0 || !Enum.IsDefined(typeof(ActorObservationKind), observation.Kind) ||
            !float.IsFinite(observation.Position.x) || !float.IsFinite(observation.Position.y) || !float.IsFinite(observation.Position.z) ||
            !double.IsFinite(observation.Observed) || observation.Observed < 0 || observation.Observed > now ||
            !double.IsFinite(observation.Emitted) || observation.Emitted < 0 || observation.Emitted > observation.Observed ||
            !double.IsFinite(observation.Expires) || observation.Expires <= observation.Observed ||
            !float.IsFinite(observation.Uncertainty) || observation.Uncertainty < 0 ||
            !float.IsFinite(observation.UncertaintyGrowth) || observation.UncertaintyGrowth < 0 ||
            !float.IsFinite(observation.Confidence) || observation.Confidence < 0 || observation.Confidence > 1 ||
            !float.IsFinite(observation.IdentityConfidence) || observation.IdentityConfidence < 0 || observation.IdentityConfidence > 1)
            throw new ArgumentOutOfRangeException(nameof(observation));
        Expire(now);
        if (observation.Expires > now) Merge(observation, now);
    }
    void Merge(Observation observation, double now)
    {
        for (int i = 0; i < _items.Count; i++)
            if (_items[i].Id == observation.Id && _items[i].Kind == observation.Kind)
            {
                var previous = _items[i].At(now); var incoming = observation.At(now);
                // A fresh visual fix locates the actor itself. A strong trail may be somewhere the actor left.
                if (_items[i].Sense == ActorSense.Sight && observation.Sense != ActorSense.Sight && observation.Observed <= _items[i].Observed) return;
                if (observation.Sense == ActorSense.Sight && (observation.Observed > _items[i].Observed ||
                    _items[i].Sense != ActorSense.Sight && observation.Observed >= _items[i].Observed))
                { _items[i] = observation; return; }
                if (observation.Sense == ActorSense.Smell && _items[i].Sense == ActorSense.Smell && observation.Emitted > _items[i].Emitted)
                { _items[i] = observation; return; }
                if (incoming.Confidence / (1 + incoming.Uncertainty) > previous.Confidence / (1 + previous.Uncertainty) ||
                    observation.Observed >= _items[i].Observed && observation.Expires > _items[i].Expires && incoming.Uncertainty <= previous.Uncertainty)
                    _items[i] = observation;
                return;
            }
        if (_items.Count == 32)
        {
            // Reserve room for both moving contacts and useful places when memory is full. Unused
            // room remains shared. A 90-second water memory must not reject a fresh 12-second predator.
            bool incomingContact = IsContact(observation.Kind);
            int contacts = 0;
            foreach (var item in _items) if (IsContact(item.Kind)) contacts++;
            bool evictContact = incomingContact ? contacts >= 24 : _items.Count - contacts < 8;
            int oldest = -1;
            for (int i = 0; i < _items.Count; i++)
                if (IsContact(_items[i].Kind) == evictContact && (oldest < 0 || _items[i].Expires < _items[oldest].Expires)) oldest = i;
            if (oldest < 0 || incomingContact == evictContact && _items[oldest].Expires >= observation.Expires) return;
            _items.RemoveAt(oldest);
        }
        _items.Add(observation);
    }
    static bool IsContact(ActorObservationKind kind) => kind is ActorObservationKind.Actor or ActorObservationKind.Threat;
    public void ShareWith(ActorKnowledge destination, double now)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        Expire(now); destination.Expire(now);
        if (destination == this) return;
        foreach (var observation in _items) destination.Merge(observation, now);
    }
    public bool TryGet(ulong id, ActorObservationKind kind, double now, out Observation observation)
    {
        Expire(now);
        foreach (var item in _items)
            if (item.Id == id && item.Kind == kind) { observation = item.At(now); return true; }
        observation = default; return false;
    }
    public void Forget(ulong id, ActorObservationKind kind) => _items.RemoveAll(item => item.Id == id && item.Kind == kind);
    public bool Knows(ulong id, ActorObservationKind kind, double now)
    {
        Expire(now);
        foreach (var observation in _items) if (observation.Id == id && observation.Kind == kind) return true;
        return false;
    }
    public bool ThreatNear(Vector3 position, float radius, double now)
    {
        Expire(now);
        foreach (var observation in _items)
            if (observation.Kind == ActorObservationKind.Threat && Vector3.Distance(position, observation.Position) <= radius) return true;
        return false;
    }
}
