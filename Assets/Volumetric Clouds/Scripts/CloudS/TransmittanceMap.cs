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

    [SerializeField] private CloudsPostProcess_V3 clouds;

    public bool Refresh;

    private void Awake()
    {
        Setup();
    }

    private void Setup()
    {
        if (clouds == null) return;
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
        if (clouds == null) return;
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
        mapCompute.SetFloats("_BoundsMin", new float[] { clouds.BoundsMin.x, clouds.BoundsMin.y, clouds.BoundsMin.z });
        mapCompute.SetFloats("_BoundsMax", new float[] { clouds.BoundsMax.x, clouds.BoundsMax.y, clouds.BoundsMax.z });
        mapCompute.SetFloats("_CloudsScale", new float[] { clouds.cloudsScale.x, clouds.cloudsScale.y, clouds.cloudsScale.z });
        mapCompute.SetFloat("_GlobalDensity", clouds.globalDensity);

        // SDF
        mapCompute.SetTexture(mapKernel, "_ShapeSDF", clouds.shapeSDF);
        mapCompute.SetInts("_ShapeSDFSize", new int[] { clouds.shapeSDF.width, clouds.shapeSDF.height, clouds.shapeSDF.depth });
        mapCompute.SetFloat("_ThresholdSDF", clouds.sdfThreshold);

        // Lighting
        mapCompute.SetFloat("_LightMinStepSize", lightMinStepSize);
        mapCompute.SetFloat("_SunLightAbsorption", clouds.sunlightAbsorption);

        // Powder effect
        mapCompute.SetFloat("_PowderBrightness", clouds.powderBrightness);
        mapCompute.SetFloat("_PowderIntensity", clouds.powderIntensity);
        mapCompute.SetBool("_UsePowderEffect", clouds.usePowderEffect);

        // Erosion
        mapCompute.SetTexture(mapKernel, "_Erosion", clouds.erosionTexture);
        mapCompute.SetFloat("_ErosionTextureScale", clouds.erosionTextureScale);
        mapCompute.SetFloat("_ErosionWorldScale", clouds.erosionWorldScale);
        mapCompute.SetFloats("_ErosionSpeed", new float[] { clouds.erosionSpeed.x, clouds.erosionSpeed.y, clouds.erosionSpeed.z });
        mapCompute.SetFloat("_ErosionIntensity", clouds.erosionIntensity);

        // Others
        mapCompute.SetTexture(mapKernel, "_OffsetNoise", offsetNoise);
        mapCompute.SetFloat("_OffsetNoiseIntensity", offsetNoiseIntensity);

        // Position & size
        mapCompute.SetFloats("_StartPos", new float[] { clouds.ContainerCenter.x, clouds.ContainerCenter.y, clouds.ContainerCenter.z });
        mapCompute.SetFloats("_TransmittanceMapCoverage", new float[] { mapWidth, mapHeight, mapDepth });
        mapCompute.SetInts("_TransmittanceMapResolution", new int[] { textureWidth, textureHeight, TextureDepth });

        // Dispatch
        mapCompute.Dispatch(mapKernel, Mathf.CeilToInt(textureWidth / 8.0f), Mathf.CeilToInt(textureHeight / 8.0f), Mathf.CeilToInt(textureDepth / 8.0f));
        clouds.SetupTransmittanceMap(MapRenderTexture, clouds.ContainerCenter, new Vector3Int(textureWidth, textureHeight, textureDepth), new Vector3(mapWidth, mapHeight, mapDepth));
        //StartCoroutine(RenderingUtils.ConvertRenderTextureToTexture3D(MapRenderTexture, 1, TextureFormat.R8, TextureWrapMode.Clamp, FilterMode.Bilinear, (Texture3D res) =>
        //{
        //    mapViz = res;
        //}));
    }
}
