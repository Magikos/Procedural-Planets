using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Builds a review fixture using the existing presentation prototype.</summary>
public static class CreatureAnimationReviewAuthor
{
    public const string ScenePath = "Assets/Scenes/Tests/WildlifeAnimationReview.unity";

    [MenuItem("Tools/Creatures/Build Wildlife Animation Review Scene")]
    static void BuildMenu() => Build();

    public static string Build()
    {
        var library = AssetDatabase.LoadAssetAtPath<CreatureLibrary>("Assets/Resources/Settings/CreatureLibrary.asset")
            ?? throw new InvalidOperationException("Missing planet creature library.");
        var species = library.Species.Where(s => s != null && s.CruiseAltitudeMeters <= 0f && s.Visuals != null).ToArray();
        var deer = species.FirstOrDefault(s => s.DisplayName == "Deer")
            ?? throw new InvalidOperationException("The review needs the existing deer primary pair.");
        var additional = species.Where(s => s != deer).ToArray();
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            GameObject Create(string name)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, scene);
                return go;
            }
            var host = Create("Wildlife Animation Review");
            var prototype = host.AddComponent<CreatureAnimationPrototype>();
            prototype.Primary = deer.Visuals;
            prototype.AdditionalVisuals = additional.Select(s => s.Visuals).ToArray();
            prototype.AdditionalBodyHeights = additional.Select(s => s.BodyHeightMeters).ToArray();
            prototype.Walk = true; prototype.Speed = .7f; prototype.TurnRate = 15f;
            int count = additional.Length + 2;
            float center = (count - 3) * 1.75f;
            var cameraObject = Create("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            camera.orthographic = true; camera.orthographicSize = Mathf.Max(8f, count * 1.25f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.16f, .2f, .24f);
            cameraObject.transform.position = new Vector3(center, 12f, -18f);
            cameraObject.transform.LookAt(new Vector3(center, 1f, 1f));
            var sun = Create("Review light").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.5f;
            sun.transform.rotation = Quaternion.Euler(45f, -25f, 0f);

            var ground = Create("Prototype uneven ground");
            ground.AddComponent<MeshFilter>().sharedMesh = GroundMesh(-8f, (count - 1) * 3.5f + 5f);
            var renderer = ground.AddComponent<MeshRenderer>();
            const string materialPath = "Assets/Scenes/Tests/WildlifeReviewGround.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetColor("_BaseColor", new Color(.28f, .34f, .24f));
            material.SetFloat("_Smoothness", 0f); EditorUtility.SetDirty(material);
            renderer.sharedMaterial = material;
            // CreatureAnimationPrototype applies the shared preview material utility to its spawned models.
            for (int i = 0; i < count; i++)
            {
                string name = i < 2 ? "Deer " + (CreatureAnimationView.IsMale((ulong)i) ? "male" : "female") : additional[i - 2].DisplayName;
                var label = Create(name + " label").AddComponent<TextMesh>();
                label.text = name; label.fontSize = 64; label.characterSize = .055f;
                label.anchor = TextAnchor.MiddleCenter; label.color = Color.white;
                label.transform.position = new Vector3((i - 1) * 3.5f, i < 2 ? 2.7f : additional[i - 2].BodyHeightMeters + .8f, 0f);
                label.transform.rotation = camera.transform.rotation;
            }
            var instructions = Create("Review controls").AddComponent<TextMesh>();
            instructions.text = "Select Wildlife Animation Review: Walk / Stalk / Eat / Drink / Rest / Sleep\nToggle Feet, Spine, Look, Chains to compare procedural motion";
            instructions.fontSize = 48; instructions.characterSize = .045f; instructions.anchor = TextAnchor.MiddleCenter;
            instructions.transform.position = new Vector3(center, 5.5f, 2f);
            instructions.transform.rotation = camera.transform.rotation;
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save " + ScenePath);
            return ScenePath;
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    static Mesh GroundMesh(float left, float right)
    {
        const string path = "Assets/Scenes/Tests/WildlifeReviewGround.asset";
        int width = Mathf.CeilToInt((right - left) * 2f), depth = 32;
        var vertices = new Vector3[(width + 1) * (depth + 1)];
        var triangles = new int[width * depth * 6];
        for (int z = 0; z <= depth; z++)
        for (int x = 0; x <= width; x++)
        {
            float px = Mathf.Lerp(left, right, x / (float)width), pz = z * .5f - 8f;
            vertices[z * (width + 1) + x] = new Vector3(px, CreatureAnimationPrototype.GroundHeight(px, pz), pz);
        }
        int index = 0;
        for (int z = 0; z < depth; z++)
        for (int x = 0; x < width; x++)
        {
            int a = z * (width + 1) + x, b = a + width + 1;
            triangles[index++] = a; triangles[index++] = b; triangles[index++] = a + 1;
            triangles[index++] = a + 1; triangles[index++] = b; triangles[index++] = b + 1;
        }
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null) { mesh = new Mesh { name = "Wildlife prototype ground" }; AssetDatabase.CreateAsset(mesh, path); }
        mesh.Clear(); mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }
}
