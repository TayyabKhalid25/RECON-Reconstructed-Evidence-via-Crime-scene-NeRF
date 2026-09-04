using UnityEngine;

namespace Recon.Rendering
{
    /// <summary>
    /// Finds the active <see cref="ISplatRenderer"/> in the scene without app code knowing which
    /// adapter is installed. Put exactly one adapter component in the scene (SceneBootstrap does);
    /// this returns it, or null with a loud log if none is present, which is the symptom of the
    /// renderer package being missing from Packages/manifest.json.
    /// </summary>
    public static class SplatRendererProvider
    {
        static ISplatRenderer s_cached;

        public static ISplatRenderer Current
        {
            get
            {
                if (s_cached is Component c && c != null) return s_cached;
                s_cached = null;
                foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (behaviour is ISplatRenderer renderer)
                    {
                        s_cached = renderer;
                        break;
                    }
                }
                if (s_cached == null)
                {
                    Debug.LogError("[Recon] No ISplatRenderer in the scene. Is the splat package in " +
                                   "Packages/manifest.json and an adapter component on the Recon object? " +
                                   "See Unity/README.md and docs/MOBILE-SPLAT-OPTIONS.md.");
                }
                return s_cached;
            }
        }

        /// <summary>Tests and scene reloads use this to drop the cached instance.</summary>
        public static void Reset() => s_cached = null;
    }
}
