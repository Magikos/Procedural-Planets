using System.Collections.Generic;
using System.Text;
using UnityEngine;

public sealed partial class PredatorEncounterPrototype
{
    public enum PerceptionScenario { Ecosystem, WaterApproach, NightVision, HiddenSound, ScentTracking }
    public PerceptionScenario Scenario;
    [Header("Perception (profiles apply on restart)")]
    public CreaturePerceptionSettings DeerPerception, WolfPerception;
    [Tooltip("0 is darkness; 1 is daylight. Independent of camera exposure for repeatable tests.")]
    [Range(0, 1)] public float PerceptionLight = 1f;
    public bool ShowPerception = true;
    [Tooltip("Diagnostic only: disable other senses in the night, sound, and scent scenarios. Leave off for normal pursuit tests.")]
    public bool IsolateTestSense;
    readonly List<ActorStimulus> _stimuli = new();

    public void TestPerception(PerceptionScenario scenario)
    {
        Scenario = scenario;
        Ecosystem = scenario == PerceptionScenario.Ecosystem;
        PerceptionLight = scenario == PerceptionScenario.NightVision ? 0 : 1;
        WaterPosition = new(0, 0, -1);
        ResetScenario();
        if (Ecosystem) return;
        var deer = _actors[0];
        Place(deer.View.Root, new(0, 0, -1), deer.Height * .5f);
        Place(_wolf.View.Root, new(7, 0, -1), _wolf.Height * .5f);
        deer.View.Root.forward = Vector3.right; _wolf.View.Root.forward = Vector3.left;
        DeerHunger = .1f; DeerThirst = .9f; WolfHunger = .1f; WolfThirst = .9f;
        if (scenario == PerceptionScenario.NightVision && IsolateTestSense)
        {
            // Isolate vision. Authored night ranges differ; other senses cannot mask the result.
            deer.Perception = new(deer.Perception.Profile with { HearingRange = 0, SmellRange = 0 });
            _wolf.Perception = new(_wolf.Perception.Profile with { HearingRange = 0, SmellRange = 0 });
        }
        if (scenario == PerceptionScenario.HiddenSound)
        {
            Place(deer.View.Root, new(-7, 0, -1), deer.Height * .5f);
            Place(_wolf.View.Root, new(-1, 0, -1), _wolf.Height * .5f);
            if (IsolateTestSense)
            {
                deer.Perception = new(deer.Perception.Profile with { SmellRange = 0 });
                _wolf.Perception = new(_wolf.Perception.Profile with { SmellRange = 0 });
            }
            Emit(_wolf, ActorSense.Hearing, 2f, .8f);
        }
        if (scenario == PerceptionScenario.ScentTracking)
        {
            Place(_wolf.View.Root, new(-8, 0, -1), _wolf.Height * .5f);
            if (IsolateTestSense)
            {
                deer.Perception = new(deer.Perception.Profile with { DaySight = 0, NightSight = 0, HearingRange = 0 });
                _wolf.Perception = new(_wolf.Perception.Profile with { DaySight = 0, NightSight = 0, HearingRange = 0 });
            }
            WolfHunger = .8f; WolfThirst = .1f;
        }
        _stimuli.Clear();
        if (scenario == PerceptionScenario.HiddenSound) Emit(_wolf, ActorSense.Hearing, 2f, .8f);
        FocusActor(0); UpdateStatus();
    }

    void ObservePerception()
    {
        _stimuli.RemoveAll(s => s.Expires <= _elapsed);
        foreach (var a in _actors)
        {
            if (!Available(a)) continue;
            if (_elapsed >= a.NextScent)
            {
                a.NextScent = _elapsed + 2f;
                Emit(a, ActorSense.Smell, a.Vitals.Bleeding > 0 ? 2f : 1f, 45f);
            }
            if (_elapsed >= a.NextSound && (a.Velocity.sqrMagnitude > .01f || a.Brain.Behaviour is CreatureBehaviour.Attack or CreatureBehaviour.Threaten))
            {
                a.NextSound = _elapsed + .65f;
                float strength = a.Brain.Behaviour is CreatureBehaviour.Attack or CreatureBehaviour.Threaten ? 2f :
                    Mathf.Clamp(a.Velocity.magnitude / 4f, .1f, 1.5f);
                Emit(a, ActorSense.Hearing, strength, .8f);
            }
        }
        Physics.SyncTransforms();
        foreach (var a in _actors)
        {
            if (!Available(a)) continue;
            a.Knowledge.Expire(_elapsed);
            a.DirectSight.Clear();
            foreach (var other in _actors)
                if (other != a && Observation(a, other, out var known))
                    a.Perception.SearchReachedScent(a.View.Root.position, Vector3.up, known, a.Knowledge);
            foreach (var other in _actors)
                if (other != a && Available(other))
                    Sense(a, new(other.Id, ActorObservationKind.Actor, ActorSense.Sight, other.View.Root.position, 1, _elapsed, _elapsed + .1));
            foreach (var stimulus in _stimuli) if (stimulus.Id != a.Id) Sense(a, stimulus);
            foreach (var source in _sources)
            {
                if (!SourceAvailable(source, a.Diet)) continue;
                var kind = source.Stock.ThirstPerUnit > 0 ? ActorObservationKind.Water : ActorObservationKind.Food;
                var position = source.Owner?.View.Root.position ?? source.Position + Vector3.up * .1f;
                Sense(a, new(source.Id, kind, ActorSense.Sight, position, 1, _elapsed, _elapsed + .1));
                Sense(a, new(source.Id, kind, ActorSense.Smell, position, source.Owner != null ? 2 : .5f, _elapsed, _elapsed + 1));
            }
        }
    }
    void Emit(Actor a, ActorSense sense, float strength, float lifetime)
    {
        // Bounded fixture event history. A world host can spatially index these same value events.
        if (_stimuli.Count >= 512) _stimuli.RemoveAt(0);
        _stimuli.Add(new(a.Id, ActorObservationKind.Actor, sense, a.View.Root.position, strength, _elapsed, _elapsed + lifetime));
    }
    void Sense(Actor a, ActorStimulus stimulus)
    {
        if (a.Brain.Behaviour == CreatureBehaviour.Sleep && stimulus.Sense == ActorSense.Sight) return;
        if (!a.Perception.WithinRange(a.View.Root.position, a.View.Root.forward, Vector3.up,
                a.Brain.Behaviour == CreatureBehaviour.Sleep ? 0 : PerceptionLight, stimulus)) return;
        bool blocked = stimulus.Sense != ActorSense.Smell && Physics.Linecast(a.View.Root.position,
            stimulus.Position, ~0, QueryTriggerInteraction.Ignore);
        bool detected = a.Perception.Observe(a.Id, a.View.Root.position, a.View.Root.forward, Vector3.up,
            a.Brain.Behaviour == CreatureBehaviour.Sleep ? 0 : PerceptionLight, blocked, stimulus, _elapsed, a.Knowledge);
        if (detected && stimulus.Sense == ActorSense.Sight) a.DirectSight.Add(stimulus.Id);
        if (detected && stimulus.Sense == ActorSense.Sight && stimulus.Kind is ActorObservationKind.Food or ActorObservationKind.Water &&
            a.Knowledge.TryGet(stimulus.Id, stimulus.Kind, _elapsed, out var resource))
            a.Knowledge.Remember(new ActorKnowledge.Observation(resource.Id, resource.Kind, resource.Position,
                _elapsed + Mathf.Max(1, ResourceMemorySeconds), _elapsed, resource.Sense, resource.Uncertainty, resource.Confidence,
                resource.IdentityConfidence), _elapsed);
    }
    bool Observation(Actor observer, Actor target, out ActorKnowledge.Observation observation) =>
        observer.Knowledge.TryGet(target.Id, ActorObservationKind.Actor, _elapsed, out observation) && observation.Confidence >= .15f;
    bool Visible(Actor observer, Actor target) => observer.DirectSight.Contains(target.Id);
    double PerceivedStrength(Actor observer, Actor target) => Visible(observer, target) ? Strength(target) :
        Mathf.Max(.1f, target.Predator ? WolfStrength : DeerStrength);
    double PerceivedSupport(Actor observer, Actor opponent)
    {
        if (!PackSupportEnabled || opponent.Group == 0) return 0;
        double support = 0;
        foreach (var other in _actors)
            if (other != opponent && Available(other) && ActorGroup.Allied(opponent.Group, other.Group) && Observation(observer, other, out var known))
                support += ActorGroup.Support(opponent.Group, other.Group, PerceivedStrength(observer, other),
                    FlatDistance(KnownPosition(observer, opponent), known.Position), PackSupportRadius, true);
        return support;
    }
    Vector3 KnownPosition(Actor observer, Actor target) => Observation(observer, target, out var observation)
        ? observation.Position : observer.View.Root.position;
    CreatureResourceTarget KnownResource(Actor a, Source source)
    {
        if (source == null) return default;
        var kind = source.Stock.ThirstPerUnit > 0 ? ActorObservationKind.Water : ActorObservationKind.Food;
        return a.Knowledge.TryGet(source.Id, kind, _elapsed, out var observation)
            ? new() { Id = new(EntityId.HostOwner, source.Id), Position = observation.Position, Uncertainty = observation.Uncertainty, Available = true } : default;
    }
    void PerceptionStatus(Actor a, StringBuilder text)
    {
        if (!ShowPerception) return;
        var target = a.Threat ?? a.Prey ?? a.Interest;
        if (target != null && Observation(a, target, out var observation))
            text.AppendLine($"  {observation.Sense}: {target.Id} | confidence {observation.Confidence:P0} | identity {observation.IdentityConfidence:P0} | area ±{observation.Uncertainty:F1}m | age {_elapsed - observation.Observed:F1}s");
    }
}
