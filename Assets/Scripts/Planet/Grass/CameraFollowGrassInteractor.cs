using UnityEngine;

/// <summary>
/// Per-LateUpdate, snaps this transform to a point ahead of the main camera on the
/// planet surface. Used to drive the debug grass interactor sphere so it drags
/// along beneath the freecam as you fly. Disable the component to leave the sphere
/// stationary.
/// </summary>
[DisallowMultipleComponent]
public sealed class CameraFollowGrassInteractor : MonoBehaviour
{
    [Tooltip("How far in front of the camera (along camera forward) to project the target.")]
    [Range(2f, 100f)] public float forwardDistance = 20f;

    [Tooltip("Offset above the terrain surface in meters. Small positive = sit on ground; 0 = embedded.")]
    public float surfaceOffset = 0.5f;

    [Tooltip("Meters per second while holding [ or ] to pull/push the interactor.")]
    public float distanceAdjustSpeed = 12f;

    Camera _camera;
    Planet _planet;
    IInputMapService _input;
    float _planetSearchCooldown;

    public void SetForwardDistance(float distance)
    {
        forwardDistance = Mathf.Clamp(distance, 2f, 100f);
    }

    public void AdjustForwardDistance(float delta)
    {
        SetForwardDistance(forwardDistance + delta);
    }

    void LateUpdate()
    {
        if (_input == null)
            ServiceLocator.TryGet(out _input);
        if (_input != null && _input.GameplayEnabled)
        {
            float axis = _input.GrassInteractorDistance.ReadValue<float>();
            if (Mathf.Abs(axis) > 0.001f)
            {
                float speed = Mathf.Max(distanceAdjustSpeed, 0f);
                if (_input.Sprint.IsPressed())
                    speed *= 3f;
                AdjustForwardDistance(axis * speed * Time.unscaledDeltaTime);
            }
        }

        if (_camera == null)
        {
            // Camera.main only returns a "MainCamera"-tagged camera. The freecam may not be
            // tagged, so fall back to any active camera. Done lazily because Camera.allCameras
            // is not free.
            _camera = Camera.main;
            if (_camera == null && Camera.allCamerasCount > 0)
            {
                var cams = new Camera[Camera.allCamerasCount];
                Camera.GetAllCameras(cams);
                for (int i = 0; i < cams.Length; i++)
                {
                    if (cams[i] != null && cams[i].isActiveAndEnabled)
                    {
                        _camera = cams[i];
                        break;
                    }
                }
            }
        }
        if (_camera == null) return;

        if (_planet == null && Time.unscaledTime >= _planetSearchCooldown)
        {
            _planet = Object.FindAnyObjectByType<Planet>(FindObjectsInactive.Exclude);
            if (_planet == null) _planetSearchCooldown = Time.unscaledTime + 1f;
        }

        Vector3 target = _camera.transform.position + _camera.transform.forward * forwardDistance;

        if (_planet != null)
        {
            Vector3 center = _planet.transform.position;
            Vector3 fromCenter = target - center;
            if (fromCenter.sqrMagnitude > 0.0001f)
            {
                Vector3 dir = fromCenter.normalized;
                float surfaceRadius;
                // Prefer actual terrain elevation. ShapeGenerator.EvaluateElevation +
                // GetScaledElevation give the world-space radius at this direction, accounting
                // for mountains/valleys. Falls back to LastSeaLevelRadius if shape isn't ready
                // (e.g. mid-regeneration).
                ShapeGenerator shape = _planet.ShapeGenerator;
                if (shape != null && _planet.PlanetSettingsAsset != null)
                {
                    float elevation = shape.EvaluateElevation(dir);
                    surfaceRadius = shape.GetScaledElevation(elevation);
                }
                else
                {
                    surfaceRadius = _planet.LastSeaLevelRadius;
                }
                target = center + dir * (surfaceRadius + surfaceOffset);
            }
        }

        transform.position = target;
    }
}
