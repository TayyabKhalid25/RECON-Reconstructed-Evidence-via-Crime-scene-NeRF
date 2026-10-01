// Copyright (c) 2026 ARLOOPA
// SPDX-License-Identifier: MIT

#if false // Temporarily disabled: lod-meta.json support is not part of the current public release.

using System;
using UnityEngine;

namespace Gsplat
{
    /// <summary>Unity asset wrapper for validated PlayCanvas <c>lod-meta.json</c> data.</summary>
    [CreateAssetMenu(fileName = "Gsplat LOD Manifest", menuName = "UnitySplats/LOD Manifest")]
    public sealed class GsplatLodManifestAsset : ScriptableObject
    {
        [SerializeField, TextArea(4, 12)] string m_json;
        [SerializeField, Tooltip("Directory or absolute URI used to resolve manifest filenames.")]
        string m_baseUri;
        [SerializeField] SourceCoordinates m_sourceCoordinates = SourceCoordinates.RUB;

        [NonSerialized] GsplatLodManifest m_manifest;

        public string Json => m_json;
        public string BaseUri => m_baseUri;
        public SourceCoordinates SourceCoordinates => m_sourceCoordinates;
        public GsplatLodManifest Manifest => m_manifest ??= GsplatLodManifest.Parse(m_json, m_sourceCoordinates);

        public void Initialize(string json, string baseUri,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUB)
        {
            m_json = json ?? throw new ArgumentNullException(nameof(json));
            m_baseUri = baseUri ?? string.Empty;
            m_sourceCoordinates = sourceCoordinates;
            m_manifest = GsplatLodManifest.Parse(m_json, m_sourceCoordinates);
        }

        void OnEnable() => m_manifest = null;
        void OnValidate() => m_manifest = null;
    }
}
#endif
