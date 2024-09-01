using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class RenderingUtils
{

    public static IEnumerator ConvertRenderTextureToTexture3D(RenderTexture rt3D, int texelSize, TextureFormat textureFormat, TextureWrapMode textureWrapMode, FilterMode filterMode, Action<Texture3D> onCompleted = null)
    {
        if (rt3D.dimension != UnityEngine.Rendering.TextureDimension.Tex3D)
        {
            UnityEngine.Debug.LogError("Provided RenderTexture is not a 3D volume.");
            yield break; // Exit the coroutine early if the dimension is incorrect
        }

        int width = rt3D.width;
        int height = rt3D.height;
        int depth = rt3D.volumeDepth;
        int byteSize = width * height * depth * texelSize;

        // Allocate a NativeArray in the temporary job memory which gets cleaned up automatically
        var voxelData = new NativeArray<byte>(byteSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        AsyncGPUReadbackRequest request = AsyncGPUReadback.RequestIntoNativeArray(ref voxelData, rt3D);

        // Wait for the readback to complete
        while (!request.done)
        {
            yield return null;
        }

        if (request.hasError)
        {
            UnityEngine.Debug.LogError("GPU readback error detected.");
            voxelData.Dispose();
            yield break;
        }

        // Create the Texture3D from readback data
        Texture3D outputTexture = new Texture3D(width, height, depth, textureFormat, false);
        outputTexture.filterMode = filterMode;
        outputTexture.anisoLevel = 0;
        outputTexture.SetPixelData(voxelData, 0);
        outputTexture.Apply(updateMipmaps: false);
        outputTexture.wrapMode = textureWrapMode;

        // Cleanup and trigger callback
        voxelData.Dispose();
        onCompleted?.Invoke(outputTexture);
    }

    public static IEnumerator ConvertRenderTextureToTexture2D(RenderTexture rt2D, int texelSize, TextureFormat textureFormat, TextureWrapMode textureWrapMode, FilterMode filterMode, Action<Texture2D> onCompleted = null)
    {
        if (rt2D.dimension != UnityEngine.Rendering.TextureDimension.Tex2D)
        {
            UnityEngine.Debug.LogError("Provided RenderTexture is not a 2D texture.");
            yield break; // Exit the coroutine early if the dimension is incorrect
        }

        int width = rt2D.width;
        int height = rt2D.height;
        int depth = 1;
        int byteSize = width * height * depth * texelSize;

        // Allocate a NativeArray in the temporary job memory which gets cleaned up automatically
        var voxelData = new NativeArray<byte>(byteSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        AsyncGPUReadbackRequest request = AsyncGPUReadback.RequestIntoNativeArray(ref voxelData, rt2D);

        // Wait for the readback to complete
        while (!request.done)
        {
            yield return null;
        }

        if (request.hasError)
        {
            UnityEngine.Debug.LogError("GPU readback error detected.");
            voxelData.Dispose();
            yield break;
        }

        // Create the Texture3D from readback data
        Texture2D outputTexture = new Texture2D(width, height, textureFormat, false);
        outputTexture.filterMode = filterMode;
        outputTexture.anisoLevel = 0;
        outputTexture.SetPixelData(voxelData, 0);
        outputTexture.Apply(updateMipmaps: false);
        outputTexture.wrapMode = textureWrapMode;

        // Cleanup and trigger callback
        voxelData.Dispose();
        onCompleted?.Invoke(outputTexture);
    }

    public static void DrawToTexture(Texture2D source, Texture2D target, Vector2 scale, Vector2 offset, Vector4 mask)
    {
        for (int y = 0; y < target.height; y++)
        {
            for (int x = 0; x < target.width; x++)
            {
                int u, v;
                u = (int) (x / scale.x + offset.x); 
                v = (int) (x / scale.y + offset.y); 
                Color sourceColor = Color.white * source.GetPixel(u, v) * mask;
                Color targetColor = target.GetPixel(x, y);
                target.SetPixel(x, y, targetColor + sourceColor);
            }
        }
    }
}
