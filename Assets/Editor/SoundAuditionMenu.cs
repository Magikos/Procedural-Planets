using UnityEditor;
using UnityEditor.SceneManagement;

public static class SoundAuditionMenu
{
    public const string ScenePath = "Assets/Scenes/Tests/SoundAudition.unity";

    [MenuItem("Tools/Audio/Open Sound Audition")]
    static void Open()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(ScenePath);
    }

    [MenuItem("Tools/Audio/Open Sound Audition", true)]
    static bool CanOpen() => !EditorApplication.isPlayingOrWillChangePlaymode;
}
