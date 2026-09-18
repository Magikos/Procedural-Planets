using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Bow training fixture. Shared playback and physical gear own poses and item custody.</summary>
public sealed class BowInteractionReview : MonoBehaviour
{
    public HumanoidAnimationPrototype Actor;
    public ActorInteractionDefinition Definition;
    public PhysicalEquipmentItem Bow, Arrow, Sword;
    public Transform Quiver, Target, TipA, TipB, LimbA, LimbB;
    public LineRenderer String;
    public Vector3 QuiverPosition, QuiverEuler;
    [Range(0, 1)] public float BowContact = .45f, ArrowContact = .475f;
    public float ArrowSpeed = 18;
    public AnimationCurve ArrowGripAlongShaft = AnimationCurve.EaseInOut(0, .22f, 1, 0);
    public ActorBow Action { get; private set; } = new();
    public float Progress { get; private set; }
    public string Status { get; private set; } = "Stowed";
    public float BowGripError { get; private set; }
    public float ArrowGripError { get; private set; }
    bool _bound;
    Vector3 _releaseNock;
    ActorBow.Motion _previous;
    float _speed = 1, _draw, _idle;
    Quaternion _limbA, _limbB;
    Transform _chest;
    ProceduralPoseRig _pose;
    EntityId _owner, _bowId, _arrowId, _swordId;

    void OnEnable()
    {
        var ids = new EntityIdAllocator(); _owner = ids.Next(); _bowId = ids.Next(); _arrowId = ids.Next(); _swordId = ids.Next();
        _limbA = LimbA.localRotation; _limbB = LimbB.localRotation;
        Actor.PrepareInteraction += Tick; Actor.PresentInteraction += Present;
    }

    bool Held(PhysicalEquipmentItem item) => item.State?.Location == EquipmentLocation.Hand;
    bool Stowed(PhysicalEquipmentItem item) => item.State?.Location == EquipmentLocation.Stowed;
    public bool Equip() => _bound && Action.Equip(Stowed(Bow));
    public bool Retrieve() => _bound && Action.Retrieve(Stowed(Arrow));
    public bool Fire()
    {
        if (!_bound || !Held(Bow) || !Held(Arrow) || !Action.Release()) return false;
        // Release the currently displayed arrow before the hand enters its recoil pose.
        var direction = Arrow.transform.up;
        _releaseNock = Arrow.transform.position;
        Arrow.Drop();
        var body = Arrow.GetComponent<Rigidbody>(); body.linearVelocity = direction * ArrowSpeed; body.angularVelocity = Vector3.zero;
        Status = "Arrow released"; return true;
    }
    public bool Stow() => _bound && Action.Stow();
    public void Cancel()
    {
        var before = Action.State;
        Action.Cancel(Held(Bow), Held(Arrow));
        if (before == ActorBow.Motion.Retrieving && Action.State == ActorBow.Motion.ReturningArrow)
        {
            Progress = 1 - Progress; _previous = Action.State; _speed = 0;
        }
    }

    public void ResetReview()
    {
        Action = new ActorBow(); _bound = false; _draw = _idle = Progress = 0; _previous = Action.State; _speed = 1;
        LimbA.localRotation = _limbA; LimbB.localRotation = _limbB;
        Actor.InteractionMovementLocked = false; Actor.InteractionPosition = Actor.InteractionForward = null;
        Actor.ResetActor(); Actor.VisitStation(0); Status = "Stowed";
    }

    void Tick(float dt)
    {
        if (Actor.View == null) return;
        if (!_bound)
        {
            var animator = Actor.Actor.GetComponentInChildren<Animator>();
            Bow.Bind(_bowId, _owner, animator, Actor); Arrow.Bind(_arrowId, _owner, animator, Actor); Sword.Bind(_swordId, _owner, animator, Actor);
            _chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            Actor.InteractionPosition = Actor.Actor.position; Actor.InteractionForward = Actor.Actor.forward;
            Actor.View.BeginInteraction(Definition.Action); _bound = true;
        }
        if (_pose != Actor.View.Pose)
        {
            if (_pose != null) _pose.BeforeCorrections -= ContactPose;
            _pose = Actor.View.Pose; _pose.BeforeCorrections += ContactPose;
        }
        if (_previous != Action.State)
        {
            // Reverse the same draw cursor when lowering; do not jump to the fully drawn endpoint.
            Progress = Action.State == ActorBow.Motion.Lowering ? Progress : 0;
            if (Action.State == ActorBow.Motion.Lowering && _previous == ActorBow.Motion.Holding) Progress = 1;
            _previous = Action.State; _speed = Action.State == ActorBow.Motion.Lowering ? 0 : 1;
            Status = Action.State.ToString();
        }
        _idle += dt;
        var phase = System.Array.Find(Definition.Phases, p => p.Animation.Name == Action.State.ToString());
        bool idle = Action.State is ActorBow.Motion.Stowed or ActorBow.Motion.Ready or ActorBow.Motion.Holding;
        _speed = Mathf.MoveTowards(_speed, Action.State == ActorBow.Motion.Lowering ? -1 : Action.Direction, dt * 12);
        if (!idle) Progress = Mathf.Clamp01(Progress + _speed * dt / phase.Seconds);
        float sample = idle ? _idle / phase.Seconds : Progress;
        if (Action.State is ActorBow.Motion.ReturningArrow or ActorBow.Motion.Stowing) sample = 1 - sample;
        Actor.View.SetInteractionPhase(Action.State.ToString(), sample);
        float shaftGrip = Action.State is ActorBow.Motion.Drawing or ActorBow.Motion.Lowering ? ArrowGripAlongShaft.Evaluate(Progress)
            : Action.State is ActorBow.Motion.Holding or ActorBow.Motion.Releasing ? 0 : ArrowGripAlongShaft.Evaluate(0);
        Arrow.Grip.Primary.localPosition = Vector3.up * shaftGrip;
        Actor.InteractionMovementLocked = true;
        Actor.LookTarget = Action.State is ActorBow.Motion.Drawing or ActorBow.Motion.Holding or ActorBow.Motion.Releasing ? Target : null;
    }

    void ContactPose(float dt)
    {
        Contact(Bow, Action.State is ActorBow.Motion.Equipping or ActorBow.Motion.Stowing,
            Action.State == ActorBow.Motion.Stowing ? 1 - Progress : Progress, BowContact);
        Contact(Arrow, Action.State is ActorBow.Motion.Retrieving or ActorBow.Motion.ReturningArrow,
            Action.State == ActorBow.Motion.ReturningArrow ? 1 - Progress : Progress, ArrowContact);
    }

    void Contact(PhysicalEquipmentItem item, bool reaching, float time, float contact)
    {
        if (reaching && Stowed(item) && _pose.TryGetInteractionContactPose(item.Grip.PrimaryId, out var palm))
        {
            var point = item.Grip.Primary.position;
            if (Vector3.Distance(point, palm.position) < .12f && Mathf.Abs(time - contact) < .15f)
            {
                float weight = 1 - Mathf.Abs(time - contact) / .15f;
                _pose.SetInteractionTarget(item.Grip.PrimaryId, new InteractionPoseTarget(point, null, weight,
                    useContact: true, followAuthoredMotion: true, preserveAuthoredContactJoints: true)); return;
            }
        }
        _pose.SetInteractionTarget(item.Grip.PrimaryId, null);
    }

    void Present(float dt)
    {
        if (!_bound) return;
        _pose.TryGetInteractionContactPose("LeftHand", out var left);
        _pose.TryGetInteractionContactPose("RightHand", out var right);
        Bow.Grip.TryFit(left.position, left.rotation, out var bowPose);
        Arrow.Grip.TryFit(right.position, right.rotation, out var arrowPose);
        var state = Action.State;
        if (state == ActorBow.Motion.Equipping && Action.Direction > 0 && Progress >= BowContact && Stowed(Bow))
        {
            if (!Bow.Contact(EquipmentLocation.Hand, bowPose)) { Cancel(); Status = "Bow contact missed; returning"; }
        }
        if (state == ActorBow.Motion.Stowing && Action.Direction > 0 && Progress >= 1 - BowContact && Held(Bow))
        {
            if (!Bow.Contact(EquipmentLocation.Stowed, Bow.StoragePose())) { Cancel(); Status = "Storage contact missed; retaining bow"; }
        }
        if (state == ActorBow.Motion.Retrieving && Action.Direction > 0 && Progress >= ArrowContact && Stowed(Arrow))
        {
            if (!Arrow.Contact(EquipmentLocation.Hand, arrowPose)) { Cancel(); Status = "Arrow contact missed; returning"; }
        }
        if (state == ActorBow.Motion.ReturningArrow && Progress >= 1 - ArrowContact && Held(Arrow))
        {
            if (!Arrow.Contact(EquipmentLocation.Stowed, Arrow.StoragePose()))
            {
                Action.ReturnFailed(); Progress = 1 - Progress; _previous = Action.State; _speed = 0;
                Status = "Quiver contact missed; retaining arrow";
            }
        }
        Bow.Present(bowPose, dt); Arrow.Present(arrowPose, dt);
        Sword.Present(default, dt);
        Quiver.SetPositionAndRotation(_chest.TransformPoint(QuiverPosition), _chest.rotation * Quaternion.Euler(QuiverEuler));
        BowGripError = Vector3.Distance(left.position, Bow.Grip.Primary.position);
        ArrowGripError = Vector3.Distance(right.position, Arrow.Grip.Primary.position);
        float desiredDraw = Held(Arrow) && state is ActorBow.Motion.Drawing or ActorBow.Motion.Holding or ActorBow.Motion.Lowering ? 1 : 0;
        _draw = Mathf.MoveTowards(_draw, desiredDraw, dt / (state == ActorBow.Motion.Releasing ? .055f : .15f));
        Vector3 rest = (TipA.position + TipB.position) * .5f;
        Vector3 nock = Vector3.Lerp(rest, state == ActorBow.Motion.Releasing ? _releaseNock : Arrow.transform.position, _draw);
        float bend = Mathf.Clamp01(Vector3.Distance(rest, nock) / .65f) * 18;
        LimbA.localRotation = _limbA * Quaternion.Euler(0, 0, -bend);
        LimbB.localRotation = _limbB * Quaternion.Euler(0, 0, -bend);
        String.positionCount = 3; String.SetPosition(0, TipA.position); String.SetPosition(1, nock); String.SetPosition(2, TipB.position);
        bool endpoint = state == ActorBow.Motion.Lowering || Action.Direction < 0 ? Progress <= 0 : Progress >= 1;
        if (endpoint) Action.CompletePhase(Held(Bow), Held(Arrow), Stowed(Arrow));
    }

    void Update()
    {
        var key = Keyboard.current; if (key == null) return;
        if (key.digit1Key.wasPressedThisFrame) { if (Action.State == ActorBow.Motion.Stowed) Equip(); else Stow(); }
        if (key.eKey.wasPressedThisFrame) Retrieve();
        if (key.spaceKey.wasPressedThisFrame) Fire();
        if (key.escapeKey.wasPressedThisFrame) Cancel();
    }

    void OnDisable()
    {
        Actor.PrepareInteraction -= Tick; Actor.PresentInteraction -= Present;
        if (_pose != null) { _pose.BeforeCorrections -= ContactPose; _pose.SetInteractionTarget("LeftHand", null); _pose.SetInteractionTarget("RightHand", null); }
        Actor.View?.EndInteraction(); Actor.InteractionMovementLocked = false;
        Actor.InteractionPosition = Actor.InteractionForward = null; Actor.LookTarget = null;
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width - 350, 10, 340, 145), GUI.skin.box);
        GUILayout.Label("Bow: 1 draw/stow · E retrieve/draw · Space release · Escape cancel");
        GUILayout.Label(Status);
        GUILayout.Label("One physical arrow. Reset restores the review setup.");
        if (GUILayout.Button("Reset bow review")) ResetReview();
        GUILayout.EndArea();
    }
}
