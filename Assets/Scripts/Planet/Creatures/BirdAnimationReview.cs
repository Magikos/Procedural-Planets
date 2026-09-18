using System;
using UnityEngine;

/// <summary>Presentation review only. The selected action does not run a gameplay brain.</summary>
public sealed class BirdAnimationReview : MonoBehaviour
{
    public enum ReviewAction { Perch, Fly, Feed, Drink, Sleep }
    public ReviewAction Action;
    public bool RaisedSupport = true;
    [Range(-20f, 20f)] public float SupportSlope;
    [Range(0f, 360f)] public float Heading = 25f;
    [Range(.1f, 2f)] public float PlaybackSpeed = 1f;
    public Camera ReviewCamera;
    public string Diagnostics { get; private set; }
    readonly BirdAnimationView[] _birds = new BirdAnimationView[3];
    readonly Transform[] _supports = new Transform[3];
    readonly TextMesh[] _labels = new TextMesh[3];
    readonly System.Collections.Generic.List<Material> _previewMaterials = new();
    static readonly BirdVisualKind[] Kinds = { BirdVisualKind.Eagle, BirdVisualKind.Vulture, BirdVisualKind.Seagull };

    void OnEnable()
    {
        for (int i = 0; i < _birds.Length; i++)
        {
            _birds[i] = new BirdAnimationView(transform, (ulong)(997 * (i + 1)), .8f, visual: Kinds[i]);
            CreatureAnimationPrototype.ApplyPreviewMaterials(_birds[i].Root, _previewMaterials);
            var label = new GameObject(Kinds[i] + " label").AddComponent<TextMesh>();
            label.transform.SetParent(transform, false);
            label.text = Kinds[i].ToString(); label.fontSize = 48; label.characterSize = .035f;
            label.anchor = TextAnchor.MiddleCenter; label.color = Color.white;
            _labels[i] = label;
            var support = GameObject.CreatePrimitive(PrimitiveType.Cube);
            support.name = Kinds[i] + " support";
            support.transform.SetParent(transform, false);
            _supports[i] = support.transform;
        }
    }

    void Update()
    {
        bool grounded = Action != ReviewAction.Fly;
        CreatureBehaviour behaviour = Action switch
        {
            ReviewAction.Feed => CreatureBehaviour.Feed,
            ReviewAction.Drink => CreatureBehaviour.Drink,
            ReviewAction.Sleep => CreatureBehaviour.Sleep,
            _ => CreatureBehaviour.Perch,
        };
        Vector3 up = transform.up;
        Vector3 supportNormal = Quaternion.AngleAxis(SupportSlope, transform.forward) * up;
        Diagnostics = "";
        for (int i = 0; i < _birds.Length; i++)
        {
            Vector3 point = transform.TransformPoint(new Vector3((i - 1) * 2.5f, RaisedSupport ? 1.2f : 0f, 0f));
            _supports[i].SetPositionAndRotation(point - supportNormal * .1f,
                Quaternion.FromToRotation(Vector3.up, supportNormal));
            _supports[i].localScale = new Vector3(1.6f, .2f, 1.6f);
            _birds[i].Root.SetPositionAndRotation(point + up * (grounded ? .4f : 1.5f),
                Quaternion.AngleAxis(Heading, up) * transform.rotation);
            _birds[i].Tick(grounded, false, Time.deltaTime * PlaybackSpeed, supportNormal,
                behaviour: behaviour, supportPoint: point);
            _labels[i].transform.position = point + up * (grounded ? 1.2f : 2.3f);
            if (ReviewCamera != null) _labels[i].transform.rotation = ReviewCamera.transform.rotation;
            var pose = _birds[i].Pose;
            Diagnostics += Kinds[i] + ": " + (!grounded ? "airborne; feet disabled" : pose == null ? "NO AUTHORED RIG" :
                $"feet {pose.PlantedFeet}, error {pose.MaxFootError:F3} m") + "\n";
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12, 12, 520, 260), GUI.skin.box);
        GUILayout.Label("Bird animation review: Eagle / Vulture / Seagull");
        GUILayout.BeginHorizontal();
        foreach (ReviewAction action in Enum.GetValues(typeof(ReviewAction)))
            if (GUILayout.Button(action.ToString())) Action = action;
        GUILayout.EndHorizontal();
        RaisedSupport = GUILayout.Toggle(RaisedSupport, "Raised branch support");
        GUILayout.Label("Support slope: " + SupportSlope.ToString("F0"));
        SupportSlope = GUILayout.HorizontalSlider(SupportSlope, -20f, 20f);
        GUILayout.Label(Diagnostics);
        GUILayout.Label("Seagull: head rig only; owned mesh has no leg joints.");
        GUILayout.EndArea();
    }

    void OnDisable()
    {
        for (int i = 0; i < _birds.Length; i++)
        {
            _birds[i]?.Dispose(); _birds[i] = null;
            if (_supports[i] != null) Destroy(_supports[i].gameObject);
            if (_labels[i] != null) Destroy(_labels[i].gameObject);
        }
        foreach (Material material in _previewMaterials) Destroy(material);
        _previewMaterials.Clear();
    }
}
