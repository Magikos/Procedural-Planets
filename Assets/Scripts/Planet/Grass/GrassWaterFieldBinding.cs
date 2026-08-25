using UnityEngine;

// Binds the water level field to a grass placement compute.
//
// Grass suppression used to gate on one global radius, which can only describe the ocean sphere - so grass
// grew straight through every lake perched above sea level, and the near-field cull counter read zero
// rejections while blades stood in open water. The field answers per direction instead.
//
// Compute shaders do NOT see shader globals, so the values WaterLevelTexture publishes have to be read back
// and set on the compute explicitly. Both placement computes need the identical binding, hence one place.
public static class GrassWaterFieldBinding
{
    static readonly int LevelTexId = Shader.PropertyToID("_GrassWaterLevelTex");
    static readonly int LevelResId = Shader.PropertyToID("_GrassWaterLevelRes");
    static readonly int BaseRadiusId = Shader.PropertyToID("_GrassWaterLevelBaseRadius");
    static readonly int SurfaceOffsetId = Shader.PropertyToID("_GrassWaterSurfaceOffset");

    static readonly int LevelTexGlobalId = Shader.PropertyToID(ShaderGlobalIds.WaterLevelTex);
    static readonly int LevelResGlobalId = Shader.PropertyToID(ShaderGlobalIds.WaterLevelRes);
    static readonly int BaseRadiusGlobalId = Shader.PropertyToID(ShaderGlobalIds.WaterLevelBaseRadius);
    static readonly int SurfaceOffsetGlobalId = Shader.PropertyToID(ShaderGlobalIds.WaterSurfaceOffset);

    // A compute kernel that declares a texture still needs one bound even on the path that never reads it,
    // so an unpublished field gets this rather than nothing. Resolution 0 is what actually disables the
    // lookup, and the compute then falls back to the caller's ocean radius exactly as before.
    static Texture2DArray _empty;

    public static void Bind(ComputeShader compute, int kernel)
    {
        if (compute == null)
            return;

        Texture levelTex = Shader.GetGlobalTexture(LevelTexGlobalId);
        int resolution = levelTex != null
            ? Mathf.RoundToInt(Shader.GetGlobalFloat(LevelResGlobalId))
            : 0;

        compute.SetInt(LevelResId, resolution);
        compute.SetFloat(BaseRadiusId, Shader.GetGlobalFloat(BaseRadiusGlobalId));
        compute.SetFloat(SurfaceOffsetId, Shader.GetGlobalFloat(SurfaceOffsetGlobalId));
        compute.SetTexture(kernel, LevelTexId, resolution > 0 ? levelTex : EmptyField());
    }

    static Texture EmptyField()
    {
        if (_empty == null)
        {
            _empty = new Texture2DArray(1, 1, 6, TextureFormat.RFloat, false)
            {
                name = "GrassWaterLevelFieldFallback",
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int face = 0; face < 6; face++)
                _empty.SetPixels(new[] { new Color(-1f, 0f, 0f, 0f) }, face);
            _empty.Apply(false, true);
        }

        return _empty;
    }
}
