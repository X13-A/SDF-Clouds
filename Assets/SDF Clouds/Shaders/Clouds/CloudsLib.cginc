#ifndef CLOUDS_LIB_INCLUDED
#define CLOUDS_LIB_INCLUDED

#define WIND_SPEED 0.0 // NOT SURE IF THIS WORKS

struct s_lightMarchResult
{
    float transmittance;
    int complexity;
};

struct s_lightParams
{
    float3 lightDir;
    float sunLightAbsorption;
    bool usePowderEffect;
    float powderIntensity;
    float powderBrightness;
};

struct s_rayMarchParams
{
#if defined(SHADER_STAGE_COMPUTE)
    SamplerState sampler_SDF;
    Texture3D<float> SDF;
#elif defined(SHADER_STAGE_FRAGMENT)
    sampler3D SDF;
#endif
    int3 sdfSize;
    float sdfThreshold;
    float minStepSize;
    float offset;
};

struct s_erosionParams
{
#if defined(SHADER_STAGE_COMPUTE)
    Texture3D<float> erosion;
    SamplerState sampler_erosion;
#elif defined(SHADER_STAGE_FRAGMENT)
    sampler3D erosion;
#endif
    float intensity;
    float worldScale;
    float textureScale;
    float3 speed;
    bool erode;
};

struct s_cloudParams
{
    float sunLightAbsorption;
    float globalDensity;
    float3 boundsMin;
    float3 boundsMax;
    float3 cloudsScale;
};

struct cloudMarchResult
{
    float transmittance;
    float lightEnergy;
    int complexity;
};

// Remaps a value to the [0, 1] range
float remap01(float v, float min, float max)
{
    return saturate((v - min) / (max - min));
}

float min3(float3 vec)
{
    return min(min(vec.x, vec.y), vec.z);
}

float powder(float d, s_lightParams lightParams)
{
    float powder = -exp(-d * lightParams.powderIntensity) + lightParams.powderBrightness;
    return powder;
}

// Beer's law, used for transmittance calculations
float beer(float d) 
{
    return exp(-d);
}

// Combines beer and powder effect
float beerPowder(float d, s_lightParams lightParams) 
{
    float powderVal = saturate(powder(d, lightParams));
    float beerVal = saturate(beer(d));
    return (beerVal * powderVal);
}

struct rayBoxInfo
{
    float dstToBox;
    float dstInsideBox;
};

// Computes distance to box and distance inside the box
rayBoxInfo rayBoxDst(float3 rayOrigin, float3 rayDir, float3 boundsMin, float3 boundsMax)
{
    float3 invRayDir = 1 / rayDir;

    // Calculate ray intersections with box
    float3 t0 = (boundsMin - rayOrigin) * invRayDir;
    float3 t1 = (boundsMax - rayOrigin) * invRayDir;
    float3 tmin = min(t0, t1);
    float3 tmax = max(t0, t1);

    // Calculate distances
    float dstA = max(max(tmin.x, tmin.y), tmin.z); // A is the closest point
    float dstB = min(tmax.x, min(tmax.y, tmax.z)); // B is the furthest point

    rayBoxInfo res;
    res.dstToBox = max(0, dstA);
    res.dstInsideBox = max(0, dstB - res.dstToBox);
    return res;
}

// Rather or not the point is inside the container, based on the bounds
bool isInBox_bounds(float3 pos, float3 boundsMin, float3 boundsMax, bool check_x = true, bool check_y = true, bool check_z = true)
{
    bool x = true;
    bool y = true;
    bool z = true;
    if (check_x) x = pos.x > boundsMin.x && pos.x < boundsMax.x;
    if (check_y) y = pos.y > boundsMin.y && pos.y < boundsMax.y;
    if (check_z) z = pos.z > boundsMin.z && pos.z < boundsMax.z;

    return x && y && z;
}

// Rather or not the point is inside the container, based on the origin and size
bool isInBox_size(float3 pos, float3 boxOrigin, float3 boxSize)
{
    if (pos.x >= boxOrigin.x + boxSize.x / 2) return false;
    if (pos.x < boxOrigin.x - boxSize.x / 2) return false;
                
    if (pos.y >= boxOrigin.y + boxSize.y / 2) return false;
    if (pos.y < boxOrigin.y - boxSize.y / 2) return false;
                
    if (pos.z >= boxOrigin.z + boxSize.z / 2) return false;
    if (pos.z < boxOrigin.z - boxSize.z / 2) return false;
    return true;
}

float sampleErosion(float3 pos, s_erosionParams erosionParams, float time, float density)
{
    // Sample erosion texture
    float3 uvw = pos / erosionParams.textureScale + erosionParams.speed * time / 1000.0;

#if defined(SHADER_STAGE_COMPUTE)
    float erosion = erosionParams.erosion.SampleLevel(erosionParams.sampler_erosion, uvw, 0).r;
#elif defined(SHADER_STAGE_FRAGMENT)
    float erosion = tex3D(erosionParams.erosion, uvw).r;
#endif

    // Threshold erosion value
    erosion = saturate(erosion - (1.0 - erosionParams.intensity));

    // Apply on low densities only
    erosion *= saturate(1.0 - density);
    return 1.0 - saturate(erosion);
}

// Samples the SDF
float sampleSDF(float3 pos, s_rayMarchParams rayParams, s_cloudParams cloudParams, float time)
{
    // Compute the UVW coordinate for sampling
    float3 uvw = (pos - cloudParams.boundsMin) / cloudParams.cloudsScale;

    // Ensure X and Z axes are centered relative to cloudsScale
    uvw.x = (pos.x + cloudParams.cloudsScale.x / 2.0) / cloudParams.cloudsScale.x;
    uvw.z = (pos.z + cloudParams.cloudsScale.z / 2.0) / cloudParams.cloudsScale.z;
    uvw.xz += time * WIND_SPEED;
    // Sample the SDF based on the shader stage
#if defined(SHADER_STAGE_COMPUTE)
    float sdfValue = rayParams.SDF.SampleLevel(rayParams.sampler_SDF, uvw, 0).r;
#elif defined(SHADER_STAGE_FRAGMENT)
    float sdfValue = tex3D(rayParams.SDF, uvw).r;
#endif

    // Compute the scaled result
    float3 res = float3(1, 1, 1) * sdfValue / float3(rayParams.sdfSize);
    res *= cloudParams.cloudsScale;
    return min3(res);
}



//float sampleFog(float3 pos, float YfadeStart, float YfadeEnd)
//{
//    float t = 1 - clamp(max((pos.y - YfadeStart) / (YfadeEnd - YfadeStart), 0), 0, 1);
//    return t;
//}

float sampleDensity(float3 pos, s_cloudParams cloudParams, s_erosionParams erosionParams, s_rayMarchParams rayParams, float time)
{
    float sdfValue = sampleSDF(pos, rayParams, cloudParams, time);
    if (sdfValue > rayParams.sdfThreshold) return 0;
    
    float dstInsideCloud = abs(sdfValue) - rayParams.sdfThreshold;

    float cloudDensity = min(dstInsideCloud / 500.0, 1) * cloudParams.globalDensity;
    
    if (erosionParams.erode)
    {

        float lowDetail = cloudDensity;
        float sampledErosion = sampleErosion(pos, erosionParams, time, cloudParams.globalDensity);

        float highDetail = cloudParams.globalDensity;
        if (sampledErosion < 0.5) highDetail = 0;

        // Compute inverse shape density
        float oneMinusShape = 1.0 - saturate(dstInsideCloud / erosionParams.worldScale);

        // Apply non-linear erosion weight
        float detailErodeWeight = pow(oneMinusShape, 16);

        // Subtract weighted detail noise from base shape density
        float finalDetail = highDetail * saturate(detailErodeWeight);

        float finalDensity = saturate(lowDetail - finalDetail);

        return finalDensity;
    }
    else
    {
        return cloudParams.globalDensity * (sdfValue < rayParams.sdfThreshold);
    }
}

s_lightMarchResult lightMarch_sdf(float3 samplePos, s_rayMarchParams rayParams, s_cloudParams cloudParams, s_erosionParams erosionParams, s_lightParams lightParams, float time, float dstToLight = 1e10)
{
    float dstInsideBox = rayBoxDst(samplePos, lightParams.lightDir, cloudParams.boundsMin, cloudParams.boundsMax).dstInsideBox;
    float dstTravelled = rayParams.offset / 8.0;
    float3 currentPos = samplePos + lightParams.lightDir * dstTravelled;
    float sdfValue = 0.1;

    int steps = 0;
    s_lightMarchResult res;
    res.transmittance = 1;
    res.complexity = 0;

    bool insideCloud = false;
    float stepSize = rayParams.minStepSize;
    float density;

    int hardLoopLimit = 1000;

    [loop]
    while (dstTravelled < dstInsideBox && steps < hardLoopLimit && dstTravelled < dstToLight)
    {
        if (res.transmittance < 0.02)
        {
            break;
        }

        sdfValue = sampleSDF(currentPos, rayParams, cloudParams, time);

        if (sdfValue <= rayParams.sdfThreshold) // Inside the cloud
        {
            insideCloud = true;
        }
        else // Outside the cloud
        {
            insideCloud = false;
        }

        // Uncomment to use sdf inside clouds as well (lighting only)
        // Faster, but produces slightly less precise results
        //stepSize = abs(sdfValue);

        // SDF-based step size with a minimum
        stepSize = max(stepSize, rayParams.minStepSize);
        stepSize = min(stepSize, dstInsideBox - dstTravelled);
        dstTravelled += stepSize;
        currentPos += lightParams.lightDir * stepSize;

        if (insideCloud)
        {
            density = sampleDensity(currentPos, cloudParams, erosionParams, rayParams, time);
            density *= stepSize * lightParams.sunLightAbsorption;

            if (lightParams.usePowderEffect)
            {
                res.transmittance *= beerPowder(density, lightParams);
            }
            else
            {
                res.transmittance *= beer(density);
            }
        }
        steps++;
    }

    res.complexity = steps;
    return res;
}

#if defined(SHADER_STAGE_FRAGMENT)
float sampleTransmittanceMap(float3 pos, float3 mapOrigin, float3 mapCoverage, sampler3D map, float time = 0)
{
    float3 uvw = (pos - mapOrigin) / mapCoverage + float3(0.5, 0.5, 0.5);
    uvw.x += time * WIND_SPEED;
    uvw.z += time * WIND_SPEED;
    return tex3D(map, uvw).r;
}
#endif

#if defined(SHADER_STAGE_COMPUTE) 
float sampleTransmittanceMap(float3 pos, float3 mapOrigin, float3 mapCoverage, Texture3D<float> map, SamplerState sampler_map, float time = 0)
{
    float3 uvw = (pos - mapOrigin) / mapCoverage + float3(0.5, 0.5, 0.5);
    uvw.x += time * WIND_SPEED;
    uvw.z += time * WIND_SPEED;
    return map.SampleLevel(sampler_map, uvw, 0).r;
}
#endif

struct cloudShadowingResult
{
    float shadowing;
    float totalFogDist;
    float dstInsideClouds;
    float dstToClouds;
};

#if defined(SHADER_STAGE_FRAGMENT)
cloudShadowingResult getCloudShadowing(float3 pos, float3 lightDir, float3 containerBoundsMin, float3 containerBoundsMax, sampler3D transmittanceMap, float3 transmittanceMapOrigin, float3 transmittanceMapCoverage, bool softShadows = false, float time = 0)
#elif defined(SHADER_STAGE_COMPUTE)
cloudShadowingResult getCloudShadowing(float3 pos, float3 lightDir, float3 containerBoundsMin, float3 containerBoundsMax, Texture3D<float> transmittanceMap, SamplerState sampler_TransmittanceMap, float3 transmittanceMapOrigin, float3 transmittanceMapCoverage, bool softShadows = false, float time = 0)
#endif
{
    cloudShadowingResult res;
    res.shadowing = 1.0;
    res.totalFogDist = 0.0;

    // Shoot ray towards light
    rayBoxInfo rayBoxRes = rayBoxDst(pos, -lightDir, containerBoundsMin, containerBoundsMax);
    res.dstInsideClouds = rayBoxRes.dstInsideBox;
    res.dstToClouds = rayBoxRes.dstToBox;
    res.totalFogDist = rayBoxRes.dstToBox + rayBoxRes.dstInsideBox;

    if (rayBoxRes.dstInsideBox <= 0) return res;

    float3 samplePos = pos + (rayBoxRes.dstToBox + 0.1) * (-lightDir);
    if (softShadows)
    {
        float3 right = float3(1, 0, 0);
        float3 up = float3(0, 0, 1);

        // Define sampling offsets
        const float offsetScale = 100; // Small horizontal step (adjustable)
        float kernel[9] = { 1, 2, 1, 2, 4, 2, 1, 2, 1 }; // Gaussian-like weighting
        float totalWeight = 16.0; // Sum of weights

        float shadowSum = 0.0;
        int index = 0;

        for (int y = -1;y <= 1; y++) 
        {
            for (int x = -1;x <= 1; x++) 
            {
                float2 offset = float2(x, y) * offsetScale;
                float3 newSamplePos = samplePos + offset.x * right + offset.y * up;

                // Sample the transmittance map
                float shadowSample;
                #if defined(SHADER_STAGE_FRAGMENT)
                shadowSample = sampleTransmittanceMap(newSamplePos, transmittanceMapOrigin, transmittanceMapCoverage, transmittanceMap, time);
                #elif defined(SHADER_STAGE_COMPUTE)
                shadowSample = sampleTransmittanceMap(newSamplePos, transmittanceMapOrigin, transmittanceMapCoverage, transmittanceMap, sampler_TransmittanceMap, time);
                #endif
            
                shadowSum += shadowSample * kernel[index++];
            }
        }

        // Normalize the shadowing value
        res.shadowing = shadowSum / totalWeight;
    }
    else
    {
        #if defined(SHADER_STAGE_FRAGMENT)
        res.shadowing = sampleTransmittanceMap(samplePos, transmittanceMapOrigin, transmittanceMapCoverage, transmittanceMap, time);
        #elif defined(SHADER_STAGE_COMPUTE)
        res.shadowing = sampleTransmittanceMap(samplePos, transmittanceMapOrigin, transmittanceMapCoverage, transmittanceMap, sampler_TransmittanceMap, time);
        #endif
    }
    res.shadowing = res.shadowing;
    return res;
}


//#if defined(SHADER_STAGE_FRAGMENT)
//cloudShadowingResult getCloudShadowing(float3 pos, float3 lightDir, float3 containerBoundsMin, float3 containerBoundsMax, sampler3D transmittanceMap, float3 transmittanceMapOrigin, float3 transmittanceMapCoverage)
//#elif defined(SHADER_STAGE_COMPUTE)
//cloudShadowingResult getCloudShadowing(float3 pos, float3 lightDir, float3 containerBoundsMin, float3 containerBoundsMax, Texture3D<float> transmittanceMap, SamplerState sampler_TransmittanceMap, float3 transmittanceMapOrigin, float3 transmittanceMapCoverage)
//#endif
//{
//    cloudShadowingResult res;
//    res.shadowing = 1;
//    res.totalFogDist = 0;

//    // Shoot ray towards light
//    rayBoxInfo rayBoxRes = rayBoxDst(pos, -lightDir, containerBoundsMin, containerBoundsMax);
//    res.totalFogDist = rayBoxRes.dstToBox + rayBoxRes.dstInsideBox;

//    if (rayBoxRes.dstInsideBox <= 0) return res;
    
//    // Project ray onto the transmittanceMap, if underneath
//    float3 samplePos = pos + (rayBoxRes.dstToBox + 0.1) * (-lightDir);

//    // Sample the map and get the shadowing
//    #if defined(SHADER_STAGE_FRAGMENT)
//    res.shadowing = sampleTransmittanceMap(samplePos, transmittanceMapOrigin, transmittanceMapCoverage, transmittanceMap);
//    #elif defined (SHADER_STAGE_COMPUTE)
//    res.shadowing = sampleTransmittanceMap(samplePos, transmittanceMapOrigin, transmittanceMapCoverage, transmittanceMap, sampler_TransmittanceMap);
//    #endif
//    return res;
//}
 
#endif // CLOUDS_LIB_INCLUDED