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
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 viewVector : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _LightColor0;
            sampler2D _LightTransmittanceTex;

            UNITY_DECLARE_SCREENSPACE_TEXTURE(_CameraDepthTexture); // Unity depth

            // Shape
            float3 _BoundsMin;
            float3 _BoundsMax;

            // Transmittance map
            sampler3D _TransmittanceMap;
            uint3 _TransmittanceMapResolution;
            float3 _TransmittanceMapCoverage;
            float3 _TransmittanceMapOrigin;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                float3 viewVector = mul(unity_CameraInvProjection, float4(v.uv * 2 - 1, 0, -1));
                o.viewVector = mul(unity_CameraToWorld, float4(viewVector, 0));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 rayPos = _WorldSpaceCameraPos;
                float viewLength = length(i.viewVector);
                float3 rayDir = i.viewVector.xyz / viewLength;
                float3 lightDir = normalize(-_WorldSpaceLightPos0);

                float depth = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CameraDepthTexture, i.uv);
                float depthDist = LinearEyeDepth(depth) * viewLength;

                float3 worldPos = rayPos + rayDir * depthDist;
                float shadowing = 1 - getCloudShadowing(worldPos, lightDir, _BoundsMin, _BoundsMax, _TransmittanceMap, _TransmittanceMapOrigin, _TransmittanceMapCoverage);

                fixed4 backgroundColor = tex2D(_MainTex, i.uv);
                fixed2 read = tex2D(_LightTransmittanceTex, i.uv);
                fixed transmittance = read.r;
                fixed lightEnergy = read.g;

                float4 cloudColor = float4(_LightColor0.rgb, 0);
                cloudColor = cloudColor * lightEnergy;

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
