using System;

public enum ActorLifeStage { Juvenile, Adult, Elder }
public enum ActorSex { Female, Male }
public enum ActorDeathCause { None, Attack, Starvation, Thirst, Bleeding, OldAge }

/// <summary>Durations use world days, independent of rendering or simulation tick frequency.</summary>
public sealed record ActorLifeProfile(double AdultDays = 4, double ElderDays = 60,
    double MinimumDeathDays = 90, double MaximumDeathDays = 120,
    double GestationDays = 2, double BirthCooldownDays = 4, int LitterSize = 2)
{
    public ActorLifeProfile Validate()
    {
        foreach (double value in new[] { AdultDays, ElderDays, MinimumDeathDays, MaximumDeathDays, GestationDays, BirthCooldownDays })
            if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(AdultDays));
        if (ElderDays <= AdultDays || MinimumDeathDays <= ElderDays || MaximumDeathDays < MinimumDeathDays || LitterSize < 1 || LitterSize > 16)
            throw new ArgumentOutOfRangeException(nameof(ElderDays));
        return this;
    }
}

/// <summary>Serializable authority values. A host allocates offspring identities and validates habitat and mates.</summary>
[Serializable]
public sealed class ActorLifeCycle
{
    public double AgeDays { get; private set; }
    public double DeathAgeDays { get; }
    public ActorSex Sex { get; }
    public ulong MotherId { get; }
    public ulong FatherId { get; }
    public ulong MateId { get; private set; }
    public double PregnancyDays { get; private set; }
    public double CooldownDays { get; private set; }
    public bool Pregnant { get; private set; }
    public bool DiedOfAge => AgeDays >= DeathAgeDays;

    public ActorLifeCycle(ActorSex sex, double ageDays, double deathAgeDays, ulong motherId = 0,
        ulong fatherId = 0, bool pregnant = false, double pregnancyDays = 0, double cooldownDays = 0, ulong mateId = 0)
    {
        if (!Enum.IsDefined(typeof(ActorSex), sex) || !double.IsFinite(ageDays) || ageDays < 0 ||
            !double.IsFinite(deathAgeDays) || deathAgeDays <= 0 || !double.IsFinite(pregnancyDays) || pregnancyDays < 0 ||
            !double.IsFinite(cooldownDays) || cooldownDays < 0 || pregnant && (sex != ActorSex.Female || mateId == 0) ||
            !pregnant && pregnancyDays != 0)
            throw new ArgumentOutOfRangeException(nameof(ageDays));
        Sex = sex; AgeDays = ageDays; DeathAgeDays = deathAgeDays; MotherId = motherId; FatherId = fatherId;
        Pregnant = pregnant; PregnancyDays = pregnancyDays; CooldownDays = cooldownDays;
        MateId = mateId;
    }

    public ActorLifeStage Stage(ActorLifeProfile profile) => AgeDays < profile.AdultDays ? ActorLifeStage.Juvenile :
        AgeDays < profile.ElderDays ? ActorLifeStage.Adult : ActorLifeStage.Elder;

    public double PhysicalScale(ActorLifeProfile profile) => Stage(profile) == ActorLifeStage.Juvenile
        ? .55 + .45 * Math.Min(1, AgeDays / profile.AdultDays)
        : Stage(profile) == ActorLifeStage.Elder ? 1 - .3 * Math.Min(1, (AgeDays - profile.ElderDays) / (DeathAgeDays - profile.ElderDays)) : 1;

    public bool CanReproduce(ActorLifeProfile profile, in ActorNeeds needs, double healthFraction, double fatigue,
        bool safe, bool habitatSupportsBirth) => !DiedOfAge && Stage(profile) == ActorLifeStage.Adult && !Pregnant && CooldownDays <= 0 &&
        needs.Hunger < .45 && needs.Thirst < .45 && healthFraction >= .8 && fatigue < .5 && safe && habitatSupportsBirth;

    public bool TryConceive(ulong mateId, ActorLifeProfile profile, in ActorNeeds needs, double healthFraction,
        double fatigue, bool safe, bool habitatSupportsBirth)
    {
        if (mateId == 0 || Sex != ActorSex.Female || !CanReproduce(profile, needs, healthFraction, fatigue, safe, habitatSupportsBirth)) return false;
        Pregnant = true; PregnancyDays = 0; MateId = mateId; return true;
    }

    /// <returns>Offspring count exactly once when gestation completes. Poor resources prevent conception, not an already conceived birth.</returns>
    public int Advance(double days, ActorLifeProfile profile)
    {
        if (!double.IsFinite(days) || days < 0 || !double.IsFinite(AgeDays + days)) throw new ArgumentOutOfRangeException(nameof(days));
        AgeDays += days; CooldownDays = Math.Max(0, CooldownDays - days);
        if (!Pregnant || DiedOfAge) return 0;
        PregnancyDays += days;
        if (PregnancyDays < profile.GestationDays) return 0;
        Pregnant = false; PregnancyDays = 0; CooldownDays = profile.BirthCooldownDays;
        return profile.LitterSize;
    }
}
