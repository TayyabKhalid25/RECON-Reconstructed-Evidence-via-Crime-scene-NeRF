using System;
using System.Numerics;
using Newtonsoft.Json;

namespace Recon.Ballistics
{
    /// <summary>
    /// One impact, as the swept raycast found it. This is the row the Challenge 1 study compares
    /// between the AR-plane collider set and the splat-derived mesh, so it carries the geometry the
    /// comparison needs and nothing that would have to be recomputed later.
    ///
    /// <see cref="IncidenceAngleDegrees"/> is the angle between the trajectory and the surface
    /// PLANE, not the normal, because that is the angle bloodstain pattern analysis uses and the
    /// spatter ellipse ratio is its sine (handbook Section 09, "Blood spatter, the actual formula").
    /// Straight into a wall is 90 degrees; a graze is near 0.
    /// </summary>
    public sealed class ImpactRecord
    {
        public ImpactRecord() { }

        public ImpactRecord(Vector3 point, Vector3 normal, Vector3 incomingVelocity,
                            string colliderTag, float flightTime)
        {
            Point = point;
            Normal = Normalise(normal);
            IncomingVelocity = incomingVelocity;
            Speed = incomingVelocity.Length();
            IncidenceAngleDegrees = IncidenceDegrees(incomingVelocity, Normal);
            ColliderTag = colliderTag;
            FlightTime = flightTime;
        }

        /// <summary>Impact point in world space, metres.</summary>
        [JsonProperty("point")] public Vector3 Point { get; set; }

        /// <summary>Surface normal at the impact, unit length.</summary>
        [JsonProperty("normal")] public Vector3 Normal { get; set; }

        /// <summary>Velocity at the moment of impact, m/s. Its direction is the trajectory.</summary>
        [JsonProperty("incomingVelocity")] public Vector3 IncomingVelocity { get; set; }

        [JsonProperty("speedMetresPerSecond")] public float Speed { get; set; }

        /// <summary>Angle between the trajectory and the surface plane, degrees, 0..90.</summary>
        [JsonProperty("incidenceAngleDegrees")] public float IncidenceAngleDegrees { get; set; }

        /// <summary>Which collider set was hit; the Unity raycaster puts the layer name here.</summary>
        [JsonProperty("colliderTag")] public string ColliderTag { get; set; }

        /// <summary>Seconds from muzzle to impact, interpolated inside the substep that hit.</summary>
        [JsonProperty("flightTimeSeconds")] public float FlightTime { get; set; }

        /// <summary>
        /// asin(|dot(v, n)|) in degrees: the angle from the surface plane. Clamped because a
        /// normalised dot product can land a hair outside [-1, 1] in float and asin would return NaN.
        /// </summary>
        public static float IncidenceDegrees(Vector3 velocity, Vector3 normal)
        {
            var v = Normalise(velocity);
            var n = Normalise(normal);
            float d = Math.Abs(Vector3.Dot(v, n));
            if (d > 1f) d = 1f;
            return (float)(Math.Asin(d) * 180.0 / Math.PI);
        }

        static Vector3 Normalise(Vector3 v)
        {
            float len = v.Length();
            return len > 0f ? v / len : v;
        }

        public override string ToString() =>
            $"{ColliderTag ?? "?"} at {Point} n {Normal} {Speed:F1} m/s {IncidenceAngleDegrees:F1} deg t {FlightTime:F4} s";
    }
}
