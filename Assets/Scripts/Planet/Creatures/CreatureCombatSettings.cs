using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Creature Combat")]
public sealed class CreatureCombatSettings : ScriptableObject
{
    [Min(1)] public int MaximumHealth = 100;
    [Min(.1f)] public float Damage = 18f, Reach = 1.4f;
    [Range(1f, 180f)] public float HalfAngle = 40f;
    [Min(.01f)] public float Windup = .45f, ActiveSeconds = .25f, RecoverySeconds = 1.2f;
    [Range(.01f, 1f)] public float StaminaCost = .12f;
    [Range(0f, 1f)] public float Wounds = .2f, Bleeding = .15f;
    [Header("Recovery in game hours")]
    [Min(.01f)] public float SafeDelayHours = .25f, HealthRecoveryHours = 24f, WoundRecoveryHours = 12f;
    [Min(.01f)] public float ClotHours = 12f, BleedDrainHours = 2f;
    [Header("Carcass food")]
    [Tooltip("Full hunger bars supplied by a fresh carcass before decay or feeding.")]
    [Min(.01f)] public float MeatYield = 1.5f;
    public CreatureCombatDto Snapshot() => new(MaximumHealth,
        new ActorAttackDefinition(Damage, Reach, HalfAngle, Windup, ActiveSeconds, RecoverySeconds, StaminaCost, Wounds, Bleeding),
        new ActorRecoveryProfile(SafeDelayHours * 3600d, HealthRecoveryHours * 3600d, WoundRecoveryHours * 3600d,
            ClotHours * 3600d, BleedDrainHours * 3600d), MeatYield);
}

public sealed record CreatureCombatDto(int MaximumHealth, ActorAttackDefinition Attack, ActorRecoveryProfile Recovery, double MeatYield = 1.5d)
{
    public double MeatYield { get; init; } = double.IsFinite(MeatYield) && MeatYield > 0d ? MeatYield :
        throw new System.ArgumentOutOfRangeException(nameof(MeatYield));
}
