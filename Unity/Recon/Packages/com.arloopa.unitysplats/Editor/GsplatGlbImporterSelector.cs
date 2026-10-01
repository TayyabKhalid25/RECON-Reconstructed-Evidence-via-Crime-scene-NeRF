// Copyright (c) 2026 ARLOOPA
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Gsplat.Editor
{
    /// <summary>Chooses the importer used for selected GLB files without claiming GLB globally.</summary>
    static class GsplatGlbImporterSelector
    {
        const string MenuPath = "Assets/UnitySplats/Choose GLB Importer...";

        [MenuItem(MenuPath, true)]
        static bool CanChooseImporter()
        {
            return GetSelectedGlbPaths().Count != 0;
        }

        [MenuItem(MenuPath, false, 1100)]
        static void ChooseImporter()
        {
            List<string> paths = GetSelectedGlbPaths();
            if (paths.Count == 0)
                return;

            string selection = paths.Count == 1
                ? $"'{Path.GetFileName(paths[0])}'"
                : $"the {paths.Count} selected GLB files";

            int choice = EditorUtility.DisplayDialogComplex(
                "Choose GLB Importer",
                $"Choose how Unity imports {selection}.\n\n" +
                "UnitySplats is for GLB files containing KHR_gaussian_splatting. " +
                "Project Default returns the files to the GLB importer supplied by another package. " +
                "If several other packages all claim GLB as their default, Unity still cannot choose between them.",
                "UnitySplats (Gaussian Splat)",
                "Cancel",
                "Project Default");

            if (choice == 1)
                return;

            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string path in paths)
                {
                    if (choice == 0)
                        AssetDatabase.SetImporterOverride<GsplatImporter>(path);
                    else
                        AssetDatabase.ClearImporterOverride(path);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            foreach (string path in paths)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (choice == 0)
            {
                var imported = new List<UnityEngine.Object>(paths.Count);
                foreach (string path in paths)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GsplatAsset>(path);
                    if (asset)
                        imported.Add(asset);
                }

                if (imported.Count != 0)
                    Selection.objects = imported.ToArray();
            }
        }

        static List<string> GetSelectedGlbPaths()
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(path) || !seen.Add(path))
                    continue;

                result.Add(path);
            }

            return result;
        }
    }
}
