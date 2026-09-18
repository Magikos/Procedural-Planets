using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Creature Life")]
public sealed class CreatureLifeSettings : ScriptableObject
{
    [Min(.01f)] public float AdultDays = 4, ElderDays = 60, MinimumDeathDays = 90, MaximumDeathDays = 120;
    [Min(.01f)] public float GestationDays = 2, BirthCooldownDays = 4;
    [Range(1, 16)] public int LitterSize = 2;
    public ActorLifeProfile Snapshot() => new ActorLifeProfile(AdultDays, ElderDays, MinimumDeathDays,
        MaximumDeathDays, GestationDays, BirthCooldownDays, LitterSize).Validate();
}
