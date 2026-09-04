using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Recon.Editor
{
    /// <summary>
    /// Batch-mode Android build, driven by Unity/build-android.ps1:
    ///   Unity.exe -batchmode -nographics -projectPath Unity/Recon -buildTarget Android
    ///             -executeMethod Recon.Editor.BuildScript.BuildAndroid -outputPath Builds/Recon.apk [-development]
    /// Builds every scene enabled in EditorBuildSettings. Exit code 0 on success, 1 on failure,
    /// so the script and CI can tell the difference without parsing the log.
    /// </summary>
    public static class BuildScript
    {
        public static void BuildAndroid()
        {
            try
            {
                string output = Arg("-outputPath")
                                ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Recon.apk"));
                bool development = HasFlag("-development");

                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                if (scenes.Length == 0) throw new BuildFailedException("No enabled scenes in EditorBuildSettings. Run Recon → Bootstrap ReconAR scene first.");

                Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");

                EditorUserBuildSettings.buildAppBundle = false;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = output,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = development ? (BuildOptions.Development | BuildOptions.ConnectWithProfiler) : BuildOptions.None,
                };

                Debug.Log($"[Recon] Building Android ({(development ? "development" : "release")}) → {output}\n  scenes: {string.Join(", ", scenes)}");
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary s = report.summary;
                Debug.Log($"[Recon] Build {s.result}: {s.totalSize / 1048576.0:F1} MB in {s.totalTime.TotalSeconds:F0} s, " +
                          $"{s.totalErrors} errors, {s.totalWarnings} warnings → {s.outputPath}");

                if (s.result != BuildResult.Succeeded)
                    throw new BuildFailedException($"Build result {s.result} with {s.totalErrors} errors; see the log.");

                Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[Recon] Build failed: " + e.Message);
                Exit(1);
                throw;
            }
        }

        static void Exit(int code)
        {
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        static bool HasFlag(string name) =>
            Environment.GetCommandLineArgs().Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    }
}
