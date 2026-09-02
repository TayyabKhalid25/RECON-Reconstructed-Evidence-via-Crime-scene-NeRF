using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Recon
{
    /// <summary>
    /// Type conversion between the pure C# cores (System.Numerics.Vector3, so they compile and are
    /// tested under plain dotnet) and UnityEngine.Vector3. Available to every Recon.* namespace
    /// because they all sit inside Recon.
    ///
    /// This is a TYPE change and nothing else. It is not a frame conversion: no axis is swapped,
    /// negated or reordered here, and none ever should be. docs/FRAMES.md puts the one conversion
    /// in the pipeline on the GPU side before export, and a second one on the client would cancel
    /// it and cost a day to find.
    /// </summary>
    public static class NumericsInterop
    {
        public static Vector3 ToUnity(this NVector3 v) => new Vector3(v.X, v.Y, v.Z);

        public static NVector3 ToNumerics(this Vector3 v) => new NVector3(v.x, v.y, v.z);

        /// <summary>Bulk conversion for mesh vertices and trajectory arcs. Null in, null out.</summary>
        public static Vector3[] ToUnity(this NVector3[] source)
        {
            if (source == null) return null;
            var result = new Vector3[source.Length];
            for (int i = 0; i < source.Length; i++) result[i] = source[i].ToUnity();
            return result;
        }

        public static Vector3[] ToUnity(this System.Collections.Generic.List<NVector3> source)
        {
            if (source == null) return null;
            var result = new Vector3[source.Count];
            for (int i = 0; i < source.Count; i++) result[i] = source[i].ToUnity();
            return result;
        }
    }
}
