using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Recon.Alignment
{
    /// <summary>
    /// Priority 3 of the fallback chain: the user taps a detected plane and the twin moves there.
    ///
    /// This is not a cop-out, it is the risk-register entry "anchoring service unavailable" made
    /// real, and it is what keeps a demo alive on a network that does not cooperate
    /// (docs/ANCHORING.md). What makes it safe is that it is visibly labelled NOT METRIC VERIFIED
    /// and cannot produce an evidential measurement.
    ///
    /// <b>unitScale stays applied.</b> The geometry is still metric — it came from the marker at
    /// export time — it is only the twin's <i>position in the room</i> that is unverified. Nothing
    /// here touches scale.
    ///
    /// Off until the user turns it on (<see cref="Enabled"/>), so a stray tap can never move a
    /// marker-aligned scene.
    /// </summary>
    [AddComponentMenu("Recon/Alignment/Manual Placement")]
    [DisallowMultipleComponent]
    public sealed class ManualPlacement : MonoBehaviour
    {
        [SerializeField, Tooltip("Off by default. The user turns manual placement on deliberately; otherwise a stray tap could move a marker-aligned scene.")]
        bool enabledByUser;

        [SerializeField, Tooltip("Left empty, the manager on the XR Origin is found at Awake.")]
        ARRaycastManager raycastManager;

        static readonly List<ARRaycastHit> s_hits = new List<ARRaycastHit>();

        public bool Enabled
        {
            get => enabledByUser;
            set => enabledByUser = value;
        }

        public int PlacementCount { get; private set; }

        void Awake()
        {
            if (raycastManager == null)
                raycastManager = FindFirstObjectByType<ARRaycastManager>(FindObjectsInactive.Include);
            if (raycastManager == null)
                Debug.LogError("[Recon] ManualPlacement found no ARRaycastManager. Run Recon → Bootstrap ReconAR scene.");
        }

        void Update()
        {
            if (!enabledByUser || raycastManager == null) return;
            if (!TryGetTap(out var screenPoint)) return;

            // Planes only: a point-cloud hit lands on whatever feature the tracker happened to
            // find and is not a surface a room can sit on.
            if (!raycastManager.Raycast(screenPoint, s_hits, TrackableType.PlaneWithinPolygon))
            {
                Debug.Log("[Recon] Manual placement: tap hit no detected plane. Sweep the phone over the floor first.");
                return;
            }

            var root = SceneRoot.Find();
            if (root == null) return;

            var hit = s_hits[0];

            // Position from the tap; rotation left exactly as it was. A tap says nothing about the
            // scene's heading, and adopting the plane's rotation would tilt the whole twin onto a
            // wall's normal the moment a vertical plane is tapped.
            root.SetPose(new Pose(hit.pose.position, root.transform.rotation));
            PlacementCount++;

            var status = AlignmentStatus.Find();
            if (status != null)
            {
                if (AlignmentSources.Priority(status.Source) > AlignmentSources.Priority(AlignmentSource.Manual))
                    Debug.LogWarning($"[Recon] Manual placement is overriding a {status.Source} alignment. " +
                                     "The scene is no longer metric verified; turn manual placement off and " +
                                     "re-detect the marker to get back to a verified pose.");

                status.Set(AlignmentSource.Manual, $"tapped plane at {hit.pose.position}");
            }

            Debug.Log($"[Recon] Manual placement #{PlacementCount} at {hit.pose.position}. " +
                      "NOT METRIC VERIFIED: geometry is still metric (unitScale is applied), the position is not. " +
                      "No measurement from this session may be quoted (docs/ANCHORING.md).");
        }

        /// <summary>
        /// One tap, from the Input System. Touchscreen first for the phone; Pointer covers a mouse
        /// so the flow can be exercised in the Editor without a device.
        /// </summary>
        static bool TryGetTap(out Vector2 screenPoint)
        {
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
            {
                screenPoint = touch.primaryTouch.position.ReadValue();
                return true;
            }

            var pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame)
            {
                screenPoint = pointer.position.ReadValue();
                return true;
            }

            screenPoint = default;
            return false;
        }
    }
}
