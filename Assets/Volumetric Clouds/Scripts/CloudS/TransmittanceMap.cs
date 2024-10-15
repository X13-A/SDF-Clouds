using UnityEngine;

[ExecuteInEditMode]
public class TransmittanceMap : MonoBehaviour
{
    [Header("Quality params")]
    [SerializeField] private float lightMinStepSize;
    [SerializeField] private Texture2D offsetNoise;
    [SerializeField] private float offsetNoiseIntensity;

    [SerializeField] private Transform lightForwardTransform;

    [Header("Light projection params")]
    [SerializeField] private float mapWidth = 50f;
    [SerializeField] private float mapHeight = 50f;
    [SerializeField] private float mapDepth = 50f;

    public float MapWidth => mapWidth;
    public float MapHeight => mapHeight;
    public Vector3 LightDir => lightForwardTransform.forward;

    [Header("Computing")]
    [SerializeField] private ComputeShader mapCompute;
    public RenderTexture MapRenderTexture { get; private set; }
    public Texture3D mapViz;

    [SerializeField] private int textureWidth = 1920;
    [SerializeField] private int textureHeight = 1920;
    [SerializeField] private int textureDepth= 1920;
    public int TextureWidth => textureWidth;
    public int TextureHeight => textureHeight;
    public int TextureDepth => textureDepth;
    private int mapKernel;
    [SerializeField] private bool useErosion = false;

    [SerializeField] private CloudsPostProcess_V3 cloudsV3;
    [SerializeField] private CloudsPostProcess_V4 cloudsV4;

    public bool Refresh;

    private void Awake()
    {
        Setup();
    }

    private void Setup()
    {
        if (cloudsV3 == null) return;
        if (lightForwardTransform == null) return;

        UnityEngine.Object.DestroyImmediate(MapRenderTexture);

        mapKernel = mapCompute.FindKernel("CSMain");

        // Create Render Texture
        MapRenderTexture = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.R8);
        MapRenderTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        MapRenderTexture.volumeDepth = textureDepth;
        MapRenderTexture.enableRandomWrite = true;
        MapRenderTexture.filterMode = FilterMode.Bilinear;
        MapRenderTexture.wrapMode = TextureWrapMode.Clamp;
        MapRenderTexture.Create();
    }

    private void Update()
    {
        if (Camera.main == null) return;
        if (cloudsV3 == null) return;
        if (cloudsV4 == null) return;
        if (Refresh)
        {
            Refresh = false;
            Setup();
        }

        mapCompute.SetTexture(mapKernel, "_TransmittanceMap", MapRenderTexture);

        // View
        mapCompute.SetFloats("_LightDir", new float[] { LightDir.x, LightDir.y, LightDir.z });

        // Debug view
        mapCompute.SetFloats("_CameraPos", new float[] { Camera.main.transform.position.x, Camera.main.transform.position.y, Camera.main.transform.position.z });
        mapCompute.SetMatrix("_InvProjectionMatrix", Camera.main.projectionMatrix.inverse);
        mapCompute.SetMatrix("_InvViewMatrix", Camera.main.worldToCameraMatrix.inverse);

        // Shape
        mapCompute.SetFloats("_BoundsMin", new float[] { cloudsV3.CloudsBoundsMin.x, cloudsV3.CloudsBoundsMin.y, cloudsV3.CloudsBoundsMin.z });
        mapCompute.SetFloats("_BoundsMax", new float[] { cloudsV3.CloudsBoundsMax.x, cloudsV3.CloudsBoundsMax.y, cloudsV3.CloudsBoundsMax.z });
        mapCompute.SetFloats("_CloudsScale", new float[] { cloudsV3.cloudsScale.x, cloudsV3.cloudsScale.y, cloudsV3.cloudsScale.z });
        mapCompute.SetFloat("_GlobalDensity", cloudsV3.globalDensity);

        // SDF
        mapCompute.SetTexture(mapKernel, "_ShapeSDF", cloudsV3.shapeSDF);
        mapCompute.SetInts("_ShapeSDFSize", new int[] { cloudsV3.shapeSDF.width, cloudsV3.shapeSDF.height, cloudsV3.shapeSDF.depth });
        mapCompute.SetFloat("_ThresholdSDF", cloudsV3.sdfThreshold);

        // Lighting
        mapCompute.SetFloat("_LightMinStepSize", lightMinStepSize);
        mapCompute.SetFloat("_SunLightAbsorption", cloudsV3.sunlightAbsorption);

        // Powder effect
        mapCompute.SetFloat("_PowderBrightness", cloudsV3.powderBrightness);
        mapCompute.SetFloat("_PowderIntensity", cloudsV3.powderIntensity);
        mapCompute.SetBool("_UsePowderEffect", cloudsV3.usePowderEffect);

        // Erosion
        mapCompute.SetTexture(mapKernel, "_Erosion", cloudsV3.erosionTexture);
        mapCompute.SetFloat("_ErosionTextureScale", cloudsV3.erosionTextureScale);
        mapCompute.SetFloat("_ErosionWorldScale", cloudsV3.erosionWorldScale);
        mapCompute.SetFloats("_ErosionSpeed", new float[] { cloudsV3.erosionSpeed.x, cloudsV3.erosionSpeed.y, cloudsV3.erosionSpeed.z });
        mapCompute.SetFloat("_ErosionIntensity", cloudsV3.erosionIntensity);
        mapCompute.SetBool("_UseErosion", useErosion);

        // Others
        mapCompute.SetTexture(mapKernel, "_OffsetNoise", offsetNoise);
        mapCompute.SetFloat("_OffsetNoiseIntensity", offsetNoiseIntensity);

        // Position & size
        mapCompute.SetFloats("_StartPos", new float[] { cloudsV3.CloudsContainerCenter.x, cloudsV3.CloudsContainerCenter.y, cloudsV3.CloudsContainerCenter.z });
        mapCompute.SetFloats("_TransmittanceMapCoverage", new float[] { mapWidth, mapHeight, mapDepth });
        mapCompute.SetInts("_TransmittanceMapResolution", new int[] { textureWidth, textureHeight, TextureDepth });

        // Dispatch
        mapCompute.Dispatch(mapKernel, Mathf.CeilToInt(textureWidth / 8.0f), Mathf.CeilToInt(textureHeight / 8.0f), Mathf.CeilToInt(textureDepth / 8.0f));
        cloudsV3.SetupTransmittanceMap(MapRenderTexture, cloudsV3.CloudsContainerCenter, new Vector3Int(textureWidth, textureHeight, textureDepth), new Vector3(mapWidth, mapHeight, mapDepth));
        cloudsV4.SetupTransmittanceMap(MapRenderTexture, cloudsV3.CloudsContainerCenter, new Vector3Int(textureWidth, textureHeight, textureDepth), new Vector3(mapWidth, mapHeight, mapDepth));
        //StartCoroutine(RenderingUtils.ConvertRenderTextureToTexture3D(MapRenderTexture, 1, TextureFormat.R8, TextureWrapMode.Clamp, FilterMode.Bilinear, (Texture3D res) =>
        //{
        //    mapViz = res;
        //}));
    }
}
