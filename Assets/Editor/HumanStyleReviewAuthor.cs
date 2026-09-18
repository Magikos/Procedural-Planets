using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HumanStyleReviewAuthor
{
    public const string ScenePath = "Assets/Scenes/Tests/HumanStyleReview.unity";
    const string Folder = HumanTrialAuthor.Folder + "/Review";
    const string SourcePack = "D:/Unity/Explore Assets/Assets/Synty/PolygonFantasyKingdom/";
    static readonly string[] Models = { "SM_Wep_Sword_01", "SM_Wep_Shield_01", "SM_Chr_Attach_Priest_Hat_01",
        "SM_Bld_House_Wall_Door_01", "SM_Bld_House_Roof_Thatch_01", "Townsfolk_Capes" };

    [MenuItem("Tools/Actors/Human/Create Style Review Scene")]
    public static void Build()
    {
        if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Stop Play mode and preserve scene changes before creating the style review.");
        if (File.Exists(ScenePath)) throw new IOException("Style review already exists. Open it to preserve fitted changes.");
        HumanTrialAuthor.Import();
        Directory.CreateDirectory(Folder);
        var paths = new Dictionary<string, string>();
        foreach (string model in Models) paths[model] = HumanTrialAuthor.CopyArt(SourcePack + "Models/" + model + ".fbx", Folder + "/" + model + ".fbx");
        string atlas = HumanTrialAuthor.CopyArt(SourcePack + "Textures/Alts/PolygonFantasyKingdom_01_A.png", Folder + "/TownsfolkAtlas.png");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (string path in paths.Values)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.isReadable = true; importer.SaveAndReimport();
        }
        var reference = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Characters/Baseline/Hero.mat");
        var townsfolkMaterial = new Material(reference) { name = "Townsfolk review" };
        townsfolkMaterial.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(atlas));
        townsfolkMaterial.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(townsfolkMaterial, Folder + "/Townsfolk.mat");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var host = new GameObject("Style review controls").AddComponent<HumanStyleReview>();
        var fitted = Place(AssetDatabase.LoadAssetAtPath<GameObject>(HumanTrialAuthor.PrefabPath), "FITTED", new Vector3(-1.4f, 0f, 0f));
        var source = Place(AssetDatabase.LoadAssetAtPath<GameObject>(HumanoidAuthor.PrefabPath), "SOURCE BASELINE", new Vector3(1.4f, 0f, 0f));
        host.Characters = new[] { fitted.GetComponentInChildren<Animator>(), source.GetComponentInChildren<Animator>() };
        fitted.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        source.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        string clipsFolder = "Assets/Art/Characters/Animations/";
        AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(clipsFolder + name + ".anim")
            ?? AssetDatabase.LoadAllAssetsAtPath(clipsFolder + name + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
        host.Motions = new[] { "HumanoidIdle", "Relaxed Walk Forward", "HumanoidRun", "Crouch Idle", "Crouch Walk",
            "Basic Crawl Forward", "Crawl Enter", "Jump", "Vault", "Ledge Hang", "Basic Pull Up", "Surface Idle", "Underwater Swim" }.Select(Clip).ToArray();

        var attachments = new List<GameObject>();
        GameObject Model(string name, Vector3 position)
        {
            var obj = Place(AssetDatabase.LoadAssetAtPath<GameObject>(paths[name]), name, position);
            foreach (var r in obj.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = Enumerable.Repeat(townsfolkMaterial, r.sharedMaterials.Length).ToArray();
            return obj;
        }
        void Attach(string model, HumanBodyBones bone, Vector3 offset, Vector3 rotation, string label)
        {
            var obj = Model(model, Vector3.zero); obj.name = label;
            obj.transform.SetParent(host.Characters[0].GetBoneTransform(bone), false);
            obj.transform.localPosition = offset; obj.transform.localRotation = Quaternion.Euler(rotation);
            attachments.Add(obj); obj.SetActive(false);
        }
        Attach(Models[0], HumanBodyBones.RightHand, Vector3.zero, Vector3.zero, "Sword / placement trial");
        Attach(Models[1], HumanBodyBones.LeftHand, Vector3.zero, Vector3.zero, "Shield / placement trial");
        Attach(Models[2], HumanBodyBones.Head, Vector3.zero, Vector3.zero, "Priest hat / shape trial");

        var cape = Model("Townsfolk_Capes", new Vector3(-4f, 1.7f, 2f));
        cape.name = "Legacy capes / original rig reference";
        var torso = UnityEngine.Object.Instantiate(source);
        torso.name = "Legacy torso / original skeleton reference"; torso.transform.position = new Vector3(4f, 0f, 2f);
        foreach (var r in torso.GetComponentsInChildren<SkinnedMeshRenderer>()) r.enabled = r.name.Contains("Torso");
        Label("LEGACY GARMENTS\noriginal rigs / not converted", new Vector3(4f, 2.2f, 2f), .055f);
        Label("CAPES\noriginal rig / not converted", new Vector3(-4f, 2.2f, 2f), .055f);

        string[] animalPaths = { "Assets/Art/Creatures/Deer/DeerMale.prefab", "Assets/Art/Creatures/Wolf/Wolf.prefab" };
        host.Animals = new Animator[2]; host.AnimalIdles = new AnimationClip[2];
        for (int i = 0; i < animalPaths.Length; i++)
        {
            var animal = Place(AssetDatabase.LoadAssetAtPath<GameObject>(animalPaths[i]), i == 0 ? "DEER" : "WOLF", new Vector3(i == 0 ? -3.5f : 3.5f, 0f, -1.5f));
            var animator = animal.GetComponentInChildren<Animator>() ?? animal.AddComponent<Animator>();
            var visuals = AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(Path.GetDirectoryName(animalPaths[i]) + (i == 0 ? "/DeerVisuals.asset" : "/WolfVisuals.asset"));
            host.Animals[i] = animator;
            host.AnimalIdles[i] = visuals != null ? visuals.Idle : throw new InvalidOperationException("Missing animal visual settings.");
            animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
        }
        var door = Model("SM_Bld_House_Wall_Door_01", new Vector3(0f, 0f, 4f));
        var roof = Model("SM_Bld_House_Roof_Thatch_01", new Vector3(6f, 1.5f, 4f));
        host.Accessories = attachments.ToArray();
        Label("FITTED", new Vector3(-1.4f, 2.4f, 0f)); Label("SOURCE", new Vector3(1.4f, 2.4f, 0f));
        Label("PROJECT DEER", new Vector3(-3.5f, 1.9f, -1.5f), .055f); Label("PROJECT WOLF", new Vector3(3.5f, 1.6f, -1.5f), .055f);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "Neutral floor";
        ground.transform.position = new Vector3(0f, -.1f, 1f); ground.transform.localScale = new Vector3(22f, .2f, 18f);
        var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.24f, .28f, .29f) };
        AssetDatabase.CreateAsset(floorMaterial, Folder + "/Floor.mat"); ground.GetComponent<Renderer>().sharedMaterial = floorMaterial;
        var backdrop = GameObject.CreatePrimitive(PrimitiveType.Cube); backdrop.name = "Neutral backdrop";
        backdrop.transform.position = new Vector3(0f, 4f, 8f); backdrop.transform.localScale = new Vector3(28f, 8f, .2f);
        backdrop.GetComponent<Renderer>().sharedMaterial = floorMaterial;
        var ruler = GameObject.CreatePrimitive(PrimitiveType.Cube); ruler.name = "2 metre reference";
        ruler.transform.position = new Vector3(5.3f, 1f, 0f); ruler.transform.localScale = new Vector3(.05f, 2f, .05f);
        Label("2 m", new Vector3(5.3f, 2.2f, 0f), .06f);
        var light = new GameObject("Common daylight").AddComponent<Light>(); light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(40f, -35f, 0f); light.intensity = 1.3f;
        RenderSettings.sun = light;
        host.gameObject.AddComponent<ActorReviewLighting>().Sun = light;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = Color.gray;
        var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .16f, .2f);
        camera.nearClipPlane = .05f; camera.farClipPlane = 100f; camera.fieldOfView = 45f;
        camera.gameObject.AddComponent<AudioListener>();
        var fly = camera.gameObject.AddComponent<WorkbenchFlyCamera>(); fly.MoveSpeed = 4f; fly.RequireRightMouseToMove = true;
        host.ReviewCamera = camera;
        Transform View(string name, Vector3 position, Vector3 focus)
        {
            var view = new GameObject(name).transform; view.position = position; view.rotation = Quaternion.LookRotation(focus - position); return view;
        }
        host.Views = new[] {
            View("Whole collection", new Vector3(0f, 2.8f, -9f), new Vector3(0f, 1.2f, 1f)),
            View("Character comparison", new Vector3(0f, 2.2f, -7f), new Vector3(0f, 1.1f, 0f)),
            View("Fitted close", new Vector3(-1.4f, 1.9f, -3.5f), new Vector3(-1.4f, 1.1f, 0f)),
            View("Fitted back", new Vector3(-1.4f, 2f, 3f), new Vector3(-1.4f, 1.2f, 0f)) };
        host.SelectView(0);
        host.Motions[0].SampleAnimation(fitted, .4f); host.Motions[0].SampleAnimation(source, .4f);
        fitted.transform.SetPositionAndRotation(new Vector3(-1.4f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f));
        source.transform.SetPositionAndRotation(new Vector3(1.4f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f));
        File.WriteAllText(Folder + "/SOURCE.md", "# Style review art\n\n" + string.Join("\n", paths.Select(p => "- " + p.Key + ": " + SourcePack + "Models/" + p.Key + ".fbx -> " + p.Value)) +
            "\n\nTownsfolk palette: Textures/Alts/PolygonFantasyKingdom_01_A.png. Materials use Planet/PropLit.\n\n" + string.Join("\n", animalPaths) +
            "\n\nLegacy torso uses the existing source hero geometry and original skeleton. Legacy cape model retains its original rig. Neither is fitted to our body.\n");
        EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets(); Selection.activeGameObject = host.gameObject;
    }

    static GameObject Place(GameObject prefab, string name, Vector3 position)
    {
        if (prefab == null) throw new InvalidOperationException("Missing review asset: " + name);
        var obj = UnityEngine.Object.Instantiate(prefab); obj.name = name;
        obj.transform.SetPositionAndRotation(position, Quaternion.identity); return obj;
    }

    static void Label(string text, Vector3 position, float size = .08f)
    {
        var label = new GameObject(text).AddComponent<TextMesh>(); label.text = text;
        label.fontSize = 64; label.characterSize = size * .3f; label.anchor = TextAnchor.MiddleCenter;
        label.transform.position = position; label.transform.rotation = Quaternion.identity;
    }
}
