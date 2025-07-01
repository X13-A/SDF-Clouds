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
public class WorleyNoiseGenerator : MonoBehaviour
{
    [Header("Compute shader")]
    [SerializeField] private ComputeShader worleyComputeShader;

    [Header("Actions")]
    [SerializeField] private string outputPath = "Assets/worley3D";
    [SerializeField] private bool compute = false;

    [Header("Noise settings")]
    [SerializeField] private Vector3Int resolution = new Vector3Int(256, 32, 256);
    [SerializeField] private int points = 150;
    [SerializeField] private float attenuation = 0.5f;
    [SerializeField] private float radius = 10;
    [SerializeField] private int octaves = 6;
    [SerializeField] private bool useGrid = false;
    [SerializeField] private int gridSize = 0;

    [Header("Vertical attenuation")]
    [SerializeField] private bool attenuateVertically = true;
    [SerializeField] private float attenuateTopExponent = 512;
    [SerializeField] private float attenuateBottomExponent = 512;

    #region Worley
    /// <summary>
    /// Generates Worley Noise asynchronously on the GPU
    /// </summary>
    /// <param name="outputPath">Must be the path & name of the file WITHOUT extension</param>
    public void Generate3DWorley_GPU(Vector3Int resolution, List<Vector3> points, float attenuation, float radius, int octaves, string outputPath)
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

    public RenderTexture ComputeWorleyTexture3D(List<Vector3> worleyPoints, Vector3Int textureSize, float attenuation, float radius, int octaves)
    {
        int computeKernel = worleyComputeShader.FindKernel("CSMain");

        // Create a RenderTexture with 3D support and enable random write
        RenderTextureDescriptor renderDesc = new RenderTextureDescriptor
        {
            width = textureSize.x,
            height = textureSize.y,
            volumeDepth = textureSize.z,
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

        worleyComputeShader.SetTexture(computeKernel, "Result", resRenderTex);
        worleyComputeShader.SetBuffer(computeKernel, "pointsBuffer", pointsBuffer);
        worleyComputeShader.SetInt("pointsBufferLength", worleyPoints.Count);
        worleyComputeShader.SetInts("textureDimensions", textureSize.x, textureSize.y, textureSize.z);
        worleyComputeShader.SetFloat("attenuation", attenuation);
        worleyComputeShader.SetFloat("radius", radius);
        worleyComputeShader.SetInt("octaves", octaves);
        worleyComputeShader.SetBool("attenuate_vertically", attenuateVertically);
        worleyComputeShader.SetFloat("attenuate_top_exponent", attenuateTopExponent);
        worleyComputeShader.SetFloat("attenuate_bottom_exponent", attenuateBottomExponent);


        worleyComputeShader.Dispatch(computeKernel, textureSize.x / 8, textureSize.y / 8, textureSize.z / 8);

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
        if (compute)
        {
            List<Vector3> points;
            if (useGrid)
            {
                points = CreateWorleyPointsGrid(gridSize);
            }
            else
            {
                points = CreateWorleyPoints(this.points);
            }
            points = RepeatWorleyPoints(points); // Make texture tile
            
            Generate3DWorley_GPU(resolution, points, attenuation, radius, octaves, outputPath);
            
            compute = false;
        }
    }
}
