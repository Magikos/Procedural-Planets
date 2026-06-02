using UnityEngine;

public sealed class GrassSurfaceAtlasGpuData : System.IDisposable
{
    static readonly int AtlasResolutionId = Shader.PropertyToID("_GrassSurfaceAtlasResolution");
    static readonly int[] RadiusIds =
    {
        Shader.PropertyToID("_GrassSurfaceRadius_F0"),
        Shader.PropertyToID("_GrassSurfaceRadius_F1"),
        Shader.PropertyToID("_GrassSurfaceRadius_F2"),
        Shader.PropertyToID("_GrassSurfaceRadius_F3"),
        Shader.PropertyToID("_GrassSurfaceRadius_F4"),
        Shader.PropertyToID("_GrassSurfaceRadius_F5"),
    };
    static readonly int[] NormalIds =
    {
        Shader.PropertyToID("_GrassSurfaceNormal_F0"),
        Shader.PropertyToID("_GrassSurfaceNormal_F1"),
        Shader.PropertyToID("_GrassSurfaceNormal_F2"),
        Shader.PropertyToID("_GrassSurfaceNormal_F3"),
        Shader.PropertyToID("_GrassSurfaceNormal_F4"),
        Shader.PropertyToID("_GrassSurfaceNormal_F5"),
    };

    public readonly Texture2D[] RadiusByFace;
    public readonly Texture2D[] NormalByFace;
    public readonly int AtlasResolution;

    public GrassSurfaceAtlasGpuData(Texture2D[] radiusByFace, Texture2D[] normalByFace, int atlasResolution)
    {
        RadiusByFace = radiusByFace;
        NormalByFace = normalByFace;
        AtlasResolution = atlasResolution;
        BindGlobals();
    }

    public bool TryGetFace(int face, out Texture2D radius, out Texture2D normal)
    {
        radius = null;
        normal = null;
        if (face < 0 || face >= 6) return false;
        if (RadiusByFace == null || NormalByFace == null) return false;

        radius = RadiusByFace[face];
        normal = NormalByFace[face];
        return radius != null && normal != null;
    }

    public Vector4 GetUvScaleOffset(PlanetChunk chunk)
    {
        if (chunk == null) return new Vector4(1f, 1f, 0f, 0f);
        float size = chunk.UvHalfExtent * 2f;
        return new Vector4(size, size,
            chunk.UvCenter.x - chunk.UvHalfExtent,
            chunk.UvCenter.y - chunk.UvHalfExtent);
    }

    public void Dispose()
    {
        ClearGlobals();
        DestroyTextureArray(RadiusByFace);
        DestroyTextureArray(NormalByFace);
    }

    void BindGlobals()
    {
        Shader.SetGlobalInt(AtlasResolutionId, AtlasResolution);
        for (int i = 0; i < 6; i++)
        {
            Shader.SetGlobalTexture(RadiusIds[i], RadiusByFace != null && i < RadiusByFace.Length ? RadiusByFace[i] : null);
            Shader.SetGlobalTexture(NormalIds[i], NormalByFace != null && i < NormalByFace.Length ? NormalByFace[i] : null);
        }
    }

    static void ClearGlobals()
    {
        Shader.SetGlobalInt(AtlasResolutionId, 0);
        for (int i = 0; i < 6; i++)
        {
            Shader.SetGlobalTexture(RadiusIds[i], null);
            Shader.SetGlobalTexture(NormalIds[i], null);
        }
    }

    static void DestroyTextureArray(Texture2D[] textures)
    {
        if (textures == null) return;
        for (int i = 0; i < textures.Length; i++)
        {
            if (textures[i] == null) continue;
            if (Application.isPlaying) Object.Destroy(textures[i]);
            else Object.DestroyImmediate(textures[i]);
            textures[i] = null;
        }
    }
}
