using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Recon.Contract;
using UnityEngine;

namespace Recon.Api
{
    /// <summary>
    /// Scenes from the live web API over the tailnet. The <see cref="ISceneSource"/> the demo path
    /// uses; <see cref="FileSceneSource"/> is the same interface backed by a folder, so nothing
    /// downstream branches on which one is active.
    /// </summary>
    public sealed class ApiSceneSource : ISceneSource
    {
        readonly ReconApiClient m_client;

        public ApiSceneSource(ReconApiClient client)
        {
            m_client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public ReconApiClient Client => m_client;

        public string DisplayName => "api: " + m_client.BaseUrl;

        public string LastAssetSha256 { get; private set; }

        public async Task<IReadOnlyList<SceneSummary>> ListReadyAsync(CancellationToken cancellationToken = default) =>
            await m_client.ListReadyScenesAsync(cancellationToken);

        public async Task<SceneDetail> GetSceneAsync(string sceneId, CancellationToken cancellationToken = default) =>
            await m_client.GetSceneAsync(sceneId, cancellationToken);

        /// <summary>
        /// Tries to fetch the scene's metadata.json and refuses the scene if it cannot.
        ///
        /// <b>Known contract gap, verified 2026-09-02, raised with Track B as its own ticket.</b>
        /// The API exposes <c>scene.unitScale</c> but serves no metadata document:
        /// <c>GET /api/scenes/:id/asset</c> hard-codes <c>kind: 'SPLAT_PLY'</c> and ignores any
        /// query string, even though <c>AssetKind</c> already has <c>METADATA_JSON</c> and
        /// <c>POST /api/jobs/:id/result</c> stores one. So this asks for
        /// <c>?kind=METADATA_JSON</c> — the shape being requested — and turns the 404, the 400, or
        /// the .ply bytes it gets instead into a <see cref="MetadataUnavailableException"/> that
        /// names the endpoint.
        ///
        /// It deliberately does not synthesise metadata from <c>unitScale</c> plus assumed
        /// <c>handedness: "left"</c> / <c>upAxis: "y"</c>. docs/FRAMES.md has the client
        /// <i>assert</i> the frame instead of guessing, and an assertion against a default the
        /// client itself supplied is not an assertion — it is the "two flips cancel out" bug with
        /// extra steps. Refusing is the correct behaviour, and FileSceneSource covers the demo
        /// path until the endpoint exists.
        /// </summary>
        public async Task<SceneMetadata> GetMetadataAsync(string sceneId, CancellationToken cancellationToken = default)
        {
            var url = ApiRoutes.AssetOfKind(m_client.BaseUrl, sceneId, AssetInfo.MetadataJson);

            string body;
            try
            {
                body = await m_client.GetTextAsync(url, cancellationToken);
            }
            catch (ApiException e)
            {
                // A 404 or 400 here is the expected shape of the gap, not a surprise.
                throw new MetadataUnavailableException(sceneId, url, $"{e.Code} ({e.HttpStatus}): {e.Message}");
            }

            if (string.IsNullOrWhiteSpace(body))
                throw new MetadataUnavailableException(sceneId, url, "an empty body");

            var head = body.TrimStart();
            if (head.Length == 0 || head[0] != '{')
            {
                // The route currently streams the .ply for any kind, so this is the likely branch:
                // a PLY header where JSON was asked for.
                var snippet = head.Substring(0, Math.Min(40, head.Length)).Replace("\n", "\\n");
                throw new MetadataUnavailableException(sceneId, url,
                    $"a non-JSON body starting \"{snippet}\" (the route serves the SPLAT_PLY asset for any kind)");
            }

            try
            {
                return SceneMetadata.FromJson(body);
            }
            catch (Exception e)
            {
                throw new MetadataUnavailableException(sceneId, url, $"a body that did not parse as metadata.json ({e.Message})");
            }
        }

        public async Task<string> DownloadPlyAsync(string sceneId, string destFolder, IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(destFolder)) throw new ArgumentException("destFolder is empty", nameof(destFolder));

            Directory.CreateDirectory(destFolder);
            var destPath = Path.Combine(destFolder, Config.ReconSettings.Instance.plyFileName);

            LastAssetSha256 = await m_client.DownloadAssetAsync(sceneId, destPath, progress, cancellationToken);
            if (string.IsNullOrEmpty(LastAssetSha256))
                Debug.LogWarning($"[Recon] {ApiRoutes.AssetSha256Header} was absent on the asset response for " +
                                 $"{sceneId}; the download will be checked against the scene's asset row instead.");

            return destPath;
        }
    }
}
