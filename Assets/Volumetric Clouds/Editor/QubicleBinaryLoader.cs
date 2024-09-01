using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;

public class QubicleBinaryLoader : EditorWindow
{
    [MenuItem("Tools/Load Qubicle Binary")]
    public static void ShowWindow()
    {
        GetWindow<QubicleBinaryLoader>("Load Qubicle Binary");
    }

    private string inputPath = "";
    private string outputPath = "Assets/GeneratedTextures/QubicleTexture.asset";

    private void OnGUI()
    {
        GUILayout.Label("Load Qubicle Binary", EditorStyles.boldLabel);

        if (GUILayout.Button("Select Qubicle Binary File"))
        {
            inputPath = EditorUtility.OpenFilePanel("Load Qubicle Binary", "", "qb");
        }

        if (!string.IsNullOrEmpty(inputPath))
        {
            GUILayout.Label("Selected File: " + inputPath, EditorStyles.wordWrappedLabel);

            GUILayout.Label("Specify Output Path:", EditorStyles.boldLabel);
            outputPath = EditorGUILayout.TextField("Output Path", outputPath);

            if (GUILayout.Button("Load and Create 3D Texture"))
            {
                Texture3D texture = LoadQubicleBinary(inputPath);
                SaveTexture3D(texture, outputPath);
                inputPath = "";
            }
        }
    }

    private Texture3D LoadQubicleBinary(string filename)
    {
        using (BinaryReader reader = new BinaryReader(File.Open(filename, FileMode.Open)))
        {
            uint version = reader.ReadUInt32();
            uint colorFormat = reader.ReadUInt32();
            uint zAxisOrientation = reader.ReadUInt32();
            uint compressed = reader.ReadUInt32();
            uint visibilityMaskEncoded = reader.ReadUInt32();
            uint numMatrices = reader.ReadUInt32();

            int width = 0;
            int height = 0;
            int depth = 0;

            List<Color[]> matrixList = new List<Color[]>();

            for (uint i = 0; i < numMatrices; i++)
            {
                byte nameLength = reader.ReadByte();
                string name = new string(reader.ReadChars(nameLength));

                uint sizeX = reader.ReadUInt32();
                uint sizeY = reader.ReadUInt32();
                uint sizeZ = reader.ReadUInt32();

                int posX = reader.ReadInt32();
                int posY = reader.ReadInt32();
                int posZ = reader.ReadInt32();

                width = (int)sizeX;
                height = (int)sizeY;
                depth = (int)sizeZ;

                Color[] matrix = new Color[sizeX * sizeY * sizeZ];
                matrixList.Add(matrix);

                if (compressed == 0)
                {
                    for (uint z = 0; z < sizeZ; z++)
                    {
                        for (uint y = 0; y < sizeY; y++)
                        {
                            for (uint x = 0; x < sizeX; x++)
                            {
                                uint color = reader.ReadUInt32();
                                matrix[x + y * sizeX + z * sizeX * sizeY] = ColorFromUInt32(color);
                            }
                        }
                    }
                }
                else
                {
                    uint z = 0;
                    while (z < sizeZ)
                    {
                        z++;
                        uint index = 0;

                        while (true)
                        {
                            uint data = reader.ReadUInt32();
                            if (data == 6)
                                break;
                            else if (data == 2)
                            {
                                uint count = reader.ReadUInt32();
                                data = reader.ReadUInt32();
                                for (uint j = 0; j < count; j++)
                                {
                                    uint x = index % sizeX;
                                    uint y = index / sizeX;
                                    index++;
                                    matrix[x + y * sizeX + z * sizeX * sizeY] = ColorFromUInt32(data);
                                }
                            }
                            else
                            {
                                uint x = index % sizeX;
                                uint y = index / sizeX;
                                index++;
                                matrix[x + y * sizeX + z * sizeX * sizeY] = ColorFromUInt32(data);
                            }
                        }
                    }
                }
            }

            return CreateTexture3D(matrixList[0], width, height, depth);
        }
    }

    private Color ColorFromUInt32(uint color)
    {
        byte r = (byte)((color >> 24) & 0xFF);
        byte g = (byte)((color >> 16) & 0xFF);
        byte b = (byte)((color >> 8) & 0xFF);
        byte a = (byte)(color & 0xFF);
        return new Color32(r, g, b, a);
    }

    private Texture3D CreateTexture3D(Color[] voxelColors, int width, int height, int depth)
    {
        Texture3D texture = new Texture3D(width, height, depth, TextureFormat.RGBA32, false);
        texture.SetPixels(voxelColors);
        texture.Apply();
        return texture;
    }

    private void SaveTexture3D(Texture3D texture, string path)
    {
        AssetDatabase.CreateAsset(texture, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
