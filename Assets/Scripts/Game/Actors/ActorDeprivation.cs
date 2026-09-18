using System;

/// <summary>Authority-owned exposure and fractional damage. All durations use the host's simulation clock.</summary>
public sealed class ActorDeprivation
{
    public double StarvingSeconds { get; private set; }
    public double DehydratedSeconds { get; private set; }
    public double DamageRemainder { get; private set; }

    public ActorDeprivation(double starvingSeconds = 0d, double dehydratedSeconds = 0d, double damageRemainder = 0d)
    {
        Validate(starvingSeconds); Validate(dehydratedSeconds); Validate(damageRemainder);
        if (damageRemainder >= 1d) throw new ArgumentOutOfRangeException(nameof(damageRemainder));
        StarvingSeconds = starvingSeconds; DehydratedSeconds = dehydratedSeconds; DamageRemainder = damageRemainder;
    }

    /// <summary>Call after consumption, for an interval during which need saturation does not change.</summary>
    public int Advance(in ActorNeeds needs, double seconds, int maxHealth,
        double starvationGrace, double starvationFatalSeconds, double thirstGrace, double thirstFatalSeconds)
    {
        Validate(seconds); Validate(starvationGrace); Validate(starvationFatalSeconds);
        Validate(thirstGrace); Validate(thirstFatalSeconds);
        if (maxHealth <= 0 || starvationFatalSeconds <= 0d || thirstFatalSeconds <= 0d)
            throw new ArgumentOutOfRangeException(nameof(maxHealth));
        double hungerAge = needs.Hunger >= 1d ? StarvingSeconds + seconds : 0d;
        double thirstAge = needs.Thirst >= 1d ? DehydratedSeconds + seconds : 0d;
        Validate(hungerAge); Validate(thirstAge);
        double damage = DamageRemainder + maxHealth *
            (Exposure(StarvingSeconds, hungerAge, starvationGrace) / starvationFatalSeconds +
             Exposure(DehydratedSeconds, thirstAge, thirstGrace) / thirstFatalSeconds);
        // Returning at most full health also bounds casts when a debug clock advances many days.
        int whole = damage >= maxHealth ? maxHealth : (int)Math.Floor(damage + 1e-10d);
        StarvingSeconds = hungerAge; DehydratedSeconds = thirstAge;
        DamageRemainder = damage >= maxHealth ? 0d : Math.Max(0d, damage - whole);
        return whole;
    }

    static double Exposure(double before, double after, double grace) =>
        Math.Max(0d, Math.Max(0d, after - grace) - Math.Max(0d, before - grace));

    static void Validate(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}
