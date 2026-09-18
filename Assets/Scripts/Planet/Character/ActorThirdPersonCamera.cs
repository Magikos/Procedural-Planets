using System;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>Gravity-relative look and collision-safe camera placement, ticked by the local actor host.</summary>
public sealed class ActorThirdPersonCamera : IDisposable
{
    readonly int _mask;
    Vector3 _up = Vector3.up;
    float _pitch = 12f;
    GameObject _rig;
    Transform _target;
    CinemachineBrain _brain;
    CinemachineCamera _virtualCamera;
    CinemachineThirdPersonFollow _follow;
    bool _ownsBrain, _brainWasEnabled;
    Transform _previousWorldUp;
    CinemachineBrain.UpdateMethods _previousUpdate;
    int _frame;
    public Vector3 Forward { get; private set; } = Vector3.forward;
    public float Pitch => _pitch;

    public ActorThirdPersonCamera(int collisionMask) => _mask = collisionMask;

    public void Reset(CharacterPose pose)
    {
        if (!pose.IsFinite) throw new ArgumentException("Camera requires a finite actor pose.", nameof(pose));
        _up = pose.Up.normalized;
        AlignFacing(pose);
        _pitch = 12f;
        if (_virtualCamera != null) _virtualCamera.PreviousStateIsValid = false;
    }

    public void AlignFacing(CharacterPose pose)
    {
        if (!pose.IsFinite) throw new ArgumentException("Camera requires a finite actor pose.", nameof(pose));
        _up = pose.Up.normalized;
        Forward = CharacterMath.TryProjectOntoTangent(pose.Forward, _up, out var forward)
            ? forward : CharacterMath.ArbitraryTangent(_up);
    }

    public void Look(Vector2 delta, Vector3 up, float sensitivity)
    {
        if (!float.IsFinite(delta.x) || !float.IsFinite(delta.y) || !CharacterMath.IsFinite(up) ||
            up.sqrMagnitude < .001f || !float.IsFinite(sensitivity) || sensitivity < 0f)
            throw new ArgumentOutOfRangeException(nameof(delta));
        up.Normalize();
        Forward = Quaternion.FromToRotation(_up, up) * Forward;
        Forward = Quaternion.AngleAxis(delta.x * sensitivity, up) * Forward;
        Forward = CharacterMath.TryProjectOntoTangent(Forward, up, out var forward)
            ? forward : CharacterMath.ArbitraryTangent(up);
        _up = up;
        _pitch = Mathf.Clamp(_pitch - delta.y * sensitivity, -60f, 75f);
    }

    public void Follow(Camera camera, CharacterPose pose, float focusHeight, float distance, float dt)
    {
        if (camera == null) return;
        if (!pose.IsFinite || !float.IsFinite(focusHeight) || focusHeight < 0f ||
            !float.IsFinite(distance) || distance <= 0f || !float.IsFinite(dt) || dt < 0f)
            throw new ArgumentOutOfRangeException(nameof(distance));
        if (_rig == null) Initialize(camera);
        SetActive(true);
        Vector3 view = Quaternion.AngleAxis(_pitch, Vector3.Cross(pose.Up, Forward)) * Forward;
        Vector3 focus = pose.Position + pose.Up * focusHeight;
        // Enclose the near plane, including its corners, so a wall cannot clip through the image.
        float halfHeight = camera.nearClipPlane * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
        float radius = Mathf.Max(.12f, new Vector3(halfHeight * camera.aspect, halfHeight, camera.nearClipPlane).magnitude);
        _rig.transform.SetPositionAndRotation(pose.Position, Quaternion.LookRotation(pose.Forward, pose.Up));
        _target.SetPositionAndRotation(focus, Quaternion.LookRotation(view, pose.Up));
        _follow.CameraDistance = distance;
        _follow.AvoidObstacles.CameraRadius = radius;
        _brain.ManualUpdate(++_frame, dt);
    }

    void Initialize(Camera camera)
    {
        _rig = new GameObject("Actor camera rig");
        _target = new GameObject("Look target").transform; _target.SetParent(_rig.transform, false);
        var cameraObject = new GameObject("Third person Cinemachine camera"); cameraObject.transform.SetParent(_rig.transform, false);
        _virtualCamera = cameraObject.AddComponent<CinemachineCamera>();
        _virtualCamera.Follow = _target;
        _virtualCamera.Lens = LensSettings.FromCamera(camera);
        _follow = cameraObject.AddComponent<CinemachineThirdPersonFollow>();
        _follow.ShoulderOffset = Vector3.zero; _follow.VerticalArmLength = 0f;
        _follow.Damping = new Vector3(.1f, .25f, .15f);
        _follow.AvoidObstacles.Enabled = true;
        _follow.AvoidObstacles.CollisionFilter = _mask;
        _follow.AvoidObstacles.IgnoreTag = string.Empty;
        _follow.AvoidObstacles.DampingIntoCollision = 0f;
        _follow.AvoidObstacles.DampingFromCollision = .4f;
        _brain = camera.GetComponent<CinemachineBrain>();
        _ownsBrain = _brain == null;
        if (_ownsBrain) _brain = camera.gameObject.AddComponent<CinemachineBrain>();
        _brainWasEnabled = _brain.enabled;
        _previousWorldUp = _brain.WorldUpOverride; _previousUpdate = _brain.UpdateMethod;
        _brain.WorldUpOverride = _rig.transform;
        _brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
        _frame = Time.frameCount;
    }

    public void SetActive(bool active)
    {
        if (_rig == null) return;
        _virtualCamera.enabled = active;
        _brain.enabled = active;
    }

    public void Dispose()
    {
        if (_brain != null)
        {
            _brain.WorldUpOverride = _previousWorldUp; _brain.UpdateMethod = _previousUpdate;
            _brain.enabled = _brainWasEnabled;
            if (_ownsBrain) { _brain.enabled = false; Destroy(_brain); }
        }
        if (_rig != null) { _rig.SetActive(false); Destroy(_rig); }
        _rig = null; _brain = null;
    }

    static void Destroy(UnityEngine.Object value)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
}
