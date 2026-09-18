using UnityEngine;

/// <summary>Planet player lifecycle, input, and world services for the shared humanoid actor.</summary>
[DisallowMultipleComponent]
public sealed class PlanetCharacterController : MonoBehaviour, IGrassInteractor
{
    const float FootOffset = 0f;
    const float GrassBendRadius = 2.2f;
    const float GrassBendStrength = 0.8f;
    const float GrassReleaseSeconds = 0.6f;
    const float HarvestReach = 12f;   // from the camera, through the character, to the aimed instance (POC)
    const float HarvestPerp = 3.5f;   // generous corridor around the aim ray — tree pivots sit at the base,
                                      // so aiming near a trunk still picks it

    IPlanet _planet;
    IPlanetSurfaceSampler _sampler;
    IPlanetSurfaceRaycaster _raycaster;
    IInputProvider _input;
    ICameraRigContext _cameraRig;
    IFreeCameraService _freeCam;
    IWaterQueryService _water;
    ThreatRegistry _threats;

    PlanetHumanoidActor _humanoid;
    SurfaceCharacterController _driver => _humanoid != null ? _humanoid.Motor : null;
    Transform _child => _humanoid != null ? _humanoid.Actor : null;
    public PlanetHumanoidActor Actor => _humanoid;
    public bool IsSpawned => _spawned;

    uint _tick;

    Vector3 _center;
    float _radius;
    bool _hasPlanet;
    bool _spawned;

    bool _cursorLocked;

    // --- IGrassInteractor (boot-safe: Register runs before the first spawn creates the child) ---

    public Vector3 WorldPosition => _child != null ? _child.position : transform.position;
    public float Radius => GrassBendRadius;
    public float Strength => GrassBendStrength;
    public float ReleaseSeconds => GrassReleaseSeconds;
    public bool IsActive => _spawned && _child != null;

    void OnEnable()
    {
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
        GrassInteractorRegistry.Register(this);
    }

    void OnDisable()
    {
        GrassInteractorRegistry.Unregister(this);
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
        Despawn();
    }

    void OnDestroy()
    {
        if (_humanoid != null) Destroy(_humanoid.gameObject);
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            SetCursorLocked(false); // never keep the cursor captured when the window loses focus
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _center = evt.PlanetCenter;
        _radius = evt.PlanetRadius;
        _hasPlanet = _radius > 0f;
        ServiceLocator.TryGet(out _sampler);
        ServiceLocator.TryGet(out _raycaster);
        ServiceLocator.TryGet(out _water);
        _threats = null;   // world-scoped: a new world has a new registry, so never keep the old one

        if (_spawned && _hasPlanet && TrySeedPose(out CharacterPose seed))
            RebuildDriver(seed);
    }

    void Update()
    {
        if (!_spawned || _driver == null || _child == null)
            return;

        ActorIntent intent = _input != null ? _input.Sample(_tick++) : default;
        SetCursorLocked(intent.Held(ActorButtons.LookHold));
        bool wasAttached = _humanoid.Ladder.Active || _humanoid.LadderApproaching || _humanoid.Beam.Active || _humanoid.Rope.Active;
        _humanoid.Step(Time.deltaTime, intent);
        bool attached = _humanoid.Ladder.Active || _humanoid.LadderApproaching || _humanoid.Beam.Active || _humanoid.Rope.Active;

        // Harvest the aimed scatter instance on Interact. Rare event, so resolve the interactor per press
        // (always the active world's) rather than caching a ref that would go stale on regen.
        if (intent.Held(ActorButtons.Interact) && !wasAttached && !attached)
        {
            Transform cam = ResolveCameraRig()?.CameraTransform;
            if (cam != null && ServiceLocator.TryGet(out HarvestInteractor harvest))
                harvest.TryHarvestLookedAt(new Ray(cam.position, cam.forward), HarvestReach, HarvestPerp);
        }

        CharacterPose pose = _driver.Pose;

        // A spawned player is a THREAT-bearing entity, which the free camera deliberately is not: wildlife
        // reacts to identities, and a debug camera has none. This is the only thing that makes deer run.
        ResolveThreats()?.Report(ThreatRegistry.LocalPlayer, pose.Position, CreatureFaction.Player);
    }

    void LateUpdate()
    {
        if (!_spawned || _child == null || _cameraRig == null)
            return;
        Transform cam = _cameraRig.CameraTransform;
        if (cam == null)
            return;

        _humanoid.ThirdPersonCamera.Follow(cam.GetComponent<Camera>(), _driver.Pose,
            _humanoid.View.CameraFocusHeight, _humanoid.CameraDistance, Time.deltaTime);
    }

    // Screen-center crosshair: the harvest ray is the camera's forward (screen centre), NOT the mouse cursor,
    // so this shows where Interact (F) will aim. Aim it at the base of a tree.
    void OnGUI()
    {
        if (!_spawned)
            return;
        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;
        Color prev = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(cx - 6f, cy - 1f, 13f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 1f, cy - 6f, 2f, 13f), Texture2D.whiteTexture);
        GUI.color = prev;
    }

    // --- Spawn / despawn (driven by the static CharacterCommands) ---------

    public ConsoleCommandResult Spawn()
    {
        if (!EnsurePlanet(out string err)) return ConsoleCommandResult.Fail(err);
        if (!TrySeedPose(out CharacterPose seed))
            return ConsoleCommandResult.Fail("no surface under the view — aim at the planet and retry");
        var prefab = Resources.Load<PlanetHumanoidActor>("Characters/PlanetHumanoid");
        if (prefab == null)
            return ConsoleCommandResult.Fail("Planet humanoid prefab is missing: Resources/Characters/PlanetHumanoid");
        if (_humanoid == null)
        {
            _humanoid = Instantiate(prefab, transform);
            _humanoid.name = "Planet player";
        }
        try
        {
            RebuildDriver(seed);
            if (_driver == null || _child == null)
                throw new System.InvalidOperationException("Planet humanoid animation assets are incomplete.");
            _spawned = true;
            SuspendFreeCamera(true);
            return ConsoleCommandResult.Ok("character spawned; WASD move, RMB look, Space jump/swim up, Shift run, Ctrl crouch/dive; character.despawn restores free flight");
        }
        catch (System.Exception exception)
        {
            _humanoid.gameObject.SetActive(false);
            _spawned = false;
            _threats?.Withdraw(ThreatRegistry.LocalPlayer);
            SetCursorLocked(false);
            SuspendFreeCamera(false);
            return ConsoleCommandResult.Fail("Character initialization failed: " + exception.Message);
        }
    }

    public string Despawn()
    {
        bool wasSpawned = _spawned;
        _spawned = false;
        if (_humanoid != null) _humanoid.gameObject.SetActive(false);
        _threats?.Withdraw(ThreatRegistry.LocalPlayer);
        SetCursorLocked(false);
        SuspendFreeCamera(false);
        return wasSpawned ? "character despawned; free-fly restored" : "character not spawned";
    }

    // --- Composition / lifecycle -----------------------------------------

    void RebuildDriver(CharacterPose seed)
    {
        var gravity = new RadialGravityProvider(_center);
        // Ground on the VISIBLE mesh (raycast), falling back to the analytic surface if a ray misses — the
        // analytic radius can sit below the rendered terrain, which is why the capsule fell through.
        var samplerGround = new PlanetSurfaceGrounding(_sampler, _center);
        IGroundingProvider grounding = ResolveRaycaster() != null
            ? new PlanetRaycastGrounding(_raycaster, _center, default, samplerGround)
            : samplerGround;
        ServiceLocator.TryGet(out _water);
        _humanoid.gameObject.SetActive(false);
        _humanoid.Configure(seed, gravity, grounding, new CharacterSwimmingWater(_water));
        _humanoid.Ladders = FindObjectsByType<LadderInteraction>(FindObjectsSortMode.None);
        _humanoid.Beams = FindObjectsByType<BeamInteraction>(FindObjectsSortMode.None);
        _humanoid.Ropes = FindObjectsByType<RopeInteraction>(FindObjectsSortMode.None);
        _humanoid.gameObject.SetActive(true);
        if (_child != null && _child.GetComponent<WaterInteractor>() == null)
            _child.gameObject.AddComponent<WaterInteractor>();
    }

    bool EnsurePlanet(out string err)
    {
        err = null;
        if (_sampler == null) ServiceLocator.TryGet(out _sampler);
        _threats = null;   // world-scoped: never carry one world's registry into the next
        if (_planet == null) ServiceLocator.TryGet(out _planet);
        if (_sampler == null || _planet == null)
        {
            err = "no planet services — generate a planet first";
            return false;
        }
        if (_planet.IsGenerating)
        {
            err = "planet is generating; wait until generation completes";
            return false;
        }
        if (_planet.LastGeneratedRadius <= 0f)
        {
            err = "planet not generated yet";
            return false;
        }
        // IPlanet is the reliable source (the camera rig's radius can lag the generation event by a frame).
        if (_planet.Transform != null) _center = _planet.Transform.position;
        _radius = _planet.LastGeneratedRadius;
        _hasPlanet = true;
        ResolveInput();
        ResolveCameraRig();
        ResolveRaycaster();
        return true;
    }

    bool TrySeedPose(out CharacterPose seed)
    {
        seed = default;
        Transform camT = ResolveCameraRig()?.CameraTransform;
        Vector3 up;
        Vector3 groundPoint;

        // Spawn where the camera is looking: raycast the surface along camera forward.
        if (camT != null && ResolveRaycaster() != null &&
            _raycaster.TryRaycastSurface(new Ray(camT.position, camT.forward), Mathf.Max(_radius * 4f, 1000f),
                out PlanetSurfaceRaycastHit hit))
        {
            groundPoint = hit.Point;
            up = (hit.Point - _center).sqrMagnitude > 1e-6f ? (hit.Point - _center).normalized : Vector3.up;
        }
        else
        {
            // Fallback: the surface radially under the camera.
            Vector3 origin = camT != null ? camT.position : _center + Vector3.up * (_radius + 10f);
            Vector3 dir = origin - _center;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up;
            if (!_sampler.TryGetSurfaceRadius(dir, out float r))
                return false;
            up = dir;
            groundPoint = _center + dir * r;
        }

        Vector3 pos = groundPoint + up * FootOffset;
        Vector3 fwd = camT != null && CharacterMath.TryProjectOntoTangent(camT.forward, up, out Vector3 f)
            ? f
            : CharacterMath.ArbitraryTangent(up);
        seed = new CharacterPose(pos, up, fwd);
        return seed.IsFinite;
    }

    void SetCursorLocked(bool locked)
    {
        if (locked == _cursorLocked)
            return;
        _cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void SuspendFreeCamera(bool suspended)
    {
        if (ResolveFreeCam() != null)
            _freeCam.InputSuspended = suspended;
    }

    void ResolveInput()
    {
        if (_input == null && ServiceLocator.TryGet(out IInputMapService map))
            _input = new LocalPlayerInput(map);
    }

    IPlanetSurfaceRaycaster ResolveRaycaster()
    {
        if (_raycaster == null)
            ServiceLocator.TryGet(out _raycaster);
        return _raycaster;
    }

    ICameraRigContext ResolveCameraRig()
    {
        if (_cameraRig == null)
            ServiceLocator.TryGet(out _cameraRig);
        return _cameraRig;
    }

    IFreeCameraService ResolveFreeCam()
    {
        if (_freeCam == null)
            ServiceLocator.TryGet(out _freeCam);
        return _freeCam;
    }

    // World-scoped, so it is re-resolved rather than cached across a regeneration.
    ThreatRegistry ResolveThreats()
    {
        if (_threats == null) ServiceLocator.TryGet(out _threats);
        return _threats;
    }
}
