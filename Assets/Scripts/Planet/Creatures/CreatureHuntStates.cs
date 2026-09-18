using UnityEngine;

public static class CreatureHunt
{
    public const float BiteRange = 1.4f;
    public static bool Ready(in CreatureContext c) => c.SimulationSeconds >= c.AttackReadyAt &&
        (c.Senses.Attack == null || c.Senses.CanAttack);
    public static bool CanStrike(in CreatureContext c, Vector3 target)
    {
        if (c.Senses.PerceptionLimited && !(c.Objective == CreatureObjective.Defend ? c.Senses.DirectThreat : c.Senses.DirectPrey)) return false;
        if (c.Objective == CreatureObjective.Hunt && c.Senses.AttackLaneBlocked) return false;
        if (c.Senses.Attack == null) return CanBite(c.Senses.Position, c.Senses.Forward, c.Senses.Up, target);
        float uncertainty = c.Objective == CreatureObjective.Defend ? c.Senses.ThreatUncertainty : c.Senses.PreyUncertainty;
        return c.Senses.Attack.InContact(Vector3.Distance(c.Senses.Position, target) + uncertainty,
            CharacterMath.TangentBearing(c.Senses.Position, c.Senses.Forward, c.Senses.Up, target));
    }
    public static Vector3 TargetPosition(in CreatureContext c) => c.Objective == CreatureObjective.Defend ? c.Senses.ThreatPosition : c.Senses.PreyPosition;
    public static EntityId TargetId(in CreatureContext c) => c.Objective == CreatureObjective.Defend ? c.Senses.ThreatId : c.Senses.PreyId;
    public static float Distance(in CreatureContext c) => Vector3.Distance(c.Senses.Position, TargetPosition(c));
    public static bool Lost(in CreatureContext c) =>
        !(c.Objective == CreatureObjective.Hunt && c.Senses.HasPrey || c.Objective == CreatureObjective.Defend && c.Senses.HasThreat) ||
        c.HuntTarget != TargetId(c) || Distance(c) > (c.Objective == CreatureObjective.Defend ? 6f : 20f);
    public static bool CanBite(Vector3 position, Vector3 forward, Vector3 up, Vector3 prey) =>
        Vector3.Distance(position, prey) <= BiteRange &&
        Mathf.Abs(CharacterMath.TangentBearing(position, forward, up, prey)) <= 45f;
    public static void Approach(ref CreatureContext c, float speed)
    {
        Vector3 goal = c.Senses.HasPursuitGoal ? c.Senses.PursuitGoal : c.Senses.PreyPosition;
        bool arrived = c.Senses.HasPursuitGoal && Vector3.ProjectOnPlane(goal - c.Senses.Position, c.Senses.Up).sqrMagnitude < .09f;
        float bearing = CharacterMath.TangentBearing(c.Senses.Position, c.Senses.Forward, c.Senses.Up, arrived ? c.Senses.PreyPosition : goal);
        c.TurnDegreesPerSecond = Mathf.Clamp(bearing * 3f, -90f, 90f);
        c.Throttle = arrived ? 0f : Mathf.Clamp01(1f - Mathf.Abs(bearing) / 180f);
        c.SpeedScale = speed;
    }
}

sealed class StalkState : IState<CreatureContext>
{
    public int Id => (int)CreatureBehaviour.Stalk;
    public void Enter(ref CreatureContext c)
    {
        c.HuntStartedSeconds = c.SimulationSeconds;
        c.PursuitStarted = false;
        c.HuntTarget = c.Senses.PreyId;
    }
    public void Exit(ref CreatureContext c) { }
    public int EvaluateExit(in CreatureContext c) => CreatureHunt.Lost(c) ? (int)CreatureBehaviour.Wander :
        c.Senses.PreyAlert || CreatureHunt.Distance(c) < 3f ? (int)CreatureBehaviour.Chase : StateId.None;
    public void Update(ref CreatureContext c) => CreatureHunt.Approach(ref c, 1.2f);
}

sealed class ChaseState : IState<CreatureContext>
{
    public int Id => (int)CreatureBehaviour.Chase;
    public void Enter(ref CreatureContext c)
    {
        // Stalking does not spend the pursuit budget. Recovering after a strike does.
        if (!c.PursuitStarted) { c.HuntStartedSeconds = c.SimulationSeconds; c.PursuitStarted = true; }
    }
    public void Exit(ref CreatureContext c) { }
    public int EvaluateExit(in CreatureContext c) => CreatureHunt.Lost(c) || c.SimulationSeconds < c.HuntCooldownUntil
        ? (int)CreatureBehaviour.Recover : CreatureHunt.Ready(c) && CreatureHunt.CanStrike(c, c.Senses.PreyPosition)
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
        c.HuntTarget = CreatureHunt.TargetId(c);
        c.AttackSequence++;
        c.HitConsumed = false;
        if (c.Senses.Attack != null) c.AttackReadyAt = c.SimulationSeconds + c.Senses.Attack.Duration + c.Senses.Attack.RecoverySeconds;
    }
    public void Exit(ref CreatureContext c) { }
    public int EvaluateExit(in CreatureContext c) => CreatureHunt.Lost(c) ? (int)CreatureBehaviour.Recover :
        (c.HitConsumed || c.Senses.Attack != null) && c.SimulationSeconds - c.StateEnteredSeconds >=
            (c.Senses.Attack?.Duration ?? Mathf.Max(0.1f, c.Senses.AttackDuration))
            ? (int)CreatureBehaviour.Recover : StateId.None;
    public void Update(ref CreatureContext c)
    {
        float hit = c.Senses.Attack?.Windup ?? Mathf.Clamp(c.Senses.AttackHitTime, 0f, Mathf.Max(0.1f, c.Senses.AttackDuration));
        c.Throttle = c.SimulationSeconds - c.StateEnteredSeconds < hit ? 1f : 0f;
        c.SpeedScale = 3f;
        if (c.Throttle > 0f)
        {
            // Track during the wind-up, then commit when the bite resolves. A crossing target can still dodge.
            float bearing = CharacterMath.TangentBearing(c.Senses.Position, c.Senses.Forward, c.Senses.Up, CreatureHunt.TargetPosition(c));
            c.TurnDegreesPerSecond = Mathf.Clamp(bearing * 6f, -180f, 180f);
            c.Throttle = Mathf.Clamp01((CreatureHunt.Distance(c) - (c.Senses.Attack?.Reach * .9f ?? .65f)) / Mathf.Max(0.001f, 3f * c.Senses.DeltaTime));
        }
        if (!c.HitConsumed && c.SimulationSeconds - c.StateEnteredSeconds >= hit)
        {
            if (c.Senses.Attack == null) c.HitConsumed = true;
            c.HitRequested = true;
        }
    }
}

sealed class RecoverState : IState<CreatureContext>
{
    public int Id => (int)CreatureBehaviour.Recover;
    public void Enter(ref CreatureContext c) => c.StateEnteredSeconds = c.SimulationSeconds;
    public void Exit(ref CreatureContext c) { }
    public int EvaluateExit(in CreatureContext c) => c.SimulationSeconds - c.StateEnteredSeconds <
        (c.Senses.Attack?.RecoverySeconds ?? .6f) ? StateId.None :
        c.Objective == CreatureObjective.Defend ? (int)CreatureBehaviour.Defend :
        CreatureHunt.Lost(c) || c.SimulationSeconds < c.HuntCooldownUntil ? (int)CreatureBehaviour.Wander : (int)CreatureBehaviour.Chase;
    public void Update(ref CreatureContext c) => c.Throttle = 0f;
}

/// <summary>Face a close attacker. Counterattacks reuse the attack timing and authority hit validation.</summary>
sealed class CreatureDefendState : IState<CreatureContext>
{
    public int Id => (int)CreatureBehaviour.Defend;
    public void Enter(ref CreatureContext c) { }
    public void Exit(ref CreatureContext c) { }
    public int EvaluateExit(in CreatureContext c) => c.Objective != CreatureObjective.Defend || !c.Senses.HasThreat
        ? (int)CreatureBehaviour.Wander : CreatureHunt.Ready(c) && CreatureHunt.CanStrike(c, c.Senses.ThreatPosition)
            ? (int)CreatureBehaviour.Attack : StateId.None;
    public void Update(ref CreatureContext c)
    {
        float bearing = CharacterMath.TangentBearing(c.Senses.Position, c.Senses.Forward, c.Senses.Up, c.Senses.ThreatPosition);
        c.TurnDegreesPerSecond = Mathf.Clamp(bearing * 3f, -120f, 120f);
        c.Throttle = Mathf.Abs(bearing) > 10f ? .25f : 0f;
        c.SpeedScale = 1f;
    }
}

/// <summary>A warning display buys space without starting an attack sequence.</summary>
sealed class CreatureThreatenState : IState<CreatureContext>
{
    public int Id => (int)CreatureBehaviour.Threaten;
    public void Enter(ref CreatureContext c) => c.StateEnteredSeconds = c.SimulationSeconds;
    public void Exit(ref CreatureContext c) { }
    public int EvaluateExit(in CreatureContext c) => c.Objective != CreatureObjective.Defend || !c.Senses.HasThreat
        ? (int)CreatureBehaviour.Wander : c.Senses.ThreatProvoked || c.Senses.ThreatDistance <= 2f
            ? (int)CreatureBehaviour.Defend : StateId.None;
    public void Update(ref CreatureContext c)
    {
        if (c.SimulationSeconds - c.StateEnteredSeconds >= 6d)
        { c.YieldThreat = c.Senses.ThreatId; c.ThreatYieldUntil = c.SimulationSeconds + 8d; }
        float bearing = CharacterMath.TangentBearing(c.Senses.Position, c.Senses.Forward, c.Senses.Up, c.Senses.ThreatPosition);
        c.TurnDegreesPerSecond = Mathf.Clamp(bearing * 3f, -120f, 120f);
        c.Throttle = Mathf.Abs(bearing) > 15f ? .15f : 0f;
        c.SpeedScale = 1f;
    }
}
