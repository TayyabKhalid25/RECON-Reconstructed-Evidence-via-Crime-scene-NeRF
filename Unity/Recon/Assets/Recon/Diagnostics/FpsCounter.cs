using UnityEngine;

namespace Recon.Diagnostics
{
    /// <summary>
    /// Frames-per-second and frame-time tracking with an optional on-screen overlay. IMGUI on
    /// purpose: it needs no Canvas, no EventSystem and no prefab, so it works in a scene created
    /// by script and in Tayyab's SampleScene alike. PerfLog reads <see cref="Fps"/> once a second.
    /// </summary>
    [AddComponentMenu("Recon/Diagnostics/FPS Counter")]
    public sealed class FpsCounter : MonoBehaviour
    {
        [SerializeField] bool showOverlay = true;
        [SerializeField, Range(0.01f, 1f)] float smoothing = 0.1f;

        float m_smoothedFrameTime = -1f;
        int m_framesThisSecond;
        float m_secondStart;
        float m_lastWholeSecondFps;

        /// <summary>Exponentially smoothed instantaneous FPS.</summary>
        public float Fps => m_smoothedFrameTime > 0f ? 1f / m_smoothedFrameTime : 0f;

        /// <summary>Smoothed frame time in milliseconds.</summary>
        public float FrameTimeMs => m_smoothedFrameTime * 1000f;

        /// <summary>Frames actually rendered in the last completed wall-clock second.</summary>
        public float LastWholeSecondFps => m_lastWholeSecondFps;

        public bool ShowOverlay { get => showOverlay; set => showOverlay = value; }

        void OnEnable()
        {
            m_smoothedFrameTime = -1f;
            m_framesThisSecond = 0;
            m_secondStart = Time.unscaledTime;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;
            m_smoothedFrameTime = m_smoothedFrameTime < 0f ? dt : Mathf.Lerp(m_smoothedFrameTime, dt, smoothing);

            m_framesThisSecond++;
            float elapsed = Time.unscaledTime - m_secondStart;
            if (elapsed >= 1f)
            {
                m_lastWholeSecondFps = m_framesThisSecond / elapsed;
                m_framesThisSecond = 0;
                m_secondStart = Time.unscaledTime;
            }
        }

        void OnGUI()
        {
            if (!showOverlay) return;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Screen.dpi > 0 ? Screen.dpi * 0.18f : 28f),
                fontStyle = FontStyle.Bold,
                normal = { textColor = Fps >= 30f ? Color.green : (Fps >= 20f ? Color.yellow : Color.red) }
            };
            GUI.Label(new Rect(16, 16, 600, 80), $"{Fps:F1} fps  {FrameTimeMs:F1} ms", style);
        }
    }
}
