using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Recon.Contract;

namespace Recon.Api
{
    /// <summary>
    /// Where scenes come from. Two implementations: <c>ApiSceneSource</c> (the web API over the
    /// tailnet) and <see cref="FileSceneSource"/> (a folder pushed to the phone by hand). They exist
    /// so <c>SceneLoader</c> has one code path rather than two, and so Track C is never blocked on
    /// the web DB migration or on a laptop being awake.
    ///
    /// Pure C#: the interface names only contract DTOs and BCL types, so both implementations and
    /// the loader's logic are testable without the Editor.
    /// </summary>
    public interface ISceneSource
    {
        /// <summary>Shown in the picker, so nobody demos a stale local scene thinking it came from the API.</summary>
        string DisplayName { get; }

        /// <summary>
        /// The SHA-256 the source claimed for the bytes handed back by the most recent
        /// <see cref="DownloadPlyAsync"/>: the <c>X-Asset-SHA256</c> header for the API source, the
        /// metadata hash for the file source. Null when the source did not state one, which is
        /// itself a fact the loader reports rather than hides. Main thread only; one download at a time.
        /// </summary>
        string LastAssetSha256 { get; }

        /// <summary>Scenes whose reconstruction finished, newest first.</summary>
        Task<IReadOnlyList<SceneSummary>> ListReadyAsync(CancellationToken cancellationToken = default);

        Task<SceneDetail> GetSceneAsync(string sceneId, CancellationToken cancellationToken = default);

        /// <summary>
        /// The scene's metadata.json. Throws rather than inventing values when the source cannot
        /// provide it: docs/FRAMES.md requires the client to assert handedness and upAxis, and an
        /// assertion against a default is not an assertion.
        /// </summary>
        Task<SceneMetadata> GetMetadataAsync(string sceneId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Puts the .ply somewhere readable and returns its absolute path. May return a path
        /// outside <paramref name="destFolder"/> when the bytes are already on the device.
        /// </summary>
        Task<string> DownloadPlyAsync(string sceneId, string destFolder, IProgress<float> progress = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>A scene source could not do what was asked, with the reason a human can act on.</summary>
    public class SceneSourceException : Exception
    {
        public SceneSourceException(string message) : base(message) { }
        public SceneSourceException(string message, Exception inner) : base(message, inner) { }
    }
}
