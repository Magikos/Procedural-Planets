using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Creature Visuals")]
public sealed class CreatureVisualSettings : ScriptableObject
{
    public GameObject MalePrefab;
    public GameObject FemalePrefab;
    public AnimationClip Idle;
    public AnimationClip Walk;
    public AnimationClip Run;
    public AnimationClip Eat;
    public AnimationClip Attack;
    public AnimationClip Death;
    public AnimationClip Rest;
    public AnimationClip Sleep;
    public AnimationClip Drink;
    public AnimationClip Stalk;
    public AnimationClip Swim;
    [Range(0f, 1f)] public float AttackHitNormalized = 0.12f;
    [Min(0.1f)] public float ModelHeightMeters = 1.84f;
    [Min(0.1f)] public float WalkMetersPerSecond = 1f;
    [Min(0.1f)] public float RunMetersPerSecond = 4f;

    public CreatureVisualDto Snapshot() => new(MalePrefab, FemalePrefab, Idle, Walk, Run,
        Mathf.Max(0.1f, ModelHeightMeters), Mathf.Max(0.1f, WalkMetersPerSecond),
        Mathf.Max(WalkMetersPerSecond + 0.1f, RunMetersPerSecond))
        { Eat = Eat, Attack = Attack, Death = Death, Rest = Rest, Sleep = Sleep, Drink = Drink, Stalk = Stalk, Swim = Swim,
            AttackHitNormalized = Mathf.Clamp01(AttackHitNormalized) };
}

public sealed record CreatureVisualDto(GameObject MalePrefab, GameObject FemalePrefab,
    AnimationClip Idle, AnimationClip Walk, AnimationClip Run, float ModelHeightMeters,
    float WalkMetersPerSecond, float RunMetersPerSecond)
{
    public AnimationClip Eat { get; init; }
    public AnimationClip Attack { get; init; }
    public AnimationClip Death { get; init; }
    public AnimationClip Rest { get; init; }
    public AnimationClip Sleep { get; init; }
    public AnimationClip Drink { get; init; }
    public AnimationClip Stalk { get; init; }
    public AnimationClip Swim { get; init; }
    public float AttackHitNormalized { get; init; }
}
