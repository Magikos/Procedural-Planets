using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Author prototype poses on the existing rigs. Generated clips remain ordinary editable Unity assets.</summary>
public static class CreatureSurvivalClipAuthor
{
    [MenuItem("Tools/Creatures/Author Survival Clips")]
    public static void Build()
    {
        foreach (string species in new[] { "Wolf", "Deer" })
        {
            string folder = "Assets/AssetPacks/PolyperfectAnimals/" + species;
            var settings = AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(folder + "/" + species + "Visuals.asset");
            settings.Rest = Bake(settings, folder, "Rest");
            settings.Sleep = Bake(settings, folder, "Sleep");
            if (species == "Wolf") settings.Eat = Bake(settings, folder, "Eat");
            settings.Drink = settings.Eat;
            EditorUtility.SetDirty(settings);
        }
        AssetDatabase.SaveAssets();
    }

    static AnimationClip Bake(CreatureVisualSettings settings, string folder, string pose)
    {
        var model = Object.Instantiate(settings.MalePrefab);
        model.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var rig = model.GetComponent<ProceduralRigDefinition>();
            var bones = model.GetComponentsInChildren<Transform>().Where(t => t != model.transform).ToArray();
            var curves = bones.Select(_ => Enumerable.Range(0, 7).Select(_ => new AnimationCurve()).ToArray()).ToArray();
            const int samples = 60;
            const float duration = 4f;
            for (int frame = 0; frame <= samples; frame++)
            {
                float time = duration * frame / samples;
                settings.Idle.SampleAnimation(model, 0f);
                var tips = rig.Feet.Select(f => f.Bones[^1].position).ToArray();
                bool resting = pose != "Eat";
                if (resting)
                {
                    rig.Body.position -= model.transform.up * settings.ModelHeightMeters * .42f;
                    for (int i = 0; i < rig.Feet.Length; i++)
                    {
                        var foot = rig.Feet[i];
                        Vector3 target = tips[i] + model.transform.forward * settings.ModelHeightMeters * .12f;
                        new LimbPoseSolver(foot.Bones).Solve(target, 1f, 170f, bendDirection:
                            model.transform.TransformDirection(foot.BendDirection));
                    }
                }
                rig.Body.position += model.transform.up * (Mathf.Sin(time / duration * Mathf.PI * 2f) * .004f * settings.ModelHeightMeters);
                float pitch = pose == "Sleep" ? 40f : pose == "Eat" ? 65f + Mathf.Sin(time * Mathf.PI * 2f) * 4f : 0f;
                foreach (var bone in rig.Look)
                    bone.rotation = Quaternion.AngleAxis(pitch / rig.Look.Length, model.transform.right) * bone.rotation;
                for (int i = 0; i < bones.Length; i++)
                {
                    Vector3 p = bones[i].localPosition; Quaternion q = bones[i].localRotation;
                    float[] values = { p.x, p.y, p.z, q.x, q.y, q.z, q.w };
                    for (int k = 0; k < 7; k++) curves[i][k].AddKey(time, values[k]);
                }
            }
            string path = folder + "/" + pose + "Prototype.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves(); clip.frameRate = 15f;
            string[] properties = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
                "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
            for (int i = 0; i < bones.Length; i++)
            {
                string bonePath = AnimationUtility.CalculateTransformPath(bones[i], model.transform);
                for (int k = 0; k < 7; k++)
                {
                    var curve = curves[i][k];
                    if (curve.keys.Max(key => key.value) - curve.keys.Min(key => key.value) < 0.000001f)
                        curve = AnimationCurve.Constant(0f, duration, curve.keys[0].value);
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(bonePath, typeof(Transform), properties[k]), curve);
                }
            }
            clip.EnsureQuaternionContinuity();
            var options = AnimationUtility.GetAnimationClipSettings(clip); options.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, options); EditorUtility.SetDirty(clip);
            return clip;
        }
        finally { Object.DestroyImmediate(model); }
    }
}
