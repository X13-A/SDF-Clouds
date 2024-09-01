using UnityEngine;
using System.Collections;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class TextureDownScaler : MonoBehaviour
{
    public ComputeShader downscaleComputeShader;
    public Texture3D inputTexture;
    public Vector3Int targetResolution;
    public bool compute;
    public string outputPath;

    private RenderTexture outputTexture;

    void DownscaleVolume()
    {
        int width = targetResolution.x;
        int height = targetResolution.y;
        int depth = targetResolution.z;

        // Create RenderTexture for the downscaled SDF
        outputTexture = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
        outputTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        outputTexture.volumeDepth = depth;
        outputTexture.enableRandomWrite = true;
        outputTexture.Create();

        // Set up the compute shader
        int kernelHandle = downscaleComputeShader.FindKernel("Downscale");
        downscaleComputeShader.SetTexture(kernelHandle, "_InputTexture", inputTexture);
        downscaleComputeShader.SetTexture(kernelHandle, "_OutputTexture", outputTexture);
        downscaleComputeShader.SetInts("_InputSize", inputTexture.width, inputTexture.height, inputTexture.depth);
        downscaleComputeShader.SetInts("_OutputSize", width, height, depth);

        // Dispatch the compute shader
        int threadGroupsX = Mathf.CeilToInt(width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(height / 8.0f);
        int threadGroupsZ = Mathf.CeilToInt(depth / 8.0f);
        downscaleComputeShader.Dispatch(kernelHandle, threadGroupsX, threadGroupsY, threadGroupsZ);

        // Convert RenderTexture to Texture3D and save
        StartCoroutine(RenderingUtils.ConvertRenderTextureToTexture3D(outputTexture, 4, TextureFormat.RFloat, TextureWrapMode.Repeat, FilterMode.Point, (Texture3D tex) =>
        {
            outputTexture.Release();
#if UNITY_EDITOR
            AssetDatabase.CreateAsset(tex, outputPath + ".asset");
#endif
            Debug.Log($"Saved downscaled texture at: {outputPath}.asset");
        }));
    }

    private void Update()
    {
        if (compute)
        {
            DownscaleVolume();
            compute = false;
        }
    }
}
