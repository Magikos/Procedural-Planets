using UnityEngine;

/// <summary>
/// The thin player/planet HOST for the walking-character MVP. It is added to the bootstrap GameObject at
/// boot (inert until spawned) and owns only the Unity/player-specific concerns: input, camera follow/suspend,
/// grass, the console command, and a separate movable child GameObject. All surface math lives in the
/// actor-agnostic <see cref="SurfaceCharacterController"/>; this host resolves the planet-specific providers,
/// forwards plain intent into the driver, and applies the returned pose to the child. It never moves its own
/// (bootstrap) transform.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlanetCharacterController : MonoBehaviour, IGrassInteractor
{
    const float FootOffset = 1f;        // capsule primitive half-height (origin is its center)
    const float MoveSpeed = 6f;
    const float CameraDistance = 6f;
    const float CameraHeight = 3f;
    const float CameraLookAtHeight = 1.2f;
    const float GrassBendRadius = 2.2f;
    const float GrassBendStrength = 0.8f;
    const float GrassReleaseSeconds = 0.6f;

    IPlanetSurfaceSampler _sampler;
    IInputMapService _input;
    ICameraRigContext _cameraRig;
    IFreeCameraService _freeCam;

    SurfaceCharacterController _driver;
    Transform _child;

    Vector3 _center;
    float _radius;
    bool _hasPlanet;
    bool _spawned;

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
        SuspendFreeCamera(false);
    }

    void OnDestroy()
    {
        if (_child != null)
            Destroy(_child.gameObject);
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _center = evt.PlanetCenter;
        _radius = evt.PlanetRadius;
        _hasPlanet = _radius > 0f;
        if (_sampler == null)
            ServiceLocator.TryGet(out _sampler);

        // Regeneration: rebuild the providers against the new center and re-seed so stale state never
        // carries into a new provider frame (C24).
        if (_spawned && _hasPlanet && TrySeedPose(out CharacterPose seed))
            RebuildDriver(seed);
    }

    void Update()
    {
        if (!_spawned || _driver == null)
            return;

        Vector2 move = _input != null ? _input.Move.ReadValue<Vector2>() : Vector2.zero;
        Vector3 camForward = _cameraRig != null && _cameraRig.CameraTransform != null
            ? _cameraRig.CameraTransform.forward
            : _child.forward;

        CharacterPose pose = _driver.Tick(move, camForward, MoveSpeed, Time.deltaTime);
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
        Vector3 back = -_child.forward;
        cam.position = _child.position + up * CameraHeight + back * CameraDistance;
        Vector3 lookAt = _child.position + up * CameraLookAtHeight;
        cam.rotation = Quaternion.LookRotation((lookAt - cam.position).normalized, up);
    }

    // --- Spawn / despawn (driven by the static CharacterCommands) ---------

    public string Spawn()
    {
        if (!EnsurePlanet(out string err))
            return err;
        if (!TrySeedPose(out CharacterPose seed))
            return "surface sample failed at the camera — aim at the planet and retry";

        EnsureChild();
        RebuildDriver(seed);
        _child.gameObject.SetActive(true);
        _spawned = true;
        SuspendFreeCamera(true);
        return "character spawned; WASD to walk, `character.despawn` to return to free-fly";
    }

    public string Despawn()
    {
        if (!_spawned)
            return "character not spawned";
        _spawned = false;
        if (_child != null)
            _child.gameObject.SetActive(false);
        SuspendFreeCamera(false);
        return "character despawned; free-fly restored";
    }

    // --- Composition / lifecycle -----------------------------------------

    void RebuildDriver(CharacterPose seed)
    {
        // Providers capture the planet center at construction, so a fresh driver is the clean way to adopt a
        // (possibly new) center on spawn/respawn/regeneration — the seed guarantees a known-good start.
        var gravity = new RadialGravityProvider(_center);
        var grounding = new PlanetSurfaceGrounding(_sampler, _center);
        _driver = new SurfaceCharacterController(gravity, grounding, FootOffset, seed);
        _child.SetPositionAndRotation(seed.Position, Quaternion.LookRotation(seed.Forward, seed.Up));
    }

    bool EnsurePlanet(out string err)
    {
        err = null;
        if (_sampler == null)
            ServiceLocator.TryGet(out _sampler);
        if (_sampler == null)
        {
            err = "no surface sampler — generate a planet first";
            return false;
        }
        if (!_hasPlanet && ResolveCameraRig() != null && _cameraRig.PlanetRadius > 0f)
        {
            _center = _cameraRig.PlanetCenter;
            _radius = _cameraRig.PlanetRadius;
            _hasPlanet = true;
        }
        if (!_hasPlanet)
        {
            err = "planet not generated yet";
            return false;
        }
        ResolveInput();
        ResolveCameraRig();
        return true;
    }

    bool TrySeedPose(out CharacterPose seed)
    {
        seed = default;
        Transform camT = ResolveCameraRig()?.CameraTransform;
        Vector3 origin = camT != null ? camT.position : _center + Vector3.up * (_radius + 10f);
        Vector3 dir = origin - _center;
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up;

        if (!_sampler.TryGetSurfaceRadius(dir, out float r))
            return false;

        Vector3 pos = _center + dir * (r + FootOffset);
        Vector3 fwd = camT != null && CharacterMath.TryProjectOntoTangent(camT.forward, dir, out Vector3 f)
            ? f
            : CharacterMath.ArbitraryTangent(dir);
        seed = new CharacterPose(pos, dir, fwd);
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
        _child = go.transform;
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
