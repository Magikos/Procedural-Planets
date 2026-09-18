using System;
using UnityEngine;

public sealed class HumanBodyReview : MonoBehaviour
{
    public SkinnedMeshRenderer[] NativeParts = Array.Empty<SkinnedMeshRenderer>();
    public SkinnedMeshRenderer[] ConvertedParts = Array.Empty<SkinnedMeshRenderer>();
    [Range(0, 100)] public float SkeletonFit = 100;
    [Range(0, 100)] public float Muscular;
    [Range(0, 100)] public float Heavy;
    [Range(0, 100)] public float Skinny;
    [Range(0, 100)] public float Feminine;
    bool _refreshMeshes;

    void OnEnable() => _refreshMeshes = true;

    void RefreshMeshes()
    {
        // This Editor retained stale GPU deformation after fitting edits and reloads.
        // Upload the readable derived data and rebuild the renderer's mesh binding.
        foreach (var skin in ConvertedParts)
        {
            if (skin == null || skin.sharedMesh == null) continue;
            var mesh = skin.sharedMesh;
            mesh.UploadMeshData(false);
            skin.sharedMesh = null; skin.sharedMesh = mesh;
            skin.SetBlendShapeWeight(0, SkeletonFit);
        }
    }

    void LateUpdate()
    {
        // Wait until the animation graph and renderers have initialized for this Play session.
        if (_refreshMeshes) { RefreshMeshes(); _refreshMeshes = false; }
        ApplyBodyShapes();
    }

    public void ApplyBodyShapes()
    {
        foreach (var skin in ConvertedParts) if (skin != null) skin.SetBlendShapeWeight(0, SkeletonFit);
        ApplyShapes(NativeParts, 1);
        ApplyShapes(ConvertedParts, SkeletonFit / 100f);
    }

    void ApplyShapes(SkinnedMeshRenderer[] parts, float amount)
    {
        foreach (var skin in parts)
        {
            if (skin == null || skin.sharedMesh == null) continue;
            for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++)
            {
                string name = skin.sharedMesh.GetBlendShapeName(i);
                if (name.EndsWith("defaultBuff")) skin.SetBlendShapeWeight(i, Muscular * amount);
                else if (name.EndsWith("defaultHeavy")) skin.SetBlendShapeWeight(i, Heavy * amount);
                else if (name.EndsWith("defaultSkinny")) skin.SetBlendShapeWeight(i, Skinny * amount);
                else if (name.EndsWith("masculineFeminine")) skin.SetBlendShapeWeight(i, Feminine * amount);
            }
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width - 290, 12, 278, 350), GUI.skin.box);
        GUILayout.Label("BODY PARTS EXPERIMENT");
        GUILayout.Label("Source skeleton fit: " + SkeletonFit.ToString("F0") + "%");
        SkeletonFit = GUILayout.HorizontalSlider(SkeletonFit, 0, 100);
        GUILayout.Label("0 = original proportions. 100 = bone mapping.");
        GUILayout.Space(10);
        GUILayout.Label("Body shapes: fitted + source");
        GUILayout.Label("Source shapes follow the fit amount. Check the seams.");
        Muscular = Slider("Muscular", Muscular);
        Heavy = Slider("Heavy", Heavy);
        Skinny = Slider("Skinny", Skinny);
        Feminine = Slider("Feminine", Feminine);
        if (GUILayout.Button("Reset")) { SkeletonFit = 100; Muscular = Heavy = Skinny = Feminine = 0; }
        GUILayout.EndArea();
    }

    static float Slider(string name, float value)
    {
        GUILayout.Label(name + ": " + value.ToString("F0") + "%");
        return GUILayout.HorizontalSlider(value, 0, 100);
    }
}
