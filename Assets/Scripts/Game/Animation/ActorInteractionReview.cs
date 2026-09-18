using UnityEngine;

/// <summary>Visible two-hand binding fixture for reviewing the shared actor pose solver.</summary>
public sealed class ActorInteractionReview : MonoBehaviour
{
    public ProceduralRigDefinition Rig;
    public Transform GazeTarget;
    [Range(0f, 1f)] public float LookInfluence = 1f;
    public HandTarget[] Targets = System.Array.Empty<HandTarget>();
    [System.Serializable]
    public sealed class HandTarget
    {
        public string Id;
        public Transform Target;
        [Range(0f, 1f)] public float Weight = 1f;
        public bool MatchRotation = true;
    }
    ProceduralPoseRig _pose;
    LineRenderer[] _lines;
    Material _material;
    void Start()
    {
        _pose = new ProceduralPoseRig(transform, Rig);
        _material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        _material.color = new Color(.3f, .8f, 1f);
        _lines = new LineRenderer[Rig.Interactions.Length];
        for (int i = 0; i < _lines.Length; i++)
        {
            var line = new GameObject("Arm outline").AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.sharedMaterial = _material; line.widthMultiplier = .04f;
            line.positionCount = Rig.Interactions[i].Bones.Length;
            _lines[i] = line;
        }
    }
    void LateUpdate()
    {
        if (_pose == null) return;
        _pose.RestoreAnimation(); _pose.CaptureAnimation();
        _pose.LookInfluence = LookInfluence;
        foreach (var binding in Rig.Interactions) _pose.SetInteractionTarget(binding.Id, null);
        foreach (var target in Targets)
            if (target.Target != null)
                _pose.SetInteractionTarget(target.Id, new InteractionPoseTarget(target.Target.position,
                    target.MatchRotation ? target.Target.rotation : (Quaternion?)null, target.Weight));
        _pose.Tick(transform.up, GazeTarget != null ? GazeTarget.position - _pose.LookOrigin : transform.forward,
            null, 0f, 0f, 0f, Time.deltaTime);
        for (int i = 0; i < _lines.Length; i++)
            for (int b = 0; b < Rig.Interactions[i].Bones.Length; b++)
                _lines[i].SetPosition(b, Rig.Interactions[i].Bones[b].position);
    }
    void OnDestroy()
    {
        if (_material != null) Destroy(_material);
    }
}
