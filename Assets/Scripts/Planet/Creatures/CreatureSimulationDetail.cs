public enum CreatureSimulationDetail { Full, Reduced, Coarse }

public static class CreatureSimulationPolicy
{
    public static bool RequiresFullDetail(CreatureBehaviour behaviour) => behaviour is
        CreatureBehaviour.Flee or CreatureBehaviour.Stalk or CreatureBehaviour.Chase or
        CreatureBehaviour.Attack or CreatureBehaviour.Recover or CreatureBehaviour.Circle or
        CreatureBehaviour.Defend or CreatureBehaviour.Threaten or CreatureBehaviour.Feed or CreatureBehaviour.Drink;

    public static CreatureSimulationDetail Select(CreatureSimulationDetail previous, float distance,
        bool resident, bool important)
    {
        if (!resident) return CreatureSimulationDetail.Coarse;
        if (important) return CreatureSimulationDetail.Full;
        return distance <= (previous == CreatureSimulationDetail.Full ? 100f : 80f)
            ? CreatureSimulationDetail.Full : CreatureSimulationDetail.Reduced;
    }

    public static double PerceptionInterval(CreatureSimulationDetail detail) =>
        detail == CreatureSimulationDetail.Reduced ? 1d : .5d;
}
