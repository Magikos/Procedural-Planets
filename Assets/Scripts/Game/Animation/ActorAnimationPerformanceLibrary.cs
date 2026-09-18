using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Actors/Animation Performance Library")]
public sealed class ActorAnimationPerformanceLibrary : ScriptableObject
{
    public Entry[] Entries = Array.Empty<Entry>();

    [Serializable]
    public sealed class Entry
    {
        public string Id;
        public string Action;
        public string RigFamily;
        public string Condition = "";
        [Tooltip("Humanoid jump return phase in the locomotion cycle. -1 keeps the current clock.")]
        [Range(-1f, 1f)] public float LocomotionExitPhase = -1f;
        [Range(0f, 1f)] public float MinProficiency;
        [Range(0f, 1f)] public float MaxProficiency = 1f;
        public Phase[] Phases = Array.Empty<Phase>();
    }

    [Serializable]
    public sealed class Phase
    {
        public string Name;
        public AnimationClip Clip;
        [Range(0f, 1f)] public float StartNormalized;
        [Range(0f, 1f)] public float EndNormalized = 1f;
        public bool Loop;
        [Min(0f)] public float BlendSeconds = 0.15f;
    }

    public ActorAnimationPerformanceLibraryData Snapshot() => new(Entries);
}

public sealed class ActorAnimationPerformanceLibraryData
{
    readonly ActorAnimationPerformance[] _entries;
    public int Count => _entries.Length;
    public ActorAnimationPerformance this[int index] => _entries[index];

    public ActorAnimationPerformanceLibraryData(ActorAnimationPerformanceLibrary.Entry[] entries)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        _entries = new ActorAnimationPerformance[entries.Length];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = new ActorAnimationPerformance(entries[i]);
            if (!ids.Add(entry.Id)) throw new ArgumentException("Performance IDs must be unique.", nameof(entries));
            _entries[i] = entry;
        }
    }

    public ActorAnimationPerformance Select(string action, string rig, float proficiency = 0f, string condition = "")
    {
        ActorAnimationPerformance.RequireKey(action, nameof(action));
        ActorAnimationPerformance.RequireKey(rig, nameof(rig));
        ActorAnimationPerformance.RequireUnit(proficiency, nameof(proficiency));
        if (condition == null) throw new ArgumentNullException(nameof(condition));
        if (condition.Length > 0) ActorAnimationPerformance.RequireKey(condition, nameof(condition));
        ActorAnimationPerformance exact = null;
        ActorAnimationPerformance fallback = null;
        for (int i = 0; i < _entries.Length; i++)
        {
            var entry = _entries[i];
            if (entry.Action != action || entry.RigFamily != rig || proficiency < entry.MinProficiency || proficiency > entry.MaxProficiency)
                continue;
            if (entry.Condition == condition && Prefer(entry, exact)) exact = entry;
            else if (entry.Condition.Length == 0 && Prefer(entry, fallback)) fallback = entry;
        }
        return exact ?? fallback;
    }

    static bool Prefer(ActorAnimationPerformance candidate, ActorAnimationPerformance current) =>
        current == null || candidate.MinProficiency > current.MinProficiency ||
        candidate.MinProficiency == current.MinProficiency && string.CompareOrdinal(candidate.Id, current.Id) < 0;
}

public sealed class ActorAnimationPerformance
{
    readonly ActorAnimationPerformancePhase[] _phases;
    public string Id { get; }
    public string Action { get; }
    public string RigFamily { get; }
    public string Condition { get; }
    public float LocomotionExitPhase { get; }
    public float MinProficiency { get; }
    public float MaxProficiency { get; }
    public int PhaseCount => _phases.Length;
    public ActorAnimationPerformancePhase this[int index] => _phases[index];

    internal ActorAnimationPerformance(ActorAnimationPerformanceLibrary.Entry entry)
    {
        if (entry == null) throw new ArgumentException("Performance entries cannot be null.");
        RequireKey(entry.Id, nameof(entry.Id));
        RequireKey(entry.Action, nameof(entry.Action));
        RequireKey(entry.RigFamily, nameof(entry.RigFamily));
        if (entry.Condition == null || entry.Condition.Length > 0 && string.IsNullOrWhiteSpace(entry.Condition))
            throw new ArgumentException("Condition must be empty or a nonempty key.");
        RequireUnit(entry.MinProficiency, nameof(entry.MinProficiency));
        RequireUnit(entry.MaxProficiency, nameof(entry.MaxProficiency));
        if (entry.LocomotionExitPhase != -1f) RequireUnit(entry.LocomotionExitPhase, nameof(entry.LocomotionExitPhase));
        if (entry.MaxProficiency < entry.MinProficiency) throw new ArgumentException("Proficiency range is reversed.");
        if (entry.Phases == null || entry.Phases.Length == 0) throw new ArgumentException("Performance requires phases.");
        Id = entry.Id;
        Action = entry.Action;
        RigFamily = entry.RigFamily;
        Condition = entry.Condition;
        LocomotionExitPhase = entry.LocomotionExitPhase;
        MinProficiency = entry.MinProficiency;
        MaxProficiency = entry.MaxProficiency;
        _phases = new ActorAnimationPerformancePhase[entry.Phases.Length];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < _phases.Length; i++)
        {
            var phase = new ActorAnimationPerformancePhase(entry.Phases[i]);
            if (!names.Add(phase.Phase)) throw new ArgumentException("Phase names must be unique within a performance.");
            _phases[i] = phase;
        }
    }

    public ActorAnimationPerformancePhase GetPhase(string phase)
    {
        RequireKey(phase, nameof(phase));
        for (int i = 0; i < _phases.Length; i++)
            if (_phases[i].Phase == phase) return _phases[i];
        return null;
    }

    internal static void RequireKey(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A nonempty key is required.", name);
    }

    internal static void RequireUnit(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0f || value > 1f) throw new ArgumentOutOfRangeException(name);
    }
}

public sealed class ActorAnimationPerformancePhase
{
    public string Phase { get; }
    public AnimationClip Clip { get; }
    public float StartNormalized { get; }
    public float EndNormalized { get; }
    public bool Loop { get; }
    public float BlendSeconds { get; }

    internal ActorAnimationPerformancePhase(ActorAnimationPerformanceLibrary.Phase phase)
    {
        if (phase == null) throw new ArgumentException("Performance phases cannot be null.");
        ActorAnimationPerformance.RequireKey(phase.Name, nameof(phase.Name));
        if (phase.Clip == null) throw new ArgumentException("Performance phase requires a clip.");
        ActorAnimationPerformance.RequireUnit(phase.StartNormalized, nameof(phase.StartNormalized));
        ActorAnimationPerformance.RequireUnit(phase.EndNormalized, nameof(phase.EndNormalized));
        if (phase.EndNormalized <= phase.StartNormalized) throw new ArgumentException("Phase end must follow its start.");
        if (!float.IsFinite(phase.BlendSeconds) || phase.BlendSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(phase.BlendSeconds));
        Phase = phase.Name;
        Clip = phase.Clip;
        StartNormalized = phase.StartNormalized;
        EndNormalized = phase.EndNormalized;
        Loop = phase.Loop;
        BlendSeconds = phase.BlendSeconds;
    }
}
