using System;
using System.Collections.Generic;
using UnityEngine;

public enum ActorSense { Sight, Hearing, Smell }

/// <summary>Shared defaults. Authoring profiles override only deliberate species differences.</summary>
public sealed record ActorPerceptionProfile(
    float DaySight = 22f, float NightSight = 6f, float ViewAngle = 220f,
    float HearingRange = 16f, float HearingThreshold = .05f,
    float SmellRange = 14f, float SmellThreshold = .05f,
    float SightResolution = .15f, float HearingResolution = 5f, float SmellResolution = 4f,
    float MemorySeconds = 12f, float UncertaintyGrowth = .8f)
{
    public ActorPerceptionProfile Validate()
    {
        foreach (float value in new[] { DaySight, NightSight, ViewAngle, HearingRange, HearingThreshold,
            SmellRange, SmellThreshold, SightResolution, HearingResolution, SmellResolution, MemorySeconds, UncertaintyGrowth })
            if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (ViewAngle > 360 || HearingThreshold > 1 || SmellThreshold > 1 || MemorySeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(ViewAngle));
        return this;
    }
}

/// <summary>A sampled event, never a live target reference. Sound and scent retain their emission position.</summary>
public readonly struct ActorStimulus
{
    public readonly ulong Id;
    public readonly ActorObservationKind Kind;
    public readonly ActorSense Sense;
    public readonly Vector3 Position;
    public readonly float Strength;
    public readonly double Emitted, Expires;
    public ActorStimulus(ulong id, ActorObservationKind kind, ActorSense sense, Vector3 position,
        float strength, double emitted, double expires)
    { Id = id; Kind = kind; Sense = sense; Position = position; Strength = strength; Emitted = emitted; Expires = expires; }
}

/// <summary>Authority-owned sense evaluation. The host supplies light and occlusion from its world.</summary>
public sealed class ActorPerception
{
    readonly Dictionary<(ulong, ActorObservationKind), double> _searchedScent = new();
    public ActorPerceptionProfile Profile { get; }
    public ActorPerception(ActorPerceptionProfile profile) => Profile = (profile ?? throw new ArgumentNullException(nameof(profile))).Validate();

    /// <summary>Cheap broad-phase gate before a host performs an occlusion query.</summary>
    public bool WithinRange(Vector3 position, Vector3 forward, Vector3 up, float light, in ActorStimulus stimulus)
    {
        float range = stimulus.Sense == ActorSense.Sight
            ? Mathf.Lerp(Profile.NightSight, Profile.DaySight, Mathf.Clamp01(light))
            : stimulus.Sense == ActorSense.Hearing ? Profile.HearingRange : Profile.SmellRange;
        if (range <= 0 || stimulus.Strength <= 0 ||
            (stimulus.Position - position).sqrMagnitude > range * range * stimulus.Strength) return false;
        if (stimulus.Sense != ActorSense.Sight) return true;
        Vector3 direction = Vector3.ProjectOnPlane(stimulus.Position - position, up);
        return forward.sqrMagnitude >= .001f && up.sqrMagnitude >= .001f &&
            (direction.sqrMagnitude <= .001f ||
             Vector3.Angle(Vector3.ProjectOnPlane(forward, up), direction) <= Profile.ViewAngle * .5f);
    }

    public bool Observe(ulong observer, Vector3 position, Vector3 forward, Vector3 up, float light,
        bool occluded, in ActorStimulus stimulus, double now, ActorKnowledge memory)
    {
        if (memory == null) throw new ArgumentNullException(nameof(memory));
        if (observer == 0 || stimulus.Id == 0 || !Finite(position) || !Finite(forward) || !Finite(up) ||
            !Finite(stimulus.Position) || !float.IsFinite(light) || !float.IsFinite(stimulus.Strength) || stimulus.Strength < 0 ||
            !double.IsFinite(now) || now < 0 || !double.IsFinite(stimulus.Emitted) || !double.IsFinite(stimulus.Expires) ||
            stimulus.Expires <= stimulus.Emitted || !Enum.IsDefined(typeof(ActorSense), stimulus.Sense))
            throw new ArgumentOutOfRangeException(nameof(stimulus));
        if (stimulus.Emitted > now || stimulus.Expires <= now) return false;
        if (!WithinRange(position, forward, up, light, stimulus)) return false;
        if (stimulus.Sense == ActorSense.Smell && _searchedScent.TryGetValue((stimulus.Id, stimulus.Kind), out var searched) &&
            stimulus.Emitted <= searched) return false;
        float distance = Vector3.Distance(position, stimulus.Position);
        float range, resolution, threshold;
        float strength = stimulus.Strength;
        switch (stimulus.Sense)
        {
            case ActorSense.Sight:
                if (occluded || forward.sqrMagnitude < .001f || up.sqrMagnitude < .001f) return false;
                Vector3 direction = Vector3.ProjectOnPlane(stimulus.Position - position, up);
                if (direction.sqrMagnitude > .001f && Vector3.Angle(Vector3.ProjectOnPlane(forward, up), direction) > Profile.ViewAngle * .5f) return false;
                range = Mathf.Lerp(Profile.NightSight, Profile.DaySight, Mathf.Clamp01(light));
                resolution = Profile.SightResolution; threshold = 0;
                break;
            case ActorSense.Hearing:
                range = Profile.HearingRange; resolution = Profile.HearingResolution; threshold = Profile.HearingThreshold;
                if (occluded) strength *= .35f;
                break;
            default:
                range = Profile.SmellRange; resolution = Profile.SmellResolution; threshold = Profile.SmellThreshold;
                strength *= Mathf.Clamp01((float)((stimulus.Expires - now) / (stimulus.Expires - stimulus.Emitted)));
                break;
        }
        float effectiveRange = range * Mathf.Sqrt(strength);
        if (effectiveRange <= 0 || distance > effectiveRange) return false;
        float signal = strength * Mathf.Clamp01(1f - distance / (effectiveRange + .001f));
        if (signal < threshold) return false;
        float confidence = Mathf.Clamp01(.35f + signal * .65f);
        float uncertainty = resolution * (.25f + .75f * Mathf.Clamp01(distance / effectiveRange));
        Vector3 estimate = Estimate(stimulus.Position, uncertainty, observer);
        // Visual recognition is stronger than a distant noise or old scent. Correlation ID is not recognition.
        float identity = stimulus.Sense == ActorSense.Sight ? confidence : confidence * .7f;
        double expiry = now + Profile.MemorySeconds;
        // Re-sampling an old event must not renew its memory forever.
        double observed = stimulus.Sense == ActorSense.Hearing ? stimulus.Emitted : now;
        expiry = Math.Min(expiry, observed + Profile.MemorySeconds);
        if (expiry <= now) return false;
        memory.Remember(new ActorKnowledge.Observation(stimulus.Id, stimulus.Kind, estimate, expiry,
            observed, stimulus.Sense, uncertainty, confidence, identity, Profile.UncertaintyGrowth, stimulus.Emitted), now);
        return true;
    }
    public void SearchReachedScent(Vector3 position, Vector3 up, ActorKnowledge.Observation observation, ActorKnowledge memory)
    {
        if (memory == null) throw new ArgumentNullException(nameof(memory));
        if (!Finite(position) || !Finite(up) || up.sqrMagnitude < .001f) throw new ArgumentOutOfRangeException(nameof(position));
        if (observation.Sense != ActorSense.Smell ||
            Vector3.ProjectOnPlane(position - observation.Position, up).magnitude > Mathf.Max(.6f, observation.Uncertainty)) return;
        var key = (observation.Id, observation.Kind);
        if (!_searchedScent.ContainsKey(key) && _searchedScent.Count >= 32)
        {
            var oldest = key; double time = double.MaxValue;
            foreach (var item in _searchedScent) if (item.Value < time) { oldest = item.Key; time = item.Value; }
            _searchedScent.Remove(oldest);
        }
        _searchedScent[key] = Math.Max(_searchedScent.TryGetValue(key, out var previous) ? previous : 0, observation.Emitted);
        memory.Forget(observation.Id, observation.Kind);
    }
    static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    static Vector3 Estimate(Vector3 position, float radius, ulong observer)
    {
        if (radius <= .001f) return position;
        // Quantization gives an area estimate without tick-to-tick random jitter or averaging toward exact truth.
        float cell = radius * 1.1f;
        float offset = (observer % 17) / 17f * cell;
        return new Vector3(Mathf.Round((position.x + offset) / cell) * cell - offset,
            Mathf.Round((position.y + offset) / cell) * cell - offset,
            Mathf.Round((position.z + offset) / cell) * cell - offset);
    }
}
