using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class CloudScapeGenerator : MonoBehaviour
{
    public ComputeShader computeShader;
    public Texture3D inputTexture;
    public bool compute;
    public string outputPath;
    public float threshold;

    private RenderTexture outputTexture;

    void ConvertVolumeToSDF()
    {
        // Create a RenderTexture for the SDF
        int width = inputTexture.width;
        int height = inputTexture.height;
        int depth = inputTexture.depth;

        outputTexture = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
        outputTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        outputTexture.volumeDepth = depth;
        outputTexture.enableRandomWrite = true;
        outputTexture.Create();

        // Set up the compute shader
        int kernelHandle = computeShader.FindKernel("CSMain");
        computeShader.SetTexture(kernelHandle, "_InputTexture", inputTexture);
        computeShader.SetTexture(kernelHandle, "_OutputTexture", outputTexture);
        computeShader.SetInts("_TextureSize", width, height, depth);
        computeShader.SetFloat("_Threshold", threshold);
        // Dispatch the compute shader
        int threadGroupsX = Mathf.CeilToInt(width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(height / 8.0f);
        int threadGroupsZ = Mathf.CeilToInt(depth / 8.0f);
        computeShader.Dispatch(kernelHandle, threadGroupsX, threadGroupsY, threadGroupsZ);

        StartCoroutine(RenderingUtils.ConvertRenderTextureToTexture3D(outputTexture, 4, TextureFormat.RFloat, TextureWrapMode.Repeat, FilterMode.Point, (Texture3D tex) =>
        {
            outputTexture.Release();
#if UNITY_EDITOR
            AssetDatabase.CreateAsset(tex, outputPath + ".asset");
#endif
            UnityEngine.Debug.Log($"Saved SDF texture at: {outputPath}.asset");
        }));
    }

    private void SaveTexture3D(Texture3D texture, string path)
    {
        AssetDatabase.CreateAsset(texture, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void Update()
    {
        if (compute)
        {
            ConvertVolumeToSDF();

            compute = false;
        }
    }
}
