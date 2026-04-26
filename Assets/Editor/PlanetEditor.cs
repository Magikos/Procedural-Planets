using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(Planet))]
public class PlanetEditor : Editor
{
    Planet _planet;
    Editor _settingsEditor;

    public override void OnInspectorGUI()
    {
        using (var check = new EditorGUI.ChangeCheckScope())
        {
            base.OnInspectorGUI();
            if (check.changed)
            {
                _planet.GeneratePlanetAsync();
            }
        }

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

        DrawSettingsEditor(_planet._planetSettings, _planet.OnSettingsChanged, ref _planet.SettingsFoldout, ref _settingsEditor);
    }

    void DrawSettingsEditor(Object settings, System.Action onSettingsChanged, ref bool foldout, ref Editor editor)
    {
        if (settings == null)
        {
            EditorGUILayout.HelpBox("PlanetSettings is not assigned.", MessageType.Warning);
            return;
        }

        foldout = EditorGUILayout.InspectorTitlebar(foldout, settings);
        if (!foldout) return;

        using (var check = new EditorGUI.ChangeCheckScope())
        {
            CreateCachedEditor(settings, null, ref editor);
            editor.OnInspectorGUI();

            if (check.changed)
            {
                onSettingsChanged?.Invoke();
            }
        }
    }

    void OnEnable() { _planet = (Planet)target; }
}
