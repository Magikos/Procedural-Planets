using UnityEngine;

/// <summary>Local authority fixture using the production brain, needs, source stock, and animation view.</summary>
public sealed class PredatorEncounterPrototype : MonoBehaviour
{
    public CreatureVisualSettings WolfVisuals, DeerVisuals;
    [Header("Live needs (zero is full)")]
    [Range(0f, 1f)] public float WolfHunger = 0.5f, WolfThirst = 0.15f;
    [Range(0f, 1f)] public float DeerHunger = 0.85f, DeerThirst = 0.65f;
    [Header("Accelerated simulation")]
    [Min(1f)] public float HungerSeconds = 80f, ThirstSeconds = 160f;
    [Min(0.01f)] public float ConsumeUnitsPerSecond = 0.12f;
    public bool AllowSleep = true;
    [Header("Movement")]
    [Range(0.1f, 1f)] public float DeerMoveSpeed = 0.4f;
    [Min(0.1f)] public float DeerFullRunSpeed = 6f, WolfRunSpeed = 4f;
    [Range(0.1f, 1f)] public float WoundedSpeed = 0.7f;
    public Vector3 PlantPosition = new(0f, 0f, 3f), WaterPosition = new(4f, 0f, 3f);
    public bool TargetAvailable = true, InterruptWolf, RestartRequested;
    [TextArea] public string Status;
    public int DeerHealth => _health;
    public double MeatRemaining => _meat?.Remaining ?? 0d;
    public double PlantRemaining => _plants?.Remaining ?? 0d;
    public double WaterRemaining => _water?.Remaining ?? 0d;
    public CreatureBehaviour WolfBehaviour => _wolfBrain?.Behaviour ?? CreatureBehaviour.Wander;
    public CreatureBehaviour DeerBehaviour => _deerBrain?.Behaviour ?? CreatureBehaviour.Wander;
    CreatureAnimationView _wolf, _deer;
    CreatureBrain _wolfBrain, _deerBrain;
    CreatureVisualDto _wolfVisual, _deerVisual;
    ActorResourceSource _plants, _water, _meat;
    GameObject _sourceViews;
    readonly CreatureAnimationPrototype.PrototypeGrounding _ground = new();
    readonly System.Collections.Generic.List<Material> _materials = new();
    int _health, _hits, _misses;
    uint _tick;
    float _alertTime = -1f, _elapsed;
    GUIStyle _statusStyle;
    static readonly EntityId DeerId = new(EntityId.HostOwner, 1);
    const ResourceKind DeerDiet = ResourceKind.Plants | ResourceKind.FreshWater;
    const ResourceKind WolfDiet = ResourceKind.Meat | ResourceKind.FreshWater;

    void Start() => Restart();

    [ContextMenu("Restart Encounter")]
    public void Restart()
    {
        if (!Application.isPlaying || WolfVisuals == null || DeerVisuals == null) return;
        DisposeViews();
        _wolfVisual = WolfVisuals.Snapshot(); _deerVisual = DeerVisuals.Snapshot();
        _wolf = new CreatureAnimationView(transform, 2, _wolfVisual, 1f);
        _deer = new CreatureAnimationView(transform, 1, _deerVisual, 1.84f);
        Place(_wolf.Root, new Vector3(-2f, 0f, -6f), .5f);
        Place(_deer.Root, new Vector3(0f, 0f, -1f), .92f);
        _wolfBrain = new CreatureBrain(2, null, CreatureBehaviour.Wander);
        _deerBrain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
        foreach (var view in new[] { _wolf, _deer })
            CreatureAnimationPrototype.ApplyPreviewMaterials(view.Root, _materials);
        _plants = new ActorResourceSource(ResourceKind.Plants, 2d, 1d, 0d);
        _water = new ActorResourceSource(ResourceKind.FreshWater, 10d, 0d, 1d);
        _meat = null;
        _sourceViews = new GameObject("Survival sources"); _sourceViews.transform.SetParent(transform, false);
        Marker("Plants", PlantPosition, new Color(.22f, .48f, .08f), new Vector3(1.4f, .2f, 1.4f));
        Marker("Fresh water", WaterPosition, new Color(.08f, .4f, .7f), new Vector3(2.4f, .06f, 2.4f));
        _health = 2; _hits = _misses = 0; _tick = 0; _elapsed = 0; _alertTime = -1;
        TargetAvailable = true; InterruptWolf = false; RestartRequested = false;
    }

    [ContextMenu("Reset Survival Scenario")]
    public void ResetScenario()
    {
        WolfHunger = .5f; WolfThirst = .15f; DeerHunger = .85f; DeerThirst = .65f;
        Restart();
    }

    void Update()
    {
        if (RestartRequested) ResetScenario();
        Step(Time.deltaTime);
    }

    public void Step(float dt)
    {
        if (_wolf == null || dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
        // Bound movement and attack windows when the fixture is advanced manually.
        while (dt > 0f) { float step = Mathf.Min(dt, .05f); Simulate(step); dt -= step; }
        UpdateStatus();
    }

    void Simulate(float dt)
    {
        _elapsed += dt; _tick++;
        _deer.Root.gameObject.SetActive(TargetAvailable);
        bool alive = TargetAvailable && _health > 0;
        var wolfNeeds = new ActorNeeds(Mathf.Clamp01(WolfHunger), Mathf.Clamp01(WolfThirst))
            .Advance(dt, Mathf.Max(1f, HungerSeconds), Mathf.Max(1f, ThirstSeconds));
        var deerNeeds = new ActorNeeds(Mathf.Clamp01(DeerHunger), Mathf.Clamp01(DeerThirst));
        if (alive) deerNeeds = deerNeeds.Advance(dt, Mathf.Max(1f, HungerSeconds), Mathf.Max(1f, ThirstSeconds));
        float distance = Vector3.Distance(_wolf.Root.position, _deer.Root.position);
        bool pursuing = _wolfBrain.Objective == CreatureObjective.Hunt;
        if (alive && pursuing && _alertTime < 0f && distance < 4f) _alertTime = _elapsed;
        if (distance > 20f || !pursuing && distance > 12f) _alertTime = -1f;
        bool fleeing = alive && _alertTime >= 0f && _elapsed - _alertTime >= .6f;
        var deerSenses = Senses(_deer, fleeing, _wolf.Root.position, dt, deerNeeds);
        deerSenses.Food = Target(_plants, DeerDiet, PlantPosition, 3);
        deerSenses.Water = Target(_water, DeerDiet, WaterPosition, 4);
        _deerBrain.Observe(deerSenses);
        ActorIntent deerIntent = alive ? _deerBrain.Sample(_tick) : default;
        float deerSpeed = fleeing ? DeerFullRunSpeed * DeerMoveSpeed * (_health < 2 ? WoundedSpeed : 1f) : _deerBrain.SpeedScale;
        Vector3 deerVelocity = alive ? Move(_deer, deerIntent, deerSpeed, .92f, dt) : Vector3.zero;
        if (alive) Consume(_deer, _deerBrain, DeerDiet, _plants, PlantPosition, ref deerNeeds, dt);
        Present(_deer, _deerBrain, deerVelocity);
        _deer.LookTarget = _alertTime >= 0f && !fleeing ? _wolf.Root.position : null;

        var senses = Senses(_wolf, InterruptWolf, _deer.Root.position, dt, wolfNeeds);
        senses.HasPrey = alive; senses.PreyId = DeerId;
        senses.PreyAlert = _alertTime >= 0f; senses.PreyPosition = _deer.Root.position;
        senses.Food = Target(TargetAvailable ? _meat : null, WolfDiet, _deer.Root.position, 1);
        senses.Water = Target(_water, WolfDiet, WaterPosition, 4);
        senses.AttackDuration = _wolfVisual.Attack != null ? _wolfVisual.Attack.length : 1f;
        senses.AttackHitTime = senses.AttackDuration * _wolfVisual.AttackHitNormalized;
        _wolfBrain.Observe(senses);
        ActorIntent wolfIntent = _wolfBrain.Sample(_tick);
        float wolfSpeed = _wolfBrain.SpeedScale * (WolfBehaviour == CreatureBehaviour.Chase ? WolfRunSpeed / 4f : 1f);
        Vector3 wolfVelocity = Move(_wolf, wolfIntent, wolfSpeed, .5f, dt);
        if (_wolfBrain.HitRequested)
        {
            if (alive && CreatureHunt.CanBite(_wolf.Root.position, _wolf.Root.forward, Vector3.up, _deer.Root.position))
            {
                // Fixture authority. Production damage remains owned by CreatureResidencyService.Strike.
                _health = Mathf.Max(0, _health - 1); _hits++;
                if (_health == 0) _meat = new ActorResourceSource(ResourceKind.Meat, 1.5d, 1d, 0d);
            }
            else _misses++;
        }
        Consume(_wolf, _wolfBrain, WolfDiet, TargetAvailable ? _meat : null, _deer.Root.position, ref wolfNeeds, dt);
        Present(_wolf, _wolfBrain, wolfVelocity);
        _wolf.LookTarget = alive && _wolfBrain.Objective == CreatureObjective.Hunt && WolfBehaviour != CreatureBehaviour.Attack
            ? _deer.Root.position : null;
        _wolf.AttackTime = WolfBehaviour == CreatureBehaviour.Attack ? _wolfBrain.AttackTime : null;
        _deer.Dead = _health == 0;
        _wolf.Tick(wolfVelocity, Vector3.up, dt, _ground); _deer.Tick(deerVelocity, Vector3.up, dt, _ground);
        WolfHunger = (float)wolfNeeds.Hunger; WolfThirst = (float)wolfNeeds.Thirst;
        DeerHunger = (float)deerNeeds.Hunger; DeerThirst = (float)deerNeeds.Thirst;
    }

    void Consume(CreatureAnimationView view, CreatureBrain brain, ResourceKind diet, ActorResourceSource food,
        Vector3 foodPosition, ref ActorNeeds needs, float dt)
    {
        bool drinking = brain.Behaviour == CreatureBehaviour.Drink;
        var source = drinking ? _water : brain.Behaviour == CreatureBehaviour.Feed ? food : null;
        Vector3 position = drinking ? WaterPosition : foodPosition;
        if (source == null || Vector3.ProjectOnPlane(view.Root.position - position, Vector3.up).magnitude > CreatureConsumeState.Reach + .02f)
            return;
        source.Consume(ref needs, diet, Mathf.Max(0f, ConsumeUnitsPerSecond) * dt);
    }

    static void Present(CreatureAnimationView view, CreatureBrain brain, Vector3 velocity)
    {
        view.Resting = brain.Behaviour == CreatureBehaviour.Rest;
        view.Sleeping = brain.Behaviour == CreatureBehaviour.Sleep;
        view.Eating = brain.Behaviour == CreatureBehaviour.Feed && velocity.sqrMagnitude < .0004f;
        view.Drinking = brain.Behaviour == CreatureBehaviour.Drink && velocity.sqrMagnitude < .0004f;
    }

    CreatureSenses Senses(CreatureAnimationView view, bool threat, Vector3 from, float dt, ActorNeeds needs) => new()
    {
        Position = view.Root.position, Up = Vector3.up, Forward = view.Root.forward, Home = view.Root.position,
        DeltaTime = dt, HasThreat = threat, ThreatPosition = from, Needs = needs, CanRest = true, CanSleep = AllowSleep,
        ThreatDistance = Vector3.Distance(view.Root.position, from)
    };

    static CreatureResourceTarget Target(ActorResourceSource source, ResourceKind diet, Vector3 position, uint id) => new()
    { Available = source != null && source.Accepts(diet), Position = position, Id = new EntityId(EntityId.HostOwner, id) };

    static Vector3 Move(CreatureAnimationView view, ActorIntent intent, float speed, float halfHeight, float dt)
    {
        view.Root.rotation = Quaternion.AngleAxis(intent.Look.x * dt, Vector3.up) * view.Root.rotation;
        Vector3 velocity = view.Root.forward * (intent.Move.y * speed);
        Place(view.Root, view.Root.position + velocity * dt, halfHeight);
        return velocity;
    }

    static void Place(Transform root, Vector3 p, float height)
    { p.y = CreatureAnimationPrototype.GroundHeight(p.x, p.z) + height; root.position = p; }

    void Marker(string label, Vector3 position, Color color, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = label;
        go.transform.SetParent(_sourceViews.transform, false); Place(go.transform, position, scale.y);
        go.transform.localScale = scale; Destroy(go.GetComponent<Collider>());
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = color; material.SetFloat("_Smoothness", 0f);
        go.GetComponent<Renderer>().sharedMaterial = material; _materials.Add(material);
    }

    void UpdateStatus()
    {
        Status = $"Wolf: {WolfBehaviour} / {_wolfBrain.Objective} | Hunger {WolfHunger:P0} | Thirst {WolfThirst:P0}\n" +
            $"Deer: {(_health == 0 ? "Dead" : DeerBehaviour.ToString())} | Hunger {DeerHunger:P0} | Thirst {DeerThirst:P0}\n" +
            $"Health {_health}/2 | Hits {_hits} | Misses {_misses} | Deer speed {DeerMoveSpeed:P0}\n" +
            $"Plants {PlantRemaining:F2} | Fresh water {WaterRemaining:F2} | Meat {MeatRemaining:F2} | {_elapsed:F1}s";
        if (Camera.main == null) return;
        Vector3 center = (_wolf.Root.position + _deer.Root.position + PlantPosition + WaterPosition) * .25f;
        float spread = Mathf.Max(Vector3.Distance(_wolf.Root.position, center), Vector3.Distance(_deer.Root.position, center));
        Camera.main.fieldOfView = 40f;
        Camera.main.transform.position = center + new Vector3(1f, .8f, -.5f).normalized * Mathf.Max(19f, spread * 3f);
        Camera.main.transform.LookAt(center);
    }

    void OnGUI()
    {
        _statusStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.UpperLeft };
        GUI.Box(new Rect(15, 15, 790, 115), Status ?? "Initializing encounter", _statusStyle);
        if (GUI.Button(new Rect(15, 135, 180, 30), "Reset survival scenario")) ResetScenario();
        if (GUI.Button(new Rect(205, 135, 180, 30), "Test deer at 100%")) { DeerMoveSpeed = 1f; ResetScenario(); }
        Label(PlantPosition, "PLANTS"); Label(WaterPosition, "FRESH WATER");
    }
    static void Label(Vector3 position, string text)
    {
        if (Camera.main == null) return;
        Vector3 screen = Camera.main.WorldToScreenPoint(position + Vector3.up * .7f);
        if (screen.z > 0f) GUI.Label(new Rect(screen.x - 60f, Screen.height - screen.y, 140f, 25f), text);
    }
    void OnDisable() => DisposeViews();
    void DisposeViews()
    {
        _wolf?.Dispose(); _deer?.Dispose(); _wolf = _deer = null;
        if (_sourceViews != null) { _sourceViews.SetActive(false); Destroy(_sourceViews); }
        foreach (var material in _materials) Destroy(material);
        _materials.Clear();
    }
}
