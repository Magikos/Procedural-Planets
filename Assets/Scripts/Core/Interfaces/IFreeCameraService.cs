using UnityEngine;

public interface IFreeCameraService
{
    void FrameWorldTarget(Vector3 worldPosition);

    /// <summary>
    /// Frame a single object close enough to judge it. <see cref="FrameWorldTarget"/> is planet-scale — it
    /// stands off by 10° of arc or drops to orbit — which puts a metre-sized prop hundreds of metres away.
    /// <paramref name="boundsRadius"/> is the object's world-space extent, so the standoff and the resulting
    /// fly speed both scale to the thing being looked at rather than to the planet.
    /// </summary>
    void FrameCloseUp(Vector3 worldPosition, float boundsRadius);

    /// <summary>
    /// When true the free camera stops consuming input (look/move/shortcuts) so another owner — e.g. the
    /// character controller's third-person follow — can drive the camera without fighting it. The event
    /// subscription and <see cref="ICameraRigContext"/> registration stay intact; this is not the same as
    /// disabling the component.
    /// </summary>
    bool InputSuspended { get; set; }
}
