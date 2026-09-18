using System;

/// <summary>Authority-owned fear. Confidence describes the observed encounter, not a permanent species rank.</summary>
public sealed class ActorDisposition
{
    public double Courage { get; }
    public double EffectiveCourage { get; private set; }
    public double Fear { get; private set; }
    public double Confidence { get; private set; } = 1d;

    public ActorDisposition(double courage, double fear = 0d)
    {
        if (!ActorNeeds.IsLevel(courage) || !ActorNeeds.IsLevel(fear))
            throw new ArgumentOutOfRangeException(nameof(courage));
        Courage = EffectiveCourage = courage; Fear = fear;
    }

    public void ReportHarm(double fraction)
    {
        if (!ActorNeeds.IsLevel(fraction)) throw new ArgumentOutOfRangeException(nameof(fraction));
        Fear = Math.Min(1d, Fear + fraction);
    }

    public void Advance(double seconds, double ownStrength, double opposingStrength, bool threatened, double supportStrength = 0d)
    {
        if (!double.IsFinite(seconds) || seconds < 0d || !double.IsFinite(ownStrength) || ownStrength <= 0d ||
            !double.IsFinite(opposingStrength) || opposingStrength < 0d || !double.IsFinite(supportStrength) || supportStrength < 0d)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        // Divide first to avoid overflowing the sum of two valid strengths.
        double scale = Math.Max(Math.Max(ownStrength, opposingStrength), supportStrength);
        double supported = ownStrength / scale + supportStrength / scale;
        Confidence = supported / (supported + opposingStrength / scale);
        double opposition = opposingStrength / scale;
        double disadvantage = Math.Max(0d, opposition - supported) / (opposition + supported);
        EffectiveCourage = Math.Clamp(Courage + .2d * (supportStrength / scale) / supported - .2d * disadvantage, 0d, 1d);
        double target = threatened ? Math.Clamp(.3d + .6d * (1d - Confidence) - .2d * EffectiveCourage, 0d, 1d) : 0d;
        Fear += Math.Clamp(target - Fear, -seconds / 30d, seconds / 2d);
    }
}
