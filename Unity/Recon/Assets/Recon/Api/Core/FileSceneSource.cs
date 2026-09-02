using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Recon.Contract;
using Recon.Scenes;

namespace Recon.Api
{
    /// <summary>
    /// Scenes from a folder on the device, laid out as
    /// <c>&lt;folder&gt;/&lt;sceneId&gt;/splat_unity.ply</c> plus <c>metadata.json</c> — exactly what
    /// <c>Unity/push-dev-scene.ps1</c> pushes. A folder is listed only when it has both files.
    ///
    /// This is what lets the Unity client work before the web DB exists: as of 2026-09-02 no
    /// migration had run on Track B, and it is also the offline path for a demo on a network that
    /// does not cooperate. Same interface as the API source, so nothing downstream branches on it.
    ///
    /// Pure C#: no UnityEngine, so the whole thing is covered by `dotnet test`.
    /// </summary>
    public sealed class FileSceneSource : ISceneSource
    {
        public const string DefaultPlyFileName = "splat_unity.ply";
        public const string DefaultMetadataFileName = "metadata.json";

        readonly string m_root;
        readonly string m_plyFileName;
        readonly string m_metadataFileName;

        public FileSceneSource(string folder, string plyFileName = DefaultPlyFileName,
            string metadataFileName = DefaultMetadataFileName)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("Scene folder is empty. Point it at persistentDataPath/scenes.", nameof(folder));
            m_root = folder.Trim();
            m_plyFileName = string.IsNullOrWhiteSpace(plyFileName) ? DefaultPlyFileName : plyFileName;
            m_metadataFileName = string.IsNullOrWhiteSpace(metadataFileName) ? DefaultMetadataFileName : metadataFileName;
        }

        public string DisplayName => "file: " + m_root;

        public string LastAssetSha256 { get; private set; }

        public string RootFolder => m_root;

        public Task<IReadOnlyList<SceneSummary>> ListReadyAsync(CancellationToken cancellationToken = default)
        {
            var list = new List<SceneSummary>();

            // A phone with nothing pushed yet is a normal state, not an error: returning empty
            // lets the picker say "no scenes on disk" instead of showing a stack trace.
            if (!Directory.Exists(m_root)) return Task.FromResult((IReadOnlyList<SceneSummary>)list);

            foreach (var folder in Directory.GetDirectories(m_root))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var id = Path.GetFileName(folder);
                if (!File.Exists(Path.Combine(folder, m_plyFileName))) continue;
                var metaPath = Path.Combine(folder, m_metadataFileName);
                if (!File.Exists(metaPath)) continue;

                SceneMetadata meta;
                try
                {
                    meta = SceneMetadata.FromJson(File.ReadAllText(metaPath));
                }
                catch (Exception)
                {
                    // One corrupt folder must not empty the whole list. Loading it will fail
                    // loudly with the parse error if the user asks for it by id.
                    continue;
                }

                list.Add(Summary(id, meta));
            }

            // Newest first, matching GET /api/scenes. createdAt is ISO-8601, so ordinal compare
            // is chronological.
            list.Sort((a, b) => string.CompareOrdinal(b.CreatedAt ?? "", a.CreatedAt ?? ""));
            return Task.FromResult((IReadOnlyList<SceneSummary>)list);
        }

        public Task<SceneDetail> GetSceneAsync(string sceneId, CancellationToken cancellationToken = default)
        {
            var folder = SceneFolder(sceneId);
            var plyPath = Path.Combine(folder, m_plyFileName);
            var meta = ReadMetadata(sceneId);

            var info = new FileInfo(plyPath);
            var detail = new SceneDetail
            {
                Id = sceneId,
                Name = string.IsNullOrEmpty(meta.SceneId) ? sceneId : meta.SceneId,
                CreatedAt = meta.CreatedAt,
                UnitScale = meta.UnitScale,
                CaseId = LocalCaseId,
                Job = new JobInfo { Id = "local", Status = JobInfo.Ready, Progress = 100 },
                Assets = new List<AssetInfo>
                {
                    new AssetInfo
                    {
                        Id = sceneId + ":ply",
                        Kind = AssetInfo.SplatPly,
                        // The metadata hash when the pipeline wrote one, otherwise the bytes on
                        // disk. Either way the loader's custody check compares against something
                        // real rather than being skipped.
                        Sha256 = Sha256Hex.IsUsableHash(meta.Sha256) ? meta.Sha256 : Sha256Hex.ComputeFileHex(plyPath),
                        ByteSize = info.Length,
                        MimeType = "application/octet-stream",
                        CreatedAt = meta.CreatedAt,
                    },
                    new AssetInfo
                    {
                        Id = sceneId + ":metadata",
                        Kind = AssetInfo.MetadataJson,
                        Sha256 = Sha256Hex.ComputeFileHex(Path.Combine(folder, m_metadataFileName)),
                        ByteSize = new FileInfo(Path.Combine(folder, m_metadataFileName)).Length,
                        MimeType = "application/json",
                        CreatedAt = meta.CreatedAt,
                    },
                },
                AssetUrl = plyPath,
            };

            return Task.FromResult(detail);
        }

        public Task<SceneMetadata> GetMetadataAsync(string sceneId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ReadMetadata(sceneId));

        /// <summary>
        /// The bytes are already on the device, so this returns the path they are at rather than
        /// copying tens of megabytes into <paramref name="destFolder"/>. The loader treats the
        /// returned path as authoritative and hashes it in place.
        /// </summary>
        public Task<string> DownloadPlyAsync(string sceneId, string destFolder, IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            var folder = SceneFolder(sceneId);
            var plyPath = Path.Combine(folder, m_plyFileName);
            if (!File.Exists(plyPath))
                throw new SceneSourceException($"Scene '{sceneId}' has no {m_plyFileName} in {folder}.");

            var meta = TryReadMetadata(sceneId);
            LastAssetSha256 = meta != null && Sha256Hex.IsUsableHash(meta.Sha256) ? meta.Sha256 : null;

            progress?.Report(1f);
            return Task.FromResult(plyPath);
        }

        /// <summary>Marks scenes that never came from a case in the database.</summary>
        public const string LocalCaseId = "local";

        SceneSummary Summary(string id, SceneMetadata meta) => new SceneSummary
        {
            Id = id,
            Name = string.IsNullOrEmpty(meta.SceneId) ? id : meta.SceneId,
            CreatedAt = meta.CreatedAt,
            // Reported as the file says, including 0.0. The validator refuses a non-metric scene
            // at load time with the reason on screen; filtering it out of the list here would turn
            // that into "my scene is missing".
            UnitScale = meta.UnitScale,
            CaseId = LocalCaseId,
            Job = new JobInfo { Id = "local", Status = JobInfo.Ready, Progress = 100 },
        };

        SceneMetadata ReadMetadata(string sceneId)
        {
            var folder = SceneFolder(sceneId);
            var metaPath = Path.Combine(folder, m_metadataFileName);
            if (!File.Exists(metaPath))
                throw new SceneSourceException($"Scene '{sceneId}' has no {m_metadataFileName} in {folder}.");
            try
            {
                return SceneMetadata.FromJson(File.ReadAllText(metaPath));
            }
            catch (Exception e)
            {
                throw new SceneSourceException($"Scene '{sceneId}': {m_metadataFileName} is unreadable ({e.Message}).", e);
            }
        }

        SceneMetadata TryReadMetadata(string sceneId)
        {
            try { return ReadMetadata(sceneId); }
            catch (SceneSourceException) { return null; }
        }

        /// <summary>
        /// Resolves and checks the scene folder. Ids reach here from a UI text field as well as
        /// from a listing, so a single path segment inside the root is enforced rather than assumed.
        /// </summary>
        string SceneFolder(string sceneId)
        {
            if (string.IsNullOrWhiteSpace(sceneId))
                throw new SceneSourceException("Scene id is empty.");

            var id = sceneId.Trim();
            if (id != Path.GetFileName(id) || id == "." || id == "..")
                throw new SceneSourceException(
                    $"Scene id '{sceneId}' is not a single folder name; scenes live one level under {m_root}.");

            var folder = Path.Combine(m_root, id);
            if (!Directory.Exists(folder))
                throw new SceneSourceException(
                    $"No scene '{id}' in {m_root}. Push one with Unity/push-dev-scene.ps1, or switch the picker to the API source.");

            return folder;
        }
    }
}
