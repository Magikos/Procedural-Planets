using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HumanAccessoryReviewAuthor
{
    public const string ScenePath = "Assets/Scenes/Tests/HumanAccessoryReview.unity";
    const string Art = HumanTrialAuthor.Folder + "/Review/";

    [MenuItem("Tools/Actors/Human/Create Accessory Review")]
    public static void Create()
    {
        if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Stop Play mode and preserve scene edits before creating the accessory review.");
        if (File.Exists(ScenePath)) throw new IOException("Accessory review already exists. Preserve fitted scene changes.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(Art + "Townsfolk.mat");
        var models = new[] { "SM_Chr_Attach_Priest_Hat_01", "SM_Prop_Bag_Explorer_01", "SM_Item_Pouch_01", "Townsfolk_Capes" }
            .ToDictionary(n => n, n => AssetDatabase.LoadAssetAtPath<GameObject>(Art + n + ".fbx"));
        if (material == null || models.Values.Any(p => p == null)) throw new InvalidOperationException("Missing accessory review art.");

        var scene = EditorSceneManager.OpenScene(HumanTownsfolkReviewAuthor.ScenePath);
        var host = UnityEngine.Object.FindAnyObjectByType<HumanStyleReview>();
        var attachments = new List<GameObject>();
        var paths = new[] { "SM_Chr_Monk_01_Original", "SM_Chr_Monk_01_Fit", "SM_Chr_Peasant_Male_01_Fit", "SM_Chr_Peasant_Male_01_Original" };
        foreach (var old in host.Accessories) if (old != null) UnityEngine.Object.DestroyImmediate(old);
        for (int i = 0; i < paths.Length; i++)
        {
            var old = host.Characters[i];
            var actor = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HumanTownsfolkReviewAuthor.Folder + "/" + paths[i] + ".prefab"));
            actor.name = paths[i];
            actor.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
            var animator = actor.GetComponent<Animator>();
            host.Characters[i] = animator;
            UnityEngine.Object.DestroyImmediate(old.gameObject);
            string label = (i == 0 || i == 3 ? "SOURCE" : "FITTED") + (i < 2 ? " monk" : " peasant");

            void Attach(string model, string name, HumanBodyBones bone, Vector3 position, bool visible)
            {
                var obj = UnityEngine.Object.Instantiate(models[model]);
                obj.name = label + " / " + name;
                foreach (var r in obj.GetComponentsInChildren<Renderer>(true))
                    r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
                foreach (var a in obj.GetComponentsInChildren<Animator>()) UnityEngine.Object.DestroyImmediate(a);
                if (model == "Townsfolk_Capes")
                {
                    foreach (var r in obj.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        r.enabled = r.name == "SM_Chr_Mage_Cape_01";
                        r.updateWhenOffscreen = true;
                    }
                }
                // Fit in the actor's neutral model space, then preserve that offset on its animated bone.
                obj.transform.SetPositionAndRotation(actor.transform.TransformPoint(position), actor.transform.rotation);
                obj.transform.SetParent(animator.GetBoneTransform(bone), true);
                attachments.Add(obj);
                obj.SetActive(visible);
            }

            var head = actor.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Head).position);
            Attach("SM_Chr_Attach_Priest_Hat_01", "Priest hat", HumanBodyBones.Head, head + new Vector3(0, .12f, 0), true);
            Attach("SM_Prop_Bag_Explorer_01", "Explorer backpack", HumanBodyBones.UpperChest, new Vector3(-.06f, .97f, -.22f), i >= 2);
            Attach("SM_Item_Pouch_01", "Belt pouch", HumanBodyBones.Hips, new Vector3(.24f, .8f, .06f), true);
            Attach("Townsfolk_Capes", "Mage cape (no cloth simulation)", HumanBodyBones.UpperChest, new Vector3(0, 1.36f, -.1f), i < 2);
        }
        host.Accessories = attachments.ToArray();
        var body = host.GetComponent<HumanBodyReview>();
        var skins = host.Characters.Skip(1).Take(2).SelectMany(a => a.GetComponentsInChildren<SkinnedMeshRenderer>())
            .Where(r => !r.name.Contains("Cape")).ToArray();
        body.NativeParts = skins.Where(r => !r.name.StartsWith("SOURCE / ")).ToArray();
        body.ConvertedParts = skins.Where(r => r.name.StartsWith("SOURCE / ")).ToArray();
        Transform View(string name, Vector3 position, Vector3 target)
        {
            var view = new GameObject(name).transform;
            view.position = position; view.LookAt(target); return view;
        }
        host.Views = host.Views.Concat(new[]
        {
            View("Accessories / back comparison", new Vector3(0, 2.3f, 11f), new Vector3(0, 1.05f, 0)),
            View("Fitted backpack / close", new Vector3(2.2f, 1.9f, 2.1f), new Vector3(1.2f, 1.25f, 0)),
            View("Fitted cape / close", new Vector3(-2.4f, 1.9f, 3f), new Vector3(-1.2f, 1.05f, 0))
        }).ToArray();
        // Keep the back cameras clear while retaining building samples in the style comparison.
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name.StartsWith("SM_Bld_")) root.transform.position += Vector3.right * 8;
            if (root.name == "Neutral backdrop") root.transform.position += Vector3.forward * 7;
        }
        host.SelectView(0);
        EditorSceneManager.SaveScene(scene, ScenePath);
        BakeMountOffsets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Actors/Human/Bake Accessory Mount Offsets")]
    public static void BakeMountOffsets()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before baking accessory mounts.");
        var host = UnityEngine.Object.FindAnyObjectByType<HumanStyleReview>();
        if (host == null || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
            throw new InvalidOperationException("Open the accessory review scene before baking mounts.");
        var body = host.GetComponent<HumanBodyReview>();
        foreach (var actor in host.Characters.Where(a => a.name.EndsWith("_Fit")))
        {
            var reference = AssetDatabase.LoadAssetAtPath<GameObject>(HumanTownsfolkReviewAuthor.Folder + "/" + actor.name + ".prefab");
            var skin = reference.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name.StartsWith("SOURCE / "));
            var mesh = skin.sharedMesh;
            var vertices = mesh.vertices;
            var fit = new Vector3[vertices.Length];
            mesh.GetBlendShapeFrameVertices(mesh.GetBlendShapeIndex("SkeletonFit"), 0, fit, null, null);
            var matrix = reference.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
            var surface = vertices.Select((p, i) => matrix.MultiplyPoint3x4(p + fit[i])).ToArray();
            var indices = mesh.GetTriangles(0);
            foreach (var item in host.Accessories.Where(g => g.transform.IsChildOf(actor.transform)))
            {
                bool backpack = item.name.EndsWith("Explorer backpack");
                if (!backpack && !item.name.EndsWith("Belt pouch")) continue;
                var anchor = backpack ? new Vector3(0, 1.22f, -.2f) : new Vector3(.24f, .88f, .06f);
                float best = float.PositiveInfinity; int triangle = -1; Vector3 weights = default;
                for (int t = 0; t < indices.Length; t += 3)
                {
                    var a = surface[indices[t]]; var b = surface[indices[t + 1]]; var c = surface[indices[t + 2]];
                    if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-14f) continue;
                    var w = HumanBodyShapeAuthor.ClosestWeights(anchor, a, b, c);
                    float distance = (a * w.x + b * w.y + c * w.z - anchor).sqrMagnitude;
                    if (distance < best) { best = distance; triangle = t; weights = w; }
                }
                if (triangle < 0 || best > .09f) throw new InvalidOperationException("Cannot find a nearby garment mount: " + item.name);
                var parent = reference.GetComponentsInChildren<Transform>().Single(t => t.name == item.transform.parent.name);
                var toParent = parent.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                var offsets = new Vector3[4];
                var names = new[] { "defaultBuff", "defaultHeavy", "defaultSkinny", "masculineFeminine" };
                for (int s = 0; s < names.Length; s++)
                {
                    int shape = Enumerable.Range(0, mesh.blendShapeCount).Single(i => mesh.GetBlendShapeName(i).EndsWith(names[s]));
                    var delta = new Vector3[vertices.Length];
                    mesh.GetBlendShapeFrameVertices(shape, 0, delta, null, null);
                    offsets[s] = toParent.MultiplyVector(delta[indices[triangle]] * weights.x
                        + delta[indices[triangle + 1]] * weights.y + delta[indices[triangle + 2]] * weights.z);
                    if (!float.IsFinite(offsets[s].sqrMagnitude)) throw new InvalidOperationException("Invalid mount movement: " + item.name);
                }
                var mount = item.GetComponent<HumanAccessoryFit>();
                if (mount == null)
                {
                    mount = item.AddComponent<HumanAccessoryFit>();
                    mount.NeutralLocalPosition = item.transform.localPosition;
                }
                mount.Body = body;
                mount.MuscularOffset = offsets[0]; mount.HeavyOffset = offsets[1];
                mount.SkinnyOffset = offsets[2]; mount.FeminineOffset = offsets[3];
                EditorUtility.SetDirty(mount);
            }
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }
}
