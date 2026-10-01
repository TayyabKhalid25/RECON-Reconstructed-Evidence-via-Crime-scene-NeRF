// PlayCanvas-compatible Gaussian-splat LOD streaming for Unity.
// SPDX-License-Identifier: MIT

#if false // Temporarily disabled: lod-meta.json support is not part of the current public release.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gsplat.Formats;
using UnityEngine;
using UnityEngine.Networking;

namespace Gsplat
{
    /// <summary>
    /// Streams a PlayCanvas <c>lod-meta.json</c> using one renderer per referenced resource.
    /// Exact leaf intervals are grouped per file and an old resident LOD remains visible until
    /// its replacement has decoded successfully.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class GsplatLodStreamer : MonoBehaviour
    {
        [Header("Manifest")]
        public GsplatLodManifestAsset ManifestAsset;
        [Tooltip("Optional runtime lod-meta.json URI. Relative values resolve under StreamingAssets.")]
        public string ManifestUri;
        public SourceCoordinates SourceCoordinates = SourceCoordinates.Unspecified;
        public CompressionMode Compression = CompressionMode.Spark;

        [Header("LOD selection")]
        public Camera LodCamera;
        [Min(0.1f)] public float LodBaseDistance = 5f;
        [Min(1.2f)] public float LodMultiplier = 3f;
        [Min(0)] public int LodRangeMin;
        [Min(0)] public int LodRangeMax = 99;
        [Min(1f)] public float LodBehindPenalty = 1f;
        [Min(0)] public int LodUnderfillLimit;
        [Range(1, 120)] public int LodUpdateIntervalFrames = 10;

        [Header("Streaming")]
        [Range(1, 16)] public int MaxConcurrentLoads = 2;
        [Range(0, 8)] public int MaxRetries = 2;
        [Min(0)] public int RetryDelayMilliseconds = 250;
        [Min(0f)] public float CacheCooldownSeconds = 2f;
        [Min(0f)] public float FailureCooldownSeconds = 5f;
        public bool AsyncGpuUpload = true;

        [Header("Rendering")]
        [Range(0, 4)] public int SHDegree = 3;
        [Min(0f)] public float Brightness = 1f;
        [Range(0f, 1f)] public float SplatDownscaleFactor;
        public bool GammaToLinear;
        public uint RenderOrder;

        public bool IsInitialized => m_manifest != null;
        public int LoadedFileCount
        {
            get
            {
                int count = 0;
                if (m_slots != null)
                    foreach (FileSlot slot in m_uniqueSlots)
                        if (slot.Status == LoadStatus.Loaded) ++count;
                return count;
            }
        }
        public int ActiveLeafCount
        {
            get
            {
                int count = 0;
                if (m_currentLods != null)
                    foreach (int lod in m_currentLods)
                        if (lod >= 0) ++count;
                return count;
            }
        }
        public GsplatLodManifest Manifest => m_manifest;

        public event Action<int, string, Exception> FileLoadFailed;

        enum LoadStatus { Unloaded, Queued, Loading, Loaded, Failed }

        sealed class FileSlot
        {
            public readonly int Index;
            public readonly string Uri;
            public readonly List<int> ManifestIndices = new();
            public GameObject GameObject;
            public GsplatRenderer Renderer;
            public LoadStatus Status;
            public GsplatAsset Asset;
            public bool GpuReady;
            public bool Wanted;
            public int WantedReferences;
            public int Priority = int.MaxValue;
            public float LastWantedTime;
            public float FailureUntil;
            public CancellationTokenSource LoadCancellation;
            public GsplatActiveRange[] AppliedRanges = Array.Empty<GsplatActiveRange>();

            public FileSlot(int index, string uri)
            {
                Index = index;
                Uri = uri;
                ManifestIndices.Add(index);
            }
        }

        GsplatLodManifest m_manifest;
        FileSlot[] m_slots;
        readonly List<FileSlot> m_uniqueSlots = new();
        FileSlot m_environment;
        int[] m_currentLods;
        int[] m_optimalLods;
        int[] m_desiredLods;
        readonly List<FileSlot> m_loadQueue = new();
        readonly List<FileSlot> m_uploadingSlots = new();
        readonly Dictionary<int, List<GsplatActiveRange>> m_rangeScratch = new();
        readonly HashSet<int> m_activeRangeFiles = new();
        readonly HashSet<int> m_previousActiveRangeFiles = new();
        readonly HashSet<int> m_filesToApply = new();
        CancellationTokenSource m_lifetime;
        int m_activeLoads;
        int m_sessionGeneration;
        bool m_forceEvaluate;
        bool m_rangesDirty;
        bool m_shuttingDown;
        int m_lastEvaluationFrame = int.MinValue;
        string m_baseUri;
        SourceCoordinates m_sessionCoordinates = global::Gsplat.SourceCoordinates.Unspecified;

        async void OnEnable()
        {
            if (!Application.isPlaying || m_manifest != null) return;
            try
            {
                m_shuttingDown = false;
                m_lifetime = new CancellationTokenSource();
                if (ManifestAsset)
                {
                    SourceCoordinates coordinates = SourceCoordinates == global::Gsplat.SourceCoordinates.Unspecified
                        ? ManifestAsset.SourceCoordinates : SourceCoordinates;
                    GsplatLodManifest manifest = coordinates == ManifestAsset.SourceCoordinates
                        ? ManifestAsset.Manifest
                        : GsplatLodManifest.Parse(ManifestAsset.Json, coordinates);
                    Initialize(manifest, ManifestAsset.BaseUri, coordinates);
                }
                else if (!string.IsNullOrWhiteSpace(ManifestUri))
                {
                    string uri = GsplatLodUri.ResolveManifestUri(ManifestUri);
                    byte[] bytes = await LoadBytesAsync(uri, m_lifetime.Token);
                    string json = Encoding.UTF8.GetString(bytes);
                    SourceCoordinates coordinates = SourceCoordinates == global::Gsplat.SourceCoordinates.Unspecified
                        ? global::Gsplat.SourceCoordinates.RUB : SourceCoordinates;
                    Initialize(GsplatLodManifest.Parse(json, coordinates), GsplatLodUri.DirectoryOf(uri), coordinates);
                }
                else
                {
                    Debug.LogError("[Gsplat LOD] Assign a manifest asset or ManifestUri.", this);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        /// <summary>Initializes from an already parsed manifest. Must be called on Unity's main thread.</summary>
        public void Initialize(GsplatLodManifest manifest, string baseUri,
            SourceCoordinates sourceCoordinates = SourceCoordinates.Unspecified)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            SourceCoordinates resolvedCoordinates = sourceCoordinates == global::Gsplat.SourceCoordinates.Unspecified
                ? manifest.SourceCoordinates : sourceCoordinates;
            if (resolvedCoordinates != manifest.SourceCoordinates)
                throw new ArgumentException(
                    $"The manifest bounds were parsed as {manifest.SourceCoordinates}, but resources were requested as {resolvedCoordinates}. Reparse the manifest with the same coordinate frame.",
                    nameof(sourceCoordinates));
            Shutdown();
            m_shuttingDown = false;
            m_lifetime = new CancellationTokenSource();
            m_manifest = manifest;
            SourceCoordinates = resolvedCoordinates;
            m_sessionCoordinates = resolvedCoordinates;
            m_baseUri = baseUri ?? string.Empty;
            m_slots = new FileSlot[manifest.Filenames.Length];
            m_uniqueSlots.Clear();
            var slotsByUri = new Dictionary<string, FileSlot>(StringComparer.Ordinal);
            for (int i = 0; i < m_slots.Length; ++i)
            {
                string uri = GsplatLodUri.Resolve(m_baseUri, manifest.Filenames[i]);
                if (!slotsByUri.TryGetValue(uri, out FileSlot slot))
                {
                    slot = new FileSlot(i, uri);
                    slotsByUri.Add(uri, slot);
                    m_uniqueSlots.Add(slot);
                }
                else
                {
                    slot.ManifestIndices.Add(i);
                }
                m_slots[i] = slot;
            }

            if (!string.IsNullOrWhiteSpace(manifest.Environment))
                m_environment = new FileSlot(-1, GsplatLodUri.Resolve(m_baseUri, manifest.Environment));

            m_currentLods = new int[manifest.Leaves.Length];
            m_optimalLods = new int[manifest.Leaves.Length];
            m_desiredLods = new int[manifest.Leaves.Length];
            Array.Fill(m_currentLods, -1);
            Array.Fill(m_optimalLods, -1);
            Array.Fill(m_desiredLods, -1);
            m_forceEvaluate = true;
            m_rangesDirty = true;
            m_lastEvaluationFrame = int.MinValue;

            // Environment resources are independent of octree selection and stay referenced.
            if (m_environment != null)
            {
                MarkWanted(m_environment, -10_000);
                QueueLoad(m_environment);
            }
        }

        void Update()
        {
            if (m_manifest == null || m_shuttingDown) return;
            PollGpuUploads();
            Camera camera = LodCamera ? LodCamera : Camera.main;
            int interval = Mathf.Max(1, LodUpdateIntervalFrames);
            if (m_forceEvaluate || Time.frameCount - m_lastEvaluationFrame >= interval)
            {
                if (camera) Evaluate(camera);
                m_lastEvaluationFrame = Time.frameCount;
                m_forceEvaluate = false;
            }
            PumpLoadQueue();
            TickCooldowns();
        }

        /// <summary>Forces LOD evaluation on the next Update, useful after changing parameters.</summary>
        public void ForceLodUpdate() => m_forceEvaluate = true;

        void Evaluate(Camera camera)
        {
            int maxLod = m_manifest.LodLevels - 1;
            int rangeMin = Mathf.Clamp(LodRangeMin, 0, maxLod);
            int rangeMax = Mathf.Clamp(LodRangeMax, rangeMin, maxLod);
            Vector3 localCameraPosition = transform.InverseTransformPoint(camera.transform.position);
            Vector3 localCameraForward = transform.InverseTransformDirection(camera.transform.forward).normalized;

            foreach (FileSlot slot in m_uniqueSlots)
            {
                slot.Wanted = false;
                slot.WantedReferences = 0;
                slot.Priority = int.MaxValue;
            }
            if (m_environment != null)
            {
                m_environment.Wanted = false;
                m_environment.WantedReferences = 0;
                m_environment.Priority = int.MaxValue;
                MarkWanted(m_environment, -10_000);
            }

            for (int leafIndex = 0; leafIndex < m_manifest.Leaves.Length; ++leafIndex)
            {
                GsplatLodLeaf leaf = m_manifest.Leaves[leafIndex];
                int optimal = GsplatLodSelector.SelectLod(leaf.Bounds, localCameraPosition,
                    localCameraForward, camera.fieldOfView, camera.aspect, LodBaseDistance,
                    LodMultiplier, maxLod, rangeMin, rangeMax, LodBehindPenalty);
                int desired = SelectDesiredLod(leaf, optimal, maxLod);
                m_optimalLods[leafIndex] = optimal;
                m_desiredLods[leafIndex] = desired;

                MarkLodWanted(leaf, desired, desired * 100 + leafIndex);
                PrefetchNextLod(leaf, desired, optimal, leafIndex);

                int previousCurrent = m_currentLods[leafIndex];
                int current = previousCurrent;
                if (!IsLodResident(leaf, current)) current = -1;

                if (IsLodResident(leaf, desired))
                {
                    current = desired;
                }
                else if (current < 0)
                {
                    // First-frame fallback: use any already-resident coarse level, and request
                    // the coarsest legal level so something becomes visible quickly.
                    current = FindResidentCoarserLod(leaf, desired, rangeMax);
                    int coarse = FindAvailableCoarserLod(leaf, rangeMax, desired);
                    MarkLodWanted(leaf, coarse, 50_000 + coarse * 100 + leafIndex);
                }

                m_currentLods[leafIndex] = current;
                if (current != previousCurrent) m_rangesDirty = true;
                MarkLodWanted(leaf, current, -1_000 + current * 100 + leafIndex);
            }

            if (m_rangesDirty) ApplyActiveRanges();
            else SyncActiveRendererSettings();
            ReconcileRequests();
        }

        int SelectDesiredLod(GsplatLodLeaf leaf, int optimal, int maxLod)
        {
            if (LodUnderfillLimit <= 0) return optimal;
            int allowedMax = Mathf.Min(maxLod, optimal + LodUnderfillLimit);
            for (int lod = optimal; lod <= allowedMax; ++lod)
                if (IsLodResident(leaf, lod)) return lod;
            for (int lod = allowedMax; lod >= optimal; --lod)
                if (HasLod(leaf, lod)) return lod;
            return optimal;
        }

        void PrefetchNextLod(GsplatLodLeaf leaf, int desired, int optimal, int leafIndex)
        {
            if (desired < 0 || optimal < 0) return;
            if (desired == optimal)
            {
                MarkLodWanted(leaf, optimal, optimal * 100 + leafIndex);
                return;
            }
            int target = Mathf.Max(optimal, desired - 1);
            for (int lod = target; lod >= optimal; --lod)
            {
                if (!HasLod(leaf, lod)) continue;
                MarkLodWanted(leaf, lod, 10_000 + lod * 100 + leafIndex);
                break;
            }
        }

        void MarkLodWanted(GsplatLodLeaf leaf, int lod, int priority)
        {
            if (!HasLod(leaf, lod)) return;
            MarkWanted(m_slots[leaf.Lods[lod].FileIndex], priority);
        }

        static bool HasLod(GsplatLodLeaf leaf, int lod) =>
            lod >= 0 && lod < leaf.Lods.Length && leaf.Lods[lod].IsPresent;

        bool IsLodResident(GsplatLodLeaf leaf, int lod) =>
            HasLod(leaf, lod) && m_slots[leaf.Lods[lod].FileIndex] is { Status: LoadStatus.Loaded, GpuReady: true };

        int FindResidentCoarserLod(GsplatLodLeaf leaf, int start, int max)
        {
            for (int lod = Mathf.Max(0, start); lod <= max; ++lod)
                if (IsLodResident(leaf, lod)) return lod;
            return -1;
        }

        static int FindAvailableCoarserLod(GsplatLodLeaf leaf, int max, int minimum)
        {
            for (int lod = max; lod >= Mathf.Max(0, minimum); --lod)
                if (HasLod(leaf, lod)) return lod;
            return -1;
        }

        void ApplyActiveRanges()
        {
            foreach (int fileIndex in m_activeRangeFiles)
                m_rangeScratch[fileIndex].Clear();
            m_activeRangeFiles.Clear();

            for (int leafIndex = 0; leafIndex < m_currentLods.Length; ++leafIndex)
            {
                int lod = m_currentLods[leafIndex];
                GsplatLodLeaf leaf = m_manifest.Leaves[leafIndex];
                if (!HasLod(leaf, lod)) continue;
                GsplatLodSlice slice = leaf.Lods[lod];
                int canonicalFileIndex = m_slots[slice.FileIndex].Index;
                if (!m_rangeScratch.TryGetValue(canonicalFileIndex, out List<GsplatActiveRange> ranges))
                {
                    ranges = new List<GsplatActiveRange>();
                    m_rangeScratch.Add(canonicalFileIndex, ranges);
                }
                ranges.Add(new GsplatActiveRange((uint)slice.Offset, (uint)slice.Count));
                m_activeRangeFiles.Add(canonicalFileIndex);
            }

            m_filesToApply.Clear();
            foreach (int fileIndex in m_previousActiveRangeFiles) m_filesToApply.Add(fileIndex);
            foreach (int fileIndex in m_activeRangeFiles) m_filesToApply.Add(fileIndex);
            foreach (int i in m_filesToApply)
            {
                FileSlot slot = m_slots[i];
                if (slot.Status != LoadStatus.Loaded || !slot.Asset || !slot.GpuReady) continue;
                SyncRendererSettings(slot);
                IReadOnlyList<GsplatActiveRange> source = m_activeRangeFiles.Contains(i)
                    ? m_rangeScratch[i] : Array.Empty<GsplatActiveRange>();
                GsplatActiveRange[] normalized = GsplatLodRangeNormalizer.Normalize(source, slot.Asset.SplatCount);
                if (RangesEqual(slot.AppliedRanges, normalized)) continue;

                EnsureRendererBound(slot);
                slot.Renderer.SetActiveRanges(normalized);
                slot.AppliedRanges = normalized;
            }
            m_previousActiveRangeFiles.Clear();
            foreach (int fileIndex in m_activeRangeFiles) m_previousActiveRangeFiles.Add(fileIndex);
            m_rangesDirty = false;

            if (m_environment is { Status: LoadStatus.Loaded, GpuReady: true } && m_environment.Asset)
            {
                SyncRendererSettings(m_environment);
                EnsureRendererBound(m_environment);
                if (m_environment.Renderer.HasActiveRanges)
                    m_environment.Renderer.ClearActiveRanges();
            }
        }

        static bool RangesEqual(IReadOnlyList<GsplatActiveRange> a, IReadOnlyList<GsplatActiveRange> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; ++i)
                if (a[i].Offset != b[i].Offset || a[i].Count != b[i].Count) return false;
            return true;
        }

        void SyncActiveRendererSettings()
        {
            foreach (int fileIndex in m_previousActiveRangeFiles)
            {
                FileSlot slot = m_slots[fileIndex];
                if (slot.Status == LoadStatus.Loaded && slot.Renderer)
                    SyncRendererSettings(slot);
            }
            if (m_environment is { Status: LoadStatus.Loaded } && m_environment.Renderer)
                SyncRendererSettings(m_environment);
        }

        void EnsureRendererBound(FileSlot slot)
        {
            EnsureRendererCreated(slot);
            GsplatRenderer renderer = slot.Renderer;
            SyncRendererSettings(slot);
            renderer.RenderBeforeUploadComplete = false;
            renderer.AsyncUpload = AsyncGpuUpload;
            if (renderer.GsplatAsset != slot.Asset)
            {
                renderer.GsplatAsset = slot.Asset;
                renderer.enabled = true;
                renderer.Update();
            }
        }

        void SyncRendererSettings(FileSlot slot)
        {
            EnsureRendererCreated(slot);
            GsplatRenderer renderer = slot.Renderer;
            slot.GameObject.layer = gameObject.layer;
            renderer.SHDegree = SHDegree;
            renderer.Brightness = Brightness;
            renderer.SplatDownscaleFactor = SplatDownscaleFactor;
            renderer.GammaToLinear = GammaToLinear;
            renderer.RenderOrder = RenderOrder;
        }

        void EnsureRendererCreated(FileSlot slot)
        {
            if (slot.Renderer) return;
            slot.GameObject = new GameObject(slot.Index >= 0 ? $"LOD file {slot.Index}" : "LOD environment");
            slot.GameObject.layer = gameObject.layer;
            slot.GameObject.transform.SetParent(transform, false);
            slot.Renderer = slot.GameObject.AddComponent<GsplatRenderer>();
            slot.Renderer.enabled = false;
        }

        void PrepareGpuUpload(FileSlot slot)
        {
            slot.GpuReady = false;
            slot.AppliedRanges = Array.Empty<GsplatActiveRange>();
            EnsureRendererBound(slot);
            // Explicitly hide the newly decoded file. It must not replace the old visible LOD
            // until the asynchronous GPU upload has reached the complete source buffer.
            slot.Renderer.SetActiveRanges(Array.Empty<GsplatActiveRange>());
            if (slot.Renderer.SplatCount == slot.Asset.SplatCount)
            {
                slot.GpuReady = true;
                m_rangesDirty = true;
                m_forceEvaluate = true;
            }
            else if (!m_uploadingSlots.Contains(slot))
            {
                m_uploadingSlots.Add(slot);
            }
        }

        void PollGpuUploads()
        {
            for (int i = m_uploadingSlots.Count - 1; i >= 0; --i)
            {
                FileSlot slot = m_uploadingSlots[i];
                if (slot.Status != LoadStatus.Loaded || !slot.Asset)
                {
                    m_uploadingSlots.RemoveAt(i);
                    continue;
                }
                Task uploadTask = slot.Renderer.GsplatResource?.UploadTask;
                if (uploadTask is { IsFaulted: true })
                {
                    m_uploadingSlots.RemoveAt(i);
                    FailGpuUpload(slot, uploadTask.Exception?.GetBaseException() ??
                        new IOException($"GPU upload failed for '{slot.Uri}'."));
                    continue;
                }
                if (uploadTask is { IsCanceled: true })
                {
                    m_uploadingSlots.RemoveAt(i);
                    FailGpuUpload(slot, new OperationCanceledException(
                        $"GPU upload was canceled for '{slot.Uri}'."));
                    continue;
                }
                if (slot.Renderer.SplatCount != slot.Asset.SplatCount) continue;
                slot.GpuReady = true;
                m_uploadingSlots.RemoveAt(i);
                m_rangesDirty = true;
                m_forceEvaluate = true;
            }
        }

        void FailGpuUpload(FileSlot slot, Exception exception)
        {
            ReleaseSlotPayload(slot);
            slot.Status = LoadStatus.Failed;
            slot.FailureUntil = Time.unscaledTime + Mathf.Max(0f, FailureCooldownSeconds);
            m_rangesDirty = true;
            m_forceEvaluate = true;
            FileLoadFailed?.Invoke(slot.Index, slot.Uri, exception);
            Debug.LogError($"[Gsplat LOD] GPU upload failed for '{slot.Uri}': {exception.Message}", this);
        }

        void MarkWanted(FileSlot slot, int priority)
        {
            if (slot == null) return;
            slot.Wanted = true;
            ++slot.WantedReferences;
            if (priority < slot.Priority) slot.Priority = priority;
            slot.LastWantedTime = Time.unscaledTime;
            QueueLoad(slot);
        }

        void QueueLoad(FileSlot slot)
        {
            if (slot.Status == LoadStatus.Loaded || slot.Status == LoadStatus.Loading ||
                slot.Status == LoadStatus.Queued) return;
            if (slot.Status == LoadStatus.Failed && Time.unscaledTime < slot.FailureUntil) return;
            slot.Status = LoadStatus.Queued;
            if (!m_loadQueue.Contains(slot)) m_loadQueue.Add(slot);
        }

        void ReconcileRequests()
        {
            foreach (FileSlot slot in m_uniqueSlots) ReconcileSlot(slot);
            if (m_environment != null) ReconcileSlot(m_environment);
            PumpLoadQueue();
        }

        static void ReconcileSlot(FileSlot slot)
        {
            if (slot.Wanted) return;
            if (slot.Status == LoadStatus.Queued) slot.Status = LoadStatus.Unloaded;
            if (slot.Status == LoadStatus.Loading) slot.LoadCancellation?.Cancel();
        }

        void PumpLoadQueue()
        {
            if (m_shuttingDown) return;
            m_loadQueue.RemoveAll(slot => slot.Status != LoadStatus.Queued || !slot.Wanted);
            m_loadQueue.Sort((a, b) =>
            {
                int priority = a.Priority.CompareTo(b.Priority);
                return priority != 0 ? priority : a.Index.CompareTo(b.Index);
            });
            int limit = Mathf.Max(1, MaxConcurrentLoads);
            while (m_activeLoads < limit && m_loadQueue.Count != 0)
            {
                FileSlot slot = m_loadQueue[0];
                m_loadQueue.RemoveAt(0);
                BeginLoad(slot);
            }
        }

        async void BeginLoad(FileSlot slot)
        {
            int session = m_sessionGeneration;
            ++m_activeLoads;
            slot.Status = LoadStatus.Loading;
            slot.LoadCancellation = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime.Token);
            GsplatAsset loadedAsset = null;
            try
            {
                Exception lastError = null;
                int attempts = Mathf.Max(0, MaxRetries) + 1;
                for (int attempt = 0; attempt < attempts; ++attempt)
                {
                    slot.LoadCancellation.Token.ThrowIfCancellationRequested();
                    try
                    {
                        loadedAsset = await LoadGsplatAssetAsync(slot.Uri, slot.LoadCancellation.Token);
                        break;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception exception)
                    {
                        lastError = exception;
                        if (attempt + 1 >= attempts) break;
                        if (RetryDelayMilliseconds > 0)
                            await Task.Delay(RetryDelayMilliseconds, slot.LoadCancellation.Token);
                    }
                }

                if (!loadedAsset)
                    throw lastError ?? new IOException($"Could not load '{slot.Uri}'.");
                slot.LoadCancellation.Token.ThrowIfCancellationRequested();
                if (session != m_sessionGeneration) throw new OperationCanceledException();
                if (slot.Index >= 0)
                    foreach (int fileIndex in slot.ManifestIndices)
                        m_manifest.ValidateFileSplatCount(fileIndex, loadedAsset.SplatCount);

                slot.Asset = loadedAsset;
                loadedAsset = null;
                slot.Status = LoadStatus.Loaded;
                slot.FailureUntil = 0f;
                PrepareGpuUpload(slot);
                m_forceEvaluate = true;
            }
            catch (OperationCanceledException)
            {
                if (session == m_sessionGeneration)
                {
                    ReleaseSlotPayload(slot);
                    slot.Status = LoadStatus.Unloaded;
                }
            }
            catch (Exception exception)
            {
                if (session == m_sessionGeneration)
                {
                    // The decoded asset is transferred to the slot before renderer binding.
                    // If binding or a synchronous upload throws, release both the asset and
                    // any partially-created renderer resources before a later retry.
                    ReleaseSlotPayload(slot);
                    slot.Status = LoadStatus.Failed;
                    slot.FailureUntil = Time.unscaledTime + Mathf.Max(0f, FailureCooldownSeconds);
                    FileLoadFailed?.Invoke(slot.Index, slot.Uri, exception);
                    Debug.LogError($"[Gsplat LOD] Failed to load '{slot.Uri}': {exception.Message}", this);
                }
            }
            finally
            {
                DestroyAsset(loadedAsset);
                slot.LoadCancellation?.Dispose();
                slot.LoadCancellation = null;
                if (session == m_sessionGeneration)
                {
                    --m_activeLoads;
                    PumpLoadQueue();
                }
            }
        }

        async Task<GsplatAsset> LoadGsplatAssetAsync(string uri, CancellationToken token)
        {
            if (GsplatLodUri.IsUnpackedSogMetadata(uri))
            {
                string metadata = Encoding.UTF8.GetString(await LoadBytesAsync(uri, token));
                string directory = GsplatLodUri.DirectoryOf(uri);
                string[] files = CollectSogFiles(metadata);
                var resolved = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    token.ThrowIfCancellationRequested();
                    resolved[NormalizeResourceName(file)] = await LoadBytesAsync(
                        GsplatLodUri.Resolve(directory, file), token);
                }
                token.ThrowIfCancellationRequested();
                return GsplatRuntimeLoader.LoadUnpackedSog(metadata,
                    filename => resolved.TryGetValue(NormalizeResourceName(filename), out byte[] bytes)
                        ? bytes : throw new FileNotFoundException($"SOG dependency '{filename}' was not loaded."),
                    Compression, m_sessionCoordinates);
            }

            byte[] data = await LoadBytesAsync(uri, token);
            token.ThrowIfCancellationRequested();
            return GsplatRuntimeLoader.Load(data, GsplatLodUri.ExtensionOf(uri), Compression,
                m_sessionCoordinates);
        }

        static string[] CollectSogFiles(string metadata)
        {
            object root = MiniJson.Deserialize(metadata);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<object>();
            stack.Push(root);
            while (stack.Count != 0)
            {
                object value = stack.Pop();
                if (value is Dictionary<string, object> dictionary)
                {
                    foreach (var pair in dictionary)
                    {
                        if (pair.Key == "files" && pair.Value is List<object> list)
                        {
                            for (int i = 0; i < list.Count; ++i)
                                files.Add(NormalizeResourceName(JsonValues.String(list[i], $"files[{i}]")));
                        }
                        else if (pair.Value != null) stack.Push(pair.Value);
                    }
                }
                else if (value is List<object> list)
                {
                    foreach (object child in list)
                        if (child != null) stack.Push(child);
                }
            }
            if (files.Count == 0) throw new FormatException("Unpacked SOG metadata references no files.");
            var result = new string[files.Count];
            files.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        static string NormalizeResourceName(string value)
        {
            string normalized = value.Replace('\\', '/');
            while (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized.Substring(2);
            return normalized;
        }

        static async Task<byte[]> LoadBytesAsync(string uri, CancellationToken token)
        {
            if (GsplatLodUri.RequiresUnityWebRequest(uri))
            {
                using var request = UnityWebRequest.Get(uri);
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (token.IsCancellationRequested)
                    {
                        request.Abort();
                        token.ThrowIfCancellationRequested();
                    }
                    await Task.Yield();
                }
                token.ThrowIfCancellationRequested();
                if (request.result != UnityWebRequest.Result.Success)
                    throw new IOException($"GET {uri} failed: {request.error}");
                return request.downloadHandler.data ?? throw new IOException($"GET {uri} returned no data.");
            }

            string path = GsplatLodUri.LocalPath(uri);
            byte[] bytes = await Task.Run(() => File.ReadAllBytes(path), token);
            token.ThrowIfCancellationRequested();
            return bytes;
        }

        void TickCooldowns()
        {
            float now = Time.unscaledTime;
            foreach (FileSlot slot in m_uniqueSlots)
                if (!slot.Wanted && slot.Status == LoadStatus.Loaded &&
                    now - slot.LastWantedTime >= Mathf.Max(0f, CacheCooldownSeconds))
                    UnloadSlot(slot);
        }

        void UnloadSlot(FileSlot slot)
        {
            ReleaseSlotPayload(slot);
            slot.Status = LoadStatus.Unloaded;
            m_rangesDirty = true;
            if (slot.GameObject)
            {
                if (Application.isPlaying) Destroy(slot.GameObject);
                else DestroyImmediate(slot.GameObject);
                slot.GameObject = null;
                slot.Renderer = null;
            }
            m_forceEvaluate = true;
        }

        void OnDisable() => Shutdown();
        void OnDestroy() => Shutdown();

        void Shutdown()
        {
            if (m_shuttingDown) return;
            m_shuttingDown = true;
            ++m_sessionGeneration;
            m_lifetime?.Cancel();
            m_lifetime?.Dispose();
            m_lifetime = null;
            m_loadQueue.Clear();
            m_uploadingSlots.Clear();
            m_activeLoads = 0;

            foreach (FileSlot slot in m_uniqueSlots) DestroySlot(slot);
            if (m_environment != null) DestroySlot(m_environment);
            m_slots = null;
            m_uniqueSlots.Clear();
            m_environment = null;
            m_manifest = null;
            m_sessionCoordinates = global::Gsplat.SourceCoordinates.Unspecified;
            m_currentLods = null;
            m_optimalLods = null;
            m_desiredLods = null;
            m_rangesDirty = false;
            m_rangeScratch.Clear();
            m_activeRangeFiles.Clear();
            m_previousActiveRangeFiles.Clear();
            m_filesToApply.Clear();
        }

        void ReleaseSlotPayload(FileSlot slot)
        {
            m_uploadingSlots.Remove(slot);
            if (slot.Renderer)
            {
                // Disabling first unregisters the renderer and disposes any partial graphics
                // resources even when its explicit Update failed during initial binding.
                slot.Renderer.enabled = false;
                slot.Renderer.GsplatAsset = null;
            }
            DestroyAsset(slot.Asset);
            slot.Asset = null;
            slot.GpuReady = false;
            slot.AppliedRanges = Array.Empty<GsplatActiveRange>();
        }

        static void DestroySlot(FileSlot slot)
        {
            if (slot == null) return;
            slot.LoadCancellation?.Cancel();
            if (slot.Renderer)
            {
                slot.Renderer.GsplatAsset = null;
                if (slot.Renderer.enabled) slot.Renderer.Update();
            }
            DestroyAsset(slot.Asset);
            if (slot.GameObject)
            {
                if (Application.isPlaying) Destroy(slot.GameObject);
                else DestroyImmediate(slot.GameObject);
            }
        }

        static void DestroyAsset(GsplatAsset asset)
        {
            if (!asset) return;
            if (Application.isPlaying) Destroy(asset);
            else DestroyImmediate(asset);
        }

        void OnValidate()
        {
            LodBaseDistance = Mathf.Max(0.1f, LodBaseDistance);
            LodMultiplier = Mathf.Max(1.2f, LodMultiplier);
            LodBehindPenalty = Mathf.Max(1f, LodBehindPenalty);
            LodRangeMin = Mathf.Max(0, LodRangeMin);
            LodRangeMax = Mathf.Max(LodRangeMin, LodRangeMax);
            MaxConcurrentLoads = Mathf.Clamp(MaxConcurrentLoads, 1, 16);
            MaxRetries = Mathf.Clamp(MaxRetries, 0, 8);
            m_forceEvaluate = true;
        }
    }

    /// <summary>URI/path resolution shared by runtime streaming and editor import.</summary>
    public static class GsplatLodUri
    {
        public const string StreamingAssetsScheme = "streaming-assets://";

        public static string ResolveManifestUri(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Manifest URI is empty.", nameof(value));
            value = value.Replace('\\', '/');
            if (value.StartsWith(StreamingAssetsScheme, StringComparison.OrdinalIgnoreCase))
                return ExpandStreamingAssets(value.Substring(StreamingAssetsScheme.Length));
            return HasExplicitUriScheme(value) || Path.IsPathRooted(value)
                ? value : Resolve(StreamingAssetsScheme, value);
        }

        public static string Resolve(string baseUri, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative)) throw new ArgumentException("Resource URI is empty.", nameof(relative));
            relative = relative.Replace('\\', '/');
            if (relative.StartsWith(StreamingAssetsScheme, StringComparison.OrdinalIgnoreCase))
                return ExpandStreamingAssets(relative.Substring(StreamingAssetsScheme.Length));
            if (HasExplicitUriScheme(relative)) return relative;

            baseUri = (baseUri ?? string.Empty).Replace('\\', '/');
            if (baseUri.StartsWith(StreamingAssetsScheme, StringComparison.OrdinalIgnoreCase))
            {
                string suffix = baseUri.Substring(StreamingAssetsScheme.Length).Trim('/');
                string combined = relative.StartsWith("/", StringComparison.Ordinal)
                    ? relative.TrimStart('/')
                    : string.IsNullOrEmpty(suffix) ? relative : $"{suffix}/{relative}";
                return ExpandStreamingAssets(combined);
            }

            if (IsWindowsDrive(relative))
                return Path.GetFullPath(relative.Replace('/', Path.DirectorySeparatorChar));

            // URI references must be resolved before Path.IsPathRooted: '/assets/x' is an
            // origin-root HTTP/content/jar reference and '//host/x' is protocol-relative,
            // not a local filesystem path. Preserve the archive portion of Android jar URIs
            // for origin-root references because System.Uri otherwise returns "jar:/...".
            bool uriBase = HasExplicitUriScheme(baseUri);
            if (uriBase && baseUri.StartsWith("jar:", StringComparison.OrdinalIgnoreCase) &&
                relative.StartsWith("/", StringComparison.Ordinal) &&
                !relative.StartsWith("//", StringComparison.Ordinal))
            {
                int archiveEnd = baseUri.IndexOf('!');
                if (archiveEnd >= 0)
                    return baseUri.Substring(0, archiveEnd + 1) + relative;
            }

            if (uriBase && Uri.TryCreate(EnsureDirectoryUri(baseUri), UriKind.Absolute,
                    out Uri absoluteBase))
                return new Uri(absoluteBase, relative).AbsoluteUri;

            if (Path.IsPathRooted(relative))
                return Path.GetFullPath(relative.Replace('/', Path.DirectorySeparatorChar));
            string basePath = string.IsNullOrWhiteSpace(baseUri) ? Directory.GetCurrentDirectory() : baseUri;
            return Path.GetFullPath(Path.Combine(basePath, relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        public static string DirectoryOf(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri)) return string.Empty;
            if (Uri.TryCreate(uri, UriKind.Absolute, out Uri absolute) && !IsWindowsDrive(uri))
                return new Uri(absolute, ".").AbsoluteUri;
            return Path.GetDirectoryName(Path.GetFullPath(uri)) ?? string.Empty;
        }

        public static bool IsUnpackedSogMetadata(string uri) =>
            string.Equals(Path.GetFileName(UriPath(uri)), "meta.json", StringComparison.OrdinalIgnoreCase);

        public static string ExtensionOf(string uri) => Path.GetExtension(UriPath(uri));

        public static bool RequiresUnityWebRequest(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri parsed) || IsWindowsDrive(uri)) return false;
            return parsed.Scheme is "http" or "https" or "jar" or "content";
        }

        public static string LocalPath(string uri)
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out Uri parsed) && parsed.IsFile) return parsed.LocalPath;
            return uri;
        }

        static string UriPath(string uri)
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out Uri parsed) && !IsWindowsDrive(uri))
                return Uri.UnescapeDataString(parsed.AbsolutePath);
            int query = uri.IndexOfAny(new[] { '?', '#' });
            return query >= 0 ? uri.Substring(0, query) : uri;
        }

        static string ExpandStreamingAssets(string suffix)
        {
            string root = Application.streamingAssetsPath.Replace('\\', '/');
            suffix = suffix.Replace('\\', '/').TrimStart('/');
            return string.IsNullOrEmpty(suffix) ? root : Resolve(root, suffix);
        }

        static bool HasExplicitUriScheme(string value) => !IsWindowsDrive(value) &&
            Uri.TryCreate(value, UriKind.Absolute, out Uri parsed) && !string.IsNullOrEmpty(parsed.Scheme) &&
            value.IndexOf(':') > 0;
        static bool IsWindowsDrive(string value) => value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':';
        static string EnsureDirectoryUri(string value) => string.IsNullOrEmpty(value) || value.EndsWith("/")
            ? value : value + "/";
    }
}
#endif
