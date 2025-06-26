using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class QubicleTextureExporter : EditorWindow
{
    [MenuItem("Tools/Export Texture3D to Qubicle")]
    public static void ShowWindow()
    {
        GetWindow<QubicleTextureExporter>("Export to Qubicle Binary");
    }

    private Texture3D sourceTexture;
    private string outputPath = "";
    private string matrixName = "VoxelMatrix";
    private bool useCompression = false;

    // Qubicle format constants
    private const uint VERSION = 0x00000101; // Version 1.1.0.0
    private const uint COLOR_FORMAT_RGBA = 0; // RGBA format
    private const uint Z_AXIS_LEFT_HANDED = 0; // Left-handed coordinate system (Unity default)
    private const uint VISIBILITY_MASK_ENCODED = 0; // No visibility mask

    private void OnGUI()
    {
        GUILayout.Label("Export Texture3D to Qubicle Binary", EditorStyles.boldLabel);

        // Texture3D selection
        sourceTexture = (Texture3D)EditorGUILayout.ObjectField("Source Texture3D", sourceTexture, typeof(Texture3D), false);

        if (sourceTexture != null)
        {
            // Display texture info
            GUILayout.Label($"Texture Dimensions: {sourceTexture.width} x {sourceTexture.height} x {sourceTexture.depth}", EditorStyles.helpBox);

            // Matrix name
            matrixName = EditorGUILayout.TextField("Matrix Name", matrixName);

            // Compression option
            useCompression = EditorGUILayout.Toggle("Use Compression", useCompression);

            // Output path
            if (GUILayout.Button("Select Output Path"))
            {
                string defaultName = sourceTexture.name + ".qb";
                outputPath = EditorUtility.SaveFilePanel("Save Qubicle Binary", "", defaultName, "qb");
            }

            if (!string.IsNullOrEmpty(outputPath))
            {
                GUILayout.Label("Output Path: " + outputPath, EditorStyles.wordWrappedLabel);

                if (GUILayout.Button("Export to Qubicle Binary"))
                {
                    ExportTexture3DToQubicle(sourceTexture, outputPath);
                }
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Please select a Texture3D to export.", MessageType.Info);
        }
    }

    private void ExportTexture3DToQubicle(Texture3D texture, string filename)
    {
        try
        {
            // Get all pixels from the texture
            Color[] pixels = texture.GetPixels();

            using (BinaryWriter writer = new BinaryWriter(File.Open(filename, FileMode.Create)))
            {
                // Write header
                writer.Write(VERSION);
                writer.Write(COLOR_FORMAT_RGBA);
                writer.Write(Z_AXIS_LEFT_HANDED);
                writer.Write(useCompression ? 1u : 0u);
                writer.Write(VISIBILITY_MASK_ENCODED);
                writer.Write(1u); // Number of matrices (we're exporting one texture as one matrix)

                // Write matrix header
                writer.Write((byte)matrixName.Length);
                writer.Write(matrixName.ToCharArray());

                // Write matrix dimensions
                writer.Write((uint)texture.width);
                writer.Write((uint)texture.height);
                writer.Write((uint)texture.depth);

                // Write matrix position (centered at origin)
                writer.Write(0); // posX
                writer.Write(0); // posY
                writer.Write(0); // posZ

                // Write voxel data
                if (!useCompression)
                {
                    WriteUncompressedData(writer, pixels, texture.width, texture.height, texture.depth);
                }
                else
                {
                    WriteCompressedData(writer, pixels, texture.width, texture.height, texture.depth);
                }
            }

            EditorUtility.DisplayDialog("Export Complete", $"Successfully exported texture to:\n{filename}", "OK");
            Debug.Log($"Exported Texture3D to Qubicle Binary: {filename}");
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("Export Failed", $"Failed to export texture:\n{e.Message}", "OK");
            Debug.LogError($"Failed to export Texture3D: {e}");
        }
    }

    private void WriteUncompressedData(BinaryWriter writer, Color[] pixels, int width, int height, int depth)
    {
        // Write voxels in z-y-x order (Qubicle format)
        for (int z = 0; z < depth; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = x + y * width + z * width * height;
                    uint colorValue = ColorToUInt32(pixels[index]);
                    writer.Write(colorValue);
                }
            }
        }
    }

    private void WriteCompressedData(BinaryWriter writer, Color[] pixels, int width, int height, int depth)
    {
        // RLE compression for each z-slice
        for (int z = 0; z < depth; z++)
        {
            List<uint> sliceData = new List<uint>();

            // Collect all colors in this z-slice
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = x + y * width + z * width * height;
                    sliceData.Add(ColorToUInt32(pixels[index]));
                }
            }

            // Perform RLE compression
            int i = 0;
            while (i < sliceData.Count)
            {
                uint currentColor = sliceData[i];
                int runLength = 1;

                // Count consecutive identical colors
                while (i + runLength < sliceData.Count && sliceData[i + runLength] == currentColor)
                {
                    runLength++;
                }

                if (runLength > 1)
                {
                    // Write RLE marker (2), count, and color
                    writer.Write(2u);
                    writer.Write((uint)runLength);
                    writer.Write(currentColor);
                }
                else
                {
                    // Write single color
                    writer.Write(currentColor);
                }

                i += runLength;
            }

            // Write end-of-slice marker
            writer.Write(6u);
        }
    }

    private uint ColorToUInt32(Color color)
    {
        // Convert Unity Color to RGBA uint32 (Qubicle format: RGBA)
        byte r = (byte)(Mathf.Clamp01(color.r) * 255);
        byte g = (byte)(Mathf.Clamp01(color.g) * 255);
        byte b = (byte)(Mathf.Clamp01(color.b) * 255);
        byte a = (byte)(Mathf.Clamp01(color.a) * 255);

        return ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | (uint)a;
    }
}