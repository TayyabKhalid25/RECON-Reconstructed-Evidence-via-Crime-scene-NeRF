using Recon.Alignment;
using UnityEngine;

namespace Recon.UI
{
    /// <summary>
    /// One line, always on screen: which path placed the twin, and whether its position is metric
    /// verified. docs/ANCHORING.md requires the active fallback path to be visible, because a
    /// manually placed scene looks exactly like a marker-aligned one and only one of them may be
    /// measured. Orange for anything unverified, so it reads as a warning at a glance and in a
    /// screen recording of a demo.
    ///
    /// IMGUI, like FpsCounter: no Canvas, no prefab.
    /// </summary>
    [AddComponentMenu("Recon/UI/Alignment Indicator")]
    [DisallowMultipleComponent]
    public sealed class AlignmentIndicator : MonoBehaviour
    {
        [SerializeField] bool show = true;

        static readonly Color Orange = new Color(1f, 0.6f, 0.2f);

        AlignmentStatus m_status;

        void Awake() => m_status = AlignmentStatus.Find();

        void OnGUI()
        {
            if (!show) return;
            if (m_status == null) m_status = AlignmentStatus.Find();

            var line = m_status == null ? "NOT ALIGNED (no AlignmentStatus)" : m_status.Label;
            bool verified = m_status != null && m_status.MetricVerified;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Screen.dpi > 0 ? Screen.dpi * 0.10f : 18f),
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                normal = { textColor = verified ? Color.white : Orange }
            };

            GUI.Label(new Rect(16, 56, Screen.width - 32, 40), line, style);
        }
    }
}
