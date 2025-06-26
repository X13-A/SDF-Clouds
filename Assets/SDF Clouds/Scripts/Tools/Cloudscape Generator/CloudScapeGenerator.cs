using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif
[ExecuteInEditMode]
public class CloudScapeGenerator : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private CloudScapeConfig config;
    [SerializeField] private ComputeShader compute;

    public Vector3Int Size => new Vector3Int(config.width, config.height, config.depth);
    private RenderTexture CloudRenderTexture;

    
    [Header("Actions")]
    [SerializeField] private bool startCompute;
    
    [Header("Result")]
    [SerializeField] private string outputPath;
    [SerializeField] private Texture3D CloudTexture;
    
    private int computeKernel;
    public bool DoneGenerating { get; private set; }

    void StartCompute()
    {
        computeKernel = compute.FindKernel("CSMain");
        StartCoroutine(GenerateClouds_GPU());
    }

    // 50x faster than on CPU (RTX 4050, 60W - R9 7940HS, 35W)
    public IEnumerator GenerateClouds_GPU(Action callback = null, bool log = true)
    {
        DoneGenerating = false;

        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();
        if (log)
        {
            UnityEngine.Debug.Log("Starting world generation (GPU)...");
        }

        // Create a RenderTexture with 3D support and enable random write
        RenderTextureDescriptor worldDesc = new RenderTextureDescriptor
        {
            width = config.width,
            height = config.height,
            volumeDepth = config.depth,
            dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
            enableRandomWrite = true,
            graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
            msaaSamples = 1
        };

        // Create the new texture based on the descriptor
        if (CloudRenderTexture != null) CloudRenderTexture.Release();
        CloudRenderTexture = new RenderTexture(worldDesc);
        CloudRenderTexture.Create();

        // Bind the texture to the compute shader
        compute.SetTexture(computeKernel, "CloudTexture", CloudRenderTexture);

        // Set uniforms
        compute.SetInts("_CloudsSize", new int[] { CloudRenderTexture.width, CloudRenderTexture.height, CloudRenderTexture.volumeDepth });
        compute.SetVector("_CloudsScale", new Vector4(config.cloudsScale.x, config.cloudsScale.y, config.cloudsScale.z, 0));
        compute.SetVector("_CloudsOffset", new Vector4(config.cloudsOffset.x, config.cloudsOffset.y, config.cloudsScale.z, 0));
        compute.SetInt("_CloudsSeed", (int)config.cloudsSeed);
        compute.SetFloat("_CloudsThreshold", config.cloudsThreshold);
        compute.SetFloat("_BorderAttenuation", config.borderAttenuation);

        // Dispatch the compute shader
        int threadGroupsX = Mathf.CeilToInt(config.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(config.height / 8.0f);
        int threadGroupsZ = Mathf.CeilToInt(config.depth / 8.0f);
        compute.Dispatch(computeKernel, threadGroupsX, threadGroupsY, threadGroupsZ);

        stopwatch.Stop();
        float generationTime = (float)stopwatch.Elapsed.TotalSeconds;
        if (log)
        {
            UnityEngine.Debug.Log($"World generated in {generationTime} seconds, converting it to readable Texture3D...");
        }
        stopwatch.Restart();

        // Convert RenderTexture to Texture3D.
        StartCoroutine(RenderingUtils.ConvertRenderTextureToTexture3D(CloudRenderTexture, 1, TextureFormat.R8, TextureWrapMode.Repeat, FilterMode.Point, (Texture3D tex) =>
        {
            CloudTexture = tex;
            CloudRenderTexture.Release();
            float conversionTime = (float)stopwatch.Elapsed.TotalSeconds - generationTime;

            UnityEngine.Debug.Log($"Conversion done in {conversionTime} seconds !");
            #if UNITY_EDITOR
            AssetDatabase.CreateAsset(tex, outputPath);
            #endif
        }));
        
        while (!DoneGenerating)
        {
            yield return null;
        }

        stopwatch.Stop();
        if (log)
        {
            float conversionTime = (float)stopwatch.Elapsed.TotalSeconds;
            UnityEngine.Debug.Log($"Conversion done in {conversionTime} seconds!");
        }
        callback?.Invoke();
    }

    private void Update()
    {
        if (startCompute)
        {
            StartCompute();
            startCompute = false;
        }
    }
}