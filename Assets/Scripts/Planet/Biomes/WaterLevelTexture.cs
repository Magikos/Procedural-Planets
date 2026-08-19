using UnityEngine;
using UnityEngine.Experimental.Rendering;

// Publishes the water level grid to the GPU as a cube-face texture array.
//
// Three shader-side consumers need to know how high water stands at a point rather than at the planet:
// the terrain shader fades grass out approaching water, and the volume decides both the underwater test
// and the caustic depth. All of them keyed off a single _SeaLevelRadius, so on a lake perched 40 m up
// grass grew to the waterline and past it, and the volume never considered the water to be water at all.
//
// One slice per cube face, indexed exactly as WaterBodyMap's own grid. RFloat rather than RHalf because
// levels sit around 0.01 in planet-radius units and a half float there resolves to roughly 1e-5, which is
// 5 cm of surface height - visible banding on a still lake. 192x192x6 at 4 bytes is 884 KB.
public sealed class WaterLevelTexture : System.IDisposable
{
    // Stored in place of WaterLevelGrid.NoWater, which is negative infinity and does not survive a texture
    // fetch. Far below any real elevation, so a shader comparing against it can never read it as water.
    public const float NoWaterSentinel = -1f;

    static readonly int _texId = Shader.PropertyToID(ShaderGlobalIds.WaterLevelTex);
    static readonly int _resId = Shader.PropertyToID(ShaderGlobalIds.WaterLevelRes);
    static readonly int _baseRadiusId = Shader.PropertyToID(ShaderGlobalIds.WaterLevelBaseRadius);

    Texture2DArray _texture;

    public void Publish(float[] level, int resolution, float baseRadiusLocal)
    {
        if (level == null || resolution <= 0 || level.Length != resolution * resolution * 6)
        {
            Clear();
            return;
        }

        if (_texture == null || _texture.width != resolution)
        {
            Release();
            _texture = new Texture2DArray(resolution, resolution, 6, GraphicsFormat.R32_SFloat,
                TextureCreationFlags.None)
            {
                name = "WaterLevelField",
                wrapMode = TextureWrapMode.Clamp,
                // Point sampling: neighbouring cells can belong to different bodies at different heights,
                // and interpolating between two lake surfaces produces a level belonging to neither.
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        int faceCells = resolution * resolution;
        var slice = new float[faceCells];
        for (int face = 0; face < 6; face++)
        {
            System.Array.Copy(level, face * faceCells, slice, 0, faceCells);
            for (int i = 0; i < faceCells; i++)
                if (float.IsNegativeInfinity(slice[i])) slice[i] = NoWaterSentinel;
            _texture.SetPixelData(slice, 0, face);
        }
        _texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        Shader.SetGlobalTexture(_texId, _texture);
        Shader.SetGlobalFloat(_resId, resolution);
        Shader.SetGlobalFloat(_baseRadiusId, baseRadiusLocal);
    }

    // Resolution 0 tells every shader to fall back to the global sea radius, which is the behaviour that
    // existed before this field, so a world with no solved water renders exactly as it used to.
    public void Clear() => Shader.SetGlobalFloat(_resId, 0f);

    void Release()
    {
        if (_texture == null) return;
        Object.DestroyImmediate(_texture);
        _texture = null;
    }

    public void Dispose()
    {
        Clear();
        Release();
    }
}
