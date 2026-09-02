using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Recon.Diagnostics.Core;
using Recon.Rendering;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Recon.Diagnostics
{
    /// <summary>
    /// Writes one sample per second to a JSON file under persistentDataPath/perf so the FPS rows
    /// in docs/RESULTS.md come from a file pulled with adb, with the device, graphics API,
    /// multithreaded-rendering flag and splat count recorded next to every number.
    ///
    /// Pull with:  adb pull /sdcard/Android/data/&lt;package&gt;/files/perf/ .
    /// </summary>
    [AddComponentMenu("Recon/Diagnostics/Perf Log")]
    [RequireComponent(typeof(FpsCounter))]
    public sealed class PerfLog : MonoBehaviour
    {
        [Tooltip("Free text for the run, e.g. 'still' or 'walking'. Changeable at runtime via SetLabel.")]
        [SerializeField] string label = "unlabelled";
        [SerializeField, Min(1f)] float flushEverySeconds = 5f;
        [SerializeField] bool logSummaryOnQuit = true;

        [Serializable]
        public sealed class Sample
        {
            public double t;            // seconds since the log started
            public float fps;           // frames rendered in that wall-clock second
            public float frameMs;       // smoothed frame time at sample time
            public long allocatedMb;    // Profiler.GetTotalAllocatedMemoryLong
            public int splats;          // ISplatRenderer.SplatCount, 0 if none
            public string label;
        }

        [Serializable]
        public sealed class Run
        {
            public string startedUtc;
            public string device;
            public string gpu;
            public string graphicsApi;
            public bool multithreadedRendering;
            public string os;
            public string unity;
            public string appVersion;
            public string renderer;
            public float renderScale;
            public int screenW, screenH;
            public List<Sample> samples = new List<Sample>();
            public PerfSummaryDto summary;
        }

        [Serializable]
        public sealed class PerfSummaryDto
        {
            public int count; public double min, p5, median, mean, p95, max;
            public static PerfSummaryDto From(PerfSummary s) => new PerfSummaryDto
                { count = s.Count, min = s.Min, p5 = s.P5, median = s.Median, mean = s.Mean, p95 = s.P95, max = s.Max };
        }

        FpsCounter m_fps;
        Run m_run;
        string m_path;
        float m_start;
        float m_lastSample;
        float m_lastFlush;
        readonly List<double> m_fpsSamples = new List<double>();

        public string FilePath => m_path;
        public IReadOnlyList<double> FpsSamples => m_fpsSamples;

        public void SetLabel(string value) => label = string.IsNullOrEmpty(value) ? "unlabelled" : value;

        void Awake()
        {
            m_fps = GetComponent<FpsCounter>();
            var dir = Path.Combine(Application.persistentDataPath, "perf");
            Directory.CreateDirectory(dir);
            m_path = Path.Combine(dir, $"perf-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            m_run = new Run
            {
                startedUtc = DateTime.UtcNow.ToString("o"),
                device = $"{SystemInfo.deviceModel} ({SystemInfo.processorType})",
                gpu = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                multithreadedRendering = SystemInfo.graphicsMultiThreaded,
                os = SystemInfo.operatingSystem,
                unity = Application.unityVersion,
                appVersion = Application.version,
                renderer = SplatRendererProvider.Current?.RendererName ?? "none",
                renderScale = urp != null ? urp.renderScale : 1f,
                screenW = Screen.width,
                screenH = Screen.height,
            };
            m_start = m_lastSample = m_lastFlush = Time.unscaledTime;
            Debug.Log($"[Recon] PerfLog → {m_path} | {m_run.device} | {m_run.gpu} | {m_run.graphicsApi} | MT={m_run.multithreadedRendering}");
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (now - m_lastSample < 1f) return;
            m_lastSample = now;

            var renderer = SplatRendererProvider.Current;
            var sample = new Sample
            {
                t = now - m_start,
                fps = m_fps.LastWholeSecondFps,
                frameMs = m_fps.FrameTimeMs,
                allocatedMb = Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024),
                splats = renderer != null ? renderer.SplatCount : 0,
                label = label,
            };
            // First second has no complete window yet; skip it so p5 is not dragged down by startup.
            if (sample.t >= 2.0 && sample.fps > 0f)
            {
                m_run.samples.Add(sample);
                m_fpsSamples.Add(sample.fps);
            }

            if (now - m_lastFlush >= flushEverySeconds)
            {
                m_lastFlush = now;
                Flush();
            }
        }

        public void Flush()
        {
            if (m_fpsSamples.Count > 0) m_run.summary = PerfSummaryDto.From(PerfStats.Summarize(m_fpsSamples));
            try
            {
                File.WriteAllText(m_path, JsonConvert.SerializeObject(m_run, Formatting.Indented));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Recon] PerfLog flush failed: {e.Message}");
            }
        }

        void OnApplicationPause(bool paused) { if (paused) Flush(); }

        void OnDestroy()
        {
            Flush();
            if (logSummaryOnQuit && m_fpsSamples.Count > 0)
                Debug.Log($"[Recon] PerfLog summary ({label}): {PerfStats.Summarize(m_fpsSamples)}");
        }
    }
}
