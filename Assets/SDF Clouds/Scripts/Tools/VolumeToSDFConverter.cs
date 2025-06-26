using UnityEngine;
using System.Collections.Generic;
using System.Collections;

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

    // Define the size of the chunk (cube) processed per dispatch
    public int chunkSize = 16;

    IEnumerator ConvertVolumeToSDF_GPU()
    {
        // Create a RenderTexture for the SDF
        int width = volumeTexture.width;
        int height = volumeTexture.height;
        int depth = volumeTexture.depth;

        sdfTexture = new RenderTexture(width, height, 0, RenderTextureFormat.RHalf);
        sdfTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        sdfTexture.volumeDepth = depth;
        sdfTexture.enableRandomWrite = true;
        sdfTexture.Create();

        // Set up the compute shader
        int kernelHandle = sdfComputeShader.FindKernel("CSMain");
        sdfComputeShader.SetTexture(kernelHandle, "_VolumeTexture", volumeTexture);
        sdfComputeShader.SetTexture(kernelHandle, "_SDFTexture", sdfTexture);
        sdfComputeShader.SetInts("_TextureSize", width, height, depth);

        // Dispatch the compute shader in chunks
        int threadGroupsX = Mathf.CeilToInt(chunkSize / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(chunkSize / 8.0f);
        int threadGroupsZ = Mathf.CeilToInt(chunkSize / 8.0f);

        for (int z = 0; z < depth; z += chunkSize)
        {
            for (int y = 0; y < height; y += chunkSize)
            {
                for (int x = 0; x < width; x += chunkSize)
                {
                    Debug.Log($"Processing batch x: {x / chunkSize}, y: {y / chunkSize}, z: {z / chunkSize}");
                    yield return new WaitForSeconds(0.1f);

                    // Set the offset for the chunk
                    sdfComputeShader.SetInts("_ChunkOffset", x, y, z);

                    // Determine the actual size of the chunk (handle edges)
                    int chunkWidth = Mathf.Min(chunkSize, width - x); 
                    int chunkHeight = Mathf.Min(chunkSize, height - y);
                    int chunkDepth = Mathf.Min(chunkSize, depth - z);
                    sdfComputeShader.SetInts("_ChunkSize", chunkWidth, chunkHeight, chunkDepth);

                    // Dispatch the compute shader for this chunk
                    sdfComputeShader.Dispatch(kernelHandle, threadGroupsX, threadGroupsY, threadGroupsZ);
                }
            }
        }

        StartCoroutine(RenderingUtils.ConvertRenderTextureToTexture3D(sdfTexture, 2, TextureFormat.RHalf, TextureWrapMode.Repeat, FilterMode.Point, (Texture3D tex) =>
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
        #if UNITY_EDITOR
        AssetDatabase.CreateAsset(texture, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        #endif
    }

    private void Update()
    {
        if (compute)
        {
            StartCoroutine(ConvertVolumeToSDF_GPU());
            compute = false;
        }
    }
}
