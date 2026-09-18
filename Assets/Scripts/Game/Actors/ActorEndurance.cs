using System;

public enum ActorExertion { Rest, Walk, Run, Sleep }

public readonly struct ActorEnduranceProfile
{
    public readonly double SprintSeconds, AwakeSeconds, SleepSeconds, RestRecoverySeconds, WalkRecoverySeconds;
    public ActorEnduranceProfile(double sprintSeconds, double awakeSeconds, double sleepSeconds,
        double restRecoverySeconds = 16d, double walkRecoverySeconds = 50d)
    {
        foreach (double value in new[] { sprintSeconds, awakeSeconds, sleepSeconds, restRecoverySeconds, walkRecoverySeconds })
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0d) throw new ArgumentOutOfRangeException(nameof(sprintSeconds));
        SprintSeconds = sprintSeconds; AwakeSeconds = awakeSeconds; SleepSeconds = sleepSeconds;
        RestRecoverySeconds = restRecoverySeconds; WalkRecoverySeconds = walkRecoverySeconds;
    }
}

/// <summary>Authority-owned reserves. Durations use simulation seconds; rendering never modifies them.</summary>
public sealed class ActorEndurance
{
    /// <summary>Resource travel uses moving exertion; only a stationary actor receives rest or sleep recovery.</summary>
    public static ActorExertion ClassifyExertion(double speedSquared, bool running, bool sleeping)
    {
        if (!double.IsFinite(speedSquared) || speedSquared < 0d) throw new ArgumentOutOfRangeException(nameof(speedSquared));
        return speedSquared < .0004d ? sleeping ? ActorExertion.Sleep : ActorExertion.Rest
            : running ? ActorExertion.Run : ActorExertion.Walk;
    }

    public double Stamina { get; private set; }
    public double Capacity { get; private set; }
    public double Fatigue { get; private set; }
    public bool Recovering { get; private set; }
    public bool NeedsSleep { get; private set; }
    public double Fraction => Stamina / Capacity;
    public double RunSpeedScale => .4d + .6d * Math.Clamp((Fraction - .1d) / .3d, 0d, 1d);

    public ActorEndurance(double stamina = 1d, double fatigue = 0d, double capacity = 1d,
        bool recovering = false, bool needsSleep = false)
    {
        if (!ActorNeeds.IsLevel(stamina) || !ActorNeeds.IsLevel(fatigue) ||
            capacity < .55d || capacity > 1d || double.IsNaN(capacity) || stamina > capacity)
            throw new ArgumentOutOfRangeException(nameof(stamina));
        Stamina = stamina; Fatigue = fatigue; Capacity = capacity;
        Recovering = recovering; NeedsSleep = needsSleep; UpdateThresholds();
    }

    public static double TargetCapacity(in ActorNeeds needs, double fatigue)
    {
        if (!ActorNeeds.IsLevel(fatigue)) throw new ArgumentOutOfRangeException(nameof(fatigue));
        double hunger = Math.Clamp((needs.Hunger - .85d) / .15d, 0d, 1d);
        double thirst = Math.Clamp((needs.Thirst - .8d) / .2d, 0d, 1d);
        return Math.Max(.55d, 1d - hunger * .15d - thirst * .2d - fatigue * .25d);
    }

    public double SpendUpTo(double cost)
    {
        if (!ActorNeeds.IsLevel(cost) || cost == 0d) throw new ArgumentOutOfRangeException(nameof(cost));
        double spent = Math.Min(cost, Stamina);
        Stamina -= spent; UpdateThresholds();
        return spent / cost;
    }

    public void Advance(double seconds, in ActorNeeds needs, ActorExertion exertion, in ActorEnduranceProfile profile, double wounds = 0d)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d ||
            !Enum.IsDefined(typeof(ActorExertion), exertion) || profile.SprintSeconds <= 0d || profile.AwakeSeconds <= 0d ||
            profile.SleepSeconds <= 0d || profile.RestRecoverySeconds <= 0d || profile.WalkRecoverySeconds <= 0d)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!ActorNeeds.IsLevel(wounds)) throw new ArgumentOutOfRangeException(nameof(wounds));
        Fatigue = Math.Clamp(Fatigue + seconds * (exertion == ActorExertion.Sleep ? -1d / profile.SleepSeconds :
            (exertion == ActorExertion.Run ? 2d : 1d) / profile.AwakeSeconds), 0d, 1d);
        double target = Math.Max(.55d, TargetCapacity(needs, Fatigue) - wounds * .2d);
        Capacity += Math.Clamp(target - Capacity, -seconds * .08d, seconds * .08d);
        double rate = exertion == ActorExertion.Run ? -1d / profile.SprintSeconds :
            (.6d + .4d * Capacity) / (exertion == ActorExertion.Walk ? profile.WalkRecoverySeconds : profile.RestRecoverySeconds);
        Stamina = Math.Clamp(Stamina + seconds * rate, 0d, Capacity);
        UpdateThresholds();
    }
    void UpdateThresholds()
    {
        if (Fraction <= .1d) Recovering = true;
        else if (Fraction >= .65d) Recovering = false;
        if (Fatigue >= .8d) NeedsSleep = true;
        else if (Fatigue <= .25d) NeedsSleep = false;
    }
}
