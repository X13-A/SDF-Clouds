using UnityEngine;

[CreateAssetMenu(menuName = "Rendering/Cloud Settings")]
public class CloudsSettings : ScriptableObject
{
    [Header("Shaders & Compute")]
    public Shader cloudShader;
    public ComputeShader cloudComputeShader;
    public Material postProcessMaterial;

    [Header("Containers")]
    public Vector3 cloudsPosition;
    public Vector3 cloudsScale = Vector3.one;
    public Vector3 fogPosition;
    public Vector3 fogScale = Vector3.one;

    [Header("Shape")]
    public Texture3D sdfTexture;
    public Vector3 sdfTextureScale = Vector3.one;
    public Vector3 sdfTextureOffset = Vector3.zero;
    [Range(0, 5)] public float globalDensity = 0.25f;

    [Header("Erosion")]
    public bool useErosion;
    public Texture3D erosionTexture;
    [Range(0, 10000)] public float erosionTextureScale;
    [Range(0, 10000)] public float erosionWorldScale;
    [Range(0, 1)] public float erosionIntensity;
    public Vector3 erosionSpeed;

    [Header("Lighting")]
    [Range(0, 1)] public float sunlightAbsorption = 0.25f;
    [Range(0, 2)] public float lightMultiplier = 1.0f;
    [Range(0, 1)] public float directionalScattering = 0.5f;

    [Header("Shadow Parameters")]
    public Color shadowColor;
    [Range(0, 10)] public float shadowingOffset;

    [Header("Fog")]
    [Range(0, 10.0f)] public float fogDensity = 0.01f;
    [Range(0, 100000.0f)] public float fogDistance = 0;

    [Header("Quality Settings")]
    [Range(0, 1)] public float renderScale = 1.0f;
    [Range(0.001f, 500000f)] public float renderDistance = 150000f;
    [Range(1, 500)] public int cloudMaxSteps = 128;
    public float cloudMinStepSize = 15;
    public float fogStepSize = 200;

    [Header("Noise parameters")]
    public float offsetNoiseIntensity;

    [Header("Developer parameters")]
    public bool useSDFInsideClouds;
    [Range(-1000f, 1000f)] public float sdfThreshold = 0;

    public Vector3 CloudsBoundsMin => cloudsPosition - cloudsScale / 2;
    public Vector3 CloudsBoundsMax => cloudsPosition + cloudsScale / 2;
    public Vector3 FogBoundsMin => fogPosition - fogScale / 2;
    public Vector3 FogBoundsMax => fogPosition + fogScale / 2;
}
