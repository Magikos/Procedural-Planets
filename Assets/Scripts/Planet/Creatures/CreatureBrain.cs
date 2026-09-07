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

    /// <summary>On the ground and staying there for a while. Only a flier ever chooses it.</summary>
    Perch = 2,
    Stalk = 3,
    Chase = 4,
    Attack = 5,
    Recover = 6,
    Rest = 7,
    Sleep = 8,
    Feed = 9,
    Drink = 10,
}

public enum CreatureObjective { Roam, Hunt, Escape, FindFood, FindWater, Rest }

public struct CreatureResourceTarget
{
    public EntityId Id;
    public Vector3 Position;
    public bool Available;
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
    public float AltitudeMeters;
    public bool HasLandingTarget;
    public Vector3 LandingTarget;
    public uint Tick;

    public bool HasThreat;
    public Vector3 ThreatPosition;
    public float ThreatDistance;
    public bool HasPrey;
    public bool PreyAlert;
    public Vector3 PreyPosition;
    public EntityId PreyId;
    public ActorNeeds Needs;
    public int Health;
    public int MaxHealth;
    public float AttackDuration;
    public float AttackHitTime;
    public CreatureResourceTarget Food, Water;
    public bool CanRest;
    public bool CanSleep;

    public CreatureSpeciesDto Species;
}

/// <summary>Perception plus the intent the states write back. The only thing a creature state may touch.</summary>
public struct CreatureContext
{
    public CreatureSenses Senses;
    public CreatureObjective Objective;

    /// <summary>Per-creature, so two animals of a species do not wander in lockstep.</summary>
    public uint Seed;

    /// <summary>Signed turn rate in degrees per second. The host scales it by dt.</summary>
    public float TurnDegreesPerSecond;

    /// <summary>0 stands still, 1 walks.</summary>
    public float Throttle;

    /// <summary>Multiplier on the species walk speed - fleeing is faster than grazing.</summary>
    public float SpeedScale;

    /// <summary>
    /// Simulation time stays per creature because state instances are shared.
    /// </summary>
    public double SimulationSeconds;
    public double StateEnteredSeconds;
    public bool PerchDecisionDue;
    public double HuntStartedSeconds, HuntCooldownUntil;
    public EntityId HuntTarget;
    public uint AttackSequence;
    public bool HitConsumed, HitRequested;
}

/// <summary>
/// A creature's decision-making, behind the same <see cref="IInputProvider"/> seam a keyboard sits behind. The
/// locomotion driver below cannot tell an animal from a player, which is what lets an authority process with
/// no input device tick both the same way.
/// </summary>
public sealed class CreatureBrain : IInputProvider
{
    // Stateless and shared by every creature: per-actor data lives in the context, never on a state.
    static readonly IState<CreatureContext>[] States = { new WanderState(), new FleeState(), new PerchState(),
        new StalkState(), new ChaseState(), new AttackState(), new RecoverState(),
        new CreatureRestState(false), new CreatureRestState(true),
        new CreatureConsumeState(false), new CreatureConsumeState(true) };

    static readonly StateTransition<CreatureContext>[] Transitions =
    {
        new()
        {
            From = (int)CreatureBehaviour.Wander,
            Condition = (in CreatureContext c) => c.Objective == CreatureObjective.Rest,
            ResolveTo = (int _, in CreatureContext _) => (int)CreatureBehaviour.Rest,
        },
        new()
        {
            From = (int)CreatureBehaviour.Wander,
            Condition = (in CreatureContext c) => CreatureConsumeState.HasTarget(c),
            ResolveTo = (int _, in CreatureContext c) => (int)(c.Objective == CreatureObjective.FindWater
                ? CreatureBehaviour.Drink : CreatureBehaviour.Feed),
        },
        new()
        {
            From = (int)CreatureBehaviour.Wander,
            Condition = (in CreatureContext c) => c.Objective == CreatureObjective.Hunt,
            ResolveTo = (int _, in CreatureContext _) => (int)CreatureBehaviour.Stalk,
        },
        // Fear overrides whatever the animal was doing, from any state. The target is constant today; the
        // seam is what lets a predator resolve to Hunt or Flee from the same condition later.
        new()
        {
            From = StateId.None,
            Condition = (in CreatureContext c) => c.Objective == CreatureObjective.Escape,
            ResolveTo = (int _, in CreatureContext _) => (int)CreatureBehaviour.Flee,
        },

        // Only a flier ever lands, and only from an unbothered wander. Everything on the ground is already
        // where perching would put it, so the condition tests the one thing that makes a species a flier.
        new()
        {
            From = (int)CreatureBehaviour.Wander,
            Condition = WantsToLand,
            ResolveTo = (int _, in CreatureContext _) => (int)CreatureBehaviour.Perch,
        },
    };

    // Landing draws are consumed once per time window, including while already perched.
    const double PerchDecisionSeconds = 5d;
    const float PerchChance = 0.18f;

    static bool WantsToLand(in CreatureContext c)
    {
        if (!c.PerchDecisionDue || !c.Senses.HasLandingTarget ||
            (c.Senses.Species?.CruiseAltitudeMeters ?? 0f) <= 0f) return false;

        // One draw per decision window rather than per tick, so the same bird makes the same decision in the
        // same order whatever the frame rate - the same rule the wander heading is drawn under.
        uint bucket = (uint)(c.SimulationSeconds / PerchDecisionSeconds);
        return ScatterHash.To01(ScatterHash.Mix(c.Seed ^ (bucket * 0x7feb352du))) < PerchChance;
    }

    readonly AdaptiveStateMachine<CreatureContext> _machine;
    readonly UtilityDecision<(CreatureObjective, EntityId)> _decision = new(0.12f, 2d);
    readonly UtilityCandidate<(CreatureObjective, EntityId)>[] _candidates = new UtilityCandidate<(CreatureObjective, EntityId)>[6];
    CreatureContext _context;
    uint _lastPerchDecision = uint.MaxValue;

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
    public bool HitRequested => _context.HitRequested;
    public uint AttackSequence => _context.AttackSequence;
    public float AttackTime => (float)(_context.SimulationSeconds - _context.StateEnteredSeconds);
    public CreatureObjective Objective => _context.Objective;

    public void ReportHuntFailure(double retrySeconds = 5d)
    {
        if (double.IsNaN(retrySeconds) || double.IsInfinity(retrySeconds) || retrySeconds < 0d)
            throw new System.ArgumentOutOfRangeException(nameof(retrySeconds));
        _decision.Reject((CreatureObjective.Hunt, _context.Senses.PreyId), _context.SimulationSeconds + retrySeconds);
    }

    public void ClearHuntFailure(EntityId target) => _decision.ForgetFailure((CreatureObjective.Hunt, target));

    /// <summary>Perception. Called once per tick before <see cref="Sample"/>.</summary>
    public void Observe(in CreatureSenses senses) => _context.Senses = senses;

    public ActorIntent Sample(uint tick)
    {
        _context.TurnDegreesPerSecond = 0f;
        _context.Throttle = 0f;
        _context.SpeedScale = 1f;
        _context.HitRequested = false;

        _context.SimulationSeconds += Mathf.Max(0f, _context.Senses.DeltaTime);
        SelectObjective();
        uint decision = (uint)(_context.SimulationSeconds / PerchDecisionSeconds);
        _context.PerchDecisionDue = decision != _lastPerchDecision;
        _lastPerchDecision = decision;
        double previousCooldown = _context.HuntCooldownUntil;
        _machine.Tick(ref _context);
        if (_context.HuntCooldownUntil > _context.SimulationSeconds && _context.HuntCooldownUntil != previousCooldown)
            _decision.Reject((CreatureObjective.Hunt, _context.HuntTarget), _context.HuntCooldownUntil);

        return new ActorIntent(
            new Vector2(0f, _context.Throttle),
            new Vector2(_context.TurnDegreesPerSecond, 0f),
            ActorButtons.None,
            tick);
    }

    void SelectObjective()
    {
        var senses = _context.Senses;
        float hunger = (float)senses.Needs.Hunger, thirst = (float)senses.Needs.Thirst;
        float health = senses.MaxHealth > 0 ? Mathf.Clamp01((float)senses.Health / senses.MaxHealth) : 1f;
        _candidates[0] = new((CreatureObjective.Roam, default), 0.1f);
        bool feeding = _context.Objective == CreatureObjective.FindFood && senses.Food.Available;
        bool drinking = _context.Objective == CreatureObjective.FindWater && senses.Water.Available;
        _candidates[1] = new((CreatureObjective.FindFood, senses.Food.Id),
            senses.Food.Available ? 0.55f + hunger * 0.5f : hunger * 0.6f, hunger >= (feeding ? 0.05f : 0.5f));
        _candidates[2] = new((CreatureObjective.FindWater, senses.Water.Id),
            senses.Water.Available ? 0.55f + thirst * 0.55f : thirst * 0.65f, thirst >= (drinking ? 0.05f : 0.5f));
        _candidates[3] = new((CreatureObjective.Hunt, senses.PreyId), hunger * (0.35f + health * 0.65f),
            hunger >= 0.5f && senses.HasPrey && CreatureHunt.Distance(_context) <= 20f &&
            _context.SimulationSeconds >= _context.HuntCooldownUntil);
        _candidates[4] = new((CreatureObjective.Escape, default), 1f, senses.HasThreat, emergency: true);
        _candidates[5] = new((CreatureObjective.Rest, default), 0.55f,
            senses.CanRest && hunger < 0.65f && thirst < 0.6f);
        _decision.Select(_candidates, _context.SimulationSeconds);
        _context.Objective = _decision.Choice.Item1;
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
        uint bucket = (uint)(c.SimulationSeconds / HeadingChangeSeconds);
        uint h = ScatterHash.Mix(c.Seed ^ (bucket * 0x9e3779b1u));

        float wanderTurn = (ScatterHash.To01(h) * 2f - 1f) * TurnDegreesPerSecond;
        bool resting = c.Objective == CreatureObjective.Roam && (c.Senses.Species?.CruiseAltitudeMeters ?? 0f) <= 0f &&
            ScatterHash.To01(ScatterHash.Slot(h, 3)) < RestChance;

        // Beyond the home range the wander is overruled by the pull home; inside it the pull is zero and the
        // animal is free. Same arithmetic the unobserved fast-forward applies in one step.
        float range = c.Senses.Species?.HomeRangeMeters ?? 120f;
        float pull = Mathf.Clamp01((c.Senses.DistanceToHome - range) / range);
        float homeBearing = CharacterMath.TangentBearing(
            c.Senses.Position, c.Senses.Forward, c.Senses.Up, c.Senses.Home);

        c.Throttle = resting && pull <= 0f ? 0f : 1f;
        // A resting animal looks with its head. Body turns must carry a walking gait.
        c.TurnDegreesPerSecond = c.Throttle > 0f ? Mathf.Lerp(wanderTurn, homeBearing, pull) : 0f;
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

/// <summary>
/// Approach a selected ground site, rest after contact, and take off when the rest ends or support is lost.
/// </summary>
/// <remarks>
/// The state steers toward the site. The host controls altitude and reports actual support clearance.
/// </remarks>
sealed class PerchState : IState<CreatureContext>
{
    const double DwellSeconds = 12d;
    public const float ArrivalMeters = 0.2f;

    static float Distance(in CreatureContext c) =>
        Vector3.ProjectOnPlane(c.Senses.LandingTarget - c.Senses.Position, c.Senses.Up).magnitude;

    public int Id => (int)CreatureBehaviour.Perch;

    // The one piece of per-creature data any state keeps, and it lives on the CONTEXT rather than here: one
    // instance of this class is shared by every bird in the world.
    public void Enter(ref CreatureContext c) => c.StateEnteredSeconds = c.SimulationSeconds;

    public void Exit(ref CreatureContext c) { }

    public int EvaluateExit(in CreatureContext c) =>
        !c.Senses.HasLandingTarget ||
        (Distance(c) <= ArrivalMeters && c.Senses.AltitudeMeters <= 0.01f &&
            c.SimulationSeconds - c.StateEnteredSeconds >= DwellSeconds)
            ? (int)CreatureBehaviour.Wander : StateId.None;

    public void Update(ref CreatureContext c)
    {
        float distance = Distance(c);
        bool approaching = distance > ArrivalMeters;
        if (approaching || c.Senses.AltitudeMeters > 0.01f) c.StateEnteredSeconds = c.SimulationSeconds;
        float bearing = approaching ? CharacterMath.TangentBearing(
            c.Senses.Position, c.Senses.Forward, c.Senses.Up, c.Senses.LandingTarget) : 0f;
        c.TurnDegreesPerSecond = Mathf.Clamp(bearing * 3f, -120f, 120f);
        float step = Mathf.Max(0.001f, (c.Senses.Species?.WalkSpeedMps ?? 0f) * c.Senses.DeltaTime);
        c.Throttle = approaching
            ? Mathf.Min(1f, distance / step) * Mathf.Clamp01(1f - Mathf.Abs(bearing) / 90f) : 0f;
        c.SpeedScale = 1f;
    }
}
