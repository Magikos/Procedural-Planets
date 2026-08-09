using UnityEngine;

public interface IFreeCameraService
{
    void FrameWorldTarget(Vector3 worldPosition);

    /// <summary>
    /// When true the free camera stops consuming input (look/move/shortcuts) so another owner — e.g. the
    /// character controller's third-person follow — can drive the camera without fighting it. The event
    /// subscription and <see cref="ICameraRigContext"/> registration stay intact; this is not the same as
    /// disabling the component.
    /// </summary>
    bool InputSuspended { get; set; }
}
