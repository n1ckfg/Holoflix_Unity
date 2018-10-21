// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'
// Upgrade NOTE: replaced '_World2Object' with 'unity_WorldToObject'

Shader "Hidden/Holoflix/ParticleFallback"
{
	Properties 
	{
		_MainTex ("Texture", 2D) = "white" {}
		_Displacement ("Extrusion Amount", Range(-10,10)) = 0.5
		_ForcedPerspective("Forced Perspective", Range(-1,20)) = 0
		_ParticleSize ("Particle Size", Range(0.001, 0.25)) = 0.025
		_ParticleUV ("Particle UV", Range(0, 1)) = 1
		[Toggle(ENABLE_SOFTSLICING)] _softSlicingToggle ("Soft Sliced", Float) = 1
		[HideInInspector]_Dims ("UV Projection Scale", Vector) = (1,1,1,1)
	}

	SubShader 
	{
		Tags { "RenderType"="Opaque" }
		LOD 100
		ZWrite On
		
		Pass
		{
			CGPROGRAM
			#pragma vertex VS_Main
			#pragma fragment FS_Main
			#include "UnityCG.cginc"
			
			#pragma shader_feature ENABLE_SOFTSLICING
			#pragma multi_compile __ SOFT_SLICING 
			
			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				float2 uv0 : TEXCOORD0;
				float2 uv1 : TEXCOORD1;
			};

			struct FS_INPUT
			{
				float4	vertex		: POSITION;
				float2  uv			: TEXCOORD0;
				UNITY_FOG_COORDS(1)
				float	projPosZ	: TEXCOORD2;
			};
			
			sampler2D _MainTex;
			float4 _MainTex_ST;
			float _Displacement;
			float _ForcedPerspective;
			
			float _ParticleSize;
			half _ParticleUV;
			float4x4 _VP;
			float _softPercent;
			half4 _blackPoint;
			
			float4 _Dims;

			FS_INPUT VS_Main (appdata v)
			{
				float2 depthCoord = v.uv0.xy;
				depthCoord.x -= .5;
				float d = tex2Dlod(_MainTex, float4(depthCoord,0,0)).r * -_Displacement;
				v.vertex.xyz += v.normal * d;
				
				//perspective modifier to vert position
				float diffX = (.25 - depthCoord.x) ;  //.25 is the center of the lefthand image (the depth).  
				float diffY = .5 - depthCoord.y;
				v.vertex.x += diffX * _ForcedPerspective * d * 2; //the 2 compensates for the diff being only half of the relevant distance because the texture really holds 2 separate images
				v.vertex.y += diffY * _ForcedPerspective * d;
				
				v.vertex = mul(unity_ObjectToWorld, v.vertex);
				
				float3 up = mul(unity_ObjectToWorld, UNITY_MATRIX_IT_MV[0].xyz) * _ParticleSize;
				float3 right = mul(unity_ObjectToWorld, UNITY_MATRIX_IT_MV[1].xyz) * _ParticleSize;
				
				float2 upRight = v.uv1.yx * float2(1, -1);
				
				v.vertex += float4(right * upRight.x + up * upRight.y, 0.0f);
				
				v.vertex = mul(unity_WorldToObject, v.vertex);
				
				FS_INPUT o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				
				o.uv = TRANSFORM_TEX(v.uv0.xy, _MainTex) + float2(-unity_WorldToObject[0][0], unity_WorldToObject[1][1]) * _ParticleSize * _Dims.xy * _ParticleUV * v.uv1;
				
				UNITY_TRANSFER_FOG(o,o.vertex);
				o.projPosZ = o.vertex.z;
				return o;
			}
			
			float4 FS_Main(FS_INPUT i) : COLOR
			{
				fixed4 col = tex2D(_MainTex, i.uv);
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
					return col * mask;  //multiply mask after everything because _blackPoint must be included in there or we will get 'hardness' from non-black blackpoints		
				#endif
				return col;
			}
			ENDCG
		}
	}
}
