using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Local interaction fixture; the shared humanoid view owns all character posing.</summary>
public sealed class HumanInteractionReview : MonoBehaviour
{
    [Serializable]
    public sealed class Target
    {
        public string Label;
        public string Task;
        public Transform Contact;
        public Transform Hinge;
        public Vector3 OpenEuler;
        public Vector3 OpenOffset;
        [Min(.01f)] public float SlideSeconds = .2f;
        public bool SpringReturn;
        public bool MatchHingeToPhase;
        public bool MatchHingeToAuthoredHands;
        [NonSerialized] public Vector3? AuthoredHingeLever;
        [HideInInspector] public Vector3[] LidVertices;
        [HideInInspector] public int[] LidTriangles;
        public Transform PickupRoot;
        public Transform LeftContact;
        public ActorInteractionDefinition Use, Close, PutDown, Rear, Collect;
        public ActorInteractionDefinition GroundUse, TableUse, GroundPutDown, TablePutDown;
        public InteractionShelf Shelf;
        [NonSerialized] public bool Shelved;
        public Transform SeatApproach;
        public Transform Approach;
        public Transform SelectionAnchor, LookAnchor;
        public bool MatchContactRotation;
        public bool UseSourceHandRotation;
        public bool OpenAwayFromActor;
        public bool FollowAnimatedHand;
        public Transform ReturnAnchor;
        public HandContacts[] ContactSets = Array.Empty<HandContacts>();
        public Transform HandOrientation;
        public Vector3 OpenHandOffset;
        public Vector3 RightElbowDirection, LeftElbowDirection;
        public StationAction[] Actions = Array.Empty<StationAction>();
        public CampfireInteraction Campfire;
        public bool HasAuthoredContact;
        public bool FourSidedPickup;
        public Vector3 AuthoredContactOffset;
        [Min(0f)] public float PalmSurfaceOffset;
        [Min(.01f)] public float MaxContactCorrection = .12f;
    }

    [Serializable]
    public sealed class StationAction
    {
        public ActorInteractionDefinition Definition;
        public CraftingRecipe Recipe;
        public Transform Tool, ToolRest;
        public Vector3 ToolEuler;
        public ParticleSystem Flow;
        public string FlowPhase = "";
        public bool PickupAtMarker;
        public bool RigidGrip;
        public HeldToolGrip Grip;
        [NonSerialized] public float GripWeight;
        public string ReturnPhase = "";
        [NonSerialized] public bool ToolHeld;
        [NonSerialized] public Vector3 Velocity;
    }

    [Serializable]
    public sealed class HandContacts
    {
        public string Id;
        public Transform Right, Left;
        public bool MatchRotation;
        public Vector3 ToolEuler;
    }

    public HumanoidAnimationPrototype Actor;
    public Target[] Targets = Array.Empty<Target>();
    [Min(0f)] public float HeldItemLookDelay = 2f;
    public int Selected { get; private set; } = -1;
    Quaternion[] _closed;
    Vector3[] _closedPositions;
    bool[] _open;
    float[] _openSign;
    Vector2 _scroll;
    Target[] _initializedTargets;
    Vector3[] _homePositions;
    Quaternion[] _homeRotations;
    int _held = -1;
    bool _placing, _releaseRequested, _returning;
    InteractionShelf _placementShelf;
    int _shelfSlot = -1;
    Transform PlacementAnchor => _placementShelf != null && _shelfSlot >= 0 && _shelfSlot < _placementShelf.Slots.Length
        ? _placementShelf.Slots[_shelfSlot] : Targets[_held].ReturnAnchor;
    Vector3 _placePosition;
    Quaternion _placeRotation;
    Quaternion _carryRotation;
    Vector3 _carryFramePosition;
    Quaternion _carryFrameRotation;
    Transform _carryingHand;
    Vector3 _twoHandCarryGrip, _liftContactOffset;
    float _liftContactBlend;
    Vector3 _carryBodyOrigin, _carryBodyOffset;
    bool _carryBodyBound;
    Quaternion _handItemRotation;
    bool _lifted;
    Collider _placeSurface;
    Vector3 _surfacePosition, _placeCenter, _placeExtents;
    Quaternion _surfaceRotation;
    Vector3 _itemVelocity;
    Vector3 _carryGazePosition;
    float _carryIdleSeconds;
    Collider[] _heldColliders = Array.Empty<Collider>();
    bool[] _colliderEnabled = Array.Empty<bool>();
    Rigidbody _heldBody;
    bool _wasKinematic;
    float _useRemaining;
    readonly ActorInteractionSession _session = new();
    int _actionTarget = -1;
    bool _dragging, _seated, _usingRear;
    Vector3 _actionOrigin, _actionSelectionPosition, _actionDoorForward;
    HumanoidAnimationPrototype _boundActor;
    bool _markersBound;
    ActorInteractionDefinition _approachDefinition;
    int _approachTarget = -1;
    Vector3 _approachContact;
    float _approachElapsed, _approachStall, _approachDistance;
    public bool Approaching => _approachDefinition != null;
    public ActorInteractionSession Session => _session;
    public event Action<Target, string> InteractionMarker;
    public InventoryService Inventory { get; set; } = new InventoryService();
    bool _recipeCommitted;
    public int Held => _held;
    public bool Placing => _placing;
    public string Status { get; private set; } = "Face an object and press E.";
    Transform Frame => Actor.Actor != null ? Actor.Actor : Actor.transform;

    void Awake() => InitializeTargets();
    void OnEnable()
    {
        _boundActor = Actor;
        if (_boundActor != null)
        {
            _boundActor.PrepareInteraction += Tick;
            _boundActor.PresentInteraction += PresentHeldItem;
        }
    }

    bool BeginSequence(ActorInteractionDefinition definition, int target)
    {
        if (definition == null) return false;
        var station = Targets[target];
        foreach (var action in station.Actions ?? Array.Empty<StationAction>())
            if (action?.Definition == definition && action.Recipe != null && !action.Recipe.CanMake(Inventory))
            { Status = "Missing ingredients for " + action.Recipe.name + "."; return false; }
        if (station.Campfire != null && !station.Campfire.CanUse(definition))
        { Status = "Build the campfire, then light it before cooking."; return false; }
        if (Approaching) Cancel();
        if (_held < 0 && !_session.Active) OrientPickupContacts(Targets[target]);
        var plan = definition.Snapshot();
        for (int i = 0; i < plan.Count; i++)
        {
            if (string.IsNullOrEmpty(plan[i].ContactSet)) continue;
            var contacts = FindContacts(Targets[target], plan[i].ContactSet);
            if (contacts == null || plan[i].RightHandWeight > 0f && contacts.Right == null ||
                plan[i].LeftHandWeight > 0f && contacts.Left == null)
            { Status = "Missing interaction contacts: " + plan[i].ContactSet; return false; }
        }
        if (_held < 0 && !_session.Active && !Approaching && Actor.Motor != null)
        {
            var entry = Targets[target];
            var anchor = entry.SeatApproach != null && definition != entry.Rear ? entry.SeatApproach : entry.Approach;
            Vector3 facing = anchor != null ? anchor.forward : entry.FourSidedPickup ? entry.Contact.forward : Frame.forward;
            Vector3 destination = anchor != null ? anchor.position : Frame.position;
            if (entry.OpenAwayFromActor)
            {
                if (entry.HasAuthoredContact && !DoorContactFits(entry, Frame.position, false))
                { Status = "The door contact is outside this authored reach."; return false; }
                var doorPose = DoorApproach(entry);
                destination = doorPose.Position; facing = doorPose.Forward;
            }
            else if (anchor == null && entry.HasAuthoredContact)
            {
                Vector3 sourceContact = entry.FourSidedPickup
                    ? Frame.position + Quaternion.LookRotation(facing, Frame.up) * Vector3.Scale(entry.AuthoredContactOffset, Frame.lossyScale)
                    : Frame.TransformPoint(entry.AuthoredContactOffset);
                destination += Vector3.ProjectOnPlane(entry.Contact.position - sourceContact, Frame.up);
                if (Actor.Collision != null && !Actor.Collision.ClearSegment(Frame.position, destination, Frame.up, facing))
                {
                    Vector3 desired = destination;
                    destination = Actor.Collision.Move(Frame.position, desired, Frame.up, facing, Actor.Motor.Grounded);
                    if (!Actor.Collision.ClearSegment(Frame.position, destination, Frame.up, facing) ||
                        Vector3.Distance(destination, desired) > entry.MaxContactCorrection + ActorCollision.Skin)
                    { Status = "Move to a closer edge of the object."; return false; }
                }
            }
            if (entry.OpenAwayFromActor && Actor.Collision != null &&
                !Actor.Collision.ClearSegment(Frame.position, destination, Frame.up, facing))
            { Status = "No clear approach. Move to another side of the object."; return false; }
            float distance = Vector3.ProjectOnPlane(destination - Frame.position, Frame.up).magnitude;
            if ((anchor != null || entry.HasAuthoredContact || entry.OpenAwayFromActor) &&
                (distance > entry.MaxContactCorrection || Vector3.Angle(Frame.forward, facing) > 12f))
            {
                _approachDefinition = definition; _approachTarget = target; _approachContact = entry.Contact.position;
                _approachElapsed = _approachStall = 0f; _approachDistance = distance;
                Actor.InteractionMovementLocked = false;
                Actor.InteractionPosition = Actor.InteractionForward = null;
                Actor.InteractionApproachPosition = destination; Actor.InteractionApproachForward = facing;
                Actor.RightInteractionTarget = Actor.LeftInteractionTarget = null; Actor.Reach = false;
                Status = "Moving into position for " + entry.Label + ". Escape or movement cancels.";
                return true;
            }
        }
        if (Actor.View != null && !Actor.View.BeginInteraction(plan.Action))
        { Status = "The actor does not have this interaction animation."; return false; }
        foreach (var action in Targets[target].Actions ?? Array.Empty<StationAction>())
            if (action != null) action.ToolHeld = false;
        if (Targets[target].OpenAwayFromActor && !_open[target]) SetDoorOpeningDirection(target);
        if (Targets[target].OpenAwayFromActor) Targets[target].AuthoredHingeLever = null;
        _actionTarget = target;
        _actionOrigin = Frame.position;
        _actionSelectionPosition = Targets[target].OpenAwayFromActor ? Targets[target].Contact.position :
            (Targets[target].SelectionAnchor != null ? Targets[target].SelectionAnchor.position : Targets[target].Contact.position);
        _actionDoorForward = Vector3.ProjectOnPlane(Targets[target].Contact.forward, Frame.up).normalized;
        if (Vector3.Dot(_actionDoorForward, _actionSelectionPosition - _actionOrigin) < 0f) _actionDoorForward = -_actionDoorForward;
        _usingRear = definition == Targets[target].Rear;
        _recipeCommitted = false;
        _session.Begin(plan);
        Actor.Reach = false;
        Status = Targets[target].Label + ": " + plan.Action;
        return true;
    }

    CharacterPose DoorApproach(Target target)
    {
        Vector3 facing = Vector3.ProjectOnPlane(target.Contact.forward, Frame.up).normalized;
        if (Vector3.Dot(facing, target.Contact.position - Frame.position) < 0f) facing = -facing;
        var seed = new CharacterPose(Frame.position, Frame.up, Frame.forward);
        var pose = ActorInteractionApproach.ForContact(seed, target.Contact.position, facing, target.HasAuthoredContact ? Vector3.Scale(target.AuthoredContactOffset, Frame.lossyScale) : new Vector3(-.06f, 0f, .535f));
        return pose;
    }

    bool DoorContactFits(Target target, Vector3 actorPosition, bool includePlanar)
    {
        Vector3 expected = actorPosition + Quaternion.LookRotation(Frame.forward, Frame.up) * Vector3.Scale(target.AuthoredContactOffset, Frame.lossyScale);
        Vector3 error = target.Contact.position - expected;
        return (includePlanar ? error.magnitude : Mathf.Abs(Vector3.Dot(error, Frame.up))) <= target.MaxContactCorrection;
    }
    void OrientPickupContacts(Target target)
    {
        if (!target.FourSidedPickup || target.PickupRoot == null || target.Contact == null || target.LeftContact == null) return;
        var root = target.PickupRoot;
        Vector3 actor = root.InverseTransformPoint(Frame.position);
        Vector3 right = root.InverseTransformPoint(target.Contact.position);
        Vector3 left = root.InverseTransformPoint(target.LeftContact.position);
        Vector3 middle = (right + left) * .5f;
        float desired = Mathf.Round(Mathf.Atan2(-actor.x, -actor.z) * Mathf.Rad2Deg / 90f) * 90f;
        float current = Mathf.Atan2(-middle.x, -middle.z) * Mathf.Rad2Deg;
        Quaternion turn = Quaternion.AngleAxis(Mathf.DeltaAngle(current, desired), Vector3.up);
        // These invisible anchors change before the action. The existing pose blend owns visible hand motion.
        target.Contact.position = root.TransformPoint(turn * right);
        target.LeftContact.position = root.TransformPoint(turn * left);
        target.Contact.rotation = target.LeftContact.rotation = root.rotation * Quaternion.Euler(0f, desired, 0f);
    }

    void ApplyMarker(string marker)
    {
        var target = Targets[_actionTarget];
        foreach (var action in target.Actions ?? Array.Empty<StationAction>())
            if (action?.Definition != null && action.Definition.Action == _session.Plan.Action)
            {
                if (marker == "CraftComplete" && action.Recipe != null && !_recipeCommitted)
                {
                    if (target.Contact == null || !target.Contact.gameObject.activeInHierarchy ||
                        target.Campfire != null && !target.Campfire.CanUse(action.Definition) ||
                        !action.Recipe.TryMake(Inventory))
                    { Cancel(); Status = "Crafting stopped: station or ingredients unavailable."; return; }
                    _recipeCommitted = true;
                    Status = "Completed " + action.Recipe.name + ".";
                }
                if (marker == "TakeTool") action.ToolHeld = true;
                if (marker == "ReturnTool") action.ToolHeld = false;
            }
        if (target.OpenAwayFromActor && target.HasAuthoredContact && (marker == "Open" || marker == "Close") &&
            !DoorContactFits(target, Frame.position, true))
        { Status = "The door contact moved outside this authored reach."; return; }
        target.Campfire?.ApplyMarker(marker);
        switch (marker)
        {
            case "Open": SetOpen(_actionTarget, true); break;
            case "Close": SetOpen(_actionTarget, false); break;
            case "Pickup": Acquire(_actionTarget); break;
            case "Drag": Acquire(_actionTarget); _dragging = true; break;
            case "Place":
                if (_placing && PlacementClear()) _releaseRequested = true;
                break;
            case "Seat": _seated = true; break;
            case "Stand": _seated = false; break;
        }
        InteractionMarker?.Invoke(target, marker);
    }

    void Acquire(int index)
    {
        if (_held >= 0 || Targets[index].PickupRoot == null) return;
        _held = index;
        _carryGazePosition = Frame.position; _carryIdleSeconds = 0f;
        var root = Targets[_held].PickupRoot;
        _carryRotation = Quaternion.Inverse(Frame.rotation) * root.rotation;
        _carryFramePosition = Frame.position; _carryFrameRotation = Frame.rotation;
        var animator = Frame.GetComponentInChildren<Animator>();
        _carryingHand = (Targets[index].FollowAnimatedHand || Targets[index].LeftContact != null && _session.Active && _session.Phase.Marker == "Pickup") && animator != null && animator.isHuman
            ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        if (_carryingHand != null) _handItemRotation = Quaternion.Inverse(_carryingHand.rotation) * root.rotation;
        _lifted = false;
        _liftContactBlend = 1f; _liftContactOffset = Vector3.zero;
        _twoHandCarryGrip = new Vector3(0f, 1f, .48f);
        _carryBodyBound = false; _carryBodyOffset = Vector3.zero;
        if (Targets[index].LeftContact != null && Actor.View != null &&
            Actor.View.Pose.TryGetInteractionContact("RightHand", out var rightPalm, out _) &&
            Actor.View.Pose.TryGetInteractionContact("LeftHand", out var leftPalm, out _))
            _liftContactOffset = Frame.InverseTransformVector((Targets[index].Contact.position + Targets[index].LeftContact.position - rightPalm - leftPalm) * .5f);
        _itemVelocity = Vector3.zero;
        _heldColliders = root.GetComponentsInChildren<Collider>(true);
        _colliderEnabled = new bool[_heldColliders.Length];
        for (int i = 0; i < _heldColliders.Length; i++)
        {
            _colliderEnabled[i] = _heldColliders[i].enabled;
            _heldColliders[i].enabled = false;
        }
        _heldBody = root.GetComponent<Rigidbody>();
        if (_heldBody != null) { _wasKinematic = _heldBody.isKinematic; _heldBody.isKinematic = true; }
        Actor.Reach = false;
        Status = "Holding " + Targets[_held].Label + ". Press E to put down.";
    }

    void InitializeTargets()
    {
        if (!_markersBound) { _session.Marker += ApplyMarker; _markersBound = true; }
        if (ReferenceEquals(_initializedTargets, Targets)) return;
        ReleaseHeld();
        _initializedTargets = Targets;
        _closed = new Quaternion[Targets.Length];
        _hingeSpeeds = new float[Targets.Length];
        Array.Fill(_hingeSpeeds, 90f);
        _closedPositions = new Vector3[Targets.Length];
        _open = new bool[Targets.Length];
        _openSign = new float[Targets.Length];
        _homePositions = new Vector3[Targets.Length];
        _homeRotations = new Quaternion[Targets.Length];
        for (int i = 0; i < Targets.Length; i++)
        {
            _openSign[i] = 1f;
            if (Targets[i] == null) continue;
            if (Targets[i].Hinge != null)
            {
                _closed[i] = Targets[i].Hinge.localRotation;
                _closedPositions[i] = Targets[i].Hinge.localPosition;
            }
            if (Targets[i].PickupRoot == null) continue;
            _homePositions[i] = Targets[i].PickupRoot.position;
            _homeRotations[i] = Targets[i].PickupRoot.rotation;
        }
    }

    public void Select(int index)
    {
        if (Actor == null || index < 0 || index >= Targets.Length || Targets[index].Contact == null) return;
        Cancel(); _session.Cancel(); _seated = false;
        Actor.InteractionPosition = Actor.InteractionForward = null;
        Actor.Panel = null;
        Actor.VisitStation(index);
        Actor.HandTarget = Actor.LookTarget = Targets[index].Contact;
        Selected = index;
    }

    public void ToggleProp()
    {
        InitializeTargets();
        if (Selected >= 0 && Targets[Selected].Hinge != null) SetOpen(Selected, !_open[Selected]);
    }

    float[] _hingeSpeeds;

    void SetDoorOpeningDirection(int index)
    {
        var target = Targets[index];
        if (target.Hinge == null) return;
        Quaternion closedWorld = target.Hinge.parent != null ? target.Hinge.parent.rotation * _closed[index] : _closed[index];
        Vector3 lever = Quaternion.Inverse(target.Hinge.rotation) * (target.Contact.position - target.Hinge.position);
        Vector3 motion = closedWorld * (Quaternion.Euler(target.OpenEuler * .01f) * lever - lever);
        _openSign[index] = Vector3.Dot(motion, target.Contact.position - Frame.position) < 0f ? -1f : 1f;
    }

    void SetOpen(int index, bool open)
    {
        var target = Targets[index];
        if (open && !_open[index] && target.OpenAwayFromActor)
        {
            SetDoorOpeningDirection(index);
        }
        if (target.MatchHingeToPhase && _session.Active && _actionTarget == index)
        {
            var phase = _session.Phase;
            float remaining = Mathf.Max(.01f, phase.Seconds * (1f - phase.MarkerProgress));
            Quaternion destination = _closed[index] * (open ? Quaternion.Euler(target.OpenEuler * _openSign[index]) : Quaternion.identity);
            _hingeSpeeds[index] = Quaternion.Angle(target.Hinge.localRotation, destination) / remaining;
        }
        _open[index] = open;
    }

    public void RefreshTarget()
    {
        if (Actor == null) return;
        InitializeTargets();
        Transform frame = Actor.Actor != null ? Actor.Actor : Actor.transform;
        int nearest = -1;
        float best = float.PositiveInfinity;
        for (int i = 0; i < Targets.Length; i++)
        {
            var contact = Targets[i]?.Contact;
            if (i == _held || Targets[i].Shelved || contact == null || !contact.gameObject.activeInHierarchy) continue;
            var selection = Targets[i].OpenAwayFromActor && _open[i] ? contact :
                Targets[i].SelectionAnchor != null ? Targets[i].SelectionAnchor : contact;
            Vector3 selectionPosition = _session.Active && i == _actionTarget && Targets[i].OpenAwayFromActor
                ? _actionSelectionPosition : selection.position;
            if (Targets[i].FourSidedPickup && Targets[i].PickupRoot != null)
                selectionPosition += frame.up * Vector3.Dot(contact.position - selectionPosition, frame.up);
            Vector3 delta = selectionPosition - frame.position;
            Vector3 flat = Vector3.ProjectOnPlane(delta, frame.up);
            float distance = flat.magnitude;
            if (distance > 1.1f || Mathf.Abs(Vector3.Dot(delta, frame.up)) > 2.2f) continue;
            float facing = distance > .01f ? Vector3.Dot(frame.forward, flat / distance) : 1f;
            if (facing < .5f) continue;
            Vector3 origin = frame.position + frame.up * .9f;
            if (Physics.Linecast(origin, selectionPosition, out var hit, 1 << 0, QueryTriggerInteraction.Ignore)
                && hit.transform.root != contact.root && hit.transform.root != frame.root) continue;
            float score = distance + (1f - facing) * .5f;
            if (score >= best) continue;
            best = score; nearest = i;
        }
        if (nearest != Selected)
        {
            Actor.Reach = false;
            if (_session.Active && _held < 0 && nearest != _actionTarget && Targets[_actionTarget].SeatApproach == null)
                Cancel();
        }
        Selected = nearest;
        Actor.HandTarget = Actor.LookTarget = nearest >= 0 ? Targets[nearest].Contact : null;
    }

    public bool Collect()
    {
        if (!_session.Active || !_session.Phase.WaitForInput || _actionTarget < 0 || _held >= 0) return false;
        RefreshTarget();
        if (Selected != _actionTarget || !_open[_actionTarget] || Targets[_actionTarget].Collect == null) return false;
        return BeginSequence(Targets[_actionTarget].Collect, _actionTarget);
    }

    public bool Interact()
    {
        RefreshTarget();
        if (Actor == null) return false;
        if (Approaching) { Cancel(); return true; }
        if (_placing) { Cancel(); return true; }
        if (_session.Active && _held < 0)
        {
            if (_session.Continue()) return true;
            Cancel(); return true;
        }
        if (_held >= 0)
        {
            int held = _held;
            if (!Place()) return false;
            var putDown = Targets[held].PutDown;
            if (_placementShelf != null) putDown = Targets[held].TablePutDown ?? putDown;
            if (!_returning)
                putDown = (Vector3.Dot(_placePosition - Frame.position, Frame.up) < .5f
                    ? Targets[held].GroundPutDown : Targets[held].TablePutDown) ?? putDown;
            if (putDown != null && !BeginSequence(putDown, held))
            { _placing = false; return false; }
            return true;
        }
        if (Selected >= 0 && Targets[Selected].Use != null)
        {
            var target = Targets[Selected];
            if (target.Campfire != null)
            {
                for (int i = 0; i < target.Actions.Length; i++)
                    if (target.Campfire.CanUse(target.Actions[i]?.Definition)) return UseStationAction(i);
                return false;
            }
            var definition = _open[Selected] && target.Close != null ? target.Close : target.Use;
            if (target.PickupRoot != null)
                definition = (Vector3.Dot(target.Contact.position - Frame.position, Frame.up) < .5f
                    ? target.GroundUse : target.TableUse) ?? definition;
            if (target.Rear != null && target.SeatApproach != null &&
                Vector3.Dot(Frame.position - target.Contact.position, target.SeatApproach.forward) < 0f)
                definition = target.Rear;
            return BeginSequence(definition, Selected);
        }
        if (Selected >= 0 && Targets[Selected].Hinge != null)
        {
            ToggleProp();
            Actor.Reach = true;
            _useRemaining = .65f;
            Status = (_open[Selected] ? "Opened " : "Closed ") + Targets[Selected].Label;
            return true;
        }
        if (Selected < 0) { if (Actor != null) Actor.Reach = false; return false; }
        if (Targets[Selected].PickupRoot != null)
        {
            Acquire(Selected);
            return true;
        }
        Actor.Reach = !Actor.Reach;
        return true;
    }

    public void Cancel()
    {
        _approachDefinition = null; _approachTarget = -1;
        if (Actor != null) Actor.InteractionApproachPosition = Actor.InteractionApproachForward = null;
        if (_session.Active && _actionTarget >= 0 && Targets[_actionTarget]?.Contact != null &&
            Targets[_actionTarget].Contact.gameObject.activeInHierarchy &&
            Vector3.ProjectOnPlane(_actionOrigin - Frame.position, Frame.up).magnitude <= 1.1f &&
            (Actor.Motor == null || Actor.Motor.Grounded && !Actor.Motor.Swimming && !Actor.Traversal.Active))
            foreach (var action in Targets[_actionTarget].Actions)
                if (action?.Definition != null && action.Definition.Action == _session.Plan.Action && action.ToolHeld && !string.IsNullOrEmpty(action.ReturnPhase))
                    for (int i = 0; i < _session.Plan.Count; i++)
                        if (_session.Plan[i].Name == action.ReturnPhase)
                        {
                            if (_session.PhaseIndex < i) _session.Begin(_session.Plan, i);
                            Status = "Returning the held tool.";
                            return;
                        }
        if (_actionTarget >= 0 && _actionTarget < Targets.Length && Targets[_actionTarget].SpringReturn && _open != null)
            _open[_actionTarget] = false;
        if (_seated)
        {
            _seated = false;
            if (_actionTarget >= 0 && Targets[_actionTarget].Contact != null &&
                Targets[_actionTarget].Contact.gameObject.activeInHierarchy &&
                BeginSequence(Targets[_actionTarget].Close, _actionTarget)) return;
        }
        _session.Cancel();
        Actor?.View?.EndInteraction();
        if (Actor != null)
        {
            Actor.InteractionMovementLocked = false;
            Actor.InteractionPosition = Actor.InteractionForward = null;
            if (_held < 0) Actor.RightInteractionTarget = Actor.LeftInteractionTarget = null;
        }
        // Retain the item at its displayed pose; Tick blends it back into the carry pose.
        _placing = false;
        _releaseRequested = false;
        _useRemaining = 0f;
        if (Actor != null) Actor.Reach = false;
        Status = _held >= 0 ? "Placement cancelled. Still carrying the item." : "Interaction cancelled.";
    }

    bool Place()
    {
        var target = Targets[_held];
        if (target.PickupRoot == null) { ReleaseHeld(); return false; }
        _placementShelf = null; _shelfSlot = -1;
        if (target.Shelf != null && Vector3.ProjectOnPlane(target.Shelf.transform.position - Frame.position, Frame.up).magnitude < 1.3f &&
            Vector3.Dot(target.Shelf.transform.position - Frame.position, Frame.forward) > 0f)
        {
            _shelfSlot = target.Shelf.NextSlot();
            if (_shelfSlot < 0) { Status = "The bookshelf is full."; return false; }
            _placementShelf = target.Shelf;
        }
        var anchor = PlacementAnchor;
        if (anchor != null)
        {
            if (!anchor.gameObject.activeInHierarchy ||
                Vector3.ProjectOnPlane(anchor.position - Frame.position, Frame.up).magnitude > 1.1f ||
                Vector3.Dot(anchor.position - Frame.position, Frame.forward) <= 0f)
            { Status = "Face the rack and move closer to return the item."; return false; }
            _placePosition = anchor.position;
            _placeRotation = anchor.rotation;
            _placing = true; _returning = true; _releaseRequested = false;
            Status = "Returning " + target.Label + ". E or Escape cancels.";
            return true;
        }
        Vector3 origin = Frame.position + Frame.forward * .65f + Frame.up * 1.6f;
        if (!Physics.Raycast(origin, -Frame.up, out var hit, 2f, 1 << 0, QueryTriggerInteraction.Ignore)
            || Vector3.Dot(hit.normal, Frame.up) < .9f)
        { Status = "No level surface in front of the character."; return false; }
        _placeRotation = target.FollowAnimatedHand ? _homeRotations[_held] : target.PickupRoot.rotation;
        Bounds bounds = ItemBounds(target.PickupRoot, _placeRotation);
        Vector3 bottom = bounds.center - Frame.up * bounds.extents.y;
        _placePosition = target.PickupRoot.position + hit.point - bottom + Frame.up * .01f;
        _placeCenter = bounds.center + _placePosition - target.PickupRoot.position;
        _placeExtents = bounds.extents * .95f;
        _placeSurface = hit.collider;
        _surfacePosition = hit.collider.transform.position;
        _surfaceRotation = hit.collider.transform.rotation;
        _returning = false;
        if (!PlacementClear()) { Status = "The placement space is blocked."; return false; }
        _placing = true;
        _releaseRequested = false;
        Status = "Placing " + target.Label + ". E or Escape cancels.";
        return true;
    }

    bool PlacementClear()
    {
        var anchor = PlacementAnchor;
        if (_returning)
            return (_placementShelf == null || _placementShelf.Available(_shelfSlot)) && anchor != null && anchor.gameObject.activeInHierarchy && Vector3.Distance(anchor.position, _placePosition) < .001f
                && Quaternion.Angle(anchor.rotation, _placeRotation) < .1f;
        if (_placeSurface == null || !_placeSurface.enabled || !_placeSurface.gameObject.activeInHierarchy
            || Vector3.Distance(_placeSurface.transform.position, _surfacePosition) > .001f
            || Quaternion.Angle(_placeSurface.transform.rotation, _surfaceRotation) > .1f) return false;
        foreach (var collider in Physics.OverlapBox(_placeCenter, _placeExtents, Quaternion.identity,
            1 << 0, QueryTriggerInteraction.Ignore))
            if (collider != _placeSurface && !collider.transform.IsChildOf(Targets[_held].PickupRoot)) return false;
        return true;
    }

    static Bounds ItemBounds(Transform root, Quaternion rotation)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        var bounds = new Bounds(root.position, Vector3.one * .05f);
        bool first = true;
        var rotate = Matrix4x4.TRS(root.position, rotation * Quaternion.Inverse(root.rotation), Vector3.one)
            * Matrix4x4.Translate(-root.position);
        foreach (var renderer in renderers)
        {
            var local = renderer.localBounds;
            var matrix = rotate * renderer.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                else bounds.Encapsulate(point);
            }
        }
        return bounds;
    }

    void ReleaseHeld()
    {
        for (int i = 0; i < _heldColliders.Length; i++)
            if (_heldColliders[i] != null) _heldColliders[i].enabled = _colliderEnabled[i];
        if (_heldBody != null) _heldBody.isKinematic = _wasKinematic;
        _heldBody = null;
        _carryingHand = null;
        _heldColliders = Array.Empty<Collider>();
        _held = -1; _placing = _dragging = _releaseRequested = _returning = false;
        _placementShelf = null; _shelfSlot = -1;
        if (Actor == null) return;
        Actor.RightInteractionTarget = Actor.LeftInteractionTarget = null;
        Actor.InteractionCrouch = false;
    }

    void OnDisable()
    {
        if (_contactPose != null) _contactPose.BeforeCorrections -= RefreshAuthoredContacts;
        _contactPose = null;
        _approachDefinition = null; _approachTarget = -1;
        if (Actor != null) Actor.InteractionApproachPosition = Actor.InteractionApproachForward = null;
        foreach (var target in Targets)
            foreach (var action in target?.Actions ?? Array.Empty<StationAction>())
                if (action?.Flow != null) action.Flow.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (_boundActor != null)
        {
            _boundActor.PrepareInteraction -= Tick;
            _boundActor.PresentInteraction -= PresentHeldItem;
        }
        _boundActor = null;
        _session.Cancel();
        Actor?.View?.EndInteraction();
        ReleaseHeld();
        if (Actor != null)
        {
            Actor.Reach = Actor.InteractionMovementLocked = false;
            Actor.InteractionPosition = Actor.InteractionForward = null;
        }
    }

    public void Tick(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
        if (Actor == null) return;
        if (_contactPose != Actor.View?.Pose)
        {
            if (_contactPose != null) _contactPose.BeforeCorrections -= RefreshAuthoredContacts;
            _contactPose = Actor.View?.Pose;
            if (_contactPose != null) _contactPose.BeforeCorrections += RefreshAuthoredContacts;
        }
        InitializeTargets();
        TickApproach(dt);
        TickSequence(dt);
        foreach (var entry in Targets) entry?.Campfire?.Tick(dt);
        _useRemaining = Mathf.Max(0f, _useRemaining - dt);
        if (_useRemaining == 0f && Selected >= 0 && Targets[Selected].Hinge != null) Actor.Reach = false;
        UpdateHinges(dt, false);
        if (_session.Active)
            SetHandTargets(Targets[_actionTarget], _session.Phase.RightHandWeight, _session.Phase.LeftHandWeight);
        if (_held < 0) return;
        var target = Targets[_held];
        if (target.PickupRoot == null || target.Contact == null || !target.PickupRoot.gameObject.activeInHierarchy)
        { ReleaseHeld(); Status = "The held item is no longer available."; return; }
        var root = target.PickupRoot;
        // Once acquired, carry the prop in the actor frame. Smooth only the remaining local settling.
        if (_lifted && !_placing && !_dragging && _carryingHand == null)
        {
            Quaternion turn = Frame.rotation * Quaternion.Inverse(_carryFrameRotation);
            root.SetPositionAndRotation(Frame.position + turn * (root.position - _carryFramePosition), turn * root.rotation);
            _itemVelocity = turn * _itemVelocity;
        }
        _carryFramePosition = Frame.position; _carryFrameRotation = Frame.rotation;
        if (!_placing && (!_session.Active || _session.Phase.AllowMovement))
        {
            float distance = Vector3.ProjectOnPlane(Frame.position - _carryGazePosition, Frame.up).magnitude;
            bool moving = distance > .08f * Mathf.Max(dt, 0f);
            _carryIdleSeconds = moving ? 0f : _carryIdleSeconds + Mathf.Max(dt, 0f);
            Actor.LookTarget = !moving && _carryIdleSeconds >= HeldItemLookDelay ? target.Contact : null;
        }
        else _carryIdleSeconds = 0f;
        _carryGazePosition = Frame.position;
        if (_placing) _carryBodyBound = false;
        if (_placing && (!PlacementClear() || Vector3.ProjectOnPlane(_placePosition - Frame.position, Frame.up).magnitude > 1.1f))
        { Cancel(); Status = "Placement interrupted. Still carrying the item."; }
        if (!_placing && _carryingHand != null && target.LeftContact != null && _session.Phase?.UseLocomotion == true)
        {
            _twoHandCarryGrip = Frame.InverseTransformPoint((target.Contact.position + target.LeftContact.position) * .5f);
            _carryingHand = null; _lifted = true;
        }
        if (!_placing && _carryingHand != null)
        {
            // The clip owns the arm after acquisition. The prop follows the completed pose below.
            Actor.RightInteractionTarget = Actor.LeftInteractionTarget = null;
            Actor.InteractionCrouch = false;
            return;
        }
        root.rotation = Quaternion.RotateTowards(root.rotation,
            _placing ? _placeRotation : Frame.rotation * _carryRotation, 180f * dt);
        Vector3 grip = target.LeftContact != null ? (target.Contact.position + target.LeftContact.position) * .5f : target.Contact.position;
        Vector3 destination = _placing ? _placePosition : root.position +
            Frame.TransformPoint(target.LeftContact != null && _lifted && !_dragging ? _twoHandCarryGrip + _carryBodyOffset :
                new Vector3(target.LeftContact != null ? 0f : .24f, _dragging ? .85f : 1f, .48f)) - grip;
        if (_dragging && !_placing) destination.y = root.position.y;
        // No reparent or pose reset: acquisition, placement, reversal, and movement start at the displayed pose.
        if (dt > 0f) root.position = Vector3.SmoothDamp(root.position, destination, ref _itemVelocity, .12f,
            _lifted && !_placing ? 6f : 1.2f, dt);
        if (!_placing && Vector3.Distance(root.position, destination) < .02f) _lifted = true;
        Actor.InteractionCrouch = !_session.Active && Vector3.Dot(target.Contact.position - Frame.position, Frame.up) < .65f;
        float rightWeight = _releaseRequested ? 0f : _session.Phase?.RightHandWeight ?? 1f;
        float leftWeight = _releaseRequested ? 0f : _session.Phase?.LeftHandWeight ?? 1f;
        SetHandTargets(target, rightWeight, leftWeight);
        if (_placing && (_releaseRequested || !_session.Active) && Vector3.Distance(root.position, _placePosition) < .002f
            && Quaternion.Angle(root.rotation, _placeRotation) < .1f)
        {
            if (_placementShelf != null) target.Shelved = _placementShelf.Place(_shelfSlot, root);
            ReleaseHeld(); Status = "Item placed.";
        }
    }

    public bool UseStationAction(int index)
    {
        RefreshTarget();
        if (Selected < 0 || _held >= 0) return false;
        var actions = Targets[Selected].Actions;
        if (actions == null || index < 0 || index >= actions.Length || actions[index]?.Definition == null) return false;
        if (Targets[Selected].Campfire != null && !Targets[Selected].Campfire.CanUse(actions[index].Definition))
        {
            Status = "Build the campfire, then light it before cooking.";
            return false;
        }
        if (_session.Active && (_actionTarget != Selected || !_session.Phase.WaitForInput)) return false;
        return BeginSequence(actions[index].Definition, Selected);
    }

    void PresentHeldItem(float dt)
    {
        PresentStationTools(dt);
        if (_held < 0 || _placing || _carryingHand == null || dt <= 0f || Actor.View == null) return;
        var target = Targets[_held];
        if (target.PickupRoot == null || target.Contact == null ||
            !Actor.View.Pose.TryGetInteractionContact("RightHand", out var palm, out _)) return;
        var root = target.PickupRoot;
        if (target.LeftContact != null && Actor.View.Pose.TryGetInteractionContact("LeftHand", out var leftPalm, out _))
        {
            // The authored lift supplies force and travel. The prop follows both palms without a second lag curve.
            root.rotation = Frame.rotation * _carryRotation;
            _liftContactBlend = Mathf.MoveTowards(_liftContactBlend, 0f, dt / .12f);
            Vector3 grip = (target.Contact.position + target.LeftContact.position) * .5f;
            root.position += (palm + leftPalm) * .5f + Frame.TransformVector(_liftContactOffset) * _liftContactBlend - grip;
            return;
        }
        root.rotation = Quaternion.RotateTowards(root.rotation, _carryingHand.rotation * _handItemRotation, 360f * dt);
        var destination = root.position + palm - target.Contact.position;
        root.position = Vector3.SmoothDamp(root.position, destination, ref _itemVelocity, .06f, 6f, dt);
    }

    void PresentStationTools(float dt)
    {
        if (dt <= 0f || Actor.View == null ||
            !Actor.View.Pose.TryGetInteractionContact("RightHand", out var palm, out _)) return;
        var animator = Frame.GetComponentInChildren<Animator>();
        var hand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        if (hand == null) return;
        for (int i = 0; i < Targets.Length; i++)
        {
            var actions = Targets[i]?.Actions;
            if (actions == null) continue;
            foreach (var action in actions)
            {
                if (action?.Tool == null || action.ToolRest == null) continue;
                bool active = _session.Active && _actionTarget == i && action.Definition != null &&
                    _session.Plan.Action == action.Definition.Action;
                bool held = active && (!action.PickupAtMarker || action.ToolHeld);
                if (action.Flow != null)
                {
                    bool flowing = held && _session.Phase.Name == action.FlowPhase;
                    if (flowing && !action.Flow.isEmitting) action.Flow.Play();
                    else if (!flowing && action.Flow.isEmitting) action.Flow.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
                var position = held ? palm : action.ToolRest.position;
                var rotation = held ? hand.rotation * Quaternion.Euler(action.ToolEuler) : action.ToolRest.rotation;
                if (held && action.Grip != null && Actor.View.Pose.TryGetInteractionContactPose(action.Grip.PrimaryId, out var primary) &&
                    action.Grip.TryFit(primary.position, primary.rotation, out var fitted))
                {
                    position = fitted.position; rotation = fitted.rotation;
                }
                var contacts = active ? FindContacts(Targets[i], _session.Phase.ContactSet) : null;
                if (held && action.PickupAtMarker && contacts?.Right != null)
                    rotation = contacts.Right.rotation * Quaternion.Euler(contacts.ToolEuler);
                if (action.RigidGrip)
                {
                    action.GripWeight = Mathf.MoveTowards(action.GripWeight, held ? 1f : 0f, dt / .2f);
                    float weight = Mathf.SmoothStep(0f, 1f, held ? action.GripWeight : 1f - action.GripWeight);
                    action.Tool.rotation = Quaternion.Slerp(action.Tool.rotation, rotation, weight);
                    action.Tool.position = Vector3.Lerp(action.Tool.position, position, weight);
                }
                else
                {
                    action.Tool.rotation = Quaternion.RotateTowards(action.Tool.rotation, rotation, 360f * dt);
                    action.Tool.position = Vector3.SmoothDamp(action.Tool.position, position, ref action.Velocity, .06f, 4f, dt);
                }
            }
        }
    }

    void TickApproach(float dt)
    {
        if (!Approaching) return;
        if (_approachTarget < 0 || _approachTarget >= Targets.Length || Targets[_approachTarget]?.Contact == null ||
            !Targets[_approachTarget].Contact.gameObject.activeInHierarchy || !Actor.InteractionApproachPosition.HasValue ||
            Vector3.Distance(Targets[_approachTarget].Contact.position, _approachContact) > .05f ||
            Actor.Motor != null && (!Actor.Motor.Grounded || Actor.Motor.Swimming || Actor.Traversal.Active))
        { Cancel(); Status = "Approach interrupted."; return; }
        float distance = Vector3.ProjectOnPlane(Actor.InteractionApproachPosition.Value - Frame.position, Frame.up).magnitude;
        _approachElapsed += dt;
        _approachStall = distance > .05f && distance >= _approachDistance - .001f ? _approachStall + dt : 0f;
        _approachDistance = distance;
        if (_approachElapsed > 4f || _approachStall > .8f)
        { Cancel(); Status = "No clear approach. Move to another side of the object."; return; }
        if (distance > .045f || Vector3.Angle(Frame.forward, Actor.InteractionApproachForward ?? Frame.forward) > 4f) return;
        var definition = _approachDefinition; int target = _approachTarget;
        _approachDefinition = null; _approachTarget = -1;
        Actor.InteractionApproachPosition = Actor.InteractionApproachForward = null;
        BeginSequence(definition, target);
    }

    void TickSequence(float dt)
    {
        if (!_session.Active) return;
        var target = Targets[_actionTarget];
        if (target?.Contact == null || !target.Contact.gameObject.activeInHierarchy ||
            Actor.Motor != null && (!Actor.Motor.Grounded || Actor.Motor.Swimming || Actor.Traversal.Active))
        { Cancel(); return; }
        if (_held < 0 && !_seated && Vector3.ProjectOnPlane(_actionOrigin - Frame.position, Frame.up).magnitude > 1.1f)
        { Cancel(); return; }
        if (target.OpenAwayFromActor && target.HasAuthoredContact && (_session.Phase.Marker == "Open" || _session.Phase.Marker == "Close") &&
            !DoorContactFits(target, Frame.position, true))
        { Cancel(); Status = "The door contact moved outside this authored reach."; return; }
        _session.Advance(dt);
        if (!_session.Active)
        {
            Actor.View?.EndInteraction();
            Actor.InteractionMovementLocked = false;
            Actor.InteractionPosition = Actor.InteractionForward = null;
            Actor.RightInteractionTarget = Actor.LeftInteractionTarget = null;
            return;
        }
        var phase = _session.Phase;
        Actor.LookTarget = phase.Name == "Look inside" ? null : target.LookAnchor != null ? target.LookAnchor : target.Contact;
        Actor.InteractionMovementLocked = !phase.AllowMovement;
        if (phase.UseLocomotion) Actor.View?.EndInteraction();
        else Actor.View?.SetInteractionPhase(phase.Name,
            phase.ReverseAnimation ? 1f - Mathf.Clamp01(_session.Progress) : _session.Progress);
        if (Actor.View != null) Actor.View.Pose.ForwardLeanDegrees = phase.ForwardLeanDegrees;
        var approach = target.SeatApproach != null ? target.SeatApproach : target.Approach;
        if (approach != null && !_usingRear && _held < 0)
        {
            Actor.InteractionPosition = _actionOrigin;
            Actor.InteractionForward = approach.forward;
        }
        else if (target.OpenAwayFromActor)
        {
            // Hold the stance outside the initial door plane; do not chase its swinging handle.
            Actor.InteractionPosition = _actionOrigin;
            Actor.InteractionForward = _actionDoorForward;
        }
        Status = target.Label + ": " + phase.Name + (phase.WaitForInput ? " — E to continue" : "");
    }

    ProceduralPoseRig _contactPose;

    bool UsesCurrentAuthoredPose(int index) => _contactPose != null && _session.Active && _actionTarget == index && (Targets[index].MatchHingeToAuthoredHands || Targets[index].OpenAwayFromActor && Targets[index].HasAuthoredContact);

    void RefreshAuthoredContacts(float dt)
    {
        UpdateHinges(dt, true);
        if (_session.Active && _actionTarget >= 0)
            foreach (var action in Targets[_actionTarget].Actions)
            {
                var grip = action?.Grip;
                if (grip == null || grip.Secondary == null || action.Definition == null ||
                    action.Definition.Action != _session.Plan.Action || action.GripWeight <= 0f ||
                    !_contactPose.TryGetInteractionContactPose(grip.PrimaryId, out var primary) ||
                    !grip.TryFit(primary.position, primary.rotation, out var fitted) ||
                    !_contactPose.TryGetInteractionContact(grip.SecondaryId, out var support, out _)) continue;
                Vector3 target = grip.SupportPoint(support, fitted);
                target = Vector3.MoveTowards(support, target, grip.MaximumSupportCorrection);
                _contactPose.SetInteractionTarget(grip.SecondaryId, new InteractionPoseTarget(target, null, action.GripWeight,
                    useContact: true, followAuthoredMotion: true, preserveAuthoredContactJoints: true));
            }
        if (_held >= 0 && _lifted && !_placing && !_dragging && Targets[_held].LeftContact != null && Actor.View != null)
        {
            var animator = Frame.GetComponentInChildren<Animator>();
            Vector3 body = Frame.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position);
            if (!_carryBodyBound) { _carryBodyOrigin = body - _carryBodyOffset; _carryBodyBound = true; }
            Vector3 offset = body - _carryBodyOrigin;
            // Transfer the authored body's weight shifts to the supported load before solving its hand contacts.
            Targets[_held].PickupRoot.position += Frame.TransformVector(offset - _carryBodyOffset);
            _carryBodyOffset = offset;
            SetHandTargets(Targets[_held], 1f, 1f);
            _contactPose.SetInteractionTarget("RightHand", Actor.RightInteractionTarget);
            _contactPose.SetInteractionTarget("LeftHand", Actor.LeftInteractionTarget);
        }
        if (!_session.Active || !UsesCurrentAuthoredPose(_actionTarget)) return;
        SetHandTargets(Targets[_actionTarget], _session.Phase.RightHandWeight, _session.Phase.LeftHandWeight);
        _contactPose.SetInteractionTarget("RightHand", Actor.RightInteractionTarget);
        _contactPose.SetInteractionTarget("LeftHand", Actor.LeftInteractionTarget);
    }

    void UpdateHinges(float dt, bool capturedPose)
    {
        for (int i = 0; i < Targets.Length; i++)
            if (Targets[i]?.Hinge != null && (UsesCurrentAuthoredPose(i) == capturedPose))
            {
                var hingeTarget = _closed[i] * (_open[i] ? Quaternion.Euler(Targets[i].OpenEuler * _openSign[i]) : Quaternion.identity);
                float hingeSpeed = _hingeSpeeds[i];
                if (Targets[i].MatchHingeToAuthoredHands && _session.Active && _actionTarget == i &&
                    (_session.Phase.Marker == "Open" || _session.Phase.Marker == "Close") && Actor.View != null &&
                    (!Targets[i].OpenAwayFromActor || DoorContactFits(Targets[i], Frame.position, true)) &&
                    Actor.View.Pose.TryGetAuthoredInteractionContact("RightHand", out var authoredRight) &&
                    Actor.View.Pose.TryGetAuthoredInteractionContact("LeftHand", out var authoredLeft))
                {
                    var hinge = Targets[i].Hinge;
                    var contact = Targets[i].LeftContact != null ? (Targets[i].Contact.position + Targets[i].LeftContact.position) * .5f : Targets[i].Contact.position;
                    var authoredContact = Targets[i].LeftContact != null ? (authoredRight + authoredLeft) * .5f : authoredRight;
                    if (Targets[i].OpenAwayFromActor) authoredContact += Frame.forward * Targets[i].PalmSurfaceOffset * Frame.lossyScale.x;
                    Targets[i].AuthoredHingeLever ??= Quaternion.Inverse(hinge.rotation) * ((Targets[i].OpenAwayFromActor ? authoredContact : contact) - hinge.position);
                    var lever = Targets[i].AuthoredHingeLever.Value;
                    var closedWorld = (hinge.parent != null ? hinge.parent.rotation : Quaternion.identity) * _closed[i];
                    var authoredLever = Quaternion.Inverse(closedWorld) * (authoredContact - hinge.position);
                    hingeTarget = FitAuthoredHinge(_closed[i], Targets[i].OpenEuler * _openSign[i], lever, authoredLever);
                    if (Targets[i].OpenAwayFromActor && _session.Phase.Marker == "Open" &&
                        Quaternion.Angle(_closed[i], hingeTarget) < Quaternion.Angle(_closed[i], hinge.localRotation))
                        hingeTarget = hinge.localRotation;
                    // Authored lift accelerates within the phase; its average speed is not its peak speed.
                    hingeSpeed = Mathf.Max(hingeSpeed, 540f);
                }
                Targets[i].Hinge.localRotation = Quaternion.RotateTowards(Targets[i].Hinge.localRotation, hingeTarget, hingeSpeed * dt);
                if (Targets[i].MatchHingeToAuthoredHands && _session.Active && _actionTarget == i && Actor.View != null &&
                    (_session.Phase.Marker == "Open" || _session.Phase.Marker == "Close"))
                {
                    if (Actor.View.Pose.TryGetAuthoredInteractionContact("RightHand", out var palmRight))
                    {
                        if (!Targets[i].OpenAwayFromActor) Targets[i].Contact.position = ClosestLidPoint(Targets[i], palmRight);
                        else if (TryProjectContactSurface(Targets[i], palmRight - Frame.forward * .2f, Frame.forward, out var surface, out _))
                            Targets[i].Contact.position = surface;
                    }
                    if (Targets[i].LeftContact != null && Actor.View.Pose.TryGetAuthoredInteractionContact("LeftHand", out var palmLeft))
                        Targets[i].LeftContact.position = ClosestLidPoint(Targets[i], palmLeft);
                }
                if (Targets[i].OpenOffset != Vector3.zero)
                    Targets[i].Hinge.localPosition = Vector3.MoveTowards(Targets[i].Hinge.localPosition,
                        _closedPositions[i] + (_open[i] ? Targets[i].OpenOffset : Vector3.zero),
                        Targets[i].OpenOffset.magnitude / Mathf.Max(.01f, Targets[i].SlideSeconds) * dt);
            }
    }

    void SetHandTargets(Target target, float rightWeight, float leftWeight)
    {
        var contacts = FindContacts(target, _session.Phase?.ContactSet);
        bool sourceRotation = (target.UseSourceHandRotation || target.OpenAwayFromActor) && contacts?.MatchRotation != true;
        var right = contacts != null ? contacts.Right : target.Contact;
        var left = contacts != null ? contacts.Left : target.LeftContact;
        Vector3 palmOffset = target.OpenAwayFromActor ? Frame.forward * target.PalmSurfaceOffset * Frame.lossyScale.x : Vector3.zero;
        if ((target.MatchHingeToAuthoredHands || target.OpenAwayFromActor) && Actor.View != null)
        {
            if (right != null && Actor.View.Pose.TryGetAuthoredInteractionContact("RightHand", out var authoredRight))
                rightWeight *= AuthoredContactWeight(Vector3.Distance(authoredRight + palmOffset, right.position), target.MaxContactCorrection);
            if (left != null && Actor.View.Pose.TryGetAuthoredInteractionContact("LeftHand", out var authoredLeft))
                leftWeight *= AuthoredContactWeight(Vector3.Distance(authoredLeft, left.position), target.MaxContactCorrection);
        }
        Vector3 hingeOffset = Vector3.zero;
        if (contacts == null && target.Hinge != null && target.OpenHandOffset != Vector3.zero)
        {
            int index = Array.IndexOf(Targets, target);
            float travel = Quaternion.Angle(Quaternion.identity, Quaternion.Euler(target.OpenEuler));
            if (index >= 0 && travel > .001f)
                hingeOffset = target.Hinge.root.TransformVector(target.OpenHandOffset) *
                    Mathf.Clamp01(Quaternion.Angle(_closed[index], target.Hinge.localRotation) / travel);
        }
        Actor.RightInteractionTarget = right != null && rightWeight > 0f ? new InteractionPoseTarget(right.position + hingeOffset - palmOffset,
            sourceRotation ? null : contacts == null && target.HandOrientation != null ? target.HandOrientation.rotation :
                target.MatchContactRotation || contacts?.MatchRotation == true ? right.rotation : Quaternion.LookRotation(-Frame.right, Frame.forward),
            rightWeight, useContact: true, gripRadius: sourceRotation ? 0f : _session.Phase?.GripRadius ?? 0f,
            bendDirection: !target.OpenAwayFromActor && target.RightElbowDirection != Vector3.zero ? Frame.TransformDirection(target.RightElbowDirection) : null,
            followAuthoredMotion: target.MatchHingeToAuthoredHands && !target.OpenAwayFromActor) : null;
        Actor.LeftInteractionTarget = left != null && leftWeight > 0f ? new InteractionPoseTarget(left.position + hingeOffset,
            sourceRotation ? null : contacts == null && target.HandOrientation != null ? target.HandOrientation.rotation :
                target.MatchContactRotation || contacts?.MatchRotation == true ? left.rotation : Quaternion.LookRotation(Frame.right, Frame.forward),
            leftWeight, useContact: true, gripRadius: sourceRotation ? 0f : _session.Phase?.GripRadius ?? 0f,
            bendDirection: !target.OpenAwayFromActor && target.LeftElbowDirection != Vector3.zero ? Frame.TransformDirection(target.LeftElbowDirection) : null,
            followAuthoredMotion: target.MatchHingeToAuthoredHands && !target.OpenAwayFromActor) : null;
    }

    static void EnsureContactSurface(Target target)
    {
        if (target.LidVertices == null || target.LidVertices.Length == 0 || target.LidTriangles == null || target.LidTriangles.Length == 0)
        {
            var mesh = target.Hinge.GetComponent<MeshFilter>().sharedMesh;
            if (!mesh.isReadable) throw new InvalidOperationException("Author the lid contact surface before runtime mesh data is discarded.");
            target.LidVertices = mesh.vertices;
            target.LidTriangles = mesh.triangles;
            if (target.LidVertices.Length == 0 || target.LidTriangles.Length == 0)
                throw new InvalidOperationException("The lid contact surface has no triangles.");
        }
    }

    public static bool TryProjectContactSurface(Target target, Vector3 origin, Vector3 direction, out Vector3 point, out Vector3 normal)
    {
        if (!CharacterMath.IsFinite(origin) || !CharacterMath.IsFinite(direction) || direction.sqrMagnitude < 1e-10f)
            throw new ArgumentException("Surface projection requires a finite origin and nonzero direction.");
        EnsureContactSurface(target);
        point = normal = Vector3.zero;
        float nearest = float.PositiveInfinity;
        direction.Normalize();
        for (int i = 0; i < target.LidTriangles.Length; i += 3)
        {
            Vector3 a = target.Hinge.TransformPoint(target.LidVertices[target.LidTriangles[i]]);
            Vector3 b = target.Hinge.TransformPoint(target.LidVertices[target.LidTriangles[i + 1]]);
            Vector3 c = target.Hinge.TransformPoint(target.LidVertices[target.LidTriangles[i + 2]]);
            Vector3 n = Vector3.Cross(b - a, c - a);
            float denominator = Vector3.Dot(n, direction);
            if (denominator >= -1e-8f) continue;
            float distance = Vector3.Dot(n, a - origin) / denominator;
            if (distance < 0f || distance >= nearest) continue;
            Vector3 hit = origin + direction * distance;
            if (Vector3.Dot(Vector3.Cross(b - a, hit - a), n) < -1e-8f ||
                Vector3.Dot(Vector3.Cross(c - b, hit - b), n) < -1e-8f ||
                Vector3.Dot(Vector3.Cross(a - c, hit - c), n) < -1e-8f) continue;
            nearest = distance; point = hit; normal = n.normalized;
        }
        return float.IsFinite(nearest);
    }

    public static Vector3 ClosestLidPoint(Target target, Vector3 palm)
    {
        EnsureContactSurface(target);
        Vector3 best = target.Contact.position;
        float distance = float.PositiveInfinity;
        void Consider(Vector3 point)
        {
            float squared = (point - palm).sqrMagnitude;
            if (squared < distance) { distance = squared; best = point; }
        }
        void Edge(Vector3 a, Vector3 b)
        {
            var edge = b - a;
            Consider(a + edge * Mathf.Clamp01(Vector3.Dot(palm - a, edge) / Mathf.Max(edge.sqrMagnitude, 1e-12f)));
        }
        for (int i = 0; i < target.LidTriangles.Length; i += 3)
        {
            var a = target.Hinge.TransformPoint(target.LidVertices[target.LidTriangles[i]]);
            var b = target.Hinge.TransformPoint(target.LidVertices[target.LidTriangles[i + 1]]);
            var c = target.Hinge.TransformPoint(target.LidVertices[target.LidTriangles[i + 2]]);
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude > 1e-12f)
            {
                var projected = palm - normal * (Vector3.Dot(palm - a, normal) / normal.sqrMagnitude);
                if (Vector3.Dot(Vector3.Cross(b - a, projected - a), normal) >= 0f &&
                    Vector3.Dot(Vector3.Cross(c - b, projected - b), normal) >= 0f &&
                    Vector3.Dot(Vector3.Cross(a - c, projected - c), normal) >= 0f) Consider(projected);
            }
            Edge(a, b); Edge(b, c); Edge(c, a);
        }
        return best;
    }

    public static Quaternion FitAuthoredHinge(Quaternion closed, Vector3 openEuler, Vector3 contactLever, Vector3 authoredLever)
    {
        Quaternion.Euler(openEuler).ToAngleAxis(out float limit, out var axis);
        if (limit > 180f) { limit = 360f - limit; axis = -axis; }
        var contact = Vector3.ProjectOnPlane(contactLever, axis);
        var authored = Vector3.ProjectOnPlane(authoredLever, axis);
        if (limit < .001f || contact.sqrMagnitude < .000001f || authored.sqrMagnitude < .000001f) return closed;
        float angle = Mathf.Clamp(Vector3.SignedAngle(contact, authored, axis), 0f, limit);
        return closed * Quaternion.AngleAxis(angle, axis);
    }

    public static float AuthoredContactWeight(float error, float allowance)
    {
        if (!float.IsFinite(error) || error < 0f || !float.IsFinite(allowance) || allowance <= 0f)
            throw new ArgumentOutOfRangeException(nameof(error));
        // Keep the real prop contact. The shared hand blend releases it when the source needs excessive adaptation.
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(allowance * .5f, allowance, error));
    }

    static HandContacts FindContacts(Target target, string id)
    {
        if (string.IsNullOrEmpty(id) || target.ContactSets == null) return null;
        foreach (var contacts in target.ContactSets)
            if (contacts != null && contacts.Id == id) return contacts;
        return null;
    }

    public void ResetRoom()
    {
        _approachDefinition = null; _approachTarget = -1;
        Actor.InteractionApproachPosition = Actor.InteractionApproachForward = null;
        InitializeTargets();
        _session.Cancel(); _seated = false;
        Actor.View?.EndInteraction();
        Actor.InteractionMovementLocked = false;
        Actor.InteractionPosition = Actor.InteractionForward = null;
        ReleaseHeld();
        // Explicit diagnostic reset restores authored poses immediately, outside normal interaction transitions.
        Actor.Reach = false;
        for (int i = 0; i < Targets.Length; i++)
        {
            _open[i] = false;
            Targets[i]?.Campfire?.ResetState();
            if (Targets[i] != null) { Targets[i].Shelved = false; Targets[i].Shelf?.ResetSlots(); }
            if (Targets[i]?.Hinge != null)
            {
                Targets[i].Hinge.localRotation = _closed[i];
                Targets[i].Hinge.localPosition = _closedPositions[i];
            }
            if (Targets[i]?.PickupRoot != null) Targets[i].PickupRoot.SetPositionAndRotation(_homePositions[i], _homeRotations[i]);
            foreach (var action in Targets[i]?.Actions ?? Array.Empty<StationAction>())
                if (action?.Tool != null && action.ToolRest != null)
                {
                    action.Tool.SetPositionAndRotation(action.ToolRest.position, action.ToolRest.rotation); action.Velocity = Vector3.zero;
                    action.ToolHeld = false;
                    if (action.Flow != null) action.Flow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
        }
        if (Selected >= 0) Select(Selected); else Actor.ResetActor();
    }

    void Update()
    {
        RefreshTarget();
        var keyboard = Keyboard.current;
        if (Application.isFocused && keyboard != null)
        {
            if (keyboard.eKey.wasPressedThisFrame) Interact();
            if (keyboard.rKey.wasPressedThisFrame) ResetRoom();
            if (keyboard.escapeKey.wasPressedThisFrame) Cancel();
        }
        if (Actor != null && Actor.View == null) Tick(Time.deltaTime);
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12, 12, 325, Mathf.Min(Screen.height - 24, 650)), GUI.skin.box);
        _scroll = GUILayout.BeginScrollView(_scroll);
        GUILayout.Label("HUMAN / INTERACTIONS");
        GUILayout.Label("WASD move · Shift run · RMB look · Tab free camera");
        GUILayout.Label("Ctrl crouch · E use / pick up / place · Escape cancel · R reset");
        GUILayout.Label(Status);
        if (GUILayout.Button(_held >= 0 ? "Use / place held item" : "Interact with nearby object")) Interact();
        GUILayout.Label(Selected >= 0 ? "Target: " + Targets[Selected].Label : "Move closer and face an object");
        for (int i = 0; i < Targets.Length; i++)
            if (GUILayout.Button("Visit " + Targets[i].Label)) Select(i);
        if (Selected >= 0)
        {
            var target = Targets[Selected];
            GUILayout.Space(8);
            GUILayout.Label(target.Task);
            if (target.Actions != null)
                for (int action = 0; action < target.Actions.Length; action++)
                    if (target.Actions[action]?.Definition != null && GUILayout.Button(target.Actions[action].Definition.Action))
                        UseStationAction(action);
            Actor.Reach = GUILayout.Toggle(Actor.Reach, "Reach toward contact");
            Actor.Crouch = GUILayout.Toggle(Actor.Crouch, "Crouch");
            if (Actor.Reach && Actor.View != null && Actor.View.Pose.TryGetInteractionContact("RightHand", out _, out bool reached))
                GUILayout.Label(reached ? "Hand contact reached" : "Outside current hand pose limits");
            if (target.Hinge != null && GUILayout.Button("Preview open / close")) ToggleProp();
            if (target.Collect != null && _session.Active && _session.Phase.WaitForInput &&
                _actionTarget == Selected && _open[Selected] && GUILayout.Button("Collect from container")) Collect();
        }
        if (GUILayout.Button("Reset room")) ResetRoom();
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}












