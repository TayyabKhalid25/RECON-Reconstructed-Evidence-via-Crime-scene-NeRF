using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Recon.Config;
using Recon.Contract;
using UnityEngine;
using UnityEngine.Networking;

namespace Recon.Api
{
    /// <summary>
    /// The RECON web API as the phone sees it. A plain class, not a MonoBehaviour: it holds no
    /// scene state, so a loader or an overlay can own one.
    ///
    /// Deliberately thin. URLs and header names are in <see cref="ApiRoutes"/> and body parsing is
    /// in Contract/Core, both of which are unit tested; what is left here is UnityWebRequest
    /// plumbing that only a device can exercise. Every non-2xx goes through the error envelope so
    /// the text on screen is the text the route wrote, and a transport failure names the URL —
    /// on a tailnet that is most of the diagnosis (docs/NETWORK.md).
    ///
    /// Call from Unity's main thread; continuations resume there.
    /// </summary>
    public sealed class ReconApiClient
    {
        readonly ISessionStore m_session;
        readonly string m_baseUrlOverride;

        public ReconApiClient(ISessionStore session = null, string baseUrlOverride = null)
        {
            m_session = session ?? new InMemorySessionStore();
            m_baseUrlOverride = baseUrlOverride;
        }

        public ISessionStore Session => m_session;

        /// <summary>From the ReconSettings asset unless an override was passed in.</summary>
        public string BaseUrl => string.IsNullOrWhiteSpace(m_baseUrlOverride)
            ? ReconSettings.Instance.apiBaseUrl
            : m_baseUrlOverride;

        int TimeoutSeconds => Mathf.Max(1, ReconSettings.Instance.apiTimeoutSeconds);

        // ---- auth ------------------------------------------------------------------------

        /// <summary>
        /// Logs in and stores the token in the session store. Seeded dev logins are in
        /// web/README.md (<c>investigator@recon.local</c> / <c>investigator-dev</c>), dev only.
        /// </summary>
        public async Task<LoginResponse> LoginAsync(string email, string password,
            CancellationToken cancellationToken = default)
        {
            var url = ApiRoutes.Login(BaseUrl);
            var body = Newtonsoft.Json.JsonConvert.SerializeObject(new { email, password });

            var json = await SendJsonAsync(url, "POST", body, authenticated: false, cancellationToken);
            var response = LoginResponse.FromJson(json);

            m_session.Set(response.Token);
            Debug.Log($"[Recon] Logged in to {BaseUrl} as {response.User}");
            return response;
        }

        public void Logout() => m_session.Clear();

        // ---- scenes ----------------------------------------------------------------------

        public async Task<IReadOnlyList<SceneSummary>> ListReadyScenesAsync(
            CancellationToken cancellationToken = default)
        {
            var url = ApiRoutes.ReadyScenes(BaseUrl);
            var json = await SendJsonAsync(url, "GET", null, authenticated: true, cancellationToken);
            var list = SceneListResponse.FromJson(json).Scenes;
            Debug.Log($"[Recon] {list.Count} READY scene(s) from {url}");
            return list;
        }

        public async Task<SceneDetail> GetSceneAsync(string sceneId, CancellationToken cancellationToken = default)
        {
            var url = ApiRoutes.Scene(BaseUrl, sceneId);
            var json = await SendJsonAsync(url, "GET", null, authenticated: true, cancellationToken);
            return SceneDetailResponse.FromJson(json).Scene;
        }

        /// <summary>
        /// Fetches an asset URL as text. Used to probe for metadata.json; the caller decides what
        /// a non-JSON body means, because "the server handed back .ply bytes" is a contract fact
        /// worth reporting rather than a parse error to swallow.
        /// </summary>
        public Task<string> GetTextAsync(string url, CancellationToken cancellationToken = default) =>
            SendJsonAsync(url, "GET", null, authenticated: true, cancellationToken);

        /// <summary>
        /// Streams <c>GET /api/scenes/:id/asset</c> straight to <paramref name="destPath"/> with
        /// <see cref="DownloadHandlerFile"/>, so a 45 MB .ply never sits in a managed buffer on a
        /// phone. Returns the <c>X-Asset-SHA256</c> header, which is what the bytes on disk get
        /// checked against.
        /// </summary>
        public async Task<string> DownloadAssetAsync(string sceneId, string destPath,
            IProgress<float> progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(destPath)) throw new ArgumentException("destPath is empty", nameof(destPath));

            var url = ApiRoutes.Asset(BaseUrl, sceneId);
            var folder = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            using (var request = new UnityWebRequest(url, "GET"))
            {
                var handler = new DownloadHandlerFile(destPath) { removeFileOnAbort = true };
                request.downloadHandler = handler;
                request.timeout = 0;    // a big .ply over a home uplink outlives any sane timeout
                Authenticate(request);

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        request.Abort();
                        throw new OperationCanceledException(cancellationToken);
                    }
                    progress?.Report(request.downloadProgress);
                    // Yields to the player loop; Unity's SynchronizationContext resumes this on
                    // the main thread, which is where Abort() and the handler must be touched.
                    await Task.Yield();
                }
                progress?.Report(1f);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    // With DownloadHandlerFile the error body lands in the file rather than in a
                    // buffer, so read it back before deleting the partial download.
                    string body = ReadSmallFile(destPath);
                    SafeDelete(destPath);
                    if (request.result == UnityWebRequest.Result.ConnectionError)
                        throw ApiException.Network(url, request.error);
                    throw ApiException.FromResponse(request.responseCode, body, url);
                }

                var sha = request.GetResponseHeader(ApiRoutes.AssetSha256Header);
                long bytes = new FileInfo(destPath).Length;
                Debug.Log($"[Recon] Downloaded {bytes / (1024.0 * 1024.0):F1} MB from {url} → {destPath} " +
                          $"| {ApiRoutes.AssetSha256Header}: {sha ?? "(absent)"}");
                return sha;
            }
        }

        // ---- anchors (the DTO half of FTW-52/FTW-53) -------------------------------------

        /// <summary>
        /// <c>GET /api/scenes/:id/anchor</c>. A 404 here means "nobody has hosted an anchor yet",
        /// which the server keeps distinct from "you cannot see this scene" — so this returns null
        /// on NOT_FOUND rather than throwing, and the caller falls back down the chain in
        /// docs/ANCHORING.md.
        /// </summary>
        public async Task<AnchorRecord> GetAnchorAsync(string sceneId, CancellationToken cancellationToken = default)
        {
            var url = ApiRoutes.Anchor(BaseUrl, sceneId);
            try
            {
                var json = await SendJsonAsync(url, "GET", null, authenticated: true, cancellationToken);
                return AnchorResponse.FromJson(json).Anchor;
            }
            catch (ApiException e) when (e.IsNotFound)
            {
                return null;
            }
        }

        /// <summary>
        /// <c>POST /api/scenes/:id/anchor</c>. The transform is decomposed, never a 4x4 matrix, and
        /// the body is validated client side first so an invalid quaternion costs a log line rather
        /// than a round trip (see Recon.Alignment.AnchorTransform.EnsureValidForPost).
        /// </summary>
        public async Task<AnchorRecord> PostAnchorAsync(string sceneId, AnchorInput input,
            CancellationToken cancellationToken = default)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var url = ApiRoutes.Anchor(BaseUrl, sceneId);
            var json = await SendJsonAsync(url, "POST", input.ToJson(), authenticated: true, cancellationToken);
            return AnchorResponse.FromJson(json).Anchor;
        }

        // ---- plumbing --------------------------------------------------------------------

        async Task<string> SendJsonAsync(string url, string method, string body, bool authenticated,
            CancellationToken cancellationToken)
        {
            using (var request = new UnityWebRequest(url, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = TimeoutSeconds;

                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.SetRequestHeader("Accept", "application/json");
                if (authenticated) Authenticate(request);

                var operation = request.SendWebRequest();
                if (cancellationToken.CanBeCanceled)
                {
                    while (!operation.isDone)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            request.Abort();
                            throw new OperationCanceledException(cancellationToken);
                        }
                        await Task.Yield();
                    }
                }
                else
                {
                    await operation;
                }

                string text = request.downloadHandler != null ? request.downloadHandler.text : null;

                switch (request.result)
                {
                    case UnityWebRequest.Result.Success:
                        return text;

                    case UnityWebRequest.Result.ConnectionError:
                    case UnityWebRequest.Result.DataProcessingError:
                        throw ApiException.Network(url, request.error);

                    default:
                        var failure = ApiException.FromResponse(request.responseCode, text, url);
                        // A 401 means whatever token we hold is no longer usable; keeping it would
                        // make every later call fail the same way for no visible reason.
                        if (failure.IsUnauthorized) m_session.Clear();
                        throw failure;
                }
            }
        }

        void Authenticate(UnityWebRequest request)
        {
            if (!m_session.HasToken)
                throw new ApiException("UNAUTHORIZED", $"Not logged in; no session token for {BaseUrl}. " +
                                                       "Log in from the scene picker first.", 401);
            request.SetRequestHeader(ApiRoutes.AuthorizationHeader, ApiRoutes.BearerValue(m_session.Token));
        }

        static string ReadSmallFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var info = new FileInfo(path);
                if (info.Length == 0 || info.Length > 64 * 1024) return null;
                return File.ReadAllText(path);
            }
            catch (IOException)
            {
                return null;
            }
        }

        static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException e) { Debug.LogWarning($"[Recon] Could not delete partial download {path}: {e.Message}"); }
        }
    }
}
