using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Physical gear transfer fixture using shared authored playback and grip anchors.</summary>
public sealed class EquipmentInteractionReview : MonoBehaviour
{
    public HumanoidAnimationPrototype Actor;
    public ActorInteractionDefinition Definition;
    public PhysicalEquipmentItem Sword, Shield;
    [Range(0, 1)] public float SwordContact = .44f, ShieldContact = .36f;
    public Vector3 PickupContactOffset = new(.065f, .114f, .761f);
    [Range(0, 1)] public float PickupContact = .76f;
    public string Status { get; private set; } = "Stowed";
    public float Progress => _progress;
    public string Phase => _pickupStage == 1 ? "Approach" : _pickupStage == 2 ? "Pickup" : _pickupStage == 3 ? "Rise" : _active == null ? "Idle" : _active == Sword ? "Sword" : "Shield";
    public bool Busy => _active != null;
    PhysicalEquipmentItem _active;
    float _progress, _speed, _desiredSpeed, _idle;
    bool _startedHeld, _bound;
    ProceduralPoseRig _pose;
    EntityId _owner, _swordId, _shieldId;
    int _pickupStage;
    float _approachTime;
    Vector3 _pickupTarget, _pickupForward;
    Quaternion _pickupGripRotation;

    void OnEnable()
    {
        var ids = new EntityIdAllocator(); _owner = ids.Next(); _swordId = ids.Next(); _shieldId = ids.Next();
        Actor.PrepareInteraction += Tick; Actor.PresentInteraction += Present;
    }

    public bool Toggle(bool shield)
    {
        var item = shield ? Shield : Sword;
        if (!_bound || Busy || item.State.Location == EquipmentLocation.World || !Actor.Motor.Grounded) return false;
        _active = item; _startedHeld = item.State.Location == EquipmentLocation.Hand;
        _progress = _startedHeld ? 1 : 0; _desiredSpeed = _speed = _startedHeld ? -1 : 1;
        Status = _startedHeld ? "Stowing" : "Drawing"; return true;
    }

    public void Cancel()
    {
        if (!Busy) return;
        if (_pickupStage == 1)
        {
            EndApproach(); _pickupStage = 0; _active = null; Status = "Pickup cancelled"; return;
        }
        if (_pickupStage != 0 && Sword.State.Location == EquipmentLocation.Hand)
        {
            Status = "Finishing pickup with sword retained"; return;
        }
        _desiredSpeed = _startedHeld ? 1 : -1; Status = "Returning through transfer";
    }

    public bool Pickup()
    {
        if (!_bound || Busy || Sword.State.Location != EquipmentLocation.World || !Actor.Motor.Grounded ||
            Vector3.Distance(Actor.Actor.position, Sword.Grip.Primary.position) > 2.5f) return false;
        _active = Sword; _pickupStage = 1; _approachTime = 0; _startedHeld = false; _progress = _speed = 0;
        _pickupForward = Actor.Actor.forward;
        Actor.InteractionPosition = Actor.InteractionForward = null;
        Actor.InteractionMovementLocked = false; Actor.View.EndInteraction();
        Status = "Approaching dropped sword"; return true;
    }

    void EndApproach()
    {
        Actor.InteractionApproachPosition = Actor.InteractionApproachForward = null;
        Actor.InteractionPosition = Actor.Actor.position; Actor.InteractionForward = Actor.Actor.forward;
        Actor.InteractionMovementLocked = true; Actor.View.BeginInteraction(Definition.Action);
    }

    public void Drop(bool shield)
    {
        var item = shield ? Shield : Sword;
        if (!_bound || item.State.Location != EquipmentLocation.Hand) return;
        item.Drop(); Status = "Dropped " + item.name;
        if (_active == item) { _active = null; _pickupStage = 0; }
    }

    public void ResetReview()
    {
        _active = null; _bound = false; _idle = 0; _pickupStage = 0; Status = "Stowed";
        Actor.InteractionApproachPosition = Actor.InteractionApproachForward = null;
        Actor.InteractionMovementLocked = false;
        Actor.InteractionPosition = Actor.InteractionForward = null;
        Actor.ResetActor(); Actor.VisitStation(0);
    }

    void Tick(float dt)
    {
        if (Actor.View == null) return;
        if (!_bound)
        {
            var animator = Actor.Actor.GetComponentInChildren<Animator>();
            Sword.Bind(_swordId, _owner, animator, Actor); Shield.Bind(_shieldId, _owner, animator, Actor);
            // Keep a stationary transfer at its support position on the sloped review floor.
            Actor.InteractionPosition = Actor.Actor.position;
            Actor.InteractionForward = Actor.Actor.forward;
            Actor.View.BeginInteraction(Definition.Action); _bound = true;
        }
        if (_pose != Actor.View.Pose)
        {
            if (_pose != null) _pose.BeforeCorrections -= ContactPose;
            _pose = Actor.View.Pose; _pose.BeforeCorrections += ContactPose;
        }
        _idle += dt;
        if (_pickupStage == 1)
        {
            _approachTime += dt;
            if (Sword.State.Location != EquipmentLocation.World || _approachTime > 8f) { Cancel(); return; }
            var approach = ActorInteractionApproach.ForContact(Actor.Motor.Pose, Sword.Grip.Primary.position, _pickupForward, PickupContactOffset);
            Actor.InteractionApproachPosition = approach.Position; Actor.InteractionApproachForward = approach.Forward;
            if (Vector3.ProjectOnPlane(approach.Position - Actor.Actor.position, approach.Up).magnitude > .045f) return;
            EndApproach(); _pickupStage = 2; _progress = 0; _desiredSpeed = _speed = 1;
            _pickupTarget = Sword.Grip.Primary.position;
            _pickupGripRotation = Sword.Grip.Primary.rotation;
            Status = "Reaching for sword";
        }
        if (Busy)
        {
            _speed = Mathf.MoveTowards(_speed, _desiredSpeed, dt * 12);
            float seconds = _pickupStage == 2 ? 1.5f : _pickupStage == 3 ? .9f : 1.3f;
            _progress = Mathf.Clamp01(_progress + _speed * dt / seconds);
            if (_pickupStage == 2 && Sword.State.Location == EquipmentLocation.World && Vector3.Distance(Sword.Grip.Primary.position, _pickupTarget) > .15f)
            { _desiredSpeed = -1; Status = "Sword moved; withdrawing reach"; }
            Actor.View.SetInteractionPhase(Phase, _progress);
        }
        else
        {
            bool sword = Sword.State.Location == EquipmentLocation.Hand, shield = Shield.State.Location == EquipmentLocation.Hand;
            Actor.View.SetInteractionPhase(sword && shield ? "Ready" : sword ? "SwordReady" : shield ? "ShieldReady" : "Idle", _idle / Definition.Phases[0].Seconds);
        }
        Actor.InteractionMovementLocked = true;
    }

    void ContactPose(float dt)
    {
        if (!Busy) return;
        var item = _active;
        if (_pickupStage == 1) return;
        float contact = _pickupStage == 2 ? PickupContact : item == Sword ? SwordContact : ShieldContact;
        if (!_pose.TryGetInteractionContactPose(item.Grip.PrimaryId, out var palm)) return;
        if (_pickupStage >= 2 && item.State.Location == EquipmentLocation.Hand)
        {
            float liftWeight = _pickupStage == 3 ? 1 - Mathf.SmoothStep(0, 1, _progress / .65f) : 1;
            _pose.SetInteractionTarget(item.Grip.PrimaryId, new InteractionPoseTarget(palm.position, _pickupGripRotation, liftWeight,
                useContact: true, followAuthoredMotion: true, preserveAuthoredContactJoints: false));
            return;
        }
        var target = item.Grip.Primary.position;
        float distance = Vector3.Distance(target, palm.position);
        bool atStorage = item.State.Location == EquipmentLocation.Stowed || _pickupStage == 2 && item.State.Location == EquipmentLocation.World;
        if (atStorage && Mathf.Abs(_progress - contact) < .15f && distance < (_pickupStage == 2 ? .16f : .12f))
        {
            float weight = 1 - Mathf.Clamp01(Mathf.Abs(_progress - contact) / .15f);
            _pose.SetInteractionTarget(item.Grip.PrimaryId, new InteractionPoseTarget(target, _pickupStage == 2 ? _pickupGripRotation : (Quaternion?)null, weight,
                useContact: true, followAuthoredMotion: true, preserveAuthoredContactJoints: _pickupStage != 2));
        }
        else _pose.SetInteractionTarget(item.Grip.PrimaryId, null);
    }

    void Present(float dt)
    {
        if (!_bound) return;
        foreach (var item in new[]{Sword, Shield})
        {
            if (!_pose.TryGetInteractionContactPose(item.Grip.PrimaryId, out var palm) ||
                !item.Grip.TryFit(palm.position, palm.rotation, out var hand)) continue;
            if (_active == item && _pickupStage == 2)
            {
                if (_speed > 0 && _desiredSpeed > 0 && _progress >= PickupContact && item.State.Location == EquipmentLocation.World)
                {
                    if (item.Pickup(_owner, hand)) Status = "Lifting recovered sword";
                    else { _desiredSpeed = -1; Status = "Pickup contact missed; withdrawing reach"; }
                }
            }
            else if (_active == item && _pickupStage == 0)
            {
                float contact = item == Sword ? SwordContact : ShieldContact;
                if (_speed > 0 && _progress >= contact && item.State.Location == EquipmentLocation.Stowed)
                {
                    if (!item.Contact(EquipmentLocation.Hand, hand)) { _desiredSpeed = -1; Status = "Contact missed; returning"; }
                }
                if (_speed < 0 && _progress <= contact && item.State.Location == EquipmentLocation.Hand)
                {
                    if (!item.Contact(EquipmentLocation.Stowed, item.StoragePose())) { _desiredSpeed = 1; Status = "Storage contact missed; retaining item"; }
                }
            }
            item.Present(hand, dt);
        }
        if (Busy && _pickupStage != 1 && (_progress >= 1 && _speed > 0 || _progress <= 0 && _speed < 0))
        {
            _pose.SetInteractionTarget(_active.Grip.PrimaryId, null);
            if (_pickupStage == 2 && Sword.State.Location == EquipmentLocation.Hand)
            { _pickupStage = 3; _progress = 0; _speed = _desiredSpeed = 1; }
            else
            {
                if (_pickupStage == 3) Status = "Sword recovered";
                _active = null; _pickupStage = 0;
            }
        }
    }

    void Update()
    {
        var key = Keyboard.current; if (key == null) return;
        if (key.digit1Key.wasPressedThisFrame) Toggle(false);
        if (key.digit2Key.wasPressedThisFrame) Toggle(true);
        if (key.gKey.wasPressedThisFrame) Drop(false);
        if (key.fKey.wasPressedThisFrame) Pickup();
        if (key.escapeKey.wasPressedThisFrame) Cancel();
    }

    void OnDisable()
    {
        Actor.PrepareInteraction -= Tick; Actor.PresentInteraction -= Present;
        if (_pose != null) { _pose.BeforeCorrections -= ContactPose; _pose.SetInteractionTarget("RightHand", null); _pose.SetInteractionTarget("LeftHand", null); }
        Actor.View?.EndInteraction(); Actor.InteractionMovementLocked = false;
        Actor.InteractionPosition = Actor.InteractionForward = null;
        Actor.InteractionApproachPosition = Actor.InteractionApproachForward = null;
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width - 340, 10, 330, 150), GUI.skin.box);
        GUILayout.Label("Physical gear: 1 sword · 2 shield · Escape return · G drop · F pick up");
        GUILayout.Label(Status);
        if (_bound) GUILayout.Label("Sword: " + Sword.State.Location + " | Shield: " + Shield.State.Location);
        if (GUILayout.Button("Reset gear review")) ResetReview();
        GUILayout.EndArea();
    }
}
