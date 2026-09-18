using UnityEngine;

/// <summary>Look first, then search an estimated position. A timeout prevents indefinite investigation.</summary>
sealed class CreatureInvestigateState : IState<CreatureContext>
{
    readonly bool _search;
    public CreatureInvestigateState(bool search) => _search = search;
    public int Id => (int)(_search ? CreatureBehaviour.Investigate : CreatureBehaviour.Alert);
    public void Enter(ref CreatureContext c) => c.StateEnteredSeconds = c.SimulationSeconds;
    public void Exit(ref CreatureContext c) { }
    public int EvaluateExit(in CreatureContext c) => c.Objective is not (CreatureObjective.Investigate or CreatureObjective.Vigilance)
        ? (int)CreatureBehaviour.Wander : !_search && c.Senses.InvestigateInterest && c.SimulationSeconds - c.StateEnteredSeconds >= .8
            && c.Objective != CreatureObjective.Vigilance
            ? (int)CreatureBehaviour.Investigate : StateId.None;
    public void Update(ref CreatureContext c)
    {
        bool vigilant = c.Objective == CreatureObjective.Vigilance;
        if (!vigilant && c.SimulationSeconds - c.StateEnteredSeconds >= (_search ? 6 : 2))
        { c.ExaminedInterest = c.Senses.InterestId; c.InterestCooldownUntil = c.SimulationSeconds + 10; }
        Vector3 target = vigilant ? c.LastThreatPosition : c.Senses.InterestPosition;
        float bearing = CharacterMath.TangentBearing(c.Senses.Position, c.Senses.Forward, c.Senses.Up, target);
        // Head look handles small angles; larger turns carry steps.
        float distance = Vector3.ProjectOnPlane(target - c.Senses.Position, c.Senses.Up).magnitude;
        c.Throttle = _search && !vigilant && distance > 1 ? Mathf.Clamp01(1 - Mathf.Abs(bearing) / 120) : Mathf.Abs(bearing) > 40 ? .15f : 0;
        c.TurnDegreesPerSecond = c.Throttle > 0 ? Mathf.Clamp(bearing * 3, -120, 120) : 0;
        c.SpeedScale = 1;
    }
}

/// <summary>Travel and bounded gathering use the existing objective and interruption rules.</summary>
sealed class CreatureHomeState : IState<CreatureContext>
{
    readonly bool _gather;
    public CreatureHomeState(bool gather) => _gather = gather;
    public int Id => (int)(_gather ? CreatureBehaviour.Gather : CreatureBehaviour.ReturnHome);
    public void Enter(ref CreatureContext c) { }
    public void Exit(ref CreatureContext c) { }
    public int EvaluateExit(in CreatureContext c) => c.Objective != CreatureObjective.ReturnHome
        ? (int)CreatureBehaviour.Wander : !_gather && c.Senses.AtHome && c.Senses.GatherAtHome
            ? (int)CreatureBehaviour.Gather : _gather && !c.Senses.AtHome ? (int)CreatureBehaviour.ReturnHome : StateId.None;
    public void Update(ref CreatureContext c)
    {
        if (_gather || c.Senses.AtHome) { c.Throttle = 0; return; }
        float bearing = CharacterMath.TangentBearing(c.Senses.Position, c.Senses.Forward, c.Senses.Up, c.Senses.RestPosition);
        c.TurnDegreesPerSecond = Mathf.Clamp(bearing * 4f, -180f, 180f);
        c.Throttle = Mathf.Clamp01(1f - Mathf.Abs(bearing) / 100f);
        c.SpeedScale = 2f;
    }
}

sealed class CreatureRestState : IState<CreatureContext>
{
    readonly bool _sleep;
    public CreatureRestState(bool sleep) => _sleep = sleep;
    public int Id => (int)(_sleep ? CreatureBehaviour.Sleep : CreatureBehaviour.Rest);
    public void Enter(ref CreatureContext c) => c.StateEnteredSeconds = c.SimulationSeconds;
    public void Exit(ref CreatureContext c) { }
    public int EvaluateExit(in CreatureContext c) => c.Objective != CreatureObjective.Rest
        ? (int)CreatureBehaviour.Wander : _sleep && !c.Senses.CanSleep ? (int)CreatureBehaviour.Rest
        : !_sleep && c.Senses.CanSleep && c.SimulationSeconds - c.StateEnteredSeconds >= 6d
            ? (int)CreatureBehaviour.Sleep : StateId.None;
    public void Update(ref CreatureContext c) => c.Throttle = 0f;
}

/// <summary>Approach and request consumption. The host validates stock, diet, range, and life state.</summary>
sealed class CreatureConsumeState : IState<CreatureContext>
{
    public const float Reach = 1.1f;
    readonly bool _water;
    public CreatureConsumeState(bool water) => _water = water;
    public int Id => (int)(_water ? CreatureBehaviour.Drink : CreatureBehaviour.Feed);
    public static bool HasTarget(in CreatureContext c) =>
        c.Objective == CreatureObjective.FindFood && c.Senses.Food.Available ||
        c.Objective == CreatureObjective.FindWater && c.Senses.Water.Available;
    public void Enter(ref CreatureContext c) { }
    public void Exit(ref CreatureContext c) { }
    public int EvaluateExit(in CreatureContext c) =>
        c.Objective != (_water ? CreatureObjective.FindWater : CreatureObjective.FindFood) ||
        !(_water ? c.Senses.Water : c.Senses.Food).Available ? (int)CreatureBehaviour.Wander : StateId.None;
    public void Update(ref CreatureContext c)
    {
        var source = _water ? c.Senses.Water : c.Senses.Food;
        float distance = Vector3.ProjectOnPlane(source.Position - c.Senses.Position, c.Senses.Up).magnitude;
        float reach = Mathf.Max(.1f, Reach - source.Uncertainty);
        float bearing = CharacterMath.TangentBearing(c.Senses.Position, c.Senses.Forward, c.Senses.Up, source.Position);
        c.TurnDegreesPerSecond = Mathf.Clamp(bearing * 3f, -90f, 90f);
        c.Throttle = distance <= reach ? 0f : Mathf.Min(1f, (distance - reach) /
            Mathf.Max(0.001f, (c.Senses.Species?.WalkSpeedMps ?? 1f) * c.Senses.DeltaTime)) *
            Mathf.Clamp01(1f - Mathf.Abs(bearing) / 180f);
        if (c.Throttle == 0f) c.TurnDegreesPerSecond = 0f;
    }
}
