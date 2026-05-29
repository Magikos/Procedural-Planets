using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(Planet))]
public class PlanetEditor : Editor
{
    Planet _planet;
    Editor _settingsEditor;

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(_planet.IsGenerating))
        {
            if (GUILayout.Button(_planet.IsGenerating ? "Generating..." : "Generate Planet"))
            {
                _planet.GeneratePlanetAsync();
            }
        }

        if (_planet.IsGenerating)
        {
            var rect = GUILayoutUtility.GetRect(18, 18, "TextField");
            EditorGUI.ProgressBar(rect, 1f, "Generating planet...");
            Repaint();
        }

        DrawSettingsEditor(_planet.PlanetSettingsAsset, serializedObject.FindProperty("_settingsFoldout"), ref _settingsEditor);
    }

    void DrawSettingsEditor(Object settings, SerializedProperty foldoutProp, ref Editor editor)
    {
        if (settings == null)
        {
            EditorGUILayout.HelpBox("PlanetSettings is not assigned.", MessageType.Warning);
            return;
        }

        foldoutProp.boolValue = EditorGUILayout.InspectorTitlebar(foldoutProp.boolValue, settings);
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        if (!foldoutProp.boolValue) return;

        CreateCachedEditor(settings, null, ref editor);
        editor.OnInspectorGUI();
    }

    void OnEnable() { _planet = (Planet)target; }
}
