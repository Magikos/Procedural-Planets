using UnityEngine;

/// <summary>
/// MonoBehaviour wrapper that exposes a GameObject's transform as an
/// <see cref="IGrassInteractor"/>. The transform's world position is the bend center;
/// radius and strength are inspector-tunable scalars. Self-registers with
/// <see cref="GrassInteractorRegistry"/> on enable.
///
/// Use cases:
/// - Spawned by <c>GrassInteractorCommands.spawn</c> for the debug sphere demo.
/// - Manually attached to any GameObject you want to push grass (testing).
///
/// When the real character controller arrives, that controller implements
/// <see cref="IGrassInteractor"/> directly; this MonoBehaviour stays for debug.
/// </summary>
[DisallowMultipleComponent]
public sealed class DebugGrassInteractor : MonoBehaviour, IGrassInteractor
{
    [Tooltip("Radius of the bend zone in meters. Grass within this distance bends with smoothstepped falloff.")]
    [Range(0.1f, 50f)] public float radius = 4f;

    [Tooltip("Bend strength multiplier. 0 = no bend, 1 = full bend.")]
    [Range(0f, 2f)] public float strength = 1f;

    [Tooltip("Seconds for grass to recover after the interactor moves away.")]
    [Range(0.05f, 5f)] public float releaseSeconds = 0.5f;

    Material _debugMaterial;

    public Vector3 WorldPosition => transform.position;
    public float Radius => radius;
    public float Strength => strength;
    public float ReleaseSeconds => releaseSeconds;
    public bool IsActive => isActiveAndEnabled;

    public void OwnDebugMaterial(Material material) => _debugMaterial = material;

    void OnEnable() => GrassInteractorRegistry.Register(this);
    void OnDisable() => GrassInteractorRegistry.Unregister(this);

    void OnDestroy()
    {
        if (_debugMaterial != null)
            Destroy(_debugMaterial);
    }
}
