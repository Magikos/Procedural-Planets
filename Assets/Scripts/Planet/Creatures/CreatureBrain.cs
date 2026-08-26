using System;
using UnityEngine;

/// <summary>
/// What a creature's behaviour is doing, as a value small enough to write down. The machine is rebuilt from
/// this on promotion, so a creature that was fleeing when you walked away is still fleeing when you return.
/// </summary>
/// <remarks>
/// Never renumber: this is written to the delta record's state byte once demotion persists. See the design
/// doc, section 15 - behaviour persists as an ID, never as the live machine.
/// </remarks>
public enum CreatureBehaviour : byte
{
    Wander = 0,
    Flee = 1,
}

/// <summary>
/// Everything a creature's states are allowed to know, handed to them fresh each tick. Per-actor data lives
/// here rather than on the states, which is what lets one set of states drive every animal of a species.
/// </summary>
public struct CreatureContext
{
    // --- perception, written by the brain before the machine ticks ---
    public Vector3 Position;
    public Vector3 Up;
    public Vector3 Forward;
    public Vector3 Home;
    public float DistanceToHome;
    public float DeltaTime;
    public uint Tick;

    public bool HasThreat;
    public Vector3 ThreatPosition;
    public float ThreatDistance;

    public CreatureSpeciesDto Species;
    public uint Seed;

    // --- intent, written by the states ---
    /// <summary>Signed turn rate in degrees per second. The host scales it by dt.</summary>
    public float TurnDegreesPerSecond;

    /// <summary>0 stands still, 1 walks. Multiplied by the species walk speed by the host.</summary>
    public float Throttle;

    /// <summary>Multiplier on the species walk speed - fleeing is faster than grazing.</summary>
    public float SpeedScale;
}

/// <summary>
/// A creature's decision-making, behind the same <see cref="IInputProvider"/> seam a keyboard sits behind. The
/// driver below cannot tell an animal from a player, which is what lets an authority process with no input
/// device tick both the same way.
/// </summary>
public sealed class CreatureBrain : IInputProvider
{
    static readonly WanderState Wander = new();
    static readonly FleeState Flee = new();

    readonly AdaptiveStateMachine<CreatureContext> _machine;
    CreatureContext _context;

    public CreatureBrain(int seed, CreatureSpeciesDto species, CreatureBehaviour start,
        Action<string, string> onTransition = null)
    {
        _context.Seed = ScatterHash.Mix(unchecked((uint)seed));
        _context.Species = species;

        _machine = new AdaptiveStateMachine<CreatureContext>()
            .WithStates(Wander, Flee)
            .WithTransitions(
                // Fear overrides whatever the animal was doing, from any state. ResolveTo is constant here,
                // but the seam is what lets a later predator resolve to Hunt or Flee by context.
                new StateTransition<CreatureContext>
                {
                    From = null,
                    Condition = c => c.HasThreat,
                    ResolveTo = (_, _) => typeof(FleeState),
                });

        _machine.OnTransition = onTransition;
        _machine.Start(ref _context, TypeOf(start));
    }

    /// <summary>The behaviour to write down when this creature stops being simulated.</summary>
    public CreatureBehaviour Behaviour =>
        _machine.CurrentStateType == typeof(FleeState) ? CreatureBehaviour.Flee : CreatureBehaviour.Wander;

    static Type TypeOf(CreatureBehaviour behaviour) =>
        behaviour == CreatureBehaviour.Flee ? typeof(FleeState) : typeof(WanderState);

    /// <summary>Perception. Called once per tick before <see cref="Sample"/>.</summary>
    public void Observe(in CreatureContext senses)
    {
        // Keep the fields the states own; overwrite only what the world told us.
        _context.Position = senses.Position;
        _context.Up = senses.Up;
        _context.Forward = senses.Forward;
        _context.Home = senses.Home;
        _context.DistanceToHome = senses.DistanceToHome;
        _context.DeltaTime = senses.DeltaTime;
        _context.Tick = senses.Tick;
        _context.HasThreat = senses.HasThreat;
        _context.ThreatPosition = senses.ThreatPosition;
        _context.ThreatDistance = senses.ThreatDistance;
        _context.Species = senses.Species;
    }

    public ActorIntent Sample(uint tick)
    {
        _context.TurnDegreesPerSecond = 0f;
        _context.Throttle = 0f;
        _context.SpeedScale = 1f;

        _machine.Tick(ref _context);

        return new ActorIntent(
            new Vector2(0f, _context.Throttle),
            new Vector2(_context.TurnDegreesPerSecond, 0f),
            ActorButtons.None,
            tick);
    }

    /// <summary>Speed multiplier the current behaviour asked for, read by the host after <see cref="Sample"/>.</summary>
    public float SpeedScale => _context.SpeedScale;
}

/// <summary>Graze, drift, and stay near home. The default life of an animal nobody is bothering.</summary>
sealed class WanderState : IState<CreatureContext>
{
    const float TurnDegreesPerSecond = 45f;
    const float HeadingChangeSeconds = 4f;
    const float RestChance = 0.25f;

    public void Enter(ref CreatureContext c) { }
    public void Exit(ref CreatureContext c) { }

    // Wander never ends itself: it is what a creature does when nothing else is happening. Leaving it is a
    // transition driven by the world (a threat appeared), not a decision the state makes.
    public Type EvaluateExit(ref CreatureContext c) => null;

    public void Update(ref CreatureContext c)
    {
        // One draw per HeadingChangeSeconds of a 50 Hz tick, so the same creature makes the same decisions in
        // the same order however the frame rate varies.
        uint bucket = c.Tick / (uint)Mathf.Max(1, Mathf.RoundToInt(HeadingChangeSeconds * 50f));
        uint h = ScatterHash.Mix(c.Seed ^ (bucket * 0x9e3779b1u));

        float wanderTurn = (ScatterHash.To01(h) * 2f - 1f) * TurnDegreesPerSecond;
        bool resting = ScatterHash.To01(ScatterHash.Slot(h, 3)) < RestChance;

        // Beyond the home range the wander is overruled by the pull home; inside it the pull is zero and the
        // animal is free. Same arithmetic the unobserved fast-forward applies in one step.
        float range = c.Species?.HomeRangeMeters ?? 120f;
        float pull = Mathf.Clamp01((c.DistanceToHome - range) / range);
        float homeBearing = Bearing(c.Position, c.Forward, c.Up, c.Home);

        c.TurnDegreesPerSecond = Mathf.Lerp(wanderTurn, homeBearing, pull);
        c.Throttle = resting && pull <= 0f ? 0f : 1f;
        c.SpeedScale = 1f;
    }

    internal static float Bearing(Vector3 from, Vector3 forward, Vector3 up, Vector3 target)
    {
        if (!CharacterMath.TryProjectOntoTangent(target - from, up, out Vector3 toTarget) ||
            !CharacterMath.TryProjectOntoTangent(forward, up, out Vector3 face))
            return 0f;
        return Vector3.SignedAngle(face, toTarget, up);
    }
}

/// <summary>Run directly away from the threat, and keep running while it is still there.</summary>
sealed class FleeState : IState<CreatureContext>
{
    const float TurnDegreesPerSecond = 220f;   // panic turns hard
    const float SpeedScale = 2.4f;

    public void Enter(ref CreatureContext c) { }
    public void Exit(ref CreatureContext c) { }

    /// <summary>
    /// Flee ends ITSELF the moment nothing is chasing it. The perception step already applies the awareness
    /// radius, so "no threat this tick" means the animal has outrun it or lost it.
    /// </summary>
    public Type EvaluateExit(ref CreatureContext c) => c.HasThreat ? null : typeof(WanderState);

    public void Update(ref CreatureContext c)
    {
        // Away from the threat: the bearing TO it, turned around. Turning rather than snapping keeps the run
        // readable and stops an animal pivoting on the spot when a threat crosses in front of it.
        float toThreat = WanderState.Bearing(c.Position, c.Forward, c.Up, c.ThreatPosition);
        float away = Mathf.DeltaAngle(0f, toThreat + 180f);

        c.TurnDegreesPerSecond = Mathf.Clamp(away, -TurnDegreesPerSecond, TurnDegreesPerSecond);
        c.Throttle = 1f;
        c.SpeedScale = SpeedScale;
    }
}
