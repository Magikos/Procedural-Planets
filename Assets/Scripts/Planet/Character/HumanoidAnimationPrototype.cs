using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Review-scene controls and environment for the shared humanoid actor.</summary>
public sealed class HumanoidAnimationPrototype : HumanoidActorController
{
    public const float WaterLevel = .7f;
    public static float Height(float x, float z) => .12f * Mathf.Sin(x * .9f) * Mathf.Cos(z * .8f)
        - 3.5f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 5f, x));
    void Update() => Step(Time.deltaTime, true);
    public void Step(float dt, bool readInput = false) => base.Step(dt, readInput ? ReadInput() : default);
    protected override void OnActorInitialized()
    {
        _camera = Camera.main;
        _flyCamera = _camera != null ? _camera.GetComponent<WorkbenchFlyCamera>() : null;
        _flyWasEnabled = _flyCamera != null && _flyCamera.enabled;
        SetCameraMode();
    }

    ActorIntent ReadInput()
    {
        var keyboard = Keyboard.current; var mouse = Mouse.current;
        if (keyboard == null || !Application.isFocused) { SetCursorLocked(false); return default; }
        if (keyboard.tabKey.wasPressedThisFrame) FollowActor = !FollowActor;
        SetCameraMode();
        bool held = mouse != null && mouse.rightButton.isPressed;
        if (!held) _lookReleased = false;
        if (keyboard.escapeKey.wasPressedThisFrame) _lookReleased = true;
        bool looking = FollowActor && held && !_lookReleased;
        SetCursorLocked(looking);
        if (!FollowActor) return default;
        Vector2 move = new((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
            (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
        ActorButtons buttons = ActorButtons.None;
        if (keyboard.leftShiftKey.isPressed) buttons |= ActorButtons.Sprint;
        if (keyboard.leftCtrlKey.isPressed) buttons |= ActorButtons.Crouch;
        if (keyboard.spaceKey.isPressed) buttons |= ActorButtons.SwimUp;
        if (keyboard.spaceKey.wasPressedThisFrame) buttons |= ActorButtons.Jump;
        if (keyboard.eKey.wasPressedThisFrame) buttons |= ActorButtons.Interact;
        if (keyboard.qKey.wasPressedThisFrame) buttons |= ActorButtons.Dodge;
        if (keyboard.escapeKey.wasPressedThisFrame) buttons |= ActorButtons.Cancel;
        if (keyboard.zKey.wasPressedThisFrame) buttons |= ActorButtons.ToggleCrawl;
        return new ActorIntent(move, looking ? mouse.delta.ReadValue() : Vector2.zero, buttons, 0);
    }

    void LateUpdate()
    {
        SetCameraMode();
        if (FollowActor && _actor != null)
            ThirdPersonCamera.Follow(_camera, _motor.Pose, _view.CameraFocusHeight, CameraDistance, Time.deltaTime);
    }

    public override bool TryGetGravity(Vector3 position, out Vector3 acceleration)
    { acceleration = -transform.up * 9.81f; return true; }

    public override bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult result)
    {
        Vector3 local = transform.InverseTransformPoint(position);
        float height = Height(local.x, local.z);
        if (Physics.Raycast(position + transform.up * .4f, -transform.up, out var hit, 20f, 1 << 0, QueryTriggerInteraction.Ignore))
        { result = new GroundResult(hit.point + transform.up * offset, hit.normal); return true; }
        float dx = (Height(local.x + .01f, local.z) - Height(local.x - .01f, local.z)) / .02f;
        float dz = (Height(local.x, local.z + .01f) - Height(local.x, local.z - .01f)) / .02f;
        Vector3 normal = transform.TransformDirection(new Vector3(-dx, 1f, -dz).normalized);
        result = new GroundResult(transform.TransformPoint(new Vector3(local.x, height, local.z)) + transform.up * offset, normal);
        return true;
    }

    public override bool TryGetDepth(Vector3 position, out float signedDepth, out float bodyDepth)
    {
        if (!EnableWater) { signedDepth = bodyDepth = 0f; return false; }
        var local = transform.InverseTransformPoint(position);
        signedDepth = WaterLevel - local.y; bodyDepth = WaterLevel - Height(local.x, local.z);
        return local.x >= 2f && local.x <= 12f && Mathf.Abs(local.z) <= 12f;
    }

    public void EnterWater()
    {
        ResetActor();
        FollowActor = true;
        var point = transform.TransformPoint(new Vector3(6f, WaterLevel - 1.25f, 0f));
        _motor.ResetPose(new CharacterPose(point, transform.up, transform.forward));
        _actor.transform.SetPositionAndRotation(point, transform.rotation);
        ThirdPersonCamera.Reset(_motor.Pose);
    }

    public void VisitStation(int index)
    {
        if (Stations == null || index < 0 || index >= Stations.Length || Stations[index] == null) return;
        ResetActor(); FollowActor = true;
        var station = Stations[index];
        var pose = new CharacterPose(station.position, transform.up, station.forward);
        _motor.ResetPose(pose); Collision.TryStance(ActorStance.Standing, pose);
        ThirdPersonCamera.Reset(pose);
        _actor.transform.SetPositionAndRotation(pose.Position, station.rotation);
    }

    void OnGUI()
    {
        if (!ShowControls) return;
        Matrix4x4 previous = GUI.matrix;
        float scale = Mathf.Max(1f, Screen.width / 1280f);
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        GUILayout.BeginArea(new Rect(12, 12, 340, Mathf.Min(700f, Screen.height / scale - 24f)), GUI.skin.box);
        _scroll = GUILayout.BeginScrollView(_scroll);
        GUILayout.Label("Humanoid review");
        GUILayout.Label("W/S: forward/back | A/D: strafe | Shift: run");
        if (DodgeMotions.Length >= 3)
            GUILayout.Label("Q: dodge back | A/D + Q: dodge sideways | " + (Dodge?.Status ?? "Ready"));
        if (DodgeMotions.Length > 3) GUILayout.Label("W + Q: forward roll");
        GUILayout.Label("Hold RMB: look | Tab: actor/free camera | Esc: release mouse");
        GUILayout.Label("Ctrl: crouch/dive/drop | Z: crawl | Space: jump/climb | Hanging: A/D move, W climbs, S drops");
        GUILayout.Label("Ctrl + Space near an edge: lower to hang | RMB: look around");
        Crouch = GUILayout.Toggle(Crouch, "Crouch"); Crawl = GUILayout.Toggle(Crawl, "Crawl");
        if (GUILayout.Button("Jump / traverse / climb")) RequestJump = true;
        if (Collision != null) GUILayout.Label($"{Collision.Stance} | {Traversal.Kind}");
        if (Beam?.Active == true) GUILayout.Label($"Beam: {Beam.PhaseName}. W/S travel, A/D turn around, E leave.");
        WalkLoop = GUILayout.Toggle(WalkLoop, "Walk loop"); Running = GUILayout.Toggle(Running, "Run");
        Feet = GUILayout.Toggle(Feet, "Foot IK"); Gaze = GUILayout.Toggle(Gaze, "Look at target");
        Reach = GUILayout.Toggle(Reach, "Reach right hand to target");
        if (Reach && _view != null && _view.Pose.TryGetInteractionContact("RightHand", out _, out bool contactReached))
            GUILayout.Label(contactReached ? "Knob contact" : "Target outside current hand pose limits");
        if (GUILayout.Button("Use hinged panel")) UsePanel();
        if (GUILayout.Button("Cancel interaction")) _reachMotion.Cancel();
        if (GUILayout.Button("Reset actor")) ResetActor();
        if (GUILayout.Button("Enter water")) EnterWater();
        FollowActor = GUILayout.Toggle(FollowActor, "Third-person camera (off: RMB free flight)");
        Dive = GUILayout.Toggle(Dive, "Dive (Ctrl)"); SwimUp = GUILayout.Toggle(SwimUp, "Ascend (Space)");
        if (_motor != null) GUILayout.Label($"{(_motor.Swimming ? _motor.Diving ? "Underwater" : "Surface swim" : "Land")} | Root depth {_motor.WaterDepth:F2} m");
        if (_view != null) GUILayout.Label($"Speed {_view.Speed:F2} m/s | Planted feet {_view.Pose.PlantedFeet}");
        if (_view != null) GUILayout.Label($"Motion {_view.Airborne.Phase} | Performance {_view.PerformanceId ?? "legacy"}");
        if (Stations != null) for (int i = 0; i < Stations.Length; i++)
            if (Stations[i] != null && GUILayout.Button("Visit " + Stations[i].name)) VisitStation(i);
        if (Traversal?.Rejection != null) GUILayout.Label(Traversal.Rejection);
        if (Ropes.Length > 0) GUILayout.Label("Rope: E mount/release, W/S climb, release movement to stop; Space/Ctrl lets go.");
        if (Ladders.Length > 0)
        {
            GUILayout.Label("Ladder: E mount/release, W/S climb, Shift+W skip rungs, Shift+S slide, release movement to stop, Escape release.");
            for (int i = 0; i < Ladders.Length; i++)
            {
                if (Ladders[i] == null) continue;
                if (GUILayout.Button("Visit " + Ladders[i].name + " bottom")) VisitLadderEntrance(i, false);
                if (GUILayout.Button("Visit " + Ladders[i].name + " top")) VisitLadderEntrance(i, true);
            }
            if (LadderStatus != null) GUILayout.Label(LadderStatus);
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        GUI.matrix = previous;
    }

    public void VisitLadderEntrance(int index, bool top)
    {
        if (index < 0 || index >= Ladders.Length || Ladders[index] == null)
            throw new System.ArgumentOutOfRangeException(nameof(index));
        ResetActor();
        var ladder = Ladders[index];
        // Explicit review teleport; normal ladder use approaches through the motor.
        var pose = new CharacterPose(top ? ladder.TopApproach : ladder.BottomApproach - ladder.transform.forward * .5f,
            ladder.transform.up, top ? ladder.TopApproachForward : ladder.transform.forward);
        _motor.ResetPose(pose); _actor.transform.SetPositionAndRotation(pose.Position, Quaternion.LookRotation(pose.Forward, pose.Up));
        ThirdPersonCamera.Reset(pose);
    }

}
