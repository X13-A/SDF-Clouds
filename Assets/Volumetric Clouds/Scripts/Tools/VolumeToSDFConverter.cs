using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class VolumeToSDFConverter : MonoBehaviour
{
    public ComputeShader sdfComputeShader;
    public Texture3D volumeTexture;
    public bool compute;
    public string outputPath;

    private RenderTexture sdfTexture;

    void ConvertVolumeToSDF()
    {
        // Create a RenderTexture for the SDF
        int width = volumeTexture.width;
        int height = volumeTexture.height;
        int depth = volumeTexture.depth;

        sdfTexture = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
        sdfTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        sdfTexture.volumeDepth = depth;
        sdfTexture.enableRandomWrite = true;
        sdfTexture.Create();

        // Set up the compute shader
        int kernelHandle = sdfComputeShader.FindKernel("CSMain");
        sdfComputeShader.SetTexture(kernelHandle, "_VolumeTexture", volumeTexture);
        sdfComputeShader.SetTexture(kernelHandle, "_SDFTexture", sdfTexture);
        sdfComputeShader.SetInts("_TextureSize", width, height, depth);

        // Dispatch the compute shader
        int threadGroupsX = Mathf.CeilToInt(width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(height / 8.0f);
        int threadGroupsZ = Mathf.CeilToInt(depth / 8.0f);
        sdfComputeShader.Dispatch(kernelHandle, threadGroupsX, threadGroupsY, threadGroupsZ);

        StartCoroutine(RenderingUtils.ConvertRenderTextureToTexture3D(sdfTexture, 4, TextureFormat.RFloat, TextureWrapMode.Repeat, FilterMode.Point, (Texture3D tex) =>
        {
            sdfTexture.Release();
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
