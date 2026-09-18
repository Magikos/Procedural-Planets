using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

/// <summary>Playable fishing fixture. Shared sessions, graphs, grip anchors, and inventory own their existing responsibilities.</summary>
public sealed class FishingInteractionReview : MonoBehaviour
{
    public HumanoidAnimationPrototype Actor;
    public ActorInteractionDefinition Definition;
    public ActorAnimationPerformanceLibrary PropPerformances;
    public Animator RodAnimator;
    public HeldToolGrip Grip;
    public Transform Stand, WaterTarget;
    public Animator FishAnimator;
    public AnimationClip FishClip;
    public Transform Hook;
    public Transform RodTip;
    public LineRenderer FishingLine;
    [Range(0, 1)] public float LateralFight = 1;
    public float ReelInput, GiveLineInput;
    public ActorFishingFight Fight { get; private set; } = new();
    public float FightWeight => _fightWeight;
    public ActorFishing Fishing { get; private set; } = new();
    public InventoryService Inventory { get; private set; } = new();
    ActorAnimationGraph _graph;
    ActorPerformancePlayback _prop;
    ProceduralPoseRig _pose;
    Vector3 _origin;
    bool _wasActive;
    ActorAnimationGraph _fishGraph;
    UnityEngine.Animations.AnimationClipPlayable _fishClip;
    float _fishWeight;
    Vector3 _lineEnd, _lineVelocity;
    float _lineGrip;
    bool _lineInitialized;
    Transform _look, _previousLook;
    Vector3 _waterPoint;
    Vector3 _retrieveFrom;
    bool _fighting;
    float _fightWeight;
    Transform[] _rodJoints;
    Quaternion[] _rodRest, _rodAuthored;
    Vector3 _previousTip;
    float _retrievalLength;
    bool _retrieving;

    void OnEnable()
    {
        if (Actor == null) return;
        Actor.PrepareInteraction += Tick; Actor.PresentInteraction += Present;
        Fishing.Caught += Reward;
        _previousLook = Actor.LookTarget;
        _look = new GameObject("Fishing gaze").transform;
        _look.SetParent(transform);
        _waterPoint = WaterTarget != null ? WaterTarget.position : transform.position;
        _look.position = _waterPoint;
        _rodJoints = new Transform[5]; _rodRest = new Quaternion[5]; _rodAuthored = new Quaternion[5];
        var bones = RodAnimator.GetComponentsInChildren<Transform>();
        for (int i = 0; i < 5; i++)
        {
            _rodJoints[i] = System.Array.Find(bones, t => t.name == "WoodenPole0" + (i + 1));
            _rodRest[i] = _rodJoints[i].localRotation;
        }
    }
    void Reward() => Inventory.Add("Fish");
    public bool Interact()
    {
        if (Fishing.Active) return Fishing.Hook();
        if (_prop == null || Actor == null || Actor.View == null || Stand == null || Definition == null ||
            WaterTarget == null || !WaterTarget.gameObject.activeInHierarchy ||
            Vector3.Distance(Actor.Actor.position, Stand.position) > .2f || Vector3.Dot(Actor.Actor.forward, Stand.forward) < .98f ||
            !Actor.Motor.Grounded || Actor.Motor.Swimming) return false;
        if (!Actor.View.BeginInteraction(Definition.Action)) return false;
        Fishing.Begin(Definition.Snapshot()); _origin = Actor.Actor.position;
        _fighting = false; _retrieveFrom = _waterPoint;
        _prop.Begin("FishingProp", "Prop"); _wasActive = true; return true;
    }
    public void Cancel() => Fishing.Cancel();
    public void ResetReview()
    {
        Fishing.Session.Cancel(); Inventory = new InventoryService(); _fishWeight = 0;
        _lineInitialized = false; _lineGrip = 0; _lineVelocity = Vector3.zero;
        _fighting = false; _fightWeight = 0; Fight = new ActorFishingFight();
        _retrieving = false;
        ReelInput = GiveLineInput = 0; _retrieveFrom = _waterPoint;
        if (FishAnimator != null) FishAnimator.transform.localScale = Vector3.zero;
        Actor.InteractionMovementLocked = false; Actor.InteractionPosition = Actor.InteractionForward = null;
        Actor.ResetActor(); Actor.VisitStation(0); _wasActive = false;
    }

    void Tick(float dt)
    {
        if (WaterTarget != null) _waterPoint = WaterTarget.position;
        if (_graph == null)
        {
            var data = PropPerformances.Snapshot();
            _graph = new ActorAnimationGraph(RodAnimator, "Fishing rod", ActorPerformancePlayback.CountInputs(data));
            _prop = new ActorPerformancePlayback(_graph, 0, data); _prop.Begin("FishingProp", "Prop");
            _prop.ApplyPhase("Cast", 0, 1); _graph.Evaluate();
            if (FishAnimator != null && FishClip != null)
            {
                _fishGraph = new ActorAnimationGraph(FishAnimator, "Caught fish", 1);
                _fishClip = _fishGraph.AddBaseClip(0, FishClip); _fishClip.SetSpeed(0);
                _fishGraph.BaseMixer.SetInputWeight(0, 1);
            }
        }
        if (_pose != Actor.View?.Pose)
        {
            if (_pose != null) _pose.BeforeCorrections -= FitHands;
            _pose = Actor.View?.Pose;
            if (_pose != null) _pose.BeforeCorrections += FitHands;
        }
        bool valid = WaterTarget != null && WaterTarget.gameObject.activeInHierarchy && Actor.Motor.Grounded &&
            Vector3.Distance(Actor.Actor.position, _origin) < .4f;
        if (Fishing.Active && Actor.View.PerformanceId != Definition.Action) Fishing.Session.Cancel();
        bool pulling = Fishing.Active && Fishing.Session.Phase.Name == "Pull";
        if (pulling && !_fighting)
        {
            Fight.Begin(_lineInitialized ? _lineEnd : _waterPoint, RodTip.position, Actor.Actor.up, Stand.forward);
            _fighting = true;
        }
        if (pulling) Fight.Tick(Mathf.Min(dt, 1), RodTip.position, Mathf.Clamp01(ReelInput), Mathf.Clamp01(GiveLineInput), LateralFight);
        if (_fighting) _retrieveFrom = Fight.Position;
        _fightWeight = Mathf.MoveTowards(_fightWeight, pulling ? 1 : 0, dt * 3);
        Fishing.Tick(dt, valid, _fighting && Fight.ReadyToLand);
        Actor.LookTarget = Fishing.Active ? _look : _previousLook;
        if (Fishing.Active)
        {
            var phase = Fishing.Session.Phase;
            Actor.InteractionMovementLocked = true; Actor.InteractionPosition = _origin; Actor.InteractionForward = Stand.forward;
            Actor.View.SetInteractionPhase(phase.Name, Fishing.Session.Progress);
            _prop.ApplyPhase(phase.Name, Fishing.Session.Progress, dt); _graph.Evaluate();
            for (int i = 0; i < _rodJoints.Length; i++) _rodAuthored[i] = _rodJoints[i].localRotation;
        }
        else if (_wasActive)
        {
            Actor.View.EndInteraction(); Actor.InteractionMovementLocked = false;
            Actor.InteractionPosition = Actor.InteractionForward = null; _wasActive = false;
        }
    }

    void FitHands(float dt)
    {
        if (_pose != null)
        {
            Vector3 towardFish = Vector3.ProjectOnPlane(Fight.Position - Actor.Actor.position, Actor.Actor.up);
            _pose.FacingOffsetDegrees = Mathf.Clamp(Vector3.SignedAngle(Actor.Actor.forward, towardFish, Actor.Actor.up), -12, 12) * _fightWeight;
            _pose.ForwardLeanDegrees = -5 * Fight.Tension * _fightWeight;
        }
        if (!Fishing.Active || !Actor.View.Pose.TryGetInteractionContactPose(Grip.PrimaryId, out var palm) ||
            !Grip.TryFit(palm.position, palm.rotation, out var fitted)) return;
        Grip.transform.SetPositionAndRotation(fitted.position, fitted.rotation);
        BendRod();
        if (Fishing.Session.Phase.Name != "Catch" && Grip.Secondary != null && Actor.View.Pose.TryGetInteractionContactPose(Grip.SecondaryId, out var support))
        {
            var target = Grip.SupportPoint(support.position, fitted);
            float error = Vector3.Distance(target, support.position);
            float weight = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.04f, Grip.MaximumSupportCorrection, error));
            Actor.View.Pose.SetInteractionTarget(Grip.SecondaryId, new InteractionPoseTarget(target, support.rotation, weight, useContact: true));
        }
    }
    void Present(float dt)
    {
        // Keep the tool in its primary hand through recovery; equipment stow remains separate work.
        if (_pose != null && _pose.TryGetInteractionContactPose(Grip.PrimaryId, out var palm) && Grip.TryFit(palm.position, palm.rotation, out var fitted))
            Grip.transform.SetPositionAndRotation(fitted.position, fitted.rotation);
        BendRod();
        string phase = Fishing.Active ? Fishing.Session.Phase.Name : "";
        float progress = Fishing.Session.Progress;
        PresentLine(phase, progress, dt);
        if (_fishGraph != null && _lineInitialized)
        {
            float visible = phase == "Pull" || phase == "Catch" && progress < .9f ? 1f : 0f;
            _fishWeight = Mathf.MoveTowards(_fishWeight, visible, dt * 5f);
            _fishClip.SetTime(phase == "Catch" ? progress * FishClip.length : 0);
            _fishGraph.Evaluate();
            // The mouth remains on the same line endpoint throughout retrieval.
            Quaternion hanging = Quaternion.LookRotation(Actor.Actor.forward, -Actor.Actor.up);
            Vector3 swim = Fight.Velocity.sqrMagnitude > .001f ? Fight.Velocity.normalized : Stand.forward;
            Quaternion swimming = Quaternion.LookRotation(Actor.Actor.up, -swim);
            FishAnimator.transform.SetPositionAndRotation(_lineEnd, Quaternion.Slerp(hanging, swimming, _fightWeight));
            FishAnimator.transform.localScale = Vector3.one * _fishWeight;
        }
    }

    void BendRod()
    {
        if (_rodJoints == null || _fightWeight <= 0) return;
        // The rod graph supplies the base pose. This is its sole final deformation pass.
        for (int i = 0; i < _rodJoints.Length; i++)
            _rodJoints[i].localRotation = Quaternion.Slerp(_rodAuthored[i], _rodRest[i], _fightWeight);
        Vector3 force = (Fight.Position - RodTip.position).normalized;
        for (int i = 0; i < _rodJoints.Length; i++)
        {
            Transform joint = _rodJoints[i];
            Vector3 axis = (i + 1 < _rodJoints.Length ? _rodJoints[i + 1].position : RodTip.position) - joint.position;
            Vector3 bent = Vector3.RotateTowards(axis.normalized, force, Mathf.Deg2Rad * (4 + i * 2) * Fight.Tension * _fightWeight, 0);
            joint.rotation = Quaternion.FromToRotation(axis, bent) * joint.rotation;
        }
    }

    void PresentLine(string phase, float progress, float dt)
    {
        if (RodTip == null || FishingLine == null || _pose == null) return;
        Vector3 up = Actor.Actor.up;
        Vector3 tip = RodTip.position;
        if (phase == "Catch" && !_retrieving)
        {
            _retrievalLength = Vector3.Distance(_previousTip, _lineEnd);
            _retrieving = true;
        }
        if (phase != "Catch") _retrieving = false;
        bool inWater = phase == "Wait" || phase == "Nibble" || phase == "Bite" || phase == "Pull";
        float cast = phase == "Cast" ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.4f, .85f, progress)) : 0;
        float securing = phase == "Catch" ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, .4f, progress)) *
            (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.82f, .95f, progress))) : 0;
        _lineGrip = Mathf.MoveTowards(_lineGrip, securing, dt * 4);
        _pose.TryGetInteractionContactPose("LeftHand", out var hand);
        Vector3 hanging = tip - up * 1.25f;
        Vector3 target = Vector3.Lerp(hanging, _waterPoint, inWater ? 1 : cast);
        if (phase == "Catch")
        {
            float retrieve = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .4f, progress));
            target = Vector3.Lerp(_retrieveFrom, hand.position - up * .28f, retrieve);
            target = Vector3.Lerp(target, hanging, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.94f, 1, progress)));
        }
        if (_lineGrip > 0) target = Vector3.Lerp(target, hand.position - up * .28f, _lineGrip);
        if (!_lineInitialized) { _lineEnd = target; _lineInitialized = true; }
        if (phase == "Pull") { _lineEnd = Fight.Position; _lineVelocity = Fight.Velocity; }
        else if (phase == "Catch")
        {
            // A taut line transfers the rod lift immediately. Do not add endpoint follow lag.
            _lineEnd = Vector3.Lerp(FishingLineGeometry.LimitReach(tip, target, _retrievalLength), target, _lineGrip);
            _lineVelocity = Vector3.zero;
        }
        else _lineEnd = Vector3.SmoothDamp(_lineEnd, target, ref _lineVelocity, .18f, 8f, dt);
        FishingLine.positionCount = 25;
        for (int i = 0; i < 25; i++)
        {
            float t = i / 24f;
            float sag = phase == "Catch" ? 0 : Mathf.Lerp(.035f, .3f * (1 - Fight.Tension), _fightWeight);
            Vector3 point = FishingLineGeometry.Point(tip, _lineEnd, hand.position, up, sag, _lineGrip, t);
            FishingLine.SetPosition(i, point);
        }
        _previousTip = tip;
        Vector3 look = Vector3.Lerp(phase == "Pull" ? Fight.Position : _retrieveFrom, hand.position - up * .2f, securing);
        if (phase != "Pull" && phase != "Catch") look = _waterPoint;
        _look.position = Vector3.Lerp(_look.position, look, 1 - Mathf.Exp(-dt * 6));
    }
    void Update()
    {
        if (Keyboard.current == null) return;
        if (Keyboard.current.eKey.wasPressedThisFrame) Interact();
        ReelInput = Keyboard.current.eKey.isPressed ? 1 : 0;
        GiveLineInput = Keyboard.current.qKey.isPressed ? 1 : 0;
        if (Keyboard.current.escapeKey.wasPressedThisFrame) Cancel();
    }
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width - 330, 10, 320, 130), GUI.skin.box);
        GUILayout.Label("Fishing: E cast / hook on bite; Escape cancel.");
        GUILayout.Label("Hold E to reel; hold Q to give line.");
        if (_fighting) GUILayout.Label($"Line {Fight.LineLength:F1} m | Tension {Fight.Tension:P0}");
        GUILayout.Label(Fishing.Status); GUILayout.Label("Fish: " + Inventory.Count("Fish"));
        GUILayout.EndArea();
    }
    void OnDisable()
    {
        if (Actor != null) { Actor.PrepareInteraction -= Tick; Actor.PresentInteraction -= Present; }
        if (_pose != null) _pose.BeforeCorrections -= FitHands;
        if (_pose != null) { _pose.FacingOffsetDegrees = 0; _pose.ForwardLeanDegrees = 0; }
        Fishing.Caught -= Reward; Fishing.Session.Cancel(); _graph?.Dispose(); _graph = null;
        _fishGraph?.Dispose(); _fishGraph = null;
        if (Actor != null && Actor.LookTarget == _look) Actor.LookTarget = _previousLook;
        if (_look != null) Destroy(_look.gameObject);
        if (Actor != null) { Actor.InteractionMovementLocked = false; Actor.InteractionPosition = Actor.InteractionForward = null; Actor.View?.EndInteraction(); }
    }
}
