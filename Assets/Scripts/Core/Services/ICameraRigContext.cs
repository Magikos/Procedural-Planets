using UnityEngine;

public interface ICameraRigContext
{
    Transform CameraTransform { get; }
    Camera CameraComponent { get; }
    Transform TargetCenter { get; }
    bool SurfaceView { get; }
    Vector3 PlanetCenter { get; }
    float PlanetRadius { get; }
    float SeaLevelRadius { get; }
    float ElevationMin { get; }
    float ElevationMax { get; }
}
