using Recon.Ballistics;
using Recon.Colliders;
using Recon.Spatter;
using Recon.UI;
using UnityEditor;
using UnityEngine;

namespace Recon.Editor
{
    /// <summary>
    /// Adds the FTW-71 physics components to the "Recon" GameObject in the open scene.
    ///
    /// It is a separate menu item rather than an edit to Editor/SceneBootstrap.cs because FTW-71 and
    /// FTW-69 are two open pull requests: touching the bootstrap here would conflict with the branch
    /// that owns it. Once both are merged, SceneBootstrap.CreateReconArScene should call
    /// <see cref="AddPhysicsComponents"/> right after it adds DevSplatLoader, and this menu item can
    /// stay as the way to fix up a scene that already exists.
    ///
    /// Idempotent: it only adds what is missing, so running it twice changes nothing.
    /// </summary>
    public static class ReconPhysicsAdditions
    {
        public const string ReconObjectName = "Recon";

        [MenuItem("Recon/Add physics components to open scene")]
        public static void AddToOpenScene()
        {
            var recon = GameObject.Find(ReconObjectName);
            if (recon == null)
            {
                Debug.LogError(
                    $"[Recon] No '{ReconObjectName}' GameObject in the open scene. Run " +
                    "Recon -> Bootstrap ReconAR scene first, or open Assets/Recon/Scenes/ReconAR.unity.");
                return;
            }

            int added = AddPhysicsComponents(recon);
            EditorUtility.SetDirty(recon);
            Debug.Log(added == 0
                ? $"[Recon] '{ReconObjectName}' already has the physics components; nothing to add."
                : $"[Recon] Added {added} physics component(s) to '{ReconObjectName}'. Save the scene to keep them.");
        }

        /// <summary>
        /// Adds ColliderMeshLoader, ShotController, SpatterEmitter and ShotParametersPanel if they are
        /// not already there. Returns how many were added. Safe to call from SceneBootstrap.
        /// </summary>
        public static int AddPhysicsComponents(GameObject recon)
        {
            if (recon == null) return 0;
            int added = 0;
            added += Ensure<ColliderMeshLoader>(recon);
            added += Ensure<ShotController>(recon);
            added += Ensure<SpatterEmitter>(recon);       // finds the ShotController on the same object
            added += Ensure<ShotParametersPanel>(recon);  // and wires itself to both
            return added;
        }

        static int Ensure<T>(GameObject go) where T : Component
        {
            if (go.GetComponent<T>() != null) return 0;
            go.AddComponent<T>();
            return 1;
        }
    }
}
