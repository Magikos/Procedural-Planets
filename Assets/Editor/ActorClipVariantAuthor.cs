using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Creates editable clip variants without changing imported or authored originals.</summary>
public static class ActorClipVariantAuthor
{
    public static AnimationClip Create(AnimationClip source, string destination, float speed = 1f,
        string bonePath = "", Vector3 localPositionOffset = default, bool reverse = false)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (!float.IsFinite(speed) || speed <= 0f) throw new ArgumentOutOfRangeException(nameof(speed));
        if (!float.IsFinite(source.length / speed)) throw new ArgumentOutOfRangeException(nameof(speed));
        if (!float.IsFinite(localPositionOffset.x) || !float.IsFinite(localPositionOffset.y) || !float.IsFinite(localPositionOffset.z))
            throw new ArgumentException("Position offset must be finite.", nameof(localPositionOffset));
        if (source.isHumanMotion) throw new ArgumentException("This tool requires a Generic transform clip.", nameof(source));
        if (reverse && AnimationUtility.GetObjectReferenceCurveBindings(source).Length > 0)
            throw new ArgumentException("Reverse variants require continuous curves; object-reference curves are not supported.", nameof(source));
        if (string.IsNullOrEmpty(destination) || !destination.StartsWith("Assets/", StringComparison.Ordinal) ||
            !destination.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) || destination.Contains("..") || destination.Contains('\\') ||
            !AssetDatabase.IsValidFolder(Path.GetDirectoryName(destination)?.Replace('\\', '/')))
            throw new ArgumentException("Choose an .anim path inside an existing Assets folder.", nameof(destination));
        if (File.Exists(destination) || AssetDatabase.LoadMainAssetAtPath(destination) != null)
            throw new IOException("Clip variants cannot overwrite an existing asset: " + destination);

        var clip = UnityEngine.Object.Instantiate(source);
        clip.name = Path.GetFileNameWithoutExtension(destination);
        try
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                var keys = curve.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    float inTangent = keys[i].inTangent, outTangent = keys[i].outTangent;
                    if (reverse && (!float.IsFinite(inTangent) || !float.IsFinite(outTangent)))
                        throw new ArgumentException("Reverse variants require continuous curves; stepped tangents are not supported.", nameof(source));
                    keys[i].time = (reverse ? source.length - keys[i].time : keys[i].time) / speed;
                    keys[i].inTangent = (reverse ? -outTangent : inTangent) * speed;
                    keys[i].outTangent = (reverse ? -inTangent : outTangent) * speed;
                    if (reverse)
                    {
                        float incomingWeight = keys[i].inWeight;
                        keys[i].inWeight = keys[i].outWeight;
                        keys[i].outWeight = incomingWeight;
                        WeightedMode mode = keys[i].weightedMode;
                        keys[i].weightedMode = (mode & WeightedMode.In) != 0 ? WeightedMode.Out : WeightedMode.None;
                        if ((mode & WeightedMode.Out) != 0) keys[i].weightedMode |= WeightedMode.In;
                    }
                    if (!float.IsFinite(keys[i].time)) throw new ArgumentOutOfRangeException(nameof(speed));
                    if (float.IsFinite(inTangent) && !float.IsFinite(keys[i].inTangent) ||
                        float.IsFinite(outTangent) && !float.IsFinite(keys[i].outTangent))
                        throw new ArgumentOutOfRangeException(nameof(speed));
                }
                curve.keys = keys;
                if (reverse)
                {
                    WrapMode before = curve.preWrapMode;
                    curve.preWrapMode = curve.postWrapMode;
                    curve.postWrapMode = before;
                }
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
            {
                var keys = AnimationUtility.GetObjectReferenceCurve(source, binding);
                for (int i = 0; i < keys.Length; i++) keys[i].time /= speed;
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            }
            var events = AnimationUtility.GetAnimationEvents(source);
            foreach (var item in events) item.time = (reverse ? source.length - item.time : item.time) / speed;
            Array.Sort(events, (a, b) => a.time.CompareTo(b.time));
            AnimationUtility.SetAnimationEvents(clip, events);
            var settings = AnimationUtility.GetAnimationClipSettings(source);
            float start = settings.startTime, stop = settings.stopTime;
            settings.startTime = (reverse ? source.length - stop : start) / speed;
            settings.stopTime = (reverse ? source.length - start : stop) / speed;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            for (int axis = 0; axis < 3; axis++)
            {
                if (localPositionOffset[axis] == 0f) continue;
                var binding = EditorCurveBinding.FloatCurve(bonePath ?? "", typeof(Transform), "m_LocalPosition." + "xyz"[axis]);
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) throw new ArgumentException("The source has no position curve for " + binding.path + "/" + binding.propertyName);
                var keys = curve.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].value += localPositionOffset[axis];
                    if (!float.IsFinite(keys[i].value)) throw new ArgumentOutOfRangeException(nameof(localPositionOffset));
                }
                curve.keys = keys;
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            AssetDatabase.CreateAsset(clip, destination);
            AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
        }
        catch
        {
            if (!AssetDatabase.Contains(clip)) UnityEngine.Object.DestroyImmediate(clip);
            throw;
        }
    }
}

public sealed class ActorClipVariantWindow : EditorWindow
{
    AnimationClip _source;
    float _speed = 1f;
    string _bonePath = "";
    Vector3 _offset;
    bool _reverse;
    string _error;

    [MenuItem("Tools/Actors/Create Clip Variant")]
    static void Open() => GetWindow<ActorClipVariantWindow>("Clip Variant")._source = Selection.activeObject as AnimationClip;

    void OnGUI()
    {
        _source = (AnimationClip)EditorGUILayout.ObjectField("Source clip", _source, typeof(AnimationClip), false);
        _speed = EditorGUILayout.FloatField("Playback speed", _speed);
        _reverse = EditorGUILayout.Toggle("Reverse time", _reverse);
        if (_reverse) EditorGUILayout.HelpBox("Reverses continuous curves and event times. Review event meaning for the reversed action.", MessageType.Info);
        _bonePath = EditorGUILayout.TextField("Bone path", _bonePath);
        _offset = EditorGUILayout.Vector3Field("Local position offset", _offset);
        EditorGUILayout.HelpBox("Creates a separate Generic clip. Offsets require existing position curves. Originals remain unchanged.", MessageType.Info);
        if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
        using (new EditorGUI.DisabledScope(_source == null))
        if (GUILayout.Button("Save Variant"))
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Clip Variant", _source.name + "Variant", "anim", "Choose a new asset path.");
            if (string.IsNullOrEmpty(path)) return;
            try { Selection.activeObject = ActorClipVariantAuthor.Create(_source, path, _speed, _bonePath, _offset, _reverse); _error = null; }
            catch (Exception error) { _error = error.Message; }
        }
    }
}
