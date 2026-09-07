using UnityEngine;

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
        float bearing = CharacterMath.TangentBearing(c.Senses.Position, c.Senses.Forward, c.Senses.Up, source.Position);
        c.TurnDegreesPerSecond = Mathf.Clamp(bearing * 3f, -90f, 90f);
        c.Throttle = distance <= Reach ? 0f : Mathf.Min(1f, (distance - Reach) /
            Mathf.Max(0.001f, (c.Senses.Species?.WalkSpeedMps ?? 1f) * c.Senses.DeltaTime)) *
            Mathf.Clamp01(1f - Mathf.Abs(bearing) / 180f);
        if (c.Throttle == 0f) c.TurnDegreesPerSecond = 0f;
    }
}
