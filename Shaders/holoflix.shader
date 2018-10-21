// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Holoflix/Holoflix"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_Displacement ("Extrusion Amount", Range(-10,10)) = 0.5
		_ForcedPerspective("Forced Perspective", Range(-1,20)) = 0
		[Toggle(ENABLE_SOFTSLICING)] _softSlicingToggle ("Soft Sliced", Float) = 1
	}
	SubShader
	{
		Tags { "RenderType"="Opaque" }
		LOD 100
		ZWrite On

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			
			#include "UnityCG.cginc"
			
			#pragma shader_feature ENABLE_SOFTSLICING
			#pragma multi_compile __ SOFT_SLICING 

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float2 uv : TEXCOORD0;
				UNITY_FOG_COORDS(1)
				float projPosZ : TEXCOORD2; //Screen Z position
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			float _Displacement;
			float _ForcedPerspective;
			
			float _softPercent;
			half4 _blackPoint;
			
			v2f vert (appdata v)
			{
				float2 depthCoord = v.uv.xy;
				depthCoord.x -= .5;
				float d = tex2Dlod(_MainTex, float4(depthCoord,0,0)).r * -_Displacement;
				v.vertex.xyz += v.normal * d;
				
				//perspective modifier to vert position
				float diffX = (.25 - depthCoord.x) ;  //.25 is the center of the lefthand image (the depth).  
				float diffY = .5 - depthCoord.y;
				v.vertex.x += diffX * _ForcedPerspective * d * 2; //the 2 compensates for the diff being only half of the relevant distance because the texture really holds 2 separate images
				v.vertex.y += diffY * _ForcedPerspective * d;

				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.uv.xy, _MainTex);
				UNITY_TRANSFER_FOG(o,o.vertex);
				o.projPosZ = o.vertex.z;
				return o;
			}
			
			fixed4 frag (v2f i) : SV_Target
			{
				fixed4 col = tex2D(_MainTex, i.uv) + _blackPoint;
				UNITY_APPLY_FOG_COLOR(i.fogCoord, col, unity_FogColor); // fog towards black due to our blend mode
				
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
