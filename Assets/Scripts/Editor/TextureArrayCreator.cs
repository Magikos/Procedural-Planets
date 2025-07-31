using UnityEditor;
using UnityEngine;

public static class TextureArrayCreator
{
    [MenuItem("Tools/Planet/Create Terrain Albedo Array")]
    public static void BuildTextureArray()
    {
        string folder = "Assets/Textures/Terrain/";
        string[] names = new[] { "sand_albedo", "grass_albedo", "rock_albedo", "snow_albedo" };
        Texture2D[] sources = new Texture2D[names.Length];

        for (int i = 0; i < names.Length; i++)
        {
            sources[i] = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}{names[i]}.png");
            if (sources[i] == null)
            {
                Debug.LogError($"Missing texture at {folder}{names[i]}.png");
                return;
            }
        }

        // 2) Create the Texture2DArray (all textures must share size & format)
        int size = sources[0].width;
        var texArray = new Texture2DArray(size, size, sources.Length, sources[0].format, mipChain: true);
        texArray.filterMode = FilterMode.Bilinear;
        texArray.wrapMode = TextureWrapMode.Repeat;

        // 3) Copy pixels into each slice
        for (int slice = 0; slice < sources.Length; slice++)
        {
            Graphics.CopyTexture(sources[slice], 0, 0, texArray, slice, 0);
        }
        texArray.Apply();

        // 4) Save to asset
        string path = "Assets/Textures/Terrain/TerrainAlbedoArray.asset";
        AssetDatabase.CreateAsset(texArray, path);
        AssetDatabase.SaveAssets();
        Debug.Log($"Created Texture2DArray at {path}");
    }
}
