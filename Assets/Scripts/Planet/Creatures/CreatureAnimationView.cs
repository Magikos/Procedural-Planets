using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public sealed class CreatureAnimationView : IDisposable
{
    readonly CreatureVisualDto _visual;
    readonly ActorAnimationGraph _graph;
    public ActorAnimationGraph Graph => _graph;
    readonly AnimationMixerPlayable _mixer;
    readonly AnimationClipPlayable _walk;
    readonly AnimationClipPlayable _run;
    readonly AnimationClipPlayable _stalk;
    float _stalkWeight;
    readonly AnimationClipPlayable _attack, _death;
    readonly Transform _body;
    readonly ProceduralRigDefinition _definition;
    readonly float _height;
    ActorDeathPose _deathPose;
    float _attackWeight, _deathTime;
    readonly float _lookPhase;
    float _elapsed, _eatWeight;
    float _speed;
    float _restWeight, _sleepWeight, _drinkWeight;
    readonly SwimmingPose _swimmingPose;
    float _swimWeight;
    float _idleSupport = 1f;
    readonly float[] _returningContacts;
    bool _poseSkipped;
    public int PoseEvaluations { get; private set; }
    public bool Swimming { get; set; }
    public float SwimWaterline { get; set; } = .7f;
    public float SwimWeight => _swimWeight;

    public Transform Root { get; }
    public ProceduralPoseRig Pose { get; private set; }
    public Vector3? LookTarget { get; set; }
    public bool Eating { get; set; }
    public float? AttackTime { get; set; }
    public bool Dead { get; set; }
    public bool DeathPoseSettled { get; private set; }
    public bool Resting { get; set; }
    public bool Sleeping { get; set; }
    public bool Drinking { get; set; }
    public bool Stalking { get; set; }
    public bool Threatening { get; set; }

    public static bool IsMale(ulong identity)
    {
        // Mix the whole identity: the low bits alone are the generation, initially zero for every animal.
        identity ^= identity >> 30;
        identity *= 0xbf58476d1ce4e5b9UL;
        identity ^= identity >> 27;
        identity *= 0x94d049bb133111ebUL;
        return ((identity ^ (identity >> 31)) & 1UL) == 0;
    }

    public CreatureAnimationView(Transform parent, ulong identity, CreatureVisualDto visual, float height)
    {
        _visual = visual;
        _height = height;
        _lookPhase = (identity % 997UL) / 997f * Mathf.PI * 2f;
        bool male = IsMale(identity);
        GameObject prefab = male ? visual.MalePrefab : visual.FemalePrefab;
        if (prefab == null || visual.Idle == null || visual.Walk == null || visual.Run == null)
            throw new ArgumentException("Creature visuals require both prefabs and idle, walk, and run clips.");

        Root = new GameObject(prefab.name).transform;
        Root.SetParent(parent, false);
        try
        {
            GameObject model = UnityEngine.Object.Instantiate(prefab, Root);
            model.transform.localPosition = new Vector3(0f, -height * 0.5f, 0f);
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * (height / visual.ModelHeightMeters);
            Animator animator = model.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid)
                throw new ArgumentException("Creature prefab requires an Animator with a valid Avatar.");
            _graph = new ActorAnimationGraph(animator, "Creature " + identity, 11);
            _mixer = _graph.BaseMixer;
            var idle = _graph.AddBaseClip(0, visual.Idle);
            _walk = _graph.AddBaseClip(1, visual.Walk);
            _run = _graph.AddBaseClip(2, visual.Run);
            var eat = _graph.AddBaseClip(3, visual.Eat != null ? visual.Eat : visual.Idle);
            _attack = _graph.AddBaseClip(4, visual.Attack != null ? visual.Attack : visual.Idle);
            _death = _graph.AddBaseClip(5, visual.Death != null ? visual.Death : visual.Idle);
            _attack.SetSpeed(0); _death.SetSpeed(0);
            var rest = _graph.AddBaseClip(6, visual.Rest != null ? visual.Rest : visual.Idle);
            var sleep = _graph.AddBaseClip(7, visual.Sleep != null ? visual.Sleep : visual.Idle);
            var drink = _graph.AddBaseClip(8, visual.Drink != null ? visual.Drink : visual.Eat != null ? visual.Eat : visual.Idle);
            _stalk = _graph.AddBaseClip(9, visual.Stalk != null ? visual.Stalk : visual.Walk);
            var definition = model.GetComponent<ProceduralRigDefinition>();
            _definition = definition;
            _returningContacts = definition != null ? new float[definition.Feet.Length] : null;
            // Legless swimmers retain lateral slither. Limbed swimmers paddle from a standing base, never a running clip.
            var swim = _graph.AddBaseClip(10, visual.Swim != null ? visual.Swim :
                definition != null && definition.Feet.Length == 0 ? visual.Walk : visual.Idle);
            if (visual.Swim == null && definition != null && definition.Feet.Length > 0) swim.SetSpeed(0);
            double phase = (identity % 997UL) / 997.0;
            idle.SetTime(phase * visual.Idle.length);
            _walk.SetTime(phase * visual.Walk.length);
            _run.SetTime(phase * visual.Run.length);
            _mixer.SetInputWeight(0, 1f);
            _graph.Evaluate();
            _body = definition != null ? definition.Body : null;
            if (definition != null)
            {
                Pose = new ProceduralPoseRig(Root, definition) { LookInfluence = 0f, BodyLeanEnabled = false };
                Pose.Reset();
                _swimmingPose = new SwimmingPose(Root, definition, height);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Tick(Vector3 velocity, Vector3 up, float dt, IGroundingProvider ground = null, bool evaluatePose = true)
    {
        if (_graph == null || !_graph.IsValid() || !Root.gameObject.activeInHierarchy) return;
        if (!float.IsFinite(dt) || dt < 0f) return;
        if (_deathPose != null)
        {
            if (Dead)
            {
                SettleDeath(ground, up, dt);
                return;
            }
            _deathPose = null;
            DeathPoseSettled = false;
            Pose?.Reset();
        }
        float target = Vector3.ProjectOnPlane(velocity, up).magnitude;
        _speed = Mathf.Lerp(_speed, target, 1f - Mathf.Exp(-12f * Mathf.Max(0f, dt)));
        // Blend out idle near rest. Playback speed already scales the stride frequency;
        // retaining idle throughout slow walking suppresses swing and prevents foot-anchor release.
        float moving = Mathf.InverseLerp(0.02f, Mathf.Max(0.04f, _visual.WalkMetersPerSecond * 0.1f), _speed);
        float running = Mathf.InverseLerp(_visual.WalkMetersPerSecond, _visual.RunMetersPerSecond, _speed);
        _elapsed += Mathf.Max(0f, dt);
        _eatWeight = Mathf.MoveTowards(_eatWeight, Eating && _visual.Eat != null && target < 0.02f ? 1f : 0f,
            Mathf.Max(0f, dt) * 3f);
        _mixer.SetInputWeight(0, (1f - moving) * (1f - _eatWeight));
        _mixer.SetInputWeight(3, (1f - moving) * _eatWeight);
        _mixer.SetInputWeight(1, moving * (1f - running));
        _mixer.SetInputWeight(2, moving * running);
        _restWeight = Mathf.MoveTowards(_restWeight, (Resting || Sleeping) && target < 0.02f ? 1f : 0f, dt * 1.5f);
        _sleepWeight = Mathf.MoveTowards(_sleepWeight, Sleeping ? 1f : 0f, dt);
        _drinkWeight = Mathf.MoveTowards(_drinkWeight, Drinking && target < 0.02f ? 1f : 0f, dt * 3f);
        for (int i = 0; i < 4; i++) _mixer.SetInputWeight(i, _mixer.GetInputWeight(i) * (1f - _restWeight) * (1f - _drinkWeight));
        _mixer.SetInputWeight(6, _restWeight * (1f - _sleepWeight) * (1f - _drinkWeight));
        _mixer.SetInputWeight(7, _restWeight * _sleepWeight * (1f - _drinkWeight));
        _mixer.SetInputWeight(8, _drinkWeight);
        _stalkWeight = Mathf.MoveTowards(_stalkWeight, (Stalking || Threatening) && _visual.Stalk != null && !Dead ? 1f : 0f, dt * 3f);
        for (int i = 0; i < 9; i++) _mixer.SetInputWeight(i, _mixer.GetInputWeight(i) * (1f - _stalkWeight));
        _mixer.SetInputWeight(9, _stalkWeight);
        _attackWeight = Mathf.MoveTowards(_attackWeight, AttackTime.HasValue && _visual.Attack != null && !Dead ? 1f : 0f,
            Mathf.Max(0f, dt) * 10f);
        if (AttackTime.HasValue && _visual.Attack != null)
            _attack.SetTime(Mathf.Clamp(AttackTime.Value, 0f, _visual.Attack.length));
        if (Dead) _deathTime += Mathf.Max(0f, dt); else _deathTime = 0f;
        if (_visual.Death != null) _death.SetTime(Mathf.Min(_deathTime, _visual.Death.length));
        float deathWeight = Dead && _visual.Death != null ? 1f : 0f;
        for (int i = 0; i < 4; i++) _mixer.SetInputWeight(i, _mixer.GetInputWeight(i) * (1f - _attackWeight) * (1f - deathWeight));
        for (int i = 6; i < 10; i++) _mixer.SetInputWeight(i, _mixer.GetInputWeight(i) * (1f - _attackWeight) * (1f - deathWeight));
        _mixer.SetInputWeight(4, _attackWeight * (1f - deathWeight));
        _mixer.SetInputWeight(5, deathWeight);
        _swimWeight = Dead ? 0f : Mathf.MoveTowards(_swimWeight, Swimming ? 1f : 0f, Mathf.Max(0f, dt) * 4f);
        for (int i = 0; i < 10; i++) _mixer.SetInputWeight(i, _mixer.GetInputWeight(i) * (1f - _swimWeight));
        _mixer.SetInputWeight(10, _swimWeight);
        // Both moving clips traverse one gait cycle together while their weights change.
        float phaseRate = _speed / Mathf.Max(0.1f, Mathf.Lerp(_visual.WalkMetersPerSecond * _visual.Walk.length,
            _visual.RunMetersPerSecond * _visual.Run.length, running));
        _walk.SetSpeed(phaseRate * _visual.Walk.length);
        _run.SetSpeed(phaseRate * _visual.Run.length);
        _stalk.SetSpeed(0);
        _stalk.SetTime(Threatening ? 0 : _walk.GetTime() / _visual.Walk.length * (_visual.Stalk != null ? _visual.Stalk.length : _visual.Walk.length));
        _graph.Advance(Mathf.Max(0f, dt));
        _graph.BlendBaseWeights(dt);
        if (!evaluatePose && !Dead)
        {
            _poseSkipped = true;
            return;
        }
        Pose?.RestoreAnimation();
        if (_poseSkipped) { Pose?.Reset(); _poseSkipped = false; }
        _graph.Evaluate();
        PoseEvaluations++;
        // Imported death clips contain lateral root travel. Keep the carcass over its authority-owned source position.
        if (Dead && _body != null)
            _body.position -= Vector3.ProjectOnPlane(_body.position - Root.position, up) * _mixer.GetInputWeight(5);
        float displayedSwim = _mixer.GetInputWeight(10);
        if (Pose != null)
        {
            Pose.CaptureAnimation();
            if (displayedSwim > .001f && _visual.Swim == null)
                _swimmingPose?.Apply(_elapsed + _lookPhase, displayedSwim, SwimWaterline);
            // Authored idle and action clips supply their own gaze. Only an explicit
            // world target adds a correction, weighted by the displayed locomotion pose.
            // Feeding clips supply their own lowered stance; idle anchors must release through the displayed blend.
            bool releasingSupport = Dead || Resting || Sleeping || _restWeight >= .01f || displayedSwim > .001f ||
                _mixer.GetInputWeight(3) > .001f || _mixer.GetInputWeight(8) > .001f;
            Pose.LookInfluence = LookTarget.HasValue && !releasingSupport ? _mixer.GetInputWeight(0) +
                _mixer.GetInputWeight(1) + _mixer.GetInputWeight(2) + _mixer.GetInputWeight(9) : 0f;
            Vector3 direction = LookTarget.HasValue ? LookTarget.Value - Pose.LookOrigin :
                Root.forward;
            // Keep ticking outgoing corrections while authored rest/death poses take over.
            // Reset would discard the displayed contact and gaze before they can release.
            bool spineEnabled = Pose.SpineEnabled;
            Pose.SpineEnabled = spineEnabled && !releasingSupport;
            try
            {
                // Keep outgoing authored gait in control until its displayed blend ends.
                // The shared pose layer starts idle replants below .1; retain that movement
                // threshold while any walk/run contribution remains, rather than using motor speed.
                float poseMoving = _mixer.GetInputWeight(1) + _mixer.GetInputWeight(2);
                poseMoving = poseMoving > 0f ? Mathf.Max(.1f, poseMoving) : moving;
                bool unsupported = releasingSupport || _attackWeight > .01f;
                // A returning idle stance must not acquire the full pelvis/foot correction in one tick.
                // Reuse contact weights to restore support; moving gaits retain their authored curves.
                _idleSupport = unsupported ? 0f : Mathf.MoveTowards(_idleSupport, 1f, dt * 3f);
                float[] contacts = null;
                if (!unsupported && poseMoving < .1f && _idleSupport < 1f)
                {
                    contacts = _returningContacts;
                    for (int i = 0; i < contacts.Length; i++) contacts[i] = Mathf.SmoothStep(0f, 1f, _idleSupport);
                }
                Pose.Tick(up, direction, unsupported ? null : ground,
                    Mathf.Repeat((float)(_walk.GetTime() / _visual.Walk.length), 1f), poseMoving, running, dt, footContacts: contacts,
                    preserveAnimatedFootTravel: true, secondaryWaterWeight: displayedSwim);
            }
            finally { Pose.SpineEnabled = spineEnabled; }
        }
        DeathPoseSettled = Dead && (_visual.Death == null || _deathTime >= _visual.Death.length);
        if (DeathPoseSettled && _visual.Death != null && ground != null && _body != null && _body != Root && _body.IsChildOf(Root))
        {
            _deathPose = new ActorDeathPose(Root, _definition, _height);
            DeathPoseSettled = false;
            SettleDeath(ground, up, dt);
        }
    }

    void SettleDeath(IGroundingProvider ground, Vector3 up, float dt)
    {
        if (DeathPoseSettled) return;
        _deathPose.Tick(ground, up, dt);
        if (!_deathPose.Settled) return;
        ActorSurfaceFit.ClearMeshPenetration(Root, _body, ground, up);
        _deathPose.CaptureFrozenPose();
        DeathPoseSettled = true;
    }

    public void Dispose()
    {
        _graph?.Dispose();
        if (Root != null)
        {
            Root.gameObject.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(Root.gameObject);
            else UnityEngine.Object.DestroyImmediate(Root.gameObject);
        }
    }
}
