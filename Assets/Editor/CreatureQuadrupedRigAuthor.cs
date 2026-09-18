using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit bindings share the existing procedural solvers across imported quadrupeds.</summary>
public static class CreatureQuadrupedRigAuthor
{
    const string Root = "Assets/Art/Creatures/";

    [MenuItem("Tools/Creatures/Bind Additional Quadrupeds And Author Actions")]
    public static void Build()
    {
        foreach (string species in new[] { "Rabbit", "Boar", "Fox" }) BuildSpecies(species);
        AssetDatabase.SaveAssets();
    }

    public static void BuildSpecies(string species)
    {
        string folder = Root + species;
        var settings = AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(folder + "/" + species + "Visuals.asset")
            ?? throw new InvalidOperationException("Missing visual settings: " + species);
        string path = AssetDatabase.GetAssetPath(settings.MalePrefab);
        var model = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (species == "Rabbit")
                Bind(model, settings, "skin_Root_M", new[] { "skin_Spine1_M", "skin_Chest_M" },
                    new[] { "skin_Neck_M", "skin_Neck1_M", "skin_Neck2_M", "skin_Head_M" },
                    new[] { new[] { "skin_frontHip_L", "skin_frontKnee_L", "skin_frontAnkle_L" },
                        new[] { "skin_frontHip_R", "skin_frontKnee_R", "skin_frontAnkle_R" },
                        new[] { "skin_backHip_L", "skin_backKnee_L", "skin_backAnkle_L" },
                        new[] { "skin_backHip_R", "skin_backKnee_R", "skin_backAnkle_R" } },
                    new[] { "skin_frontToes1_L", "skin_frontToes1_R", "skin_backToes1_L", "skin_backToes1_R" },
                    new[] { "skin_Tail0_M", "skin_Tail1_M", "skin_Tail2_M" });
            else if (species is "Boar" or "Fox")
                Bind(model, settings, "Root_M",
                    species == "Fox" ? new[] { "Spine1_M", "Spine2_M", "Spine3_M", "Chest_M" } : new[] { "Spine1_M", "Chest_M" },
                    species == "Fox" ? new[] { "Neck_M", "Neck1_M", "Neck2_M", "Head_M" } : new[] { "Neck_M", "Head_M" },
                    new[] { new[] { "Shoulder_L", "Elbow_L", "Wrist_L" }, new[] { "Shoulder_R", "Elbow_R", "Wrist_R" },
                        new[] { "Hip_L", "Knee_L", "Ankle_L" }, new[] { "Hip_R", "Knee_R", "Ankle_R" } },
                    new[] { "Fingers1_L", "Fingers1_R", "Toes1_L", "Toes1_R" },
                    Enumerable.Range(0, species == "Fox" ? 7 : 6).Select(i => "Tail" + i + "_M").ToArray());
            else throw new ArgumentException("No quadruped binding map for " + species, nameof(species));
            PrefabUtility.SaveAsPrefabAsset(model, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(model); }
        AuthorMissingActions(settings, folder);
    }

    public static ProceduralRigDefinition Bind(GameObject model, CreatureVisualSettings visuals, string body,
        string[] spine, string[] look, string[][] legs, string[] contacts, string[] tail)
    {
        if (model == null || visuals == null || visuals.Idle == null || visuals.Walk == null || visuals.Run == null)
            throw new ArgumentException("A model and idle/walk/run clips are required for rig authoring.");
        if (legs == null || contacts == null || legs.Length != contacts.Length || legs.Length == 0)
            throw new ArgumentException("Each leg needs a contact binding.");
        var bones = model.GetComponentsInChildren<Transform>(true);
        Transform Find(string name)
        {
            var matches = bones.Where(t => t.name == name || t.name.EndsWith(":" + name, StringComparison.Ordinal)).ToArray();
            return matches.Length == 1 ? matches[0] : throw new InvalidOperationException(
                $"Expected one bone '{name}' in {model.name}, found {matches.Length}.");
        }
        Transform[] Map(string[] names) => names.Select(Find).ToArray();
        var rig = model.GetComponent<ProceduralRigDefinition>() ?? model.AddComponent<ProceduralRigDefinition>();
        rig.Body = Find(body); rig.Spine = Map(spine); rig.Look = Map(look);
        rig.SpineLimit = 12f; rig.LookYawLimit = 50f; rig.LookPitchLimit = 25f;
        rig.Chains = tail == null || tail.Length < 2 ? Array.Empty<SpringChainDefinition>() : new[]
        {
            new SpringChainDefinition { Bones = Map(tail), Frequency = 4f, Damping = .8f, AngleLimit = 30f,
                GravityScale = .05f, Weight = .6f },
        };
        rig.Feet = new FootDefinition[legs.Length];
        visuals.Idle.SampleAnimation(model, 0f);
        for (int i = 0; i < legs.Length; i++)
        {
            Transform[] limb = Map(legs[i]);
            if (limb.Length != 3 || !limb[1].IsChildOf(limb[0]) || !limb[2].IsChildOf(limb[1]))
                throw new InvalidOperationException("A leg needs ordered hip, knee, and ankle bindings.");
            Vector3 axis = limb[2].position - limb[0].position;
            Vector3 bend = Vector3.ProjectOnPlane(limb[1].position - limb[0].position, axis);
            if (bend.sqrMagnitude < 1e-8f) bend = model.transform.forward;
            rig.Feet[i] = new FootDefinition { Bones = limb, Contact = Find(contacts[i]),
                BendDirection = model.transform.InverseTransformDirection(bend.normalized),
                SoleOffset = visuals.ModelHeightMeters * .015f, MaxCorrection = visuals.ModelHeightMeters * .2f,
                JointLimit = 55f };
        }
        foreach (var foot in rig.Feet)
        {
            foot.WalkContact = SampleContact(model, foot.Contact, visuals.Walk, visuals.ModelHeightMeters);
            foot.RunContact = SampleContact(model, foot.Contact, visuals.Run, visuals.ModelHeightMeters);
        }
        visuals.Idle.SampleAnimation(model, 0f);
        EditorUtility.SetDirty(rig);
        return rig;
    }

    public static AnimationCurve SampleContact(GameObject model, Transform contact, AnimationClip clip, float height)
    {
        if (clip == null || clip.length <= 0f || contact == null) throw new ArgumentException("A contact and nonempty clip are required.");
        const int samples = 60;
        var elevations = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            clip.SampleAnimation(model, clip.length * i / samples);
            elevations[i] = model.transform.InverseTransformPoint(contact.position).y;
        }
        float low = elevations.Min(), span = elevations.Max() - low;
        if (span < height * .002f) return AnimationCurve.Constant(0f, 1f, 1f);
        var curve = new AnimationCurve();
        for (int i = 0; i <= samples; i++)
        {
            float lift = (elevations[i % samples] - low) / span;
            float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.15f, .4f, lift));
            curve.AddKey(i / (float)samples, weight);
        }
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
        }
        curve.preWrapMode = curve.postWrapMode = WrapMode.Loop;
        return curve;
    }

    public static void AuthorMissingActions(CreatureVisualSettings settings, string folder)
    {
        if (settings.Eat == null) settings.Eat = CreatureSurvivalClipAuthor.Bake(settings, folder, "Eat");
        if (settings.Drink == null) settings.Drink = CreatureSurvivalClipAuthor.Bake(settings, folder, "Drink");
        if (settings.Rest == null) settings.Rest = CreatureSurvivalClipAuthor.Bake(settings, folder, "Rest");
        if (settings.Sleep == null) settings.Sleep = CreatureSurvivalClipAuthor.Bake(settings, folder, "Sleep");
        if (settings.Stalk == null) settings.Stalk = CreatureSurvivalClipAuthor.Bake(settings, folder, "Stalk");
        EditorUtility.SetDirty(settings);
    }

    public static string Diagnose(string species)
    {
        var settings = AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(Root + species + "/" + species + "Visuals.asset");
        var rig = settings.MalePrefab.GetComponent<ProceduralRigDefinition>();
        var text = new StringBuilder($"{species}: legs={rig.Feet.Length}, spine={rig.Spine.Length}, look={rig.Look.Length}, chains={rig.Chains.Length}");
        foreach (var foot in rig.Feet)
            text.Append($"\n{foot.Contact.name}: walk={foot.WalkContact.keys.Min(k => k.value):F2}..{foot.WalkContact.keys.Max(k => k.value):F2}, run={foot.RunContact.keys.Min(k => k.value):F2}..{foot.RunContact.keys.Max(k => k.value):F2}");
        foreach (var clip in new[] { settings.Idle, settings.Walk, settings.Run, settings.Eat, settings.Drink, settings.Rest, settings.Sleep, settings.Stalk })
            text.Append($"\n{clip.name}: {clip.length:F2}s, curves={AnimationUtility.GetCurveBindings(clip).Length}");
        return text.ToString();
    }
}
