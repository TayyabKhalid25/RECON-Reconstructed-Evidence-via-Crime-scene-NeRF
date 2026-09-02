using System;
using Recon.Contract;

namespace Recon.Api
{
    /// <summary>
    /// A call that did not succeed, with the server's own words. Every non-2xx response is parsed
    /// through the <c>{ error: { code, message, details? } }</c> envelope so the text shown on the
    /// phone is the text the route wrote, and transport failures name the URL — on a tailnet
    /// "which host did it try to reach" is most of the diagnosis (docs/NETWORK.md).
    /// Pure C#: no UnityEngine, so it is testable under dotnet.
    /// </summary>
    public class ApiException : Exception
    {
        /// <summary>Code used when the response was not 2xx but carried no error envelope.</summary>
        public const string HttpCode = "HTTP";

        /// <summary>Code used when the request never reached the server.</summary>
        public const string NetworkCode = "NETWORK";

        public ApiException(string code, string message, long httpStatus)
            : base(message)
        {
            Code = code;
            HttpStatus = httpStatus;
        }

        /// <summary>An ApiErrorCode from web/src/lib/api.ts, or <see cref="HttpCode"/>/<see cref="NetworkCode"/>.</summary>
        public string Code { get; }

        /// <summary>HTTP status, or 0 when the request never got a response.</summary>
        public long HttpStatus { get; }

        public bool IsUnauthorized => HttpStatus == 401 || Code == "UNAUTHORIZED";
        public bool IsNotFound => HttpStatus == 404 || Code == "NOT_FOUND";
        public bool IsNetwork => Code == NetworkCode;

        /// <summary>Builds the exception for a non-2xx response.</summary>
        public static ApiException FromResponse(long httpStatus, string body, string url)
        {
            if (ApiErrorEnvelope.TryParse(body, out var err))
                return new ApiException(err.Code ?? HttpCode, $"{err} [{url}]", httpStatus);

            return new ApiException(HttpCode, $"{ApiErrorEnvelope.DescribeFailure(httpStatus, body)} [{url}]", httpStatus);
        }

        /// <summary>Builds the exception for a request that never reached the server.</summary>
        public static ApiException Network(string url, string transportError) =>
            new ApiException(NetworkCode,
                $"Could not reach {url}: {transportError}. Is the dev API running with -H 0.0.0.0 and " +
                "is this device on the tailnet? (docs/NETWORK.md)", 0);

        public override string ToString() => $"{Code} ({HttpStatus}): {Message}";
    }

    /// <summary>
    /// The server did not give us <c>metadata.json</c>, so the scene cannot be placed.
    ///
    /// Verified 2026-09-02 and being raised with Track B as its own ticket: the API exposes
    /// <c>scene.unitScale</c>, but no route serves the metadata document.
    /// <c>GET /api/scenes/:id/asset</c> hard-codes <c>kind: 'SPLAT_PLY'</c> and ignores any query
    /// string, even though <c>AssetKind</c> already has <c>METADATA_JSON</c> and
    /// <c>POST /api/jobs/:id/result</c> stores one.
    ///
    /// Unity needs more than unitScale: docs/FRAMES.md says the client <b>asserts</b>
    /// <c>handedness</c> and <c>upAxis</c> rather than guessing them, and refuses the scene loudly
    /// when they are not <c>left</c>/<c>y</c>. Defaulting them to what the pipeline usually writes
    /// would make the assertion decorative — exactly the failure the frame convention exists to
    /// prevent — so this refuses instead. FileSceneSource covers the demo path meanwhile.
    /// </summary>
    public sealed class MetadataUnavailableException : ApiException
    {
        public MetadataUnavailableException(string sceneId, string requestedUrl, string serverSaid)
            : base("METADATA_UNAVAILABLE",
                $"Scene {sceneId}: the server did not provide metadata.json. Requested {requestedUrl} and got: " +
                $"{serverSaid}. GET /api/scenes/:id/asset serves only the SPLAT_PLY asset, so handedness, upAxis, " +
                "scaleMethod and the metadata sha256 cannot be read from the API yet. The scene is refused rather " +
                "than placed on assumed values (docs/FRAMES.md). Use a file source, or wait for the metadata " +
                "endpoint on Track B.",
                0)
        {
            SceneId = sceneId;
            RequestedUrl = requestedUrl;
        }

        public string SceneId { get; }
        public string RequestedUrl { get; }
    }
}
