using System;
using UnityEngine;

// Point-loaded textures avoid a mandatory structured-buffer binding in scenes without a planet.
public sealed class RiverGpu : IDisposable
{
    Texture2D _segments, _ranges, _indices;
    static readonly int SegmentsId = Shader.PropertyToID(ShaderGlobalIds.RiverSegments);
    static readonly int RangesId = Shader.PropertyToID(ShaderGlobalIds.RiverRanges);
    static readonly int IndicesId = Shader.PropertyToID(ShaderGlobalIds.RiverIndices);
    static readonly int RadiusId = Shader.PropertyToID(ShaderGlobalIds.RiverPlanetRadius);
    static readonly int ActiveId = Shader.PropertyToID(ShaderGlobalIds.RiverActive);

    public RiverGpu(RiverField field)
    {
        var data = field.Data;
        var segments = new Color[Mathf.Max(1, data.Segments.Length) * 5];
        for (int i = 0; i < data.Segments.Length; i++)
        {
            var s = data.Segments[i];
            segments[i * 5] = ToColor(s.A); segments[i * 5 + 1] = ToColor(s.B);
            segments[i * 5 + 2] = ToColor(s.Shape); segments[i * 5 + 3] = ToColor(s.Flow);
            segments[i * 5 + 4] = ToColor(s.Profile);
        }
        _segments = Texture(segments, 1024, "River segments");
        var ranges = new Color[data.Ranges.Length];
        for (int i = 0; i < ranges.Length; i++) ranges[i] = new Color(data.Ranges[i].x, data.Ranges[i].y, 0, 0);
        _ranges = Texture(ranges, RiverFieldData.Resolution, "River ranges");
        var indices = new Color[Mathf.Max(1, data.Indices.Length)];
        for (int i = 0; i < data.Indices.Length; i++) indices[i] = new Color(data.Indices[i], 0, 0, 0);
        _indices = Texture(indices, 1024, "River indices");
        Shader.SetGlobalTexture(SegmentsId, _segments);
        Shader.SetGlobalTexture(RangesId, _ranges);
        Shader.SetGlobalTexture(IndicesId, _indices);
        Shader.SetGlobalFloat(RadiusId, data.PlanetRadius);
        Shader.SetGlobalInt(ActiveId, data.Segments.Length > 0 ? 1 : 0);
    }

    static Color ToColor(Unity.Mathematics.float4 v) => new Color(v.x, v.y, v.z, v.w);

    static Texture2D Texture(Color[] input, int width, string name)
    {
        int height = Mathf.Max(1, Mathf.CeilToInt((float)input.Length / width));
        var pixels = new Color[width * height];
        Array.Copy(input, pixels, input.Length);
        var texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
        { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    public static void Bind(ComputeShader compute, int kernel)
    {
        compute.SetInt(ActiveId, Shader.GetGlobalInt(ActiveId));
        compute.SetFloat(RadiusId, Shader.GetGlobalFloat(RadiusId));
        compute.SetTexture(kernel, SegmentsId, Shader.GetGlobalTexture(SegmentsId) ?? Texture2D.blackTexture);
        compute.SetTexture(kernel, RangesId, Shader.GetGlobalTexture(RangesId) ?? Texture2D.blackTexture);
        compute.SetTexture(kernel, IndicesId, Shader.GetGlobalTexture(IndicesId) ?? Texture2D.blackTexture);
    }

    public void Dispose()
    {
        Shader.SetGlobalInt(ActiveId, 0);
        Shader.SetGlobalTexture(SegmentsId, Texture2D.blackTexture);
        Shader.SetGlobalTexture(RangesId, Texture2D.blackTexture);
        Shader.SetGlobalTexture(IndicesId, Texture2D.blackTexture);
        Destroy(_segments); Destroy(_ranges); Destroy(_indices);
    }

    static void Destroy(UnityEngine.Object value)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
}
