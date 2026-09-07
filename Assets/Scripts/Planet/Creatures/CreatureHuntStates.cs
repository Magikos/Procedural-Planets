using UnityEngine;

public static class CreatureHunt
{
    public const float BiteRange = 1.4f;
    public static float Distance(in CreatureContext c) => Vector3.Distance(c.Senses.Position, c.Senses.PreyPosition);
    public static bool Lost(in CreatureContext c) => c.Objective != CreatureObjective.Hunt || !c.Senses.HasPrey ||
        c.HuntTarget != c.Senses.PreyId || Distance(c) > 20f;
    public static bool CanBite(Vector3 position, Vector3 forward, Vector3 up, Vector3 prey) =>
        Vector3.Distance(position, prey) <= BiteRange &&
        Mathf.Abs(CharacterMath.TangentBearing(position, forward, up, prey)) <= 45f;
    public static void Approach(ref CreatureContext c, float speed)
    {
        float bearing = CharacterMath.TangentBearing(c.Senses.Position, c.Senses.Forward, c.Senses.Up, c.Senses.PreyPosition);
        c.TurnDegreesPerSecond = Mathf.Clamp(bearing * 3f, -90f, 90f);
        c.Throttle = Mathf.Clamp01(1f - Mathf.Abs(bearing) / 180f);
        c.SpeedScale = speed;
    }
}

sealed class StalkState : IState<CreatureContext>
{
    public int Id => (int)CreatureBehaviour.Stalk;
    public void Enter(ref CreatureContext c)
    {
        c.HuntStartedSeconds = c.SimulationSeconds;
        c.HuntTarget = c.Senses.PreyId;
    }
    public void Exit(ref CreatureContext c) { }
    public int EvaluateExit(in CreatureContext c) => CreatureHunt.Lost(c) ? (int)CreatureBehaviour.Wander :
        c.Senses.PreyAlert || CreatureHunt.Distance(c) < 3f ? (int)CreatureBehaviour.Chase : StateId.None;
    public void Update(ref CreatureContext c) => CreatureHunt.Approach(ref c, 0.6f);
}

sealed class ChaseState : IState<CreatureContext>
{
    public int Id => (int)CreatureBehaviour.Chase;
    public void Enter(ref CreatureContext c) { }
    public void Exit(ref CreatureContext c) { }
    public int EvaluateExit(in CreatureContext c) => CreatureHunt.Lost(c) || c.SimulationSeconds < c.HuntCooldownUntil
        ? (int)CreatureBehaviour.Recover : CreatureHunt.CanBite(c.Senses.Position, c.Senses.Forward, c.Senses.Up, c.Senses.PreyPosition)
            ? (int)CreatureBehaviour.Attack : StateId.None;
    public void Update(ref CreatureContext c)
    {
        CreatureHunt.Approach(ref c, 4f);
        if (c.SimulationSeconds - c.HuntStartedSeconds > 18d) c.HuntCooldownUntil = c.SimulationSeconds + 5d;
    }
}

sealed class AttackState : IState<CreatureContext>
{
    public int Id => (int)CreatureBehaviour.Attack;
    public void Enter(ref CreatureContext c)
    {
        c.StateEnteredSeconds = c.SimulationSeconds;
        c.AttackSequence++;
        c.HitConsumed = false;
    }
    public void Exit(ref CreatureContext c) { }
    public int EvaluateExit(in CreatureContext c) => CreatureHunt.Lost(c) ? (int)CreatureBehaviour.Recover :
        c.HitConsumed && c.SimulationSeconds - c.StateEnteredSeconds >= Mathf.Max(0.1f, c.Senses.AttackDuration)
            ? (int)CreatureBehaviour.Recover : StateId.None;
    public void Update(ref CreatureContext c)
    {
        float hit = Mathf.Clamp(c.Senses.AttackHitTime, 0f, Mathf.Max(0.1f, c.Senses.AttackDuration));
        c.Throttle = c.SimulationSeconds - c.StateEnteredSeconds < hit ? 1f : 0f;
        c.SpeedScale = 3f;
        if (!c.HitConsumed && c.SimulationSeconds - c.StateEnteredSeconds >= hit)
        {
            c.HitConsumed = true;
            c.HitRequested = true;
        }
    }
}

sealed class RecoverState : IState<CreatureContext>
{
    public int Id => (int)CreatureBehaviour.Recover;
    public void Enter(ref CreatureContext c) => c.StateEnteredSeconds = c.SimulationSeconds;
    public void Exit(ref CreatureContext c) { }
    public int EvaluateExit(in CreatureContext c) => c.SimulationSeconds - c.StateEnteredSeconds < 0.6d ? StateId.None :
        CreatureHunt.Lost(c) || c.SimulationSeconds < c.HuntCooldownUntil ? (int)CreatureBehaviour.Wander : (int)CreatureBehaviour.Chase;
    public void Update(ref CreatureContext c) => c.Throttle = 0f;
}
