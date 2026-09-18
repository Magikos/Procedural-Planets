using System;

public sealed class ActorAttackDefinition
{
    public double Damage { get; }
    public float Reach { get; }
    public float HalfAngle { get; }
    public float Windup { get; }
    public float ActiveSeconds { get; }
    public float RecoverySeconds { get; }
    public double StaminaCost { get; }
    public double Wounds { get; }
    public double Bleeding { get; }
    public float Duration => Windup + ActiveSeconds;
    public ActorAttackDefinition(double damage, float reach, float halfAngle, float windup, float activeSeconds,
        float recoverySeconds, double staminaCost, double wounds, double bleeding)
    {
        foreach (double value in new[] { damage, reach, halfAngle, windup, activeSeconds, recoverySeconds, staminaCost })
            if (!double.IsFinite(value) || value <= 0d) throw new ArgumentOutOfRangeException(nameof(damage));
        if (halfAngle > 180f || staminaCost > 1d || !ActorNeeds.IsLevel(wounds) || !ActorNeeds.IsLevel(bleeding) ||
            !float.IsFinite(windup + activeSeconds)) throw new ArgumentOutOfRangeException(nameof(staminaCost));
        Damage = damage; Reach = reach; HalfAngle = halfAngle; Windup = windup; ActiveSeconds = activeSeconds;
        RecoverySeconds = recoverySeconds; StaminaCost = staminaCost; Wounds = wounds; Bleeding = bleeding;
    }
    public bool InContact(float distance, float bearing) => distance >= 0f && distance <= Reach && Math.Abs(bearing) <= HalfAngle;
}
