using UnityEngine;

/// <summary>
/// What a creature's behaviour is doing, as a value small enough to write down. It is also the state machine's
/// dispatch key, so there is no second table mapping "which state is running" to "what we saved".
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
/// What the world told a creature this tick. Written by the host, read-only to the states.
/// </summary>
/// <remarks>
/// Separate from the intent fields on purpose. It is one assignment to hand over, so adding a sense cannot
/// silently go missing the way a field-by-field copy does.
/// </remarks>
public struct CreatureSenses
{
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
}

/// <summary>Perception plus the intent the states write back. The only thing a creature state may touch.</summary>
public struct CreatureContext
{
    public CreatureSenses Senses;

    /// <summary>Per-creature, so two animals of a species do not wander in lockstep.</summary>
    public uint Seed;

    /// <summary>Signed turn rate in degrees per second. The host scales it by dt.</summary>
    public float TurnDegreesPerSecond;

    /// <summary>0 stands still, 1 walks.</summary>
    public float Throttle;

    /// <summary>Multiplier on the species walk speed - fleeing is faster than grazing.</summary>
    public float SpeedScale;
}

/// <summary>
/// A creature's decision-making, behind the same <see cref="IInputProvider"/> seam a keyboard sits behind. The
/// locomotion driver below cannot tell an animal from a player, which is what lets an authority process with
/// no input device tick both the same way.
/// </summary>
public sealed class CreatureBrain : IInputProvider
{
    // Stateless and shared by every creature: per-actor data lives in the context, never on a state.
    static readonly IState<CreatureContext>[] States = { new WanderState(), new FleeState() };

    static readonly StateTransition<CreatureContext>[] Transitions =
    {
        // Fear overrides whatever the animal was doing, from any state. The target is constant today; the
        // seam is what lets a predator resolve to Hunt or Flee from the same condition later.
        new()
        {
            From = StateId.None,
            Condition = (in CreatureContext c) => c.Senses.HasThreat,
            ResolveTo = (int _, in CreatureContext _) => (int)CreatureBehaviour.Flee,
        },
    };

    readonly AdaptiveStateMachine<CreatureContext> _machine;
    CreatureContext _context;

    public CreatureBrain(int seed, CreatureSpeciesDto species, CreatureBehaviour start)
    {
        _context.Seed = ScatterHash.Mix(unchecked((uint)seed));
        _context.Senses.Species = species;

        _machine = new AdaptiveStateMachine<CreatureContext>(States, Transitions);
        _machine.Start(ref _context, (int)start);
    }

    /// <summary>The behaviour to write down when this creature stops being simulated.</summary>
    public CreatureBehaviour Behaviour => (CreatureBehaviour)_machine.CurrentId;

    /// <summary>Speed multiplier the current behaviour asked for, read by the host after <see cref="Sample"/>.</summary>
    public float SpeedScale => _context.SpeedScale;

    /// <summary>Perception. Called once per tick before <see cref="Sample"/>.</summary>
    public void Observe(in CreatureSenses senses) => _context.Senses = senses;

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
}

/// <summary>Graze, drift, and stay near home. The life of an animal nobody is bothering.</summary>
sealed class WanderState : IState<CreatureContext>
{
    const float TurnDegreesPerSecond = 45f;
    const float HeadingChangeSeconds = 4f;
    const float RestChance = 0.25f;

    public int Id => (int)CreatureBehaviour.Wander;

    public void Enter(ref CreatureContext c) { }
    public void Exit(ref CreatureContext c) { }

    // Wander never ends itself: it is what a creature does when nothing else is happening. Leaving it is
    // driven by the world, not by a decision this state makes.
    public int EvaluateExit(in CreatureContext c) => StateId.None;

    public void Update(ref CreatureContext c)
    {
        // One draw per HeadingChangeSeconds of a 50 Hz tick, so the same creature makes the same decisions in
        // the same order however the frame rate varies.
        uint bucket = c.Senses.Tick / (uint)Mathf.Max(1, Mathf.RoundToInt(HeadingChangeSeconds * 50f));
        uint h = ScatterHash.Mix(c.Seed ^ (bucket * 0x9e3779b1u));

        float wanderTurn = (ScatterHash.To01(h) * 2f - 1f) * TurnDegreesPerSecond;
        bool resting = ScatterHash.To01(ScatterHash.Slot(h, 3)) < RestChance;

        // Beyond the home range the wander is overruled by the pull home; inside it the pull is zero and the
        // animal is free. Same arithmetic the unobserved fast-forward applies in one step.
        float range = c.Senses.Species?.HomeRangeMeters ?? 120f;
        float pull = Mathf.Clamp01((c.Senses.DistanceToHome - range) / range);
        float homeBearing = CharacterMath.TangentBearing(
            c.Senses.Position, c.Senses.Forward, c.Senses.Up, c.Senses.Home);

        c.TurnDegreesPerSecond = Mathf.Lerp(wanderTurn, homeBearing, pull);
        c.Throttle = resting && pull <= 0f ? 0f : 1f;
        c.SpeedScale = 1f;
    }
}

/// <summary>Run away from the threat, and keep running while it is still there.</summary>
sealed class FleeState : IState<CreatureContext>
{
    const float TurnDegreesPerSecond = 220f;   // panic turns hard
    const float PanicSpeedScale = 2.4f;

    public int Id => (int)CreatureBehaviour.Flee;

    public void Enter(ref CreatureContext c) { }
    public void Exit(ref CreatureContext c) { }

    /// <summary>
    /// Flee ends ITSELF the moment nothing is chasing it. Perception has already applied the awareness
    /// radius, so "no threat this tick" means the animal has outrun it or lost it.
    /// </summary>
    public int EvaluateExit(in CreatureContext c) =>
        c.Senses.HasThreat ? StateId.None : (int)CreatureBehaviour.Wander;

    public void Update(ref CreatureContext c)
    {
        // Away from the threat: the bearing TO it, turned around. Turning rather than snapping keeps the run
        // readable and stops an animal pivoting on the spot when a threat crosses in front of it.
        float toThreat = CharacterMath.TangentBearing(
            c.Senses.Position, c.Senses.Forward, c.Senses.Up, c.Senses.ThreatPosition);
        float away = Mathf.DeltaAngle(0f, toThreat + 180f);

        c.TurnDegreesPerSecond = Mathf.Clamp(away, -TurnDegreesPerSecond, TurnDegreesPerSecond);
        c.Throttle = 1f;
        c.SpeedScale = PanicSpeedScale;
    }
}
