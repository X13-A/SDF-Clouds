Shader"Custom/Volumetrics/CloudsShader"
{
    Properties
    {
            _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #define SHADER_STAGE_FRAGMENT 1
            #include "./CloudsLib.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 viewVector : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            UNITY_DECLARE_SCREENSPACE_TEXTURE(_CloudsRayMarchTexture);
            UNITY_DECLARE_SCREENSPACE_TEXTURE(_MainTex);
            float4 _MainTex_ST;
            fixed4 _LightColor0;
            float _LightMultiplier;

            UNITY_DECLARE_SCREENSPACE_TEXTURE(_CameraDepthTexture); // Unity depth

            // Shape
            float3 _BoundsMin;
            float3 _BoundsMax;

            // Shadows
            float3 _ShadowColor;
            float _ShadowingOffset;

            // Transmittance map
            sampler3D _TransmittanceMap;
            uint3 _TransmittanceMapResolution;
            float3 _TransmittanceMapCoverage;
            float3 _TransmittanceMapOrigin;
            float _CustomTime;

            v2f vert (appdata v)
            {
                v2f o;

                // Setup VR
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv = float2(o.uv.x, o.uv.y);

                // Calculate the view vector
                float4 clipPos = float4(o.uv * 2.0 - 1.0, 0.0, 1.0);
                float4 viewPos = mul(unity_CameraInvProjection, clipPos);
                viewPos.z = 1.0;
                viewPos.w = 0.0;
                o.viewVector = mul(unity_CameraToWorld, viewPos).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 rayPos = _WorldSpaceCameraPos;
                float viewLength = length(i.viewVector);
                float3 rayDir = i.viewVector.xyz / viewLength;
                float3 lightDir = normalize(-_WorldSpaceLightPos0);

                float depth = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CameraDepthTexture, i.uv);
                float depthDist = LinearEyeDepth(depth) * viewLength;

                float3 worldPos = rayPos + rayDir * depthDist;
                cloudShadowingResult shadowingRes = getCloudShadowing(worldPos, lightDir, _BoundsMin, _BoundsMax, _TransmittanceMap, _TransmittanceMapOrigin, _TransmittanceMapCoverage, true, _CustomTime);
                float shadowing = shadowingRes.shadowing;
                shadowing += _ShadowingOffset;
                shadowing = clamp(shadowing, 0.2, 1.0);
                shadowing = pow(shadowing, 5);

                fixed4 backgroundColor = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv);
                fixed2 read = tex2D(_CloudsRayMarchTexture, i.uv);
                fixed transmittance = read.r;
                fixed lightEnergy = read.g;
                float4 cloudColor = float4(_LightColor0.rgb * _LightMultiplier, 0);
                float shadowFactor = saturate(1.0 - lightEnergy);
                float3 shadowedColor = lerp(cloudColor.rgb, _ShadowColor, shadowFactor);

                cloudColor.rgb = shadowedColor * lightEnergy;

                // depth == 0 -> skybox
                if (depth > 0)
                {
                    backgroundColor *= shadowing;
                }
                return float4(backgroundColor.rgb * transmittance + cloudColor.rgb, 1);
            }
            ENDCG
        }
    }
}
