using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

// GPU parallelism boosts performance by ~500% (Ryzen 9 7940HS - RTX 4050 60W)
[ExecuteInEditMode]
public class Noise3DGenerator : MonoBehaviour
{
    private enum Type { Texture3D, Texture2D };

    [Header("Perlin noise")]
    [SerializeField] private string perlinOutputPath;
    [SerializeField] private bool computePerlin;

    [SerializeField] private ComputeShader perlinCompute;
    [SerializeField] private Vector3 perlinScale;
    [SerializeField] private Vector3 perlinOffset;
    [SerializeField] private int perlinResolution;
    [SerializeField] private int perlinSeed;
    [SerializeField] private int perlinOctaves;
    [SerializeField] private float perlinAttenuation;

    [Header("Worley noise")]
    [SerializeField] private string worleyOutputPath;
    [SerializeField] private bool computeWorley;
    [SerializeField] private Type textureType;

    [SerializeField] private ComputeShader worleyCompute3D;
    [SerializeField] private ComputeShader worleyCompute2D;
    [SerializeField] private int worleyResolution;
    [SerializeField] private int worleyPoints;
    [SerializeField] private float worleyAttenuation = 0.5f;
    [SerializeField] private float worleyRadius = 10;
    [SerializeField] private int worleyOctaves = 8;
    [SerializeField] private bool worleyUseGrid;
    [SerializeField] private int worleyGridSize;

    #region Perlin
    /// <summary>
    /// Generates Perlin Noise asynchronously on the GPU
    /// </summary>
    /// <param name="outputPath">Must be the path & name of the file WITHOUT extension</param>
    public void Generate3DPerlin_GPU(int resolution, Vector3 scale, Vector3 offset, int seed, string outputPath)
    {
        int computeKernel = perlinCompute.FindKernel("CSMain");
        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();
        UnityEngine.Debug.Log("Starting noise generation (GPU)...");

        // Create a RenderTexture with 3D support and enable random write
        RenderTextureDescriptor renderDesc = new RenderTextureDescriptor
        {
            width = resolution,
            height = resolution,
            volumeDepth = resolution,
            dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
            enableRandomWrite = true,
            graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
            msaaSamples = 1
        };

        // Create the new texture based on the descriptor
        RenderTexture resRenderTex = new RenderTexture(renderDesc);
        resRenderTex.Create();
        // Bind the texture to the compute shader
        perlinCompute.SetTexture(computeKernel, "Result", resRenderTex);
        perlinCompute.SetInts("ResultRes", new int[] { resolution, resolution, resolution });
        perlinCompute.SetFloats("Scale", new float[] { scale.x, scale.y, scale.z });
        perlinCompute.SetFloats("Offset", new float[] { offset.x, offset.y, offset.z });
        perlinCompute.SetInt("Seed", seed);
        perlinCompute.SetInt("Levels", perlinOctaves);
        perlinCompute.SetFloat("Attenuation", perlinAttenuation);

        // Dispatch the compute shader
        int threadGroupsX = Mathf.CeilToInt(resolution / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(resolution / 8.0f);
        int threadGroupsZ = Mathf.CeilToInt(resolution / 8.0f);
        perlinCompute.Dispatch(computeKernel, threadGroupsX, threadGroupsY, threadGroupsZ);

        stopwatch.Stop();
        float generationTime = (float)stopwatch.Elapsed.TotalSeconds;
        UnityEngine.Debug.Log($"Noise generated in {generationTime} seconds, converting it to readable Texture3D...");
        stopwatch.Restart();

        Texture3D resTexture;
        // Convert RenderTexture to Texture3D.
        StartCoroutine(RenderingUtils.ConvertRenderTextureToTexture3D(resRenderTex, 1, TextureFormat.R8, TextureWrapMode.Repeat, FilterMode.Bilinear, (Texture3D tex) =>
        {
            stopwatch.Stop();
            float conversionTime = (float)stopwatch.Elapsed.TotalSeconds;
            UnityEngine.Debug.Log($"Conversion done in {conversionTime} seconds!");
            resTexture = tex;
            resRenderTex.Release();
            #if UNITY_EDITOR
                AssetDatabase.CreateAsset(tex, outputPath + ".asset");
            #endif
            UnityEngine.Debug.Log($"Saved 3D texture at: {outputPath}.asset");
        }));
    }
    #endregion

    #region Worley
    /// <summary>
    /// Generates Worley Noise asynchronously on the GPU
    /// </summary>
    /// <param name="outputPath">Must be the path & name of the file WITHOUT extension</param>
    public void Generate3DWorley_GPU(int resolution, List<Vector3> points, float attenuation, float radius, int octaves, string outputPath)
    {
        RenderTexture resRenderTex = ComputeWorleyTexture3D(points, resolution, attenuation, radius, octaves);
        Texture3D resTexture;
        // Convert RenderTexture to Texture3D.
        StartCoroutine(RenderingUtils.ConvertRenderTextureToTexture3D(resRenderTex, 1, TextureFormat.R8, TextureWrapMode.Repeat, FilterMode.Bilinear, (Texture3D tex) =>
        {
            resTexture = tex;
            resRenderTex.Release();
            #if UNITY_EDITOR
                AssetDatabase.CreateAsset(tex, outputPath + ".asset");
            #endif
            UnityEngine.Debug.Log($"Saved 3D texture at: {outputPath}.asset");
        }));
    }

    /// <summary>
    /// Generates Worley Noise asynchronously on the GPU
    /// </summary>
    /// <param name="outputPath">Must be the path & name of the file WITHOUT extension</param>
    public void Generate2DWorley_GPU(int resolution, List<Vector3> points, float attenuation, float radius, int octaves, string outputPath)
    {
        RenderTexture resRenderTex = ComputeWorleyTexture2D(points, resolution, attenuation, radius, octaves);
        Texture2D resTexture;
        // Convert RenderTexture to Texture3D.
        StartCoroutine(RenderingUtils.ConvertRenderTextureToTexture2D(resRenderTex, 1, TextureFormat.R8, TextureWrapMode.Repeat, FilterMode.Bilinear, (Texture2D tex) =>
        {
            resTexture = tex;
            resRenderTex.Release();
            #if UNITY_EDITOR
                AssetDatabase.CreateAsset(tex, outputPath + ".asset");
            #endif
            UnityEngine.Debug.Log($"Saved 2D texture at: {outputPath}.asset");
        }));
    }

    public RenderTexture ComputeWorleyTexture3D(List<Vector3> worleyPoints, int textureSize, float attenuation, float radius, int octaves)
    {
        int computeKernel = worleyCompute3D.FindKernel("CSMain");

        // Create a RenderTexture with 3D support and enable random write
        RenderTextureDescriptor renderDesc = new RenderTextureDescriptor
        {
            width = textureSize,
            height = textureSize,
            volumeDepth = textureSize,
            dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
            enableRandomWrite = true,
            graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
            msaaSamples = 1,
        };

        RenderTexture resRenderTex = new RenderTexture(renderDesc);
        resRenderTex.Create();

        int bufferStride = sizeof(float) * 3; // 3 floats for Vector3
        ComputeBuffer pointsBuffer = new ComputeBuffer(worleyPoints.Count, bufferStride);
        pointsBuffer.SetData(worleyPoints);

        worleyCompute3D.SetTexture(computeKernel, "Result", resRenderTex);
        worleyCompute3D.SetBuffer(computeKernel, "pointsBuffer", pointsBuffer);
        worleyCompute3D.SetInt("pointsBufferLength", worleyPoints.Count);
        worleyCompute3D.SetInts("textureDimensions", textureSize, textureSize, textureSize);
        worleyCompute3D.SetFloat("attenuation", attenuation);
        worleyCompute3D.SetFloat("radius", radius);
        worleyCompute3D.SetInt("octaves", octaves);

        worleyCompute3D.Dispatch(computeKernel, textureSize / 8, textureSize / 8, textureSize / 8);

        pointsBuffer.Release();
        return resRenderTex;
    }

    public RenderTexture ComputeWorleyTexture2D(List<Vector3> worleyPoints, int textureSize, float attenuation, float radius, int octaves)
    {
        int computeKernel = worleyCompute2D.FindKernel("CSMain");

        // Create a RenderTexture with 2D support and enable random write
        RenderTextureDescriptor renderDesc = new RenderTextureDescriptor
        {
            width = textureSize,
            height = textureSize,
            volumeDepth = 1,
            dimension = UnityEngine.Rendering.TextureDimension.Tex2D,
            enableRandomWrite = true,
            graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
            msaaSamples = 1,
        };

        RenderTexture resRenderTex = new RenderTexture(renderDesc);
        resRenderTex.Create();

        int bufferStride = sizeof(float) * 3; // 3 floats for Vector3
        ComputeBuffer pointsBuffer = new ComputeBuffer(worleyPoints.Count, bufferStride);
        pointsBuffer.SetData(worleyPoints);

        worleyCompute2D.SetTexture(computeKernel, "Result", resRenderTex);
        worleyCompute2D.SetBuffer(computeKernel, "pointsBuffer", pointsBuffer);
        worleyCompute2D.SetInt("pointsBufferLength", worleyPoints.Count);
        worleyCompute2D.SetInts("textureDimensions", textureSize, textureSize, textureSize);
        worleyCompute2D.SetFloat("attenuation", attenuation);
        worleyCompute2D.SetFloat("radius", radius);
        worleyCompute2D.SetInt("octaves", octaves);

        worleyCompute2D.Dispatch(computeKernel, textureSize / 8, textureSize / 8, 1);

        pointsBuffer.Release();
        return resRenderTex;
    }

    public List<Vector3> CreateWorleyPointsGrid(int gridSize)
    {
        List<Vector3> points = new List<Vector3>();

        float cellSize = 1.0f / gridSize;
        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                for (int z = 0; z < gridSize; z++)
                {
                    Vector3 randomOffset = new Vector3(
                        UnityEngine.Random.value * cellSize,
                        UnityEngine.Random.value * cellSize,
                        UnityEngine.Random.value * cellSize
                    );
                    Vector3 cellCorner = new Vector3(x, y, z) * cellSize;
                    Vector3 point = cellCorner + randomOffset;
                    points.Add(point);
                }
            }
        }
        return points;
    }

    public List<Vector3> CreateWorleyPoints(int n)
    {
        List<Vector3> points = new List<Vector3>();
        for (int i = 0; i < n; i++)
        {
            points.Add(new Vector3(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value));
        }
        return points;
    }

    public List<Vector3> RepeatWorleyPoints(List<Vector3> points)
    {
        List<Vector3> repeatedPoints = new List<Vector3>();
        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                for (int z = 0; z < 3; z++)
                {
                    Vector3 offset = new Vector3(x, y, z);
                    foreach (Vector3 point in points)
                    {
                        repeatedPoints.Add(point + offset - Vector3.one);
                    }
                }
            }
        }
        return repeatedPoints;
    }
    #endregion

    private void Update()
    {
        if (computePerlin)
        {
            Generate3DPerlin_GPU(perlinResolution, perlinScale, perlinOffset, perlinSeed, perlinOutputPath);
            computePerlin = false;
        }
        if (computeWorley)
        {
            List<Vector3> points;
            if (worleyUseGrid)
            {
                points = CreateWorleyPointsGrid(worleyGridSize);
            }
            else
            {
                points = CreateWorleyPoints(worleyPoints);
            }
            points = RepeatWorleyPoints(points); // Make texture tile

            if (textureType == Type.Texture3D)
            {
                Generate3DWorley_GPU(worleyResolution, points, worleyAttenuation, worleyRadius, worleyOctaves, worleyOutputPath);
            }
            else
            {
                Generate2DWorley_GPU(worleyResolution, points, worleyAttenuation, worleyRadius, worleyOctaves, worleyOutputPath);
            }

            computeWorley = false;
        }
    }
}
