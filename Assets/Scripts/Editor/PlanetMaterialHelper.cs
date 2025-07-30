using UnityEngine;
using UnityEditor;

public class PlanetMaterialHelper : EditorWindow
{
    [MenuItem("Tools/Planet Material Helper")]
    public static void ShowWindow()
    {
        GetWindow<PlanetMaterialHelper>("Planet Material Helper");
    }

    void OnGUI()
    {
        GUILayout.Label("Planet Material Helper", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.HelpBox("If your planet appears solid white, the material might not support vertex colors.", MessageType.Info);

        GUILayout.Space(10);

        if (GUILayout.Button("Create Vertex Color Material (URP)"))
        {
            CreateVertexColorURP();
        }

        if (GUILayout.Button("Create Vertex Color Material (Built-in)"))
        {
            CreateVertexColorBuiltIn();
        }

        if (GUILayout.Button("Create Simple Unlit Material"))
        {
            CreateUnlitMaterial();
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Fix Selected Planet Material"))
        {
            FixSelectedPlanetMaterial();
        }

        GUILayout.Space(10);
        GUILayout.Label("Instructions:", EditorStyles.boldLabel);
        GUILayout.Label("1. Create a vertex color material above");
        GUILayout.Label("2. Assign it to your ColorSettings asset");
        GUILayout.Label("3. Or select your Planet and click 'Fix Selected Planet Material'");
    }

    void CreateVertexColorURP()
    {
        // Try to find URP Lit shader
        Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
        if (urpShader == null)
        {
            urpShader = Shader.Find("Lightweight Render Pipeline/Lit");
        }
        if (urpShader == null)
        {
            Debug.LogError("URP/Lit shader not found. Make sure you're using URP render pipeline.");
            return;
        }

        Material mat = new Material(urpShader);
        mat.name = "Planet_VertexColor_URP";

        // Enable vertex colors if supported
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", Color.white);
        }

        AssetDatabase.CreateAsset(mat, "Assets/Planet_VertexColor_URP.mat");
        AssetDatabase.SaveAssets();
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = mat;

        Debug.Log("Created URP vertex color material! Assign this to your ColorSettings.");
    }

    void CreateVertexColorBuiltIn()
    {
        Shader standardShader = Shader.Find("Standard");
        if (standardShader == null)
        {
            Debug.LogError("Standard shader not found.");
            return;
        }

        Material mat = new Material(standardShader);
        mat.name = "Planet_VertexColor_Standard";
        mat.SetColor("_Color", Color.white);

        AssetDatabase.CreateAsset(mat, "Assets/Planet_VertexColor_Standard.mat");
        AssetDatabase.SaveAssets();
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = mat;

        Debug.Log("Created Standard vertex color material! Assign this to your ColorSettings.");
    }

    void CreateUnlitMaterial()
    {
        // Create a simple unlit material that definitely uses vertex colors
        string shaderCode = @"
Shader ""Custom/PlanetVertexColor""
{
    Properties
    {
        _MainTex (""Texture"", 2D) = ""white"" {}
    }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include ""UnityCG.cginc""

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = i.color;
                return col;
            }
            ENDCG
        }
    }
}";

        // Save shader
        System.IO.File.WriteAllText("Assets/PlanetVertexColor.shader", shaderCode);
        AssetDatabase.Refresh();

        // Create material
        Shader customShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/PlanetVertexColor.shader");
        if (customShader != null)
        {
            Material mat = new Material(customShader);
            mat.name = "Planet_VertexColor_Custom";

            AssetDatabase.CreateAsset(mat, "Assets/Planet_VertexColor_Custom.mat");
            AssetDatabase.SaveAssets();
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = mat;

            Debug.Log("Created custom vertex color shader and material! This will definitely show vertex colors.");
        }
    }

    void FixSelectedPlanetMaterial()
    {
        GameObject selectedPlanet = Selection.activeGameObject;
        if (selectedPlanet == null)
        {
            Debug.LogWarning("No GameObject selected. Select your Planet GameObject first.");
            return;
        }

        Planet planet = selectedPlanet.GetComponent<Planet>();
        if (planet == null)
        {
            Debug.LogWarning("Selected GameObject doesn't have a Planet component.");
            return;
        }

        if (planet.colorSettings == null)
        {
            Debug.LogWarning("Planet has no ColorSettings assigned. Create ColorSettings first using Tools > Color Settings Helper.");
            return;
        }

        // Create a simple vertex color material
        CreateUnlitMaterial();

        // Wait for asset refresh, then assign
        EditorApplication.delayCall += () =>
        {
            Material newMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Planet_VertexColor_Custom.mat");
            if (newMat != null && planet.colorSettings != null)
            {
                planet.colorSettings.planetMaterial = newMat;
                EditorUtility.SetDirty(planet.colorSettings);
                planet.GeneratePlanet();
                Debug.Log("Assigned vertex color material to planet and regenerated!");
            }
        };
    }
}
