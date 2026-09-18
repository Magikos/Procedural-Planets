using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HumanAccessoryMotionAuthor
{
    public const string ScenePath = "Assets/Scenes/Tests/HumanAccessoryMotionReview.unity";
    public const string Folder = "Assets/Art/Characters/Human/BodyReview/AccessoryMotion";

    [MenuItem("Tools/Actors/Human/Refine Accessories And Robe")]
    public static void Build()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != ScenePath || scene.isDirty)
            throw new InvalidOperationException("Open the saved accessory motion scene outside Play mode.");
        if (Directory.Exists(Folder)) throw new InvalidOperationException("Accessory refinement already exists.");
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var host = UnityEngine.Object.FindAnyObjectByType<HumanStyleReview>();
        host.ShowSecondaryMotionControls = true;
        var backpack = host.Accessories.First(a => a.name.Contains("Explorer backpack")).GetComponentInChildren<MeshFilter>().sharedMesh;
        var split = SplitCup(backpack);
        AssetDatabase.CreateAsset(split.bag, Folder + "/BackpackWithoutCup.asset");
        AssetDatabase.CreateAsset(split.cup, Folder + "/HangingCup.asset");
        var capeModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Human/Review/Townsfolk_Capes.fbx");
        var capeMesh = capeModel.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "SM_Chr_Mage_Cape_01").sharedMesh;
        var clothControls = host.GetComponent<HumanCapeClothReview>();
        if (clothControls != null) UnityEngine.Object.DestroyImmediate(clothControls);
        foreach (var actor in host.Characters)
        {
            foreach (var item in host.Accessories.Where(a => a.transform.IsChildOf(actor.transform)))
            {
                if (item.name.EndsWith("Priest hat") && actor.name.EndsWith("_Fit"))
                    item.transform.position -= actor.transform.up * .045f;
                if (item.name.EndsWith("Belt pouch"))
                {
                    var move = item.transform.parent.InverseTransformVector(-actor.transform.right * .035f);
                    item.transform.localPosition += move;
                    var fit = item.GetComponent<HumanAccessoryFit>();
                    if (fit != null) fit.NeutralLocalPosition += move;
                    var filter = item.GetComponentInChildren<MeshFilter>();
                    var renderer = filter.GetComponent<MeshRenderer>();
                    var pivot = new GameObject("Pouch suspension").transform;
                    pivot.SetParent(item.transform, false); pivot.localPosition = new Vector3(0, .115f, 0);
                    var visual = Visual("Swinging pouch", filter.sharedMesh, renderer.sharedMaterials, pivot);
                    visual.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
                    renderer.enabled = false;
                    var tip = new GameObject("Pouch mass").transform; tip.SetParent(pivot, false); tip.localPosition = Vector3.down * .1f;
                    Motion(pivot.gameObject, host, actor, new[] { pivot, tip }, 2f, .45f, 22f, .4f, fit);
                }
                if (item.name.EndsWith("Explorer backpack"))
                {
                    var filter = item.GetComponentInChildren<MeshFilter>();
                    filter.sharedMesh = split.bag;
                    var pivot = new GameObject("Cup suspension").transform;
                    pivot.SetParent(item.transform, false); pivot.localPosition = new Vector3(0, .39f, -.17f);
                    var visual = Visual("Cup and hanging strap", split.cup, filter.GetComponent<MeshRenderer>().sharedMaterials, pivot);
                    visual.localRotation = Quaternion.Euler(0, 90, 0);
                    visual.localPosition = -(visual.localRotation * new Vector3(.19f, .39f, -.03f));
                    var tip = new GameObject("Cup mass").transform; tip.SetParent(pivot, false); tip.localPosition = Vector3.down * .3f;
                    Motion(pivot.gameObject, host, actor, new[] { pivot, tip }, 1.8f, .4f, 20f, .7f, item.GetComponent<HumanAccessoryFit>());
                }
                if (item.name.Contains("Mage cape"))
                {
                    var skin = item.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.enabled);
                    var cloth = skin.GetComponent<Cloth>();
                    var contacts = cloth != null ? cloth.capsuleColliders : Array.Empty<CapsuleCollider>();
                    if (cloth != null) UnityEngine.Object.DestroyImmediate(cloth);
                    skin.sharedMesh = capeMesh;
                    var bones = Enumerable.Range(1, 6).Select(i => item.GetComponentsInChildren<Transform>().Single(t => t.name == "Cape_0" + i)).ToArray();
                    var motion = Motion(item, host, actor, bones, 1.2f, .4f, 75f, 1f, null);
                    motion.Chain.GroundClearance = .03f; motion.UseFloor = true; motion.Contacts = AddCalfContacts(actor, contacts);
                    item.name = item.name.Replace("(cloth trial)", "(spring bones)");
                }
            }
            if (actor.name.Contains("Monk")) RefineRobe(actor);
        }
        foreach (var label in UnityEngine.Object.FindObjectsByType<TextMesh>())
            label.text = label.text.Replace("/ ORIGINAL", "/ SOURCE RIG");
        EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
    }

    static Transform Visual(string name, Mesh mesh, Material[] materials, Transform parent)
    {
        var visual = new GameObject(name).transform; visual.SetParent(parent, false);
        visual.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        visual.gameObject.AddComponent<MeshRenderer>().sharedMaterials = materials;
        return visual;
    }

    public static CapsuleCollider[] AddCalfContacts(Animator actor, CapsuleCollider[] contacts)
    {
        if (contacts.Any(c => c.name.Contains("LowerLeg"))) return contacts;
        var result = contacts.ToList();
        foreach (var contact in contacts.Where(c => c.name.Contains("UpperLeg"))) contact.radius = .18f;
        foreach (bool left in new[] { true, false })
        {
            var bone = left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
            var obj = new GameObject("Cape cloth collision / " + bone);
            obj.transform.SetPositionAndRotation(actor.transform.TransformPoint(new Vector3(left ? -.1f : .1f, .25f, 0)), actor.transform.rotation);
            obj.transform.SetParent(actor.GetBoneTransform(bone), true);
            var contact = obj.AddComponent<CapsuleCollider>();
            contact.radius = .16f; contact.height = .46f; contact.isTrigger = true;
            result.Add(contact);
        }
        return result.ToArray();
    }

    static HumanAccessoryMotion Motion(GameObject root, HumanStyleReview host, Animator actor,
        Transform[] bones, float frequency, float damping, float angle, float gravity, HumanAccessoryFit fit)
    {
        var motion = root.AddComponent<HumanAccessoryMotion>();
        motion.Review = host; motion.Actor = actor.transform; motion.Mount = fit;
        motion.Chain = new SpringChainDefinition { Bones = bones, Frequency = frequency,
            Damping = damping, AngleLimit = angle, GravityScale = gravity };
        return motion;
    }

    public static (Mesh bag, Mesh cup) SplitCup(Mesh source)
    {
        var vertices = source.vertices;
        var parents = Enumerable.Range(0, vertices.Length).ToArray();
        int Find(int i) { while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; } return i; }
        void Join(int a, int b) => parents[Find(a)] = Find(b);
        var welded = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < vertices.Length; i++)
        {
            var point = Vector3Int.RoundToInt(vertices[i] * 100000);
            if (welded.TryGetValue(point, out int other)) Join(i, other); else welded.Add(point, i);
        }
        var triangles = source.triangles;
        for (int i = 0; i < triangles.Length; i += 3) { Join(triangles[i], triangles[i + 1]); Join(triangles[i], triangles[i + 2]); }
        var cupComponents = new HashSet<int>();
        foreach (var group in Enumerable.Range(0, vertices.Length).GroupBy(Find))
        {
            var bounds = new Bounds(vertices[group.First()], Vector3.zero);
            foreach (int i in group) bounds.Encapsulate(vertices[i]);
            // File-verified right-side cup, fittings, and suspension strap. The bedroll and bag are separate islands.
            if (bounds.center.x > .17f) cupComponents.Add(group.Key);
        }
        var cup = new List<int>(); var bag = new List<int>();
        for (int i = 0; i < triangles.Length; i += 3)
            (cupComponents.Contains(Find(triangles[i])) ? cup : bag).AddRange(new[] { triangles[i], triangles[i + 1], triangles[i + 2] });
        if (cup.Count == 0 || bag.Count == 0) throw new InvalidOperationException("Cannot isolate backpack cup.");
        return (Subset(source, bag, "Backpack without cup"), Subset(source, cup, "Cup and suspension strap"));
    }

    static Mesh Subset(Mesh source, List<int> triangles, string name)
    {
        var mesh = UnityEngine.Object.Instantiate(source); mesh.name = name; mesh.triangles = triangles.ToArray();
        var vertices = mesh.vertices; var bounds = new Bounds(vertices[triangles[0]], Vector3.zero);
        foreach (int i in triangles) bounds.Encapsulate(vertices[i]);
        mesh.bounds = bounds; return mesh;
    }

    static void RefineRobe(Animator actor)
    {
        var reference = AssetDatabase.LoadAssetAtPath<GameObject>(HumanTownsfolkReviewAuthor.Folder + "/SM_Chr_Monk_01_Original.prefab")
            .GetComponentsInChildren<SkinnedMeshRenderer>().Single();
        var source = reference.sharedMesh;
        var skin = actor.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "SM_Chr_Monk_01" || r.name == "SOURCE / SM_Chr_Monk_01");
        var texture = new Texture2D(2, 2);
        Color32[] colors;
        try
        {
            texture.LoadImage(File.ReadAllBytes("Assets/Art/Characters/Human/Review/TownsfolkAtlas.png"));
            colors = source.uv.Select(u => (Color32)texture.GetPixel((int)(u.x * texture.width), (int)(u.y * texture.height))).ToArray();
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
        var positions = source.vertices;
        var covered = positions.Select((p, i) => colors[i].r == 255 && colors[i].g == 204 && colors[i].b == 174 && p.y > .13f && p.y < .65f).ToArray();
        var mesh = UnityEngine.Object.Instantiate(skin.sharedMesh);
        mesh.name = actor.name + "_RobeClearance";
        int removed = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var triangles = mesh.GetTriangles(s); var kept = new List<int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                if (covered[triangles[i]] || covered[triangles[i + 1]] || covered[triangles[i + 2]]) { removed++; continue; }
                kept.AddRange(new[] { triangles[i], triangles[i + 1], triangles[i + 2] });
            }
            mesh.SetTriangles(kept, s);
        }
        if (removed == 0) throw new InvalidOperationException("No covered leg skin found.");
        var vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            var c = colors[i]; var p = positions[i];
            bool cloth = (c.r == 88 && c.g == 63 && c.b == 47) || (c.r == 77 && c.g == 56 && c.b == 40)
                || (c.r == 111 && c.g == 79 && c.b == 58) || (c.r == 136 && c.g == 97 && c.b == 70);
            if (!cloth || p.y < .06f || p.y > .72f) continue;
            float amount = Mathf.Clamp01((.72f - p.y) / .3f);
            vertices[i] += new Vector3(p.x * .15f, 0, Mathf.Sign(p.z) * .025f) * amount;
        }
        mesh.vertices = vertices;
        RefreshNormals(mesh);
        mesh.RecalculateBounds(); mesh.UploadMeshData(false);
        AssetDatabase.CreateAsset(mesh, Folder + "/" + actor.name + "_Robe.asset");
        skin.sharedMesh = mesh;
    }

    static void RefreshNormals(Mesh mesh)
    {
        var frames = new List<(string name, float weight, Vector3[] delta)>();
        for (int i = 0; i < mesh.blendShapeCount; i++)
            for (int f = 0; f < mesh.GetBlendShapeFrameCount(i); f++)
            {
                var delta = new Vector3[mesh.vertexCount]; mesh.GetBlendShapeFrameVertices(i, f, delta, null, null);
                frames.Add((mesh.GetBlendShapeName(i), mesh.GetBlendShapeFrameWeight(i, f), delta));
            }
        mesh.ClearBlendShapes(); mesh.RecalculateNormals();
        var vertices = mesh.vertices; var normals = mesh.normals;
        var scratch = new Mesh { vertices = vertices, triangles = mesh.triangles };
        try
        {
            foreach (var frame in frames)
            {
                scratch.vertices = vertices.Select((v, i) => v + frame.delta[i]).ToArray(); scratch.RecalculateNormals();
                mesh.AddBlendShapeFrame(frame.name, frame.weight, frame.delta, scratch.normals.Select((n, i) => n - normals[i]).ToArray(), null);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(scratch); }
    }
}
