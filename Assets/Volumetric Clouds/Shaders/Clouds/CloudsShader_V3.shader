Shader"Custom/CloudsPostProcess_V3"
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

        Pass
        { 
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            #define SHADER_STAGE_FRAGMENT
            #include "./CloudsLib.cginc"
            #undef SHADER_STAGE_FRAGMENT

            fixed4 _OverlayColor;

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

            v2f vert(appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;

                // Calculate the view vector
                float4 clipPos = float4(o.uv * 2.0 - 1.0, 0.0, 1.0);
                float4 viewPos = mul(unity_CameraInvProjection, clipPos);
                viewPos.z = 1.0;
                viewPos.w = 0.0;
                o.viewVector = mul(unity_CameraToWorld, viewPos).xyz;

                return o; 
            }

            UNITY_DECLARE_SCREENSPACE_TEXTURE(_MainTex);
            UNITY_DECLARE_SCREENSPACE_TEXTURE(_CameraDepthTexture); // Unity depth

            // Shape
            float3 _BoundsMin;
            float3 _BoundsMax;
            float3 _CloudsScale;
            float _GlobalDensity;

            sampler3D _ShapeSDF;
            int3 _ShapeSDFSize;

            // Erosion
            sampler3D _Erosion;
            float _ErosionTextureScale;
            float _ErosionWorldScale;
            float3 _ErosionSpeed;
            float _ErosionIntensity;

            // Lighting
            fixed4 _LightColor0;
            float2 _PhaseParams;
            float _SunLightAbsorption;

            float _PowderBrightness;
            float _PowderIntensity;
            bool _UsePowderEffect;

            // Transmittance map
            sampler3D _TransmittanceMap;
            uint3 _TransmittanceMapResolution;
            float3 _TransmittanceMapCoverage;
            float3 _TransmittanceMapOrigin;

            // Sampling
            sampler2D _OffsetNoise;
            float _OffsetNoiseIntensity;
            float _CloudMinStepSize;
            float _LightMinStepSize;
            float _ThresholdSDF;

            // Others
            float _CustomTime;

            // g = 0 causes uniform scattering while g = 1 causes directional scattering, in the direction of the photons
            float henyeyGreenstein(float angle, float g)
            {
 	            float g2 = g * g;
 	            return (1 - g2) / (4 * 3.1415 * pow(1 + g2 - 2 * g * (angle), 1.5));
            }

            // Amplifies brightness when the ray is directed towards the sun
            // https://fr.wikipedia.org/wiki/Fonction_de_phase_de_Henyey-Greenstein
            float phaseHG(float3 rayDir, float3 lightDir)
            {
 	            float angleBottom = dot(rayDir, lightDir);
 	            float angleTop = dot(lightDir, rayDir);
 	            float hgBottom = henyeyGreenstein(angleBottom, _PhaseParams.x);
 	            float hgTop = henyeyGreenstein(angleTop, _PhaseParams.x);
                float hg = hgBottom + hgTop;
 	            return _PhaseParams.y + hg;
            }

            // Not used for now
            float phaseMie(float3 rayDir, float3 lightDir)
            {
                float cosTheta = dot(rayDir, lightDir);
                float mieG = _PhaseParams.x; // Asymmetry parameter
                float phaseMie = (1.0 + mieG*mieG - 2.0*mieG*cosTheta) /
                                pow(1.0 + mieG*mieG - 2.0*mieG*cosTheta, 1.5);
                return phaseMie;
            }

            bool isInTransmittanceMap(float3 pos)
            {
                if (pos.x >= _TransmittanceMapOrigin.x + _TransmittanceMapCoverage.x / 2) return false;
                if (pos.x < _TransmittanceMapOrigin.x - _TransmittanceMapCoverage.x / 2) return false;
                
                if (pos.y >= _TransmittanceMapOrigin.y + _TransmittanceMapCoverage.y / 2) return false;
                if (pos.y < _TransmittanceMapOrigin.y - _TransmittanceMapCoverage.y / 2) return false;
                
                if (pos.z >= _TransmittanceMapOrigin.z + _TransmittanceMapCoverage.z / 2) return false;
                if (pos.z < _TransmittanceMapOrigin.z - _TransmittanceMapCoverage.z / 2) return false;
                return true;
            }

            float sampleTransmittanceMap(float3 pos)
            {
                float3 uvw = (pos - _TransmittanceMapOrigin) / _TransmittanceMapCoverage + float3(0.5, 0.5, 0.5);
                return tex3D(_TransmittanceMap, uvw).r;
            }

            struct cloudMarchResult
            {
                float transmittance;
                float lightEnergy;
                int complexity;
            };
      
            cloudMarchResult cloudMarch(float3 rayPos, float3 rayDir, s_lightParams lightParams, s_rayMarchParams rayParams, s_cloudParams cloudParams, s_erosionParams erosionParams, float dstToBox, float dstInsideBox, float depthDist = 1e10, float offset = 0)
            {
                float dstTravelled = dstToBox + offset;
                float3 currentPos = rayPos + rayDir * dstTravelled;
                float sdfValue = 0.1;
 	            float phaseVal = phaseHG(rayDir, lightParams.lightDir);

                int steps = 0;
                float accumulatedDensity = 0.0;
                float lightEnergy = 0.0;

                bool insideCloud = false;
                float stepSize = _CloudMinStepSize;

                int hardLoopLimit = 200;

                cloudMarchResult res;
                res.transmittance = 1;
                res.lightEnergy = 0;
                
                float density;
                float lightTransmittance;

                [loop]
                while (dstTravelled - dstToBox < dstInsideBox && dstTravelled < depthDist && steps < hardLoopLimit)
                {
                    if (res.transmittance < 0.05)
                    {
                        break;
                    }

                    sdfValue = sampleSDF(currentPos, rayParams, cloudParams);
                    if (sdfValue <= rayParams.sdfThreshold) // Inside the cloud
                    {
                        if (!insideCloud)
                        {
                            insideCloud = true;
                        }
                    }
                    else // Outside the cloud
                    {
                        if (insideCloud)
                        {
                            insideCloud = false;
                        }
                    }

                    // SDF-based step size with a minimum
                    stepSize = sdfValue; 
                    
                    // Negative sdf value means we are inside the volume
                    if (stepSize < 0)
                    {
                        stepSize = 1;
                    }
                    stepSize = max(stepSize, _CloudMinStepSize);
                    stepSize = min(stepSize, dstInsideBox - (dstTravelled - dstToBox));

                    if (insideCloud)
                    {
                        density = sampleDensity(currentPos, cloudParams, erosionParams, rayParams, _CustomTime) * stepSize;
                        accumulatedDensity += density;

                        float lightTransmittance;
                        if (isInTransmittanceMap(currentPos))
                        {
                            lightTransmittance = sampleTransmittanceMap(currentPos);
                        }
                        else
                        {
                            lightTransmittance = 0;
                        }

                        res.lightEnergy += density * res.transmittance * lightTransmittance;
                        res.transmittance *= beer(density);
                    }

                    dstTravelled += stepSize;
                    currentPos += rayDir * stepSize;
                    steps++;
                }
                res.complexity = steps;
                res.lightEnergy *= phaseVal;
                return res;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // Create ray 
                float3 rayPos = _WorldSpaceCameraPos;
                float viewLength = length(i.viewVector);
                float3 rayDir = i.viewVector.xyz / viewLength;
                float3 lightDir = normalize(_WorldSpaceLightPos0);

                // Compute depth
                float depthDist = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CameraDepthTexture, i.uv);
                depthDist = LinearEyeDepth(depthDist) * viewLength;
    
                // Sample background
                float4 backgroundColor = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv);

                // Calculate dist inside box
                float2 rayBoxInfo = rayBoxDst(rayPos, rayDir, _BoundsMin, _BoundsMax);
                float dstToBox = rayBoxInfo.x;
                float dstInsideBox = rayBoxInfo.y;

                // Early exit when the container is not occluded
                if (dstInsideBox == 0 || depthDist < dstToBox)
                {
                    return backgroundColor;
                }

                // Initialize structures
                s_lightParams lightParams;
                lightParams.lightDir = lightDir;
                lightParams.sunLightAbsorption = _SunLightAbsorption;
                lightParams.usePowderEffect = _UsePowderEffect;
                lightParams.powderIntensity = _PowderIntensity;
                lightParams.powderBrightness = _PowderBrightness;

                s_rayMarchParams rayParams;
                rayParams.SDF = _ShapeSDF;
                rayParams.sdfSize = _ShapeSDFSize;
                rayParams.sdfThreshold = _ThresholdSDF;
                rayParams.minStepSize = _CloudMinStepSize;
                rayParams.offset = length(tex2D(_OffsetNoise, i.uv).rgb) / 3.0 * _OffsetNoiseIntensity;

                s_cloudParams cloudParams;
                cloudParams.boundsMin = _BoundsMin;
                cloudParams.boundsMax = _BoundsMax;
                cloudParams.cloudsScale = _CloudsScale;
                cloudParams.globalDensity = _GlobalDensity;

                s_erosionParams erosionParams;
                erosionParams.erosion = _Erosion;
                erosionParams.intensity = _ErosionIntensity;
                erosionParams.worldScale = _ErosionWorldScale;
                erosionParams.textureScale = _ErosionTextureScale;
                erosionParams.speed = _ErosionSpeed;
                erosionParams.erode = true; // Assuming erosion is always enabled;

                // Perform cloud marching
                cloudMarchResult res = cloudMarch(rayPos, rayDir, lightParams, rayParams, cloudParams, erosionParams, dstToBox, dstInsideBox, depthDist, rayParams.offset);
                
                float4 cloudColor = float4(_LightColor0.rgb, 0);
                cloudColor = cloudColor * res.lightEnergy;
                return float4(backgroundColor.rgb * res.transmittance + cloudColor.rgb, 1);
            }

            ENDCG 
        }
    }
}
