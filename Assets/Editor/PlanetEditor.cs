using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(Planet))]
public class PlanetEditor : Editor
{
    Planet _planet;
    Editor _shapeSettingsEditor;
    Editor _colorSettingsEditor;

    public override void OnInspectorGUI()
    {
        using (var check = new EditorGUI.ChangeCheckScope())
        {
            base.OnInspectorGUI();
            if (check.changed)
            {
                _planet.GeneratePlanet();
            }
        }

        if (GUILayout.Button("Generate Planet"))
        {
            _planet.GeneratePlanet();
        }

        DrawSettingsEditor(_planet._shapeSettings, _planet.OnShapeSettingsChanged, ref _planet.ShapeSettingsFoldout, ref _shapeSettingsEditor);
        DrawSettingsEditor(_planet._colorSettings, _planet.OnColorSettingsChanged, ref _planet.ColorSettingsFoldout, ref _colorSettingsEditor);
    }

    void DrawSettingsEditor(Object settings, System.Action onSettingsChanged, ref bool foldout, ref Editor editor)
    {
        if (settings == null)
        {
            EditorGUILayout.HelpBox("Settings object is missing.", MessageType.Warning);
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