using System;
using UnityEngine;

namespace Recon.Alignment
{
    /// <summary>
    /// Which path in the docs/ANCHORING.md fallback chain placed the twin, and whether a
    /// measurement taken inside it may be quoted. Every aligner reports here; the UI reads here.
    ///
    /// <see cref="MetricVerified"/> is true only for the marker. The marker is measured against a
    /// physical 170 mm edge and is the only path the project's 2 cm positional accuracy claim
    /// belongs to; a cloud anchor resolve adds error nobody has measured yet (FTW-56) and manual
    /// placement is a fingertip. The rule itself lives in <see cref="AlignmentSources"/>, which is
    /// pure and unit tested.
    ///
    /// Note this is about <b>position</b>. Geometry stays metric either way: unitScale came from
    /// the marker at export time and SceneRoot applies it regardless of how the scene was placed.
    /// </summary>
    [AddComponentMenu("Recon/Alignment/Alignment Status")]
    [DisallowMultipleComponent]
    public sealed class AlignmentStatus : MonoBehaviour
    {
        public AlignmentSource Source { get; private set; } = AlignmentSource.None;

        public bool MetricVerified => AlignmentSources.IsMetricVerified(Source);

        /// <summary>When the current alignment was last set or refreshed. Default value while unaligned.</summary>
        public DateTime LastUpdateUtc { get; private set; }

        /// <summary>Seconds since the last update, or -1 while nothing has aligned.</summary>
        public double SecondsSinceUpdate =>
            LastUpdateUtc == default ? -1 : (DateTime.UtcNow - LastUpdateUtc).TotalSeconds;

        /// <summary>Free text from the aligner, e.g. the pose it used. Shown under the label.</summary>
        public string Detail { get; private set; }

        /// <summary>The line the indicator shows.</summary>
        public string Label => AlignmentSources.Label(Source);

        public event Action<AlignmentStatus> Changed;

        /// <summary>
        /// Records that <paramref name="source"/> has placed or refreshed the scene. Last writer
        /// wins on purpose: a marker re-detection refreshing a pose and a user deliberately taking
        /// over with manual placement are both legitimate, and gating that here would fight the
        /// aligners' own enable flags.
        /// </summary>
        public void Set(AlignmentSource source, string detail = null)
        {
            bool changed = Source != source;
            Source = source;
            Detail = detail;
            LastUpdateUtc = DateTime.UtcNow;

            if (changed)
                Debug.Log($"[Recon] Alignment → {Label}" + (string.IsNullOrEmpty(detail) ? "" : " | " + detail));

            Changed?.Invoke(this);
        }

        public void Clear()
        {
            Source = AlignmentSource.None;
            Detail = null;
            LastUpdateUtc = default;
            Changed?.Invoke(this);
        }

        static AlignmentStatus s_instance;

        /// <summary>The scene's AlignmentStatus, or null. Aligners use this rather than a serialized reference.</summary>
        public static AlignmentStatus Find()
        {
            if (s_instance != null) return s_instance;
            s_instance = FindFirstObjectByType<AlignmentStatus>(FindObjectsInactive.Exclude);
            return s_instance;
        }

        void OnEnable() { s_instance = this; }
        void OnDestroy() { if (s_instance == this) s_instance = null; }
    }
}
