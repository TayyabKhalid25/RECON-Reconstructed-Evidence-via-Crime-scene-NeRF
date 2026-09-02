using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Recon.Alignment;
using Recon.Api;
using Recon.Config;
using Recon.Rendering;
using UnityEngine;

namespace Recon.Scenes
{
    /// <summary>
    /// The I2 load path: a scene id in, a validated metric twin under SceneRoot out.
    ///
    /// <code>
    /// GetScene → GetMetadata → SceneMetadataValidator.Validate   (refuse loudly, reason on screen)
    ///          → download to persistentDataPath/scenes/&lt;id&gt;/  (skipped on a sha cache hit)
    ///          → verify sha256 against X-Asset-SHA256 and the asset row
    ///          → SceneRoot.ApplyUnitScale(metadata.unitScale)     (the only scaling anywhere)
    ///          → ISplatRenderer.LoadAsync(ply, sceneRoot.transform)
    /// </code>
    ///
    /// Order matters. Validation happens before the 45 MB download so a wrong-framed or non-metric
    /// scene is refused in a second rather than after a coffee, and the custody check happens
    /// before anything is rendered, because evidence that cannot be verified must not appear on
    /// screen as if it were fine.
    ///
    /// Nothing here flips an axis and nothing here scales the renderer: the .ply is already in
    /// Unity convention and in scene units, and <c>SceneRoot.ApplyUnitScale</c> is the single
    /// scaling step in the whole client (docs/FRAMES.md).
    ///
    /// The interesting logic lives in <see cref="SceneAssetCache"/> and
    /// <see cref="SceneMetadataValidator"/>, both pure and both unit tested; this is the glue.
    /// </summary>
    [AddComponentMenu("Recon/Scenes/Scene Loader")]
    [DisallowMultipleComponent]
    public sealed class SceneLoader : MonoBehaviour
    {
        [SerializeField] bool showStatus = true;

        /// <summary>Human-readable state, the same contract DevSplatLoader exposes. Shown by the picker overlay.</summary>
        public string Status { get; private set; } = "idle";

        /// <summary>0..1 for the download step; 0 outside it.</summary>
        public float Progress { get; private set; }

        public bool IsBusy { get; private set; }

        /// <summary>True only when a validated metric scene is loaded.</summary>
        public bool IsMetric { get; private set; }

        /// <summary>True when the loaded .ply matched a sha256 someone recorded for it.</summary>
        public bool CustodyVerified { get; private set; }

        public string LoadedSceneId { get; private set; }

        /// <summary>Raised on every Status change, so UI does not have to poll.</summary>
        public event Action<SceneLoader> Changed;

        CancellationTokenSource m_cancellation;

        /// <summary>Cancels an in-flight load. Safe to call when idle.</summary>
        public void Cancel() => m_cancellation?.Cancel();

        void OnDestroy()
        {
            m_cancellation?.Cancel();
            m_cancellation?.Dispose();
        }

        /// <summary>
        /// Loads a scene, replacing whatever is loaded now. Never throws: failures land in
        /// <see cref="Status"/> and in the log with the <c>[Recon]</c> prefix, because the caller
        /// is a button and the user needs the reason, not a stack trace. Returns true on success.
        /// </summary>
        public async Task<bool> LoadAsync(string sceneId, ISceneSource source)
        {
            if (IsBusy)
            {
                SetStatus("Already loading " + LoadedSceneId + "; cancel it first.");
                return false;
            }
            if (source == null)
            {
                SetStatus("No scene source selected.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(sceneId))
            {
                SetStatus("No scene id.");
                return false;
            }

            m_cancellation?.Dispose();
            m_cancellation = new CancellationTokenSource();
            var token = m_cancellation.Token;

            IsBusy = true;
            IsMetric = false;
            CustodyVerified = false;
            Progress = 0f;
            LoadedSceneId = sceneId;

            try
            {
                return await RunAsync(sceneId, source, token);
            }
            catch (OperationCanceledException)
            {
                SetStatus("Cancelled " + sceneId);
                return false;
            }
            catch (Exception e)
            {
                // Every refusal reaches the user as the reason it was refused: a validator result,
                // a custody mismatch, the metadata gap, or the server's own error message.
                SetStatus("FAILED: " + e.Message);
                Debug.LogException(e);
                return false;
            }
            finally
            {
                IsBusy = false;
                Progress = 0f;
                Changed?.Invoke(this);
            }
        }

        async Task<bool> RunAsync(string sceneId, ISceneSource source, CancellationToken token)
        {
            var root = SceneRoot.Find();
            var renderer = SplatRendererProvider.Current;
            if (root == null || renderer == null)
            {
                SetStatus("Scene is missing SceneRoot or an ISplatRenderer. Run Recon → Bootstrap ReconAR scene.");
                return false;
            }

            // 1. the scene record
            SetStatus($"Fetching {sceneId} from {source.DisplayName}…");
            var detail = await source.GetSceneAsync(sceneId, token);
            if (!detail.IsReady)
            {
                SetStatus($"Scene {sceneId} is not READY: {detail.Job}");
                return false;
            }

            // 2. metadata. Refused rather than assumed when the source cannot provide it — see
            //    MetadataUnavailableException for the contract gap this surfaces.
            SetStatus("Reading metadata.json…");
            var metadata = await source.GetMetadataAsync(sceneId, token);

            // 3. validate before downloading. A wrong-framed scene should cost a second, not 45 MB.
            var check = SceneMetadataValidator.Validate(metadata);
            foreach (var w in check.Warnings) Debug.LogWarning($"[Recon] {sceneId} metadata warning: {w}");
            if (!check.IsValid)
            {
                root.ClearUnitScale();
                SetStatus(check.ToString());
                Debug.LogError($"[Recon] {sceneId} refused. {check}");
                return false;
            }
            if (detail.HasUnitScale && Math.Abs(detail.UnitScale.Value - metadata.UnitScale) > 1e-6)
            {
                // Scene.unitScale is read from metadata.json by POST /api/jobs/:id/result, so the
                // two disagreeing means one of them was edited after the fact.
                SetStatus($"REJECTED: the API says unitScale {detail.UnitScale.Value:F6} but metadata.json says " +
                          $"{metadata.UnitScale:F6}. One of them was changed after the reconstruction; refusing " +
                          "rather than picking one (docs/FRAMES.md).");
                Debug.LogError("[Recon] " + Status);
                root.ClearUnitScale();
                return false;
            }
            Debug.Log($"[Recon] {sceneId} metadata valid: handedness {metadata.Handedness}, upAxis {metadata.UpAxis}, " +
                      $"unitScale {metadata.UnitScale:F6} via {metadata.ScaleMethod}, {metadata.SplatCount} splats");

            // 4. download, or reuse a cached file with the same content hash
            var expectedSha = SceneAssetCache.ExpectedSha256(detail, metadata);
            var folder = SceneAssetCache.SceneFolder(Application.persistentDataPath, sceneId);
            var cachedPath = Path.Combine(folder, ReconSettings.Instance.plyFileName);

            string plyPath;
            if (!SceneAssetCache.ShouldDownload(cachedPath, expectedSha))
            {
                plyPath = cachedPath;
                Debug.Log($"[Recon] Cache hit for {sceneId}: {cachedPath} already hashes to {expectedSha}");
                SetStatus("Using cached .ply (sha matches)");
            }
            else
            {
                SetStatus("Downloading .ply…");
                plyPath = await source.DownloadPlyAsync(sceneId, folder,
                    new Progress<float>(p => { Progress = p; }), token);
            }

            // 5. custody: the bytes on this device must be the bytes the record describes
            var verification = SceneAssetCache.Verify(plyPath, expectedSha, source.LastAssetSha256);
            CustodyVerified = verification.Verified;
            if (!verification.Verified)
                Debug.LogWarning($"[Recon] {sceneId} {verification}");
            else
                Debug.Log($"[Recon] {sceneId} {verification}");

            // 6. the one scaling step in the entire client
            root.ApplyUnitScale(metadata.UnitScale);
            IsMetric = root.IsMetric;

            // 7. render, parented under SceneRoot so alignment moves everything at once
            SetStatus("Loading splats…");
            var result = await renderer.LoadAsync(plyPath, root.transform,
                new Progress<float>(p => { Progress = p; }), token);

            SetStatus($"{detail.Name}: {result} | unitScale {root.UnitScale:F6}" +
                      (CustodyVerified ? " | sha verified" : " | SHA UNVERIFIED"));
            Debug.Log("[Recon] " + Status);
            return true;
        }

        void SetStatus(string status)
        {
            Status = status;
            Changed?.Invoke(this);
        }

        void OnGUI()
        {
            if (!showStatus) return;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Screen.dpi > 0 ? Screen.dpi * 0.09f : 14f),
                wordWrap = true,
                normal = { textColor = IsMetric && CustodyVerified ? Color.white : new Color(1f, 0.6f, 0.2f) }
            };
            var text = Status;
            if (IsBusy && Progress > 0f) text += $"  {Progress * 100f:F0}%";
            GUI.Label(new Rect(16, Screen.height - 140, Screen.width - 32, 124), text, style);
        }
    }
}
