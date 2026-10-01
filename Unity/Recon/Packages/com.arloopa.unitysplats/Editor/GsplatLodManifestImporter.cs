// Copyright (c) 2026 ARLOOPA
// SPDX-License-Identifier: MIT

#if false // Temporarily disabled: lod-meta.json editor import and creation menus.

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Gsplat.Editor
{
    /// <summary>Safe basename-specific import path that does not claim every .json in the project.</summary>
    static class GsplatLodManifestImporter
    {
        const string ImportMenu = "Assets/UnitySplats/Import selected lod-meta.json";

        [MenuItem(ImportMenu, true)]
        static bool CanImport()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return string.Equals(Path.GetFileName(path), "lod-meta.json", StringComparison.OrdinalIgnoreCase);
        }

        [MenuItem(ImportMenu)]
        static void Import()
        {
            string sourceAssetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            string sourceFullPath = Path.GetFullPath(sourceAssetPath);
            string json = File.ReadAllText(sourceFullPath);
            string baseUri = BuildBaseUri(sourceAssetPath, sourceFullPath);

            try
            {
                // Validate before creating or changing an asset.
                GsplatLodManifest.Parse(json, SourceCoordinates.RUB);
                string outputPath = BuildOutputPath(sourceAssetPath);
                EnsureAssetFolder(Path.GetDirectoryName(outputPath)?.Replace('\\', '/'));
                var asset = AssetDatabase.LoadAssetAtPath<GsplatLodManifestAsset>(outputPath);
                if (!asset)
                {
                    UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(outputPath);
                    if (existing)
                        throw new InvalidOperationException(
                            $"'{outputPath}' already contains {existing.GetType().Name}, not a GsplatLodManifestAsset.");
                    asset = ScriptableObject.CreateInstance<GsplatLodManifestAsset>();
                    AssetDatabase.CreateAsset(asset, outputPath);
                }
                asset.Initialize(json, baseUri, SourceCoordinates.RUB);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);

                if (!sourceAssetPath.Replace('\\', '/').StartsWith("Assets/StreamingAssets/",
                        StringComparison.OrdinalIgnoreCase))
                    Debug.LogWarning(
                        $"[Gsplat LOD] Imported with editor-local base path '{baseUri}'. For players, keep the manifest directory under Assets/StreamingAssets or configure a remote URI.", asset);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Could not import '{sourceAssetPath}' as a Gaussian-splat LOD manifest: {exception.Message}");
                throw;
            }
        }

        [MenuItem("GameObject/UnitySplats/LOD Streamer", false, 10)]
        static void CreateStreamer(MenuCommand command)
        {
            var gameObject = new GameObject("Gaussian Splat LOD Streamer");
            GameObjectUtility.SetParentAndAlign(gameObject, command.context as GameObject);
            var streamer = gameObject.AddComponent<GsplatLodStreamer>();
            if (Selection.activeObject is GsplatLodManifestAsset manifest)
                streamer.ManifestAsset = manifest;
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Gaussian Splat LOD Streamer");
            Selection.activeGameObject = gameObject;
        }

        static string BuildBaseUri(string assetPath, string fullPath)
        {
            string normalized = assetPath.Replace('\\', '/');
            const string streamingRoot = "Assets/StreamingAssets/";
            if (normalized.StartsWith(streamingRoot, StringComparison.OrdinalIgnoreCase))
            {
                string relativeDirectory = Path.GetDirectoryName(normalized.Substring(streamingRoot.Length))
                    ?.Replace('\\', '/').Trim('/') ?? string.Empty;
                return string.IsNullOrEmpty(relativeDirectory)
                    ? GsplatLodUri.StreamingAssetsScheme
                    : GsplatLodUri.StreamingAssetsScheme + relativeDirectory;
            }
            return Path.GetDirectoryName(fullPath)?.Replace('\\', '/') ?? string.Empty;
        }

        static string BuildOutputPath(string sourceAssetPath)
        {
            string normalized = sourceAssetPath.Replace('\\', '/');
            const string streamingRoot = "Assets/StreamingAssets/";
            if (!normalized.StartsWith(streamingRoot, StringComparison.OrdinalIgnoreCase))
                return Path.ChangeExtension(normalized, ".asset").Replace('\\', '/');

            // Unity treats StreamingAssets as raw player data and does not permit native
            // ScriptableObject assets inside it. Mirror the relative converter directory in
            // a normal Assets folder while the asset's base URI remains streaming-assets://.
            string relative = normalized.Substring(streamingRoot.Length);
            return ("Assets/UnitySplats/LodManifests/" + Path.ChangeExtension(relative, ".asset"))
                .Replace('\\', '/');
        }

        static void EnsureAssetFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || AssetDatabase.IsValidFolder(folder)) return;
            string[] segments = folder.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; ++i)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }
    }
}
#endif
