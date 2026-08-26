using System;
using System.Collections.Generic;

/// <summary>
/// A state's behaviour for one context type. States hold NO per-actor data: everything they need arrives in
/// the context each tick, which is what lets one set of states drive every actor of a species.
/// </summary>
/// <remarks>
/// <b>Do not add instance fields to a state.</b> The project this was harvested from did exactly that
/// (a falling state stashed its air-control factor on itself), and sharing such a state across actors
/// cross-contaminates them. Per-actor data belongs in the context.
/// </remarks>
public interface IState<TContext>
{
    void Enter(ref TContext context);
    void Exit(ref TContext context);
    void Update(ref TContext context);

    /// <summary>
    /// The state a state chooses to leave for, or null to stay. This is how a behaviour ends ITSELF - "done
    /// eating", "lost the scent" - on a condition only it can see, without a transition table polling for it.
    /// </summary>
    Type EvaluateExit(ref TContext context);
}

/// <summary>A condition, and the state it leads to. A null <see cref="From"/> means "from any state".</summary>
public sealed class StateTransition<TContext>
{
    public Type From;
    public Func<TContext, bool> Condition = _ => false;

    /// <summary>
    /// Where the transition goes, chosen from the context at the moment it fires. Context-adaptive rather
    /// than fixed, so "flee exits back to whatever I was doing" needs no state to know any other state.
    /// </summary>
    public Func<Type, TContext, Type> ResolveTo = (_, _) => null;
}

/// <summary>
/// A small hierarchical-capable state machine, driven by a context injected per tick rather than held.
/// Harvested from Bryan's State Machine project (see the design doc, section 15) with its three unusable
/// parts fixed: it reads no static clock, it logs through an injected callback instead of a plugin, and its
/// states are required to be free of instance data.
/// </summary>
/// <remarks>
/// <para>
/// The machine holds the CURRENT STATE, so a machine belongs to one actor while that actor is simulated. It
/// is a promotion-time artefact and never the source of truth: rebuild it from a persisted state id, or an
/// actor that was fleeing when it was last seen comes back calm.
/// </para>
/// <para>
/// <c>TContext</c> is passed by reference throughout so a struct context can carry per-tick results back out
/// without allocating.
/// </para>
/// </remarks>
public sealed class AdaptiveStateMachine<TContext>
{
    readonly Dictionary<Type, IState<TContext>> _states = new();
    readonly Dictionary<Type, List<StateTransition<TContext>>> _byState = new();
    readonly List<StateTransition<TContext>> _fromAny = new();
    static readonly List<StateTransition<TContext>> NoTransitions = new();

    List<StateTransition<TContext>> _current = NoTransitions;
    IState<TContext> _state;

    /// <summary>Notified as (from, to) on every switch. The owner wires it to a logger; the machine owns none.</summary>
    public Action<string, string> OnTransition;

    public Type CurrentStateType => _state?.GetType();

    public AdaptiveStateMachine<TContext> WithStates(params IState<TContext>[] states)
    {
        foreach (IState<TContext> s in states)
            if (s != null) _states[s.GetType()] = s;
        return this;
    }

    public AdaptiveStateMachine<TContext> WithTransitions(params StateTransition<TContext>[] transitions)
    {
        foreach (StateTransition<TContext> t in transitions)
        {
            if (t == null) continue;
            if (t.From == null) { _fromAny.Add(t); continue; }
            if (!_byState.TryGetValue(t.From, out List<StateTransition<TContext>> list))
                _byState[t.From] = list = new List<StateTransition<TContext>>();
            list.Add(t);
        }
        return this;
    }

    public bool Has(Type stateType) => stateType != null && _states.ContainsKey(stateType);

    /// <summary>Enter a state directly. Used at construction and when restoring a persisted behaviour.</summary>
    public void Start(ref TContext context, Type stateType)
    {
        if (!_states.TryGetValue(stateType ?? typeof(void), out IState<TContext> next))
            return;
        _state = next;
        _current = _byState.TryGetValue(stateType, out List<StateTransition<TContext>> list) ? list : NoTransitions;
        _state.Enter(ref context);
    }

    public void Tick(ref TContext context)
    {
        if (_state == null) return;

        // At most one switch per tick, checked from-any first, then this state's own table, then the state's
        // own exit.
        bool handled = false;
        for (int i = 0; i < _fromAny.Count && !handled; i++)
            if (_fromAny[i].Condition(context))
            {
                Switch(ref context, _fromAny[i].ResolveTo(CurrentStateType, context));
                handled = true;
            }

        for (int i = 0; i < _current.Count && !handled; i++)
            if (_current[i].Condition(context))
            {
                Switch(ref context, _current[i].ResolveTo(CurrentStateType, context));
                handled = true;
            }

        if (!handled)
        {
            Type exit = _state.EvaluateExit(ref context);
            if (exit != null) Switch(ref context, exit);
        }

        // A state ACTS on the frame it is entered. The machine this was harvested from returned here instead,
        // which spent the whole tick on the transition and produced no intent - visible as an animal freezing
        // for a beat before it starts running.
        _state.Update(ref context);
    }

    bool Switch(ref TContext context, Type toType)
    {
        if (toType == null || !_states.TryGetValue(toType, out IState<TContext> next) || ReferenceEquals(next, _state))
            return false;

        OnTransition?.Invoke(_state?.GetType().Name ?? "None", next.GetType().Name);
        _state?.Exit(ref context);
        _state = next;
        _current = _byState.TryGetValue(toType, out List<StateTransition<TContext>> list) ? list : NoTransitions;
        _state.Enter(ref context);
        return true;
    }
}
