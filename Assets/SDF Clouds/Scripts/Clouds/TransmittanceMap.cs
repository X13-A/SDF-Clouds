using UnityEngine;
using UnityEngine.Experimental.Rendering;

[ExecuteInEditMode]
public class TransmittanceMap : MonoBehaviour
{
    public bool refreshAll;
    public bool calculateLightingEachFrame;

    [Header("References")]
    [SerializeField] private ComputeShader mapCompute;
    [SerializeField] private CloudsPostProcess clouds;
    
    [Header("Quality params")]
    [SerializeField] private float lightMinStepSize;
    [SerializeField] private int textureWidth = 1920;
    [SerializeField] private int textureHeight = 1920;
    [SerializeField] private int textureDepth= 1920;
    [SerializeField] private bool useErosion = false;

    private float mapWidth => clouds.cloudSettings.sdfTextureScale.x;
    private float mapHeight => clouds.cloudSettings.cloudsScale.y;
    private float mapDepth => clouds.cloudSettings.sdfTextureScale.z;

    [Header("Dev only, very slow")]
    [SerializeField] private Light pointLight;
    public bool visualizeMap;
    public Texture3D mapVisualizer;

    public int TextureWidth => textureWidth;
    public int TextureHeight => textureHeight;
    public int TextureDepth => textureDepth;

    public float MapWidth => mapWidth;
    public float MapHeight => mapHeight;
    public float MapDepth => mapDepth;
    
    private int mapKernel;
    public Vector3 LightDir => clouds.sun.transform.forward;
    public RenderTexture MapRenderTexture { get; private set; }

    private void Awake()
    {
        refreshAll = true;
        Setup();
    }

    private void Start()
    {
        refreshAll = true;
        Setup();
    }


    private void Setup()
    {
        if (clouds == null) return;

        UnityEngine.Object.DestroyImmediate(MapRenderTexture);

        mapKernel = mapCompute.FindKernel("CSMain");

        // Create Render Texture
        MapRenderTexture = new RenderTexture(textureWidth, textureHeight, 0, GraphicsFormat.R8_UNorm);
        MapRenderTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        MapRenderTexture.volumeDepth = textureDepth;
        MapRenderTexture.enableRandomWrite = true;
        MapRenderTexture.filterMode = FilterMode.Bilinear;
        MapRenderTexture.wrapMode = TextureWrapMode.Repeat;
        MapRenderTexture.Create();
    }

    private void ComputeLighting()
    {
        mapCompute.SetTexture(mapKernel, "_TransmittanceMap", MapRenderTexture);

        // View
        mapCompute.SetFloats("_LightDir", new float[] { LightDir.x, LightDir.y, LightDir.z });

        // Debug view
        mapCompute.SetFloats("_CameraPos", new float[] { Camera.main.transform.position.x, Camera.main.transform.position.y, Camera.main.transform.position.z });
        mapCompute.SetMatrix("_InvProjectionMatrix", Camera.main.projectionMatrix.inverse);
        mapCompute.SetMatrix("_InvViewMatrix", Camera.main.worldToCameraMatrix.inverse);

        // Shape
        mapCompute.SetFloats("_BoundsMin", new float[] { clouds.CloudsBoundsMin.x, clouds.CloudsBoundsMin.y, clouds.CloudsBoundsMin.z });
        mapCompute.SetFloats("_BoundsMax", new float[] { clouds.CloudsBoundsMax.x, clouds.CloudsBoundsMax.y, clouds.CloudsBoundsMax.z });
        mapCompute.SetFloats("_SDFTextureScale", new float[] { clouds.cloudSettings.sdfTextureScale.x, clouds.cloudSettings.sdfTextureScale.y, clouds.cloudSettings.sdfTextureScale.z });
        mapCompute.SetFloat("_GlobalDensity", clouds.cloudSettings.globalDensity);

        // SDF
        mapCompute.SetTexture(mapKernel, "_ShapeSDF", clouds.cloudSettings.sdfTexture);
        mapCompute.SetInts("_ShapeSDFSize", new int[] { clouds.cloudSettings.sdfTexture.width, clouds.cloudSettings.sdfTexture.height, clouds.cloudSettings.sdfTexture.depth });
        mapCompute.SetFloat("_ThresholdSDF", clouds.cloudSettings.sdfThreshold);

        // Lighting
        mapCompute.SetFloat("_LightMinStepSize", lightMinStepSize);
        mapCompute.SetFloat("_SunLightAbsorption", clouds.cloudSettings.sunlightAbsorption);

        // Erosion
        mapCompute.SetTexture(mapKernel, "_Erosion", clouds.cloudSettings.erosionTexture);
        mapCompute.SetFloat("_ErosionTextureScale", clouds.cloudSettings.erosionTextureScale);
        mapCompute.SetFloat("_ErosionWorldScale", clouds.cloudSettings.erosionWorldScale);
        mapCompute.SetFloats("_ErosionSpeed", new float[] { clouds.cloudSettings.erosionSpeed.x, clouds.cloudSettings.erosionSpeed.y, clouds.cloudSettings.erosionSpeed.z });
        mapCompute.SetFloat("_ErosionIntensity", clouds.cloudSettings.erosionIntensity);
        mapCompute.SetBool("_UseErosion", useErosion);

        // Position & size
        mapCompute.SetFloats("_StartPos", new float[] { clouds.CloudsContainerCenter.x, clouds.CloudsContainerCenter.y, clouds.CloudsContainerCenter.z });
        mapCompute.SetFloats("_TransmittanceMapCoverage", new float[] { mapWidth, mapHeight, mapDepth });
        mapCompute.SetInts("_TransmittanceMapResolution", new int[] { textureWidth, textureHeight, TextureDepth });

        // Dev: point light
        if (pointLight != null)
        {
            mapCompute.SetBool("_UsePointLight", true);
            mapCompute.SetFloats("_PointLightPos", new float[] { pointLight.transform.position.x, pointLight.transform.position.y, pointLight.transform.position.z });
        }
        else
        {
            mapCompute.SetBool("_UsePointLight", false);
        }

            // Dispatch
            mapCompute.Dispatch(mapKernel, Mathf.CeilToInt(textureWidth / 8.0f), Mathf.CeilToInt(textureHeight / 8.0f), Mathf.CeilToInt(textureDepth / 8.0f));
        clouds.SetupTransmittanceMap(MapRenderTexture, clouds.CloudsContainerCenter, new Vector3Int(textureWidth, textureHeight, textureDepth), new Vector3(mapWidth, mapHeight, mapDepth));
    }

    private void Update()
    {
        if (Camera.main == null)
        {
            Debug.LogWarning("No camera available");
            return;
        }
        if (clouds == null)
        {
            Debug.LogWarning("Missing reference to CloudsPostProcess");
            return;
        }
        if (MapRenderTexture == null)
        {
            Debug.LogWarning("RenderTexture not created");
        }
        if (!refreshAll && !calculateLightingEachFrame)
        {
            return;
        }

        if (refreshAll == true)
        {
            refreshAll = false;
            Setup();
            ComputeLighting();
        }
        else if (calculateLightingEachFrame)
        {
            ComputeLighting();
        }

        if (visualizeMap)
        {
            StartCoroutine(RenderingUtils.ConvertRenderTextureToTexture3D(MapRenderTexture, 1, TextureFormat.R8, TextureWrapMode.Clamp, FilterMode.Bilinear, (Texture3D res) =>
            {
                mapVisualizer = res;
            })); 
        }
    }
}
