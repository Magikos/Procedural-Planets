using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CreatureBirdAssetAuthor
{
    [MenuItem("Tools/Creatures/Bind Bird Grounded Rigs")]
    public static void BuildRigs()
    {
        foreach (BirdVisualKind kind in new[] { BirdVisualKind.Eagle, BirdVisualKind.Vulture, BirdVisualKind.Seagull })
        {
            bool gull = kind == BirdVisualKind.Seagull;
            string root = "Assets/Resources/Wildlife/Birds/" + kind;
            string modelPath = gull ? root + "/Source.prefab" : root + ".fbx";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath)
                ?? throw new InvalidOperationException("Missing bird model: " + modelPath);
            AnimationClip idle = gull ? AssetDatabase.LoadAssetAtPath<AnimationClip>(root + "/Idle.anim")
                : AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().Single(c => c.name == kind + "_Idle");
            var model = UnityEngine.Object.Instantiate(source);
            var visuals = ScriptableObject.CreateInstance<CreatureVisualSettings>();
            try
            {
                model.name = kind + " grounded rig";
                if (gull) idle = AuthorFoldedGull(model, idle, root + "/FoldedIdle.anim");
                idle.SampleAnimation(model, 0f);
                Bounds bounds = BirdAnimationView.MeasureStandingBounds(model);
                visuals.Idle = visuals.Walk = visuals.Run = idle;
                visuals.ModelHeightMeters = bounds.size.y;
                Transform Find(string name) => model.GetComponentsInChildren<Transform>(true).Single(t =>
                    t.name == name || t.name.EndsWith(":" + name, StringComparison.Ordinal));
                ProceduralRigDefinition rig;
                if (gull)
                {
                    // The owned gull has body/head/tail and wing joints, but no leg joints.
                    rig = model.AddComponent<ProceduralRigDefinition>();
                    rig.Body = Find("joint1");
                    rig.Look = new[] { Find("joint5"), Find("joint6") };
                }
                else
                {
                    foreach (string side in new[] { "L", "R" })
                    {
                        Transform ankle = Find("Ankle_" + side);
                        var contact = new GameObject("FootContact_" + side).transform;
                        contact.SetParent(ankle, false);
                        Vector3 local = model.transform.InverseTransformPoint(ankle.position);
                        local.y = bounds.min.y;
                        contact.position = model.transform.TransformPoint(local);
                    }
                    rig = CreatureQuadrupedRigAuthor.Bind(model, visuals, "Root_M", Array.Empty<string>(),
                        new[] { "Neck_M", "Head_M" },
                        new[] { new[] { "Hip_L", "Knee_L", "Ankle_L" }, new[] { "Hip_R", "Knee_R", "Ankle_R" } },
                        new[] { "FootContact_L", "FootContact_R" }, Array.Empty<string>());
                    foreach (FootDefinition foot in rig.Feet)
                    {
                        foot.SoleOffset = 0f;
                        foot.WalkContact = foot.RunContact = AnimationCurve.Constant(0f, 1f, 1f);
                    }
                }
                rig.LookPitchLimit = 60f;
                rig.LookYawLimit = 35f;
                PrefabUtility.SaveAsPrefabAsset(model, gull ? root + "/Rig.prefab" : root + "Rig.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(model);
                UnityEngine.Object.DestroyImmediate(visuals);
            }
        }
        AssetDatabase.SaveAssets();
    }

    static AnimationClip AuthorFoldedGull(GameObject model, AnimationClip source, string path)
    {
        source.SampleAnimation(model, 0f);
        Transform Find(string name) => model.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
        Transform body = Find("joint1");
        Vector3 up = model.transform.up;
        Vector3 forward = Vector3.ProjectOnPlane(Find("joint7").position - body.position, up).normalized;
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        EditorUtility.CopySerialized(source, clip);
        clip.name = "Seagull_FoldedIdle";
        foreach (int first in new[] { 8, 13 })
        {
            Transform shoulder = Find("joint" + first);
            Vector3 side = Vector3.ProjectOnPlane(shoulder.position - body.position, up);
            side = Vector3.ProjectOnPlane(side, forward).normalized;
            for (int segment = 0; segment < 4; segment++)
            {
                Transform bone = Find("joint" + (first + segment));
                Transform child = Find("joint" + (first + segment + 1));
                Vector3 direction = segment switch
                {
                    0 => side * .2f - forward * .8f - up * .35f,
                    1 => side * .1f + forward * .65f - up * .2f,
                    _ => -forward - up * .08f,
                };
                bone.rotation = Quaternion.FromToRotation(child.position - bone.position, direction) * bone.rotation;
                Quaternion rotation = bone.localRotation;
                string binding = AnimationUtility.CalculateTransformPath(bone, model.transform);
                string[] axes = { "x", "y", "z", "w" };
                float[] values = { rotation.x, rotation.y, rotation.z, rotation.w };
                for (int axis = 0; axis < axes.Length; axis++)
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(binding, typeof(Transform),
                        "m_LocalRotation." + axes[axis]), AnimationCurve.Constant(0f, source.length, values[axis]));
            }
        }
        clip.EnsureQuaternionContinuity();
        EditorUtility.SetDirty(clip);
        return clip;
    }

    [MenuItem("Tools/Creatures/Configure Coastal Birds")]
    public static void ConfigurePlanetLibrary()
    {
        const string audioPath = "Assets/Art/Creatures/Seagull/SeagullAudio.asset";
        var audio = AssetDatabase.LoadAssetAtPath<CreatureAudioSettings>(audioPath);
        if (audio == null)
        {
            var calls = new[] { "SeagullCall1", "SeagullCall2" }.Select(name =>
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Art/Creatures/Seagull/" + name + ".ogg")
                    ?? throw new InvalidOperationException("Missing gull audio: " + name)).ToArray();
            audio = ScriptableObject.CreateInstance<CreatureAudioSettings>();
            audio.Calls = calls;
            audio.Volume = .45f;
            audio.AudibleDistance = 100f;
            audio.MinimumCallSeconds = 18f;
            audio.MaximumCallSeconds = 45f;
            AssetDatabase.CreateAsset(audio, audioPath);
        }
        var library = AssetDatabase.LoadAssetAtPath<CreatureLibrary>("Assets/Resources/Settings/CreatureLibrary.asset")
            ?? throw new InvalidOperationException("The planet creature library is missing.");
        if (library.Species.Any(species => species != null && species.DisplayName == "Seagull")) return;
        var occupied = library.Species.Where(species => species != null)
            .SelectMany(species => species.AdditionalSlots ?? Array.Empty<int>()).ToArray();
        if (occupied.Contains(46) || occupied.Contains(47) || library.Species.Sum(species => species?.PerTerritory ?? 0) > 46)
            throw new InvalidOperationException("Coastal bird slots 46 and 47 are already occupied.");
        var entries = library.Species.ToList();
        entries.Add(new CreatureSpecies
        {
            DisplayName = "Seagull", BirdVisual = BirdVisualKind.Seagull, Audio = audio,
            PerTerritory = 0, AdditionalSlots = new[] { 46, 47 }, HomeRangeMeters = 160f,
            WalkSpeedMps = 6f, DriftHomeSpeedMps = 2f, RespawnSeconds = 600f,
            MinAltitudeMeters = .2f, MaxAltitudeMeters = 1500f,
            Biomes = new[] { BiomeType.Beach, BiomeType.LakeShore },
            Faction = CreatureFaction.Wildlife, Scavenger = true, AwarenessMeters = 55f,
            MaxHealth = 2, Diet = ResourceKind.Meat | ResourceKind.Plants | ResourceKind.FreshWater | ResourceKind.SaltWater,
            HungerSeconds = 2400f, ThirstSeconds = 1800f, ConsumeUnitsPerSecond = .08f,
            BodyHeightMeters = .4f, CruiseAltitudeMeters = 12f,
            YieldItemId = "Feathers", YieldCount = 1,
        });
        library.Species = entries.ToArray();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
    }
}
