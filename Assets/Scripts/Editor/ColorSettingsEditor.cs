using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(ColorSettings))]
public class ColorSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Draw planet material
        SerializedProperty planetMaterialProperty = serializedObject.FindProperty("planetMaterial");
        EditorGUILayout.PropertyField(planetMaterialProperty);

        EditorGUILayout.Space();

        // Draw biome color settings
        SerializedProperty biomeColorSettingsProperty = serializedObject.FindProperty("biomeColorSettings");
        EditorGUILayout.PropertyField(biomeColorSettingsProperty, new GUIContent("Biome Color Settings"), true);

        serializedObject.ApplyModifiedProperties();
    }
}
