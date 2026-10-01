// Manual test for LoadFromPlyBytes.
// Menu: Tools > Gsplat > Test LoadFromPlyBytes...
// Loads the same PLY via LoadFromPly(path) and LoadFromPlyBytes(bytes) for both
// compression modes and verifies the resulting assets are identical.

using System;
using System.IO;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Gsplat.Tests
{
    static class LoadFromPlyBytesManualTest
    {
        static int s_failures;

        [MenuItem("Tools/Gsplat/Test LoadFromPlyBytes...")]
        static void Run()
        {
            var path = EditorUtility.OpenFilePanel("Select a PLY file", "", "ply");
            if (string.IsNullOrEmpty(path)) return;

            s_failures = 0;
            var bytes = File.ReadAllBytes(path);

            try
            {
                TestUncompressed(path, bytes);
                TestSpark(path, bytes);
                TestErrorCases();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (s_failures == 0)
                Debug.Log("<color=green>LoadFromPlyBytes test: ALL PASSED</color>");
            else
                Debug.LogError($"LoadFromPlyBytes test: {s_failures} FAILURES");
        }

        static void Check(string name, bool cond)
        {
            if (cond)
                Debug.Log($"PASS {name}");
            else
            {
                Debug.LogError($"FAIL {name}");
                s_failures++;
            }
        }

        static void TestUncompressed(string path, byte[] bytes)
        {
            var fromFile = ScriptableObject.CreateInstance<GsplatAssetUncompressed>();
            var fromBytes = ScriptableObject.CreateInstance<GsplatAssetUncompressed>();
            try
            {
                fromFile.LoadFromPly(path);
                fromBytes.LoadFromPlyBytes(bytes);

                Check("uncompressed SplatCount", fromFile.SplatCount == fromBytes.SplatCount);
                Check("uncompressed SHBands", fromFile.SHBands == fromBytes.SHBands);
                Check("uncompressed Bounds", fromFile.Bounds == fromBytes.Bounds);
                CheckArray("uncompressed Positions", fromFile.Positions, fromBytes.Positions);
                CheckArray("uncompressed Colors", fromFile.Colors, fromBytes.Colors);
                CheckArray("uncompressed SHs", fromFile.SHs, fromBytes.SHs);
                CheckArray("uncompressed Scales", fromFile.Scales, fromBytes.Scales);
                CheckArray("uncompressed Rotations", fromFile.Rotations, fromBytes.Rotations);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fromFile);
                UnityEngine.Object.DestroyImmediate(fromBytes);
            }
        }

        static void TestSpark(string path, byte[] bytes)
        {
            var fromFile = ScriptableObject.CreateInstance<GsplatAssetSpark>();
            var fromBytes = ScriptableObject.CreateInstance<GsplatAssetSpark>();
            try
            {
                fromFile.LoadFromPly(path);
                fromBytes.LoadFromPlyBytes(bytes);

                Check("spark SplatCount", fromFile.SplatCount == fromBytes.SplatCount);
                Check("spark SHBands", fromFile.SHBands == fromBytes.SHBands);
                Check("spark Bounds", fromFile.Bounds == fromBytes.Bounds);
                CheckArray("spark PackedSplats", fromFile.PackedSplats, fromBytes.PackedSplats);
                CheckArray("spark PackedSH1", fromFile.PackedSH1, fromBytes.PackedSH1);
                CheckArray("spark PackedSH2", fromFile.PackedSH2, fromBytes.PackedSH2);
                CheckArray("spark PackedSH3", fromFile.PackedSH3, fromBytes.PackedSH3);
                CheckArray("spark PackedSH4", fromFile.PackedSH4, fromBytes.PackedSH4);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fromFile);
                UnityEngine.Object.DestroyImmediate(fromBytes);
            }
        }

        static void TestErrorCases()
        {
            var uncompressed = ScriptableObject.CreateInstance<GsplatAssetUncompressed>();
            var spz = ScriptableObject.CreateInstance<GsplatAssetSpz>();
            var spzUncompressed = ScriptableObject.CreateInstance<GsplatAssetSpzUncompressed>();
            try
            {
                Check("empty bytes throw ArgumentException",
                    Throws<ArgumentException>(() => uncompressed.LoadFromPlyBytes(Array.Empty<byte>())));
                Check("null bytes throw ArgumentException",
                    Throws<ArgumentException>(() => uncompressed.LoadFromPlyBytes(null)));
                Check("GsplatAssetSpz throws NotSupportedException",
                    Throws<NotSupportedException>(() => spz.LoadFromPlyBytes(new byte[] { 1 })));
                Check("GsplatAssetSpzUncompressed throws NotSupportedException",
                    Throws<NotSupportedException>(() => spzUncompressed.LoadFromPlyBytes(new byte[] { 1 })));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(uncompressed);
                UnityEngine.Object.DestroyImmediate(spz);
                UnityEngine.Object.DestroyImmediate(spzUncompressed);
            }
        }

        static bool Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (T)
            {
                return true;
            }
        }

        static void CheckArray<T>(string name, T[] a, T[] b) where T : IEquatable<T>
        {
            if (a == null || b == null)
            {
                Check(name, a == b);
                return;
            }

            if (a.Length != b.Length)
            {
                Check($"{name} length ({a.Length} vs {b.Length})", false);
                return;
            }

            for (var i = 0; i < a.Length; i++)
                if (!a[i].Equals(b[i]))
                {
                    Check($"{name} element {i} ({a[i]} vs {b[i]})", false);
                    return;
                }

            Check($"{name} ({a.Length} elements)", true);
        }

        static void CheckArray(string name, Vector3[] a, Vector3[] b) =>
            CheckArrayExact(name, a, b, (x, y) => x == y);

        static void CheckArray(string name, Vector4[] a, Vector4[] b) =>
            CheckArrayExact(name, a, b, (x, y) => x == y);

        static void CheckArray(string name, uint4[] a, uint4[] b) =>
            CheckArrayExact(name, a, b, (x, y) => x.Equals(y));

        static void CheckArrayExact<T>(string name, T[] a, T[] b, Func<T, T, bool> equals)
        {
            if (a == null || b == null)
            {
                Check(name, ReferenceEquals(a, b));
                return;
            }

            if (a.Length != b.Length)
            {
                Check($"{name} length ({a.Length} vs {b.Length})", false);
                return;
            }

            for (var i = 0; i < a.Length; i++)
                if (!equals(a[i], b[i]))
                {
                    Check($"{name} element {i} ({a[i]} vs {b[i]})", false);
                    return;
                }

            Check($"{name} ({a.Length} elements)", true);
        }
    }
}
