using Unity.Mathematics;
using UnityEngine;

public struct FishAgentState
{
    public Vector3 Position, Next, Heading, Normal, LastThreat;
    public float Depth, Alarm;
    public WaterSample Water;
}

public struct FishSchoolState
{
    public Vector3 Heading;
    public double Seconds;
    public ushort Body;
    public FishRules Rules;
    public bool Viable;
    public int Fleeing;
}

public struct FishRules
{
    public float Clearance, Speed, MinimumDepth, QueryMargin;
    public bool HasSpecies, Ocean;
    public static FishRules From(FishSpecies species, float? clearance = null) => new()
    {
        Clearance = clearance ?? species?.Clearance ?? .2f, Speed = species?.SwimSpeed ?? 1.5f,
        MinimumDepth = species?.MinimumWaterDepth ?? 0f,
        HasSpecies = species != null, Ocean = species?.Habitat == FishHabitat.Ocean
    };
    public bool Accepts(WaterSample water) => !HasSpecies ||
        (water.IsOcean == Ocean && water.BodyDepth >= MinimumDepth + QueryMargin);
}

public interface IFishStateBuffer
{
    int Count { get; }
    FishAgentState this[int index] { get; set; }
}

public interface IFishThreatQuery
{
    bool TryFind(int index, Vector3 position, out Vector3 threat);
}

// Managed fixtures and native jobs run the same ordered school algorithm.
public static class FishSimulation
{
    public static void Tick<TBuffer, TWater, TThreat>(ref FishSchoolState school, TBuffer agents,
        TWater water, TThreat threats, float dt, float queryMargin = 0f)
        where TBuffer : struct, IFishStateBuffer
        where TWater : struct, IWaterQueryService
        where TThreat : struct, IFishThreatQuery
    {
        if (dt <= 0f || !school.Viable) return;
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 30f));
        float step = dt / steps;
        var rules = school.Rules;
        rules.QueryMargin = queryMargin;
        for (int tick = 0; tick < steps; tick++)
        {
            school.Seconds += step;
            Vector3 center = Vector3.zero;
            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (!FishMovement.TryGetHabitat(water, agent.Position, school.Body, rules, out agent.Water))
                { school.Viable = false; return; }
                agents[i] = agent;
                center += agent.Position;
            }
            center /= agents.Count;
            school.Heading = Rotate(school.Heading, agents[0].Normal,
                Mathf.Sin((float)school.Seconds * .3f) * step * 20f);
            bool blocked = false;
            school.Fleeing = 0;
            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                Vector3 position = agent.Position;
                var sample = agent.Water;
                agent.Normal = sample.Normal;
                Vector3 steering = Vector3.ProjectOnPlane(school.Heading, sample.Normal).normalized +
                    Vector3.ClampMagnitude(center - position, 2f) * .5f;
                float desiredDepth = Mathf.Clamp(agent.Depth + Mathf.Sin((float)school.Seconds * .25f + i) * .3f,
                    rules.Clearance * 1.5f, sample.BodyDepth - rules.Clearance * 1.5f);
                steering += sample.Normal * Mathf.Clamp(sample.SignedDepth - desiredDepth, -1f, 1f);
                for (int j = 0; j < agents.Count; j++)
                {
                    Vector3 delta = position - agents[j].Position;
                    if (j != i && delta.sqrMagnitude > .001f && delta.sqrMagnitude < 1f)
                        steering += delta / delta.sqrMagnitude;
                }
                float speed = rules.Speed;
                if (threats.TryFind(i, position, out Vector3 threat))
                { agent.LastThreat = threat; agent.Alarm = 3f; }
                else agent.Alarm = Mathf.Max(0f, agent.Alarm - step);
                bool fleeing = agent.Alarm > 0f;
                if (fleeing)
                {
                    steering = position - agent.LastThreat;
                    if (steering.sqrMagnitude < .001f) steering = -agent.Heading;
                    Vector3 escape = Vector3.ProjectOnPlane(steering, sample.Normal);
                    if (escape.sqrMagnitude < .001f) escape = Vector3.ProjectOnPlane(agent.Heading, sample.Normal);
                    if (escape.sqrMagnitude < .001f) escape = CharacterMath.ArbitraryTangent(sample.Normal);
                    steering = escape.normalized + sample.Normal * Mathf.Clamp(Vector3.Dot(steering.normalized, sample.Normal), -.5f, .5f);
                    school.Heading = escape.normalized;
                    speed *= 2f;
                    school.Fleeing++;
                }
                Vector3 heading = TurnTowards(agent.Heading, steering.normalized, step * (fleeing ? 6f : 2f));
                Vector3 desired = position + heading * (speed * step);
                if (fleeing)
                {
                    float depth = sample.SignedDepth - Vector3.Dot(desired - position, sample.Normal);
                    desired += sample.Normal * (depth - Mathf.Clamp(depth, rules.Clearance * 1.1f, sample.BodyDepth - rules.Clearance * 1.1f));
                }
                if (!FishMovement.TryMoveFromHabitat(water, position, desired, school.Body, rules, out agent.Next))
                { blocked = true; heading = Rotate(agent.Heading, sample.Normal, step * 120f); }
                agent.Heading = heading;
                agents[i] = agent;
            }
            for (int i = 0; i < agents.Count; i++)
            { var agent = agents[i]; agent.Position = agent.Next; agents[i] = agent; }
            if (blocked) school.Heading = Rotate(school.Heading, agents[0].Normal, step * 120f);
        }
    }

    static Vector3 Rotate(Vector3 value, Vector3 axis, float degrees) =>
        math.mul(quaternion.AxisAngle(math.normalizesafe((float3)axis), math.radians(degrees)), (float3)value);

    // Vector3.RotateTowards calls into Unity's native API, which Burst cannot invoke.
    public static Vector3 TurnTowards(Vector3 current, Vector3 target, float radians)
    {
        if (target.sqrMagnitude < 1e-12f || radians <= 0f) return current;
        float angle = math.acos(math.clamp(Vector3.Dot(current, target), -1f, 1f));
        if (angle <= radians) return target;
        Vector3 axis = Vector3.Cross(current, target);
        if (axis.sqrMagnitude < 1e-12f) axis = CharacterMath.ArbitraryTangent(current);
        return math.mul(quaternion.AxisAngle(math.normalizesafe((float3)axis), radians), (float3)current);
    }
}

