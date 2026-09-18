using UnityEngine;

/// <summary>Shared humanoid movement, traversal, and presentation driven by actor intent and environment providers.</summary>
public abstract class HumanoidActorController : MonoBehaviour, IGroundingProvider, IGravityProvider, ISwimmingProvider
{
    public GameObject CharacterPrefab;
    public bool ShowControls = true;
    [Range(0f, .4f)] public float TorsoTurnResponse = .25f;
    public bool EnableWater = true;
    public AnimationClip Idle, Walk, Run;
    public AnimationClip SurfaceIdle, SurfaceSwim, UnderwaterIdle, UnderwaterSwim;
    public AnimationClip[] SwimDirectional;
    public AnimationClip Wade;
    public AnimationClip[] WalkDiagonals;
    public AnimationClip[] RunDirectionalClips;
    public AnimationClip FastSurfaceSwim;
    [Min(.1f)] public float SwimSpeed = 1f;
    [Min(.1f)] public float FastSwimSpeed = 2.5f;
    public AnimationClip[] TraversalClips;
    public ActorTraversalMotionAsset VaultMotion;
    public ActorTraversalMotionAsset StepUpMotion;
    public ActorTraversalMotionAsset JumpGrabMotion;
    public ActorTraversalMotionAsset HangLeftMotion, HangRightMotion;
    public ActorTraversalMotionAsset[] LedgeCornerMotions;
    public AnimationClip[] StairClips;
    public AnimationClip[] CrawlTransitions;
    public AnimationClip CrawlBackward;
    public AnimationClip[] CrawlSideways;
    public Vector4 CrawlCycleDistances = new(.9033507f, .506274f, .5306829f, .9113607f);
    public AnimationClip[] DirectionalClips;
    public AnimationClip Fall;
    public ActorAnimationPerformanceLibrary Performances;
    public ActorAnimationPerformanceLibrary LadderPerformances;
    public ActorAnimationPerformanceLibrary BeamPerformances;
    public ActorAnimationPerformanceLibrary LedgeWalkPerformances;
    public ActorAnimationPerformanceLibrary RopePerformances;
    public RopeInteraction[] Ropes = System.Array.Empty<RopeInteraction>();
    public ActorRope Rope { get; private set; }
    public BeamInteraction[] Beams = System.Array.Empty<BeamInteraction>();
    public ActorBeam Beam { get; private set; }
    public ActorTraversalMotionAsset[] DodgeMotions = System.Array.Empty<ActorTraversalMotionAsset>();
    public ActorDodge Dodge { get; private set; }
    bool _beamTurnHeld;
    public LadderInteraction[] Ladders = System.Array.Empty<LadderInteraction>();
    public ActorInteractionDefinition[] Interactions = System.Array.Empty<ActorInteractionDefinition>();
    [Range(0f, 1f)] public float Proficiency;
    public bool Crouch, Crawl;
    public Transform[] Stations;
    public bool RequestJump;
    public ActorCollision Collision { get; private set; }
    public ActorTraversal Traversal { get; private set; }
    public ActorLadder Ladder { get; private set; }
    public bool LadderApproaching => _ladderApproaching;
    public float LadderEntrySupportSpeed { get; private set; }
    bool _ladderSupportSampled;
    Vector3 _ladderLeftSupport, _ladderRightSupport;
    public string LadderStatus { get; private set; }
    LadderInteraction _ladderTarget;
    protected bool _ladderApproaching, _ladderFromTop, _ladderSprintTop;
    Vector3 _ladderPosition;
    Quaternion _ladderRotation;
    float _ladderHeight, _ladderSpacing, _ladderApproachTime;
    protected Vector2 _scroll;
    public bool Dive, SwimUp;
    public bool FollowActor = true;
    [Min(0f)] public float LookSensitivity = .12f;
    [Min(.5f)] public float CameraDistance = 4f;
    protected Camera _camera;
    protected WorkbenchFlyCamera _flyCamera;
    protected bool _flyWasEnabled, _following, _cursorLocked, _lookReleased;
    public ActorThirdPersonCamera ThirdPersonCamera { get; private set; }
    [Min(.1f)] public float WalkSpeed = 1.4f;
    [Min(.2f)] public float RunSpeed = 4f;
    [Min(.1f)] public float JumpHeight = .75f;
    [Min(.1f)] public float StandingHopHeight = .3f;
    public bool WalkLoop, Running, Feet = true, Gaze = true, Reach;
    public Transform LookTarget, HandTarget;
    public InteractionPoseTarget? RightInteractionTarget { get; set; }
    public InteractionPoseTarget? LeftInteractionTarget { get; set; }
    public bool InteractionCrouch { get; set; }
    public bool InteractionMovementLocked { get; set; }
    public Vector3? InteractionPosition { get; set; }
    public Vector3? InteractionForward { get; set; }
    public Vector3? InteractionApproachPosition { get; set; }
    public Vector3? InteractionApproachForward { get; set; }
    float _approachSpeed;
    public event System.Action<float> PrepareInteraction;
    public event System.Action<float> PresentInteraction;
    public Transform Panel;
    protected readonly ActorReachMotion _reachMotion = new();
    Transform _rightHand;
    float _panelStart, _panelEnd;
    public ActorReachMotion ReachMotion => _reachMotion;
    public int PanelContacts { get; private set; }
    protected GameObject _actor;
    protected HumanoidAnimationView _view;
    protected SurfaceCharacterController _motor;
    float _handWeight, _time;
    Quaternion _gripRotation = Quaternion.identity;
    bool _dropHeld;
    float _previousHangMove;
    bool _pendingHangClimb;
    float _hopPreparation = -1f;
    const float HopPreparationSeconds = .18f;
    public HumanoidAnimationView View => _view;
    public Transform Actor => _actor != null ? _actor.transform : null;
    public SurfaceCharacterController Motor => _motor;

    protected virtual void OnEnable() => Initialize();
    protected virtual void OnActorInitialized() { }
    public abstract bool TryGetGravity(Vector3 position, out Vector3 acceleration);
    public abstract bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult result);
    public abstract bool TryGetDepth(Vector3 position, out float signedDepth, out float bodyDepth);

    public void Initialize()
    {
        if (_view != null || CharacterPrefab == null || Idle == null || Walk == null || Run == null) return;
        _actor = Instantiate(CharacterPrefab, transform);
        _actor.name = CharacterPrefab.name + " actor";
        try
        {
            var animator = _actor.GetComponentInChildren<Animator>();
            _rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var rig = animator.GetComponent<ProceduralRigDefinition>();
            if (rig == null) rig = animator.gameObject.AddComponent<ProceduralRigDefinition>();
            HumanoidRigBinding.Bind(animator, rig);
            if (LadderPerformances != null) LadderInteraction.BindFootContacts(rig);
            var vaultMotion = VaultMotion != null ? VaultMotion.CreateMotion() : null;
            var jumpGrabMotion = JumpGrabMotion != null ? JumpGrabMotion.CreateMotion() : null;
            var stepUpMotion = StepUpMotion != null ? StepUpMotion.CreateMotion() : null;
            if (StepUpMotion != null) TraversalClips[8] = StepUpMotion.Clip;
            if (VaultMotion != null) TraversalClips[5] = VaultMotion.Clip;
            _view = new HumanoidAnimationView(animator, rig, Idle, Walk, Run, WalkSpeed, RunSpeed,
                SurfaceIdle != null ? new[] { SurfaceIdle, SurfaceSwim, UnderwaterIdle, UnderwaterSwim } : null,
                TraversalClips != null && TraversalClips.Length > 0 ? TraversalClips : null,
                DirectionalClips != null && DirectionalClips.Length > 0 ? DirectionalClips : null, Fall, PerformanceLibrary(),
                SwimDirectional != null && SwimDirectional.Length > 0 ? SwimDirectional : null, Wade,
                WalkDiagonals != null && WalkDiagonals.Length > 0 ? WalkDiagonals : null, FastSurfaceSwim,
                CrawlTransitions != null && CrawlTransitions.Length > 0 ? CrawlTransitions : null, CrawlBackward,
                CrawlSideways != null && CrawlSideways.Length > 0 ? CrawlSideways : null, CrawlCycleDistances,
                vaultMotion, VaultMotion != null ? VaultMotion.SupportContact : "RightHand",
                StairClips != null && StairClips.Length > 0 ? StairClips : null,
                RunDirectionalClips != null && RunDirectionalClips.Length > 0 ? RunDirectionalClips : null, stepUpMotion: stepUpMotion, jumpGrabClip: JumpGrabMotion != null ? JumpGrabMotion.Clip : null, jumpGrabMotion: jumpGrabMotion, hangLeft: HangLeftMotion, hangRight: HangRightMotion, ledgeCorners: LedgeCornerMotions);
            _view.Pose.BeforeCorrections += RefreshLadderContacts;
            Collision = new ActorCollision(1 << 0);
            Traversal = new ActorTraversal(Collision);
            Ladder = new ActorLadder(Collision);
            Beam = new ActorBeam(Collision);
            Dodge = new ActorDodge(Collision);
            Rope = new ActorRope(Collision);
            Traversal.VaultMotion = vaultMotion;
            Traversal.StepUpMotion = stepUpMotion;
            Traversal.JumpGrabMotion = jumpGrabMotion;
            Traversal.HangLeftMotion = HangLeftMotion != null ? HangLeftMotion.CreateMotion() : null;
            Traversal.HangRightMotion = HangRightMotion != null ? HangRightMotion.CreateMotion() : null;
            if (LedgeCornerMotions != null && LedgeCornerMotions.Length == 4)
                Traversal.CornerMotions = System.Array.ConvertAll(LedgeCornerMotions, asset => asset != null ? asset.CreateMotion() : null);
            _motor = new SurfaceCharacterController(this, this, 0f,
                new CharacterPose(transform.position, transform.up, transform.forward), this,
                new SurfaceSwimProfile(1.25f, 1.45f, .2f, holdDiveDepth: true, wadingSpeedMultiplier: .5f), Collision);
            ThirdPersonCamera = new ActorThirdPersonCamera(1 << 0);
            ThirdPersonCamera.Reset(_motor.Pose);
            OnActorInitialized();
        }
        catch { OnDisable(); throw; }
    }


    ActorAnimationPerformanceLibraryData PerformanceLibrary()
    {
        if (Interactions.Length == 0 && LadderPerformances == null && BeamPerformances == null && LedgeWalkPerformances == null && RopePerformances == null) return Performances?.Snapshot();
        var entries = new System.Collections.Generic.List<ActorAnimationPerformanceLibrary.Entry>();
        if (Performances != null) entries.AddRange(Performances.Entries);
        if (LadderPerformances != null) entries.AddRange(LadderPerformances.Entries);
        if (BeamPerformances != null) entries.AddRange(BeamPerformances.Entries);
        if (LedgeWalkPerformances != null) entries.AddRange(LedgeWalkPerformances.Entries);
        if (RopePerformances != null) entries.AddRange(RopePerformances.Entries);
        foreach (var definition in Interactions) entries.Add(definition.Snapshot().Performance);
        return new ActorAnimationPerformanceLibraryData(entries.ToArray());
    }

    public void Step(float dt) => Step(dt, default(ActorIntent));

    public bool TryUseLadder(LadderInteraction target, bool fromTop = false, bool fast = false)
    {
        if (target != null && target.isActiveAndEnabled &&
            !target.TryValidate(LadderPerformances?.Snapshot().Select("Ladder", "Humanoid"), out var invalidLadder))
        { LadderStatus = invalidLadder; return false; }
        if (target == null || !target.isActiveAndEnabled || LadderPerformances == null ||
            _view == null || Dodge.Active || Rope.Active || Beam.Active || Traversal.Active || _motor.Swimming || !_motor.Grounded || InteractionMovementLocked ||
            Vector3.Angle(target.transform.up, _motor.Pose.Up) > 1f)
        { LadderStatus = "Approach a valid ladder on foot with clear standing space."; return false; }
        bool sprintTop = fast && !fromTop && target.SprintTopAvailable;
        Vector3 approach = sprintTop ? target.SprintBottomApproach : fromTop ? target.TopApproach : target.BottomApproach;
        if (Vector3.Distance(_motor.Pose.Position, approach) > 2.5f)
        { LadderStatus = "Move within 2.5 metres of the ladder entrance."; return false; }
        CancelLadder();
        _ladderTarget = target; _ladderFromTop = fromTop; _ladderApproaching = true; _ladderApproachTime = 0f;
        _ladderSprintTop = sprintTop;
        _ladderSupportSampled = false; LadderEntrySupportSpeed = 0f;
        _ladderPosition = target.transform.position; _ladderRotation = target.transform.rotation;
        _ladderHeight = target.Height; _ladderSpacing = target.RungSpacing;
        InteractionApproachPosition = approach;
        InteractionApproachForward = fromTop ? target.TopApproachForward : target.transform.forward;
        LadderStatus = "Approaching ladder through ordinary locomotion.";
        return true;
    }

    void RefreshLadderContacts(float dt)
    {
        if (Rope.Active && Rope.Target != null) Rope.Target.ApplyContacts(_view.Pose, Rope);
        if (_ladderTarget == null || _ladderApproaching) return;
        Vector3 fit = _ladderTarget.ApplyAuthoredContacts(_view.Pose, Ladder, dt);
        if (fit.sqrMagnitude > 0f)
        {
            _motor.ResetPose(Ladder.Pose, grounded: false);
            _actor.transform.SetPositionAndRotation(Ladder.Pose.Position, Quaternion.LookRotation(Ladder.Pose.Forward, Ladder.Pose.Up));
        }
    }

    public void CancelLadder()
    {
        if (_ladderTarget == null && !_ladderApproaching && (Ladder == null || !Ladder.Active)) return;
        bool mounted = Ladder != null && Ladder.Active;
        Ladder?.Cancel();
        if (_ladderApproaching) InteractionApproachPosition = InteractionApproachForward = null;
        _ladderApproaching = false; _ladderSprintTop = false;
        if (_view != null && mounted)
        {
            _view.EndInteraction();
            if (_ladderTarget != null) _ladderTarget.ReleaseContacts(_view.Pose);
            else foreach (string contact in new[] { "LeftHand", "RightHand", "LadderLeftFoot", "LadderRightFoot" })
                _view.Pose.SetInteractionTarget(contact, null);
        }
        _ladderTarget = null;
        LadderStatus = "Ladder released; ordinary movement and gravity resumed.";
    }

    public bool TryUseBeam(BeamInteraction target)
    {
        if (target == null || Dodge.Active || (target.WallSideWalk ? LedgeWalkPerformances : BeamPerformances) == null || Rope.Active || Beam.Active || Traversal.Active || Ladder.Active || InteractionMovementLocked || _motor.Swimming || !_motor.Grounded) return false;
        if (!Beam.Begin(target, _motor.Pose)) return false;
        if (_view.BeginInteraction(target.WallSideWalk ? "LedgeWalk" : "Beam")) return true;
        Beam.Cancel(); return false;
    }

    public bool TryDodge(int direction)
    {
        if (_view == null || Dodge.Active || direction < 0 || direction >= DodgeMotions.Length ||
            DodgeMotions[direction] == null || !_motor.Grounded || _motor.Swimming || Rope.Active ||
            Beam.Active || Ladder.Active || _ladderApproaching || Traversal.Active || InteractionMovementLocked ||
            Collision.Stance != ActorStance.Standing || _reachMotion.Active || InteractionApproachPosition.HasValue) return false;
        if (!Dodge.Begin(DodgeMotions[direction].CreateMotion(), direction, _motor.Pose,
            DodgeMotions[direction].LocomotionExitNormalized)) return false;
        if (_view.BeginInteraction("Dodge")) return true;
        Dodge.Reset(); return false;
    }

    public bool TryUseRope(RopeInteraction target)
    {
        if (RopePerformances == null || Dodge.Active || Rope.Active || Beam.Active || Ladder.Active || Traversal.Active || InteractionMovementLocked || !_motor.Grounded || _motor.Swimming) return false;
        if (!Rope.Begin(target, _motor.Pose)) return false;
        if (_view.BeginInteraction("Rope")) return true;
        Rope.Cancel(); return false;
    }

    public void Step(float dt, ActorIntent intent)
    {
        if (_view == null || dt <= 0f) return;
        if (intent.Held(ActorButtons.ToggleCrawl)) { Crawl = !Crawl; Crouch = false; }
        if (intent.Held(ActorButtons.Dodge))
            TryDodge(intent.Move.y > .25f && DodgeMotions.Length > 3 ? 3 : intent.Move.x < -.25f ? 0 : intent.Move.x > .25f ? 1 : 2);
        if (intent.Held(ActorButtons.Cancel))
        {
            Dodge.RequestStop();
            if (_ladderApproaching || Ladder.Active) CancelLadder();
        }
        if (intent.Move.sqrMagnitude > .01f && Dodge.TryResumeLocomotion()) _view.EndInteraction();
        bool wasDodging = Dodge.Active;
        if (wasDodging) intent = new ActorIntent(Vector2.zero, intent.Look, ActorButtons.None, 0);
        bool wasOnRope = Rope.Active;
        bool ropeInput = false;
        if (intent.Held(ActorButtons.Interact))
        {
            if (Rope.Active) { Rope.Cancel(); _view.EndInteraction(); ropeInput = true; }
            else foreach (var target in Ropes)
                if (target != null && TryUseRope(target)) { ropeInput = true; break; }
        }
        if (Rope.Active && (intent.Held(ActorButtons.Jump) || intent.Held(ActorButtons.Crouch)))
        { Rope.Cancel(); _view.EndInteraction(); }
        bool beamInput = ropeInput;
        if (intent.Held(ActorButtons.Interact) && !ropeInput)
        {
            if (Beam.Active) { Beam.Cancel(); _view.EndInteraction(); beamInput = true; }
            else foreach (var target in Beams)
                if (target != null && target.Valid && TryUseBeam(target)) { beamInput = true; break; }
        }
        bool wasOnBeam = Beam.Active;
        if (wasOnBeam && (intent.Held(ActorButtons.Jump) || intent.Held(ActorButtons.Crouch))) { Beam.Cancel(); _view.EndInteraction(); }
        if (intent.Held(ActorButtons.Interact) && !beamInput)
        {
            if (Ladder.Active || _ladderApproaching) CancelLadder();
            else
            {
                LadderInteraction nearest = null; bool top = false; float distance = 2.5f;
                foreach (var ladder in Ladders)
                {
                    if (ladder == null || !ladder.isActiveAndEnabled) continue;
                    float bottomDistance = Vector3.Distance(_motor.Pose.Position,
                        intent.Held(ActorButtons.Sprint) && ladder.SprintTopAvailable ? ladder.SprintBottomApproach : ladder.BottomApproach);
                    float topDistance = Vector3.Distance(_motor.Pose.Position, ladder.TopApproach);
                    float candidate = Mathf.Min(bottomDistance, topDistance);
                    if (candidate < distance) { nearest = ladder; top = topDistance < bottomDistance; distance = candidate; }
                }
                if (nearest != null) TryUseLadder(nearest, top, intent.Held(ActorButtons.Sprint));
            }
        }
        if (_ladderTarget != null && (!_ladderTarget.isActiveAndEnabled || !_ladderTarget.GeometryValid ||
            Vector3.Distance(_ladderPosition, _ladderTarget.transform.position) > .001f ||
            Quaternion.Angle(_ladderRotation, _ladderTarget.transform.rotation) > .1f ||
            _ladderTarget.Height != _ladderHeight || _ladderTarget.RungSpacing != _ladderSpacing)) CancelLadder();
        if (_ladderTarget == null && (Ladder.Active || _ladderApproaching)) CancelLadder();
        if (_ladderApproaching && _ladderSprintTop && !intent.Held(ActorButtons.Sprint))
        {
            _ladderSprintTop = false;
            InteractionApproachPosition = _ladderTarget.BottomApproach;
            InteractionApproachForward = _ladderTarget.transform.forward;
            _ladderApproachTime = 0f;
        }
        if (_ladderApproaching && (_ladderApproachTime += dt) > 8f)
        { CancelLadder(); LadderStatus = "Ladder approach is blocked; choose a clear entrance."; }
        if (InteractionApproachPosition.HasValue && (intent.Move.sqrMagnitude > .01f || intent.Held(ActorButtons.Jump)))
            InteractionApproachPosition = InteractionApproachForward = null;
        if (_ladderApproaching && !InteractionApproachPosition.HasValue) CancelLadder();
        // The authored final step completes a walking arrival with the mount's staggered support.
        // Entrances without that step must finish their locomotion stop before transferring authority.
        bool authoredArrival = _ladderApproaching && !_ladderFromTop && (_ladderSprintTop || _ladderTarget.ApproachStepMotion != null);
        if (_ladderApproaching && _motor.Grounded && (authoredArrival || (_approachSpeed <= .01f && _view.Speed < .025f &&
            _ladderSupportSampled && LadderEntrySupportSpeed < .04f)) &&
            Vector3.ProjectOnPlane(_motor.Pose.Position - InteractionApproachPosition.Value, _motor.Pose.Up).magnitude < .04f &&
            Vector3.Angle(_motor.Pose.Forward, InteractionApproachForward.Value) < 4f)
        {
            _ladderApproaching = false; InteractionApproachPosition = InteractionApproachForward = null;
            if (Ladder.Begin(_motor.Pose, _ladderTarget.Bottom, _ladderTarget.transform.up, _ladderTarget.transform.forward,
                _ladderTarget.Height, _ladderTarget.RungSpacing, _ladderFromTop,
                LadderPerformances.Snapshot().Select("Ladder", "Humanoid"), _ladderTarget.RungsPerCycle,
                _ladderTarget.TopExitMotion != null ? _ladderTarget.TopExitMotion.CreateMotion() : null,
                _ladderTarget.TopMountMotion != null ? _ladderTarget.TopMountMotion.CreateMotion() : null,
                _ladderTarget.BottomMountMotion != null ? _ladderTarget.BottomMountMotion.CreateMotion() : null,
                _ladderTarget.ApproachStepMotion != null ? _ladderTarget.ApproachStepMotion.CreateMotion() : null,
                _ladderTarget.BottomExitMotion != null ? _ladderTarget.BottomExitMotion.CreateMotion() : null,
                _ladderTarget.UpGaitMotion != null ? _ladderTarget.UpGaitMotion.CreateMotion() : null,
                _ladderTarget.DownGaitMotion != null ? _ladderTarget.DownGaitMotion.CreateMotion() : null,
                topMountSupportOffset: _ladderTarget.TopMountSupportOffset,
                topExitMirrored: _ladderTarget.TopExitMirroredMotion != null ? _ladderTarget.TopExitMirroredMotion.CreateMotion() : null,
                slideStart: _ladderTarget.SlideStartMotion != null ? _ladderTarget.SlideStartMotion.CreateMotion() : null,
                slide: _ladderTarget.SlideMotion != null ? _ladderTarget.SlideMotion.CreateMotion() : null,
                slideEnd: _ladderTarget.SlideEndMotion != null ? _ladderTarget.SlideEndMotion.CreateMotion() : null,
                sprintUp: _ladderTarget.SprintLeftMotion != null ? _ladderTarget.SprintLeftMotion.CreateMotion() : null,
                sprintUpRight: _ladderTarget.SprintRightMotion != null ? _ladderTarget.SprintRightMotion.CreateMotion() : null,
                bottomExitMirrored: _ladderTarget.BottomExitMirroredMotion != null ? _ladderTarget.BottomExitMirroredMotion.CreateMotion() : null,
                sprintPlaybackRate: _ladderTarget.SprintPlaybackRate,
                sprintTop: _ladderTarget.SprintTopMotion != null ? _ladderTarget.SprintTopMotion.CreateMotion() : null,
                sprintFromBottom: _ladderSprintTop,
                sprintApproachOffset: _ladderSprintTop ? Quaternion.Inverse(_ladderTarget.transform.rotation) *
                    (_ladderTarget.SprintBottomApproach - _ladderTarget.Bottom) : Vector3.zero))
            {
                if (!_view.BeginInteraction("Ladder")) CancelLadder();
                else LadderStatus = "W/S climb, Shift+W skip rungs, Shift+S slide; release movement to stop, E or Escape to leave.";
            }
            else { LadderStatus = Ladder.Rejection; _ladderTarget = null; }
        }
        bool wasOnLadder = Ladder.Active;
        if (wasOnLadder && (intent.Held(ActorButtons.Jump) || intent.Held(ActorButtons.Crouch))) CancelLadder();
        if (InteractionMovementLocked) intent = new ActorIntent(Vector2.zero, intent.Look, ActorButtons.None, 0);
        _time += dt;
        _motor.JumpHeight = intent.Move.sqrMagnitude < .01f && !WalkLoop ? Mathf.Min(StandingHopHeight, JumpHeight) : JumpHeight;
        Vector2 move = intent.Move;
        bool run = Running || intent.Held(ActorButtons.Sprint);
        bool dive = Dive || intent.Held(ActorButtons.Crouch), ascend = SwimUp || intent.Held(ActorButtons.SwimUp);
        bool jump = !wasDodging && (RequestJump || intent.Held(ActorButtons.Jump)); RequestJump = false;
        bool release = dive && !_dropHeld; _dropHeld = dive;
        bool climbFromMove = move.y > .5f && _previousHangMove <= .5f;
        bool dropFromMove = move.y < -.5f && _previousHangMove >= -.5f;
        _previousHangMove = move.y;
        bool wasTraversing = Traversal.Active;
        if (Traversal.MovingOnLedge && (jump || climbFromMove)) _pendingHangClimb = true;
        if (!Traversal.Active || release || dropFromMove) _pendingHangClimb = false;
        bool lowerToLedge = (jump && dive || release && ascend) && _motor.Grounded && !_motor.Swimming && !Traversal.Active;
        if (Crouch || intent.Held(ActorButtons.Crouch)) Crawl = false;
        bool crouch = Crouch || InteractionCrouch || intent.Held(ActorButtons.Crouch);
        ThirdPersonCamera.Look(intent.Look, _motor.Pose.Up, LookSensitivity);
        Vector3 forward = ThirdPersonCamera.Forward;
        if (move.sqrMagnitude < .01f && WalkLoop && !wasDodging)
        {
            Vector3 local = transform.InverseTransformPoint(_motor.Pose.Position);
            Vector3 target = new(2f * Mathf.Sin(_time * .35f), 0f, 2f * Mathf.Cos(_time * .35f));
            Vector3 direction = transform.TransformDirection(target - new Vector3(local.x, 0f, local.z));
            forward = Vector3.RotateTowards(_motor.Pose.Forward,
                Vector3.ProjectOnPlane(direction, transform.up).normalized, dt * 4f, 0f);
            move = Vector2.up;
        }
        float speed = move.sqrMagnitude > .01f ? (run ? RunSpeed : WalkSpeed) : 0f;
        if (InteractionApproachPosition.HasValue)
        {
            Vector3 delta = Vector3.ProjectOnPlane(InteractionApproachPosition.Value - _motor.Pose.Position, _motor.Pose.Up);
            Vector3 facing = InteractionApproachForward ?? _motor.Pose.Forward;
            forward = Vector3.RotateTowards(_motor.Pose.Forward, facing, dt * 3f, 0f);
            Vector3 direction = delta.normalized;
            _approachSpeed = Mathf.MoveTowards(_approachSpeed, delta.magnitude > .04f ? Mathf.Min(WalkSpeed, delta.magnitude * 4f) : 0f, dt * 3f);
            speed = Mathf.Min(_approachSpeed, delta.magnitude / dt);
            move = delta.magnitude > .04f ? new Vector2(Vector3.Dot(direction, Vector3.Cross(_motor.Pose.Up, forward)), Vector3.Dot(direction, forward)) : Vector2.zero;
            run = false;
        }
        else _approachSpeed = 0f;
        ActorStance previousStance = Collision.Stance;
        Collision.TryStance(wasDodging || Rope.Active || Beam.Active || Ladder.Active || _motor.Swimming || Traversal.Active || lowerToLedge ? ActorStance.Standing : Crawl ? ActorStance.Crawling :
            crouch ? ActorStance.Crouching : ActorStance.Standing, _motor.Pose);
        if (Collision.Stance != ActorStance.Standing) speed = Mathf.Min(speed,
            Collision.Stance == ActorStance.Crawling ? .55f : .85f);
        if (_motor.Swimming) speed = move.sqrMagnitude > .01f ? (run ? FastSwimSpeed : SwimSpeed) : 0f;
        if (!_motor.Swimming && !Traversal.Active && (_view.CrawlTransitionActive ||
            previousStance != Collision.Stance && (previousStance == ActorStance.Crawling || Collision.Stance == ActorStance.Crawling)))
            speed = 0f;
        Vector3 previous = _motor.Pose.Position;
        if (!Collision.Fits(previous, _motor.Pose.Up, forward, Collision.Stance)) forward = _motor.Pose.Forward;
        if (Ladder.Active) jump = false;
        else if (lowerToLedge)
        {
            if (move.sqrMagnitude > .01f)
                Traversal.TryDropToHang(_motor.Pose, Vector3.Cross(_motor.Pose.Up, forward) * move.x + forward * move.y);
            else Traversal.TryDropToHang(_motor.Pose);
            jump = false;
        }
        else if (Traversal.Kind == TraversalKind.Hanging && (jump || climbFromMove || _pendingHangClimb) && !release && !dropFromMove) { Traversal.Climb(); _pendingHangClimb = false; jump = false; }
        else if (!Traversal.Active && !Rope.Active && !wasOnRope && !Beam.Active && !_motor.Swimming)
            jump = Traversal.ResolveJump(new CharacterPose(previous, _motor.Pose.Up, forward),
                Vector3.ClampMagnitude(Vector3.Cross(_motor.Pose.Up, forward) * move.x + forward * move.y, 1f) * speed,
                _motor.Grounded, jump, dt);
        else if (_motor.Swimming && Traversal.ApproachPending) Traversal.Cancel();
        if ((Traversal.Kind == TraversalKind.Hanging || Traversal.MovingOnLedge) && (release || dropFromMove)) Traversal.Cancel();
        if (Traversal.Kind == TraversalKind.Hanging) Traversal.MoveAlongLedge(move.x);
        if (_hopPreparation >= 0f && (!_motor.Grounded || _motor.Swimming || Traversal.Active || Ladder.Active ||
            Collision.Stance != ActorStance.Standing || InteractionMovementLocked || move.sqrMagnitude > .01f))
        { _hopPreparation = -1f; _view.PrepareStandingHop(-1f); }
        if (_hopPreparation < 0f && jump && _motor.Grounded && !_motor.Swimming && !Traversal.Active && !Ladder.Active &&
            Collision.Stance == ActorStance.Standing && move.sqrMagnitude < .01f && !InteractionMovementLocked &&
            _view.PrepareStandingHop(0f)) _hopPreparation = 0f;
        if (_hopPreparation >= 0f)
        {
            _hopPreparation += dt;
            jump = _hopPreparation >= HopPreparationSeconds;
            _view.PrepareStandingHop(jump ? -1f : _hopPreparation / HopPreparationSeconds);
            if (jump) _hopPreparation = -1f;
        }
        CharacterPose pose;
        bool turnBeam = Mathf.Abs(intent.Move.x) > .5f;
        if (wasDodging) { pose = Dodge.Tick(dt); _motor.ResetPose(pose); }
        else if (Rope.Active) { pose = Rope.Tick(intent.Move.y, dt); _motor.ResetPose(pose, grounded: false); }
        else if (Beam.Active) { pose = Beam.Tick(intent.Move.y, turnBeam && !_beamTurnHeld, dt); _motor.ResetPose(pose); }
        else if (Ladder.Active) { pose = Ladder.Tick(intent.Move.y, dt, intent.Held(ActorButtons.Sprint)); _motor.ResetPose(pose, grounded: false); }
        else if (Traversal.Active) { pose = Traversal.Tick(dt); _motor.ResetPose(pose); }
        else pose = _motor.Tick(move, forward, speed, dt,
            _motor.Swimming ? ascend : jump && Collision.Stance == ActorStance.Standing, dive);
        _beamTurnHeld = turnBeam;
        if (!Rope.Active && !wasOnRope && !Beam.Active && !wasOnBeam && !Traversal.Active && !wasOnLadder)
        {
            Traversal.UpdateCatchAvailability(pose, _motor.Grounded);
            Vector3 intended = Vector3.Cross(pose.Up, forward) * move.x + forward * move.y;
            if (!_motor.Grounded && !_motor.Swimming && !dive &&
                Traversal.TryCatchLedge(pose, (pose.Position - previous) / dt, intended))
                _motor.ResetPose(pose);
        }
        if (InteractionMovementLocked && InteractionPosition.HasValue && InteractionForward.HasValue)
        {
            pose = new CharacterPose(Vector3.MoveTowards(pose.Position, InteractionPosition.Value, dt * .8f), pose.Up,
                Vector3.RotateTowards(pose.Forward, InteractionForward.Value, dt * 3f, 0f));
            _motor.ResetPose(pose);
        }
        _actor.transform.SetPositionAndRotation(pose.Position, Quaternion.LookRotation(pose.Forward, pose.Up));
        PrepareInteraction?.Invoke(dt);
        if (Beam.Active || wasOnBeam || !Traversal.Active && !wasTraversing) ThirdPersonCamera.AlignFacing(pose);
        if (_reachMotion.Active && _reachMotion.Contacted && Panel != null)
            Panel.localRotation = Quaternion.Euler(0f, Mathf.LerpAngle(_panelStart, _panelEnd,
                Mathf.SmoothStep(0f, 1f, _reachMotion.ContactProgress)), 0f);
        _reachMotion.Advance(dt, HandTarget != null ? HandTarget.position : null,
            !_motor.Swimming && !Traversal.Active && speed < .01f);
        bool ladderGroundStep = Ladder.Active && Ladder.Phase == ActorLadderPhase.ApproachStep;
        _view.Pose.FeetEnabled = Feet && !wasDodging && !Rope.Active && (!Ladder.Active || ladderGroundStep);
        _view.Pose.TurnResponseSeconds = InteractionMovementLocked || Ladder.Active || Rope.Active || Beam.Active ? 0f : TorsoTurnResponse;
        _view.Pose.LookInfluence = Gaze && LookTarget != null && !Ladder.Active && !Rope.Active && !Beam.Active ? 1f : 0f;
        if (Reach && _handWeight <= 0f) _gripRotation = Quaternion.LookRotation(-_actor.transform.right, _actor.transform.forward);
        _handWeight = Mathf.MoveTowards(_handWeight, Reach && HandTarget != null && !_motor.Swimming ? 1f : 0f, dt * 3f);
        _view.Pose.SetInteractionTarget("RightHand", _handWeight > 0f && HandTarget != null
            ? new InteractionPoseTarget(HandTarget.position, _gripRotation, _handWeight, useContact: true, gripRadius: HandGripRadius) : null);
        if (_reachMotion.Target is InteractionPoseTarget reachTarget)
            _view.Pose.SetInteractionTarget("RightHand", new InteractionPoseTarget(reachTarget.Position,
                _gripRotation, reachTarget.Weight, useContact: true, gripRadius: HandGripRadius));
        if (RightInteractionTarget.HasValue) _view.Pose.SetInteractionTarget("RightHand", RightInteractionTarget);
        _view.Pose.SetInteractionTarget("LeftHand", LeftInteractionTarget);
        if (_ladderTarget != null && !_ladderApproaching)
        {
            if (Ladder.Active) _view.SetInteractionPhase(Ladder.AnimationPhase, Ladder.AnimationProgress);
            else { _ladderTarget.ApplyContacts(_view.Pose, false, dt); _view.EndInteraction(); _ladderTarget = null; LadderStatus = "Ladder exit complete; ordinary movement resumed."; }
            if (Ladder.Rejection != null) LadderStatus = Ladder.Rejection;
        }
        if (Rope.Active) _view.SetInteractionPhase(Rope.PhaseName, Rope.Progress);
        else if (wasOnRope) _view.EndInteraction();
        if (Beam.Active) _view.SetInteractionPhase(Beam.PhaseName, Beam.Progress);
        else if (wasOnBeam) _view.EndInteraction();
        if (Dodge.Active) _view.SetInteractionPhase(Dodge.PhaseName, Dodge.Progress);
        else if (wasDodging) _view.EndInteraction();
        _view.UseAuthoredJumpGrab = Traversal.ActiveMotion != null && Traversal.ActiveMotion == Traversal.JumpGrabMotion;
        _view.Proficiency = Proficiency;
        _view.SetLocomotionState(Collision.Stance, Ladder.Active || _motor.Grounded, Traversal.Kind, Traversal.Progress,
            Traversal.Active ? Traversal.Edge : null, !wasOnLadder && _motor.Jumping,
            (Vector3.Cross(pose.Up, forward) * move.x + forward * move.y) * speed);
        _view.Tick((pose.Position - previous) / dt, pose.Up, LookTarget != null ? LookTarget.position : null,
            Ladder.Active && !ladderGroundStep ? null : this, dt,
            swimming: _motor.Swimming, diving: _motor.Diving, waterDepth: _motor.WaterDepth, fastSwimming: run);
        PresentInteraction?.Invoke(dt);
        if (_ladderApproaching && _view.Pose.TryGetInteractionContact("LadderLeftFoot", out var leftSupport, out _) &&
            _view.Pose.TryGetInteractionContact("LadderRightFoot", out var rightSupport, out _))
        {
            LadderEntrySupportSpeed = _ladderSupportSampled ? Mathf.Max(
                Vector3.Distance(leftSupport, _ladderLeftSupport), Vector3.Distance(rightSupport, _ladderRightSupport)) / dt : float.MaxValue;
            _ladderLeftSupport = leftSupport; _ladderRightSupport = rightSupport; _ladderSupportSampled = true;
        }
        if (_view.Pose.TryGetInteractionContact("RightHand", out var contact, out bool reached) && reached &&
            _reachMotion.ObserveContact(contact, .035f)) PanelContacts++;
    }

    float HandGripRadius => HandTarget != null ? .5f * Mathf.Max(HandTarget.lossyScale.x,
        Mathf.Max(HandTarget.lossyScale.y, HandTarget.lossyScale.z)) : 0f;

    public void ResetActor()
    {
        if (_view == null) return;
        CancelLadder(); Rope.Cancel(); Beam.Cancel(); Dodge.Reset(); _beamTurnHeld = false;
        _time = _handWeight = _previousHangMove = 0f; _dropHeld = false; WalkLoop = Running = Reach = Dive = SwimUp = false;
        _hopPreparation = -1f; _view?.PrepareStandingHop(-1f);
        Crouch = Crawl = RequestJump = false; Traversal.Cancel();
        _reachMotion.Reset(); PanelContacts = 0;
        if (Panel != null) Panel.localRotation = Quaternion.identity;
        _motor.ResetPose(new CharacterPose(transform.position, transform.up, transform.forward));
        ThirdPersonCamera.Reset(_motor.Pose);
        Collision.TryStance(ActorStance.Standing, _motor.Pose);
        _actor.transform.SetPositionAndRotation(transform.position, transform.rotation);
        _view.Reset();
    }

    public bool UsePanel()
    {
        if (_view == null || Dodge.Active || _motor.Swimming || Panel == null || HandTarget == null || _reachMotion.Active) return false;
        _panelStart = Panel.localEulerAngles.y;
        _panelEnd = Mathf.Abs(Mathf.DeltaAngle(_panelStart, 0f)) < 30f ? 60f : 0f;
        // A round knob permits the grasp to roll while the panel turns. Keep the hand's approach frame stable.
        _gripRotation = Quaternion.LookRotation(-_actor.transform.right, _actor.transform.forward);
        Reach = false;
        return _reachMotion.Begin(HandTarget.position);
    }

    protected virtual void OnDisable()
    {
        CancelLadder();
        SetCursorLocked(false);
        ThirdPersonCamera?.Dispose(); ThirdPersonCamera = null;
        if (_flyCamera != null) _flyCamera.enabled = _flyWasEnabled;
        _following = false;
        _reachMotion.Reset();
        if (_view != null) _view.Pose.BeforeCorrections -= RefreshLadderContacts;
        _view?.Dispose(); _view = null; _motor = null;
        if (_actor != null) { if (Application.isPlaying) Destroy(_actor); else DestroyImmediate(_actor); }
        _actor = null;
    }

    protected void SetCameraMode()
    {
        if (_following == FollowActor) return;
        _following = FollowActor;
        ThirdPersonCamera?.SetActive(FollowActor);
        if (_flyCamera != null) _flyCamera.enabled = !FollowActor && _flyWasEnabled;
        if (FollowActor && _motor != null) ThirdPersonCamera.Reset(_motor.Pose);
        if (!FollowActor) SetCursorLocked(false);
    }

    protected void SetCursorLocked(bool locked)
    {
        if (_cursorLocked == locked) return;
        _cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void OnApplicationFocus(bool focused)
    {
        if (!focused) { _lookReleased = true; SetCursorLocked(false); }
    }
}


