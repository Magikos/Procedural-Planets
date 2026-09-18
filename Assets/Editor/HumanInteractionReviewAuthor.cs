using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HumanInteractionReviewAuthor
{
    public const string ScenePath = "Assets/Scenes/Tests/HumanInteractionReview.unity";
    public const string Folder = "Assets/Art/Interactions/RoundOne";

    [MenuItem("Tools/Actors/Human/Create Interaction Review")]
    public static void Create()
    {
        if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Stop Play mode and preserve scene edits before creating the interaction review.");
        if (File.Exists(ScenePath)) throw new IOException("The interaction review already exists. Open it to preserve authored changes.");
        var scene = EditorSceneManager.OpenScene(HumanTrialAuthor.GameplayScene);
        var actor = UnityEngine.Object.FindFirstObjectByType<HumanoidAnimationPrototype>();
        var groundMaterial = GameObject.Find("Uneven ground").GetComponent<Renderer>().sharedMaterial;
        var camera = Camera.main;
        var light = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional);
        // Save a new identity before editing the copied workbench.
        EditorSceneManager.SaveScene(scene, ScenePath);
        foreach (var root in scene.GetRootGameObjects())
            if (root != actor.gameObject && root != camera.gameObject && root != light.gameObject)
                UnityEngine.Object.DestroyImmediate(root);
        actor.transform.position = new Vector3(0, 0, -5);
        actor.ShowControls = false; actor.EnableWater = false;
        actor.Panel = actor.HandTarget = actor.LookTarget = null;
        actor.FollowActor = true; actor.WalkLoop = false;
        actor.Performances = HumanoidRunningJumpAuthor.CreateLibrary();
        var review = actor.gameObject.AddComponent<HumanInteractionReview>(); review.Actor = actor;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Interaction room floor"; floor.transform.position = new Vector3(0, -.15f, 0);
        floor.transform.localScale = new Vector3(13, .3f, 15);
        floor.GetComponent<Renderer>().sharedMaterial = groundMaterial;
        var material = AssetDatabase.LoadAssetAtPath<Material>(HumanTrialAuthor.Folder + "/Review/Townsfolk.mat");
        if (material == null) throw new InvalidOperationException("The reviewed Townsfolk material is required.");
        var targets = new List<HumanInteractionReview.Target>();
        var approaches = new List<Transform>();

        GameObject Prop(string model, string label, Vector3 position, float scale = 1f, Vector3 rotation = default)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + model + ".fbx");
            if (prefab == null) throw new FileNotFoundException("Missing interaction prop: " + model);
            var wrapper = new GameObject(label);
            var visual = UnityEngine.Object.Instantiate(prefab, wrapper.transform);
            visual.name = model;
            visual.transform.localScale = Vector3.one * scale;
            visual.transform.localRotation = Quaternion.Euler(rotation);
            var renderers = visual.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
                r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
            }
            visual.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>())
            {
                var collider = filter.gameObject.AddComponent<BoxCollider>();
                collider.center = filter.sharedMesh.bounds.center; collider.size = filter.sharedMesh.bounds.size;
            }
            wrapper.transform.position = position;
            return wrapper;
        }

        void Target(string label, string task, Transform parent, Vector3 point, Vector3 approach,
            Transform hinge = null, Vector3 open = default, float diameter = .06f, Transform pickup = null, Transform leftContact = null)
        {
            var contact = new GameObject(label + " contact").transform;
            contact.SetParent(parent); contact.position = point; contact.localScale = Vector3.one * diameter;
            var station = new GameObject(label + " approach").transform;
            station.position = approach; station.rotation = Quaternion.identity;
            approaches.Add(station);
            targets.Add(new HumanInteractionReview.Target { Label = label, Task = task,
                Contact = contact, Hinge = hinge, OpenEuler = open, PickupRoot = pickup, LeftContact = leftContact });
        }

        void Label(string title, Vector3 position)
        {
            var text = new GameObject(title).AddComponent<TextMesh>();
            text.text = title; text.fontSize = 64; text.characterSize = .035f;
            text.anchor = TextAnchor.MiddleCenter; text.transform.position = position;
        }

        var chest = Prop("SM_Prop_Chest_01", "01 / Chest", new Vector3(-3.5f, 0, 2), 1.3f, new Vector3(0, 180, 0));
        var lid = chest.GetComponentsInChildren<Transform>().Single(t => t.name == "SM_Prop_Chest_01_Lid");
        var latch = chest.GetComponentsInChildren<Transform>().Single(t => t.name == "SM_Prop_Chest_01_Latch");
        Target("Chest", "Low latch reach. Preview the lid and inspect hand clearance.", latch, latch.position,
            new Vector3(-3.7f, 0, .95f), lid, new Vector3(-105, 0, 0));
        Label("CHEST / LOW REACH", new Vector3(-3.5f, 1.5f, 2.1f));

        var door = Prop("SM_Bld_Castle_Door_Single_01", "02 / Door", new Vector3(0, 0, 3));
        var doorHinge = door.transform.GetChild(0);
        Target("Door", "Standing handle reach. Preview the door swing from both sides.", doorHinge,
            new Vector3(-.32f, 1.18f, 2.83f), new Vector3(-.52f, 0, 2.43f), doorHinge, new Vector3(0, -90, 0));
        foreach (float x in new[] { -.65f, .65f })
        {
            var post = GameObject.CreatePrimitive(PrimitiveType.Cube); post.name = "Door frame post";
            post.transform.position = new Vector3(x, 1.25f, 3); post.transform.localScale = new Vector3(.18f, 2.5f, .35f);
            post.GetComponent<Renderer>().sharedMaterial = groundMaterial;
        }
        Label("DOOR / STANDING REACH", new Vector3(0, 2.85f, 3));

        var table = Prop("SM_Prop_Table_Wood_04", "03 / Table", new Vector3(3.4f, 0, 2), 1f, new Vector3(0, 90, 0));
        float top = table.GetComponentsInChildren<Renderer>().Max(r => r.bounds.max.y);
        var mug = Prop("SM_Item_Mug_Tankard_01", "Table / Tankard", new Vector3(2.7f, top, 1.55f));
        var bottle = Prop("SM_Item_Bottle_01", "Table / Bottle", new Vector3(3.45f, top, 2.1f), .65f);
        var book = Prop("SM_Item_Book_01", "Table / Book", new Vector3(4.05f, top, 1.8f), 1f, new Vector3(0, 0, 90));
        foreach (var item in new[] { mug, bottle, book })
        {
            var bounds = item.GetComponentInChildren<Renderer>().bounds;
            Target(item.name, "Table-height reach. Compare grip size and reach across the tabletop.", item.transform,
                bounds.center, new Vector3(bounds.center.x - .2f, 0, 1.2f), pickup: item.transform);
        }
        var key = Prop("SM_Item_Key_01", "Table / Key", new Vector3(3.35f, top, 1.67f), .65f, new Vector3(90, 0, 0));
        Target("Small key", "Small-object precision target.", key.transform, key.GetComponentInChildren<Renderer>().bounds.center,
            new Vector3(3.15f, 0, 1.2f), diameter: .025f, pickup: key.transform);
        Label("TABLE / PICKUP HEIGHTS", new Vector3(3.4f, 1.8f, 2.4f));

        var groundItem = Prop("SM_Item_Book_01", "04 / Ground item", new Vector3(-3.5f, 0, -2), 1f, new Vector3(0, 0, 90));
        Target("Ground item", "Ground pickup target. Compare standing and crouched reach before adding a bend motion.",
            groundItem.transform, groundItem.GetComponentInChildren<Renderer>().bounds.center, new Vector3(-3.7f, 0, -2.65f), pickup: groundItem.transform);
        Label("GROUND / BEND AND PICKUP", new Vector3(-3.5f, .7f, -1.6f));

        var crate = Prop("SM_Prop_Crate_Wood_01", "05 / Carry crate", new Vector3(0, 0, -2), .55f);
        var leftGrip = new GameObject("Carry crate left contact").transform;
        leftGrip.SetParent(crate.transform); leftGrip.position = new Vector3(-.30f, .16f, -2.08f);
        Target("Carry crate", "Two-hand pickup and carrying fixture.", crate.transform, new Vector3(.30f, .16f, -2.08f),
            new Vector3(0, 0, -2.95f), pickup: crate.transform, leftContact: leftGrip);
        Label("CRATE / TWO-HAND CARRY", new Vector3(0, 1.2f, -1.7f));

        var chair = Prop("SM_Prop_Chair_Wood_01", "06 / Chair", new Vector3(3.5f, 0, -2));
        Target("Chair", "Seat-height and approach fixture for sitting interactions.", chair.transform,
            new Vector3(3.5f, .5f, -2), new Vector3(3.3f, 0, -2.9f));
        Label("CHAIR / SEATED INTERACTION", new Vector3(3.5f, 1.5f, -1.7f));

        actor.Stations = approaches.ToArray(); review.Targets = targets.ToArray();
        actor.HandTarget = actor.LookTarget = targets[0].Contact;
        camera.transform.position = new Vector3(10, 10, -12); camera.transform.LookAt(new Vector3(0, .6f, .3f));
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.13f, .18f, .2f);
        camera.fieldOfView = 50;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(new Bounds(new Vector3(0, 1, 0), new Vector3(12, 4, 12)), true);
        EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
    }
}
