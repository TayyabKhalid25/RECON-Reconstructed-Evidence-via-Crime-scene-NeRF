// Copyright (c) 2026 ARLOOPA
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Gsplat.Editor
{
    static class GsplatUnpackedSogImporter
    {
        const string k_MenuRoot = "Assets/UnitySplats/";

        [MenuItem(k_MenuRoot + "Import unpacked SOG (Spark)")]
        static void ImportSpark() => Import(CompressionMode.Spark);

        [MenuItem(k_MenuRoot + "Import unpacked SOG (Uncompressed)")]
        static void ImportUncompressed() => Import(CompressionMode.Uncompressed);

        [MenuItem(k_MenuRoot + "Import unpacked SOG (Spark)", true)]
        [MenuItem(k_MenuRoot + "Import unpacked SOG (Uncompressed)", true)]
        static bool CanImport()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path) &&
                   Path.GetFileName(path).Equals("meta.json", StringComparison.OrdinalIgnoreCase);
        }

        static void Import(CompressionMode compression)
        {
            string metadataPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            string directory = Path.GetDirectoryName(metadataPath) ?? "Assets";
            string absoluteRoot = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) +
                                  Path.DirectorySeparatorChar;

            byte[] ResolveFile(string relativePath)
            {
                string absolutePath = Path.GetFullPath(Path.Combine(absoluteRoot, relativePath));
                if (!absolutePath.StartsWith(absoluteRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"SOG file '{relativePath}' resolves outside the metadata directory.");
                return File.ReadAllBytes(absolutePath);
            }

            try
            {
                string metadata = File.ReadAllText(metadataPath);
                ProgressCallback progress = (info, value) =>
                    EditorUtility.DisplayProgressBar("Importing unpacked SOG", info, value);
                GsplatAsset asset = GsplatRuntimeLoader.LoadUnpackedSog(
                    metadata, ResolveFile, compression, SourceCoordinates.RUB, progress);

                string folderName = new DirectoryInfo(directory).Name;
                string outputPath = AssetDatabase.GenerateUniqueAssetPath(
                    Path.Combine(directory, folderName + ".sog.asset").Replace('\\', '/'));
                asset.name = folderName;
                AssetDatabase.CreateAsset(asset, outputPath);
                AssetDatabase.SaveAssets();
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                Debug.Log($"Imported unpacked SOG as {outputPath}", asset);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("SOG import failed", exception.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
