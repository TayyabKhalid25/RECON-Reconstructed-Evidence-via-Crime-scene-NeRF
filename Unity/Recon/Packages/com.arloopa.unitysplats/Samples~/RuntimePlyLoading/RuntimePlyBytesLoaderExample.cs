// Runtime loading example for UnitySplats.
// SPDX-License-Identifier: MIT

using System.IO;
using UnityEngine;

namespace Gsplat.Samples
{
    [RequireComponent(typeof(GsplatRenderer))]
    public sealed class RuntimePlyBytesLoaderExample : MonoBehaviour
    {
        [Tooltip("Absolute path, or relative to StreamingAssets")]
        public string PlyPath;

        public CompressionMode Compression = CompressionMode.Spark;
        public SourceCoordinates SourceCoordinates = SourceCoordinates.RUB;

        GsplatAsset m_runtimeAsset;

        void Start()
        {
            var path = Path.IsPathRooted(PlyPath)
                ? PlyPath
                : Path.Combine(Application.streamingAssetsPath, PlyPath);
            if (!File.Exists(path))
            {
                Debug.LogError($"PLY not found: {path}");
                return;
            }

            // Simulates data arriving as a byte array (e.g. downloaded at runtime).
            var bytes = File.ReadAllBytes(path);

            m_runtimeAsset = Compression == CompressionMode.Spark
                ? ScriptableObject.CreateInstance<GsplatAssetSpark>()
                : ScriptableObject.CreateInstance<GsplatAssetUncompressed>();
            m_runtimeAsset.LoadFromPlyBytes(bytes, null, SourceCoordinates);

            GetComponent<GsplatRenderer>().GsplatAsset = m_runtimeAsset;
            Debug.Log($"Loaded {m_runtimeAsset.SplatCount} splats (SH bands {m_runtimeAsset.SHBands}) " +
                      $"from {bytes.Length} bytes via LoadFromPlyBytes");
        }

        void OnDestroy()
        {
            if (!m_runtimeAsset)
                return;

            var renderer = GetComponent<GsplatRenderer>();
            if (renderer && renderer.GsplatAsset == m_runtimeAsset)
                renderer.GsplatAsset = null;
            Destroy(m_runtimeAsset);
        }
    }
}
