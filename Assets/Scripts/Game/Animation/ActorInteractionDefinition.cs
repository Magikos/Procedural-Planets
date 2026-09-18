using System;
using UnityEngine;

/// <summary>Reusable clip sequence. Objects supply world-space contacts and interpret named markers.</summary>
[CreateAssetMenu(menuName = "Actors/Interaction Definition")]
public sealed class ActorInteractionDefinition : ScriptableObject
{
    public string Action;
    public Phase[] Phases = Array.Empty<Phase>();

    [Serializable]
    public sealed class Phase
    {
        public ActorAnimationPerformanceLibrary.Phase Animation = new();
        [Min(.01f)] public float Seconds = 1f;
        [Range(0f, 1f)] public float RightHandWeight;
        [Range(0f, 1f)] public float LeftHandWeight;
        public bool WaitForInput;
        public bool RepeatUntilInput;
        public bool AllowMovement;
        public bool UseLocomotion;
        public string ContactSet = "";
        public bool ReverseAnimation;
        [Min(0f)] public float GripRadius;
        [Range(-30f, 30f)] public float ForwardLeanDegrees;
        public string Marker = "";
        [Range(0f, 1f)] public float MarkerProgress = .5f;
    }

    public ActorInteractionPlan Snapshot() => new(this);
}

/// <summary>Validated runtime data, independent of mutable inspector settings.</summary>
public sealed class ActorInteractionPlan
{
    public sealed class Phase
    {
        public readonly string Name, Marker, ContactSet;
        public readonly float Seconds, RightHandWeight, LeftHandWeight, MarkerProgress, GripRadius, ForwardLeanDegrees;
        public readonly bool WaitForInput, RepeatUntilInput, AllowMovement, UseLocomotion, ReverseAnimation;
        internal Phase(ActorInteractionDefinition.Phase source)
        {
            if (source == null || !float.IsFinite(source.Seconds) || source.Seconds <= 0f)
                throw new ArgumentException("Interaction phases need a positive duration.");
            ActorAnimationPerformance.RequireUnit(source.RightHandWeight, nameof(source.RightHandWeight));
            ActorAnimationPerformance.RequireUnit(source.LeftHandWeight, nameof(source.LeftHandWeight));
            ActorAnimationPerformance.RequireUnit(source.MarkerProgress, nameof(source.MarkerProgress));
            if (source.Marker == null) throw new ArgumentException("Interaction marker cannot be null.");
            Name = source.Animation.Name; Marker = source.Marker; ContactSet = source.ContactSet ?? "";
            Seconds = source.Seconds; RightHandWeight = source.RightHandWeight; LeftHandWeight = source.LeftHandWeight;
            MarkerProgress = source.MarkerProgress; WaitForInput = source.WaitForInput;
            RepeatUntilInput = source.RepeatUntilInput;
            if (RepeatUntilInput && (!WaitForInput || !source.Animation.Loop))
                throw new ArgumentException("Repeated work requires a looping animation and input-controlled exit.");
            AllowMovement = source.AllowMovement; UseLocomotion = source.UseLocomotion;
            if (source.ReverseAnimation && source.Animation.Loop)
                throw new ArgumentException("Reversed interaction phases must not loop.");
            ReverseAnimation = source.ReverseAnimation;
            if (!float.IsFinite(source.GripRadius) || source.GripRadius < 0f)
                throw new ArgumentOutOfRangeException(nameof(source.GripRadius));
            GripRadius = source.GripRadius;
            if (!float.IsFinite(source.ForwardLeanDegrees) || Mathf.Abs(source.ForwardLeanDegrees) > 30f)
                throw new ArgumentOutOfRangeException(nameof(source.ForwardLeanDegrees));
            ForwardLeanDegrees = source.ForwardLeanDegrees;
        }
    }
    readonly Phase[] _phases;
    public string Action { get; }
    public int Count => _phases.Length;
    public Phase this[int index] => _phases[index];
    public ActorAnimationPerformanceLibrary.Entry Performance { get; }

    public ActorInteractionPlan(ActorInteractionDefinition source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        ActorAnimationPerformance.RequireKey(source.Action, nameof(source.Action));
        if (source.Phases == null || source.Phases.Length == 0) throw new ArgumentException("Interaction requires phases.");
        Action = source.Action;
        _phases = new Phase[source.Phases.Length];
        var animations = new ActorAnimationPerformanceLibrary.Phase[_phases.Length];
        for (int i = 0; i < _phases.Length; i++)
        {
            var phase = source.Phases[i];
            if (phase?.Animation == null || phase.Animation.BlendSeconds <= 0f)
                throw new ArgumentException("Interaction animation phases require a positive blend duration.");
            _phases[i] = new Phase(phase);
            var a = phase.Animation;
            animations[i] = new ActorAnimationPerformanceLibrary.Phase { Name = a.Name, Clip = a.Clip,
                StartNormalized = a.StartNormalized, EndNormalized = a.EndNormalized, Loop = a.Loop, BlendSeconds = a.BlendSeconds };
        }
        Performance = new ActorAnimationPerformanceLibrary.Entry { Id = Action, Action = Action, RigFamily = "Humanoid", Phases = animations };
        _ = new ActorAnimationPerformance(Performance);
    }
}
