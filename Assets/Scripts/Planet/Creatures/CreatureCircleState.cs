using UnityEngine;

/// <summary>Read-only carrion observation from the same store that owns creature deaths and loot.</summary>
public static class CreatureCarrion
{
    public static CreatureResourceTarget Find(CreatureCorpseStore corpses, Vector3 position, float range, long now)
    {
        CreatureResourceTarget result = default;
        if (corpses == null || !float.IsFinite(range) || range <= 0f) return result;
        float nearest = range * range;
        foreach (CreatureCorpse corpse in corpses.All)
        {
            if (corpses.StageOf(corpse, now) >= CorpseStage.Bones) continue;
            float distance = (corpse.Position - position).sqrMagnitude;
            if (distance > nearest || (distance == nearest && result.Available && corpse.Id.Value >= result.Id.Value)) continue;
            nearest = distance;
            result = new CreatureResourceTarget { Id = corpse.Id, Position = corpse.Position, Available = true };
        }
        return result;
    }
}

/// <summary>Approach a carcass, then follow a tangent orbit while the flight motor maintains terrain clearance.</summary>
sealed class CreatureCircleState : IState<CreatureContext>
{
    public int Id => (int)CreatureBehaviour.Circle;
    public void Enter(ref CreatureContext c) { }
    public void Exit(ref CreatureContext c) { }
    public int EvaluateExit(in CreatureContext c) =>
        c.Objective == CreatureObjective.InvestigateCarrion && c.Senses.Carrion.Available
            ? StateId.None : (int)CreatureBehaviour.Wander;

    public void Update(ref CreatureContext c)
    {
        Vector3 outward = Vector3.ProjectOnPlane(c.Senses.Position - c.Senses.Carrion.Position, c.Senses.Up);
        float distance = outward.magnitude;
        outward = distance > 0.01f ? outward / distance : CharacterMath.ArbitraryTangent(c.Senses.Up);
        // Different radii prevent birds converging onto the same ring. All circle in one direction.
        float radius = 14f + ScatterHash.To01(c.Seed) * 12f;
        Vector3 tangent = Vector3.Cross(c.Senses.Up, outward);
        Vector3 direction = tangent - outward * Mathf.Clamp((distance - radius) / 6f, -3f, 3f);
        float bearing = CharacterMath.TangentBearing(c.Senses.Position, c.Senses.Forward,
            c.Senses.Up, c.Senses.Position + direction);
        c.TurnDegreesPerSecond = Mathf.Clamp(bearing * 3f, -100f, 100f);
        c.Throttle = 1f;
        c.SpeedScale = 1f;
    }
}
