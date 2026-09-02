using Recon.Config;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Recon.Alignment
{
    /// <summary>
    /// Puts the twin back in the room by the printed marker. Priority 1 of the fallback chain in
    /// docs/ANCHORING.md, and the only path that is metric verified: one detection gives position,
    /// rotation <i>and</i> the physical reference the 2 cm accuracy claim rests on.
    ///
    /// It moves <see cref="SceneRoot"/> and nothing else, and it <b>never touches scale</b>.
    /// Scaling is <c>SceneRoot.ApplyUnitScale(metadata.unitScale)</c>, once, from the .ply's own
    /// metadata (docs/FRAMES.md). "The twin is the wrong size" is never fixed here.
    ///
    /// AR Foundation 6.x: <c>ARTrackedImageManager.trackablesChanged</c> replaces the deprecated
    /// <c>trackedImagesChanged</c>, and carries <c>.added</c> / <c>.updated</c> / <c>.removed</c>.
    /// </summary>
    [AddComponentMenu("Recon/Alignment/Marker Aligner")]
    [DisallowMultipleComponent]
    public sealed class MarkerAligner : MonoBehaviour
    {
        [SerializeField, Tooltip("Left empty, the manager on the XR Origin is found at Awake.")]
        ARTrackedImageManager trackedImageManager;

        [SerializeField, Tooltip("Re-align every time the marker is seen again. This is the drift correction: an AR session's pose slowly walks away from the room, and re-detecting is cheaper and more accurate than a cloud anchor resolve (docs/ANCHORING.md).")]
        bool correctDriftOnRedetect = true;

        /// <summary>How many times the marker has aligned the scene. 0 means the marker has never been seen.</summary>
        public int AlignmentCount { get; private set; }

        /// <summary>The reference image this aligner answers to, from the ReconSettings asset.</summary>
        public string MarkerReferenceName => ReconSettings.Instance.markerReferenceName;

        void Awake()
        {
            if (trackedImageManager == null)
                trackedImageManager = FindFirstObjectByType<ARTrackedImageManager>(FindObjectsInactive.Include);

            if (trackedImageManager == null)
            {
                Debug.LogError("[Recon] MarkerAligner found no ARTrackedImageManager. Run Recon → Bootstrap ReconAR scene.");
                return;
            }

            WarnIfMarkerSizeIsNotSet();
        }

        void OnEnable()
        {
            if (trackedImageManager != null) trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
        }

        void OnDisable()
        {
            if (trackedImageManager != null) trackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
        }

        void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
        {
            foreach (var image in args.added) Consider(image);
            foreach (var image in args.updated) Consider(image);
        }

        void Consider(ARTrackedImage image)
        {
            if (image == null) return;

            // Limited tracking means the pose is extrapolated, not measured. Aligning off it puts
            // the twin somewhere plausible and wrong, which is worse than staying put.
            if (image.trackingState != TrackingState.Tracking) return;

            if (image.referenceImage.name != MarkerReferenceName) return;

            if (AlignmentCount > 0 && !correctDriftOnRedetect) return;

            var root = SceneRoot.Find();
            if (root == null) return;

            var pose = new Pose(image.transform.position, image.transform.rotation);
            root.SetPose(pose);
            AlignmentCount++;

            var detail = $"marker '{MarkerReferenceName}' at pos {pose.position} rot {pose.rotation.eulerAngles}";
            AlignmentStatus.Find()?.Set(AlignmentSource.Marker, detail);

            // The first alignment is the one worth a log line with the pose in it: `adb logcat -s Unity`
            // showing it is how a device session is confirmed to have worked at all. Re-detections
            // are frequent and would drown the log.
            if (AlignmentCount == 1)
                Debug.Log($"[Recon] Marker alignment #1: {detail}. Scale untouched; unitScale stays SceneRoot's job.");
        }

        /// <summary>
        /// docs/FRAMES.md: "set the reference image physicalSize to 0.170 m. If it is left unset,
        /// ARCore estimates the marker size and the alignment scale drifts." A silent drift is
        /// expensive to find on a phone, so say it at startup instead.
        /// </summary>
        void WarnIfMarkerSizeIsNotSet()
        {
            var library = trackedImageManager.referenceLibrary;
            if (library == null)
            {
                Debug.LogError("[Recon] ARTrackedImageManager has no reference image library; the marker cannot be detected.");
                return;
            }

            float expected = ReconSettings.Instance.markerPhysicalSizeMetres;
            bool found = false;
            for (int i = 0; i < library.count; i++)
            {
                var entry = library[i];
                if (entry.name != MarkerReferenceName) continue;
                found = true;

                if (!entry.specifySize || entry.size.x <= 0f)
                    Debug.LogError($"[Recon] Reference image '{entry.name}' has no physical size. ARCore will estimate it " +
                                   $"and the alignment scale will drift. Set it to {expected:F3} m (docs/FRAMES.md).");
                else if (Mathf.Abs(entry.size.x - expected) > 0.001f)
                    Debug.LogWarning($"[Recon] Reference image '{entry.name}' physicalSize is {entry.size.x:F3} m but the " +
                                     $"measured marker edge is {expected:F3} m (docs/FRAMES.md, measured 2026-08-23).");
                break;
            }

            if (!found)
                Debug.LogError($"[Recon] No reference image named '{MarkerReferenceName}' in the library. " +
                               "The marker PNG lands with FTW-72; until then marker alignment cannot happen.");
        }
    }
}
