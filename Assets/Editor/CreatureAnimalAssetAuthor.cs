using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Creates project-owned presentation assets from the selected Polyperfect art imports.</summary>
public static class CreatureAnimalAssetAuthor
{
    const string Root = "Assets/Art/Creatures/";

    [MenuItem("Tools/Creatures/Build Additional Animal Visuals")]
    public static void Build()
    {
        BuildSpecies("Rabbit", "Rabbit_Rig.fbx", "Rabbit_COL_1k.png",
            "Rabbit_Idle", "Rabbit_Jump_Walk", "Rabbit_Run", null, "Rabbit_Death_1", .65f, 3.5f);
        BuildSpecies("Boar", "Boar_Rig.fbx", "Boar_Brown_COL_2k.png",
            "Boar_Idle_Breathing", "Boar_Walk", "Boar_Run", "Boar_Attack", "Boar_Death", 1f, 4.5f);
        BuildSpecies("Fox", "Fox.fbx", "Fox_COL_2k.png",
            "Fox_Idle", "Fox_Walk", "Fox_Run", "Fox_Attack", "Fox_Death", 1f, 4f);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Creatures/Configure Planet Animal Library")]
    public static void ConfigurePlanetLibrary()
    {
        var library = AssetDatabase.LoadAssetAtPath<CreatureLibrary>("Assets/Resources/Settings/CreatureLibrary.asset");
        if (library == null || library.Species == null || library.Species.Length < 4
            || library.Species[0]?.DisplayName != "Deer"
            || (library.Species[1]?.DisplayName != "Rabbit" && library.Species[1]?.DisplayName != "Placeholder Rabbit"))
            throw new InvalidOperationException("The planet library no longer has the expected deer and rabbit slots.");
        var names = new[] { "Deer", "Rabbit", "Wolf", "Boar", "Fox" };
        var visuals = names.ToDictionary(n => n, n => AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(Root + n + "/" + n + "Visuals.asset")
            ?? throw new InvalidOperationException("Build animal visual assets before configuring the library: " + n));
        var entries = library.Species.ToList();
        var deer = entries[0];
        deer.AdditionalSlots = Enumerable.Range(32, 4).ToArray();
        deer.VigilanceSeconds = 3f;
        deer.Biomes = (deer.Biomes ?? Array.Empty<BiomeType>()).Concat(new[]
            { BiomeType.Grassland, BiomeType.Scrub, BiomeType.Steppe }).Distinct().ToArray();
        var rabbit = entries[1];
        rabbit.DisplayName = "Rabbit";
        rabbit.AdditionalSlots = Enumerable.Range(40, 6).ToArray();
        rabbit.VigilanceSeconds = 4f;
        foreach (string species in new[] { "Wolf", "Boar", "Fox" })
        {
            if (entries.Any(s => s != null && s.DisplayName == species)) continue;
            bool wolf = species == "Wolf", fox = species == "Fox";
            entries.Add(new CreatureSpecies
            {
                DisplayName = species, PerTerritory = 0,
                AdditionalSlots = Enumerable.Range(wolf ? 15 : fox ? 21 : 18, fox ? 2 : 3).ToArray(),
                HomeRangeMeters = wolf ? 180f : 90f,
                WalkSpeedMps = wolf ? 2.5f : fox ? 2.2f : 1.8f,
                DriftHomeSpeedMps = .8f, RespawnSeconds = 600f,
                MinAltitudeMeters = 2f, MaxAltitudeMeters = 2800f,
                Biomes = new[] { BiomeType.Forest, BiomeType.Taiga, BiomeType.Grassland, BiomeType.Scrub, BiomeType.Steppe },
                Faction = wolf || fox ? CreatureFaction.Predator : CreatureFaction.Wildlife, AwarenessMeters = wolf ? 60f : 40f,
                MaxHealth = wolf ? 5 : fox ? 2 : 6,
                Diet = (wolf || fox ? ResourceKind.Meat : ResourceKind.Plants) | ResourceKind.FreshWater,
                HungerSeconds = 1800f, ThirstSeconds = 900f, ConsumeUnitsPerSecond = .12f,
                BodyHeightMeters = wolf ? 1f : fox ? .65f : .95f,
                YieldItemId = "Hide", YieldCount = fox ? 1 : 2
            });
        }
        foreach (var entry in entries.Where(e => e != null && visuals.ContainsKey(e.DisplayName)))
        {
            entry.Visuals = visuals[entry.DisplayName];
            string profile = entry.DisplayName is "Wolf" or "Fox" ? "Wolf" : "Deer";
            entry.Perception = AssetDatabase.LoadAssetAtPath<CreaturePerceptionSettings>("Assets/Resources/Settings/CreaturePerception/" + profile + ".asset");
            var audio = AssetDatabase.LoadAssetAtPath<CreatureAudioSettings>("Assets/Art/Creatures/" + entry.DisplayName + "/" + entry.DisplayName + "Audio.asset");
            if (audio != null) entry.Audio = audio;
        }
        library.Species = entries.ToArray();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
    }

    public static void BuildSpecies(string species, string modelFile, string textureFile, string idleName,
        string walkName, string runName, string attackName, string deathName, float walkSpeed, float runSpeed)
    {
        string folder = Root + species + "/";
        var importer = AssetImporter.GetAtPath(folder + modelFile) as ModelImporter;
        if (importer != null && !importer.isReadable)
        {
            // Carcass flesh removal reads the source topology once when creating its owned mesh.
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
        var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { Root + species })
            .Select(AssetDatabase.GUIDToAssetPath)
            .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
        AnimationClip Clip(string name) => name == null ? null : clips.FirstOrDefault(c => c.name == name)
            ?? throw new InvalidOperationException("Missing animal clip: " + name);
        var idle = Clip(idleName);
        var walk = Clip(walkName);
        var run = Clip(runName);
        var death = Clip(deathName);
        var attack = Clip(attackName);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(folder + modelFile);
        if (prefab == null) throw new InvalidOperationException("Missing animal model: " + folder + modelFile);
        string settingsPath = folder + species + "Visuals.asset";
        // Existing authored settings and prefabs belong to the artist after initial creation.
        if (AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(settingsPath) != null) return;
        var material = AssetDatabase.LoadAssetAtPath<Material>(folder + species + ".mat");
        if (material == null)
        {
            var reference = AssetDatabase.LoadAssetAtPath<Material>(Root + "Wolf/Wolf.mat");
            material = new Material(reference) { name = species };
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + textureFile));
            AssetDatabase.CreateAsset(material, folder + species + ".mat");
        }
        var model = UnityEngine.Object.Instantiate(prefab);
        try
        {
            model.name = species;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
            var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            foreach (var clip in clips.Where(c => AssetDatabase.GetAssetPath(c).EndsWith(".anim", StringComparison.Ordinal)))
            {
                clip.legacy = false;
                EditorUtility.SetDirty(clip);
            }
            idle.SampleAnimation(model, 0f);
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("Animal has no renderer: " + species);
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            var cleanPrefab = PrefabUtility.SaveAsPrefabAsset(model, folder + species + ".prefab");
            var settings = ScriptableObject.CreateInstance<CreatureVisualSettings>();
            settings.MalePrefab = settings.FemalePrefab = cleanPrefab;
            settings.Idle = idle; settings.Walk = walk; settings.Run = run;
            settings.Attack = attack; settings.Death = death;
            settings.ModelHeightMeters = MeasureHeight(model, bounds.size.y);
            settings.WalkMetersPerSecond = walkSpeed; settings.RunMetersPerSecond = runSpeed;
            AssetDatabase.CreateAsset(settings, settingsPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(model); }
    }

    // Importer culling bounds include empty space. Measure the posed geometry for presentation scale.
    static float MeasureHeight(GameObject model, float fallback)
    {
        float low = float.MaxValue, high = float.MinValue;
        foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh();
            try
            {
                skin.BakeMesh(mesh, true);
                foreach (var vertex in mesh.vertices)
                {
                    float y = skin.transform.TransformPoint(vertex).y;
                    low = Mathf.Min(low, y); high = Mathf.Max(high, y);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
        return Mathf.Max(.1f, high >= low ? high - low : fallback);
    }
}
