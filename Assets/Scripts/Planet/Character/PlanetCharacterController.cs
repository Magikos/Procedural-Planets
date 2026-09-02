using UnityEngine;

/// <summary>
/// The thin player/planet HOST for the walking-character MVP. It owns only the Unity/player concerns: the
/// local input provider, a mouse-look third-person camera, grass, and a separate movable child GameObject. All
/// surface math lives in the actor-agnostic <see cref="SurfaceCharacterController"/>; this host resolves the
/// planet-specific providers, feeds look-relative intent into the driver, and applies the returned pose to the
/// child. It never moves its own transform.
///
/// Controls (fly-camera style): mouse looks, character faces the look direction, W/S forward/back, A/D strafe,
/// Space jump, Left-Shift sprint, Left-Ctrl crouch.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlanetCharacterController : MonoBehaviour, IGrassInteractor
{
    const float FootOffset = 1f;        // capsule primitive half-height (origin is its center)
    const float WalkSpeed = 5f;
    const float SprintMult = 2f;
    const float CrouchMult = 0.45f;
    const float LookSensitivity = 0.12f;
    const float MinPitch = -70f;
    const float MaxPitch = 75f;
    const float CamDistance = 5.5f;
    const float CamLookHeight = 1.3f;
    const float StartPitch = 12f;
    const float GrassBendRadius = 2.2f;
    const float GrassBendStrength = 0.8f;
    const float GrassReleaseSeconds = 0.6f;
    const float HarvestReach = 12f;   // from the camera, through the character, to the aimed instance (POC)
    const float HarvestPerp = 3.5f;   // generous corridor around the aim ray — tree pivots sit at the base,
                                      // so aiming near a trunk still picks it

    IPlanet _planet;
    IPlanetSurfaceSampler _sampler;
    IPlanetSurfaceRaycaster _raycaster;
    IWaterQueryService _water;
    IInputProvider _input;
    ICameraRigContext _cameraRig;
    IFreeCameraService _freeCam;
    ThreatRegistry _threats;

    SurfaceCharacterController _driver;
    Transform _child;
    Material _propMaterial;

    uint _tick;

    Vector3 _center;
    float _radius;
    bool _hasPlanet;
    bool _spawned;

    Vector3 _forward = Vector3.forward; // character horizontal facing (tangent unit)
    float _pitch;                       // camera pitch (degrees)
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
        SetCursorLocked(false);
        SuspendFreeCamera(false);
    }

    void OnDestroy()
    {
        if (_child != null)
            Destroy(_child.gameObject);
        if (_propMaterial != null)
            Destroy(_propMaterial);
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
        if (_sampler == null)
            ServiceLocator.TryGet(out _sampler);
        _threats = null;   // world-scoped: a new world has a new registry, so never keep the old one
        _water = null;     // world-scoped for the same reason — a new world solves new bodies

        if (_spawned && _hasPlanet && TrySeedPose(out CharacterPose seed))
            RebuildDriver(seed);
    }

    void Update()
    {
        if (!_spawned || _driver == null || _child == null)
            return;

        Vector3 up = _driver.Pose.Up;
        ActorIntent intent = _input != null ? _input.Sample(_tick++) : default;

        // Look only while HOLDING right-mouse (like the free camera) — the cursor is captured only during the
        // hold and released the instant you let go, so it can never trap the mouse (e.g. to open the console).
        SetCursorLocked(intent.Held(ActorButtons.LookHold));
        if (intent.Look.sqrMagnitude > 0.0001f)
        {
            _forward = Quaternion.AngleAxis(intent.Look.x * LookSensitivity, up) * _forward;
            _pitch = Mathf.Clamp(_pitch - intent.Look.y * LookSensitivity, MinPitch, MaxPitch);
        }
        if (CharacterMath.TryProjectOntoTangent(_forward, up, out Vector3 fp))
            _forward = fp;

        float speed = WalkSpeed;
        if (intent.Held(ActorButtons.Sprint)) speed *= SprintMult;
        if (intent.Held(ActorButtons.Crouch)) speed *= CrouchMult;

        // Harvest the aimed scatter instance on Interact. Rare event, so resolve the interactor per press
        // (always the active world's) rather than caching a ref that would go stale on regen.
        if (intent.Held(ActorButtons.Interact))
        {
            Transform cam = ResolveCameraRig()?.CameraTransform;
            if (cam != null && ServiceLocator.TryGet(out HarvestInteractor harvest))
                harvest.TryHarvestLookedAt(new Ray(cam.position, cam.forward), HarvestReach, HarvestPerp);
        }

        CharacterPose pose = _driver.Tick(
            intent.Move, _forward, speed, Time.deltaTime, intent.Held(ActorButtons.Jump));
        _forward = pose.Forward;
        _child.SetPositionAndRotation(pose.Position, Quaternion.LookRotation(pose.Forward, pose.Up));

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

        Vector3 up = _child.up;
        Vector3 fwd = _child.forward;
        Vector3 right = Vector3.Cross(up, fwd);
        Vector3 viewDir = Quaternion.AngleAxis(_pitch, right) * fwd; // pitch tilts the orbit up/down
        Vector3 focus = _child.position + up * CamLookHeight;
        cam.position = focus - viewDir * CamDistance;
        cam.rotation = Quaternion.LookRotation(viewDir, up);
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

    public string Spawn()
    {
        if (!EnsurePlanet(out string err))
            return err;
        if (!TrySeedPose(out CharacterPose seed))
            return "no surface under the view — aim at the planet and retry";

        EnsureChild();
        _forward = seed.Forward;
        _pitch = StartPitch;
        RebuildDriver(seed);
        _child.gameObject.SetActive(true);
        _spawned = true;
        SuspendFreeCamera(true);
        return "character spawned; WASD walk, HOLD right-mouse to look, Space jump, Shift sprint, Ctrl crouch; `character.despawn` to exit";
    }

    public string Despawn()
    {
        if (!_spawned)
            return "character not spawned";
        _spawned = false;
        if (_child != null)
            _child.gameObject.SetActive(false);
        // Stop frightening the wildlife the moment the player stops existing.
        ResolveThreats()?.Withdraw(ThreatRegistry.LocalPlayer);
        SetCursorLocked(false);
        SuspendFreeCamera(false);
        return "character despawned; free-fly restored";
    }

    // --- Composition / lifecycle -----------------------------------------

    void RebuildDriver(CharacterPose seed)
    {
        var gravity = new RadialGravityProvider(_center);
        // Ground on the VISIBLE mesh (raycast), falling back to the analytic surface if a ray misses — the
        // analytic radius can sit below the rendered terrain, which is why the capsule fell through.
        CharacterWaterFloor water = WaterFloor();
        var samplerGround = new PlanetSurfaceGrounding(_sampler, _center, water);
        IGroundingProvider grounding = ResolveRaycaster() != null
            ? new PlanetRaycastGrounding(_raycaster, _center, water, samplerGround)
            : samplerGround;
        _driver = new SurfaceCharacterController(gravity, grounding, FootOffset, seed);
        _child.SetPositionAndRotation(seed.Position, Quaternion.LookRotation(seed.Forward, seed.Up));
    }

    // Resolved on spawn and on regeneration only — the floor itself is queried per position, never the service.
    CharacterWaterFloor WaterFloor()
    {
        if (_water == null) ServiceLocator.TryGet(out _water);
        return new CharacterWaterFloor(_water, _center);
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

        // Never seed below the water HERE — in ocean or in a raised lake, stand on THAT body's surface. A
        // global sea radius would seed 40 m under the surface of a lake that spilled above it.
        float waterRadius = WaterFloor().RadiusAt(groundPoint, up);
        if (waterRadius > 0f && Vector3.Dot(groundPoint - _center, up) < waterRadius)
            groundPoint = _center + up * waterRadius;

        Vector3 pos = groundPoint + up * FootOffset;
        Vector3 fwd = camT != null && CharacterMath.TryProjectOntoTangent(camT.forward, up, out Vector3 f)
            ? f
            : CharacterMath.ArbitraryTangent(up);
        seed = new CharacterPose(pos, up, fwd);
        return seed.IsFinite;
    }

    void EnsureChild()
    {
        if (_child != null)
            return;

        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "Character (MVP)";
        // No physics against terrain — the primitive's collider is unwanted (chunks have no colliders either).
        Collider col = go.GetComponent<Collider>();
        if (col != null)
            Destroy(col);

        // Planet-aware lit material so the capsule darkens on the night side (the planet body occludes the
        // sun). Default URP Lit takes the raw directional sun and stays bright on the far hemisphere.
        Shader propShader = Shader.Find("Planet/PropLit");
        if (propShader != null)
        {
            _propMaterial = new Material(propShader) { name = "PropLit (runtime)" };
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = _propMaterial;
        }
        _child = go.transform;
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
