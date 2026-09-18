using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>Local authority fixture using shared brains, needs, finite resources, and presentation.</summary>
public sealed partial class PredatorEncounterPrototype : MonoBehaviour
{
    public CreatureVisualSettings WolfVisuals, DeerVisuals;
    public CreatureCombatSettings WolfCombat, DeerCombat;
    public Texture2D BloodTexture;
    public AudioClip WolfWarning;
    public bool Ecosystem = true;
    [Header("Population (restart to apply)")]
    [Range(4, 16)] public int DeerCount = 8;
    [Range(1, 8)] public int PackWolfCount = 3;
    public bool IncludeSoloWolf = true, PackSupportEnabled = true;
    [Min(1f)] public float PackSupportRadius = 12f;
    [Header("Live needs for the first pair (zero is full)")]
    [Range(0f, 1f)] public float WolfHunger = .5f, WolfThirst = .15f, DeerHunger = .85f, DeerThirst = .65f;
    [Header("Accelerated simulation")]
    [Min(1f)] public float HungerSeconds = 80f, ThirstSeconds = 160f;
    [Min(.01f)] public float ConsumeUnitsPerSecond = .12f;
    [Min(1f)] public float DayLengthSeconds = 120f;
    [Min(0f)] public float StarvationGraceDays = 1f, ThirstGraceDays = .25f;
    [Min(.01f)] public float StarvationFatalDays = 3f, ThirstFatalDays = 1.5f;
    public bool AllowSleep = true, FoodAvailable = true, WaterAvailable = true;
    [Header("Endurance (restart to apply durations and initial reserves)")]
    [Min(.1f)] public float DeerSprintSeconds = 8f, WolfSprintSeconds = 22f;
    [Min(1f)] public float AwakeSeconds = 360f, SleepRecoverySeconds = 60f;
    [Range(0f, 1f)] public float InitialFatigue, InitialStamina = 1f;
    [Header("Disposition (restart to apply courage)")]
    [Range(0f, 1f)] public float DeerCourage = .35f, WolfCourage = .65f;
    [Min(.1f)] public float DeerStrength = 1f, WolfStrength = 1.5f;
    [Tooltip("Diagnostic: the first deer has no escape route. Does not change terrain or navigation.")]
    public bool FirstDeerCornered;
    [Header("Carcass game days (applied at death)")]
    [Min(0f)] public float FliesAfterDays = .25f, BloatedAfterDays = 2f, RottingAfterDays = 5f;
    [Min(.01f)] public float BonesAfterDays = 21f, GoneAfterDays = 90f;
    [Header("Movement")]
    [Range(.1f, 1f)] public float DeerMoveSpeed = 1f;
    [Min(.1f)] public float DeerFullRunSpeed = 6f, WolfRunSpeed = 4f;
    [Range(.1f, 1f)] public float WoundedSpeed = .7f;
    public Vector3 PlantPosition = new(0f, 0f, 3f), WaterPosition = new(4f, 0f, 3f);
    public bool TargetAvailable = true, InterruptWolf, RestartRequested;
    public bool FollowCamera = true;
    [Tooltip("Disable animation evaluation for accelerated simulation checks. Decisions, movement, attacks, needs and decay still run.")]
    public bool AnimateActors = true;
    public bool NavigationObstacles = true;
    CreatureNavigationCourse _navigationCourse;
    [TextArea] public string Status;
    public double DeerHealth => _actors.Count > 0 ? _actors[0].Health : 0;
    public double MeatRemaining => Sum(ResourceKind.Meat);
    public double PlantRemaining => Sum(ResourceKind.Plants);
    public double WaterRemaining => Sum(ResourceKind.FreshWater);
    public int LivingCount { get { int n = 0; foreach (var a in _actors) if (a.Health > 0) n++; return n; } }
    public int CorpseCount { get { int n = 0; foreach (var a in _actors) if (a.Carrion != null) n++; return n; } }
    public CreatureBehaviour WolfBehaviour => _wolf?.Brain.Behaviour ?? CreatureBehaviour.Wander;
    public CreatureBehaviour DeerBehaviour => _actors.Count > 0 ? _actors[0].Brain.Behaviour : CreatureBehaviour.Wander;

    sealed class Actor
    {
        public readonly CreatureNavigation Navigation = new();
        public CreatureObjective NavigationObjective;
        public EntityId NavigationTarget;
        public bool OnRefuge;
        public EntityId BlockedTarget;
        public float BlockedUntil;
        public uint Id;
        public bool Predator;
        public uint Group;
        public AudioSource WarningVoice;
        public float NextWarning;
        public double Support;
        public Actor HuntLeader, OfferedPrey;
        public bool GroupHuntCommitted;
        public ulong JoinedTarget;
        public double JoinedUntil;
        public ActorPursuitRole PursuitRole;
        public bool HasApproach;
        public Vector3 Approach;
        public int ApproachSlot = -1;
        public ulong ApproachTarget;
        public float NextApproach, ApproachRetry;
        public ActorHealth Vitals;
        public ActorLifeCycle Life;
        public ActorLifeProfile LifeProfile;
        public ActorDeathCause DeathCause;
        public CreatureCombatDto Combat;
        public double Health => Vitals.Current;
        public float Height, AlertAt = -1f;
        public uint LastAttackSequence;
        public float AttackEnds;
        public double AttackPower;
        public bool AttackResolved;
        public int FailedAttacks;
        public Actor AttackTarget;
        public Vector3 Home, Velocity, LastBlood;
        public ActorHomeSite HomeSite;
        public ActorKnowledge Knowledge = new();
        public ActorPerception Perception;
        public readonly HashSet<ulong> DirectSight = new();
        public Actor Interest;
        public float NextScent, NextSound;
        public float HomeBlockedUntil;
        public ActorNeeds Needs;
        public ActorEndurance Endurance;
        public ActorDisposition Disposition;
        public ActorEnduranceProfile EnduranceProfile;
        public ActorDeprivation Deprivation = new();
        public CreatureBrain Brain;
        public CreatureAnimationView View;
        public CreatureVisualDto Visual;
        public CreatureSenses Senses;
        public ActorIntent Intent;
        public Actor Prey;
        public Actor Threat, LastAttacker;
        public Source Food, Water;
        public CreatureCorpseResource Carrion;
        public CreatureCorpsePresentation CorpseView;
        public ResourceKind Diet => (Predator ? ResourceKind.Meat : ResourceKind.Plants) | ResourceKind.FreshWater;
    }
    sealed class Source
    {
        public uint Id;
        public Vector3 Position;
        public ActorResourceSource Stock;
        public Actor Owner;
    }
    readonly List<Actor> _actors = new();
    readonly List<ActorGroup.Candidate> _groupCandidates = new();
    readonly Dictionary<uint, ActorGroup.Hunt> _groupHunts = new();
    readonly List<Source> _sources = new();
    readonly List<Material> _materials = new();
    readonly List<(ulong id, Vector3 position, Vector3 up)> _flies = new();
    readonly CreatureAnimationPrototype.PrototypeGrounding _ground = new();
    Actor _wolf, _soloWolf;
    CreatureVisualDto _deerVisualSnapshot, _wolfVisualSnapshot;
    CreatureCombatDto _deerCombatSnapshot, _wolfCombatSnapshot;
    ActorPerceptionProfile _deerPerceptionSnapshot, _wolfPerceptionSnapshot;
    ActorLifeProfile _deerLifeSnapshot, _wolfLifeSnapshot;
    GameObject _sourceViews;
    AmbientSwarms _swarms;
    CreatureBloodPresentation _blood;
    uint _tick;
    int _hits, _misses;
    float _elapsed;
    double _gameSeconds;
    GUIStyle _statusStyle;
    Vector2 _statusScroll, _controlsScroll;
    int _focusActor = -1;
    FreeCameraController _freeCamera;
    InputMapService _cameraInput;
    bool _ownsCamera;
    public void FocusActor(int index)
    {
        _focusActor = Mathf.Clamp(index, -1, _actors.Count - 1); FollowCamera = true;
        _statusScroll = Vector2.zero;
        if (_wolf != null) UpdateStatus();
    }
    void LateUpdate()
    {
        var camera = Camera.main;
        if (camera == null || _actors.Count == 0) return;
        if (_freeCamera == null)
        {
            _freeCamera = camera.GetComponent<FreeCameraController>();
            _ownsCamera = _freeCamera == null;
            if (_ownsCamera) _freeCamera = camera.gameObject.AddComponent<FreeCameraController>();
            if (!ServiceLocator.TryGet<IInputMapService>(out _))
            { _cameraInput = new InputMapService(); _freeCamera.InputOverride = _cameraInput; }
            _freeCamera.AutoPositionOnGenerate = false;
            _freeCamera.MoveSpeed = 10f;
        }
        _freeCamera.enabled = !FollowCamera;
        if (!FollowCamera) return;
        Vector3 center = Vector3.zero;
        float spread = 8f;
        if (_focusActor >= 0 && _focusActor < _actors.Count)
        { center = _actors[_focusActor].View.Root.position; spread = 3f; }
        else
        {
            int count = 0;
            foreach (var a in _actors) if (Available(a)) { center += a.View.Root.position; count++; }
            if (count > 0) center /= count;
            foreach (var a in _actors) if (Available(a)) spread = Mathf.Max(spread, FlatDistance(a.View.Root.position, center));
        }
        camera.fieldOfView = 45f;
        camera.transform.position = center + new Vector3(1f, 1f, -.8f).normalized * spread * 3f;
        camera.transform.LookAt(center);
    }

    void Start() { if (Scenario == PerceptionScenario.Ecosystem) Restart(); else TestPerception(Scenario); }
    [ContextMenu("Restart Encounter")]
    public void Restart()
    {
        if (!Application.isPlaying || WolfVisuals == null || DeerVisuals == null) return;
        if (WolfCombat == null || DeerCombat == null) throw new System.InvalidOperationException("The encounter requires deer and wolf combat settings.");
        _deerVisualSnapshot = DeerVisuals.Snapshot(); _wolfVisualSnapshot = WolfVisuals.Snapshot();
        _deerCombatSnapshot = DeerCombat.Snapshot(); _wolfCombatSnapshot = WolfCombat.Snapshot();
        _deerPerceptionSnapshot = DeerPerception != null ? DeerPerception.Snapshot() : new ActorPerceptionProfile();
        _wolfPerceptionSnapshot = WolfPerception != null ? WolfPerception.Snapshot() : new ActorPerceptionProfile();
        _deerLifeSnapshot = DeerLife != null ? DeerLife.Snapshot() : new ActorLifeProfile();
        _wolfLifeSnapshot = WolfLife != null ? WolfLife.Snapshot() : new ActorLifeProfile();
        DisposeViews();
        _tick = 0; _elapsed = 0; _gameSeconds = 0; _hits = _misses = 0;
        Births = 0; _deathCounts.Clear(); _nextBirthId = 10000; _nextMateScan = 0;
        FoodConsumed = WaterConsumed = 0;
        _stimuli.Clear();
        if (NavigationObstacles) _navigationCourse = new CreatureNavigationCourse(transform);
        _sourceViews = new GameObject("Survival sources"); _sourceViews.transform.SetParent(transform, false);
        AddActor(1, false, Ecosystem ? Vector3.zero : new(0, 0, -1), DeerHunger, DeerThirst);
        if (Ecosystem)
        {
            AddActor(2, false, new(4, 0, 2), .3, .8);
            AddActor(3, false, new(-1, 0, 6), .65, .5);
            AddActor(4, false, new(5, 0, -4), .2, .2);
            for (int i = 4; i < Mathf.Clamp(DeerCount, 4, 16); i++)
                AddActor((uint)(i + 2), false, new(8 + (i - 4) % 4 * 3, 0, -8 - (i - 4) / 4 * 3), .35 + .1 * (i % 3), .25);
        }
        _wolf = AddActor(5, true, Ecosystem ? new(-8, 0, -4) : new(-2, 0, -6), WolfHunger, WolfThirst);
        if (Ecosystem)
        {
            _wolf.Group = 1; _wolf.View.Root.name = "Pack wolf 5";
            for (int i = 1; i < Mathf.Clamp(PackWolfCount, 1, 8); i++)
            {
                var member = AddActor((uint)(19 + i), true, new(-8 - i % 3 * 2, 0, -4 - i / 3 * 2), .5 + i * .02, .15);
                member.Group = 1; member.View.Root.name = "Pack wolf " + member.Id;
            }
            if (IncludeSoloWolf)
            {
                _soloWolf = AddActor(40, true, new(14, 0, -12), .55, .15);
                _soloWolf.View.Root.name = "Solo wolf 40";
            }
        }
        AddSource(101, ResourceKind.Plants, Ecosystem ? new(-3, 0, 2) : PlantPosition, 2);
        AddSource(104, ResourceKind.FreshWater, Ecosystem ? new(-6, 0, 6) : WaterPosition, 10);
        if (Ecosystem)
        {
            AddSource(102, ResourceKind.Plants, new(6, 0, 5), 2);
            AddSource(103, ResourceKind.Plants, new(3, 0, -7), 2);
            AddSource(105, ResourceKind.FreshWater, new(8, 0, -1), 10);
            AddSource(106, ResourceKind.SaltWater, new(-8, 0, -8), 10);
            AddSource(107, ResourceKind.Plants, new(12, 0, -8), 4);
            AddSource(108, ResourceKind.Plants, new(16, 0, -3), 4);
            AddSource(109, ResourceKind.FreshWater, new(18, 0, -10), 15);
        }
        CreateHabitatSources();
        CreateHomeSites();
        _swarms = new AmbientSwarms(transform);
        _blood = new CreatureBloodPresentation(transform, BloodTexture, _ground);
        TargetAvailable = true; InterruptWolf = false; RestartRequested = false;
        UpdateStatus();
    }
    Actor AddActor(uint id, bool predator, Vector3 position, double hunger, double thirst)
    {
        var a = new Actor { Id = id, Predator = predator, Height = predator ? 1f : 1.84f,
            Visual = predator ? _wolfVisualSnapshot : _deerVisualSnapshot, Home = position, Needs = new(hunger, thirst),
            Brain = new CreatureBrain((int)id + 72026, null, CreatureBehaviour.Wander) };
        a.Combat = predator ? _wolfCombatSnapshot : _deerCombatSnapshot;
        a.Perception = new ActorPerception(predator ? _wolfPerceptionSnapshot : _deerPerceptionSnapshot);
        a.Vitals = new ActorHealth(a.Combat.MaximumHealth);
        InitializeLife(a);
        a.View = new CreatureAnimationView(transform, id, a.Visual, a.Height);
        a.Endurance = new ActorEndurance(Mathf.Clamp01(InitialStamina), Mathf.Clamp01(InitialFatigue));
        a.Disposition = new ActorDisposition(Mathf.Clamp01(predator ? WolfCourage : DeerCourage));
        a.EnduranceProfile = new ActorEnduranceProfile(Mathf.Max(.1f, predator ? WolfSprintSeconds : DeerSprintSeconds),
            Mathf.Max(1f, AwakeSeconds), Mathf.Max(1f, SleepRecoverySeconds));
        if (predator && WolfWarning != null)
        {
            a.WarningVoice = a.View.Root.gameObject.AddComponent<AudioSource>();
            a.WarningVoice.playOnAwake = false; a.WarningVoice.spatialBlend = 1f;
            a.WarningVoice.minDistance = 4f; a.WarningVoice.maxDistance = 35f; a.WarningVoice.volume = .5f;
        }
        a.View.Root.name = (predator ? "Wolf " : "Deer ") + id;
        Place(a.View.Root, position, a.Height * .5f);
        CreatureAnimationPrototype.ApplyPreviewMaterials(a.View.Root, _materials);
        _actors.Add(a); return a;
    }
    void AddSource(uint id, ResourceKind kind, Vector3 position, double quantity)
    {
        bool plant = kind == ResourceKind.Plants;
        var source = new Source { Id = id, Position = position,
            Stock = new ActorResourceSource(kind, quantity, plant ? 1d : 0d, plant ? 0d : 1d,
                Mathf.Max(0, plant ? PlantUnitsPerDay : WaterUnitsPerDay) / 86400d) };
        _sources.Add(source);
        Marker(kind.ToString(), position, plant ? new(.22f, .48f, .08f) :
            kind == ResourceKind.SaltWater ? new(.2f, .65f, .65f) : new(.08f, .4f, .7f),
            plant ? new(1.4f, .2f, 1.4f) : new(2.4f, .06f, 2.4f));
    }
    [ContextMenu("Reset Survival Scenario")]
    public void ResetScenario()
    {
        WolfHunger = .5f; WolfThirst = .15f; DeerHunger = .85f; DeerThirst = Ecosystem ? .3f : .65f;
        Restart();
    }
    void Update() { if (RestartRequested) ResetScenario(); Step(Time.deltaTime); }
    public void Step(float dt)
    {
        if (_wolf == null || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
        while (dt > 0f) { float step = Mathf.Min(dt, .05f); Simulate(step); dt -= step; }
        UpdateStatus();
    }
    void Simulate(float dt)
    {
        _elapsed += dt; _tick++;
        double gameStep = dt * 86400d / Mathf.Max(1f, DayLengthSeconds);
        _gameSeconds += gameStep;
        AdvanceHabitat(dt, gameStep);
        _actors[0].Needs = new(Mathf.Clamp01(DeerHunger), Mathf.Clamp01(DeerThirst));
        _wolf.Needs = new(Mathf.Clamp01(WolfHunger), Mathf.Clamp01(WolfThirst));
        // Observe all positions before any movement or resource mutation.
        foreach (var a in _actors)
        {
            if (!Available(a)) { a.Velocity = Vector3.zero; continue; }
            a.Needs = a.Needs.Advance(dt, Mathf.Max(1, HungerSeconds), Mathf.Max(1, ThirstSeconds));
        }
        ObservePerception();
        ObserveKnowledge();
        UpdateHomeSites();
        SelectHuntTargets();
        foreach (var a in _actors) if (Available(a)) Observe(a, dt);
        PlanHunts();
        foreach (var a in _actors)
            if (Available(a)) { a.Brain.Observe(a.Senses); a.Intent = a.Brain.Sample(_tick); }
        foreach (var a in _actors)
        {
            if (!Available(a)) continue;
            if (a.Brain.AttackSequence != a.LastAttackSequence)
            {
                a.LastAttackSequence = a.Brain.AttackSequence;
                a.AttackTarget = a.Brain.Objective == CreatureObjective.Defend ? a.Threat : a.Prey;
                a.AttackPower = a.Endurance.SpendUpTo(a.Combat.Attack.StaminaCost);
                if (LifeCycleEnabled) a.AttackPower *= System.Math.Pow(a.Life.PhysicalScale(a.LifeProfile), 2);
                a.AttackEnds = _elapsed + a.Combat.Attack.Duration;
                a.AttackResolved = false;
            }
            float speed = a.Brain.SpeedScale;
            if (a.Brain.Behaviour == CreatureBehaviour.Flee && !a.Predator)
                speed = DeerFullRunSpeed * DeerMoveSpeed;
            else if (a.Brain.Behaviour == CreatureBehaviour.Chase) speed *= WolfRunSpeed / 4f;
            bool running = a.Brain.Behaviour is CreatureBehaviour.Flee or CreatureBehaviour.Chase or CreatureBehaviour.Attack;
            speed = MovementSpeed(a, speed, running);
            float turn = a.Intent.Look.x, throttle = a.Intent.Move.y;
            if (_navigationCourse != null) Navigate(a, dt, ref turn, ref throttle);
            if (a.Brain.Objective == CreatureObjective.Hunt && a.Prey != null &&
                (!a.HasApproach || a.Brain.Behaviour == CreatureBehaviour.Attack) &&
                FlatDistance(Feet(a), Feet(a.Prey)) <= a.Combat.Attack.Reach * 2f && StrikeLaneBlocked(a, a.Prey)) throttle = 0f;
            if (a.OnRefuge) throttle = 0f;
            a.View.Root.rotation = Quaternion.AngleAxis(turn * dt, Vector3.up) * a.View.Root.rotation;
            Vector3 previous = a.View.Root.position;
            Vector3 destination = previous + a.View.Root.forward * (throttle * speed * dt);
            if (_navigationCourse != null && !a.OnRefuge)
                destination = CreatureNavigation.Constrain(Feet(a), destination - Vector3.up * a.Height * .5f);
            if (!a.OnRefuge) Place(a.View.Root, destination, a.Height * .5f);
            a.Velocity = (a.View.Root.position - previous) / dt;
        }
        ResolveBodySpacing();
        foreach (var a in _actors)
        {
            if (!Available(a)) continue;
            if (a.Brain.HitRequested && !a.AttackResolved)
            {
                var target = a.AttackTarget;
                if (target != null && Available(target) && !StrikeLaneBlocked(a, target) &&
                    (_navigationCourse == null || CreatureNavigation.ClearContact(Feet(a), Feet(target))) && a.Combat.Attack.InContact(
                    Vector3.Distance(a.View.Root.position, target.View.Root.position),
                    CharacterMath.TangentBearing(a.View.Root.position, a.View.Root.forward, Vector3.up, target.View.Root.position)))
                {
                    Damage(target, a.Combat.Attack.Damage * a.AttackPower, true, a,
                        a.Combat.Attack.Wounds * a.AttackPower, a.Combat.Attack.Bleeding * a.AttackPower);
                    a.Brain.ConfirmHit(); a.AttackResolved = true; a.FailedAttacks = 0; _hits++;
                }
            }
            if (!a.AttackResolved && a.LastAttackSequence > 0 && _elapsed >= a.AttackEnds)
            {
                a.AttackResolved = true; _misses++;
                if (++a.FailedAttacks >= 3) { a.Brain.ReportHuntFailure(8d); a.FailedAttacks = 0; }
            }
            Source source = a.Brain.Behaviour == CreatureBehaviour.Feed ? a.Food :
                a.Brain.Behaviour == CreatureBehaviour.Drink ? a.Water : null;
            if (source != null && SourceAvailable(source, a.Diet) &&
                FlatDistance(a.View.Root.position, source.Position) <= CreatureConsumeState.Reach + .02f)
            {
                double consumed = source.Stock.Consume(ref a.Needs, a.Diet, Mathf.Max(0, ConsumeUnitsPerSecond) * dt);
                FoodConsumed += consumed * source.Stock.HungerPerUnit;
                WaterConsumed += consumed * source.Stock.ThirstPerUnit;
            }
            int damage = a.Deprivation.Advance(a.Needs, gameStep, a.Vitals.Maximum, StarvationGraceDays * 86400d,
                Mathf.Max(.01f, StarvationFatalDays) * 86400d, ThirstGraceDays * 86400d, Mathf.Max(.01f, ThirstFatalDays) * 86400d);
            if (damage > 0)
            {
                if (damage >= a.Health) a.DeathCause = a.Needs.Thirst >= 1 ? ActorDeathCause.Thirst : ActorDeathCause.Starvation;
                Damage(a, damage, false);
            }
            if (a.Health > 0)
            {
                bool running = a.Brain.Behaviour is CreatureBehaviour.Flee or CreatureBehaviour.Chase or CreatureBehaviour.Attack;
                var exertion = ActorEndurance.ClassifyExertion(a.Velocity.sqrMagnitude, running,
                    a.Brain.Behaviour == CreatureBehaviour.Sleep);
                a.Endurance.Advance(dt, a.Needs, exertion, a.EnduranceProfile, a.Vitals.Wounds);
                a.Vitals.Advance(gameStep, a.Needs, exertion, a.Senses.HasThreat || a.Brain.Objective == CreatureObjective.Hunt, a.Combat.Recovery);
                if (!a.Vitals.Alive) { a.DeathCause = ActorDeathCause.Bleeding; CreateCorpse(a); }
            }
            if (a.Health > 0 && a.Vitals.Bleeding > 0d && FlatDistance(a.View.Root.position, a.LastBlood) >= .7f)
            { _blood.Add(a.View.Root.position, .13f, _gameSeconds); a.LastBlood = a.View.Root.position; }
        }
        _flies.Clear();
        foreach (var a in _actors)
        {
            bool visible = a.Id != 1 || TargetAvailable;
            a.View.Root.gameObject.SetActive(visible && (a.Carrion == null || a.Carrion.Stage != CorpseStage.Gone));
            var b = a.Brain.Behaviour;
            a.View.Dead = a.Health <= 0;
            a.View.Resting = b == CreatureBehaviour.Rest; a.View.Sleeping = b == CreatureBehaviour.Sleep;
            a.View.Eating = b == CreatureBehaviour.Feed && a.Velocity.sqrMagnitude < .0004f;
            a.View.Drinking = b == CreatureBehaviour.Drink && a.Velocity.sqrMagnitude < .0004f;
            a.View.Stalking = b == CreatureBehaviour.Stalk;
            a.View.Threatening = b == CreatureBehaviour.Threaten;
            if (b == CreatureBehaviour.Threaten && a.Health > 0 && _elapsed >= a.NextWarning)
            {
                a.NextWarning = _elapsed + 4f;
                if (Time.timeScale > 0 && a.WarningVoice != null) a.WarningVoice.PlayOneShot(WolfWarning);
            }
            a.View.AttackTime = b == CreatureBehaviour.Attack && a.Health > 0 ? AttackClipTime(a) : null;
            a.View.LookTarget = a.Health <= 0 ? null : a.Senses.HasThreat ? a.Senses.ThreatPosition :
                a.Brain.Objective == CreatureObjective.Hunt ? a.Senses.PreyPosition : a.Senses.HasInterest ? a.Senses.InterestPosition : null;
            if (AnimateActors) a.View.Tick(a.Velocity, Vector3.up, dt, a.OnRefuge ? RefugeGround : _ground);
            if (a.Carrion != null)
            {
                a.Carrion.Advance(gameStep); a.CorpseView.Tick(a.Carrion, a.View.DeathPoseSettled);
                if (visible && a.Carrion.HasFlies) _flies.Add((a.Id, a.View.Root.position, Vector3.up));
            }
        }
        _swarms.SyncCorpseFlies(_flies);
        _blood.Tick(_gameSeconds);
        WolfHunger = (float)_wolf.Needs.Hunger; WolfThirst = (float)_wolf.Needs.Thirst;
        DeerHunger = (float)_actors[0].Needs.Hunger; DeerThirst = (float)_actors[0].Needs.Thirst;
    }
    static readonly IGroundingProvider RefugeGround = new RefugeGrounding();
    sealed class RefugeGrounding : IGroundingProvider
    {
        public bool TryGround(Vector3 p, Vector3 down, float offset, out GroundResult result)
        { result = new GroundResult(new Vector3(p.x, CreatureNavigationCourse.Refuge.y + offset, p.z), Vector3.up); return true; }
    }
    static Vector3 Feet(Actor a) => a.View.Root.position - Vector3.up * a.Height * .5f;
    static float BodyRadius(Actor a) => a.Predator ? .55f : .65f;
    float MovementSpeed(Actor a, float speed, bool running) => speed * (running ? (float)a.Endurance.RunSpeedScale : 1f) *
        Mathf.Lerp(1f, WoundedSpeed, (float)a.Vitals.Wounds) * (LifeCycleEnabled ? (float)a.Life.PhysicalScale(a.LifeProfile) : 1f);
    public void PutDeerOnRefuge()
    {
        if (_navigationCourse == null || _actors.Count == 0 || !Available(_actors[0])) return;
        var a = _actors[0]; a.OnRefuge = true;
        a.View.Root.position = CreatureNavigationCourse.Refuge + Vector3.up * a.Height * .5f;
        a.Navigation.Reset(); FocusActor(0);
    }
    public void TestRefugeAttack()
    {
        if (_navigationCourse == null || _wolf == null || !Available(_wolf) || !Available(_actors[0])) return;
        PutDeerOnRefuge();
        Place(_wolf.View.Root, CreatureNavigationCourse.Refuge + Vector3.right * 4f, _wolf.Height * .5f);
        _wolf.Navigation.Reset();
        Damage(_wolf, 5d, true, _actors[0]);
        FocusActor(_actors.IndexOf(_wolf));
    }
    void Navigate(Actor a, float dt, ref float turn, ref float throttle)
    {
        if (a.NavigationObjective != a.Brain.Objective || a.NavigationTarget != a.Brain.ObjectiveTarget)
        {
            a.Navigation.Reset(); a.NavigationObjective = a.Brain.Objective;
            a.NavigationTarget = a.Brain.ObjectiveTarget;
        }
        // Combat owns facing and its committed wind-up. Movement still passes through boundary clipping.
        if (throttle <= 0 || a.OnRefuge || a.Brain.Objective == CreatureObjective.Defend ||
            a.Brain.Behaviour == CreatureBehaviour.Attack) return;
        Vector3 position = Feet(a), goal;
        switch (a.Brain.Objective)
        {
            case CreatureObjective.Hunt:
                if (a.Prey == null) return;
                goal = a.HasApproach ? a.Approach : a.Senses.PreyPosition - Vector3.up * a.Prey.Height * .5f;
                break;
            case CreatureObjective.FindFood:
                if (a.Food == null) return;
                goal = a.Senses.Food.Position - Vector3.up * (a.Food.Owner?.Height * .5f ?? 0f);
                break;
            case CreatureObjective.FindWater: if (a.Water == null) return; goal = a.Senses.Water.Position; break;
            case CreatureObjective.Investigate: goal = a.Senses.InterestPosition; break;
            case CreatureObjective.ReturnHome: goal = a.Senses.RestPosition - Vector3.up * a.Height * .5f; break;
            case CreatureObjective.Escape:
                if (a.Threat == null) return;
                if (a.Navigation.Escape(position, a.Senses.ThreatPosition, a.View.Root.forward, dt, out var escape))
                {
                    float bearing = CharacterMath.TangentBearing(position, a.View.Root.forward, Vector3.up, position + escape);
                    turn = Mathf.Clamp(bearing * 5f, -220f, 220f);
                    throttle *= Mathf.Clamp01(1f - Mathf.Abs(bearing) / 100f);
                }
                else throttle = 0;
                return;
            default:
                goal = position + Quaternion.AngleAxis(turn * dt, Vector3.up) * a.View.Root.forward * 2f; break;
        }
        if (a.Brain.Objective != CreatureObjective.Hunt && a.Brain.Objective != CreatureObjective.ReturnHome &&
            !(a.Brain.Objective == CreatureObjective.FindFood && a.Food?.Owner != null))
            goal.y = CreatureAnimationPrototype.GroundHeight(goal.x, goal.z);
        if (a.Navigation.Steer(position, goal, dt, out var direction))
        {
            float bearing = CharacterMath.TangentBearing(position, a.View.Root.forward, Vector3.up, position + direction);
            turn = Mathf.Clamp(bearing * 4f, -180, 180);
            throttle *= Mathf.Clamp01(1f - Mathf.Abs(bearing) / 100f);
        }
        else
        {
            throttle = 0;
            if (a.Navigation.Failed && a.Brain.Objective == CreatureObjective.ReturnHome)
            { a.HomeBlockedUntil = _elapsed + 15f; a.Brain.ReportNavigationFailure(); }
            else if (a.Navigation.Failed && a.Brain.Objective is CreatureObjective.Hunt or CreatureObjective.FindFood or CreatureObjective.FindWater)
            {
                if (a.Brain.Objective == CreatureObjective.Hunt && a.HasApproach)
                {
                    a.HasApproach = false; a.ApproachRetry = _elapsed + 4f; a.Navigation.Reset();
                    return;
                }
                a.BlockedTarget = a.Brain.ObjectiveTarget; a.BlockedUntil = _elapsed + 15f;
                a.Brain.ReportNavigationFailure();
            }
        }
    }
    void ResolveBodySpacing()
    {
        // Horizontal body discs; vertical offsets belong to grounding, not contact spacing.
        for (int pass = 0; pass < 4; pass++)
        for (int i = 0; i < _actors.Count; i++)
        for (int j = i + 1; j < _actors.Count; j++)
        {
            var a = _actors[i]; var b = _actors[j];
            if (!Available(a) || !Available(b) || a.OnRefuge || b.OnRefuge) continue;
            Vector3 delta = Vector3.ProjectOnPlane(b.View.Root.position - a.View.Root.position, Vector3.up);
            float distance = delta.magnitude;
            float clearance = BodyRadius(a) + BodyRadius(b);
            if (distance >= clearance) continue;
            Vector3 correction = (distance > .0001f ? delta / distance : Vector3.right) * ((clearance - distance) * .5f);
            Place(a.View.Root, _navigationCourse == null ? a.View.Root.position - correction :
                CreatureNavigation.Constrain(Feet(a), Feet(a) - correction), a.Height * .5f);
            Place(b.View.Root, _navigationCourse == null ? b.View.Root.position + correction :
                CreatureNavigation.Constrain(Feet(b), Feet(b) + correction), b.Height * .5f);
        }
    }
    bool Available(Actor a) => a.Health > 0 && (a.Id != 1 || TargetAvailable);
    static float AttackClipTime(Actor a)
    {
        float duration = a.Visual.Attack != null ? a.Visual.Attack.length : 1f;
        float contact = duration * a.Visual.AttackHitNormalized;
        float time = a.Brain.AttackTime;
        return time < a.Combat.Attack.Windup ? contact * time / a.Combat.Attack.Windup :
            Mathf.Lerp(contact, duration, (time - a.Combat.Attack.Windup) / a.Combat.Attack.ActiveSeconds);
    }
    bool WillSupport(Actor a) => Available(a) && !a.Endurance.Recovering &&
        a.Brain.Behaviour != CreatureBehaviour.Sleep && a.Brain.Objective != CreatureObjective.Escape && a.Vitals.Fraction > .35d;
    double NearbySupport(Actor actor)
    {
        if (!PackSupportEnabled || actor.Group == 0) return 0d;
        double support = 0;
        foreach (var other in _actors)
            if (other != actor && Available(other)) support += ActorGroup.Support(actor.Group, other.Group, Strength(other),
                FlatDistance(actor.View.Root.position, other.View.Root.position), Mathf.Max(1, PackSupportRadius), WillSupport(other));
        return support;
    }
    bool CanJoinHunt(Actor a) => WillSupport(a) && !a.Endurance.NeedsSleep && a.Needs.Thirst < .8 && a.Needs.Hunger >= .35 &&
        !(a.HomeSite?.Gathering ?? false);
    readonly List<ActorPursuit.Member> _pursuitMembers = new();
    ActorPursuit.Member PursuitMember(Actor a) => new(a.Id, Feet(a), Mathf.Max(.1f, MovementSpeed(a, WolfRunSpeed, true)),
        a.Combat.Attack.Reach, BodyRadius(a));
    bool StrikeLaneBlocked(Actor a, Actor target)
    {
        foreach (var other in _actors)
            if (other != a && other != target && Available(other) && ActorGroup.Allied(a.Group, other.Group) &&
                ActorPursuit.BlocksStrike(Feet(a), Feet(target), Feet(other), Vector3.up, BodyRadius(other) + .1f)) return true;
        return false;
    }
    void PlanHunts()
    {
        foreach (var a in _actors)
        {
            a.Senses.AttackLaneBlocked = a.Prey != null && StrikeLaneBlocked(a, a.Prey);
            if (!a.GroupHuntCommitted || a.Brain.Objective != CreatureObjective.Hunt || !CanJoinHunt(a) || a.Prey == null || !Visible(a, a.Prey))
            { a.HasApproach = false; a.ApproachSlot = -1; a.PursuitRole = ActorPursuitRole.Pursue; }
            if (_elapsed < a.ApproachRetry) a.PursuitRole = ActorPursuitRole.Pursue;
        }
        foreach (var entry in _groupHunts)
        {
            var hunt = entry.Value; var target = FindActor(hunt.Target);
            if (!hunt.Active(_elapsed) || target == null || !Available(target)) { hunt.Pursuit.Clear(); continue; }
            _pursuitMembers.Clear();
            foreach (var a in _actors)
                if (a.Group == entry.Key && a.GroupHuntCommitted && a.Prey == target &&
                    CanJoinHunt(a) && Visible(a, target) && a.Brain.Objective == CreatureObjective.Hunt && _elapsed >= a.ApproachRetry)
                    _pursuitMembers.Add(PursuitMember(a));
            hunt.Pursuit.Update(target.Id, Feet(target), target.Velocity, Vector3.up, _pursuitMembers);
            foreach (var member in _pursuitMembers)
            {
                var a = FindActor(member.Id);
                if (!hunt.Pursuit.TryGet(a.Id, out int slot)) continue;
                a.PursuitRole = ActorPursuit.Role(slot);
                bool changed = a.ApproachTarget != target.Id || a.ApproachSlot != slot;
                if (changed) { a.NextApproach = 0; a.ApproachRetry = 0; a.Navigation.Reset(); }
                a.ApproachTarget = target.Id; a.ApproachSlot = slot;
                if (slot == 0) { a.HasApproach = false; continue; }
                if (_elapsed >= a.NextApproach)
                {
                    a.NextApproach = _elapsed + .5f;
                    Vector3 goal = hunt.Pursuit.Goal(member, slot, Feet(target), target.Velocity, Vector3.up);
                    goal.y = CreatureAnimationPrototype.GroundHeight(goal.x, goal.z);
                    if (_navigationCourse == null || a.Navigation.TryApproach(Feet(a), goal, Feet(target), out goal))
                    { a.Approach = goal; a.HasApproach = true; }
                    else
                    { a.HasApproach = false; a.PursuitRole = ActorPursuitRole.Pursue; a.ApproachRetry = _elapsed + 4f; a.Navigation.Reset(); }
                }
                a.Senses.HasPursuitGoal = a.HasApproach;
                a.Senses.PursuitGoal = a.Approach + Vector3.up * a.Height * .5f;
            }
        }
    }
    Actor FindActor(ulong id) { foreach (var a in _actors) if (a.Id == id) return a; return null; }
    void SelectHuntTargets()
    {
        foreach (var a in _actors)
        {
            if (!a.Predator || !Available(a)) continue;
            a.HuntLeader = null; a.GroupHuntCommitted = false;
            if (a.Prey != null && Available(a.Prey) && !IsBlocked(a, a.Prey.Id) && !a.Prey.OnRefuge &&
                Observation(a, a.Prey, out var retained) && retained.IdentityConfidence >= .25f &&
                FlatDistance(a.View.Root.position, retained.Position) <= 20f) continue;
            a.Prey = null; float best = float.MaxValue;
            foreach (var prey in _actors)
            {
                if (prey.Predator || !Available(prey) || IsBlocked(a, prey.Id) || prey.OnRefuge) continue;
                if (!Observation(a, prey, out var observation) || observation.IdentityConfidence < .25f) continue;
                float distance = FlatDistance(a.View.Root.position, observation.Position);
                float cost = distance + observation.Uncertainty;
                if (distance <= 20 && cost < best) { best = cost; a.Prey = prey; }
            }
        }
        foreach (var a in _actors) a.OfferedPrey = a.Prey;
        foreach (var a in _actors)
        {
            if (!a.Predator || !CanJoinHunt(a) || a.Group == 0) continue;
            if (!_groupHunts.TryGetValue(a.Group, out var hunt))
                _groupHunts.Add(a.Group, hunt = new ActorGroup.Hunt());
            var previousLeader = FindActor(hunt.Leader); var previousTarget = FindActor(hunt.Target);
            if (hunt.Active(_elapsed) && (previousLeader == null || !CanJoinHunt(previousLeader) ||
                previousTarget == null || !Available(previousTarget) || previousTarget.OnRefuge)) hunt.Clear();
            if (!hunt.Active(_elapsed))
            {
                _groupCandidates.Clear();
                foreach (var member in _actors)
                    _groupCandidates.Add(new ActorGroup.Candidate(member.Id, member.Group,
                        FlatDistance(a.View.Root.position, member.View.Root.position),
                        member.OfferedPrey == null ? 0 : FlatDistance(a.View.Root.position, KnownPosition(member, member.OfferedPrey)),
                        member.OfferedPrey != null && CanJoinHunt(member) && member.Needs.Hunger >= .65 && !IsBlocked(a, member.OfferedPrey.Id)));
                int selected = ActorGroup.SelectLeader(a.Group, _groupCandidates, Mathf.Max(1, PackSupportRadius), 20);
                if (selected >= 0) hunt.Commit(_actors[selected].Id, _actors[selected].OfferedPrey.Id, _elapsed, 20d);
            }
            if (!hunt.Active(_elapsed)) continue;
            var leader = FindActor(hunt.Leader); var target = FindActor(hunt.Target);
            if (target == null || IsBlocked(a, target.Id) || !Observation(a, target, out var known) ||
                FlatDistance(a.View.Root.position, known.Position) > 20f) continue;
            bool alreadyJoined = a.JoinedTarget == hunt.Target && _elapsed < a.JoinedUntil;
            if (!alreadyJoined && FlatDistance(a.View.Root.position, leader.View.Root.position) > PackSupportRadius) continue;
            a.JoinedTarget = hunt.Target; a.JoinedUntil = hunt.Until;
            a.HuntLeader = leader; a.Prey = target; a.GroupHuntCommitted = true;
        }
    }
    public void TestPackConfrontation()
    {
        if (_soloWolf == null || !Available(_soloWolf)) return;
        int slot = 0;
        foreach (var a in _actors)
        {
            if (!a.Predator || !Available(a)) continue;
            Place(a.View.Root, a == _soloWolf ? new Vector3(22, 0, -15) : new Vector3(18, 0, -15 + slot++ * 2), a.Height * .5f);
            a.Navigation.Reset();
        }
        FocusActor(_actors.IndexOf(_soloWolf));
    }
    void Observe(Actor a, float dt)
    {
        var p = a.View.Root.position;
        a.Food = FindSource(a, false); a.Water = FindSource(a, true);
        Actor danger = null; float closest = float.MaxValue;
        a.Interest = null;
        foreach (var other in _actors)
        {
            if (other == a || !Available(other) || ActorGroup.Allied(a.Group, other.Group) ||
                !Observation(a, other, out var observation)) continue;
            float distance = FlatDistance(p, observation.Position);
            if ((a.Predator || other.Predator || observation.IdentityConfidence < .25f) &&
                (a.Interest == null || distance < FlatDistance(p, KnownPosition(a, a.Interest)))) a.Interest = other;
            // Losing a viewing angle does not make a recognized pursuer harmless. Other senses and
            // fading memory sustain retreat; they do not need to identify the same threat again.
            bool retreating = a.Threat == other && a.Brain.Objective == CreatureObjective.Escape;
            if (!other.Predator || !retreating && observation.IdentityConfidence < .25f) continue;
            float awareness = a.AlertAt >= 0f ? 12f : a.Predator ? 5f : 8f;
            if ((retreating || distance - observation.Uncertainty <= awareness) && distance < closest)
            { danger = other; closest = distance; }
        }
        if (danger != null && a.AlertAt < 0f) a.AlertAt = _elapsed;
        if (danger == null) a.AlertAt = -1f;
        a.Threat = danger != null && (a.Predator || closest <= 3f || _elapsed - a.AlertAt >= .2f) ? danger : null;
        if (a == _wolf && InterruptWolf) a.Threat = _actors[0];
        if (a.LastAttacker != null && Available(a.LastAttacker) &&
            Observation(a, a.LastAttacker, out var attackerObservation) && FlatDistance(p, attackerObservation.Position) < 12f) a.Threat = a.LastAttacker;
        else a.LastAttacker = null;
        if (a.Predator && a.Prey != null && Available(a.Prey) &&
            Visible(a, a.Prey) && FlatDistance(p, KnownPosition(a, a.Prey)) < 12f &&
            (a.Prey.Brain.Objective == CreatureObjective.Defend || Strength(a.Prey) > Strength(a) * 2.5d)) a.Threat = a.Prey;
        if (a.Threat != null && !Available(a.Threat)) a.Threat = null;
        var opponent = a.Threat ?? (a.Predator ? a.Prey : null);
        a.Support = NearbySupport(a);
        double opposing = opponent != null && Available(opponent) ? PerceivedStrength(a, opponent) + PerceivedSupport(a, opponent) : 0d;
        a.Disposition.Advance(dt, Strength(a), opposing, a.Threat != null, a.Support);
        a.Senses = new CreatureSenses { Position = p, Up = Vector3.up, Forward = a.View.Root.forward,
            Home = a.Home, DistanceToHome = FlatDistance(p, a.Home), DeltaTime = dt, Needs = a.Needs,
            Health = a.Health, MaxHealth = a.Vitals.Maximum, HasThreat = a.Threat != null, ThreatPosition = a.Threat != null ? KnownPosition(a, a.Threat) : p,
            Attack = a.Combat.Attack, CanAttack = a.Endurance.Stamina >= a.Combat.Attack.StaminaCost *
                (a.Brain.Objective == CreatureObjective.Defend ? .25d : 1d),
            NeedsHealing = a.Vitals.Wounds > .05d || a.Vitals.Fraction < .9d,
            ThreatId = a.Threat != null ? new(EntityId.HostOwner, a.Threat.Id) : default,
            ThreatDistance = a.Threat != null ? FlatDistance(p, KnownPosition(a, a.Threat)) : 0f, CanRest = true,
            PreyUnreachable = a.Prey != null && (a.Prey.OnRefuge ||
                _elapsed < a.BlockedUntil && a.BlockedTarget == new EntityId(EntityId.HostOwner, a.Prey.Id)),
            ThreatUnreachable = a.Threat != null && (a.Threat.OnRefuge ||
                _elapsed < a.BlockedUntil && a.BlockedTarget == new EntityId(EntityId.HostOwner, a.Threat.Id)),
            CanThreaten = a.Predator && a.Threat != null && a.Threat.Predator,
            ThreatProvoked = a.LastAttacker != null && a.LastAttacker == a.Threat,
            HasDisposition = true, CanDefend = a.Visual.Attack != null, EscapeBlocked = a.Id == 1 && FirstDeerCornered || a.NavigationObjective == CreatureObjective.Escape && a.Navigation.Failed,
            Fear = (float)a.Disposition.Fear, Confidence = (float)a.Disposition.Confidence, Courage = (float)a.Disposition.EffectiveCourage,
            CanSleep = AllowSleep && (a.Endurance.NeedsSleep || a.Endurance.Fatigue >= .35d ||
                a.Brain.Behaviour == CreatureBehaviour.Sleep && a.Endurance.Fatigue > .15d),
            NeedsRecovery = a.Endurance.Recovering, NeedsSleep = AllowSleep && a.Endurance.NeedsSleep,
            Food = KnownResource(a, a.Food), Water = KnownResource(a, a.Water),
            GroupHuntCommitted = a.GroupHuntCommitted,
            HasPrey = a.Prey != null && Available(a.Prey), PreyId = a.Prey != null ? new(EntityId.HostOwner, a.Prey.Id) : default,
            PreyPosition = a.Prey != null ? KnownPosition(a, a.Prey) : p, PreyAlert = a.Prey != null && Visible(a, a.Prey) && a.Prey.Velocity.sqrMagnitude > 4f,
            AttackDuration = a.Visual.Attack != null ? a.Visual.Attack.length : 1f };
        a.Senses.PerceptionLimited = true;
        a.Senses.DirectPrey = a.Prey != null && Visible(a, a.Prey);
        a.Senses.DirectThreat = a.Threat != null && Visible(a, a.Threat);
        a.Senses.PreyUncertainty = a.Prey != null && Observation(a, a.Prey, out var preyObservation) ? preyObservation.Uncertainty : 0;
        a.Senses.ThreatUncertainty = a.Threat != null && Observation(a, a.Threat, out var threatObservation) ? threatObservation.Uncertainty : 0;
        a.Senses.HasInterest = a.Interest != null;
        a.Senses.InterestPosition = a.Interest != null ? KnownPosition(a, a.Interest) : p;
        a.Senses.InterestId = a.Interest != null ? new(EntityId.HostOwner, a.Interest.Id) : default;
        a.Senses.InvestigateInterest = a.Predator && a.Needs.Hunger >= .5;
        a.Senses.AttackHitTime = a.Senses.AttackDuration * a.Visual.AttackHitNormalized;
        ObserveHome(a);
        ObserveParent(a);
    }
    double Strength(Actor a) => Mathf.Max(.1f, a.Predator ? WolfStrength : DeerStrength) *
        (.3d + .7d * a.Vitals.Fraction) * (.6d + .4d * a.Endurance.Stamina) *
        (LifeCycleEnabled ? System.Math.Pow(a.Life.PhysicalScale(a.LifeProfile), 2) : 1);
    bool IsBlocked(Actor a, uint id) => _elapsed < a.BlockedUntil && a.BlockedTarget == new EntityId(EntityId.HostOwner, id);
    Source FindSource(Actor a, bool water)
    {
        Source previous = water ? a.Water : a.Food;
        // Memory survives a chase, but a previous destination must not prevent choosing safer, closer food.
        var kind = water ? ActorObservationKind.Water : ActorObservationKind.Food;
        Source best = null; float distance = float.MaxValue;
        foreach (var s in _sources)
        {
            if (IsBlocked(a, s.Id) || !SourceAvailable(s, a.Diet) || water != (s.Stock.ThirstPerUnit > 0d) ||
                !a.Knowledge.Knows(s.Id, kind, _elapsed)) continue;
            var known = KnownResource(a, s);
            float d = FlatDistance(a.View.Root.position, known.Position) + known.Uncertainty;
            if (a.Knowledge.ThreatNear(known.Position, 8f, _elapsed)) d += 30f;
            if (s == previous) d *= .8f; // Keep a destination unless another is substantially better.
            if (d < distance) { distance = d; best = s; }
        }
        return best;
    }
    bool SourceAvailable(Source s, ResourceKind diet) => s.Stock.Accepts(diet) &&
        (s.Stock.ThirstPerUnit > 0d ? WaterAvailable : FoodAvailable) &&
        (s.Owner == null || s.Owner.Carrion.Stage != CorpseStage.Gone && (s.Owner.Id != 1 || TargetAvailable));
    void Damage(Actor a, double amount, bool traumatic, Actor attacker = null, double wounds = 0d, double bleeding = 0d)
    {
        if (a.Health <= 0 || amount <= 0) return;
        double applied = a.Vitals.Damage(amount, wounds, bleeding);
        if (traumatic) { a.Disposition.ReportHarm(applied / a.Vitals.Maximum); a.LastAttacker = attacker; }
        if (traumatic) { a.LastBlood = a.View.Root.position; _blood.Add(a.LastBlood, a.Health == 0 ? .65f : .25f, _gameSeconds); }
        if (!a.Vitals.Alive) { if (traumatic) a.DeathCause = ActorDeathCause.Attack; CreateCorpse(a); }
    }
    void CreateCorpse(Actor a)
    {
        if (a.Carrion != null) return;
        _deathCounts.TryGetValue(a.DeathCause, out int deaths); _deathCounts[a.DeathCause] = deaths + 1;
        a.HomeSite?.Release(a.Id);
        float bloated = Mathf.Max(0f, BloatedAfterDays);
        float rotting = Mathf.Max(bloated, RottingAfterDays);
        float bones = Mathf.Max(rotting + .01f, BonesAfterDays);
        float gone = Mathf.Max(bones + .01f, GoneAfterDays);
        var decay = new CorpseDecay(bloated * 86400f, rotting * 86400f, bones * 86400f, gone * 86400f,
            Mathf.Max(0f, FliesAfterDays) * 86400f, 1f);
        a.Velocity = Vector3.zero; a.Carrion = new CreatureCorpseResource(a.Combat.MeatYield *
            (LifeCycleEnabled && a.Life.Stage(a.LifeProfile) == ActorLifeStage.Juvenile ? a.Life.PhysicalScale(a.LifeProfile) : 1), decay);
        a.CorpseView = new CreatureCorpsePresentation(a.View.Root);
        _sources.Add(new Source { Id = 1000 + a.Id, Position = a.View.Root.position, Owner = a, Stock = a.Carrion.Meat });
    }
    public void AdvanceCarcasses(float days)
    {
        if (float.IsNaN(days) || float.IsInfinity(days) || days < 0f) return;
        foreach (var a in _actors) if (a.Carrion != null) a.Carrion.Advance(days * 86400d);
        Step(.001f);
    }
    public void WoundDeer() { if (_actors.Count > 0) Damage(_actors[0], _actors[0].Vitals.Maximum * .25d, true, wounds: .3d, bleeding: .15d); }
    double Sum(ResourceKind kind) { double total = 0; foreach (var s in _sources) if (s.Stock.Kind == kind) total += s.Stock.Remaining; return total; }
    static float FlatDistance(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;
    static void Place(Transform root, Vector3 p, float height)
    { p.y = CreatureAnimationPrototype.GroundHeight(p.x, p.z) + height; root.position = p; }
    void Marker(string label, Vector3 position, Color color, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = label;
        go.transform.SetParent(_sourceViews.transform, false); Place(go.transform, position, scale.y);
        go.transform.localScale = scale;
        var collider = go.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color = color; material.SetFloat("_Smoothness", 0f);
        go.GetComponent<Renderer>().sharedMaterial = material; _materials.Add(material);
    }
    void UpdateStatus()
    {
        var text = new StringBuilder();
        text.AppendLine($"Perception light {PerceptionLight:P0} | Focus an actor to inspect its senses");
        text.AppendLine($"{(Ecosystem ? "Ecosystem" : "Single pair")} | Day {_gameSeconds / 86400d:F2} | Living {LivingCount} | Hits {_hits} | Misses {_misses}");
        HabitatStatus(text);
        for (int row = 0; row < _actors.Count; row++)
        {
            int index = _focusActor >= 0 && _focusActor < _actors.Count
                ? row == 0 ? _focusActor : row <= _focusActor ? row - 1 : row : row;
            var a = _actors[index];
            if (LifeCycleEnabled) text.AppendLine($"  {a.Life.Sex} {a.Life.Stage(a.LifeProfile)} | Age {a.Life.AgeDays:F1}d" +
                (a.Life.Pregnant ? $" | Pregnant {a.Life.PregnancyDays:F1}/{a.LifeProfile.GestationDays:F1}d" : ""));
            text.AppendLine($"{a.View.Root.name}: {(a.Health > 0 ? a.Brain.Behaviour.ToString() : a.Carrion.Stage.ToString())} | HP {a.Health:F1}/{a.Vitals.Maximum} | Hunger {a.Needs.Hunger:P0} | Thirst {a.Needs.Thirst:P0}" +
                $" | Stamina {a.Endurance.Stamina:P0}/{a.Endurance.Capacity:P0} | Fatigue {a.Endurance.Fatigue:P0}" +
                (a.Carrion != null ? $" | Meat {a.Carrion.MeatFraction:P0} | Age {a.Carrion.AgeSeconds / 86400d:F1}d" : "") +
                $"\n    Fear {a.Disposition.Fear:P0} | Confidence {a.Disposition.Confidence:P0} | Courage {a.Disposition.EffectiveCourage:P0} (base {a.Disposition.Courage:P0}) | Group {a.Group} | Support {a.Support:F2} | Wounds {a.Vitals.Wounds:P0} | Bleeding {a.Vitals.Bleeding:P0} | Goal {a.Brain.Objective} | Prey {a.Prey?.Id ?? 0} | Leader {a.HuntLeader?.Id ?? 0} | Team hunt {a.GroupHuntCommitted} | Role {(a.Brain.Objective == CreatureObjective.Hunt ? a.PursuitRole.ToString() : "None")} | Approach {a.HasApproach} | Lane blocked {a.Senses.AttackLaneBlocked} | Route {a.Navigation.Status} | Home {(a.HomeSite == null ? "None" : a.Senses.AtHome ? "Here" : a.Senses.HomeSafe ? "Away" : "Unsafe")} | Memories {a.Knowledge.Count}");
            if (row == 0) PerceptionStatus(a, text);
        }
        foreach (var site in _homeSites)
            text.AppendLine($"{(site.OwnerGroup != 0 ? "Pack den" : "Solo site")}: {(site.Gathering ? "Gathering" : "Available")} | Wait {System.Math.Max(0, site.GatherUntil - _elapsed):F1}s | Known sources/threats {site.Knowledge.Count}");
        text.AppendLine($"Wolf goal: {_wolf.Brain.Objective} | Recovery {_wolf.Endurance.Recovering} | Sleep needed {_wolf.Endurance.NeedsSleep} | " +
            (_wolf.Food != null ? $"Known food {_wolf.Food.Id}: {_wolf.Food.Stock.Remaining:F2}" : "No known eligible food"));
        text.Append($"Plants {PlantRemaining:F2} | Water {WaterRemaining:F2} | Meat {MeatRemaining:F2} | Deer speed {DeerMoveSpeed:P0}");
        Status = text.ToString();
    }
    void OnGUI()
    {
        var oldColor = GUI.color;
        var oldContent = GUI.contentColor;
        GUI.color = GUI.contentColor = Color.white;
        DrawPerceptionDebug();
        _statusStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true,
            alignment = TextAnchor.UpperLeft, normal = { textColor = Color.white } };
        // Editor skin changes can reset cached style states between play sessions.
        _statusStyle.wordWrap = true; _statusStyle.fontSize = 16;
        _statusStyle.alignment = TextAnchor.UpperLeft;
        _statusStyle.normal.textColor = _statusStyle.hover.textColor = Color.white;
        _statusStyle.active.textColor = _statusStyle.focused.textColor = Color.white;
        float width = Mathf.Min(Screen.width - 30, 740);
        float panelHeight = Mathf.Min(Screen.height * .3f, 220);
        GUI.color = new Color(.025f, .03f, .04f, .96f);
        GUI.DrawTexture(new Rect(10, 10, width + 10, panelHeight + 8), Texture2D.whiteTexture);
        GUI.color = Color.white;
        float textHeight = _statusStyle.CalcHeight(new GUIContent(Status ?? "Initializing"), width - 28);
        _statusScroll = GUI.BeginScrollView(new Rect(15, 15, width, panelHeight), _statusScroll,
            new Rect(0, 0, width - 24, textHeight));
        GUI.Label(new Rect(0, 0, width - 28, textHeight), Status ?? "Initializing", _statusStyle);
        GUI.EndScrollView();
        float y = panelHeight + 24;
        int columns = Mathf.Max(1, Mathf.FloorToInt((width - 20) / 120f));
        _controlsScroll = GUI.BeginScrollView(new Rect(10, y, width + 10, Mathf.Max(40, Screen.height - y - 10)),
            _controlsScroll, new Rect(0, 0, width - 20, 425 + Mathf.CeilToInt(_actors.Count / (float)columns) * 30));
        y = 0;
        if (GUI.Button(new Rect(15, y, 100, 28), "Overview")) FocusActor(-1);
        if (GUI.Button(new Rect(120, y, 100, 28), "Free camera")) FollowCamera = false;
        if (GUI.Button(new Rect(225, y, 100, 28), "Pack den")) FocusHome(false);
        if (GUI.Button(new Rect(330, y, 100, 28), "Solo site")) FocusHome(true);
        if (GUI.Button(new Rect(435, y, 140, 28), "Renewable habitat")) StartHabitat();
        y += 32;
        for (int i = 0; i < _actors.Count; i++)
            if (GUI.Button(new Rect(15 + i % columns * 120, y + i / columns * 30, 115, 28), _actors[i].View.Root.name)) FocusActor(i);
        y += Mathf.CeilToInt(_actors.Count / (float)columns) * 30;
        GUI.Label(new Rect(15, y, width, 25), "Free camera: RMB look | WASD move | Q/E down/up | Shift faster", _statusStyle);
        y += 30;
        if (GUI.Button(new Rect(15, y, 120, 28), "Restart")) ResetScenario();
        if (GUI.Button(new Rect(140, y, 120, 28), Ecosystem ? "Single pair" : "Ecosystem")) { Ecosystem = !Ecosystem; ResetScenario(); }
        if (GUI.Button(new Rect(265, y, 120, 28), "Deer at 100%")) { DeerMoveSpeed = 1f; ResetScenario(); }
        FoodAvailable = GUI.Toggle(new Rect(395, y, 85, 28), FoodAvailable, "Food");
        WaterAvailable = GUI.Toggle(new Rect(480, y, 85, 28), WaterAvailable, "Water");
        y += 32;
        if (GUI.Button(new Rect(15, y, 135, 28), "Carcasses +1 day")) AdvanceCarcasses(1);
        if (GUI.Button(new Rect(155, y, 135, 28), "Carcasses +7 days")) AdvanceCarcasses(7);
        if (GUI.Button(new Rect(295, y, 140, 28), "Carcasses +30 days")) AdvanceCarcasses(30);
        if (GUI.Button(new Rect(440, y, 120, 28), "Wound deer")) WoundDeer();
        y += 32;
        if (GUI.Button(new Rect(15, y, 140, 28), "Toggle obstacles")) { NavigationObstacles = !NavigationObstacles; ResetScenario(); }
        if (GUI.Button(new Rect(160, y, 140, 28), "Deer on refuge")) PutDeerOnRefuge();
        if (GUI.Button(new Rect(305, y, 140, 28), "Refuge attack test")) TestRefugeAttack();
        if (GUI.Button(new Rect(450, y, 140, 28), "Pack confrontation")) TestPackConfrontation();
        y += 32;
        PackSupportEnabled = GUI.Toggle(new Rect(15, y, 200, 28), PackSupportEnabled, "Nearby pack support");
        y += 32;
        if (GUI.Button(new Rect(15, y, 130, 28), "Water awareness")) TestPerception(PerceptionScenario.WaterApproach);
        if (GUI.Button(new Rect(150, y, 130, 28), "Night vision")) TestPerception(PerceptionScenario.NightVision);
        if (GUI.Button(new Rect(285, y, 130, 28), "Hidden sound")) TestPerception(PerceptionScenario.HiddenSound);
        if (GUI.Button(new Rect(420, y, 130, 28), "Scent tracking")) TestPerception(PerceptionScenario.ScentTracking);
        y += 32;
        GUI.Label(new Rect(15, y, 120, 28), "Light " + PerceptionLight.ToString("P0"));
        PerceptionLight = GUI.HorizontalSlider(new Rect(140, y + 8, 300, 20), PerceptionLight, 0, 1);
        y += 32;
        DrawSenses = GUI.Toggle(new Rect(15, y, 150, 28), DrawSenses, "Draw senses");
        DrawSight = GUI.Toggle(new Rect(170, y, 100, 28), DrawSight, "Sight");
        DrawHearing = GUI.Toggle(new Rect(275, y, 100, 28), DrawHearing, "Hearing");
        DrawSmell = GUI.Toggle(new Rect(380, y, 100, 28), DrawSmell, "Smell");
        y += 28;
        DrawSenseMemory = GUI.Toggle(new Rect(15, y, 190, 28), DrawSenseMemory, "Remembered positions");
        GUI.Label(new Rect(210, y, 490, 28), "Drawings use the focused actor (first deer in Overview).");
        y += 28;
        GUI.Label(new Rect(15, y, 680, 28), "Green: sight | Cyan: hearing | Orange: smell | Rings: nominal ranges / remembered uncertainty");
        GUI.EndScrollView();
        foreach (var s in _sources) if (s.Owner == null) Label(s.Position, $"{s.Stock.Kind} {s.Stock.Remaining:F1}");
        foreach (var site in _homeSites) Label(site.Position, site.OwnerGroup != 0 ? "Pack den" : "Solo resting site");
        GUI.color = oldColor; GUI.contentColor = oldContent;
    }
    static void Label(Vector3 position, string text)
    {
        if (Camera.main == null) return;
        Vector3 screen = Camera.main.WorldToScreenPoint(position + Vector3.up * .7f);
        if (screen.z > 0f) GUI.Label(new Rect(screen.x - 60f, Screen.height - screen.y, 160f, 25f), text);
    }
    void OnDisable()
    {
        if (_freeCamera != null)
        {
            _freeCamera.InputOverride = null;
            if (_ownsCamera) Destroy(_freeCamera);
            else _freeCamera.enabled = true;
        }
        _cameraInput?.Dispose(); _cameraInput = null;
        DisposeViews();
    }
    void DisposeViews()
    {
        _navigationCourse?.Dispose(); _navigationCourse = null;
        _swarms?.Dispose(); _swarms = null; _blood?.Dispose(); _blood = null;
        foreach (var a in _actors) { a.CorpseView?.Dispose(); a.View.Dispose(); }
        _actors.Clear(); _sources.Clear(); _groupHunts.Clear(); _homeSites.Clear(); _wolf = _soloWolf = null;
        if (_sourceViews != null) { _sourceViews.SetActive(false); Destroy(_sourceViews); }
        foreach (var m in _materials) Destroy(m); _materials.Clear();
    }
}
