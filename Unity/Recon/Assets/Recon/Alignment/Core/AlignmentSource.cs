namespace Recon.Alignment
{
    /// <summary>
    /// How the twin got placed in the room. The fallback chain from docs/ANCHORING.md, implemented
    /// in this order and always shown to the user, because what placed the scene decides what may
    /// be claimed about a measurement taken inside it.
    /// </summary>
    public enum AlignmentSource
    {
        /// <summary>Nothing has placed the scene; it is sitting wherever SceneRoot was left.</summary>
        None = 0,

        /// <summary>Printed marker detected. Authoritative: pose and metric scale in one detection.</summary>
        Marker = 1,

        /// <summary>Cloud anchor resolved plus the stored anchor-to-scene transform. FTW-52/FTW-53.</summary>
        CloudAnchor = 2,

        /// <summary>User tapped a detected plane. Keeps the demo alive; produces no evidential measurement.</summary>
        Manual = 3,
    }

    /// <summary>
    /// The rules attached to each path, kept pure so they are tested rather than trusted.
    /// </summary>
    public static class AlignmentSources
    {
        /// <summary>
        /// True only for <see cref="AlignmentSource.Marker"/>.
        ///
        /// The marker is measured against a physical 170 mm edge (docs/FRAMES.md) and is the only
        /// path the project's 2 cm positional accuracy claim belongs to. A cloud anchor resolve
        /// adds re-localisation error that Google publishes no figure for and that FTW-56 has not
        /// measured yet; manual placement is a fingertip on a plane. Both are usable and neither
        /// is verified, and the UI must say so (docs/ANCHORING.md, "The fallback chain").
        ///
        /// Note this is about <b>position</b>. Geometry stays metric in every case, because
        /// unitScale came from the marker at export time and SceneRoot applies it regardless.
        /// </summary>
        public static bool IsMetricVerified(AlignmentSource source) => source == AlignmentSource.Marker;

        /// <summary>
        /// Higher wins. Lets a marker re-detection take over from a manual placement without the
        /// reverse ever happening, which is what stops a stray tap from moving a marker-aligned scene.
        /// </summary>
        public static int Priority(AlignmentSource source)
        {
            switch (source)
            {
                case AlignmentSource.Marker: return 3;
                case AlignmentSource.CloudAnchor: return 2;
                case AlignmentSource.Manual: return 1;
                default: return 0;
            }
        }

        /// <summary>The one line the AlignmentIndicator puts on screen.</summary>
        public static string Label(AlignmentSource source)
        {
            switch (source)
            {
                case AlignmentSource.Marker: return "MARKER — METRIC";
                case AlignmentSource.CloudAnchor: return "CLOUD ANCHOR — NOT METRIC VERIFIED";
                case AlignmentSource.Manual: return "MANUAL — NOT METRIC VERIFIED";
                default: return "NOT ALIGNED";
            }
        }
    }
}
