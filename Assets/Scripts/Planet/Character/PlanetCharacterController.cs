using UnityEngine;

/// <summary>
/// The thin player/planet HOST for the walking-character MVP. It owns only the Unity/player concerns: input,
/// a mouse-look third-person camera, grass, and a separate movable child GameObject. All surface math lives in
/// the actor-agnostic <see cref="SurfaceCharacterController"/>; this host resolves the planet-specific
/// providers, feeds look-relative intent into the driver, and applies the returned pose to the child. It never
/// moves its own transform.
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

    IPlanet _planet;
    IPlanetSurfaceSampler _sampler;
    IPlanetSurfaceRaycaster _raycaster;
    IInputMapService _input;
    ICameraRigContext _cameraRig;
    IFreeCameraService _freeCam;
    ICameraLookBlocker _lookBlocker;

    SurfaceCharacterController _driver;
    Transform _child;
    Material _propMaterial;

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

        if (_spawned && _hasPlanet && TrySeedPose(out CharacterPose seed))
            RebuildDriver(seed);
    }

    void Update()
    {
        if (!_spawned || _driver == null || _child == null)
            return;

        Vector3 up = _driver.Pose.Up;

        // Look only while HOLDING right-mouse (like the free camera) — the cursor is captured only during the
        // hold and released the instant you let go, so it can never trap the mouse (e.g. to open the console).
        bool looking = _input != null && _input.LookHold.IsPressed() && _input.GameplayEnabled && !LookBlocked();
        SetCursorLocked(looking);
        if (looking)
        {
            Vector2 look = _input.Look.ReadValue<Vector2>();
            if (look.sqrMagnitude > 0.0001f)
            {
                _forward = Quaternion.AngleAxis(look.x * LookSensitivity, up) * _forward;
                _pitch = Mathf.Clamp(_pitch - look.y * LookSensitivity, MinPitch, MaxPitch);
            }
        }
        if (CharacterMath.TryProjectOntoTangent(_forward, up, out Vector3 fp))
            _forward = fp;

        Vector2 move = _input != null ? _input.Move.ReadValue<Vector2>() : Vector2.zero;
        float speed = WalkSpeed;
        if (_input != null && _input.Sprint.IsPressed()) speed *= SprintMult;
        if (_input != null && _input.Crouch.IsPressed()) speed *= CrouchMult;
        bool jump = _input != null && _input.Jump.WasPressedThisFrame();

        CharacterPose pose = _driver.Tick(move, _forward, speed, Time.deltaTime, jump);
        _forward = pose.Forward;
        _child.SetPositionAndRotation(pose.Position, Quaternion.LookRotation(pose.Forward, pose.Up));
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
        var samplerGround = new PlanetSurfaceGrounding(_sampler, _center, SeaLevel());
        IGroundingProvider grounding = ResolveRaycaster() != null
            ? new PlanetRaycastGrounding(_raycaster, _center, SeaLevel(), samplerGround)
            : samplerGround;
        _driver = new SurfaceCharacterController(gravity, grounding, FootOffset, seed);
        _child.SetPositionAndRotation(seed.Position, Quaternion.LookRotation(seed.Forward, seed.Up));
    }

    float SeaLevel() => _planet != null ? _planet.LastSeaLevelRadius : 0f;

    bool EnsurePlanet(out string err)
    {
        err = null;
        if (_sampler == null) ServiceLocator.TryGet(out _sampler);
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

        // Never seed below sea level — over ocean, stand on the water surface, not the sea floor.
        float seaLevel = SeaLevel();
        if (seaLevel > 0f && Vector3.Dot(groundPoint - _center, up) < seaLevel)
            groundPoint = _center + up * seaLevel;

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

    bool LookBlocked()
    {
        if (!ServiceLocator.IsAlive(_lookBlocker))
            ServiceLocator.TryGet(out _lookBlocker);
        return _lookBlocker != null && _lookBlocker.BlocksCameraLook;
    }

    void SuspendFreeCamera(bool suspended)
    {
        if (ResolveFreeCam() != null)
            _freeCam.InputSuspended = suspended;
    }

    void ResolveInput()
    {
        if (_input == null)
            ServiceLocator.TryGet(out _input);
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
}
