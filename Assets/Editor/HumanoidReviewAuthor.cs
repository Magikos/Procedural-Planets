using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HumanoidReviewAuthor
{
    public const string ScenePath = "Assets/Scenes/Tests/HumanoidAnimationReview.unity";
    const string Folder = "Assets/Art/Characters";
    const string ReviewFolder = "Assets/Art/Materials/Review";

    [MenuItem("Tools/Actors/Create Humanoid Review Scene")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before authoring the review scene.");
        HumanoidAuthor.Build();
        AnimationClip Clip(string name) => AssetDatabase.LoadAllAssetsAtPath(Folder + "/Animations/" + name + ".fbx")
            .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
        var idle = Clip("HumanoidIdle"); var walk = Clip("Walk Forward"); var run = Clip("Run Forward");
        string[] traversalNames = { "Crouch Idle", "Crouch Walk", "Basic Crawl Idle", "Basic Crawl Forward", "Jump", "Vault", "Ledge Hang", "Basic Pull Up", "Step Up" };
        foreach (string name in new[] { "Surface Idle", "Slow Swim", "Underwater Idle", "Underwater Swim" }.Concat(traversalNames))
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Folder + "/Animations/" + name + ".fbx");
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            var clips = importer.clipAnimations;
            foreach (var clip in clips)
            {
                clip.events = Array.Empty<AnimationEvent>();
                clip.loopTime = name != "Jump" && name != "Vault" && name != "Basic Pull Up" && name != "Step Up";
                if (traversalNames.Contains(name))
                {
                    // The motor owns displacement. Remove the source stage height from retargeted poses.
                    bool crawl = name.StartsWith("Basic Crawl", StringComparison.Ordinal);
                    clip.lockRootHeightY = crawl;
                    clip.keepOriginalPositionY = crawl;
                    clip.heightFromFeet = !crawl;
                    clip.lockRootPositionXZ = false;
                }
            }
            importer.clipAnimations = clips; importer.SaveAndReimport();
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var owner = new GameObject("Humanoid controls").AddComponent<HumanoidAnimationPrototype>();
        owner.CharacterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HumanoidAuthor.PrefabPath);
        owner.Idle = idle; owner.Walk = walk; owner.Run = run;
        owner.SurfaceIdle = Clip("Surface Idle"); owner.SurfaceSwim = owner.SurfaceIdle;
        owner.FastSurfaceSwim = Clip("Surface Paddle");
        owner.UnderwaterIdle = Clip("Underwater Idle"); owner.UnderwaterSwim = Clip("Underwater Swim");
        owner.SwimDirectional = new[] { Clip("Swim Left"), Clip("Swim Right"), Clip("Swim Backward") };
        owner.Wade = HumanoidWaterAnimationAuthor.CreateWade(walk);
        owner.TraversalClips = traversalNames.Select(Clip).ToArray();
        owner.VaultMotion = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(Folder + "/Motion/Basic Vault Motion.asset");
        owner.StairClips = new[] { Clip("Stair Walk Up"), Clip("Stair Walk Down") };
        if (owner.VaultMotion != null) owner.TraversalClips[5] = owner.VaultMotion.Clip;
        owner.CrawlTransitions = new[] { Clip("Crawl Enter"), Clip("Crawl Exit") };
        owner.CrawlBackward = Clip("Crawl Backward");
        owner.CrawlSideways = new[] { "Crawl Left", "Crawl Right" }
            .Select(name => AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/Animations/" + name + ".anim")).ToArray();
        owner.CrawlCycleDistances = new Vector4(
            HumanoidCrawlStrafeAuthor.MeasureCycleDistance(owner.TraversalClips[3], owner.CharacterPrefab, Vector3.forward),
            HumanoidCrawlStrafeAuthor.MeasureCycleDistance(owner.CrawlSideways[0], owner.CharacterPrefab, Vector3.left),
            HumanoidCrawlStrafeAuthor.MeasureCycleDistance(owner.CrawlSideways[1], owner.CharacterPrefab, Vector3.right),
            HumanoidCrawlStrafeAuthor.MeasureCycleDistance(owner.CrawlBackward, owner.CharacterPrefab, Vector3.back));
        owner.DirectionalClips = ConfigureDirectionalClips();
        owner.WalkDiagonals = new[] { "Walk ForwardLeft", "Walk ForwardRight", "Walk BackwardLeft", "Walk BackwardRight" }
            .Select(ConfigureHumanoidLoop).ToArray();
        owner.RunDirectionalClips = new[] { "Left", "Right", "Backward", "ForwardLeft", "ForwardRight", "BackwardLeft", "BackwardRight" }
            .Select(direction => Clip("Run " + direction)).ToArray();
        owner.Fall = ConfigureFallClip();
        owner.Performances = ActorPerformanceReviewAuthor.CreateLibrary();
        owner.FollowActor = true;
        // The in-place walk has no root speed. Use the host's authored locomotion speed.
        owner.RunSpeed = Mathf.Max(owner.WalkSpeed + .5f, new Vector2(run.averageSpeed.x, run.averageSpeed.z).magnitude);
        owner.LookTarget = Target("Look target", new Vector3(-.5f, 1.7f, 1.5f), Color.cyan);
        owner.HandTarget = Target("Right hand target", new Vector3(.35f, 1.2f, .3f), Color.yellow);
        owner.HandTarget.rotation = Quaternion.LookRotation(Vector3.left, Vector3.forward);
        owner.Panel = new GameObject("Review panel hinge").transform;
        owner.Panel.position = new Vector3(.15f, 1.2f, .3f);
        owner.HandTarget.SetParent(owner.Panel, true);
        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube); panel.name = "Hinged panel";
        panel.transform.SetParent(owner.Panel, false); panel.transform.localPosition = new Vector3(.1f, -.15f, .06f);
        panel.transform.localScale = new Vector3(.3f, .5f, .04f);
        UnityEngine.Object.DestroyImmediate(panel.GetComponent<Collider>());
        panel.GetComponent<Renderer>().sharedMaterial = Material("ReviewPanel", new Color(.45f, .25f, .1f));

        var material = Material("ReviewGround", new Color(.32f, .37f, .32f));
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ReviewFolder + "/ReviewGround.asset");
        {
            const int count = 64;
            var vertices = new Vector3[(count + 1) * (count + 1)];
            var triangles = new int[count * count * 6];
            for (int z = 0; z <= count; z++) for (int x = 0; x <= count; x++)
            {
                float px = x * .375f - 12f, pz = z * .375f - 12f;
                vertices[z * (count + 1) + x] = new Vector3(px, HumanoidAnimationPrototype.Height(px, pz), pz);
                if (x == count || z == count) continue;
                int v = z * (count + 1) + x, t = (z * count + x) * 6;
                triangles[t] = v; triangles[t + 1] = v + count + 1; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + count + 1; triangles[t + 5] = v + count + 2;
            }
            bool create = mesh == null;
            if (create) mesh = new Mesh { name = "Humanoid uneven ground" }; else mesh.Clear();
            mesh.vertices = vertices; mesh.triangles = triangles;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (create) AssetDatabase.CreateAsset(mesh, ReviewFolder + "/ReviewGround.asset"); else EditorUtility.SetDirty(mesh);
        }
        var ground = new GameObject("Uneven ground");
        ground.AddComponent<MeshFilter>().sharedMesh = mesh;
        ground.AddComponent<MeshRenderer>().sharedMaterial = material;
        ground.AddComponent<MeshCollider>().sharedMesh = mesh;
        BuildCourse(owner);
        var water = GameObject.CreatePrimitive(PrimitiveType.Cube); water.name = "Review water surface";
        water.transform.position = new Vector3(7f, HumanoidAnimationPrototype.WaterLevel, 0f);
        water.transform.localScale = new Vector3(10f, .015f, 24f);
        UnityEngine.Object.DestroyImmediate(water.GetComponent<Collider>());
        water.GetComponent<Renderer>().sharedMaterial = Material("ReviewWater", new Color(.1f, .45f, .6f));
        var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
        camera.transform.position = new Vector3(2.6f, 1.85f, 3.6f);
        camera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1f, 0f) - camera.transform.position);
        camera.fieldOfView = 45f; camera.nearClipPlane = .05f; camera.farClipPlane = 150f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.15f, .19f, .24f);
        var fly = camera.gameObject.AddComponent<WorkbenchFlyCamera>(); fly.MoveSpeed = 3f; fly.RequireRightMouseToMove = true;
        camera.gameObject.AddComponent<AudioListener>();
        var light = new GameObject("Key light").AddComponent<Light>(); light.type = LightType.Directional;
        light.intensity = 1.3f; light.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.6f, .6f, .6f);
        EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
        Selection.activeGameObject = owner.gameObject;
    }

    static Material Material(string name, Color color)
    {
        string path = ReviewFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("URP Lit is required for the review scene.");
        material = new Material(shader) { name = name, color = color };
        AssetDatabase.CreateAsset(material, path); return material;
    }

    public static AnimationClip[] ConfigureDirectionalClips()
    {
        string[] names = { "Walk Left", "Walk Right", "Walk Backward",
            "Crouch-Walk-Left", "Crouch-Walk-Right", "Crouch-Walk-Backward" };
        return names.Select(ConfigureHumanoidLoop).ToArray();
    }

    public static AnimationClip ConfigureFallClip() => ConfigureHumanoidLoop("Fall");

    static AnimationClip ConfigureHumanoidLoop(string name)
    {
            string path = Folder + "/Animations/" + name + ".fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Missing owned directional art: " + path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.events = Array.Empty<AnimationEvent>(); clip.loopTime = true;
                clip.lockRootRotation = true; clip.lockRootHeightY = false; clip.lockRootPositionXZ = false;
                  clip.keepOriginalPositionY = false; clip.heightFromFeet = true;
                  // Measured ankle-height phase alignment against Walk Forward (80 samples per cycle).
                  clip.cycleOffset = name == "Walk Left" || name == "Walk ForwardLeft" || name == "Walk BackwardLeft" ? .0625f :
                      name == "Walk ForwardRight" ? .025f : name == "Walk Right" || name == "Walk BackwardRight" ? .05f :
                      name == "Walk Backward" ? .625f : clip.cycleOffset;
            }
            importer.clipAnimations = clips; importer.SaveAndReimport();
            var result = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            if (!result.isHumanMotion) throw new InvalidOperationException("Directional clip is not Humanoid: " + path);
            return result;
    }

    static void BuildCourse(HumanoidAnimationPrototype owner)
    {
        var obstacleMaterial = Material("ReviewObstacle", new Color(.5f, .36f, .2f));
        GameObject Box(string name, Vector3 center, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name;
            box.transform.position = center; box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = obstacleMaterial; return box;
        }
        Box("Slope 15 degrees", new Vector3(-9f, .9f, -5f), new Vector3(2f, .2f, 6f)).transform.rotation = Quaternion.Euler(-15f, 0f, 0f);
        for (int i = 0; i < 6; i++) Box("Stair " + (i + 1), new Vector3(-5f, (i + 1) * .1f, 3f + i * .55f),
            new Vector3(2f, (i + 1) * .2f, .55f));
        Box("Large step 0.75m", new Vector3(-1f, .375f, 5f), new Vector3(2f, .75f, 2f));
        Box("Vault barrier 1m", new Vector3(-9f, .5f, 5f), new Vector3(2f, 1f, .4f));
        Box("Grab ledge 2.2m", new Vector3(-1f, 1.1f, -5f), new Vector3(2f, 2.2f, 2f));
        for (int i = 0; i < 3; i++) Box("Parkour block " + (i + 1), new Vector3(-6f + i * 3f, .4f, 9f), new Vector3(1.5f, .8f, 1.5f));
        void Tunnel(string name, float x, float z, float clearance)
        {
            Box(name + " roof", new Vector3(x, clearance + .1f, z), new Vector3(2f, .2f, 2.5f));
            Box(name + " left", new Vector3(x - 1f, clearance * .5f, z), new Vector3(.15f, clearance, 2.5f));
            Box(name + " right", new Vector3(x + 1f, clearance * .5f, z), new Vector3(.15f, clearance, 2.5f));
        }
        Tunnel("Crouch tunnel", -5f, -5f, 1.3f); Tunnel("Crawl tunnel", -9f, 0f, .7f);
        Transform Station(string name, float x, float z)
        {
            var station = new GameObject(name).transform;
            station.position = new Vector3(x, HumanoidAnimationPrototype.Height(x, z) + .04f, z);
            var label = new GameObject(name + " label").AddComponent<TextMesh>(); label.text = name;
            label.fontSize = 60; label.characterSize = .04f; label.anchor = TextAnchor.MiddleCenter;
            label.transform.position = station.position + Vector3.up * 2.8f;
            label.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            return station;
        }
        owner.Stations = new[] { Station("Slope", -9f, -8.5f), Station("Stairs", -5f, 2f),
            Station("Large step", -1f, 3.35f), Station("Vault", -9f, 4f), Station("High ledge", -1f, -6.65f),
            Station("Crouch tunnel", -5f, -7f), Station("Crawl tunnel", -9f, -1.8f), Station("Block jumps", -6f, 9f) };
        owner.Stations[^1].position = new Vector3(-6f, .84f, 9f);
        owner.Stations[^1].rotation = Quaternion.Euler(0f, 90f, 0f);
        Physics.SyncTransforms();
    }

    static Transform Target(string name, Vector3 position, Color color)
    {
        var target = GameObject.CreatePrimitive(PrimitiveType.Sphere); target.name = name;
        target.transform.position = position; target.transform.localScale = Vector3.one * .08f;
        UnityEngine.Object.DestroyImmediate(target.GetComponent<Collider>());
        target.GetComponent<Renderer>().sharedMaterial = Material(name.Replace(" ", ""), color);
        return target.transform;
    }
}
