using Palmmedia.ReportGenerator.Core;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;
using UnityEngine.UI;
using UnityEngine.UIElements;

[ExecuteInEditMode]
public class CloudsPostProcess_V4 : PostProcessBase
{
    [Header("V4 Parameters")]
    [SerializeField] public Texture3D shapeSDF;

    [Header("Erosion parameters")]
    [SerializeField] public bool useErosion;
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
    [SerializeField] private Light directionalLight;
    [SerializeField] private Transform cloudsContainer;
    [SerializeField] private Transform fogContainer;
    [SerializeField] private bool refresh;
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
    [SerializeField][Range(0, 1)] private float renderScale;
    public int renderWidth;
    public int renderHeight;

    void InitializeBuffers()
    {
        ReleaseBuffers();

        rayMarchKernel = rayMarchCompute.FindKernel("CSMain");
        
        renderWidth = (int)(Screen.width * renderScale);
        renderHeight = (int)(Screen.height * renderScale);

        RayMarchRenderTexture = new RenderTexture(renderWidth, renderHeight, 0, RenderTextureFormat.RGFloat);
        RayMarchRenderTexture.enableRandomWrite = true;
        RayMarchRenderTexture.filterMode = FilterMode.Bilinear;
        RayMarchRenderTexture.Create();
    }

    public void ReleaseBuffers()
    {
        if (RayMarchRenderTexture) RayMarchRenderTexture.Release();
    }

    public void SetupTransmittanceMap(RenderTexture mapTexture, Vector3 mapOrigin, Vector3Int mapResolution, Vector3 mapCoverage)
    {
        rayMarchCompute.SetTexture(rayMarchKernel, "_TransmittanceMap", mapTexture);
        rayMarchCompute.SetVector("_TransmittanceMapOrigin", mapOrigin);
        rayMarchCompute.SetVector("_TransmittanceMapCoverage", mapCoverage);
    }

    private void Compute_RayMarch()
    {
        if (Camera.current == null) return;

        Camera.current.depthTextureMode = DepthTextureMode.Depth;


        // Output
        rayMarchCompute.SetTexture(rayMarchKernel, "_Output", RayMarchRenderTexture);
        rayMarchCompute.SetInts("_OutputResolution", new int[] { RayMarchRenderTexture.width, RayMarchRenderTexture.height });

        // Depth buffer
        rayMarchCompute.SetTextureFromGlobal(rayMarchKernel, "_UnityDepthTexture", "_CameraDepthTexture");
        rayMarchCompute.SetInts("_UnityDepthTextureSize", new int[] { Screen.width, Screen.height });

        // View
        Vector3 cameraPos = Camera.current.transform.position;
        rayMarchCompute.SetFloats("_CameraPos", new float[] { cameraPos.x, cameraPos.y, cameraPos.z });
        rayMarchCompute.SetMatrix("_InvProjectionMatrix", Camera.current.projectionMatrix.inverse);
        rayMarchCompute.SetMatrix("_InvViewMatrix", Camera.current.worldToCameraMatrix.inverse);

        // Shape
        rayMarchCompute.SetFloats("_CloudsBoundsMin", new float[] { CloudsBoundsMin.x, CloudsBoundsMin.y, CloudsBoundsMin.z });
        rayMarchCompute.SetFloats("_CloudsBoundsMax", new float[] { CloudsBoundsMax.x, CloudsBoundsMax.y, CloudsBoundsMax.z });
        rayMarchCompute.SetFloats("_FogBoundsMin", new float[] { FogBoundsMin.x, FogBoundsMin.y, FogBoundsMin.z });
        rayMarchCompute.SetFloats("_FogBoundsMax", new float[] { FogBoundsMax.x, FogBoundsMax.y, FogBoundsMax.z });

        rayMarchCompute.SetFloats("_CloudsScale", new float[] { cloudsScale.x, cloudsScale.y, cloudsScale.z });
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
        rayMarchCompute.SetBool("_UseErosion", useErosion);
        rayMarchCompute.SetFloats("_ErosionSpeed", new float[] { erosionSpeed.x, erosionSpeed.y, erosionSpeed.z });

        // Lighting
        Vector3 lightDir = directionalLight.transform.forward.normalized;
        rayMarchCompute.SetFloats("_LightDir", new float[] { lightDir.x, lightDir.y, lightDir.z });
        rayMarchCompute.SetFloat("_SunLightAbsorption", sunlightAbsorption);
        rayMarchCompute.SetInt("_UsePowderEffect", usePowderEffect ? 1 : 0);
        rayMarchCompute.SetFloat("_PowderBrightness", powderBrightness);
        rayMarchCompute.SetFloat("_PowderIntensity", powderIntensity);
        rayMarchCompute.SetFloats("_PhaseParams", new float[] { phaseParams.x, phaseParams.y });
        rayMarchCompute.SetFloat("_ShadowsIntensity", shadowsIntensity);

        // Fog
        rayMarchCompute.SetFloat("_FogDensity", fogDensity);
        rayMarchCompute.SetFloat("_FogDistance", fogDistance);
        rayMarchCompute.SetFloat("_FogStepSize", fogStepSize);

        // Others
        rayMarchCompute.SetFloat("_CustomTime", Time.time);
        rayMarchCompute.SetTexture(rayMarchKernel, "_ShapeSDF", shapeSDF);
        rayMarchCompute.SetInts("_ShapeSDFSize", new int[] { shapeSDF.width, shapeSDF.height, shapeSDF.depth });

        // Dispatch
        int threadGroupSizeX = (renderWidth + 7) / 8;
        int threadGroupSizeY = (renderHeight + 7) / 8;

        rayMarchCompute.Dispatch(rayMarchKernel, threadGroupSizeX, threadGroupSizeY, 1);
    }

    private void SetMaterialUniforms()
    {
        postProcessMaterial.SetTexture("_LightTransmittanceTex", RayMarchRenderTexture);

    }

    private int screenWidth;
    private int screenHeight;
    private float lastRenderScale;

    public override void Apply(RenderTexture source, RenderTexture dest)
    {
        // Detech changes in resolution
        if (Screen.width != screenWidth || Screen.height != screenHeight || renderScale != lastRenderScale)
        {
            refresh = true;
        }
        screenWidth = Screen.width;
        screenHeight = Screen.height;
        lastRenderScale = renderScale;

        // Update render texture
        if (refresh)
        {
            InitializeBuffers();
            refresh = false;
        }

        // Apply effect
        if (active && postProcessMaterial != null && Camera.current != null)
        {
            Compute_RayMarch();
            SetMaterialUniforms();
            Graphics.Blit(source, dest, postProcessMaterial);
        }
        else
        {
            Graphics.Blit(source, dest);
        }
    }
}
