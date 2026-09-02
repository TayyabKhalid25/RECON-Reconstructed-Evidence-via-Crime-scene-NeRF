using System.Threading;
using System.Threading.Tasks;
using Recon.Api;
using Recon.Contract;
using UnityEngine;

namespace Recon.Alignment
{
    /// <summary>
    /// Priority 2 of the fallback chain: resolve the cloud anchor a previous session hosted, apply
    /// the stored anchor-to-scene transform, and put the twin back in a room whose marker has been
    /// taken off the floor (docs/ANCHORING.md).
    ///
    /// <b>Stub. Not implemented — TODO FTW-52 (spike: does ARCore Cloud Anchors work on
    /// AR Foundation 6.5, or is the built-in persistent anchor API enough) and FTW-53 (host and
    /// resolve on two devices), both Tayyab's, D12 / 1 Nov.</b>
    ///
    /// The file exists now so the shape is agreed while the contract is fresh, and so nothing
    /// later has to reshape <see cref="AlignmentStatus"/> or the wire DTOs to fit:
    ///
    /// <list type="bullet">
    /// <item>The DTOs are done and tested: <see cref="AnchorRecord"/>, <see cref="AnchorInput"/>,
    ///   and <c>AnchorTransform.ToPose</c>, which refuses a non-unit quaternion or a non-positive
    ///   scale exactly as the server does.</item>
    /// <item>The transport is done: <c>ReconApiClient.GetAnchorAsync</c> returns null when nobody
    ///   has hosted yet, and <c>PostAnchorAsync</c> sends the decomposed transform.</item>
    /// <item>What is missing is only the provider half: hosting an anchor at the marker-derived
    ///   pose while feature map quality reads Good, and resolving it on a second device.</item>
    /// </list>
    ///
    /// Two rules FTW-52/53 must keep, because getting them wrong is expensive to find later:
    /// <list type="number">
    /// <item><b>One anchor per scene, hosted at the marker origin</b> — never one per evidence
    ///   marker. N independent re-localisation errors would disagree with each other and with the
    ///   splat, and a cloud anchor id is an opaque handle we cannot put in the custody chain.</item>
    /// <item><b><see cref="AnchorRecord.Scale"/> is the anchor-to-scene fit, normally 1, and is not
    ///   unitScale.</b> Applying one where the other belongs is the double-scale bug, and it looks
    ///   like a mostly correct twin.</item>
    /// </list>
    /// This path is never metric verified: Google publishes no accuracy figure for Cloud Anchor
    /// re-localisation and FTW-56 has not measured ours. The 2 cm claim belongs to the marker.
    /// </summary>
    [AddComponentMenu("Recon/Alignment/Cloud Anchor Aligner (stub)")]
    [DisallowMultipleComponent]
    public sealed class CloudAnchorAligner : MonoBehaviour
    {
        [SerializeField, Tooltip("Nothing is implemented behind this yet; see FTW-52 / FTW-53.")]
        bool enabledByUser;

        public bool Enabled
        {
            get => enabledByUser;
            set => enabledByUser = value;
        }

        /// <summary>
        /// Would host an anchor at the current SceneRoot pose and POST it, returning the stored
        /// record. TODO FTW-53.
        /// </summary>
        public Task<AnchorRecord> HostAsync(string sceneId, ISceneSource source,
            CancellationToken cancellationToken = default)
        {
            Debug.LogWarning("[Recon] CloudAnchorAligner.HostAsync is not implemented (FTW-52 / FTW-53). " +
                             "Align by marker, or place manually and accept NOT METRIC VERIFIED.");
            return Task.FromResult<AnchorRecord>(null);
        }

        /// <summary>
        /// Would GET the newest anchor for the scene, resolve it with the provider, then place
        /// SceneRoot at <c>AnchorTransform.ToPose(record)</c> and report
        /// <see cref="AlignmentSource.CloudAnchor"/>. Must refuse an
        /// <see cref="AnchorRecord.Expired"/> record loudly: a resolve that silently returns a dead
        /// anchor looks exactly like broken tracking on the phone. TODO FTW-52.
        /// </summary>
        public Task<bool> TryResolveAsync(string sceneId, ISceneSource source,
            CancellationToken cancellationToken = default)
        {
            Debug.LogWarning("[Recon] CloudAnchorAligner.TryResolveAsync is not implemented (FTW-52 / FTW-53).");
            return Task.FromResult(false);
        }
    }
}
