// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'
// Upgrade NOTE: replaced '_World2Object' with 'unity_WorldToObject'

Shader "Holoflix/ParticleAdditive"
{
	Properties 
	{
		_MainTex ("Texture", 2D) = "white" {}
		_Displacement ("Extrusion Amount", Range(-10,10)) = 0.5
		_ForcedPerspective("Forced Perspective", Range(-1,20)) = 0
		_brightnessMod("Brightness", Range(0,2)) = 1
		_ParticleTex ("Particle Texture", 2D) = "white" {}
		_ParticleSize ("Particle Size", Range(0.001, 0.5)) = 0.025
		_ParticleUV ("Particle UV", Range(0, 1)) = 1
		[Toggle(ENABLE_SOFTSLICING)] _softSlicingToggle ("Soft Sliced", Float) = 1
		[HideInInspector]_Dims ("UV Projection Scale", Vector) = (1,1,1,1)
	}

	SubShader 
	{
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        AlphaTest Greater .01
		ColorMask RGB
		Lighting Off ZWrite Off
		Blend SrcAlpha One
		
		Pass
		{
			CGPROGRAM
			#pragma vertex VS_Main
			#pragma fragment FS_Main
			#pragma geometry GS_Main
			#include "UnityCG.cginc"
			
			#pragma exclude_renderers glcore

			#pragma shader_feature ENABLE_SOFTSLICING
			#pragma multi_compile __ SOFT_SLICING
			
			struct GS_INPUT
			{
				float4	vertex		: POSITION;
				float3	up			: TEXCOORD0;
				float3	right		: TEXCOORD1;
				float2  uv0			: TEXCOORD2;
				float2  uv1			: TEXCOORD3;
				float	projPosZ	: TEXCOORD4; //Screen Z position
			};

			struct FS_INPUT
			{
				float4	vertex		: POSITION;
				float2  uv0			: TEXCOORD0;
				float2  uv1			: TEXCOORD1;
				float	projPosZ	: TEXCOORD2;
			};
			
			sampler2D _MainTex;
			float4 _MainTex_ST;
			float _Displacement;
			float _ForcedPerspective;
			
			float _ParticleSize;
			half _ParticleUV;
			float4x4 _VP;
			Texture2D _ParticleTex;
			SamplerState sampler_ParticleTex;
			float _softPercent;
			half4 _blackPoint;

			float _brightnessMod;
			
			float4 _Dims;

			GS_INPUT VS_Main(appdata_base v)
			{
				GS_INPUT o = (GS_INPUT)0;

				float2 depthCoord = v.texcoord.xy;
				depthCoord.x -= .5;
				float d = tex2Dlod(_MainTex, float4(depthCoord,0,0)).r * -_Displacement;
				v.vertex.xyz += v.normal * d;
				
				//perspective modifier to vert position
				float diffX = (.25 - depthCoord.x) ;  //.25 is the center of the lefthand image (the depth).  
				float diffY = .5 - depthCoord.y;
				v.vertex.x += diffX * _ForcedPerspective * d * 2; //the 2 compensates for the diff being only half of the relevant distance because the texture really holds 2 separate images
				v.vertex.y += diffY * _ForcedPerspective * d;
				
				o.vertex =  mul(unity_ObjectToWorld, v.vertex);
				
				o.up = mul(unity_ObjectToWorld, UNITY_MATRIX_IT_MV[0].xyz) * _ParticleSize;
				o.right = mul(unity_ObjectToWorld, UNITY_MATRIX_IT_MV[1].xyz) * _ParticleSize;
				
				o.uv0 = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
				o.uv1 = float2(0, 0);
				
				o.projPosZ = UnityObjectToClipPos(v.vertex).z;

				return o;
			}
			
			[maxvertexcount(4)]
			void GS_Main(point GS_INPUT p[1], inout TriangleStream<FS_INPUT> triStream)
			{		
				float4 v[4];
				v[0] = float4(p[0].vertex + p[0].right - p[0].up, 1.0f);
				v[1] = float4(p[0].vertex + p[0].right + p[0].up, 1.0f);
				v[2] = float4(p[0].vertex - p[0].right - p[0].up, 1.0f);
				v[3] = float4(p[0].vertex - p[0].right + p[0].up, 1.0f);
				
				float2 scaleUV = float2(unity_WorldToObject[0][0], unity_WorldToObject[1][1]) * _ParticleSize * _Dims.xy * _ParticleUV;
				
				float4x4 vp = UnityObjectToClipPos(unity_WorldToObject);
				FS_INPUT pIn;
				pIn.vertex = mul(vp, v[0]);
				pIn.uv0 = p[0].uv0 + scaleUV * float2(-1, 1);
				pIn.uv1 = float2(0, 1);
				pIn.projPosZ = p[0].projPosZ;
				triStream.Append(pIn);

				pIn.vertex =  mul(vp, v[1]);
				pIn.uv0 = p[0].uv0 + scaleUV * float2(1, 1);
				pIn.uv1 = float2(1, 1);
				pIn.projPosZ = p[0].projPosZ;
				triStream.Append(pIn);

				pIn.vertex =  mul(vp, v[2]);
				pIn.uv0 = p[0].uv0 + scaleUV * float2(-1, -1);
				pIn.uv1 = float2(0, 0);
				pIn.projPosZ = p[0].projPosZ;
				triStream.Append(pIn);

				pIn.vertex =  mul(vp, v[3]);
				pIn.uv0 = p[0].uv0 + scaleUV * float2(1, -1);
				pIn.uv1 = float2(1, 0);
				pIn.projPosZ = p[0].projPosZ;
				triStream.Append(pIn);
			}
			
			float4 FS_Main(FS_INPUT i) : COLOR
			{
				fixed4 col = tex2D(_MainTex, i.uv0) * _ParticleTex.Sample(sampler_ParticleTex, i.uv1);
				col.xyz += _blackPoint.xyz;
			
				#if defined(SOFT_SLICING) && defined(ENABLE_SOFTSLICING)
					float d = i.projPosZ;
					
					if (UNITY_NEAR_CLIP_VALUE == -1) //OGL will use this.
					{
						d = (d * .5) + .5;  //map  -1 to 1   into  0 to 1
					}
					
					//return d; //uncomment this to show the raw depth

					//note: if _softPercent == 0  that is the same as hard slice.

					float mask = 1;	
										
					if (d < _softPercent)
						mask *= d / _softPercent; //this is the darkening of the slice near 0 (near)
					else if (d > 1 - _softPercent)
						mask *= 1 - ((d - (1-_softPercent))/_softPercent); //this is the darkening of the slice near 1 (far)
					
					//return mask;
					return col * mask * _brightnessMod;  //multiply mask after everything because _blackPoint must be included in there or we will get 'hardness' from non-black blackpoints		
				#endif
				return col * _brightnessMod;
			}
			ENDCG
		}
	}
	//Fallback "Hidden/Holoflix/ParticleAdditiveFallback"
}
