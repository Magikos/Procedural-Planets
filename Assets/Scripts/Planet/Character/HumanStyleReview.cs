using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public sealed class HumanStyleReview : MonoBehaviour
{
    public Animator[] Characters = Array.Empty<Animator>();
    public AnimationClip[] Motions = Array.Empty<AnimationClip>();
    public GameObject[] Accessories = Array.Empty<GameObject>();
    public Animator[] Animals = Array.Empty<Animator>();
    public AnimationClip[] AnimalIdles = Array.Empty<AnimationClip>();
    public Transform[] Views = Array.Empty<Transform>();
    public Camera ReviewCamera;
    public bool Paused;
    public bool ShowSecondaryMotionControls;
    public bool SecondaryMotion = true;
    [Range(0f, 1f)] public float Phase;
    ActorAnimationGraph[] _graphs;
    AnimationClipPlayable[] _clips;
    Vector3[] _positions;
    Quaternion[] _rotations;
    int _motion;
    public int MotionIndex => _motion;
    Vector2 _scroll;

    void OnEnable()
    {
        if (Motions.Length > 0) SelectMotion(0);
    }

    public void SelectMotion(int index)
    {
        if (index < 0 || index >= Motions.Length || Motions[index] == null) throw new ArgumentOutOfRangeException(nameof(index));
        if (AnimalIdles.Length != Animals.Length) throw new InvalidOperationException("Each review animal requires an idle clip slot.");
        DisposeGraphs(); _motion = index; Phase = 0f;
        int count = Characters.Length + Animals.Length;
        _graphs = new ActorAnimationGraph[count]; _clips = new AnimationClipPlayable[count];
        _positions = new Vector3[count]; _rotations = new Quaternion[count];
        try
        {
            for (int i = 0; i < count; i++)
            {
                Animator animator = i < Characters.Length ? Characters[i] : Animals[i - Characters.Length];
                AnimationClip clip = i < Characters.Length ? Motions[index] : AnimalIdles[i - Characters.Length];
                if (animator == null || clip == null) continue;
                _positions[i] = animator.transform.position; _rotations[i] = animator.transform.rotation;
                _graphs[i] = new ActorAnimationGraph(animator, "Style review", 1);
                _clips[i] = _graphs[i].AddBaseClip(0, clip);
                _graphs[i].BaseMixer.SetInputWeight(0, 1f);
            }
            Sample();
        }
        catch { DisposeGraphs(); throw; }
    }

    void Update()
    {
        if (_graphs == null) return;
        if (!Paused) Phase = Mathf.Repeat(Phase + Time.deltaTime / Mathf.Max(.01f, Motions[_motion].length), 1f);
        Sample();
    }

    public void Sample()
    {
        if (_graphs == null) return;
        for (int i = 0; i < _graphs.Length; i++)
        {
            if (_graphs[i] == null) continue;
            _clips[i].SetTime(Phase * _clips[i].GetAnimationClip().length); _graphs[i].Evaluate();
            var animator = i < Characters.Length ? Characters[i] : Animals[i - Characters.Length];
            animator.transform.SetPositionAndRotation(_positions[i], _rotations[i]);
        }
    }

    public void SelectView(int index)
    {
        if (ReviewCamera != null && index >= 0 && index < Views.Length && Views[index] != null)
        {
            ReviewCamera.transform.SetPositionAndRotation(Views[index].position, Views[index].rotation);
            var fly = ReviewCamera.GetComponent<WorkbenchFlyCamera>();
            if (fly != null && fly.enabled) { fly.enabled = false; fly.enabled = true; }
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12, 12, 270, Screen.height - 24), GUI.skin.box);
        _scroll = GUILayout.BeginScrollView(_scroll);
        GUILayout.Label("HUMAN / SOURCE FITTING ROOM");
        GUILayout.Label("RMB + WASD: move camera. Edit attachment transforms in the Hierarchy.");
        Paused = GUILayout.Toggle(Paused, "Pause / scrub pose");
        if (ShowSecondaryMotionControls) SecondaryMotion = GUILayout.Toggle(SecondaryMotion, "Accessory secondary motion");
        Phase = GUILayout.HorizontalSlider(Phase, 0f, 1f);
        for (int i = 0; i < Views.Length; i++) if (GUILayout.Button(Views[i].name)) SelectView(i);
        GUILayout.Space(8); GUILayout.Label("Motion (both characters)");
        for (int i = 0; i < Motions.Length; i++)
            if (GUILayout.Button((_motion == i ? "> " : "") + Motions[i].name)) SelectMotion(i);
        GUILayout.Space(8); GUILayout.Label("Trial attachments (fit verdicts in report)");
        foreach (var accessory in Accessories)
            if (accessory != null) accessory.SetActive(GUILayout.Toggle(accessory.activeSelf, accessory.name));
        GUILayout.EndScrollView(); GUILayout.EndArea();
    }

    void OnDisable()
    {
        DisposeGraphs();
    }
    void DisposeGraphs()
    {
        if (_graphs != null) foreach (var graph in _graphs) graph?.Dispose();
        _graphs = null;
    }
}
