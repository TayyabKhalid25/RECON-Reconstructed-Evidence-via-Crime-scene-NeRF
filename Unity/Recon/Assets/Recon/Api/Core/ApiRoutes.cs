using System;

namespace Recon.Api
{
    /// <summary>
    /// Every URL and header name the Unity client uses, in one pure-C# place so they are testable
    /// without a device or a running server. The transport (UnityWebRequest) is deliberately thin
    /// and lives in <c>Api/ReconApiClient.cs</c>; this is the part that can be wrong in a way a
    /// unit test can catch.
    ///
    /// Paths are the ones in docs/API.md, "Web, consumed by the Unity client".
    /// </summary>
    public static class ApiRoutes
    {
        public const string AuthorizationHeader = "Authorization";

        /// <summary>Set by <c>GET /api/scenes/:id/asset</c> so Unity can verify the bytes against custody.</summary>
        public const string AssetSha256Header = "X-Asset-SHA256";

        public static string BearerValue(string token) => "Bearer " + token;

        public static string Login(string baseUrl) => Root(baseUrl) + "/api/auth/login";

        /// <summary>The list the scene picker shows. Filtered server side, newest first.</summary>
        public static string ReadyScenes(string baseUrl) => Scenes(baseUrl, JobStatusReady);

        public const string JobStatusReady = "READY";

        public static string Scenes(string baseUrl, string status)
        {
            var url = Root(baseUrl) + "/api/scenes";
            if (!string.IsNullOrEmpty(status)) url += "?status=" + Uri.EscapeDataString(status);
            return url;
        }

        public static string Scene(string baseUrl, string sceneId) =>
            Root(baseUrl) + "/api/scenes/" + Id(sceneId);

        public static string Asset(string baseUrl, string sceneId) =>
            Scene(baseUrl, sceneId) + "/asset";

        /// <summary>
        /// The asset endpoint with an explicit <c>kind</c>. As of 2026-09-02 the route hard-codes
        /// SPLAT_PLY and ignores this query, so asking for METADATA_JSON either 404s or hands back
        /// .ply bytes; this is the shape being requested of Track B, and
        /// <see cref="MetadataUnavailableException"/> is what the client throws until it exists.
        /// </summary>
        public static string AssetOfKind(string baseUrl, string sceneId, string kind) =>
            Asset(baseUrl, sceneId) + "?kind=" + Uri.EscapeDataString(kind ?? "");

        public static string Anchor(string baseUrl, string sceneId) =>
            Scene(baseUrl, sceneId) + "/anchor";

        /// <summary>
        /// Resolves <c>SceneDetail.assetUrl</c>, which the route returns relative. An absolute URL
        /// is returned unchanged so a presigned object-store URL from web/src/lib/storage.ts
        /// urlFor() keeps working without a contract change.
        /// </summary>
        public static string Resolve(string baseUrl, string urlOrPath)
        {
            if (string.IsNullOrWhiteSpace(urlOrPath))
                throw new ArgumentException("assetUrl is empty; the scene detail response is missing it.", nameof(urlOrPath));

            var trimmed = urlOrPath.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return trimmed;

            return Root(baseUrl) + "/" + trimmed.TrimStart('/');
        }

        /// <summary>Trims whitespace and trailing slashes so a hand-typed tailnet URL cannot produce "//api/...".</summary>
        public static string Root(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException(
                    "API base URL is empty. Set apiBaseUrl on the ReconSettings asset " +
                    "(Assets/Recon/Config/Resources/ReconSettings.asset), e.g. http://legion:3000 over the tailnet.",
                    nameof(baseUrl));
            return baseUrl.Trim().TrimEnd('/');
        }

        static string Id(string sceneId)
        {
            if (string.IsNullOrWhiteSpace(sceneId))
                throw new ArgumentException("sceneId is empty.", nameof(sceneId));
            // Escaped because ids also come from FileSceneSource, where they are folder names.
            return Uri.EscapeDataString(sceneId.Trim());
        }
    }
}
