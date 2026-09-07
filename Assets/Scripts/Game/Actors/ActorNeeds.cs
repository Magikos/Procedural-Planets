using System;

/// <summary>Authority-owned need levels: zero is satisfied, one is maximum urgency.</summary>
public readonly struct ActorNeeds : IEquatable<ActorNeeds>
{
    public readonly double Hunger;
    public readonly double Thirst;

    public ActorNeeds(double hunger, double thirst)
    {
        if (!IsLevel(hunger) || !IsLevel(thirst)) throw new ArgumentOutOfRangeException(nameof(hunger));
        Hunger = hunger;
        Thirst = thirst;
    }

    public static bool IsLevel(double value) => value >= 0d && value <= 1d;

    public ActorNeeds Advance(double seconds, double hungerSeconds, double thirstSeconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d ||
            double.IsNaN(hungerSeconds) || double.IsInfinity(hungerSeconds) || hungerSeconds < 0d ||
            double.IsNaN(thirstSeconds) || double.IsInfinity(thirstSeconds) || thirstSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        return new ActorNeeds(Math.Min(1d, Hunger + (hungerSeconds > 0d ? seconds / hungerSeconds : 0d)),
            Math.Min(1d, Thirst + (thirstSeconds > 0d ? seconds / thirstSeconds : 0d)));
    }

    public bool Equals(ActorNeeds other) => Hunger == other.Hunger && Thirst == other.Thirst;
    public override bool Equals(object obj) => obj is ActorNeeds other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Hunger, Thirst);
}
