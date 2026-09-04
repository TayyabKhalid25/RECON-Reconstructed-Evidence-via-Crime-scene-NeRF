using System;
using System.IO;
using System.Threading.Tasks;
using Recon.Alignment;
using Recon.Config;
using Recon.Contract;
using Recon.Rendering;
using UnityEngine;

namespace Recon.Scenes
{
    /// <summary>
    /// The FTW-30 / I1 path: load a .ply that was pushed to the phone by hand
    /// (Unity/push-dev-scene.ps1) from persistentDataPath/scenes/dev, validate its metadata.json,
    /// scale SceneRoot by unitScale, and render it under SceneRoot. No API, no marker: this is
    /// "file moved by hand" from the handbook's I1 gate, and the way the mobile FPS number is measured.
    ///
    /// Without a metadata.json the scene renders in raw scene units and is labelled NOT METRIC; that
    /// is acceptable for a frame-rate measurement and for nothing else.
    /// </summary>
    [AddComponentMenu("Recon/Scenes/Dev Splat Loader")]
    public sealed class DevSplatLoader : MonoBehaviour
    {
        [SerializeField] bool loadOnStart = true;
        [SerializeField] bool showStatus = true;

        public string Status { get; private set; } = "idle";
        public float Progress { get; private set; }
        public bool IsMetric { get; private set; }

        async void Start()
        {
            if (!loadOnStart) return;
            try { await LoadDevSceneAsync(); }
            catch (Exception e)
            {
                Status = "FAILED: " + e.Message;
                Debug.LogException(e);
            }
        }

        public string DevFolder => Path.Combine(Application.persistentDataPath, ReconSettings.Instance.devSceneFolder);

        public async Task LoadDevSceneAsync()
        {
            var settings = ReconSettings.Instance;
            var folder = DevFolder;
            var ply = Path.Combine(folder, settings.plyFileName);
            var meta = Path.Combine(folder, settings.metadataFileName);

            if (!File.Exists(ply))
            {
                Status = $"No dev scene at {ply}. Push one with Unity/push-dev-scene.ps1";
                Debug.LogWarning("[Recon] " + Status);
                return;
            }

            var root = SceneRoot.Find();
            var renderer = SplatRendererProvider.Current;
            if (root == null || renderer == null)
            {
                Status = "Scene is missing SceneRoot or an ISplatRenderer";
                return;
            }

            IsMetric = false;
            if (File.Exists(meta))
            {
                SceneMetadata m;
                try { m = SceneMetadata.FromJson(File.ReadAllText(meta)); }
                catch (Exception e)
                {
                    Status = "metadata.json unreadable: " + e.Message;
                    Debug.LogError("[Recon] " + Status);
                    return;
                }
                var check = SceneMetadataValidator.Validate(m);
                foreach (var w in check.Warnings) Debug.LogWarning("[Recon] metadata warning: " + w);
                if (!check.IsValid)
                {
                    // Refuse loudly. A wrong-frame or non-metric scene must not be rendered as if it were fine.
                    Status = check.ToString();
                    Debug.LogError("[Recon] " + Status);
                    root.ClearUnitScale();
                    return;
                }
                root.ApplyUnitScale(m.UnitScale);
                IsMetric = true;
            }
            else
            {
                root.ClearUnitScale();
                Debug.LogWarning($"[Recon] No {settings.metadataFileName} next to the .ply: rendering in raw scene units, NOT METRIC. Fine for FPS, for nothing else.");
            }

            Status = "Loading " + Path.GetFileName(ply) + "…";
            var result = await renderer.LoadAsync(ply, root.transform, new Progress<float>(p => Progress = p));
            Status = $"{renderer.RendererName}: {result}" + (IsMetric ? $" | unitScale {root.UnitScale:F6}" : " | NOT METRIC");
            Debug.Log("[Recon] " + Status);
        }

        void OnGUI()
        {
            if (!showStatus) return;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Screen.dpi > 0 ? Screen.dpi * 0.09f : 14f),
                wordWrap = true,
                normal = { textColor = IsMetric ? Color.white : new Color(1f, 0.6f, 0.2f) }
            };
            GUI.Label(new Rect(16, Screen.height - 140, Screen.width - 32, 124), Status, style);
        }
    }
}
