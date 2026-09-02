using Recon.Config;
using UnityEngine;

namespace Recon.Colliders
{
    /// <summary>
    /// Layer masks for the two collider sets, resolved from the layer names in ReconSettings
    /// (user layers 8 and 9, created in FTW-69).
    ///
    /// The whole point of keeping them apart is the Challenge 1 comparison in handbook Section 09:
    /// identical shots are fired into the AR-plane baseline and into the splat-derived mesh, and the
    /// impact-point divergence is the result. That only works if a shot can be aimed at exactly one
    /// of them, which is a layer mask.
    ///
    /// A missing layer is an error, not a fallback: masking against layer -1 silently produces a
    /// mask that hits nothing, and a shot that hits nothing looks the same as a shot that missed.
    /// </summary>
    public static class CollisionLayers
    {
        static int s_arPlanesLayer = -2;      // -2 = not resolved yet, -1 = missing from TagManager
        static int s_splatMeshLayer = -2;

        /// <summary>Layer index of the AR-plane baseline colliders, or -1 if the project has no such layer.</summary>
        public static int ArPlanesLayer
        {
            get
            {
                if (s_arPlanesLayer == -2) s_arPlanesLayer = Resolve(ReconSettings.Instance.arPlanesLayer);
                return s_arPlanesLayer;
            }
        }

        /// <summary>Layer index of the splat-derived collider mesh, or -1 if the project has no such layer.</summary>
        public static int SplatMeshLayer
        {
            get
            {
                if (s_splatMeshLayer == -2) s_splatMeshLayer = Resolve(ReconSettings.Instance.splatMeshLayer);
                return s_splatMeshLayer;
            }
        }

        public static int ArPlanesMask => MaskOf(ArPlanesLayer);
        public static int SplatMeshMask => MaskOf(SplatMeshLayer);
        public static int BothMask => ArPlanesMask | SplatMeshMask;

        public static int MaskFor(ColliderSet set)
        {
            switch (set)
            {
                case ColliderSet.ArPlanes: return ArPlanesMask;
                case ColliderSet.SplatMesh: return SplatMeshMask;
                default: return BothMask;
            }
        }

        /// <summary>True when the mask can actually hit something, so a caller can refuse to fire.</summary>
        public static bool IsUsable(ColliderSet set) => MaskFor(set) != 0;

        /// <summary>Re-reads the layer names. Only needed after the TagManager changes in the Editor.</summary>
        public static void Refresh()
        {
            s_arPlanesLayer = -2;
            s_splatMeshLayer = -2;
        }

        public static string Describe(ColliderSet set) =>
            $"{set} (mask 0x{MaskFor(set):x}{(IsUsable(set) ? "" : ", NO LAYER, hits nothing")})";

        static int Resolve(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
                Debug.LogError(
                    $"[Recon] Layer '{layerName}' is missing from ProjectSettings/TagManager.asset. Shots " +
                    "aimed at that collider set will hit nothing, which looks exactly like a physics bug. " +
                    "Add it (user layers 8 and 9) or run Recon -> Bootstrap ReconAR scene.");
            return layer;
        }

        static int MaskOf(int layer) => layer >= 0 ? 1 << layer : 0;
    }
}
