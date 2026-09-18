using System;

[Flags]
public enum ResourceKind { None = 0, Plants = 1, Meat = 2, FreshWater = 4, SaltWater = 8 }

/// <summary>Finite stock owned by the simulation. Presentation never grants nutrition.</summary>
public sealed class ActorResourceSource
{
    public ResourceKind Kind { get; }
    public double Remaining { get; private set; }
    public double HungerPerUnit { get; }
    public double ThirstPerUnit { get; }
    public double Capacity { get; }
    public double RenewalPerSecond { get; }

    public ActorResourceSource(ResourceKind kind, double quantity, double hungerPerUnit, double thirstPerUnit,
        double renewalPerSecond = 0d, double? capacity = null)
    {
        Validate(quantity); Validate(hungerPerUnit); Validate(thirstPerUnit);
        Validate(renewalPerSecond); Validate(capacity ?? quantity);
        if (capacity.HasValue && capacity.Value < quantity) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (!Enum.IsDefined(typeof(ResourceKind), kind) || kind == ResourceKind.None)
            throw new ArgumentOutOfRangeException(nameof(kind));
        Kind = kind; Remaining = quantity; HungerPerUnit = hungerPerUnit; ThirstPerUnit = thirstPerUnit;
        Capacity = capacity ?? quantity; RenewalPerSecond = renewalPerSecond;
    }

    /// <summary>World-clock renewal. Disabled sources must not be advanced by their host.</summary>
    public double Advance(double seconds)
    {
        Validate(seconds);
        double previous = Remaining;
        Remaining = Math.Min(Capacity, Remaining + RenewalPerSecond * seconds);
        return Remaining - previous;
    }

    public bool Accepts(ResourceKind diet) => Remaining > 0d && (diet & Kind) != 0;

    /// <summary>Decay removes stock without granting nutrition. A cap makes repeated age evaluation idempotent.</summary>
    public void LimitRemaining(double maximum)
    {
        Validate(maximum);
        Remaining = Math.Min(Remaining, maximum);
    }

    public double Consume(ref ActorNeeds needs, ResourceKind diet, double requested)
    {
        Validate(requested);
        if (!Accepts(diet)) return 0d;
        double useful = Math.Max(HungerPerUnit > 0d ? needs.Hunger / HungerPerUnit : 0d,
            ThirstPerUnit > 0d ? needs.Thirst / ThirstPerUnit : 0d);
        double amount = Math.Min(Math.Min(requested, Remaining), useful);
        needs = new ActorNeeds(Math.Max(0d, needs.Hunger - amount * HungerPerUnit),
            Math.Max(0d, needs.Thirst - amount * ThirstPerUnit));
        Remaining -= amount;
        return amount;
    }

    static void Validate(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}
