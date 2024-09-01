using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class CloudsPostProcess_V3 : PostProcessBase
{
    [Header("V3 Parameters")]
    [SerializeField] public Texture3D shapeSDF;

    [Header("Erosion parameters")]
    [SerializeField][Range(0, 10000)] public float erosionTextureScale;
    [SerializeField][Range(0, 10000)] public float erosionWorldScale;
    [SerializeField][Range(0, 1)] public float erosionIntensity;
    [SerializeField] public Vector3 erosionSpeed;
    [SerializeField] public Texture3D erosionTexture;
    
    [Header("Shape parameters")]
    [SerializeField] public Vector3 cloudsScale = new Vector3(1, 1, 1);
    [SerializeField][Range(0, 5)] public float globalDensity = 0.25f;

    [Header("Sampling parameters")]
    [SerializeField] private Texture2D offsetNoise;
    [SerializeField] private float offsetNoiseIntensity;
    [SerializeField] private float cloudMinStepSize = 1;
    [SerializeField] private float lightMinStepSize = 1;
    [SerializeField][Range(0.001f, 100f)] public float sdfThreshold = 0.1f;

    [Header("Lighting paramters")]
    [SerializeField][Range(0, 1)] public float sunlightAbsorption = 0.25f;
    [SerializeField] private Vector2 phaseParams = new Vector2(0.8f, 0.7f);

    [Header("Powder effect")]
    [SerializeField] public bool usePowderEffect = true;
    [SerializeField][Range(0, 2)] public float powderBrightness = 1;
    [SerializeField][Range(0, 16)] public float powderIntensity = 2;

    [Header("General parameters")]
    [SerializeField] private Transform container;
    [SerializeField] private Material postProcessMaterial;
    public bool active;

    public Vector3 BoundsMin => container.position - container.localScale / 2;
    public Vector3 BoundsMax => container.position + container.localScale / 2;
    public Vector3 ContainerCenter => container.position;

    public float TotalHeight => BoundsMax.y - BoundsMin.y;

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

        // Shape
        postProcessMaterial.SetVector("_BoundsMin", BoundsMin);
        postProcessMaterial.SetVector("_BoundsMax", BoundsMax);
        postProcessMaterial.SetVector("_CloudsScale", cloudsScale);
        postProcessMaterial.SetFloat("_GlobalDensity", globalDensity);
        
        // Sampling
        postProcessMaterial.SetTexture("_OffsetNoise", offsetNoise);
        postProcessMaterial.SetFloat("_OffsetNoiseIntensity", offsetNoiseIntensity);
        postProcessMaterial.SetFloat("_CloudMinStepSize", Mathf.Max(cloudMinStepSize, 0.1f));
        postProcessMaterial.SetFloat("_LightMinStepSize", Mathf.Max(lightMinStepSize, 0.1f));
        postProcessMaterial.SetFloat("_ThresholdSDF", sdfThreshold);

        // Erosion
        postProcessMaterial.SetTexture("_Erosion", erosionTexture);
        postProcessMaterial.SetFloat("_ErosionIntensity", erosionIntensity);
        postProcessMaterial.SetFloat("_ErosionTextureScale", erosionTextureScale);
        postProcessMaterial.SetFloat("_ErosionWorldScale", erosionWorldScale);
        postProcessMaterial.SetVector("_ErosionSpeed", erosionSpeed);

        // Lighting
        postProcessMaterial.SetFloat("_SunLightAbsorption", sunlightAbsorption);
        postProcessMaterial.SetInt("_UsePowderEffect", usePowderEffect ? 1 : 0);
        postProcessMaterial.SetFloat("_PowderBrightness", powderBrightness);
        postProcessMaterial.SetFloat("_PowderIntensity", powderIntensity);
        postProcessMaterial.SetVector("_PhaseParams", phaseParams);

        // Others
        postProcessMaterial.SetFloat("_CustomTime", Time.time);
        postProcessMaterial.SetTexture("_ShapeSDF", shapeSDF);
        postProcessMaterial.SetVector("_ShapeSDFSize", new Vector3(shapeSDF.width, shapeSDF.height, shapeSDF.depth));
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
