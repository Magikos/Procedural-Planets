Shader "Hidden/Very Animation/OnionSkin" {
	Properties{
		_Color("Main Color", Color) = (1, 1, 1, 1)
		_MainTex ("Base (RGB) Trans (A)", 2D) = "white" {}
		_AlphaTest("Alpha Test", Range(0.0, 1.0)) = 0.999
		_RimPower("Rim Power", Range(0.1, 8.0)) = 1.5
		_Fill("Interior Fill", Range(0.0, 1.0)) = 0.1
		[HideInInspector] _ColorMask("__ColorMask", Float) = 15
		[HideInInspector] _ZWrite("__ZWrite", Float) = 0
		[HideInInspector] _ZTest("__ZTest", Float) = 4
		[HideInInspector] _SrcBlend("__SrcBlend", Float) = 5
		[HideInInspector] _DstBlend("__DstBlend", Float) = 10
}

SubShader {
	Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "ForceNoShadowCasting" = "True"}
	LOD 100

	Lighting Off
	ZWrite [_ZWrite]
	ZTest [_ZTest]
	Cull Back
	Blend [_SrcBlend] [_DstBlend]
	ColorMask [_ColorMask]
	Offset 1, 1

	Pass {  
		CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			
			#include "UnityCG.cginc"

			struct appdata_t {
				float4 vertex : POSITION;
				float2 texcoord : TEXCOORD0;
				float3 normal : NORMAL;
			};

			struct v2f {
				float4 vertex : SV_POSITION;
				half2 texcoord : TEXCOORD0;
				float3 worldNormal : TEXCOORD1;
				float3 worldPos : TEXCOORD2;
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			fixed4 _Color;
			float _AlphaTest;
			float _RimPower;
			float _Fill;

			v2f vert (appdata_t v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
				o.worldNormal = UnityObjectToWorldNormal(v.normal);
				o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
				return o;
			}
			
			fixed4 frag (v2f i) : SV_Target
			{
				fixed4 col = tex2D(_MainTex, i.texcoord);
			    clip(col.a - _AlphaTest);
				col *= _Color; 
				
				float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
				float rim = 1 - abs(dot(viewDir, normalize(i.worldNormal)));
				float alpha = _Fill + (1 - _Fill) * pow(rim, _RimPower);
				col.a *= alpha;
				
				return col;
			}
		ENDCG
	}
}

}
