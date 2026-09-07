using System;
using System.Collections.Generic;

/// <summary>Sentinel state id. In a transition's <c>From</c> it means "from any state"; returned from
/// <see cref="IState{TContext}.EvaluateExit"/> it means "stay".</summary>
public static class StateId
{
    public const int None = -1;
}

/// <summary>A transition's test. Takes the context by <c>in</c> so a struct context is not copied per check.</summary>
public delegate bool StateCondition<TContext>(in TContext context);

/// <summary>Picks the destination from the context at the moment a transition fires.</summary>
public delegate int StateTarget<TContext>(int fromId, in TContext context);

/// <summary>
/// One behaviour, for one context type.
/// </summary>
/// <remarks>
/// <para>
/// <b>A state holds NO per-actor data.</b> Everything it needs arrives in the context each tick, which is
/// what lets a single set of state objects drive every actor of a species. The machine this was harvested
/// from broke exactly this rule - a falling state stashed its air-control factor on itself - and sharing such
/// a state across actors cross-contaminates them.
/// </para>
/// <para>
/// <see cref="Id"/> is the state's identity everywhere: the key the machine dispatches on, the value a
/// transition resolves to, AND the value written down when the actor stops being simulated. One number, so
/// there is no second table mapping "which state" to "what we saved", and therefore nothing to drift.
/// </para>
/// </remarks>
public interface IState<TContext>
{
    /// <summary>Small, dense, and stable across builds - it is persisted. An enum value is the intended source.</summary>
    int Id { get; }

    void Enter(ref TContext context);
    void Exit(ref TContext context);
    void Update(ref TContext context);

    /// <summary>
    /// The state this one chooses to leave for, or <see cref="StateId.None"/> to stay. This is how a
    /// behaviour ends ITSELF - "done eating", "lost the scent" - on a condition only it can see, rather than
    /// a transition table polling for something it cannot observe.
    /// </summary>
    int EvaluateExit(in TContext context);
}

/// <summary>A condition and where it leads. <see cref="From"/> of <see cref="StateId.None"/> fires from any state.</summary>
public sealed class StateTransition<TContext>
{
    public int From = StateId.None;
    public StateCondition<TContext> Condition;

    /// <summary>
    /// Context-adaptive rather than fixed, which is the point: "flee exits back to whatever I was doing"
    /// needs no state to know about any other state.
    /// </summary>
    public StateTarget<TContext> ResolveTo;
}

/// <summary>
/// A small state machine driven by a context injected per tick rather than held.
/// </summary>
/// <remarks>
/// <para>
/// Harvested from Bryan's State Machine project (design doc section 15) and then redesigned. What changed and
/// why: states are keyed by a small INTEGER they own rather than by <see cref="Type"/>, so the id the machine
/// dispatches on is the same number that gets persisted and there is no translation table to fall out of
/// sync; the block-timeout watchdog is gone with its <c>Time.deltaTime</c> read, which authority code cannot
/// make (a fast-forward has no frames); logging goes through an injected callback instead of a plugin; and a
/// state now ACTS on the frame it is entered rather than spending that tick on the transition.
/// </para>
/// <para>
/// The machine holds the current state, so one belongs to one actor while that actor is simulated. It is a
/// promotion-time artefact and never the source of truth: rebuild it from a persisted id, or an actor that
/// was fleeing when it was last seen comes back calm.
/// </para>
/// <para>
/// Nesting is not built. A composite state - one that owns a sub-machine and forwards to it - is the
/// extension when a behaviour needs sub-behaviours; nothing here forecloses it.
/// </para>
/// </remarks>
public sealed class AdaptiveStateMachine<TContext>
{
    static readonly StateTransition<TContext>[] NoTransitions = Array.Empty<StateTransition<TContext>>();

    readonly IState<TContext>[] _states;                 // indexed by state id
    readonly StateTransition<TContext>[][] _byFrom;      // indexed by state id
    readonly StateTransition<TContext>[] _fromAny;

    IState<TContext> _state;
    StateTransition<TContext>[] _current = NoTransitions;

    /// <summary>Notified as (from, to) on every switch. The owner wires it to a logger; the machine owns none.</summary>
    public Action<int, int> OnTransition;

    /// <summary>The id of the state currently running, or <see cref="StateId.None"/> before <see cref="Start"/>.</summary>
    public int CurrentId => _state?.Id ?? StateId.None;

    public AdaptiveStateMachine(IState<TContext>[] states, StateTransition<TContext>[] transitions = null)
    {
        if (states == null || states.Length == 0)
            throw new ArgumentException("a machine needs at least one state", nameof(states));

        int maxId = 0;
        foreach (IState<TContext> s in states)
        {
            if (s == null) throw new ArgumentException("null state", nameof(states));
            if (s.Id < 0) throw new ArgumentOutOfRangeException(nameof(states), s.Id, "state ids must be >= 0");
            maxId = Math.Max(maxId, s.Id);
        }

        _states = new IState<TContext>[maxId + 1];
        foreach (IState<TContext> s in states)
        {
            // Validated rather than last-one-wins: a duplicate id silently drops a behaviour, and the symptom
            // is an actor that never enters a state nobody can find a reason for.
            if (_states[s.Id] != null)
                throw new ArgumentException(
                    $"two states share id {s.Id}: {_states[s.Id].GetType().Name} and {s.GetType().Name}",
                    nameof(states));
            _states[s.Id] = s;
        }

        var any = new List<StateTransition<TContext>>();
        var byFrom = new List<StateTransition<TContext>>[_states.Length];
        foreach (StateTransition<TContext> t in transitions ?? NoTransitions)
        {
            if (t?.Condition == null || t.ResolveTo == null)
                throw new ArgumentException("a transition needs a condition and a target", nameof(transitions));
            if (t.From == StateId.None) { any.Add(t); continue; }
            if ((uint)t.From >= (uint)_states.Length || _states[t.From] == null)
                throw new ArgumentOutOfRangeException(nameof(transitions), t.From, "transition from an unknown state");
            (byFrom[t.From] ??= new List<StateTransition<TContext>>()).Add(t);
        }

        _fromAny = any.Count > 0 ? any.ToArray() : NoTransitions;
        _byFrom = new StateTransition<TContext>[_states.Length][];
        for (int i = 0; i < _byFrom.Length; i++)
            _byFrom[i] = byFrom[i]?.ToArray() ?? NoTransitions;
    }

    public bool Has(int id) => (uint)id < (uint)_states.Length && _states[id] != null;

    /// <summary>Enter a state directly - at construction, and when restoring a persisted behaviour.</summary>
    public void Start(ref TContext context, int id)
    {
        if (!Has(id)) throw new ArgumentOutOfRangeException(nameof(id), id, "no such state");
        Stop(ref context);
        _state = _states[id];
        _current = _byFrom[id];
        _state.Enter(ref context);
    }

    /// <summary>Release the active state. Repeated stops and later ticks do nothing until Start.</summary>
    public void Stop(ref TContext context)
    {
        IState<TContext> previous = _state;
        _state = null;
        _current = NoTransitions;
        previous?.Exit(ref context);
    }

    public void Tick(ref TContext context)
    {
        if (_state == null) return;

        // At most one switch per tick: from-any first, then this state's own table, then its own exit.
        int target = StateId.None;
        for (int i = 0; i < _fromAny.Length && target == StateId.None; i++)
            if (_fromAny[i].Condition(in context))
                target = _fromAny[i].ResolveTo(_state.Id, in context);

        for (int i = 0; i < _current.Length && target == StateId.None; i++)
            if (_current[i].Condition(in context))
                target = _current[i].ResolveTo(_state.Id, in context);

        if (target == StateId.None)
            target = _state.EvaluateExit(in context);

        if (target != StateId.None && target != _state.Id)
            Switch(ref context, target);

        // A state ACTS on the frame it is entered. The machine this was harvested from returned here instead,
        // spending the whole tick on the transition and producing no intent - visible as an animal freezing
        // for a beat before it starts running.
        _state.Update(ref context);
    }

    void Switch(ref TContext context, int toId)
    {
        if (!Has(toId)) return;

        int fromId = _state.Id;
        _state.Exit(ref context);
        _state = _states[toId];
        _current = _byFrom[toId];
        _state.Enter(ref context);
        OnTransition?.Invoke(fromId, toId);
    }
}
