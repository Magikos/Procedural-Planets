using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BirdAnimationReviewAuthor
{
    public const string ScenePath = "Assets/Scenes/Tests/BirdAnimationReview.unity";

    [MenuItem("Tools/Creatures/Build Bird Animation Review Scene")]
    static void BuildMenu() => Build();

    public static string Build()
    {
        CreatureBirdAssetAuthor.BuildRigs();
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
            var review = Create("Bird Animation Review").AddComponent<BirdAnimationReview>();
            var camera = Create("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.gameObject.AddComponent<AudioListener>();
            camera.orthographic = true; camera.orthographicSize = 2.4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.16f, .2f, .24f);
            camera.transform.position = new Vector3(0f, 4f, -9f);
            camera.transform.LookAt(new Vector3(0f, 1.7f, 0f));
            review.ReviewCamera = camera;
            var light = Create("Review light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save " + ScenePath);
            return ScenePath;
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }
}
