using System;
using System.Numerics;
using Newtonsoft.Json;

namespace Recon.Ballistics
{
    /// <summary>
    /// Everything one shot needs, in SI units and in the scene frame. Pure C#: no UnityEngine, so
    /// the integrator and its parabola test run under `dotnet test Unity/Recon.Core.Tests`.
    ///
    /// Serialisable with Newtonsoft because every fired shot is written to
    /// persistentDataPath/shots/shots-&lt;utc&gt;.json for the Challenge 1 study, and a recorded impact
    /// point is worthless without the parameters that produced it.
    ///
    /// Frames and scale (docs/FRAMES.md): distances here are METRES in world space, because that is
    /// what the physics runs in. SceneRoot applies unitScale once; nothing in this class scales or
    /// flips anything.
    /// </summary>
    public sealed class ShotParameters
    {
        public const float DefaultAirDensityKgPerM3 = 1.225f;      // sea level, 15 C
        public const float DefaultMassKg = 0.008f;                 // 8 g, a 9 mm round
        public const float DefaultDragCoefficient = 0.3f;          // blunt-nosed pistol bullet, order of magnitude
        public const float DefaultCrossSectionAreaM2 = 6.39e-5f;   // pi * (9.02 mm / 2)^2
        public const float DefaultDtSeconds = 0.001f;
        public const float DefaultMaxFlightTimeSeconds = 5f;
        /// <summary>
        /// The tunnelling guard from handbook Section 09. A substep longer than this can put a wall
        /// between two integration points; 5 cm is under the thickness of anything in a room and
        /// well under the triangle size of a 50k-triangle collider mesh.
        /// </summary>
        public const float DefaultMaxStepLengthMetres = 0.05f;

        public static readonly Vector3 EarthGravity = new Vector3(0f, -9.81f, 0f);

        Vector3 m_direction = new Vector3(0f, 0f, 1f);

        /// <summary>Newtonsoft and Unity inspectors need this; every field carries its default.</summary>
        public ShotParameters() { }

        public ShotParameters(Vector3 origin, Vector3 direction, float speed,
                              float mass = DefaultMassKg,
                              float dragCoefficient = DefaultDragCoefficient,
                              float crossSectionArea = DefaultCrossSectionAreaM2,
                              float airDensity = DefaultAirDensityKgPerM3,
                              Vector3? gravity = null,
                              float dt = DefaultDtSeconds,
                              float maxFlightTime = DefaultMaxFlightTimeSeconds,
                              int layerMask = ~0,
                              bool useDrag = true,
                              float maxStepLength = DefaultMaxStepLengthMetres)
        {
            if (direction.Length() <= 0f || !IsFinite(direction))
                throw new ArgumentException($"Shot direction {direction} has no length; a shot needs a direction.", nameof(direction));

            Origin = origin;
            Direction = direction;
            Speed = speed;
            Mass = mass;
            DragCoefficient = dragCoefficient;
            CrossSectionArea = crossSectionArea;
            AirDensity = airDensity;
            Gravity = gravity ?? EarthGravity;
            Dt = dt;
            MaxFlightTime = maxFlightTime;
            LayerMask = layerMask;
            UseDrag = useDrag;
            MaxStepLength = maxStepLength;
        }

        [JsonProperty("origin")] public Vector3 Origin { get; set; }

        /// <summary>Normalised on assignment, so a caller cannot accidentally scale the muzzle speed.</summary>
        [JsonProperty("direction")]
        public Vector3 Direction
        {
            get => m_direction;
            set
            {
                float len = value.Length();
                m_direction = len > 0f && IsFinite(value) ? value / len : value;
            }
        }

        /// <summary>Muzzle speed, metres per second.</summary>
        [JsonProperty("speedMetresPerSecond")] public float Speed { get; set; }

        [JsonProperty("massKg")] public float Mass { get; set; } = DefaultMassKg;
        [JsonProperty("dragCoefficient")] public float DragCoefficient { get; set; } = DefaultDragCoefficient;
        [JsonProperty("crossSectionAreaM2")] public float CrossSectionArea { get; set; } = DefaultCrossSectionAreaM2;
        [JsonProperty("airDensityKgPerM3")] public float AirDensity { get; set; } = DefaultAirDensityKgPerM3;
        [JsonProperty("gravity")] public Vector3 Gravity { get; set; } = EarthGravity;

        /// <summary>Substep length in seconds. Small on purpose: see <see cref="MaxStepLength"/>.</summary>
        [JsonProperty("dtSeconds")] public float Dt { get; set; } = DefaultDtSeconds;

        [JsonProperty("maxFlightTimeSeconds")] public float MaxFlightTime { get; set; } = DefaultMaxFlightTimeSeconds;

        /// <summary>Which collider set to sweep against; see Recon.Colliders.CollisionLayers.</summary>
        [JsonProperty("layerMask")] public int LayerMask { get; set; } = ~0;

        [JsonProperty("useDrag")] public bool UseDrag { get; set; } = true;

        /// <summary>Longest substep the integrator will accept, metres. The tunnelling guard.</summary>
        [JsonProperty("maxStepLengthMetres")] public float MaxStepLength { get; set; } = DefaultMaxStepLengthMetres;

        /// <summary>Length of the first substep, metres. What the guard is checked against.</summary>
        [JsonIgnore] public float StepLength => Speed * Dt;

        /// <summary>
        /// Quadratic drag coefficient collapsed to one number: 0.5 * rho * Cd * A / m, so the
        /// acceleration is that times |v| * v. Zero when drag is off.
        /// </summary>
        [JsonIgnore]
        public float DragFactor =>
            !UseDrag || Mass <= 0f ? 0f : 0.5f * AirDensity * DragCoefficient * CrossSectionArea / Mass;

        /// <summary>
        /// Throws with the fix in the message rather than integrating something meaningless. Every
        /// case here is a mistake that produces a plausible-looking but wrong trajectory, which is
        /// the failure mode the handbook warns about.
        /// </summary>
        public void Validate()
        {
            if (!(Dt > 0f) || float.IsNaN(Dt) || float.IsInfinity(Dt))
                throw new ArgumentException($"dt is {Dt}: the substep must be greater than 0 seconds.");

            if (!(Speed > 0f) || float.IsNaN(Speed) || float.IsInfinity(Speed))
                throw new ArgumentException($"speed is {Speed} m/s: a shot needs a positive muzzle speed.");

            if (!(MaxFlightTime > 0f))
                throw new ArgumentException($"maxFlightTime is {MaxFlightTime} s: nothing can be integrated.");

            if (!(MaxStepLength > 0f))
                throw new ArgumentException($"maxStepLength is {MaxStepLength} m: the tunnelling guard cannot be disabled, raise it instead.");

            if (m_direction.Length() <= 0f || !IsFinite(m_direction))
                throw new ArgumentException($"direction {m_direction} has no length; a shot needs a direction.");

            if (Mass <= 0f && UseDrag)
                throw new ArgumentException($"mass is {Mass} kg with drag on: drag divides by mass.");

            if (StepLength > MaxStepLength)
                throw new ArgumentException(
                    $"dt {Dt} s at {Speed} m/s gives a {StepLength:F3} m substep, over the " +
                    $"{MaxStepLength} m maximum. A substep longer than a wall is thick tunnels straight " +
                    "through it and reports no collision (handbook Section 09). Reduce dt (1 ms at " +
                    "300 m/s is 0.3 m, 0.1 ms is 0.03 m) or raise MaxStepLength deliberately.");
        }

        static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.X) && !float.IsNaN(v.Y) && !float.IsNaN(v.Z) &&
            !float.IsInfinity(v.X) && !float.IsInfinity(v.Y) && !float.IsInfinity(v.Z);

        public ShotParameters Clone() => (ShotParameters)MemberwiseClone();

        public override string ToString() =>
            $"{Speed:F0} m/s dir {Direction} dt {Dt * 1000f:F2} ms drag {(UseDrag ? "on" : "off")} mask 0x{LayerMask:x}";
    }
}
