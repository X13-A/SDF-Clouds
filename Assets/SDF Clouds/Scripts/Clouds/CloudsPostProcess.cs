using UnityEngine;
using UnityEngine.XR;

[ExecuteInEditMode, ImageEffectAllowedInSceneView]
public class CloudsPostProcess : PostProcessBase
{
    public bool active;
    public bool refresh;
    
    public CloudsSettings cloudSettings;
    public Light sun;
    public TransmittanceMap transmittanceMap;

    [Header("Visualisation only")]
    public int renderWidth;
    public int renderHeight;

    int rayMarchKernel;
    public RenderTexture RayMarchRenderTexture { get; private set; }

    public Vector3 CloudsBoundsMin => cloudSettings.cloudsPosition - cloudSettings.cloudsScale / 2;
    public Vector3 CloudsBoundsMax => cloudSettings.cloudsPosition + cloudSettings.cloudsScale / 2;
    public Vector3 CloudsContainerCenter => cloudSettings.cloudsPosition;

    public Vector3 FogBoundsMin => cloudSettings.fogPosition - cloudSettings.fogScale / 2;
    public Vector3 FogBoundsMax => cloudSettings.fogPosition + cloudSettings.fogScale / 2;
    public Vector3 FogContainerCenter => cloudSettings.fogPosition;

    public bool InitializeBuffers()
    {
        ReleaseBuffers();

        rayMarchKernel = cloudSettings.cloudComputeShader.FindKernel("CSMain");

        if (XRSettings.isDeviceActive)
        {
            // TODO: Test VR with DLSS
            renderWidth = (int)(XRSettings.eyeTextureWidth * cloudSettings.renderScale);
            renderHeight = (int)(XRSettings.eyeTextureHeight * cloudSettings.renderScale);
        }
        else
        {
            renderWidth = (int)(screenWidth * cloudSettings.renderScale);
            renderHeight = (int)(screenHeight * cloudSettings.renderScale);
        }

        if (renderWidth <= 0 || renderHeight <= 0)
        {
            Debug.LogWarning("Could not create RenderTexture");
            return false;
        }

        RayMarchRenderTexture = new RenderTexture(renderWidth, renderHeight, 0, RenderTextureFormat.RGFloat);
        RayMarchRenderTexture.enableRandomWrite = true;
        RayMarchRenderTexture.filterMode = FilterMode.Bilinear;
        RayMarchRenderTexture.Create();
        return true;
    }

    public void ReleaseBuffers()
    {
        if (RayMarchRenderTexture) RayMarchRenderTexture.Release();
    }

    public void SetupTransmittanceMap(RenderTexture mapTexture, Vector3 mapOrigin, Vector3Int mapResolution, Vector3 mapCoverage)
    {
        cloudSettings.cloudComputeShader.SetTexture(rayMarchKernel, "_TransmittanceMap", mapTexture);
        cloudSettings.cloudComputeShader.SetVector("_TransmittanceMapOrigin", mapOrigin);
        cloudSettings.cloudComputeShader.SetVector("_TransmittanceMapCoverage", mapCoverage);

        cloudSettings.postProcessMaterial.SetTexture("_TransmittanceMap", mapTexture);
        cloudSettings.postProcessMaterial.SetVector("_TransmittanceMapOrigin", mapOrigin);
        cloudSettings.postProcessMaterial.SetVector("_TransmittanceMapCoverage", mapCoverage);
        cloudSettings.postProcessMaterial.SetVector("_TransmittanceMapResolution", new Vector3(mapResolution.x, mapResolution.y, mapResolution.z));
    }

    private void Compute_RayMarch(Camera camera)
    {
        bool success = SetComputeUniforms(camera);
        if (!success)
        {
            return;
        }

        // Dispatch
        int threadGroupSizeX = (renderWidth + 7) / 8;
        int threadGroupSizeY = (renderHeight + 7) / 8;

        cloudSettings.cloudComputeShader.Dispatch(rayMarchKernel, threadGroupSizeX, threadGroupSizeY, 1);
    }

    private bool SetComputeUniforms(Camera camera)
    {
        ComputeShader computeShader = cloudSettings.cloudComputeShader; 

        // Output
        cloudSettings.cloudComputeShader.SetTexture(rayMarchKernel, "_Output", RayMarchRenderTexture);
        cloudSettings.cloudComputeShader.SetInts("_OutputResolution", new int[] { RayMarchRenderTexture.width, RayMarchRenderTexture.height });

        // Depth buffer
        camera.depthTextureMode = DepthTextureMode.Depth;
        Texture depthTexture = Shader.GetGlobalTexture("_CameraDepthTexture");
        if (depthTexture == null)
        {
            Debug.Log("_CameraDepthTexture not ready yet !");
            return false;
        }
        else
        {
            computeShader.SetTextureFromGlobal(rayMarchKernel, "_UnityDepthTexture", "_CameraDepthTexture");
        }
        computeShader.SetInts("_UnityDepthTextureSize", new int[] { Screen.width, Screen.height });

        // Camera View
        Vector3 cameraPos = camera.transform.position;
        computeShader.SetFloats("_CameraPos", new float[] { cameraPos.x, cameraPos.y, cameraPos.z });
        computeShader.SetMatrix("_InvProjectionMatrix", camera.projectionMatrix.inverse);
        computeShader.SetMatrix("_InvViewMatrix", camera.worldToCameraMatrix.inverse);

        // Shape settings
        computeShader.SetFloats("_CloudsBoundsMin", new float[] { cloudSettings.CloudsBoundsMin.x, cloudSettings.CloudsBoundsMin.y, cloudSettings.CloudsBoundsMin.z });
        computeShader.SetFloats("_CloudsBoundsMax", new float[] { cloudSettings.CloudsBoundsMax.x, cloudSettings.CloudsBoundsMax.y, cloudSettings.CloudsBoundsMax.z });
        computeShader.SetFloats("_FogBoundsMin", new float[] { cloudSettings.FogBoundsMin.x, cloudSettings.FogBoundsMin.y, cloudSettings.FogBoundsMin.z });
        computeShader.SetFloats("_FogBoundsMax", new float[] { cloudSettings.FogBoundsMax.x, cloudSettings.FogBoundsMax.y, cloudSettings.FogBoundsMax.z });

        computeShader.SetFloats("_SDFTextureScale", new float[] { cloudSettings.sdfTextureScale.x, cloudSettings.sdfTextureScale.y, cloudSettings.sdfTextureScale.z });
        computeShader.SetFloats("_SDFTextureOffset", new float[] { cloudSettings.sdfTextureOffset.x, cloudSettings.sdfTextureOffset.y, cloudSettings.sdfTextureOffset.z });
        computeShader.SetFloat("_GlobalDensity", cloudSettings.globalDensity);

        // Sampling
        computeShader.SetFloat("_OffsetNoiseIntensity", cloudSettings.offsetNoiseIntensity);
        computeShader.SetFloat("_CloudMinStepSize", Mathf.Max(cloudSettings.cloudMinStepSize, 0.1f));
        computeShader.SetInt("_CloudMaxSteps", cloudSettings.cloudMaxSteps);
        computeShader.SetFloat("_ThresholdSDF", cloudSettings.sdfThreshold);
        computeShader.SetFloat("_RenderDistance", cloudSettings.renderDistance);
        computeShader.SetInt("_UseSDFInsideClouds", cloudSettings.useSDFInsideClouds ? 1 : 0);

        // Erosion
        computeShader.SetTexture(rayMarchKernel, "_Erosion", cloudSettings.erosionTexture);
        computeShader.SetFloat("_ErosionIntensity", cloudSettings.erosionIntensity);
        computeShader.SetFloat("_ErosionTextureScale", cloudSettings.erosionTextureScale);
        computeShader.SetFloat("_ErosionWorldScale", cloudSettings.erosionWorldScale);
        computeShader.SetInt("_UseErosion", cloudSettings.useErosion ? 1 : 0);
        computeShader.SetFloats("_ErosionSpeed", new float[] { cloudSettings.erosionSpeed.x, cloudSettings.erosionSpeed.y, cloudSettings.erosionSpeed.z });

        // Lighting
        Vector3 lightDir = transmittanceMap.LightDir;
        computeShader.SetFloats("_LightDir", new float[] { lightDir.x, lightDir.y, lightDir.z });;
        computeShader.SetFloats("_LightDir", new float[] { lightDir.x, lightDir.y, lightDir.z });
        computeShader.SetFloat("_SunLightAbsorption", cloudSettings.sunlightAbsorption);
        computeShader.SetFloat("_DirectionalScattering", cloudSettings.directionalScattering);

        // Transmittance
        computeShader.SetTexture(rayMarchKernel, "_TransmittanceMap", transmittanceMap.MapRenderTexture);
        computeShader.SetFloats("_TransmittanceMapOrigin", new float[] { cloudSettings.cloudsPosition.x, cloudSettings.cloudsPosition.y, cloudSettings.cloudsPosition.z });
        computeShader.SetFloats("_TransmittanceMapCoverage", new float[] { transmittanceMap.MapWidth, transmittanceMap.MapHeight, transmittanceMap.MapDepth });

        // Fog
        double fogDensity = (double) cloudSettings.fogDensity / 1000000000000.0;
        computeShader.SetFloat("_FogDensity", (float) fogDensity);
        computeShader.SetFloat("_FogDistance", cloudSettings.fogDistance);
        computeShader.SetFloat("_FogStepSize", cloudSettings.fogStepSize);

        // Time
        computeShader.SetFloat("_CustomTime", Time.time);

        // Shape SDF
        computeShader.SetTexture(rayMarchKernel, "_ShapeSDF", cloudSettings.sdfTexture);
        computeShader.SetInts("_ShapeSDFSize", new int[] { cloudSettings.sdfTexture.width, cloudSettings.sdfTexture.height, cloudSettings.sdfTexture.depth });
        return true;
    }
    private void SetMaterialUniforms()
    {
        cloudSettings.postProcessMaterial.SetTexture("_CloudsRayMarchTexture", RayMarchRenderTexture);

        cloudSettings.postProcessMaterial.SetVector("_BoundsMin", cloudSettings.CloudsBoundsMin);
        cloudSettings.postProcessMaterial.SetVector("_BoundsMax", cloudSettings.CloudsBoundsMax);

        cloudSettings.postProcessMaterial.SetFloat("_LightMultiplier", cloudSettings.lightMultiplier);

        // Shadows
        cloudSettings.postProcessMaterial.SetVector("_ShadowColor", cloudSettings.shadowColor);
        cloudSettings.postProcessMaterial.SetFloat("_ShadowingOffset", cloudSettings.shadowingOffset);

        cloudSettings.postProcessMaterial.SetFloat("_CustomTime", Time.time);
    }

    private int screenWidth;
    private int screenHeight;
    private float lastRenderScale;
    private bool deviceActive;

    public override void Apply(RenderTexture source, RenderTexture dest, Camera camera)
    {
        // Detect changes in resolution
        bool resolutionChanged = source.width != screenWidth || source.height != screenHeight || cloudSettings.renderScale != lastRenderScale;
        bool deviceChanged = deviceActive != XRSettings.isDeviceActive;
        if (resolutionChanged || deviceChanged)
        {
            refresh = true;
        }
        
        screenWidth = source.width;
        screenHeight = source.height;
        lastRenderScale = cloudSettings.renderScale;
        deviceActive = XRSettings.isDeviceActive;


        // Update render texture
        bool valid = true;
        if (refresh)
        {
            valid = InitializeBuffers();
            refresh = !valid; // Try again next frame if buffers are not valid
        }

        // Apply effect
        if (valid && active && cloudSettings.postProcessMaterial != null && camera != null)
        {
            Compute_RayMarch(camera);
            SetMaterialUniforms();
            Graphics.Blit(source, dest, cloudSettings.postProcessMaterial);
        }
        else
        {
            Graphics.Blit(source, dest);
        }
    }
}
