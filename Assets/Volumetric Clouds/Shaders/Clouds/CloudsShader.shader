Shader"Custom/Volumetrics/Clouds"
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
            float3 _CloudsBoundsMin;
            float3 _CloudsBoundsMax;
            float3 _CloudsScale;

            float3 _FogBoundsMin;
            float3 _FogBoundsMax;

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
            float _ShadowsIntensity;

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
            float _CloudMaxSteps;
            float _LightMinStepSize;
            float _ThresholdSDF;
            float _RenderDistance;

            // Fog
            float _FogDensity;
            float _FogDistance;
            float _FogStepSize;

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
                  
            cloudMarchResult cloudMarch(float3 rayPos, float3 rayDir, s_lightParams lightParams, s_rayMarchParams rayParams, s_cloudParams cloudParams, s_erosionParams erosionParams, float dstToBox, float dstInsideBox, float depthDist = 1e10, float offset = 0)
            {
                float dstTravelled = dstToBox + offset;
                float3 currentPos = rayPos + rayDir * dstTravelled;
                float sdfValue = 0.1;
 	            float phaseVal = phaseHG(rayDir, -lightParams.lightDir);

                int steps = 0;
                float lightEnergy = 0.0;

                bool insideCloud = false;
                float stepSize = _CloudMinStepSize;

                int hardLoopLimit = 200;

                cloudMarchResult res;
                res.transmittance = 1;
                res.lightEnergy = 0;
                
                float density;
                float lightTransmittance;

                //if(dstToBox > 0 && dstInsideBox > 0)
                //{
                //    cloudMarchResult fogRes = getFog(rayPos, rayDir, min(dstToBox, depthDist), lightParams.lightDir, res.transmittance, res.lightEnergy, offset);
                //    res.transmittance = fogRes.transmittance;
                //    res.lightEnergy = fogRes.lightEnergy;
                //}

                [loop]
                while (dstTravelled - dstToBox < dstInsideBox && dstTravelled < depthDist && steps < hardLoopLimit)
                {
                    if (res.transmittance < 0.01)
                    {
                        break;
                    }

                    sdfValue = sampleSDF(currentPos, rayParams, cloudParams, _CustomTime);
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

                    // HACK : Makes better erosion
                    // TODO : Find more optimized way
                    insideCloud = true;

                    // SDF-based step size with a minimum
                    stepSize = sdfValue; 
                    
                    // Negative sdf value means we are inside the volume
                    if (stepSize < 0)
                    {
                        stepSize = 1;
                    }
                    stepSize = max(stepSize, _CloudMinStepSize);
                    stepSize = min(stepSize, dstInsideBox - (dstTravelled - dstToBox));

                    // Sample transmittance at current pos
                    float lightTransmittance;
                    if (isInBox_size(currentPos, _TransmittanceMapOrigin, _TransmittanceMapCoverage))
                    {
                        lightTransmittance = sampleTransmittanceMap(currentPos, _TransmittanceMapOrigin, _TransmittanceMapCoverage, _TransmittanceMap);
                    }
                    else
                    {
                        lightTransmittance = 1;
                    }

                    // Add density when inside cloud
                    if (insideCloud)
                    {
                        density = sampleDensity(currentPos, cloudParams, erosionParams, rayParams, _CustomTime) * stepSize;
                        res.lightEnergy += density * res.transmittance * lightTransmittance;
                        res.transmittance *= beer(density);
                    }

                    // Add fog, more optimized to do it here rather than in the specialized function
                    float fogDensity = stepSize * pow(dstTravelled, 2);
                    fogDensity *= pow(_FogDensity / 100000, 2);
                    
                    res.lightEnergy += fogDensity * res.transmittance * lightTransmittance;
                    res.transmittance *= beer(fogDensity);

                    dstTravelled += stepSize;
                    currentPos += rayDir * stepSize;
                    steps++;
                }

                //// Apply fog when coming out of the container as well
                //if(dstTravelled < depthDist)
                //{
                //    float3 outPos = rayPos;
                //    float remainingDepth = depthDist;
                    
                //    if (dstInsideBox > 0)
                //    {
                //        outPos = depthDist - (dstToBox + dstInsideBox);
                //        remainingDepth = rayPos * (dstToBox + dstInsideBox);
                //    }

                //    cloudMarchResult fogRes = getFog(outPos, rayDir, remainingDepth, lightParams.lightDir, res.transmittance, res.lightEnergy, -offset);
                //    res.transmittance = fogRes.transmittance;
                //    res.lightEnergy = fogRes.lightEnergy;
                //}

                res.complexity = steps;
                res.lightEnergy *= phaseVal;
                return res;
            }

            cloudMarchResult cloudMarch_v2(float3 rayPos, float3 rayDir, s_lightParams lightParams, s_rayMarchParams rayParams, s_cloudParams cloudParams, s_erosionParams erosionParams, rayBoxInfo cloudsBoxInfo, rayBoxInfo fogBoxInfo, float depthDist = 1e10, float offset = 0)
            {
                // Assume fog box is bigger than cloud box
                float dstTravelled = fogBoxInfo.dstToBox + offset;

                // TODO: Skip steps before fog start
                //if (_FogDistance < cloudsBoxInfo.dstToBox - dstTravelled) dstTravelled += _FogDistance;
                //else if (cloudsBoxInfo.dstInsideBox <= 0 && fogBoxInfo.dstInsideBox < _FogDistance) dstTravelled += _FogDistance;
                
                cloudMarchResult res;
                res.transmittance = 1;
                res.lightEnergy = 0;

                //if (dstInsideBox <= 0 || _FogDistance < dstToBox) dstTravelled += _FogDistance;
                //if (dstInsideBox <= 0 && depthDist >= 1e10)
                float3 currentPos = rayPos + rayDir * dstTravelled;

                float sdfValue = 0.1;
 	            float phaseVal = phaseHG(normalize(rayDir), normalize(-lightParams.lightDir)); // TODO: fix phaseHG

                int steps = 0;
                float lightEnergy = 0.0;

                bool insideCloud = false;
                float stepSize = _CloudMinStepSize;

                float density = 0;
                float lightTransmittance;

                // Problem: Algorithm always does maximum steps
                // Optimization 1:(OK) define a fog bounding box and use it to start / stop ray marching
                // Optimization 2: skip useless steps until fog appears
                // Optimization 3: increase step size with distance ?
                [loop]
                while (dstTravelled < depthDist && dstTravelled < _RenderDistance && dstTravelled < (fogBoxInfo.dstToBox + fogBoxInfo.dstInsideBox) && steps < _CloudMaxSteps && res.transmittance >= 0.01)
                {
                    bool is_inside_clouds_box = isInBox_bounds(currentPos, _CloudsBoundsMin, _CloudsBoundsMax);
                    density = 0;

                    // Add fog
                    float fogDensity = sampleFog(currentPos, _CloudsBoundsMin.y, _CloudsBoundsMax.y - _CloudsBoundsMin.y / 2);
                    fogDensity *= stepSize * pow(max(dstTravelled - _FogDistance, 0), 2);
                    fogDensity *= pow(_FogDensity / 100000, 2);
                    density += fogDensity;

                    // Sample transmittance at current pos
                    float lightTransmittance;
                    if (isInBox_size(currentPos, _TransmittanceMapOrigin, _TransmittanceMapCoverage))
                    {
                        lightTransmittance = sampleTransmittanceMap(currentPos, _TransmittanceMapOrigin, _TransmittanceMapCoverage, _TransmittanceMap);
                    }
                    else
                    {
                        // Project point on box and check value
                        lightTransmittance = getCloudShadowing(currentPos, lightParams.lightDir, _CloudsBoundsMin, _CloudsBoundsMax, _TransmittanceMap, _TransmittanceMapOrigin, _TransmittanceMapCoverage);
                    }

                    // Use SDF and sample clouds when inside volume
                    if (is_inside_clouds_box)
                    {
                        sdfValue = sampleSDF(currentPos, rayParams, cloudParams, _CustomTime);

                        // Check if inside cloud (or near erosion)
                        insideCloud = sdfValue <= rayParams.sdfThreshold + _ErosionWorldScale;

                        // SDF-based step size with a minimum
                        stepSize = sdfValue; 

                        // Negative sdf value means we are inside the volume
                        stepSize = max(abs(stepSize), _CloudMinStepSize);

                        // Add density when inside cloud
                        if (insideCloud)
                        {
                            // Only erode edges to improve performance
                            erosionParams.erode = erosionParams.erode && abs(sdfValue) <= _ErosionWorldScale * 2;
                            density += sampleDensity(currentPos, cloudParams, erosionParams, rayParams, _CustomTime) * stepSize;
                        }
                    }
                    else
                    {
                        // Set fixed step size outside SDF
                        stepSize = _FogStepSize;

                        // Clamp stepsize when entering box
                        float dstRemaining = max(cloudsBoxInfo.dstToBox - dstTravelled, 0);
                        if (cloudsBoxInfo.dstInsideBox > 0 && stepSize > dstRemaining) stepSize = min(dstRemaining + offset, _CloudMinStepSize);
                    }
                    
                    // Apply light and density
                    res.lightEnergy += density * res.transmittance * lightTransmittance;
                    res.transmittance *= beer(density);
        
                    // Advance ray
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
                float3 lightDir = normalize(-_WorldSpaceLightPos0);

                // Compute depth
                float depthDist = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CameraDepthTexture, i.uv);
                depthDist = LinearEyeDepth(depthDist) * viewLength;
    
                // Sample background
                float4 backgroundColor = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv);

                // Calculate dist inside boxes
                rayBoxInfo cloudsBoxInfo = rayBoxDst(rayPos, rayDir, _CloudsBoundsMin, _CloudsBoundsMax);
                rayBoxInfo fogBoxInfo = rayBoxDst(rayPos, rayDir, _FogBoundsMin, _FogBoundsMax);

                // Compute shadows
                float3 worldPos = rayPos + rayDir * depthDist;
                float shadowing = getCloudShadowing(worldPos, lightDir, _CloudsBoundsMin, _CloudsBoundsMax, _TransmittanceMap, _TransmittanceMapOrigin, _TransmittanceMapCoverage);
                shadowing = clamp(shadowing, 0, 0.25);
                shadowing /= 100 - clamp(_ShadowsIntensity, 0, 100);

                // Early exit when the container is not visible
                if (fogBoxInfo.dstInsideBox == 0 || depthDist < fogBoxInfo.dstToBox)
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
                cloudParams.boundsMin = _CloudsBoundsMin;
                cloudParams.boundsMax = _CloudsBoundsMax;
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
                cloudMarchResult res = cloudMarch_v2(rayPos, rayDir, lightParams, rayParams, cloudParams, erosionParams, cloudsBoxInfo, fogBoxInfo, depthDist, rayParams.offset);
                
                float4 cloudColor = float4(_LightColor0.rgb, 0);
                fixed4 shadowColor = fixed4(0, 0.025, 0.2, 0.025);
                //shadowColor *= 0;
                //cloudColor = lerp(cloudColor, shadowColor, 1 - res.lightEnergy);
                cloudColor = cloudColor * res.lightEnergy;
                return float4(backgroundColor.rgb * res.transmittance - shadowing + cloudColor.rgb * (1 - res.transmittance), 1);
            }

            ENDCG 
        }
    }
}
