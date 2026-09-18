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
            string folder = "Assets/Art/Creatures/" + species;
            var settings = AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(folder + "/" + species + "Visuals.asset");
            settings.Rest = Bake(settings, folder, "Rest");
            settings.Sleep = Bake(settings, folder, "Sleep");
            if (species == "Wolf") settings.Eat = Bake(settings, folder, "Eat");
            settings.Drink = settings.Eat;
            EditorUtility.SetDirty(settings);
        }
        AssetDatabase.SaveAssets();
    }

    public static AnimationClip Bake(CreatureVisualSettings settings, string folder, string pose,
        float restDropFraction = .42f, float sleepPitch = 75f, float feedingPitch = 65f)
    {
        if (settings == null || settings.MalePrefab == null || settings.Idle == null || pose == "Stalk" && settings.Walk == null)
            throw new System.ArgumentException("A prefab and source clips are required to bake a creature action.");
        if (pose is not ("Rest" or "Sleep" or "Eat" or "Drink" or "Stalk"))
            throw new System.ArgumentException("Unsupported creature pose: " + pose, nameof(pose));
        var model = Object.Instantiate(settings.MalePrefab);
        model.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var rig = model.GetComponent<ProceduralRigDefinition>();
            if (rig == null || rig.Body == null || rig.Look.Length == 0)
                throw new System.InvalidOperationException("Bind the creature body and look bones before baking actions.");
            var bones = model.GetComponentsInChildren<Transform>().Where(t => t != model.transform).ToArray();
            var restPositions = bones.Select(t => t.localPosition).ToArray();
            var restRotations = bones.Select(t => t.localRotation).ToArray();
            var curves = bones.Select(_ => Enumerable.Range(0, 7).Select(_ => new AnimationCurve()).ToArray()).ToArray();
            const int samples = 60;
            float duration = pose == "Stalk" ? settings.Walk.length : 4f;
            for (int frame = 0; frame <= samples; frame++)
            {
                float time = duration * frame / samples;
                for (int i = 0; i < bones.Length; i++) bones[i].SetLocalPositionAndRotation(restPositions[i], restRotations[i]);
                if (pose == "Stalk") settings.Walk.SampleAnimation(model, time);
                else settings.Idle.SampleAnimation(model, 0f);
                var tips = rig.Feet.Select(f => f.Bones[^1].position).ToArray();
                bool resting = pose is "Rest" or "Sleep" or "Stalk";
                if (resting)
                {
                    rig.Body.position -= model.transform.up * settings.ModelHeightMeters * (pose == "Stalk" ? .12f : restDropFraction);
                    for (int i = 0; i < rig.Feet.Length; i++)
                    {
                        var foot = rig.Feet[i];
                        Vector3 target = tips[i] + model.transform.forward * settings.ModelHeightMeters * (pose == "Stalk" ? 0f : .12f);
                        new LimbPoseSolver(foot.Bones).Solve(target, 1f, 170f, bendDirection:
                            model.transform.TransformDirection(foot.BendDirection));
                    }
                }
                rig.Body.position += model.transform.up * (Mathf.Sin(time / duration * Mathf.PI * 2f) * .004f * settings.ModelHeightMeters);
                if (pose == "Drink" && rig.Feet.Length > 0) PoseDrink(model, rig, settings.ModelHeightMeters, time, tips);
                else
                {
                    float pitch = pose == "Sleep" ? sleepPitch : pose == "Eat" ? feedingPitch + Mathf.Sin(time * Mathf.PI * 2f) * 4f
                        : pose == "Drink" ? feedingPitch * .9f + Mathf.Sin(time * Mathf.PI * 3f) * 2f : 0f;
                    foreach (var bone in rig.Look)
                        bone.rotation = Quaternion.AngleAxis(pitch / rig.Look.Length, model.transform.right) * bone.rotation;
                }
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

    static Vector3 MouthOffset(GameObject model, ProceduralRigDefinition rig, float height)
    {
        Transform head = rig.Look[^1];
        var children = head.GetComponentsInChildren<Transform>();
        Transform tip = children.FirstOrDefault(t => t.name.EndsWith("JawEnd_M", System.StringComparison.Ordinal))
            ?? children.FirstOrDefault(t => t.name.EndsWith("HeadEnd_M", System.StringComparison.Ordinal));
        return head.InverseTransformPoint(tip != null ? tip.position : head.position + model.transform.forward * height * .12f - model.transform.up * height * .05f);
    }

    static void PoseDrink(GameObject model, ProceduralRigDefinition rig, float height, float time, Vector3[] feet)
    {
        Vector3 up = model.transform.up, right = model.transform.right;
        float ground = rig.Feet.Min(f => Vector3.Dot((f.Contact != null ? f.Contact : f.Bones[^1]).position, up));
        float target = ground + height * (.015f + .003f * Mathf.Sin(time * Mathf.PI * 3f));
        Vector3 mouth = MouthOffset(model, rig, height), bodyPosition = rig.Body.position;
        Quaternion bodyRotation = rig.Body.localRotation;
        var rotations = rig.Look.Select(t => t.localRotation).ToArray();
        float maxDrop = Mathf.Clamp(Vector3.Dot(bodyPosition, up) - ground - height * .12f, 0f, height * .5f);
        float bestScore = float.PositiveInfinity, bestLean = 0f, bestPitch = 0f, bestDrop = 0f;
        void Apply(float lean, float pitch, float drop)
        {
            rig.Body.SetLocalPositionAndRotation(rig.Body.localPosition, bodyRotation);
            rig.Body.position = bodyPosition - up * drop;
            rig.Body.rotation = Quaternion.AngleAxis(lean, right) * rig.Body.rotation;
            for (int i = 0; i < rig.Look.Length; i++) rig.Look[i].localRotation = rotations[i];
            foreach (var bone in rig.Look) bone.rotation = Quaternion.AngleAxis(pitch / rig.Look.Length, right) * bone.rotation;
        }
        for (int lean = 0; lean <= 25; lean += 5)
        for (int pitch = 0; pitch <= 95; pitch += 5)
        {
            Apply(lean, pitch, 0f);
            float gap = Vector3.Dot(rig.Look[^1].TransformPoint(mouth), up) - target;
            float drop = Mathf.Clamp(gap, 0f, maxDrop);
            float score = Mathf.Abs(gap - drop) / height * 1000f + drop / height + lean / 90f + pitch / 360f;
            if (score >= bestScore) continue;
            bestScore = score; bestLean = lean; bestPitch = pitch; bestDrop = drop;
        }
        Apply(bestLean, bestPitch, bestDrop);
        for (int i = 0; i < rig.Feet.Length; i++)
            new LimbPoseSolver(rig.Feet[i].Bones).Solve(feet[i], 1f, 170f,
                bendDirection: model.transform.TransformDirection(rig.Feet[i].BendDirection));
    }

    public static string RebuildGroundDrinks()
    {
        var report = new System.Text.StringBuilder();
        foreach (string guid in AssetDatabase.FindAssets("t:CreatureVisualSettings", new[] { "Assets/Art/Creatures" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var settings = AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(path);
            var rig = settings.MalePrefab != null ? settings.MalePrefab.GetComponent<ProceduralRigDefinition>() : null;
            if (rig == null || rig.Feet.Length == 0) continue;
            // Imported actions remain source art. Only our generated drinks or old eat aliases are replaced.
            string clipPath = AssetDatabase.GetAssetPath(settings.Drink);
            if (settings.Drink != null && settings.Drink != settings.Eat && !clipPath.EndsWith("DrinkPrototype.anim", System.StringComparison.Ordinal)) continue;
            if (path == "Assets/Art/Creatures/Goat/GoatVisuals.asset" && settings.Eat != null)
            {
                // Reviewed native Goat_Eating preserves the muzzle and supporting stance.
                settings.Drink = settings.Eat; EditorUtility.SetDirty(settings);
                report.AppendLine("Goat: retained reviewed original Goat_Eating for drinking.");
                continue;
            }
            settings.Drink = Bake(settings, System.IO.Path.GetDirectoryName(path).Replace('\\', '/'), "Drink");
            EditorUtility.SetDirty(settings);
            var model = Object.Instantiate(settings.MalePrefab);
            try
            {
                var definition = model.GetComponent<ProceduralRigDefinition>();
                settings.Idle.SampleAnimation(model, 0f);
                Vector3 up = model.transform.up;
                float ground = definition.Feet.Min(f => Vector3.Dot((f.Contact != null ? f.Contact : f.Bones[^1]).position, up));
                Vector3 mouth = MouthOffset(model, definition, settings.ModelHeightMeters);
                var feet = definition.Feet.Select(f => f.Bones[^1].position).ToArray();
                settings.Drink.SampleAnimation(model, 1f);
                float clearance = Vector3.Dot(definition.Look[^1].TransformPoint(mouth), up) - ground;
                float footError = definition.Feet.Select((f, i) => Vector3.Distance(f.Bones[^1].position, feet[i])).Max();
                report.AppendLine($"{settings.name}: mouth={clearance:F4}m, ankle shift={footError:F4}m, height={settings.ModelHeightMeters:F3}m");
            }
            finally { Object.DestroyImmediate(model); }
        }
        AssetDatabase.SaveAssets();
        return report.ToString();
    }

    [MenuItem("Tools/Creatures/Author Wolf Stalk Clip")]
    public static void BuildStalk()
    {
        const string folder = "Assets/Art/Creatures/Wolf";
        var settings = AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(folder + "/WolfVisuals.asset");
        settings.Stalk = Bake(settings, folder, "Stalk");
        EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
    }
}
