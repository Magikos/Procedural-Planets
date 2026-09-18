using System;

public sealed partial class CreatureResidencyService
{
    static void RestoreEndurance(Resident r, CreatureSpeciesDto species, CreatureEnduranceState saved)
    {
        r.Endurance = species.Endurance.HasValue ? saved.Restore() ?? new ActorEndurance() : null;
    }

    static void AdvanceOfflineEndurance(Resident r, CreatureSpeciesDto species, double seconds)
    {
        if (r.Endurance == null || !species.Endurance.HasValue || seconds <= 0d) return;
        // An absent actor can recover its breath. Only an actor already sleeping earns sleep recovery.
        // Bound the catch-up: unobserved time is not proof of hours of safe, uninterrupted sleep.
        double elapsed = Math.Min(seconds, species.Endurance.Value.SleepSeconds);
        r.Endurance.Advance(elapsed, r.Needs,
            r.Behaviour == CreatureBehaviour.Sleep ? ActorExertion.Sleep : ActorExertion.Rest,
            species.Endurance.Value, WoundFraction(r, species));
    }

    static void PrepareEnduranceSenses(Resident r, CreatureSpeciesDto species, ref CreatureSenses senses)
    {
        if (!species.Endurance.HasValue) return;
        r.Endurance ??= new ActorEndurance();
        var endurance = r.Endurance;
        bool supported = r.Flight == null || senses.AltitudeMeters <= .01f;
        bool safe = !senses.HasThreat && r.AlarmUntilUnix <= r.LastSimulatedUnix;
        senses.NeedsRecovery = endurance.Recovering;
        senses.NeedsSleep = endurance.NeedsSleep;
        senses.CanRest = supported && safe && (endurance.Recovering || endurance.NeedsSleep
            || endurance.Fatigue >= .35d && endurance.Fraction < .9d);
        senses.CanSleep = senses.CanRest && endurance.NeedsSleep;
        if (senses.Attack != null) senses.CanAttack &= !endurance.Recovering && endurance.Fraction >= .15d;
    }

    static float EnduranceSpeedScale(Resident r) => r.Endurance != null && r.Brain != null
        && r.Brain.SpeedScale > 1.5f ? (float)r.Endurance.RunSpeedScale : 1f;

    static void FinishEnduranceStep(Resident r, CreatureSpeciesDto species, float dt, float speedSquared)
    {
        if (r.Endurance == null || !species.Endurance.HasValue) return;
        ActorExertion exertion = r.Driver?.Swimming == true ? ActorExertion.Run :
            ActorEndurance.ClassifyExertion(speedSquared, r.Brain.SpeedScale > 1.5f, r.Behaviour == CreatureBehaviour.Sleep);
        r.Endurance.Advance(dt, r.Needs, exertion, species.Endurance.Value, WoundFraction(r, species));
    }

    static double WoundFraction(Resident r, CreatureSpeciesDto species) =>
        species.MaxHealth > 0 ? Math.Clamp(1d - (double)r.Health / species.MaxHealth, 0d, 1d) : 0d;
}
