Shader"Custom/Volumetrics/CloudsV4"
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

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _LightColor0;
            sampler2D _LightTransmittanceTex;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 backgroundColor = tex2D(_MainTex, i.uv);
                fixed2 read = tex2D(_LightTransmittanceTex, i.uv);
                fixed transmittance = read.r;
                fixed lightEnergy = read.g;

                float4 cloudColor = float4(_LightColor0.rgb, 0);
                cloudColor = cloudColor * lightEnergy;
                return float4(backgroundColor.rgb * transmittance + cloudColor.rgb * (1 - transmittance), 1);
            }
            ENDCG
        }
    }
}
