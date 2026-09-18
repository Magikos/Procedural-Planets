Shader "Planet/River"
{
    Properties
    {
        _WaterColor("Water color", Color) = (.1,.35,.32,1)
        _FoamColor("Foam color", Color) = (.88,.98,.94,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+1" "RenderType"="Transparent" }
        Pass
        {
            Name "River"
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.0
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "../Graphics/Shaders/Includes/WaterSurfaceLighting.hlsl"
            float3 _PlanetCenter, _SunParams;
            float _NightAmbientIntensity, _SunIntensity;
            CBUFFER_START(UnityPerMaterial)
            float4 _WaterColor, _FoamColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float4 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float3 tangentWS:TEXCOORD2; float4 uv:TEXCOORD3; float4 color:COLOR; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.tangentWS = TransformObjectToWorldDir(v.tangentOS.xyz);
                o.uv = v.uv; o.color = v.color;
                return o;
            }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f*f*(3-2*f);
                float a = frac(sin(dot(i,float2(127.1,311.7)))*43758.5453);
                float b = frac(sin(dot(i+float2(1,0),float2(127.1,311.7)))*43758.5453);
                float c = frac(sin(dot(i+float2(0,1),float2(127.1,311.7)))*43758.5453);
                float d = frac(sin(dot(i+1,float2(127.1,311.7)))*43758.5453);
                return lerp(lerp(a,b,f.x),lerp(c,d,f.x),f.y);
            }
            half4 Frag(Varyings i):SV_Target
            {
                // Horizontal rivers use Planet/Ocean. This pass draws falling sheets only.
                float2 flow = float2(i.uv.x * .65, (i.uv.y - _Time.y * i.uv.z) * .2);
                float ripple = Noise(flow*2.3);
                float dx = Noise(flow+float2(.05,0))-Noise(flow-float2(.05,0));
                float dy = Noise(flow+float2(0,.05))-Noise(flow-float2(0,.05));
                float3 t = normalize(i.tangentWS), n = normalize(i.normalWS);
                float3 b = normalize(cross(n,t));
                n = normalize(n + (t*dx+b*dy)*2.0);
                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                if (dot(n,view)<0) n=-n;
                float3 up = PlanetSafeNormalize(i.positionWS - _PlanetCenter, n);
                float3 sunDir = PlanetSunDirection(_SunParams, up);
                float daylight = PlanetDaylight(up, sunDir);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float fresnel = .03 + .97*pow(1-saturate(dot(n,view)),5);
                float lighting = WaterSurfaceLight(saturate(dot(n, sunDir)), daylight,
                    sun.shadowAttenuation, _NightAmbientIntensity);
                float3 water = _WaterColor.rgb * lighting;
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float3 bottom = SampleSceneColor(screenUV + float2(dx,dy) * .008);
                water = lerp(bottom, water, .9);
                float3 sky = EvaluateSkyReflection(reflect(-view,n), up, sunDir, daylight);
                water = lerp(water,sky,fresnel*.65);
                float glitter = pow(saturate(dot(n,PlanetSafeNormalize(view+sunDir, sunDir))),96)*.8;
                water += sun.color * glitter * daylight * sun.shadowAttenuation
                    * smoothstep(.02, .24, dot(up, sunDir)) * saturate(_SunIntensity / 17.0);
                float bank = 1 - smoothstep(.02, .25, i.color.g);
                float foam = saturate(.60 + .32 * smoothstep(.2, .75, ripple)
                    + bank * Noise(flow * .6) * .25);
                water = lerp(water, _FoamColor.rgb * lighting, foam);
                float alpha = smoothstep(.015, .18, i.color.g) * (.62 + .25 * ripple);
                clip(alpha - .005);
                return half4(water,alpha);
            }
            ENDHLSL
        }
    }
}

