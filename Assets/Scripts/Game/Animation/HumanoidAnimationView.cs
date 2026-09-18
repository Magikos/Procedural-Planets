using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Humanoid locomotion and procedural targets through the shared actor pose pipeline.</summary>
public sealed class HumanoidAnimationView : IDisposable
{
    readonly Transform _frame;
    readonly SkinnedMeshRenderer[] _skins;
    readonly bool[] _skinMatrixUpdates;
    readonly AnimationClipPlayable _walk, _run;
    readonly float _walkLength, _runLength, _walkSpeed, _runSpeed;
    float _speed, _moving;
    readonly Transform[] _footContacts;
    readonly float[] _footSoles, _footWeights;
    Vector4 _directionWeights = new(1f, 0f, 0f, 0f);
    Vector4 _walkCardinalWeights = new(1f, 0f, 0f, 0f), _walkDiagonalWeights;
    readonly AnimationClipPlayable[] _walkDiagonals;
    readonly float[] _walkDiagonalLengths;
    readonly int _walkDiagonalOffset;
    readonly int _fastSurfaceSwimOffset;
    readonly AnimationClipPlayable _fastSurfaceSwim;
    readonly float _fastSurfaceSwimLength;
    float _fastSurfaceSwimBlend;
    double _phase, _swimPhase;
    double _crawlPhase;
    readonly Vector4 _crawlCycleDistances;
    readonly AnimationClipPlayable[] _swimDirectional;
    readonly float[] _swimDirectionalLengths;
    readonly float _surfaceMoveLength, _underwaterMoveLength;
    readonly int _swimDirectionalOffset, _wadeOffset;
    readonly AnimationClipPlayable _wade;
    readonly float _wadeLength;
    float _wadeBlend;
    Vector3 _traversalBodyCorrection;
    public float WadeWeight => _wadeBlend;
    bool _skipped;
    readonly bool _hasSwimming;
    readonly bool _sharedSurfaceIdle;
    float _swimBlend, _diveBlend;
    readonly Transform _body, _head;
    readonly Transform _leftHand, _rightHand;
    readonly Transform[] _upperArms, _elbows;
    readonly Transform _leftFoot, _rightFoot;
    readonly LimbGroundingSolver _crawlGrounding;
    readonly ActorTraversalMotion _vaultMotion, _stepUpMotion, _jumpGrabMotion;
    readonly ActorTraversalMotion _hangLeftMotion, _hangRightMotion;
    readonly ActorTraversalMotion[] _ledgeCorners;
    float _hangLeftPhase, _hangRightPhase;
    Vector3 _settledHangCorrection;
    bool _hasSettledHangCorrection;
    public bool UseAuthoredJumpGrab { get; set; }
    float _jumpGrabPhase;
    readonly float[] _actionWeights = { 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
    readonly string _vaultContact;
    float _vaultPhase, _stepUpPhase;
    Vector3? _ledge;
    bool _traversalHandsActive;
    public float TraversalHandWeight { get; private set; }
    float _surfaceLift, _divePitch;
    readonly AnimationClipPlayable[] _traversalClips;
    readonly float[] _traversalLengths;
    readonly int _traversalOffset;
    ActorStance _stance;
    TraversalKind _traversal;
    bool _grounded = true;
    bool _jumping;
    float _fallDistance, _fallBlend;
    readonly int _fallOffset;
    public float FallDistance => _fallDistance;
    public float FallWeight => _fallBlend;
    float _traversalProgress, _crouchBlend, _crawlBlend, _actionBlend, _airTime;
    int _actionIndex = 4;
    readonly AnimationClipPlayable[] _directionalClips;
    readonly float[] _directionalLengths;
    readonly int _directionalOffset, _runDirectionalOffset;
    readonly AnimationClipPlayable[] _runDirectional;
    readonly float[] _runDirectionalLengths;
    public float SwimWeight => _swimBlend;
    public ActorAnimationGraph Graph { get; }
    public ProceduralPoseRig Pose { get; }
    public float Speed => _speed;
    public float CameraFocusHeight => Mathf.Max(.15f, Vector3.Dot(_head.position - _frame.position, _frame.up) - .12f * _frame.lossyScale.x);
    public ActorAirborneMotion Airborne { get; } = new(.6f);
    readonly ActorPerformancePlayback _performance;
    readonly ActorAnimationPerformanceLibraryData _performanceData;
    readonly int _performanceOffset;
    readonly int _stairOffset;
    readonly AnimationClipPlayable[] _stairs;
    readonly float[] _stairLengths;
    readonly Vector3[] _previousStairFeet;
    bool _stairFeetValid;
    float _stairHold;
    Vector2 _stairDirection;
    public Vector2 StairBlend { get; private set; }
    readonly ActorPerformancePlayback _crawlTransition;
    readonly int _crawlTransitionOffset, _crawlDirectionalOffset;
    readonly AnimationClipPlayable[] _crawlDirectional;
    readonly float[] _crawlDirectionalLengths;
    readonly int[] _crawlDirectionIndices;
    float _crawlTransitionTime, _crawlTransitionDuration;
    string _crawlTransitionPhase;
    bool _crawlRequested;
    public float CrawlTransitionWeight => _crawlTransition?.Weight ?? 0f;
    public bool CrawlTransitionActive => _crawlTransition != null &&
        (_crawlTransitionPhase != null || _crawlTransition.Weight > .001f || (_stance == ActorStance.Crawling) != _crawlRequested);
    bool _performanceAttempted;
    Vector3? _movementIntent;
    string _jumpLandingPhase = "Landing";
    float _proficiency;
    public float Proficiency
    {
        get => _proficiency;
        set { if (!float.IsFinite(value) || value < 0f || value > 1f) throw new ArgumentOutOfRangeException(nameof(value)); _proficiency = value; }
    }
    public string PerformanceId => _performance?.ActivePerformance?.Id;
    public string PerformancePhase => _performance?.PhaseName;
    public float PerformanceTime => _performance?.CurrentNormalizedTime ?? 0f;
    string _interactionPhase;
    float _interactionProgress;
    bool _interaction;

    public bool BeginInteraction(string action)
    {
        if (_performanceData == null || _performanceData.Select(action, "Humanoid", _proficiency) == null) return false;
        _interaction = _performance != null && _performance.Begin(action, "Humanoid", _proficiency);
        _interactionPhase = null;
        return _interaction;
    }

    public void SetInteractionPhase(string phase, float progress)
    {
        if (!_interaction || _performance.ActivePerformance?.GetPhase(phase) == null)
            throw new InvalidOperationException("Begin an authored interaction before setting its phase.");
        if (!float.IsFinite(progress) || progress < 0f) throw new ArgumentOutOfRangeException(nameof(progress));
        _interactionPhase = phase; _interactionProgress = progress;
    }

    float _hopPreparation = -1f;
    public bool PrepareStandingHop(float progress)
    {
        if (progress < 0f) { _hopPreparation = -1f; return true; }
        if (_hopPreparation < 0f && (_performance == null || !_performance.Begin("Jump", "Humanoid", _proficiency, "Stationary") ||
            _performance.ActivePerformance.GetPhase("Preparation") == null)) return false;
        _hopPreparation = Mathf.Clamp01(progress);
        _performanceAttempted = true;
        return true;
    }

    public void EndInteraction() { _interaction = false; _interactionPhase = null; Pose.ForwardLeanDegrees = 0f; }

    public HumanoidAnimationView(Animator animator, ProceduralRigDefinition rig,
        AnimationClip idle, AnimationClip walk, AnimationClip run, float walkSpeed = 1.4f, float runSpeed = 4f,
        AnimationClip[] swimming = null, AnimationClip[] traversal = null, AnimationClip[] directional = null, AnimationClip fall = null,
        ActorAnimationPerformanceLibraryData performances = null, AnimationClip[] swimDirectional = null, AnimationClip wade = null,
        AnimationClip[] walkDiagonals = null, AnimationClip fastSurfaceSwim = null,
        AnimationClip[] crawlTransitions = null, AnimationClip crawlBackward = null, AnimationClip[] crawlSideways = null,
        Vector4? crawlCycleDistances = null, ActorTraversalMotion vaultMotion = null, string vaultContact = "RightHand", AnimationClip[] stairs = null, AnimationClip[] runDirectional = null, ActorTraversalMotion stepUpMotion = null, AnimationClip jumpGrabClip = null, ActorTraversalMotion jumpGrabMotion = null, ActorTraversalMotionAsset hangLeft = null, ActorTraversalMotionAsset hangRight = null, ActorTraversalMotionAsset[] ledgeCorners = null)
    {
        if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid)
            throw new ArgumentException("A valid Humanoid Animator is required.", nameof(animator));
        if (rig == null) throw new ArgumentNullException(nameof(rig));
        if (idle == null || walk == null || run == null || !idle.isHumanMotion || !walk.isHumanMotion || !run.isHumanMotion)
            throw new ArgumentException("Idle, walk, and run must be Humanoid clips.");
        if (!float.IsFinite(walkSpeed) || !float.IsFinite(runSpeed) || walkSpeed <= 0f || runSpeed <= walkSpeed)
            throw new ArgumentOutOfRangeException(nameof(walkSpeed));
        if (walk.length <= 0f || run.length <= 0f) throw new ArgumentException("Locomotion clips must have positive duration.");
        if (swimming != null)
        {
            if (swimming.Length != 4) throw new ArgumentException("Swimming needs surface idle, surface move, underwater idle, underwater move.");
            foreach (var clip in swimming)
                if (clip == null || !clip.isHumanMotion || clip.length <= 0f) throw new ArgumentException("Swimming clips must be valid Humanoid motions.");
            _hasSwimming = true;
            _sharedSurfaceIdle = swimming[0] == swimming[1];
        }
        if (swimDirectional != null)
        {
            if (!_hasSwimming || swimDirectional.Length != 3) throw new ArgumentException("Directional swimming requires swimming clips and left, right, back motions.", nameof(swimDirectional));
            foreach (var clip in swimDirectional)
                if (clip == null || !clip.isHumanMotion || clip.length <= 0f) throw new ArgumentException("Directional swimming clips must be valid Humanoid motions.", nameof(swimDirectional));
            _swimDirectional = new AnimationClipPlayable[3]; _swimDirectionalLengths = new float[3];
            _surfaceMoveLength = swimming[1].length; _underwaterMoveLength = swimming[3].length;
        }
        if (wade != null && (!wade.isHumanMotion || wade.length <= 0f)) throw new ArgumentException("Wading requires a valid Humanoid motion.", nameof(wade));
        if (fastSurfaceSwim != null && (!_hasSwimming || !fastSurfaceSwim.isHumanMotion || fastSurfaceSwim.length <= 0f))
            throw new ArgumentException("Fast surface swimming requires swimming clips and a valid Humanoid motion.", nameof(fastSurfaceSwim));
        _frame = animator.transform;
        _footContacts = new Transform[rig.Feet.Length];
        _footSoles = new float[rig.Feet.Length]; _footWeights = new float[rig.Feet.Length];
        for (int i = 0; i < rig.Feet.Length; i++)
        {
            _footContacts[i] = rig.Feet[i].Contact != null ? rig.Feet[i].Contact : rig.Feet[i].Bones[^1];
            _footSoles[i] = rig.Feet[i].SoleOffset * _frame.lossyScale.x;
        }
        if (directional != null)
        {
            if (directional.Length != 6) throw new ArgumentException("Directional locomotion needs left, right, back, then crouched left, right, back.");
            foreach (var clip in directional)
                if (clip == null || !clip.isHumanMotion || clip.length <= 0f) throw new ArgumentException("Directional clips must be valid Humanoid motions.");
            _directionalClips = new AnimationClipPlayable[6]; _directionalLengths = new float[6];
        }
        if (walkDiagonals != null)
        {
            if (_directionalClips == null || walkDiagonals.Length != 4)
                throw new ArgumentException("Diagonal walking requires cardinal clips and forward-left, forward-right, backward-left, backward-right motions.", nameof(walkDiagonals));
            foreach (var clip in walkDiagonals)
                if (clip == null || !clip.isHumanMotion || clip.length <= 0f)
                    throw new ArgumentException("Diagonal walking clips must be valid Humanoid motions.", nameof(walkDiagonals));
            _walkDiagonals = new AnimationClipPlayable[4]; _walkDiagonalLengths = new float[4];
        }
        if (traversal != null)
        {
            if (traversal.Length != 9) throw new ArgumentException("Traversal needs four posture clips and jump, vault, hang, climb, step clips.");
            foreach (var clip in traversal)
                if (clip == null || !clip.isHumanMotion || clip.length <= 0f) throw new ArgumentException("Traversal clips must be valid Humanoid motions.");
            if ((hangLeft == null) != (hangRight == null)) throw new ArgumentException("Hanging travel requires both directions.");
            if (ledgeCorners != null && ledgeCorners.Length > 0 && (ledgeCorners.Length != 4 || hangLeft == null || Array.Exists(ledgeCorners, c => c == null)))
                throw new ArgumentException("Ledge corners require four authored motions and straight ledge travel.");
            int count = ledgeCorners != null && ledgeCorners.Length == 4 ? 16 : hangLeft != null ? 12 : jumpGrabClip != null ? 10 : 9;
            _traversalClips = new AnimationClipPlayable[count]; _traversalLengths = new float[count];
        }
        _body = animator.GetBoneTransform(HumanBodyBones.Hips);
        _head = animator.GetBoneTransform(HumanBodyBones.Head);
        _upperArms = new[] { animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), animator.GetBoneTransform(HumanBodyBones.RightUpperArm) };
        _elbows = new[] { animator.GetBoneTransform(HumanBodyBones.LeftLowerArm), animator.GetBoneTransform(HumanBodyBones.RightLowerArm) };
        _leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        _rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        _leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        _rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        _stepUpMotion = stepUpMotion; _jumpGrabMotion = jumpGrabMotion;
        _hangLeftMotion = hangLeft != null ? hangLeft.CreateMotion() : null;
        _hangRightMotion = hangRight != null ? hangRight.CreateMotion() : null;
        _ledgeCorners = ledgeCorners != null && ledgeCorners.Length == 4 ? Array.ConvertAll(ledgeCorners, c => c.CreateMotion()) : null;
        if ((jumpGrabClip == null) != (jumpGrabMotion == null) || jumpGrabClip != null && (!jumpGrabClip.isHumanMotion || jumpGrabClip.length <= 0f || traversal == null))
            throw new ArgumentException("Jump grab requires a valid authored clip and matching motion.");
        _vaultMotion = vaultMotion; _vaultContact = vaultContact;
        if (vaultMotion != null && (traversal == null || (vaultContact != "LeftHand" && vaultContact != "RightHand")))
            throw new ArgumentException("Authored vault motion requires a traversal clip and hand contact.");
        if (traversal != null)
        {
            Transform[] Limb(HumanBodyBones first, HumanBodyBones middle, HumanBodyBones tip) =>
                new[] { animator.GetBoneTransform(first), animator.GetBoneTransform(middle), animator.GetBoneTransform(tip) };
            _crawlGrounding = new LimbGroundingSolver(_body, new[] {
                Limb(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
                Limb(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
                Limb(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
                Limb(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot)
            }, .055f * _frame.lossyScale.x, .3f * _frame.lossyScale.x, _frame);
        }
        _crawlCycleDistances = crawlCycleDistances ?? Vector4.one * .55f;
        for (int i = 0; i < 4; i++)
            if (!float.IsFinite(_crawlCycleDistances[i]) || _crawlCycleDistances[i] <= 0f)
                throw new ArgumentOutOfRangeException(nameof(crawlCycleDistances));
        if (runDirectional != null)
        {
            if (directional == null || walkDiagonals == null || runDirectional.Length != 7 ||
                Array.Exists(runDirectional, c => c == null || !c.isHumanMotion || c.length <= 0f))
                throw new ArgumentException("Directional running requires eight-way walking and seven valid Humanoid run clips.", nameof(runDirectional));
            _runDirectional = new AnimationClipPlayable[7];
            _runDirectionalLengths = new float[7];
        }
        _walkSpeed = walkSpeed; _runSpeed = runSpeed;
        _walkLength = walk.length; _runLength = run.length;
        Pose = new ProceduralPoseRig(_frame, rig) { BodyLeanEnabled = false };
        _traversalOffset = _hasSwimming ? 7 : 3;
        _directionalOffset = _traversalOffset + (_traversalClips?.Length ?? 0);
        if (fall != null && (!fall.isHumanMotion || fall.length <= 0f)) throw new ArgumentException("Fall must be a valid Humanoid clip.", nameof(fall));
        _runDirectionalOffset = _directionalOffset + (_directionalClips?.Length ?? 0);
        _fallOffset = fall != null ? _runDirectionalOffset + (_runDirectional?.Length ?? 0) : -1;
        _swimDirectionalOffset = _runDirectionalOffset + (_runDirectional?.Length ?? 0) + (fall != null ? 1 : 0);
        _wadeOffset = wade != null ? _swimDirectionalOffset + (_swimDirectional?.Length ?? 0) : -1;
        _walkDiagonalOffset = _swimDirectionalOffset + (_swimDirectional?.Length ?? 0) + (wade != null ? 1 : 0);
        _fastSurfaceSwimOffset = fastSurfaceSwim != null ? _walkDiagonalOffset + (_walkDiagonals?.Length ?? 0) : -1;
        ActorAnimationPerformanceLibraryData crawlData = null;
        if (crawlTransitions != null)
        {
            if (_traversalClips == null || crawlTransitions.Length != 2)
                throw new ArgumentException("Crawling transitions require posture clips and enter, exit motions.", nameof(crawlTransitions));
            foreach (var clip in crawlTransitions)
                if (clip == null || !clip.isHumanMotion || clip.length <= 0f) throw new ArgumentException("Crawl transitions require valid Humanoid clips.");
            crawlData = new ActorAnimationPerformanceLibraryData(new[] { new ActorAnimationPerformanceLibrary.Entry
            {
                Id = "BasicCrawlTransition", Action = "CrawlTransition", RigFamily = "Humanoid",
                Phases = new[] {
                    new ActorAnimationPerformanceLibrary.Phase { Name = "Enter", Clip = crawlTransitions[0], BlendSeconds = .15f },
                    new ActorAnimationPerformanceLibrary.Phase { Name = "Exit", Clip = crawlTransitions[1], BlendSeconds = .15f },
                    new ActorAnimationPerformanceLibrary.Phase { Name = "ExitToCrouch", Clip = crawlTransitions[1], EndNormalized = .8f, BlendSeconds = .2f }
                }
            }});
        }
        if (crawlBackward != null && (_traversalClips == null || !crawlBackward.isHumanMotion || crawlBackward.length <= 0f))
            throw new ArgumentException("Backward crawling requires posture clips and a valid Humanoid motion.", nameof(crawlBackward));
        if (crawlSideways != null)
        {
            if (_traversalClips == null || crawlSideways.Length != 2)
                throw new ArgumentException("Sideways crawling requires posture clips and left, right motions.", nameof(crawlSideways));
            foreach (var clip in crawlSideways)
                if (clip == null || !clip.isHumanMotion || clip.length <= 0f)
                    throw new ArgumentException("Sideways crawl clips must be valid Humanoid motions.", nameof(crawlSideways));
        }
        int nextSlot = _walkDiagonalOffset + (_walkDiagonals?.Length ?? 0) + (fastSurfaceSwim != null ? 1 : 0);
        _crawlDirectionalOffset = nextSlot;
        int crawlDirectionCount = (crawlSideways?.Length ?? 0) + (crawlBackward != null ? 1 : 0);
        _crawlDirectional = new AnimationClipPlayable[crawlDirectionCount];
        _crawlDirectionalLengths = new float[crawlDirectionCount];
        _crawlDirectionIndices = new int[crawlDirectionCount];
        nextSlot += crawlDirectionCount;
        _crawlTransitionOffset = nextSlot;
        _stairOffset = nextSlot + (crawlData != null ? ActorPerformancePlayback.CountInputs(crawlData) : 0);
        if (stairs != null)
        {
            if (stairs.Length != 2 || Array.Exists(stairs, c => c == null || !c.isHumanMotion || c.length <= 0f))
                throw new ArgumentException("Stairs require ascending and descending Humanoid clips.", nameof(stairs));
            _stairs = new AnimationClipPlayable[2]; _stairLengths = new float[2];
            _previousStairFeet = new Vector3[_footContacts.Length];
        }
        _performanceOffset = _stairOffset + (stairs != null ? 2 : 0);
        if (performances != null)
            for (int i = 0; i < performances.Count; i++)
            {
                for (int j = 0; j < performances[i].PhaseCount; j++)
                    if (!performances[i][j].Clip.isHumanMotion || performances[i][j].Clip.length <= 0f)
                        throw new ArgumentException("Humanoid performances require positive-duration Humanoid clips.", nameof(performances));
                if (performances[i].Action == "Jump" && performances[i].RigFamily == "Humanoid" &&
                    (performances[i].GetPhase("Ascent") == null || performances[i].GetPhase("Apex") == null ||
                    performances[i].GetPhase("Descent") == null || performances[i].GetPhase("Landing") == null))
                    throw new ArgumentException("Humanoid jumps require Ascent, Apex, Descent, and Landing phases.", nameof(performances));
            }
        Graph = new ActorAnimationGraph(animator, "Humanoid actor", _performanceOffset +
            (performances != null ? ActorPerformancePlayback.CountInputs(performances) : 0));
        if (_runDirectional != null) for (int i = 0; i < _runDirectional.Length; i++)
        {
            _runDirectional[i] = Graph.AddBaseClip(_runDirectionalOffset + i, runDirectional[i]);
            _runDirectional[i].SetSpeed(0d); _runDirectionalLengths[i] = runDirectional[i].length;
        }
        _performanceData = performances;
        if (performances != null) _performance = new ActorPerformancePlayback(Graph, _performanceOffset, performances);
        if (stairs != null) for (int i = 0; i < 2; i++)
        {
            _stairs[i] = Graph.AddBaseClip(_stairOffset + i, stairs[i]);
            _stairs[i].SetSpeed(0d); _stairLengths[i] = stairs[i].length;
        }
        if (crawlData != null) _crawlTransition = new ActorPerformancePlayback(Graph, _crawlTransitionOffset, crawlData);
        for (int i = 0; i < crawlDirectionCount; i++)
        {
            bool sideways = crawlSideways != null && i < 2;
            var clip = sideways ? crawlSideways[i] : crawlBackward;
            _crawlDirectional[i] = Graph.AddBaseClip(_crawlDirectionalOffset + i, clip);
            _crawlDirectional[i].SetSpeed(0d); _crawlDirectionalLengths[i] = clip.length;
            _crawlDirectionIndices[i] = sideways ? i + 1 : 3;
        }
        if (fall != null) Graph.AddBaseClip(_fallOffset, fall);
        if (_swimDirectional != null) for (int i = 0; i < 3; i++)
        {
            _swimDirectional[i] = Graph.AddBaseClip(_swimDirectionalOffset + i, swimDirectional[i]);
            _swimDirectional[i].SetSpeed(0d); _swimDirectionalLengths[i] = swimDirectional[i].length;
        }
        if (wade != null) { _wade = Graph.AddBaseClip(_wadeOffset, wade); _wade.SetSpeed(0d); _wadeLength = wade.length; }
        if (fastSurfaceSwim != null)
        {
            _fastSurfaceSwim = Graph.AddBaseClip(_fastSurfaceSwimOffset, fastSurfaceSwim);
            _fastSurfaceSwim.SetSpeed(0d); _fastSurfaceSwimLength = fastSurfaceSwim.length;
            _surfaceMoveLength = swimming[1].length;
        }
        if (_walkDiagonals != null) for (int i = 0; i < 4; i++)
        {
            _walkDiagonals[i] = Graph.AddBaseClip(_walkDiagonalOffset + i, walkDiagonals[i]);
            _walkDiagonals[i].SetSpeed(0d); _walkDiagonalLengths[i] = walkDiagonals[i].length;
        }
        Graph.AddBaseClip(0, idle);
        _walk = Graph.AddBaseClip(1, walk); _run = Graph.AddBaseClip(2, run);
        _walk.SetSpeed(0); _run.SetSpeed(0);
        Graph.BaseMixer.SetInputWeight(0, 1f);
        if (_hasSwimming) for (int i = 0; i < 4; i++) Graph.AddBaseClip(i + 3, swimming[i]);
        if (_swimDirectional != null) { Graph.BaseMixer.GetInput(4).SetSpeed(0d); Graph.BaseMixer.GetInput(6).SetSpeed(0d); }
        if (_sharedSurfaceIdle) Graph.BaseMixer.GetInput(3).SetSpeed(0d);
        if (_traversalClips != null) for (int i = 0; i < _traversalClips.Length; i++)
        {
            var source = i >= 12 ? ledgeCorners[i - 12].Clip : i == 10 ? hangLeft.Clip : i == 11 ? hangRight.Clip : i == 9 ? jumpGrabClip ?? traversal[4] : traversal[i];
            _traversalClips[i] = Graph.AddBaseClip(_traversalOffset + i, source); _traversalLengths[i] = source.length;
            if (hangLeft != null && (i == 6 || i >= 10)) _traversalClips[i].SetApplyFootIK(false);
            if (i == 4 || i == 5 || i == 7 || i == 8 || i >= 9) _traversalClips[i].SetSpeed(0f);
        }
        if (_directionalClips != null) for (int i = 0; i < 6; i++)
        {
            _directionalClips[i] = Graph.AddBaseClip(_directionalOffset + i, directional[i]);
            _directionalClips[i].SetSpeed(0f); _directionalLengths[i] = directional[i].length;
        }
        _skins = animator.GetComponentsInChildren<SkinnedMeshRenderer>();
        _skinMatrixUpdates = new bool[_skins.Length];
        // Manual graph sampling and procedural IK can change bones after Unity caches skin matrices.
        for (int i = 0; i < _skins.Length; i++)
        {
            _skinMatrixUpdates[i] = _skins[i].forceMatrixRecalculationPerRender;
            _skins[i].forceMatrixRecalculationPerRender = true;
        }
    }

    public void SetLocomotionState(ActorStance stance, bool grounded, TraversalKind traversal, float progress, Vector3? ledge = null, bool jumping = false,
        Vector3? movementIntent = null)
    {
        if (!float.IsFinite(progress) || (ledge.HasValue && !CharacterMath.IsFinite(ledge.Value)))
            throw new ArgumentOutOfRangeException(nameof(progress));
        if (movementIntent.HasValue && !CharacterMath.IsFinite(movementIntent.Value))
            throw new ArgumentOutOfRangeException(nameof(movementIntent));
        _movementIntent = movementIntent;
        _stance = stance; _grounded = grounded; _traversal = traversal; _jumping = jumping;
        _traversalProgress = Mathf.Clamp01(progress); if (ledge.HasValue) _ledge = ledge;
    }

    public void Tick(Vector3 velocity, Vector3 up, Vector3? lookTarget, IGroundingProvider ground,
        float dt, bool evaluatePose = true, bool swimming = false, bool diving = false, float? waterDepth = null, bool fastSwimming = false)
    {
        if (!CharacterMath.IsFinite(velocity) || !CharacterMath.IsFinite(up) || up.sqrMagnitude < .001f ||
            !float.IsFinite(dt) || dt < 0f || (waterDepth.HasValue && !float.IsFinite(waterDepth.Value)))
            throw new ArgumentOutOfRangeException(nameof(dt));
        up.Normalize();
        var previousPhase = Airborne.Phase;
        Airborne.Advance(dt, _grounded, _jumping, velocity, up, swimming || _traversal != TraversalKind.None);
        if (previousPhase == ActorAirbornePhase.Landing && Airborne.Phase == ActorAirbornePhase.Grounded &&
            _grounded && !swimming && _traversal == TraversalKind.None && !_interaction && _jumpLandingPhase == "Landing" &&
            _performance?.ActivePerformance is { LocomotionExitPhase: >= 0f } completedJump)
        {
            // Locomotion was hidden during landing. Match its incoming support leg before the existing release blend.
            _phase = completedJump.LocomotionExitPhase;
        }
        if (!Airborne.Active && _hopPreparation < 0f)
        {
            _performanceAttempted = false;
            if ((_performance?.Weight ?? 0f) == 0f) _jumpLandingPhase = "Landing";
        }
        if (previousPhase == ActorAirbornePhase.Landing && !_grounded) _performanceAttempted = false;
        if (_performance != null && !_interaction && Airborne.IntentionalJump && !_performanceAttempted)
        {
            string condition = Vector3.ProjectOnPlane(velocity, up).sqrMagnitude < .01f ? "Stationary" :
                Vector3.Dot(velocity, _frame.forward) > _walkSpeed ? "Running" : "";
            if (_performance.Begin("Jump", "Humanoid", _proficiency, condition))
            {
                var landing = _performance.ActivePerformance.GetPhase("Landing");
                Airborne.RecoveryDuration = landing.Clip.length * (landing.EndNormalized - landing.StartNormalized);
            }
            _performanceAttempted = true;
        }
        if (!_interaction && Airborne.IntentionalJump && _performance?.ActivePerformance is { } jumpPerformance)
        {
            // Flight carries momentum even after input release. Intent selects recovery; actual support starts it.
            bool continueMoving = !_movementIntent.HasValue || Vector3.Dot(_movementIntent.Value, _frame.forward) > .2f;
            if (previousPhase != ActorAirbornePhase.Landing)
            {
                _jumpLandingPhase = !continueMoving && jumpPerformance.GetPhase("LandingStop") != null ? "LandingStop" : "Landing";
                var landing = jumpPerformance.GetPhase(_jumpLandingPhase);
                Airborne.RecoveryDuration = landing.Clip.length * (landing.EndNormalized - landing.StartNormalized);
            }
            else if (_jumpLandingPhase == "LandingStop" && continueMoving)
                Airborne.RecoveryDuration = Mathf.Min(Airborne.RecoveryDuration, Airborne.PhaseTime + dt);
        }
        UpdateDirection(velocity, dt);
        float speed = Vector3.ProjectOnPlane(velocity, up).magnitude;
        _speed = Mathf.Lerp(_speed, speed, 1f - Mathf.Exp(-12f * dt));
        _moving = Mathf.Lerp(_moving, Mathf.InverseLerp(.02f, _walkSpeed * .1f, speed), 1f - Mathf.Exp(-12f * dt));
        float moving = _moving;
        float running = Mathf.InverseLerp(_walkSpeed, _runSpeed, _speed);
        _swimBlend = Mathf.MoveTowards(_swimBlend, swimming && _hasSwimming ? 1f : 0f, dt * 3f);
        _diveBlend = Mathf.MoveTowards(_diveBlend, diving ? 1f : 0f, dt * 3f);
        Graph.BaseMixer.SetInputWeight(0, (1f - moving) * (1f - _swimBlend));
        Graph.BaseMixer.SetInputWeight(1, moving * (1f - running) * (1f - _swimBlend));
        Graph.BaseMixer.SetInputWeight(2, moving * running * (1f - _swimBlend));
        if (_hasSwimming)
        {
            float stroke = Mathf.Clamp01(velocity.magnitude / _walkSpeed);
            Graph.BaseMixer.SetInputWeight(3, _swimBlend * (1f - _diveBlend) * (1f - stroke));
            Graph.BaseMixer.SetInputWeight(4, _swimBlend * (1f - _diveBlend) * stroke);
            Graph.BaseMixer.SetInputWeight(5, _swimBlend * _diveBlend * (1f - stroke));
            Graph.BaseMixer.SetInputWeight(6, _swimBlend * _diveBlend * stroke);
        }
        _phase += dt * _speed / Mathf.Lerp(_walkLength * _walkSpeed, _runLength * _runSpeed, running);
        Graph.Advance(dt);
        _crawlPhase += dt * speed / Vector4.Dot(_directionWeights, _crawlCycleDistances);
        if (_traversalClips != null) _traversalClips[3].SetTime(_crawlPhase * _traversalLengths[3]);
        bool falling = !_grounded && !_jumping && !swimming && _traversal == TraversalKind.None;
        _airTime = _grounded || swimming || _traversal != TraversalKind.None ? 0f : _airTime + dt;
        _fallDistance = falling ? _fallDistance + Mathf.Max(0f, -Vector3.Dot(velocity, up)) * dt : 0f;
        // Short drops retain the locomotion pose. Longer falls progressively introduce the fall clip.
        _fallBlend = Mathf.MoveTowards(_fallBlend, falling && _airTime > .12f ?
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.25f, 1f, _fallDistance)) : 0f, dt * 6f);
        if (_traversalClips != null)
        {
            bool enteringPostureTransition = _crawlTransition != null &&
                (_crawlTransitionPhase != null || (_stance == ActorStance.Crawling) != _crawlRequested) &&
                _crawlTransition.Weight < .999f && _grounded && !swimming && _traversal == TraversalKind.None;
            // Keep the previous support pose until the authored transition hides the destination pose.
            if (!enteringPostureTransition)
            {
                _crouchBlend = Mathf.MoveTowards(_crouchBlend, _stance == ActorStance.Crouching ? 1f : 0f, dt * 5f);
                _crawlBlend = Mathf.MoveTowards(_crawlBlend, _stance == ActorStance.Crawling ? 1f : 0f, dt * 5f);
            }
            bool action = !swimming && ((_jumping && _performance?.ActivePerformance == null) || _traversal != TraversalKind.None);
            _actionBlend = Mathf.MoveTowards(_actionBlend, action ? 1f : 0f, dt * 8f);
            float groundWeight = (1f - _swimBlend) * (1f - _actionBlend);
            for (int i = 0; i < 3; i++) Graph.BaseMixer.SetInputWeight(i,
                Graph.BaseMixer.GetInputWeight(i) * (1f - _actionBlend) * (1f - _crouchBlend - _crawlBlend));
            for (int i = 0; i < _traversalClips.Length; i++) Graph.BaseMixer.SetInputWeight(_traversalOffset + i, 0f);
            Graph.BaseMixer.SetInputWeight(_traversalOffset, groundWeight * _crouchBlend * (1f - moving));
            Graph.BaseMixer.SetInputWeight(_traversalOffset + 1, groundWeight * _crouchBlend * moving);
            Graph.BaseMixer.SetInputWeight(_traversalOffset + 2, groundWeight * _crawlBlend * (1f - moving));
            Graph.BaseMixer.SetInputWeight(_traversalOffset + 3, groundWeight * _crawlBlend * moving);
            if (action)
            {
                _actionIndex = _traversal switch { TraversalKind.Vault => 5, TraversalKind.Hanging => 6,
                    TraversalKind.HangOutwardLeft when _ledgeCorners != null => 12,
                    TraversalKind.HangOutwardRight when _ledgeCorners != null => 13,
                    TraversalKind.HangInwardLeft when _ledgeCorners != null => 14,
                    TraversalKind.HangInwardRight when _ledgeCorners != null => 15,
                    TraversalKind.HangLeft when _hangLeftMotion != null => 10, TraversalKind.HangRight when _hangRightMotion != null => 11,
                    TraversalKind.ClimbUp => 7, TraversalKind.DropToHang => 7, TraversalKind.StepUp => 8, TraversalKind.JumpGrab when UseAuthoredJumpGrab && _jumpGrabMotion != null => 9, _ => 4 };
                if (_traversal != TraversalKind.Hanging)
                    _traversalClips[_actionIndex].SetTime(_traversal != TraversalKind.None
                        ? (_traversal == TraversalKind.DropToHang ? 1f - _traversalProgress : _traversalProgress) * _traversalLengths[_actionIndex] : Mathf.Min(_airTime, _traversalLengths[4] * .95f));
            }
            // Retain every outgoing action weight when another transition interrupts a blend.
            for (int i = 4; i < _traversalClips.Length; i++)
            {
                _actionWeights[i] = Mathf.Lerp(_actionWeights[i], i == _actionIndex ? 1f : 0f, Mathf.Clamp01(dt / .12f));
                Graph.BaseMixer.SetInputWeight(_traversalOffset + i, (1f - _swimBlend) * _actionBlend * _actionWeights[i]);
            }
            if (_actionIndex == 10) _hangLeftPhase = _traversalProgress;
            if (_actionIndex == 11) _hangRightPhase = _traversalProgress;
            if (_jumpGrabMotion != null && _actionIndex == 9) _jumpGrabPhase = _traversalProgress;
            if (_stepUpMotion != null && _actionIndex == 8) _stepUpPhase = _traversalProgress;
            if (_vaultMotion != null && _actionIndex == 5)
            {
                _vaultPhase = _traversalProgress;
                _traversalClips[5].SetTime(_vaultPhase * _traversalLengths[5]);
            }
        }
        _walk.SetTime(_phase * _walkLength); _run.SetTime(_phase * _runLength);
        float standingRun = Graph.BaseMixer.GetInputWeight(2);
        if (_directionalClips != null)
        {
            float front = _directionWeights.x, left = _directionWeights.y, right = _directionWeights.z, back = _directionWeights.w;
            float standing = Graph.BaseMixer.GetInputWeight(1) + Graph.BaseMixer.GetInputWeight(2);
            float runStanding = _runDirectional != null ? standingRun : 0f;
            float walkStanding = standing - runStanding;
            float crouched = _traversalClips != null ? Graph.BaseMixer.GetInputWeight(_traversalOffset + 1) : 0f;
            Vector4 standingDirections = _walkDiagonals != null ? _walkCardinalWeights : _directionWeights;
            Graph.BaseMixer.SetInputWeight(1, Graph.BaseMixer.GetInputWeight(1) * standingDirections.x);
            Graph.BaseMixer.SetInputWeight(2, Graph.BaseMixer.GetInputWeight(2) * standingDirections.x);
            if (_traversalClips != null) Graph.BaseMixer.SetInputWeight(_traversalOffset + 1, crouched * front);
            for (int i = 0; i < 6; i++)
            {
                float direction = i % 3 == 0 ? left : i % 3 == 1 ? right : back;
                Graph.BaseMixer.SetInputWeight(_directionalOffset + i, i < 3 ? standingDirections[i + 1] * walkStanding : direction * crouched);
                _directionalClips[i].SetTime(_phase * _directionalLengths[i]);
            }
            if (_walkDiagonals != null) for (int i = 0; i < 4; i++)
            {
                Graph.BaseMixer.SetInputWeight(_walkDiagonalOffset + i, walkStanding * _walkDiagonalWeights[i] * (_fallOffset >= 0 ? 1f - _fallBlend : 1f));
                _walkDiagonals[i].SetTime(_phase * _walkDiagonalLengths[i]);
            }
        }
        if (_runDirectional != null)
        {
            for (int i = 0; i < _runDirectional.Length; i++)
            {
                float direction = i < 3 ? _walkCardinalWeights[i + 1] : _walkDiagonalWeights[i - 3];
                Graph.BaseMixer.SetInputWeight(_runDirectionalOffset + i, standingRun * direction);
                _runDirectional[i].SetTime(_phase * _runDirectionalLengths[i]);
            }
        }
        if (_fallOffset >= 0)
        {
            for (int i = 0; i < _fallOffset; i++)
                Graph.BaseMixer.SetInputWeight(i, Graph.BaseMixer.GetInputWeight(i) * (1f - _fallBlend));
            Graph.BaseMixer.SetInputWeight(_fallOffset, _fallBlend);
        }
        if (_swimDirectional != null)
        {
            float strokeWeight = Graph.BaseMixer.GetInputWeight(4) + Graph.BaseMixer.GetInputWeight(6);
            Graph.BaseMixer.SetInputWeight(4, Graph.BaseMixer.GetInputWeight(4) * _directionWeights.x);
            Graph.BaseMixer.SetInputWeight(6, Graph.BaseMixer.GetInputWeight(6) * _directionWeights.x);
            _swimPhase += dt * Mathf.Max(.25f, speed / _walkSpeed);
            Graph.BaseMixer.GetInput(4).SetTime(_swimPhase * _surfaceMoveLength);
            Graph.BaseMixer.GetInput(6).SetTime(_swimPhase * _underwaterMoveLength);
            for (int i = 0; i < 3; i++)
            {
                Graph.BaseMixer.SetInputWeight(_swimDirectionalOffset + i, strokeWeight * _directionWeights[i + 1]);
                _swimDirectional[i].SetTime(_swimPhase * _swimDirectionalLengths[i]);
            }
        }
        if (_wadeOffset >= 0)
        {
            float depthBlend = _grounded && !swimming && _stance == ActorStance.Standing && _traversal == TraversalKind.None && !_jumping && waterDepth.HasValue
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.1f, .65f, waterDepth.Value)) * moving : 0f;
            _wadeBlend = Mathf.MoveTowards(_wadeBlend, depthBlend, dt * 4f);
            for (int i = 0; i < _wadeOffset; i++) Graph.BaseMixer.SetInputWeight(i, Graph.BaseMixer.GetInputWeight(i) * (1f - _wadeBlend));
            Graph.BaseMixer.SetInputWeight(_wadeOffset, _wadeBlend);
            if (_walkDiagonals != null) for (int i = 0; i < 4; i++)
                Graph.BaseMixer.SetInputWeight(_walkDiagonalOffset + i, Graph.BaseMixer.GetInputWeight(_walkDiagonalOffset + i) * (1f - _wadeBlend));
            // Water resistance slows the motor and this shared stride phase together.
            _wade.SetTime(_phase * _wadeLength);
        }
        if (_stairs != null)
        {
            Vector2 desired = Vector2.zero;
            Vector3 travel = Vector3.ProjectOnPlane(velocity, up);
            if (_grounded && !swimming && _stance == ActorStance.Standing && _traversal == TraversalKind.None &&
                !_jumping && ground != null && travel.sqrMagnitude > .01f && Vector3.Dot(travel.normalized, _frame.forward) > .9f)
            {
                Vector3 direction = travel.normalized;
                if (ground.TryGround(_frame.position + direction * .22f + up * .5f, -up, 0f, out var ahead) &&
                    ground.TryGround(_frame.position - direction * .22f + up * .5f, -up, 0f, out var behind) &&
                    Vector3.Dot(ahead.Normal, up) > .995f && Vector3.Dot(behind.Normal, up) > .995f)
                {
                    float rise = Vector3.Dot(ahead.Position - behind.Position, up);
                    if (Mathf.Abs(rise) >= .08f && Mathf.Abs(rise) <= .35f)
                        desired = rise > 0f ? Vector2.right : Vector2.up;
                }
                if (desired.sqrMagnitude > 0f) { _stairDirection = desired; _stairHold = .15f; }
                else if (_stairHold > 0f) desired = _stairDirection;
            }
            else _stairHold = 0f;
            _stairHold = Mathf.Max(0f, _stairHold - dt);
            StairBlend = Vector2.Lerp(StairBlend, desired, 1f - Mathf.Exp(-8f * dt));
            float forwardWeight = Graph.BaseMixer.GetInputWeight(1);
            Graph.BaseMixer.SetInputWeight(1, forwardWeight * (1f - StairBlend.x - StairBlend.y));
            for (int i = 0; i < 2; i++)
            {
                Graph.BaseMixer.SetInputWeight(_stairOffset + i, forwardWeight * StairBlend[i]);
                _stairs[i].SetTime(_phase * _stairLengths[i]);
            }
        }
        if (_fastSurfaceSwimOffset >= 0)
        {
            _fastSurfaceSwimBlend = Mathf.MoveTowards(_fastSurfaceSwimBlend, fastSwimming && swimming && !diving ? 1f : 0f, dt * 5f);
            float forwardWeight = Graph.BaseMixer.GetInputWeight(4);
            float fastWeight = forwardWeight * _fastSurfaceSwimBlend * (_swimDirectional != null ? 1f : _directionWeights.x);
            Graph.BaseMixer.SetInputWeight(4, forwardWeight - fastWeight);
            Graph.BaseMixer.SetInputWeight(_fastSurfaceSwimOffset, fastWeight);
            double swimCycle = _swimDirectional != null ? _swimPhase : Graph.BaseMixer.GetInput(4).GetTime() / _surfaceMoveLength;
            _fastSurfaceSwim.SetTime(swimCycle * _fastSurfaceSwimLength);
        }
        if (_sharedSurfaceIdle) Graph.BaseMixer.GetInput(3).SetTime(Graph.BaseMixer.GetInput(4).GetTime());
        if (_crawlDirectional.Length > 0)
        {
            float forwardWeight = Graph.BaseMixer.GetInputWeight(_traversalOffset + 3);
            float remaining = forwardWeight;
            double cycle = Graph.BaseMixer.GetInput(_traversalOffset + 3).GetTime() / _traversalLengths[3];
            for (int i = 0; i < _crawlDirectional.Length; i++)
            {
                float weight = forwardWeight * _directionWeights[_crawlDirectionIndices[i]];
                remaining -= weight;
                Graph.BaseMixer.SetInputWeight(_crawlDirectionalOffset + i, weight);
                _crawlDirectional[i].SetTime(cycle * _crawlDirectionalLengths[i]);
            }
            Graph.BaseMixer.SetInputWeight(_traversalOffset + 3, Mathf.Max(0f, remaining));
        }
        if (_crawlTransition != null)
        {
            bool requested = _stance == ActorStance.Crawling;
            bool allowed = _grounded && !swimming && _traversal == TraversalKind.None;
            if (allowed && requested != _crawlRequested)
            {
                _crawlTransition.Begin("CrawlTransition", "Humanoid");
                _crawlTransitionPhase = requested ? "Enter" : _stance == ActorStance.Crouching ? "ExitToCrouch" : "Exit";
                var transition = _crawlTransition.ActivePerformance.GetPhase(_crawlTransitionPhase);
                _crawlTransitionDuration = transition.Clip.length * (transition.EndNormalized - transition.StartNormalized);
                _crawlTransitionTime = 0f;
            }
            _crawlRequested = requested;
            if (allowed && _crawlTransitionPhase != null)
            {
                _crawlTransition.ApplyPhase(_crawlTransitionPhase, _crawlTransitionTime / _crawlTransitionDuration, dt);
                if (_crawlTransitionTime >= _crawlTransitionDuration) _crawlTransitionPhase = null;
                _crawlTransitionTime = Mathf.Min(_crawlTransitionDuration, _crawlTransitionTime + dt);
            }
            else { _crawlTransitionPhase = null; _crawlTransition.Clear(dt); }
            for (int i = 0; i < _crawlTransitionOffset; i++)
                Graph.BaseMixer.SetInputWeight(i, Graph.BaseMixer.GetInputWeight(i) * (1f - _crawlTransition.Weight));
        }
        if (_performance != null)
        {
            string phase = Airborne.Phase switch
            {
                ActorAirbornePhase.Ascent => "Ascent", ActorAirbornePhase.Apex => "Apex",
                ActorAirbornePhase.Descent => "Descent", ActorAirbornePhase.Landing => _jumpLandingPhase, _ => null
            };
            bool landingPreparation = Airborne.Phase == ActorAirbornePhase.Descent && Airborne.DescentProgress >= .65f &&
                _jumpLandingPhase == "Landing" && _performance.ActivePerformance?.GetPhase("LandingPrepare") != null;
            if (landingPreparation) phase = "LandingPrepare";
            var authored = phase != null && Airborne.IntentionalJump ? _performance.ActivePerformance?.GetPhase(phase) : null;
            if (_hopPreparation >= 0f) _performance.ApplyPhase("Preparation", _hopPreparation, dt);
            else if (_interaction && _interactionPhase != null)
                _performance.ApplyPhase(_interactionPhase, _interactionProgress, dt);
            else if (authored != null && !_interaction)
            {
                float duration = authored.Clip.length * (authored.EndNormalized - authored.StartNormalized);
                float progress = landingPreparation ? Mathf.InverseLerp(.65f, 1f, Airborne.DescentProgress) :
                    Airborne.Phase == ActorAirbornePhase.Ascent ? Airborne.AscentProgress :
                    Airborne.Phase == ActorAirbornePhase.Descent ? Airborne.DescentProgress : Airborne.PhaseTime / duration;
                _performance.ApplyPhase(phase, progress, dt);
            }
            else _performance.Clear(dt);
            for (int i = 0; i < _performanceOffset; i++)
                Graph.BaseMixer.SetInputWeight(i, Graph.BaseMixer.GetInputWeight(i) * (1f - _performance.Weight));
        }
        Graph.BlendBaseWeights(dt);
        if (!evaluatePose) { _skipped = true; return; }
        Pose.RestoreAnimation();
        if (_skipped) { Pose.Reset(); _skipped = false; }
        Graph.Evaluate();
        Pose.CaptureAnimation();
        if (_jumpGrabMotion != null)
            _body.position -= _frame.TransformVector(_jumpGrabMotion.Sample(_jumpGrabPhase).PoseOffset) * Graph.BaseMixer.GetInputWeight(_traversalOffset + 9);
        if (_stepUpMotion != null)
            _body.position -= _frame.TransformVector(_stepUpMotion.Sample(_stepUpPhase).PoseOffset) * Graph.BaseMixer.GetInputWeight(_traversalOffset + 8);
        if (_vaultMotion != null)
            _body.position -= _frame.TransformVector(_vaultMotion.Sample(_vaultPhase).Root) *
                Graph.BaseMixer.GetInputWeight(_traversalOffset + 5);
        if (_traversalClips != null)
        {
            // Imported traversal clips can contain their source platform height in the body pose.
            // The collision trajectory supplies that height; keep the lowest foot at the motor frame.
            float footHeight = Mathf.Min(Vector3.Dot(_leftFoot.position - _frame.position, up),
                Vector3.Dot(_rightFoot.position - _frame.position, up));
            float platformWeight = (_vaultMotion == null ? Graph.BaseMixer.GetInputWeight(_traversalOffset + 5) : 0f) +
                Graph.BaseMixer.GetInputWeight(_traversalOffset + 7) + (_stepUpMotion == null ? Graph.BaseMixer.GetInputWeight(_traversalOffset + 8) : 0f);
            _body.position -= up * ((footHeight - .08f) * platformWeight);
        }
        float desiredHandWeight = 0f;
        Vector3 desiredBodyCorrection = Vector3.zero;
        bool cornerTravel = _traversal >= TraversalKind.HangOutwardLeft && _traversal <= TraversalKind.HangInwardRight;
        bool hangingTravel = _traversal == TraversalKind.HangLeft || _traversal == TraversalKind.HangRight;
        if (_ledgeCorners != null && _ledge.HasValue)
            for (int i = 0; i < _ledgeCorners.Length; i++)
                _body.position += (_ledge.Value - _frame.position - _frame.TransformVector(_ledgeCorners[i].ReferenceEdge)) *
                    Graph.BaseMixer.GetInputWeight(_traversalOffset + 12 + i);
        if (_hangLeftMotion != null && _ledge.HasValue)
        {
            for (int index = 10; index <= 11; index++)
            {
                var motion = index == 10 ? _hangLeftMotion : _hangRightMotion;
                float phase = index == 10 ? _hangLeftPhase : _hangRightPhase;
                Vector3 offset = _ledge.Value - _frame.position - _frame.TransformVector(motion.ReferenceEdge + motion.Sample(phase).Root);
                _body.position += offset * Graph.BaseMixer.GetInputWeight(_traversalOffset + index);
            }
        }
        if (_traversalClips != null && _ledge.HasValue)
        {
            float pullContact = _traversal == TraversalKind.ClimbUp ?
                1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.45f, .8f, _traversalProgress)) :
                _traversal == TraversalKind.DropToHang ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.2f, .55f, _traversalProgress)) : 0f;
            float grabContact = _traversal == TraversalKind.JumpGrab ?
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(UseAuthoredJumpGrab ? .45f : .65f, UseAuthoredJumpGrab ? .6f : 1f, _traversalProgress)) : 0f;
            float hangContact = _traversal == TraversalKind.Hanging || hangingTravel || cornerTravel ? 1f : _traversal == TraversalKind.ClimbUp ? pullContact : 0f;
            desiredHandWeight = Mathf.Clamp01(Graph.BaseMixer.GetInputWeight(_traversalOffset + 6) * hangContact +
                Graph.BaseMixer.GetInputWeight(_traversalOffset + 7) * pullContact +
                (Graph.BaseMixer.GetInputWeight(_traversalOffset + 4) + (_jumpGrabMotion != null ? Graph.BaseMixer.GetInputWeight(_traversalOffset + 9) : 0f)) * grabContact);
            if (_traversal == TraversalKind.Hanging || hangingTravel || cornerTravel)
                desiredHandWeight = Mathf.Clamp01(desiredHandWeight + Graph.BaseMixer.GetInputWeight(_traversalOffset + 7) +
                    Graph.BaseMixer.GetInputWeight(_traversalOffset + 4) + (_jumpGrabMotion != null ? Graph.BaseMixer.GetInputWeight(_traversalOffset + 9) : 0f));
            desiredBodyCorrection = (_ledge.Value - (_leftHand.position + _rightHand.position) * .5f) * desiredHandWeight;
            if (_hangLeftMotion != null && (_traversal == TraversalKind.Hanging || hangingTravel || cornerTravel))
            {
                float idleWeight = Graph.BaseMixer.GetInputWeight(_traversalOffset + 6);
                if (_traversal == TraversalKind.Hanging && idleWeight > .999f)
                { _settledHangCorrection = _frame.InverseTransformVector(desiredBodyCorrection); _hasSettledHangCorrection = true; }
                if (_hasSettledHangCorrection) desiredBodyCorrection = _frame.TransformVector(_settledHangCorrection) * idleWeight;
            }
            if (_traversal == TraversalKind.Vault)
            {
                float plant = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.3f, .42f, _traversalProgress));
                float release = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, .7f, _traversalProgress));
                desiredHandWeight = (_vaultMotion != null ? _vaultMotion.Sample(_vaultPhase).AnchorWeight : plant * release) *
                    Graph.BaseMixer.GetInputWeight(_traversalOffset + 5);
                desiredBodyCorrection = Vector3.zero;
            }
        }
        float contactBlend = 1f - Mathf.Exp(-18f * dt);
        TraversalHandWeight = _traversal != TraversalKind.None ? desiredHandWeight : Mathf.MoveTowards(TraversalHandWeight, 0f, dt * 8f);
        _traversalBodyCorrection = _traversal != TraversalKind.None ? desiredBodyCorrection :
            TraversalHandWeight > 0f ? Vector3.Lerp(_traversalBodyCorrection, Vector3.zero, contactBlend) : Vector3.zero;
        _body.position += _traversalBodyCorrection;
        // Keep the contact shape compatible across idle, travel, and corners. Dropping the bend hint
        // during a handoff releases IK first and briefly exposes the uncorrected elbow inside the wall.
        Vector3 HangBend(bool left) => (_frame.right * (left ? -.25f : .25f) - up - _frame.forward * .1f).normalized;
        Quaternion ledgeGrip = Quaternion.LookRotation(-up, _frame.forward);
        Vector3 gripOffset = up * .015f + _frame.forward * .015f;
        if (cornerTravel && _ledge.HasValue)
        {
            // Probe both fixed faces, not the actor's rotating forward axis.
            var cornerMotion = _ledgeCorners[(int)_traversal - (int)TraversalKind.HangOutwardLeft];
            Vector3 startForward = Quaternion.AngleAxis(-cornerMotion.Sample(_traversalProgress).RootYawDegrees, up) * _frame.forward;
            Vector3 endForward = Quaternion.AngleAxis(cornerMotion.Sample(1f).RootYawDegrees, up) * startForward;
            foreach (string id in new[] { "LeftHand", "RightHand" })
            {
                if (!Pose.TryGetInteractionContactPose(id, out var hand)) continue;
                Vector3 target = hand.position; Quaternion rotation = hand.rotation; float weight = 0f;
                float nearest = .18f;
                int arm = id == "LeftHand" ? 0 : 1;
                // An outside corner must retain each authored elbow's bend plane. A shared outward
                // pole puts both elbows on the same side and can reverse the visible bend.
                Vector3 bend = _traversal == TraversalKind.HangOutwardLeft || _traversal == TraversalKind.HangOutwardRight
                    ? (_elbows[arm].position - _upperArms[arm].position).normalized
                    : -startForward - endForward - up * .25f;
                foreach (Vector3 direction in new[] { startForward, endForward })
                    if (Physics.Raycast(hand.position - direction * .2f - up * .10f, direction, out var wall, .45f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                        Mathf.Abs(Vector3.Dot(wall.normal, up)) < .1f &&
                        Physics.Raycast(wall.point - wall.normal * .03f + up * .4f, -up, out var top, .6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                        Vector3.Dot(top.normal, up) > .8f)
                    {
                        Vector3 candidate = wall.point + up * Vector3.Dot(top.point - wall.point, up) - wall.normal * .015f + up * .015f;
                        float distance = Vector3.Distance(candidate, hand.position);
                        if (distance >= nearest) continue;
                        nearest = distance; target = Vector3.MoveTowards(hand.position, candidate, .12f);
                        rotation = Quaternion.LookRotation(-up, -wall.normal); weight = 1f;
                    }
                Pose.SetInteractionTarget(id, new InteractionPoseTarget(target, rotation, weight, useContact: true, preserveAuthoredContactJoints: true, bendDirection: bend));
            }
            _traversalHandsActive = true;
        }
        else if (hangingTravel && _ledge.HasValue)
        {
            bool movingRight = _traversal == TraversalKind.HangRight;
            var motion = movingRight ? _hangRightMotion : _hangLeftMotion;
            Vector3 side = Vector3.Cross(up, _frame.forward);
            Vector3 startEdge = _ledge.Value - _frame.TransformVector(motion.Sample(_traversalProgress).Root);
            float first = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.48f, .60f, _traversalProgress));
            float second = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.42f, .55f, _traversalProgress));
            float stride = motion.Sample(1f).Root.x;
            SetSupport("LeftHand", startEdge + side * (movingRight ? -.20f : stride - .20f), movingRight ? first : second);
            SetSupport("RightHand", startEdge + side * (movingRight ? stride + .20f : .20f), movingRight ? second : first);
            _traversalHandsActive = true;

            void SetSupport(string id, Vector3 target, float weight)
            {
                target += gripOffset;
                if (Pose.TryGetInteractionContactPose(id, out var authored))
                    target = Vector3.MoveTowards(authored.position, target, .08f);
                Pose.SetInteractionTarget(id, new InteractionPoseTarget(target, ledgeGrip, weight,
                    useContact: true, preserveAuthoredContactJoints: true, bendDirection: HangBend(id == "LeftHand")));
            }
        }
        else if (TraversalHandWeight > .001f && _ledge.HasValue)
        {
            Vector3 right = Vector3.Cross(up, _frame.forward) * (.22f * _frame.lossyScale.x);
            if (_actionIndex == 5 && _vaultMotion != null)
            {
                Pose.SetInteractionTarget("LeftHand", _vaultContact == "LeftHand" ? new InteractionPoseTarget(_ledge.Value, null, TraversalHandWeight) : null);
                Pose.SetInteractionTarget("RightHand", _vaultContact == "RightHand" ? new InteractionPoseTarget(_ledge.Value, null, TraversalHandWeight) : null);
            }
            else
            {
                Pose.SetInteractionTarget("LeftHand", _traversal == TraversalKind.Vault ? null :
                    new InteractionPoseTarget(_ledge.Value - right + (_hangLeftMotion != null ? gripOffset : Vector3.zero), _hangLeftMotion != null ? ledgeGrip : (Quaternion?)null, TraversalHandWeight, useContact: _hangLeftMotion != null, preserveAuthoredContactJoints: _hangLeftMotion != null, bendDirection: _hangLeftMotion != null ? HangBend(true) : (Vector3?)null));
                Pose.SetInteractionTarget("RightHand", new InteractionPoseTarget(_ledge.Value + right + (_hangLeftMotion != null ? gripOffset : Vector3.zero), _hangLeftMotion != null ? ledgeGrip : (Quaternion?)null, TraversalHandWeight, useContact: _hangLeftMotion != null, preserveAuthoredContactJoints: _hangLeftMotion != null, bendDirection: _hangLeftMotion != null ? HangBend(false) : (Vector3?)null));
            }
            _traversalHandsActive = true;
        }
        else if (_traversalHandsActive)
        {
            Pose.SetInteractionTarget("LeftHand", null); Pose.SetInteractionTarget("RightHand", null);
            _traversalHandsActive = false;
        }
        // Authored actions can step even when motor input is zero. Preserve that travel and
        // derive support from the displayed foot height through the outgoing performance blend.
        float authoredMotion = _performance?.Weight ?? 0f;
        // Sample after traversal height correction, before procedural lowering and IK.
        for (int i = 0; i < _footWeights.Length; i++)
        {
            float footLift = Vector3.Dot(_footContacts[i].position - _frame.position, up) - _footSoles[i];
            float contact = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(.03f * _frame.lossyScale.x, .10f * _frame.lossyScale.x, footLift));
            _footWeights[i] = Mathf.Lerp(1f, contact, Mathf.Max(moving, Mathf.Max(_actionBlend, authoredMotion)));
            if (_stairs != null)
            {
                Vector3 localFoot = _frame.InverseTransformPoint(_footContacts[i].position);
                Vector3 localTravel = _frame.InverseTransformDirection(Vector3.ProjectOnPlane(velocity, up)).normalized;
                if (_stairFeetValid && dt > 0f)
                {
                    float speedAlong = Vector3.Dot(localFoot - _previousStairFeet[i], localTravel) / dt;
                    float stairContact = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.05f, .15f, speedAlong));
                    float weight = Graph.BaseMixer.GetInputWeight(_stairOffset) + Graph.BaseMixer.GetInputWeight(_stairOffset + 1);
                    _footWeights[i] = Mathf.Lerp(_footWeights[i], stairContact, weight);
                }
                _previousStairFeet[i] = localFoot;
            }
        }
        _stairFeetValid = dt > 0f;
        float lift = swimming && !diving && waterDepth.HasValue
            ? Mathf.Clamp(waterDepth.Value + .04f - Vector3.Dot(_head.position - _frame.position, up), 0f, .35f) : 0f;
        _surfaceLift = Mathf.Lerp(_surfaceLift, lift, 1f - Mathf.Exp(-12f * dt));
        _body.position += up * (_surfaceLift * _swimBlend * (1f - _diveBlend));
        float pitch = diving && velocity.sqrMagnitude > .01f
            ? -Mathf.Atan2(Vector3.Dot(velocity, up), Mathf.Max(.1f, speed)) * Mathf.Rad2Deg : 0f;
        _divePitch = Mathf.Lerp(_divePitch, Mathf.Clamp(pitch, -60f, 60f), 1f - Mathf.Exp(-6f * dt));
        _body.rotation = Quaternion.AngleAxis(_divePitch * _diveBlend, _frame.right) * _body.rotation;
        bool feet = Pose.FeetEnabled, look = Pose.LookEnabled;
        try
        {
            bool traversalFeet = _traversal == TraversalKind.StepUp ||
                ((_traversal == TraversalKind.ClimbUp || _traversal == TraversalKind.Vault) && _traversalProgress > .65f);
            bool supportFeet = _grounded && (_actionBlend < .01f || traversalFeet);
            bool settledLanding = _jumpLandingPhase == "LandingStop" && !_interaction && _grounded && speed < .05f &&
                (!Airborne.Active || Airborne.Phase == ActorAirbornePhase.Landing && Airborne.PhaseTime >= Airborne.RecoveryDuration * .5f);
            bool supportedApproachStep = _interaction && _interactionPhase == "ApproachStep" && _grounded;
            Pose.FeetEnabled = feet && supportFeet && _swimBlend < .01f && _crawlBlend < .01f && CrawlTransitionWeight < .01f;
            Pose.LookEnabled = look && _swimBlend < .01f && _actionBlend < .01f && CrawlTransitionWeight < .01f;
            Pose.Tick(up, lookTarget.HasValue ? lookTarget.Value - Pose.LookOrigin : _frame.forward,
                swimming || !supportFeet ? null : ground,
                (float)(_phase % 1d), authoredMotion > 0f ? Mathf.Max(.1f, moving) : moving,
                running, dt, _footWeights,
                lockFootContacts: StairBlend.x + StairBlend.y > .5f,
                preserveAnimatedFootTravel: !settledLanding && !supportedApproachStep);
        }
        finally { Pose.FeetEnabled = feet; Pose.LookEnabled = look; }
        float proneSupport = _grounded && !swimming && _traversal == TraversalKind.None
            ? Mathf.Max(_crawlBlend, CrawlTransitionWeight) *
                (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.4f, .8f,
                    Vector3.Dot(_body.position - _frame.position, up) / _frame.lossyScale.x))) : 0f;
        _crawlGrounding?.Tick(ground, up, proneSupport, dt, velocity);
    }

    void UpdateDirection(Vector3 velocity, float dt)
    {
        float x = Vector3.Dot(velocity, _frame.right), z = Vector3.Dot(velocity, _frame.forward);
        float total = Mathf.Abs(x) + Mathf.Abs(z);
        if (total > .01f)
        {
            _directionWeights = Vector4.Lerp(_directionWeights,
                new Vector4(Mathf.Max(0f, z), Mathf.Max(0f, -x), Mathf.Max(0f, x), Mathf.Max(0f, -z)) / total,
                1f - Mathf.Exp(-12f * dt));
            if (_walkDiagonals != null)
            {
                float ax = Mathf.Abs(x), az = Mathf.Abs(z);
                float diagonal = Mathf.Min(ax, az) / Mathf.Max(ax, az);
                Vector4 cardinalTarget = Vector4.zero, diagonalTarget = Vector4.zero;
                int cardinal = az >= ax ? (z >= 0f ? 0 : 3) : (x < 0f ? 1 : 2);
                int corner = z >= 0f ? (x < 0f ? 0 : 1) : (x < 0f ? 2 : 3);
                cardinalTarget[cardinal] = 1f - diagonal;
                diagonalTarget[corner] = diagonal;
                float blend = 1f - Mathf.Exp(-12f * dt);
                _walkCardinalWeights = Vector4.Lerp(_walkCardinalWeights, cardinalTarget, blend);
                _walkDiagonalWeights = Vector4.Lerp(_walkDiagonalWeights, diagonalTarget, blend);
                float sum = _walkCardinalWeights.x + _walkCardinalWeights.y + _walkCardinalWeights.z + _walkCardinalWeights.w +
                    _walkDiagonalWeights.x + _walkDiagonalWeights.y + _walkDiagonalWeights.z + _walkDiagonalWeights.w;
                _walkCardinalWeights /= sum; _walkDiagonalWeights /= sum;
            }
        }
    }

    public void Reset()
    {
        _walkCardinalWeights = new Vector4(1f, 0f, 0f, 0f); _walkDiagonalWeights = Vector4.zero;
        _fastSurfaceSwimBlend = 0f;
        StairBlend = _stairDirection = Vector2.zero; _stairHold = 0f; _stairFeetValid = false;
        _crawlTransition?.Reset(); _crawlRequested = false; _crawlTransitionPhase = null; _crawlTransitionTime = 0f;
        _crawlGrounding?.Reset();
        _crawlPhase = 0d;
        _vaultPhase = _stepUpPhase = _jumpGrabPhase = 0f;
        _hasSettledHangCorrection = false;
        Array.Clear(_actionWeights, 0, _actionWeights.Length); _actionWeights[4] = 1f; _actionIndex = 4;
        _speed = _moving = _swimBlend = _diveBlend = _surfaceLift = _divePitch = _crouchBlend = _crawlBlend = _actionBlend = _airTime = _fallDistance = _fallBlend = 0f;
        _phase = _swimPhase = 0d; _wadeBlend = 0f; _traversalBodyCorrection = Vector3.zero; _directionWeights = new Vector4(1f, 0f, 0f, 0f); _skipped = false;
        if (_traversalHandsActive)
        { Pose.SetInteractionTarget("LeftHand", null); Pose.SetInteractionTarget("RightHand", null); }
        _traversalHandsActive = false; TraversalHandWeight = 0f; _ledge = null;
        _hopPreparation = -1f; Airborne.Reset(); _performance?.Reset(); _performanceAttempted = false; _movementIntent = null; _jumpLandingPhase = "Landing"; EndInteraction();
        Graph.ResetBlend(); Pose.Reset();
    }
    public void Dispose()
    {
        Graph.Dispose();
        for (int i = 0; i < _skins.Length; i++)
            if (_skins[i] != null) _skins[i].forceMatrixRecalculationPerRender = _skinMatrixUpdates[i];
    }
}
