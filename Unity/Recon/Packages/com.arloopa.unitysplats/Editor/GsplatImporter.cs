// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Gsplat.Editor
{
    // GLB is deliberately an override extension. General-purpose glTF packages commonly
    // register their own GLB importer, and Unity rejects every importer when more than one
    // ScriptedImporter claims the same extension as its default. Users can select this
    // importer per GLB from the Inspector or Assets > UnitySplats > Choose GLB Importer.
    [ScriptedImporter(3, new[] { "ply", "sog", "spz" }, new[] { "glb" })]
    public class GsplatImporter : ScriptedImporter
    {
        public CompressionMode Compression = CompressionMode.Spark;

        [Tooltip("The coordinate frame the source asset was authored in.\n\n" +
                 "Positions, rotations, and SH coefficients are converted to Unity (RUF) at import time.\n\n" +
                 "RUB  — standard output of 3DGS training tools, gsplat, nerfstudio, and Niantic SPZ.\n" +
                 "RDF  — OpenCV, COLMAP camera convention.\n" +
                 "LUF  — GLB, glTF.\n" +
                 "RUF  — already in Unity space; no conversion applied.")]
        public SourceCoordinates SourceCoordinates = SourceCoordinates.Unspecified;

        public override void OnImportAsset(AssetImportContext ctx)
        {
#if GSPLAT_VERBOSE_IMPORT_LOGGING
            var swTotal = System.Diagnostics.Stopwatch.StartNew();
#endif
            GsplatAsset gsplatAsset;
            try
            {
                ProgressCallback progress = (info, p) => EditorUtility.DisplayProgressBar(
                    "Importing Gsplat Asset", info, p);
                gsplatAsset = GsplatRuntimeLoader.LoadFile(
                    ctx.assetPath, Compression, SourceCoordinates, progress);
            }
            catch (Exception e)
            {
                if (GsplatSettings.Instance.ShowImportErrors)
                {
                    UnityEngine.Debug.LogError($"{ctx.assetPath} import error:");
                    UnityEngine.Debug.LogException(e);
                }

                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
#if GSPLAT_VERBOSE_IMPORT_LOGGING
                swTotal?.Stop();
#endif
            }

            ctx.AddObjectToAsset("gsplatAsset", gsplatAsset);
            ctx.SetMainObject(gsplatAsset);

#if GSPLAT_VERBOSE_IMPORT_LOGGING
            LogImportStats(ctx.assetPath, gsplatAsset, swTotal.ElapsedMilliseconds);
#endif
        }

        static void LogImportStats(string assetPath, GsplatAsset asset, long totalMs)
        {
            uint n = asset.SplatCount;
            long fileBytes = new FileInfo(assetPath).Length;
            double usPerSplat = n > 0 ? totalMs * 1000.0 / n : 0;
            string name = Path.GetFileName(assetPath);
            string fileMb = $"{fileBytes / 1048576.0:F2} MB";

            UnityEngine.Debug.Log(
                $"[Gsplat Import] {name}: {n:N0} splats, SH bands {asset.SHBands}, {fileMb} | " +
                $"total {totalMs} ms ({usPerSplat:F2} µs/splat)");
        }
    }


    public class GsplatReferenceRestorer : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            var reimported = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in importedAssets)
                if (p.EndsWith(".ply", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".sog", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".spz", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                    reimported.Add(p);

            if (reimported.Count == 0) return;

            var renderers = UnityEngine.Object.FindObjectsByType<GsplatRenderer>(FindObjectsSortMode.None);
            foreach (var renderer in renderers)
            {
                if (string.IsNullOrEmpty(renderer.AssetGuid)) continue;
                var path = AssetDatabase.GUIDToAssetPath(renderer.AssetGuid);
                if (string.IsNullOrEmpty(path) || !reimported.Contains(path)) continue;

                // Always reload — even if the reference is still live the GPU buffers
                // need to be refreshed whenever the asset is reimported.
                var asset = AssetDatabase.LoadAssetAtPath<GsplatAsset>(path);
                if (!asset) continue;
                renderer.GsplatAsset = asset;
                renderer.ReloadAsset();
                EditorUtility.SetDirty(renderer);
            }
        }
    }
}
