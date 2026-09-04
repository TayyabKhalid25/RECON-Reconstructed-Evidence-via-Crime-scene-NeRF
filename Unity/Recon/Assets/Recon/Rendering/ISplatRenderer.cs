using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Recon.Rendering
{
    /// <summary>
    /// The one seam between RECON and whichever Gaussian splat package renders on the phone.
    /// App code (scene loading, alignment, physics, UI) talks to this and nothing else, so the
    /// UnitySplats → gsplat-unity → aras-p decision (docs/MOBILE-SPLAT-OPTIONS.md) never leaks
    /// past Rendering/Adapters.
    ///
    /// Frame contract (docs/FRAMES.md): the .ply handed to <see cref="LoadAsync"/> is already in
    /// Unity convention (left handed, Y up) and in scene units. An adapter must load it with no
    /// axis conversion and must not scale it; SceneRoot applies unitScale once.
    /// </summary>
    public interface ISplatRenderer
    {
        /// <summary>Package name and pinned version, for RESULTS.md rows and logs.</summary>
        string RendererName { get; }

        bool IsLoaded { get; }

        /// <summary>Splats currently uploaded to the GPU; 0 until loaded.</summary>
        int SplatCount { get; }

        /// <summary>Bounds of the loaded splat set in the renderer's local (scene-unit) space.</summary>
        Bounds LocalBounds { get; }

        /// <summary>
        /// Loads a .ply from an absolute path and parents the resulting renderer object under
        /// <paramref name="parent"/> with identity local transform. Replaces any previous load.
        /// Throws <see cref="SplatLoadException"/> with a readable reason on failure.
        /// </summary>
        Task<SplatLoadResult> LoadAsync(string plyPath, Transform parent,
            IProgress<float> progress = null, CancellationToken cancellationToken = default);

        /// <summary>Releases GPU data and destroys the renderer object. Safe to call when not loaded.</summary>
        void Unload();
    }

    public readonly struct SplatLoadResult
    {
        public SplatLoadResult(int splatCount, Bounds localBounds, double loadSeconds, long fileBytes)
        {
            SplatCount = splatCount;
            LocalBounds = localBounds;
            LoadSeconds = loadSeconds;
            FileBytes = fileBytes;
        }

        public int SplatCount { get; }
        public Bounds LocalBounds { get; }
        public double LoadSeconds { get; }
        public long FileBytes { get; }

        public override string ToString() =>
            $"{SplatCount} splats, {FileBytes / (1024.0 * 1024.0):F1} MB, loaded in {LoadSeconds:F2} s";
    }

    public sealed class SplatLoadException : Exception
    {
        public SplatLoadException(string message) : base(message) { }
        public SplatLoadException(string message, Exception inner) : base(message, inner) { }
    }
}
