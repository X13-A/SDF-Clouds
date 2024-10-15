using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class FogPostProcess : PostProcessBase
{
    [Header("Sampling parameters")]
    [SerializeField] private Texture2D offsetNoise;
    [SerializeField] private float offsetNoiseIntensity;
    [SerializeField] private float stepSize = 1;

    [Header("Lighting paramters")]
    [SerializeField] private Vector2 phaseParams = new Vector2(0.8f, 0.7f);

    [Header("General parameters")]
    [SerializeField] private Material postProcessMaterial;
    
    public bool active;

    public void SetupTransmittanceMap(RenderTexture mapTexture, Vector3 mapOrigin, Vector3Int mapResolution, Vector3 mapCoverage)
    {
        postProcessMaterial.SetTexture("_TransmittanceMap", mapTexture);
        postProcessMaterial.SetVector("_TransmittanceMapOrigin", mapOrigin);
        postProcessMaterial.SetVector("_TransmittanceMapResolution", new Vector3(mapResolution.x, mapResolution.y, mapResolution.z));
        postProcessMaterial.SetVector("_TransmittanceMapCoverage", mapCoverage);
    }

    public void SetUniforms()
    {
        Camera.main.depthTextureMode = DepthTextureMode.Depth;

        // Sampling
        postProcessMaterial.SetTexture("_OffsetNoise", offsetNoise);
        postProcessMaterial.SetFloat("_OffsetNoiseIntensity", offsetNoiseIntensity);

        // Lighting
        postProcessMaterial.SetVector("_PhaseParams", phaseParams);

        // Others
        postProcessMaterial.SetFloat("_CustomTime", Time.time);
    }

    public override void Apply(RenderTexture source, RenderTexture dest)
    {
        if (active && postProcessMaterial != null && Camera.current != null)
        {
            SetUniforms();
            Graphics.Blit(source, dest, postProcessMaterial);
        }
        else
        {
            Graphics.Blit(source, dest);
        }
    }
}
