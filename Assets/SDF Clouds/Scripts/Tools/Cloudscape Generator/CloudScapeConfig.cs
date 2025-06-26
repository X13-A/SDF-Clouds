using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CloudScapeConfig : MonoBehaviour
{
    [Header("General parameters")]
    [SerializeField] private string configName = "New config";
    public string ConfigName => configName;

    [SerializeField] public int width = 50;
    [SerializeField] public int depth = 50;
    [SerializeField] public int height = 50;

    [Header("Terrain parameters")]
    [SerializeField] public uint cloudsSeed;
    [SerializeField] public Vector3 cloudsScale = new Vector2(3.33f, 3.33f);
    [SerializeField] public Vector3 cloudsOffset = new Vector2();
    [SerializeField][Range(0.0f, 1.0f)] public float cloudsThreshold;
    [SerializeField][Range(0.0f, 1000f)] public float borderAttenuation;
}