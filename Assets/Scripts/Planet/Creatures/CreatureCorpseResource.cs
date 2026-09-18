using System;

/// <summary>Authority-owned flesh and game age. Harvest loot remains a separate entitlement.</summary>
public sealed class CreatureCorpseResource
{
    public ActorResourceSource Meat { get; }
    public double InitialMeat { get; }
    public double AgeSeconds { get; private set; }
    public CorpseDecay Decay { get; }
    public double MeatFraction => InitialMeat > 0d ? Meat.Remaining / InitialMeat : 0d;
    public CorpseStage Stage => Decay.StageAtAge(AgeSeconds);
    public bool HasFlies => Decay.HasFliesAtAge(AgeSeconds, MeatFraction);

    public CreatureCorpseResource(double initialMeat, CorpseDecay decay, double ageSeconds = 0d, double? remaining = null)
    {
        if (double.IsNaN(initialMeat) || double.IsInfinity(initialMeat) || initialMeat <= 0d)
            throw new ArgumentOutOfRangeException(nameof(initialMeat));
        InitialMeat = initialMeat; Decay = decay;
        Meat = new ActorResourceSource(ResourceKind.Meat, initialMeat, 1d, 0d);
        if (remaining.HasValue)
        {
            if (remaining.Value > initialMeat) throw new ArgumentOutOfRangeException(nameof(remaining));
            Meat.LimitRemaining(remaining.Value);
        }
        Advance(ageSeconds);
    }

    public void Advance(double gameSeconds)
    {
        if (double.IsNaN(gameSeconds) || double.IsInfinity(gameSeconds) || gameSeconds < 0d ||
            double.IsInfinity(AgeSeconds + gameSeconds)) throw new ArgumentOutOfRangeException(nameof(gameSeconds));
        AgeSeconds += gameSeconds;
        Meat.LimitRemaining(InitialMeat * Decay.NaturalMeatFraction(AgeSeconds));
    }
}
