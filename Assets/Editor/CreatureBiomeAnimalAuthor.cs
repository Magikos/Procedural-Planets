using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Authors biome animal presentations from owned art without importing vendor runtime components.</summary>
public static class CreatureBiomeAnimalAuthor
{
    const string Root = "Assets/Art/Creatures/";

    [MenuItem("Tools/Creatures/Build Biome Animal Visuals")]
    public static void Build()
    {
        CreatureAnimalAssetAuthor.BuildSpecies("Bear", "Bear_Rig.fbx", "Bear_Grizzly_COL_2k.png",
            "Bear_Idle", "Bear_Walk", "Bear_Run", "Bear_Attack_Bite", "Bear_Dead", 1.4f, 5f);
        CreatureAnimalAssetAuthor.BuildSpecies("Goat", "Goat_Animations.fbx", "Goat_COL_2k.png",
            "Goat_Idle", "Goat_Walk", "Goat_Run", "Goat_Attack", "Goat_Death", 1f, 4f);
        CreatureAnimalAssetAuthor.BuildSpecies("Snake", "Snake_Rig.fbx", "Snake_COL_1k.png",
            "Snake_Idle", "Snake_Slither", "Snake_Slither", "Snake_Attack", "Snake_Death", .6f, 1.2f);
        BindQuadruped("Bear");
        BindQuadruped("Goat");
        BindSnake();
        BuildPolarBear();
        AssetDatabase.SaveAssets();
    }

    static CreatureVisualSettings Visuals(string species) =>
        AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(Root + species + "/" + species + "Visuals.asset")
        ?? throw new InvalidOperationException("Missing creature visual settings: " + species);

    static void BindQuadruped(string species)
    {
        var settings = Visuals(species);
        string prefabPath = AssetDatabase.GetAssetPath(settings.MalePrefab);
        var model = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            if (species == "Bear")
                foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                    if (renderer.name.Contains("Wild", StringComparison.OrdinalIgnoreCase))
                        UnityEngine.Object.DestroyImmediate(renderer);
            bool bear = species == "Bear";
            CreatureQuadrupedRigAuthor.Bind(model, settings, "Root_M",
                bear ? new[] { "Spine1_M", "Spine2_M", "Spine3_M", "Chest_M" } : new[] { "Spine1_M", "Chest_M" },
                bear ? new[] { "Neck_M", "Neck1_M", "Neck2_M", "Head_M" } : new[] { "Neck_M", "Neck1_M", "Head_M" },
                new[] { new[] { "Shoulder_L", "Elbow_L", "Wrist_L" }, new[] { "Shoulder_R", "Elbow_R", "Wrist_R" },
                    new[] { "Hip_L", "Knee_L", "Ankle_L" }, new[] { "Hip_R", "Knee_R", "Ankle_R" } },
                bear ? new[] { "Wrist_L", "Wrist_R", "Ankle_L", "Ankle_R" }
                    : new[] { "Fingers_L", "Fingers_R", "Toes1_L", "Toes1_R" },
                new[] { "Tail0_M", "Tail1_M", "Tail2_M", "Tail3_M" });
            PrefabUtility.SaveAsPrefabAsset(model, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(model); }
        if (species == "Goat")
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(Root + "Goat/Goat_Animations.fbx").OfType<AnimationClip>().ToArray();
            settings.Eat ??= clips.First(c => c.name == "Goat_Eating");
            settings.Sleep ??= clips.First(c => c.name == "Goat_Sleep");
            settings.Rest ??= settings.Sleep;
        }
        CreatureQuadrupedRigAuthor.AuthorMissingActions(settings, Root + species);
        EditorUtility.SetDirty(settings);
    }

    static void BindSnake()
    {
        var settings = Visuals("Snake");
        string prefabPath = AssetDatabase.GetAssetPath(settings.MalePrefab);
        var model = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            settings.Idle.SampleAnimation(model, 0f);
            var joints = model.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("joint", StringComparison.Ordinal)).ToArray();
            var roots = joints.Where(t => !joints.Contains(t.parent)).ToArray();
            if (roots.Length != 1) throw new InvalidOperationException("Snake rig must have one articulated root.");
            var head = joints.OrderByDescending(t => model.transform.InverseTransformPoint(t.position).z).First();
            var look = new System.Collections.Generic.List<Transform>();
            for (var bone = head; bone != null && joints.Contains(bone) && look.Count < 3; bone = bone.parent) look.Add(bone);
            look.Reverse();
            var rig = model.GetComponent<ProceduralRigDefinition>() ?? model.AddComponent<ProceduralRigDefinition>();
            rig.Body = roots[0];
            rig.Look = look.ToArray();
            rig.LookYawLimit = 20f; rig.LookPitchLimit = 12f; rig.SpineLimit = 5f;
            rig.Spine = Array.Empty<Transform>();
            rig.Feet = Array.Empty<FootDefinition>();
            int Depth(Transform bone)
            {
                int depth = 0;
                while (bone.parent != null) { depth++; bone = bone.parent; }
                return depth;
            }
            rig.SurfaceChains = new[] { new SurfaceChainDefinition
            {
                Bones = joints.OrderBy(Depth).ToArray(),
                Clearance = settings.ModelHeightMeters * .08f,
                MaxCorrection = settings.ModelHeightMeters * .8f,
                Response = 16f,
            } };
            // The authored slither already bends the full body. Only the last tail joints receive secondary motion.
            var tail = joints.OrderBy(t => model.transform.InverseTransformPoint(t.position).z).First();
            var tailChain = new System.Collections.Generic.List<Transform>();
            for (var bone = tail; bone != null && joints.Contains(bone) && tailChain.Count < 5; bone = bone.parent) tailChain.Add(bone);
            tailChain.Reverse();
            rig.Chains = tailChain.Count >= 3 ? new[] { new SpringChainDefinition { Bones = tailChain.ToArray(),
                Frequency = 4f, Damping = .8f, AngleLimit = 8f, Weight = .15f, GravityScale = 0f } } : Array.Empty<SpringChainDefinition>();
            PrefabUtility.SaveAsPrefabAsset(model, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(model); }
        settings.Eat ??= CreatureSurvivalClipAuthor.Bake(settings, Root + "Snake", "Eat", 0f, 8f, 10f);
        settings.Rest ??= CreatureSurvivalClipAuthor.Bake(settings, Root + "Snake", "Rest", 0f, 8f, 10f);
        settings.Sleep ??= CreatureSurvivalClipAuthor.Bake(settings, Root + "Snake", "Sleep", 0f, 8f, 10f);
        settings.Drink ??= settings.Eat;
        settings.Stalk ??= settings.Walk;
        EditorUtility.SetDirty(settings);
    }

    static void BuildPolarBear()
    {
        const string folder = Root + "PolarBear";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Root.TrimEnd('/'), "PolarBear");
        if (AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(folder + "/PolarBearVisuals.asset") != null) return;
        var bear = Visuals("Bear");
        var material = new Material(AssetDatabase.LoadAssetAtPath<Material>(Root + "Bear/Bear.mat")) { name = "PolarBear" };
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "PolarBear/PolarBear_COL_2k.png"));
        AssetDatabase.CreateAsset(material, folder + "/PolarBear.mat");
        var model = UnityEngine.Object.Instantiate(bear.MalePrefab);
        try
        {
            model.name = "PolarBear";
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
            var prefab = PrefabUtility.SaveAsPrefabAsset(model, folder + "/PolarBear.prefab");
            var settings = UnityEngine.Object.Instantiate(bear);
            settings.name = "PolarBearVisuals"; settings.MalePrefab = settings.FemalePrefab = prefab;
            AssetDatabase.CreateAsset(settings, folder + "/PolarBearVisuals.asset");
        }
        finally { UnityEngine.Object.DestroyImmediate(model); }
    }
}
