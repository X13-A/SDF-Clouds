#ifndef CLOUDS_LIB_INCLUDED
#define CLOUDS_LIB_INCLUDED

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
    //float3 speed = (1, 0, 0.5) * time * 10.0;
    float3 uvw = (pos + cloudParams.cloudsScale / 2.0) / cloudParams.cloudsScale;

#if defined(SHADER_STAGE_COMPUTE)
    float sdfValue = rayParams.SDF.SampleLevel(rayParams.sampler_SDF, uvw, 0).r;
#elif defined(SHADER_STAGE_FRAGMENT)
    float sdfValue = tex3D(rayParams.SDF, uvw).r;
#endif

    float3 res = float3(1, 1, 1) * sdfValue / float3(rayParams.sdfSize);
    res *= cloudParams.cloudsScale;
    return min3(res); 
}

float sampleFog(float3 pos, float YfadeStart, float YfadeEnd)
{
    float t = 1 - clamp(max((pos.y - YfadeStart) / (YfadeEnd - YfadeStart), 0), 0, 1);
    return t;
}

float sampleDensity(float3 pos, s_cloudParams cloudParams, s_erosionParams erosionParams, s_rayMarchParams rayParams, float time)
{
    float fadeTop = saturate(distance(pos.y, cloudParams.boundsMax.y) / 250);
    float fadeBottom = saturate(distance(pos.y, cloudParams.boundsMin.y) / 500);
    float fade = min(fadeTop, fadeBottom);

    if (erosionParams.erode)
    {
        float erosion = sampleErosion(pos, erosionParams, time, cloudParams.globalDensity);
        
        // Normalize erosion to [-1, 1] interval
        erosion *= 2;
        erosion -= 1;

        // Apply erosion
        float3 erodedPos = pos + erosion * erosionParams.worldScale * fade;
        return cloudParams.globalDensity * fade * (sampleSDF(erodedPos, rayParams, cloudParams, time) < rayParams.sdfThreshold);
    }
    else
    {
        return cloudParams.globalDensity * fade * (sampleSDF(pos, rayParams, cloudParams, time) < rayParams.sdfThreshold);
    }
}

s_lightMarchResult lightMarch_sdf(float3 samplePos, s_rayMarchParams rayParams, s_cloudParams cloudParams, s_erosionParams erosionParams, s_lightParams lightParams, float time)
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
    while (dstTravelled < dstInsideBox && steps < hardLoopLimit)
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

        // SDF-based step size with a minimum
        stepSize = abs(sdfValue); // Ensure step size is positive
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

// Light march without using an SDF
// Function not finished yet, because it has no use

//s_lightMarchResult lightMarch(float3 samplePos, s_rayMarchParams rayParams, s_cloudParams cloudParams, s_erosionParams erosionParams, s_lightParams lightParams, float time)
//{
//    float dstInsideBox = rayBoxDst(samplePos, lightParams.lightDir, cloudParams.boundsMin, cloudParams.boundsMax).y;
//    float dstTravelled = rayParams.offset / 8.0;
//    float3 currentPos = samplePos + lightParams.lightDir * dstTravelled;

//    int steps = 0;
//    float transmittance = 1;

//    bool insideCloud = false;
//    float density;

//    int hardLoopLimit = 100;
//    float stepSize;

//    [loop]
//    while (dstTravelled < dstInsideBox && steps < hardLoopLimit)
//    {
//        if (transmittance < 0.02)
//        {
//            break;
//        }

//        stepSize = min(stepSize, dstInsideBox - dstTravelled);

//        dstTravelled += stepSize;
//        currentPos += lightParams.lightDir * stepSize;

//        density = sampleDensity(currentPos, cloudParams, erosionParams, rayParams, time);
//        if (lightParams.usePowderEffect)
//        {
//            transmittance *= beerPowder(density, lightParams);
//        }
//        else
//        {
//            transmittance *= beer(density);
//        }
//        steps++;
//    } 

//    s_lightMarchResult res;
//    res.transmittance = transmittance;
//    res.complexity = steps;
//    return res;
//}


#if defined(SHADER_STAGE_FRAGMENT)
float sampleTransmittanceMap(float3 pos, float3 mapOrigin, float3 mapCoverage, sampler3D map)
{
    float3 uvw = (pos - mapOrigin) / mapCoverage + float3(0.5, 0.5, 0.5);
    return tex3D(map, uvw).r;
}
#endif

#if defined(SHADER_STAGE_COMPUTE) 
float sampleTransmittanceMap(float3 pos, float3 mapOrigin, float3 mapCoverage, Texture3D<float> map, SamplerState sampler_map)
{
    float3 uvw = (pos - mapOrigin) / mapCoverage + float3(0.5, 0.5, 0.5);
    return map.SampleLevel(sampler_map, uvw, 0).r;
}
#endif

#if defined(SHADER_STAGE_FRAGMENT)
float getCloudShadowing(float3 pos, float3 lightDir, float3 containerBoundsMin, float3 containerBoundsMax, sampler3D transmittanceMap, float3 transmittanceMapOrigin, float3 transmittanceMapCoverage)
#elif defined(SHADER_STAGE_COMPUTE)
float getCloudShadowing(float3 pos, float3 lightDir, float3 containerBoundsMin, float3 containerBoundsMax, Texture3D<float> transmittanceMap, SamplerState sampler_TransmittanceMap, float3 transmittanceMapOrigin, float3 transmittanceMapCoverage)
#endif
{
    // Shoot ray towards light
    rayBoxInfo rayBoxRes = rayBoxDst(pos, -lightDir, containerBoundsMin, containerBoundsMax);

    if (rayBoxRes.dstInsideBox <= 0) return 1;
    
    // Project ray onto the transmittanceMap, if underneath
    float3 samplePos = pos + (rayBoxRes.dstToBox + 0.1) * (-lightDir);

    // Check if inside the map
    //if (!isInBox_size(samplePos, transmittanceMapOrigin, transmittanceMapCoverage)) return 1;
    
    // Sample the map and get the shadowing
    #if defined(SHADER_STAGE_FRAGMENT)
    return sampleTransmittanceMap(samplePos, transmittanceMapOrigin, transmittanceMapCoverage, transmittanceMap);
    #elif defined (SHADER_STAGE_COMPUTE)
    return sampleTransmittanceMap(samplePos, transmittanceMapOrigin, transmittanceMapCoverage, transmittanceMap, sampler_TransmittanceMap);
    #endif
}
 
#endif // CLOUDS_LIB_INCLUDED