using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public sealed class CreatureAnimationView : IDisposable
{
    readonly CreatureVisualDto _visual;
    readonly PlayableGraph _graph;
    readonly AnimationMixerPlayable _mixer;
    readonly AnimationClipPlayable _walk;
    readonly AnimationClipPlayable _run;
    readonly AnimationClipPlayable _attack, _death;
    float _attackWeight, _deathTime;
    readonly float _lookPhase;
    float _elapsed, _eatWeight;
    float _speed;
    float _restWeight, _sleepWeight, _drinkWeight;

    public Transform Root { get; }
    public ProceduralPoseRig Pose { get; private set; }
    public Vector3? LookTarget { get; set; }
    public bool Eating { get; set; }
    public float? AttackTime { get; set; }
    public bool Dead { get; set; }
    public bool Resting { get; set; }
    public bool Sleeping { get; set; }
    public bool Drinking { get; set; }

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
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _graph = PlayableGraph.Create("Creature " + identity);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_graph, "Pose", animator);
            _mixer = AnimationMixerPlayable.Create(_graph, 9);
            output.SetSourcePlayable(_mixer);
            var idle = AnimationClipPlayable.Create(_graph, visual.Idle);
            _walk = AnimationClipPlayable.Create(_graph, visual.Walk);
            _run = AnimationClipPlayable.Create(_graph, visual.Run);
            _graph.Connect(idle, 0, _mixer, 0);
            _graph.Connect(_walk, 0, _mixer, 1);
            _graph.Connect(_run, 0, _mixer, 2);
            var eat = AnimationClipPlayable.Create(_graph, visual.Eat != null ? visual.Eat : visual.Idle);
            _graph.Connect(eat, 0, _mixer, 3);
            _attack = AnimationClipPlayable.Create(_graph, visual.Attack != null ? visual.Attack : visual.Idle);
            _death = AnimationClipPlayable.Create(_graph, visual.Death != null ? visual.Death : visual.Idle);
            _attack.SetSpeed(0); _death.SetSpeed(0);
            _graph.Connect(_attack, 0, _mixer, 4);
            _graph.Connect(_death, 0, _mixer, 5);
            var rest = AnimationClipPlayable.Create(_graph, visual.Rest != null ? visual.Rest : visual.Idle);
            var sleep = AnimationClipPlayable.Create(_graph, visual.Sleep != null ? visual.Sleep : visual.Idle);
            var drink = AnimationClipPlayable.Create(_graph, visual.Drink != null ? visual.Drink : visual.Eat != null ? visual.Eat : visual.Idle);
            _graph.Connect(rest, 0, _mixer, 6);
            _graph.Connect(sleep, 0, _mixer, 7);
            _graph.Connect(drink, 0, _mixer, 8);
            double phase = (identity % 997UL) / 997.0;
            idle.SetTime(phase * visual.Idle.length);
            _walk.SetTime(phase * visual.Walk.length);
            _run.SetTime(phase * visual.Run.length);
            _mixer.SetInputWeight(0, 1f);
            _graph.Play();
            _graph.Evaluate(0f);
            var definition = model.GetComponent<ProceduralRigDefinition>();
            if (definition != null) Pose = new ProceduralPoseRig(Root, definition);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Tick(Vector3 velocity, Vector3 up, float dt, IGroundingProvider ground = null)
    {
        if (!_graph.IsValid() || !Root.gameObject.activeInHierarchy) return;
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
        _attackWeight = Mathf.MoveTowards(_attackWeight, AttackTime.HasValue && _visual.Attack != null && !Dead ? 1f : 0f,
            Mathf.Max(0f, dt) * 10f);
        if (AttackTime.HasValue && _visual.Attack != null)
            _attack.SetTime(Mathf.Clamp(AttackTime.Value, 0f, _visual.Attack.length));
        if (Dead) _deathTime += Mathf.Max(0f, dt); else _deathTime = 0f;
        if (_visual.Death != null) _death.SetTime(Mathf.Min(_deathTime, _visual.Death.length));
        float deathWeight = Dead && _visual.Death != null ? 1f : 0f;
        for (int i = 0; i < 4; i++) _mixer.SetInputWeight(i, _mixer.GetInputWeight(i) * (1f - _attackWeight) * (1f - deathWeight));
        for (int i = 6; i < 9; i++) _mixer.SetInputWeight(i, _mixer.GetInputWeight(i) * (1f - _attackWeight) * (1f - deathWeight));
        _mixer.SetInputWeight(4, _attackWeight * (1f - deathWeight));
        _mixer.SetInputWeight(5, deathWeight);
        // Both moving clips traverse one gait cycle together while their weights change.
        float phaseRate = _speed / Mathf.Max(0.1f, Mathf.Lerp(_visual.WalkMetersPerSecond * _visual.Walk.length,
            _visual.RunMetersPerSecond * _visual.Run.length, running));
        _walk.SetSpeed(phaseRate * _visual.Walk.length);
        _run.SetSpeed(phaseRate * _visual.Run.length);
        Pose?.RestoreAnimation();
        _graph.Evaluate(Mathf.Max(0f, dt));
        if (Pose != null && !Dead && _restWeight < 0.01f)
        {
            Pose.CaptureAnimation();
            float lookYaw = Mathf.Sin(_elapsed * 0.65f + _lookPhase) * 38f * (1f - moving) * (1f - _eatWeight) * (1f - _attackWeight);
            Vector3 direction = LookTarget.HasValue ? LookTarget.Value - Pose.LookOrigin :
                Quaternion.AngleAxis(lookYaw, up) * Root.forward;
            // Attack poses supply their own footwork; fade locomotion grounding instead of pinning a bite to walk contacts.
            Pose.Tick(up, direction, _attackWeight > 0.01f ? null : ground, Mathf.Repeat((float)(_walk.GetTime() / _visual.Walk.length), 1f),
                moving, running, dt);
        }
        else if (Pose != null) { Pose.CaptureAnimation(); Pose.Reset(); }
    }

    public void Dispose()
    {
        if (_graph.IsValid()) _graph.Destroy();
        if (Root != null)
        {
            Root.gameObject.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(Root.gameObject);
            else UnityEngine.Object.DestroyImmediate(Root.gameObject);
        }
    }
}

