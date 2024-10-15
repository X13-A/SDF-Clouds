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
    [SerializeField] private int cloudMaxSteps = 200;
    [SerializeField][Range(0.001f, 100f)] public float sdfThreshold = 0.1f;
    [SerializeField][Range(0.001f, 50000f)] private float renderDistance;

    [Header("Lighting paramters")]
    [SerializeField][Range(0, 1)] public float sunlightAbsorption = 0.25f;
    [SerializeField] private Vector2 phaseParams = new Vector2(0.8f, 0.7f);
    [SerializeField][Range(0.0f, 100f)] public float shadowsIntensity = 1f;

    [Header("Powder effect")]
    [SerializeField] public bool usePowderEffect = true;
    [SerializeField][Range(0, 2)] public float powderBrightness = 1;
    [SerializeField][Range(0, 16)] public float powderIntensity = 2;

    [Header("Fog parameters")]
    [SerializeField][Range(0, 10.0f)] public float fogDensity = 0.01f;
    [SerializeField][Range(0, 100000.0f)] public float fogDistance = 0.01f;
    [SerializeField][Range(0, 1000.0f)] public float fogStepSize= 0.01f;

    [Header("Shadow parameters")]

    [Header("General parameters")]
    [SerializeField] private Transform cloudsContainer;
    [SerializeField] private Transform fogContainer;
    [SerializeField] private Material postProcessMaterial;
    public bool active;

    public Vector3 CloudsBoundsMin => cloudsContainer.position - cloudsContainer.localScale / 2;
    public Vector3 CloudsBoundsMax => cloudsContainer.position + cloudsContainer.localScale / 2;
    public Vector3 CloudsContainerCenter => cloudsContainer.position;

    public Vector3 FogBoundsMin => fogContainer.position - fogContainer.localScale / 2;
    public Vector3 FogBoundsMax => fogContainer.position + fogContainer.localScale / 2;
    public Vector3 FogContainerCenter => fogContainer.position;

    [Header("Compute")]
    [SerializeField] private ComputeShader rayMarchCompute;
    int rayMarchKernel;
    public RenderTexture RayMarchRenderTexture { get; private set; }
    public Texture3D rayMarchViz;

    private void Awake()
    {
        rayMarchKernel = rayMarchCompute.FindKernel("CSMain");
    }

    public void SetupTransmittanceMap(RenderTexture mapTexture, Vector3 mapOrigin, Vector3Int mapResolution, Vector3 mapCoverage)
    {
        postProcessMaterial.SetTexture("_TransmittanceMap", mapTexture);
        postProcessMaterial.SetVector("_TransmittanceMapOrigin", mapOrigin);
        postProcessMaterial.SetVector("_TransmittanceMapResolution", new Vector3(mapResolution.x, mapResolution.y, mapResolution.z));
        postProcessMaterial.SetVector("_TransmittanceMapCoverage", mapCoverage);

        rayMarchCompute.SetTexture(rayMarchKernel, "_TransmittanceMap", mapTexture);
        rayMarchCompute.SetVector("_TransmittanceMapOrigin", mapOrigin);
        rayMarchCompute.SetVector("_TransmittanceMapCoverage", mapCoverage);
    }

    public void SetUniforms_compute()
    {
        Camera.main.depthTextureMode = DepthTextureMode.Depth;

        // View
        rayMarchCompute.SetVector("_CameraPos", Camera.main.transform.position);
        rayMarchCompute.SetMatrix("_InvProjectionMatrix", Camera.main.projectionMatrix.inverse);
        rayMarchCompute.SetMatrix("_InvViewMatrix", Camera.main.worldToCameraMatrix.inverse);

        // Shape
        rayMarchCompute.SetVector("_CloudsBoundsMin", CloudsBoundsMin);
        rayMarchCompute.SetVector("_CloudsBoundsMax", CloudsBoundsMax);

        rayMarchCompute.SetVector("_FogBoundsMin", FogBoundsMin);
        rayMarchCompute.SetVector("_FogBoundsMax", FogBoundsMax);

        rayMarchCompute.SetVector("_CloudsScale", cloudsScale);
        rayMarchCompute.SetFloat("_GlobalDensity", globalDensity);

        // Sampling
        rayMarchCompute.SetTexture(rayMarchKernel, "_OffsetNoise", offsetNoise);
        rayMarchCompute.SetFloat("_OffsetNoiseIntensity", offsetNoiseIntensity);
        rayMarchCompute.SetFloat("_CloudMinStepSize", Mathf.Max(cloudMinStepSize, 0.1f));
        rayMarchCompute.SetInt("_CloudMaxSteps", cloudMaxSteps);
        rayMarchCompute.SetFloat("_ThresholdSDF", sdfThreshold);
        rayMarchCompute.SetFloat("_RenderDistance", renderDistance);

        // Erosion
        rayMarchCompute.SetTexture(rayMarchKernel, "_Erosion", erosionTexture);
        rayMarchCompute.SetFloat("_ErosionIntensity", erosionIntensity);
        rayMarchCompute.SetFloat("_ErosionTextureScale", erosionTextureScale);
        rayMarchCompute.SetFloat("_ErosionWorldScale", erosionWorldScale);
        rayMarchCompute.SetVector("_ErosionSpeed", erosionSpeed);

        // Lighting
        rayMarchCompute.SetFloat("_SunLightAbsorption", sunlightAbsorption);
        rayMarchCompute.SetInt("_UsePowderEffect", usePowderEffect ? 1 : 0);
        rayMarchCompute.SetFloat("_PowderBrightness", powderBrightness);
        rayMarchCompute.SetFloat("_PowderIntensity", powderIntensity);
        rayMarchCompute.SetVector("_PhaseParams", phaseParams);
        rayMarchCompute.SetFloat("_ShadowsIntensity", shadowsIntensity);

        // Fog
        rayMarchCompute.SetFloat("_FogDensity", fogDensity);
        rayMarchCompute.SetFloat("_FogDistance", fogDistance);
        rayMarchCompute.SetFloat("_FogStepSize", fogStepSize);

        // Others
        rayMarchCompute.SetFloat("_CustomTime", Time.time);
        rayMarchCompute.SetTexture(rayMarchKernel, "_ShapeSDF", shapeSDF);
        rayMarchCompute.SetVector("_ShapeSDFSize", new Vector3(shapeSDF.width, shapeSDF.height, shapeSDF.depth));
    }

    public void SetUniforms_frag()
    {
        Camera.main.depthTextureMode = DepthTextureMode.Depth;

        // Shape
        postProcessMaterial.SetVector("_CloudsBoundsMin", CloudsBoundsMin);
        postProcessMaterial.SetVector("_CloudsBoundsMax", CloudsBoundsMax);

        postProcessMaterial.SetVector("_FogBoundsMin", FogBoundsMin);
        postProcessMaterial.SetVector("_FogBoundsMax", FogBoundsMax);

        postProcessMaterial.SetVector("_CloudsScale", cloudsScale);
        postProcessMaterial.SetFloat("_GlobalDensity", globalDensity);
        
        // Sampling
        postProcessMaterial.SetTexture("_OffsetNoise", offsetNoise);
        postProcessMaterial.SetFloat("_OffsetNoiseIntensity", offsetNoiseIntensity);
        postProcessMaterial.SetFloat("_CloudMinStepSize", Mathf.Max(cloudMinStepSize, 0.1f));
        postProcessMaterial.SetFloat("_LightMinStepSize", Mathf.Max(lightMinStepSize, 0.1f));
        postProcessMaterial.SetInt("_CloudMaxSteps", cloudMaxSteps);
        postProcessMaterial.SetFloat("_ThresholdSDF", sdfThreshold);
        postProcessMaterial.SetFloat("_RenderDistance", renderDistance);

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
        postProcessMaterial.SetFloat("_ShadowsIntensity", shadowsIntensity);

        // Fog
        postProcessMaterial.SetFloat("_FogDensity", fogDensity);
        postProcessMaterial.SetFloat("_FogDistance", fogDistance);
        postProcessMaterial.SetFloat("_FogStepSize", fogStepSize);

        // Others
        postProcessMaterial.SetFloat("_CustomTime", Time.time);
        postProcessMaterial.SetTexture("_ShapeSDF", shapeSDF);
        postProcessMaterial.SetVector("_ShapeSDFSize", new Vector3(shapeSDF.width, shapeSDF.height, shapeSDF.depth));
    }

    public override void Apply(RenderTexture source, RenderTexture dest)
    {
        if (active && postProcessMaterial != null && Camera.current != null)
        {
            SetUniforms_frag();
            Graphics.Blit(source, dest, postProcessMaterial);
        }
        else
        {
            Graphics.Blit(source, dest);
        }
    }
}
