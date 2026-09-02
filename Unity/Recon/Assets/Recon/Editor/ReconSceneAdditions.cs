using Recon.Alignment;
using Recon.Scenes;
using Recon.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Recon.Editor
{
    /// <summary>
    /// Adds the I2 components (FTW-70) to the already-bootstrapped ReconAR scene.
    ///
    /// Separate from <see cref="SceneBootstrap"/> on purpose: that file arrived with FTW-69 and is
    /// on the parent branch, so editing it here would put a conflict in the middle of the one file
    /// that builds the app scene. Once FTW-69 and FTW-70 are both merged, SceneBootstrap should
    /// call <see cref="AddToOpenScene"/> at the end of CreateReconArScene so a fresh clone gets a
    /// complete scene in one step; until then this menu item is the second step.
    ///
    /// Idempotent: every component is added only when missing, so it is safe to re-run.
    /// </summary>
    public static class ReconSceneAdditions
    {
        const string ReconObjectName = "Recon";

        [MenuItem("Recon/Add I2 components to open scene")]
        public static void AddToOpenSceneMenu()
        {
            int added = AddToOpenScene();
            if (added > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[Recon] I2 components: {added} added, the rest were already present. " +
                      "Save the scene to keep them.");
        }

        /// <summary>
        /// Puts SceneLoader, AlignmentStatus, MarkerAligner, ManualPlacement, ScenePickerOverlay and
        /// AlignmentIndicator on the "Recon" GameObject. Returns how many were actually added.
        ///
        /// CloudAnchorAligner is deliberately left out: it is a stub with no implementation
        /// (FTW-52 / FTW-53), and a component in the scene that does nothing invites someone to
        /// wonder why the anchor path is not working.
        /// </summary>
        public static int AddToOpenScene()
        {
            var recon = GameObject.Find(ReconObjectName);
            if (recon == null)
            {
                Debug.LogError($"[Recon] No '{ReconObjectName}' GameObject in the open scene. " +
                               "Run Recon → Bootstrap ReconAR scene first (it creates the scene and that object).");
                return 0;
            }

            if (Object.FindFirstObjectByType<SceneRoot>(FindObjectsInactive.Include) == null)
                Debug.LogWarning("[Recon] No SceneRoot in the open scene; SceneLoader and the aligners will refuse to " +
                                 "do anything until Recon → Bootstrap ReconAR scene has run.");

            int added = 0;
            added += Ensure<AlignmentStatus>(recon);
            added += Ensure<SceneLoader>(recon);
            added += Ensure<MarkerAligner>(recon);
            added += Ensure<ManualPlacement>(recon);
            added += Ensure<ScenePickerOverlay>(recon);
            added += Ensure<AlignmentIndicator>(recon);
            return added;
        }

        static int Ensure<T>(GameObject go) where T : Component
        {
            if (go.GetComponent<T>() != null) return 0;
            go.AddComponent<T>();
            Debug.Log($"[Recon] Added {typeof(T).Name} to {go.name}");
            return 1;
        }
    }
}
