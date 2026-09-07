using System;
using System.Collections.Generic;

public readonly struct UtilityCandidate<T>
{
    public readonly T Key;
    public readonly float Score;
    public readonly bool Eligible;
    public readonly bool Emergency;

    public UtilityCandidate(T key, float score, bool eligible = true, bool emergency = false)
    {
        if (float.IsNaN(score) || float.IsInfinity(score)) throw new ArgumentOutOfRangeException(nameof(score));
        Key = key;
        Score = score;
        Eligible = eligible;
        Emergency = emergency;
    }
}

/// <summary>Selects an objective. The existing FSM owns execution and state transitions.</summary>
public sealed class UtilityDecision<T>
{
    readonly float _margin;
    readonly double _commitSeconds;
    readonly EqualityComparer<T> _equality = EqualityComparer<T>.Default;
    readonly Dictionary<T, double> _rejectedUntil = new();
    readonly List<T> _expired = new();
    double _committedUntil;
    double _lastTime = double.NegativeInfinity;
    public bool HasChoice { get; private set; }
    public T Choice { get; private set; }

    public void Reject(T key, double until)
    {
        if (double.IsNaN(until) || double.IsInfinity(until) || until < _lastTime)
            throw new ArgumentOutOfRangeException(nameof(until));
        if (_rejectedUntil.Count >= 32 && !_rejectedUntil.ContainsKey(key))
        {
            T oldest = default;
            double earliest = double.PositiveInfinity;
            foreach (var entry in _rejectedUntil)
                if (entry.Value < earliest) { oldest = entry.Key; earliest = entry.Value; }
            _rejectedUntil.Remove(oldest);
        }
        _rejectedUntil[key] = until;
    }

    public void ForgetFailure(T key) => _rejectedUntil.Remove(key);

    public UtilityDecision(float switchMargin, double commitSeconds)
    {
        if (float.IsNaN(switchMargin) || float.IsInfinity(switchMargin) || switchMargin < 0f ||
            double.IsNaN(commitSeconds) || double.IsInfinity(commitSeconds) || commitSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(switchMargin));
        _margin = switchMargin;
        _commitSeconds = commitSeconds;
    }

    public bool Select(IReadOnlyList<UtilityCandidate<T>> candidates, double now)
    {
        if (candidates == null) throw new ArgumentNullException(nameof(candidates));
        if (double.IsNaN(now) || double.IsInfinity(now) || now < _lastTime)
            throw new ArgumentOutOfRangeException(nameof(now));
        _lastTime = now;
        _expired.Clear();
        foreach (var entry in _rejectedUntil)
            if (entry.Value <= now) _expired.Add(entry.Key);
        foreach (T key in _expired) _rejectedUntil.Remove(key);
        int best = -1, current = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!candidate.Eligible) continue;
            if (!candidate.Emergency && _rejectedUntil.TryGetValue(candidate.Key, out double until) && now < until) continue;
            bool isCurrent = HasChoice && _equality.Equals(candidate.Key, Choice);
            if (isCurrent) current = i;
            if (best < 0 || (candidate.Emergency && !candidates[best].Emergency) ||
                (candidate.Emergency == candidates[best].Emergency &&
                 (candidate.Score > candidates[best].Score || (candidate.Score == candidates[best].Score && isCurrent))))
                best = i;
        }
        if (best < 0)
        {
            HasChoice = false;
            Choice = default;
            return false;
        }
        if (current >= 0 && best != current &&
            candidates[best].Emergency == candidates[current].Emergency &&
            (now < _committedUntil || candidates[best].Score <= candidates[current].Score + _margin))
            best = current;
        T selected = candidates[best].Key;
        if (!HasChoice || !_equality.Equals(Choice, selected)) _committedUntil = now + _commitSeconds;
        Choice = selected;
        HasChoice = true;
        return true;
    }
}
