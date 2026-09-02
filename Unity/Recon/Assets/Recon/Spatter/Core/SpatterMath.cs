using System;
using System.Numerics;
using Recon.Ballistics;

namespace Recon.Spatter
{
    /// <summary>
    /// The blood spatter proxy, and nothing more than a proxy. Standard bloodstain pattern analysis
    /// gives one relationship the project can defend (handbook Section 09, "Blood spatter, the
    /// actual formula"): for a droplet striking a surface at incidence angle alpha, measured between
    /// the trajectory and the surface PLANE, the stain width over length approximates sin(alpha), so
    /// alpha = arcsin(width / length).
    ///
    /// Scope discipline, and this must survive into the report: this is a DIRECTIONAL PROXY. There
    /// is no fluid dynamics and no rheology here, and no claim of forensic validity beyond
    /// indicating direction. What is implemented is the published width-to-length relationship and
    /// the geometry that orients it.
    ///
    /// Pure C#, so the ratio is tested against sin(alpha) with no Editor and no device.
    /// </summary>
    public static class SpatterMath
    {
        /// <summary>Below this the stain is a hairline; keeps a graze visible instead of zero-width.</summary>
        public const float MinimumAxisRatio = 0.02f;

        /// <summary>
        /// minor / major = |dot(dir, normal)| = sin(alpha). The absolute value is what makes the
        /// result independent of which way the collider's normal happens to face.
        ///
        /// The gotcha the skill lists as "ellipses all circles" is computing this against the wrong
        /// vector: it is the TRAJECTORY against the surface normal, never the view direction.
        /// </summary>
        public static float EllipseAxisRatio(Vector3 incomingDirection, Vector3 surfaceNormal)
        {
            float dl = incomingDirection.Length();
            float nl = surfaceNormal.Length();
            if (dl <= 0f || nl <= 0f) return 0f;          // degenerate input: no stain, not a NaN
            float d = Math.Abs(Vector3.Dot(incomingDirection / dl, surfaceNormal / nl));
            return d > 1f ? 1f : d;
        }

        /// <summary>
        /// The angle between the trajectory and the surface plane, degrees. Deliberately the same
        /// implementation the ballistics impact record uses, so a stain and the impact it came from
        /// can never disagree about the angle.
        /// </summary>
        public static float IncidenceAngleDegrees(Vector3 incomingDirection, Vector3 surfaceNormal) =>
            ImpactRecord.IncidenceDegrees(incomingDirection, surfaceNormal);

        /// <summary>
        /// The stain's long axis: the incoming direction projected onto the surface, normalised, so
        /// the elongation points away from the source. For a perpendicular impact the projection
        /// vanishes and the stain is a circle, so any in-surface direction is correct; a stable
        /// perpendicular of the normal is returned rather than a zero vector, because the caller
        /// builds a rotation out of this.
        /// </summary>
        public static Vector3 MajorAxisDirection(Vector3 incomingDirection, Vector3 surfaceNormal)
        {
            var n = Normalise(surfaceNormal);
            if (n == Vector3.Zero) return new Vector3(1f, 0f, 0f);

            var projected = incomingDirection - n * Vector3.Dot(incomingDirection, n);
            float len = projected.Length();
            if (len > 1e-6f) return projected / len;
            return AnyPerpendicular(n);
        }

        /// <summary>
        /// The decal's local scale, with minor over major equal to sin(alpha):
        /// (baseSize * ratio, baseSize, 1). Z is 1 because the decal is a flat quad.
        /// </summary>
        public static Vector3 DecalScale(Vector3 incomingDirection, Vector3 surfaceNormal, float baseSize)
        {
            float ratio = EllipseAxisRatio(incomingDirection, surfaceNormal);
            if (ratio < MinimumAxisRatio) ratio = MinimumAxisRatio;
            return new Vector3(baseSize * ratio, baseSize, 1f);
        }

        /// <summary>
        /// <paramref name="count"/> directions spread uniformly over the spherical cap of half angle
        /// <paramref name="coneHalfAngleDeg"/> around <paramref name="centre"/>.
        ///
        /// Deterministic by construction: a small xorshift PRNG is used rather than System.Random,
        /// because the same seed has to produce the same droplets under Unity's Mono and under
        /// dotnet, or the test that pins this cannot run in both places.
        ///
        /// The half angle is clamped to 90 degrees: a wider cone would emit droplets backwards
        /// through the surface the impact just struck.
        /// </summary>
        public static Vector3[] DropletDirections(Vector3 centre, float coneHalfAngleDeg, int count, int seed)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "A negative droplet count is meaningless.");
            var axis = Normalise(centre);
            if (axis == Vector3.Zero)
                throw new ArgumentException("The cone needs an axis; the impact direction has no length.", nameof(centre));
            if (count == 0) return new Vector3[0];

            if (coneHalfAngleDeg < 0f) coneHalfAngleDeg = 0f;
            if (coneHalfAngleDeg > 90f) coneHalfAngleDeg = 90f;

            var tangent = AnyPerpendicular(axis);
            var bitangent = Vector3.Cross(axis, tangent);
            double cosLimit = Math.Cos(coneHalfAngleDeg * Math.PI / 180.0);

            var result = new Vector3[count];
            uint state = (uint)seed * 2654435761u + 1u;   // Knuth multiplicative, then never zero
            for (int i = 0; i < count; i++)
            {
                double u = NextUnit(ref state);
                double v = NextUnit(ref state);
                double cosTheta = 1.0 - u * (1.0 - cosLimit);       // uniform over the cap
                double sinTheta = Math.Sqrt(Math.Max(0.0, 1.0 - cosTheta * cosTheta));
                double phi = 2.0 * Math.PI * v;

                var dir = axis * (float)cosTheta
                          + tangent * (float)(sinTheta * Math.Cos(phi))
                          + bitangent * (float)(sinTheta * Math.Sin(phi));
                result[i] = Normalise(dir);
            }
            return result;
        }

        /// <summary>A unit vector perpendicular to n, chosen off n's smallest component so it never degenerates.</summary>
        public static Vector3 AnyPerpendicular(Vector3 n)
        {
            var unit = Normalise(n);
            if (unit == Vector3.Zero) return new Vector3(1f, 0f, 0f);
            var helper = Math.Abs(unit.X) <= Math.Abs(unit.Y) && Math.Abs(unit.X) <= Math.Abs(unit.Z)
                ? new Vector3(1f, 0f, 0f)
                : Math.Abs(unit.Y) <= Math.Abs(unit.Z) ? new Vector3(0f, 1f, 0f) : new Vector3(0f, 0f, 1f);
            return Normalise(Vector3.Cross(unit, helper));
        }

        static Vector3 Normalise(Vector3 v)
        {
            float len = v.Length();
            return len > 0f && !float.IsNaN(len) && !float.IsInfinity(len) ? v / len : Vector3.Zero;
        }

        /// <summary>xorshift32, then scaled into [0, 1). Identical on every runtime, which is the point.</summary>
        static double NextUnit(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state / 4294967296.0;
        }
    }
}
