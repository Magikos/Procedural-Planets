using System;
using System.Collections.Generic;

/// <summary>
/// Tracks every <see cref="EventBus{TEvent}"/> that has been used this session so all of them
/// can be cleared in one call on teardown. Each closed bus type registers its <c>ClearAll</c>
/// from its static constructor the first time it is touched, so callers never have to maintain
/// a hand-written list of event types.
/// </summary>
public static class EventBusRegistry
{
    static readonly List<Action> _clearers = new();

    public static void Register(Action clearAll)
    {
        if (clearAll != null && !_clearers.Contains(clearAll))
            _clearers.Add(clearAll);
    }

    public static void ClearAll()
    {
        for (int i = 0; i < _clearers.Count; i++)
            _clearers[i]?.Invoke();
    }
}
