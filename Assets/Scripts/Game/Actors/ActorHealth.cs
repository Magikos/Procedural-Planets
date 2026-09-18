using System;

public sealed class ActorRecoveryProfile
{
    public double DelaySeconds { get; }
    public double HealthSeconds { get; }
    public double WoundSeconds { get; }
    public double ClotSeconds { get; }
    public double BleedDrainSeconds { get; }
    public ActorRecoveryProfile(double delaySeconds, double healthSeconds, double woundSeconds, double clotSeconds, double bleedDrainSeconds)
    {
        foreach (double value in new[] { delaySeconds, healthSeconds, woundSeconds, clotSeconds, bleedDrainSeconds })
            if (!double.IsFinite(value) || value <= 0d) throw new ArgumentOutOfRangeException(nameof(delaySeconds));
        DelaySeconds = delaySeconds; HealthSeconds = healthSeconds; WoundSeconds = woundSeconds;
        ClotSeconds = clotSeconds; BleedDrainSeconds = bleedDrainSeconds;
    }
}

/// <summary>Authority-owned health and injuries. Time is supplied by the world's simulation clock.</summary>
public sealed class ActorHealth
{
    public int Maximum { get; }
    public double Current { get; private set; }
    public double Wounds { get; private set; }
    public double Bleeding { get; private set; }
    public double SafeSeconds { get; private set; }
    public double Fraction => Current / Maximum;
    public bool Alive => Current > 0d;

    public ActorHealth(int maximum, double? current = null, double wounds = 0d, double bleeding = 0d, double safeSeconds = 0d)
    {
        double health = current ?? maximum;
        if (maximum <= 0 || !double.IsFinite(health) || health < 0d || health > maximum ||
            !ActorNeeds.IsLevel(wounds) || !ActorNeeds.IsLevel(bleeding) || !double.IsFinite(safeSeconds) || safeSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        Maximum = maximum; Current = health; Wounds = wounds; Bleeding = bleeding; SafeSeconds = safeSeconds;
    }

    public double Damage(double amount, double wounds = 0d, double bleeding = 0d)
    {
        if (!double.IsFinite(amount) || amount < 0d || !ActorNeeds.IsLevel(wounds) || !ActorNeeds.IsLevel(bleeding))
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (!Alive || amount == 0d) return 0d;
        double applied = Math.Min(Current, amount);
        Current -= applied; SafeSeconds = 0d;
        Wounds = Math.Min(1d, Wounds + wounds); Bleeding = Math.Min(1d, Bleeding + bleeding);
        return applied;
    }

    public void Advance(double seconds, in ActorNeeds needs, ActorExertion exertion, bool threatened, ActorRecoveryProfile profile)
    {
        if (!double.IsFinite(seconds) || seconds < 0d || profile == null || !Enum.IsDefined(typeof(ActorExertion), exertion))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!Alive || seconds == 0d) return;
        bool safe = !threatened && exertion != ActorExertion.Run;
        double delay = Math.Max(0d, profile.DelaySeconds - SafeSeconds);
        SafeSeconds = safe ? SafeSeconds + seconds : 0d;
        double clotTime = Bleeding * profile.ClotSeconds;
        double bleedingTime = Math.Min(seconds, clotTime);
        double remaining = Math.Max(0d, Bleeding - seconds / profile.ClotSeconds);
        Current = Math.Max(0d, Current - Maximum * (Bleeding + remaining) * .5d * bleedingTime / profile.BleedDrainSeconds);
        Bleeding = remaining;
        if (!Alive || !safe || needs.Hunger >= .95d || needs.Thirst >= .95d) return;
        double healingTime = Math.Max(0d, seconds - Math.Max(delay, clotTime));
        double rate = (1d - .5d * Math.Max(needs.Hunger, needs.Thirst)) *
            (exertion == ActorExertion.Sleep ? 2d : exertion == ActorExertion.Rest ? 1d : .25d);
        Current = Math.Min(Maximum, Current + Maximum * healingTime * rate / profile.HealthSeconds);
        Wounds = Math.Max(0d, Wounds - healingTime * rate / profile.WoundSeconds);
    }
}
