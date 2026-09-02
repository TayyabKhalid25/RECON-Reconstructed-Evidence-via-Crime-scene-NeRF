using System;
using System.Collections.Generic;
using System.Numerics;

namespace Recon.Ballistics
{
    /// <summary>Why the integration stopped. Anything other than Impact means nothing was hit.</summary>
    public enum TrajectoryStop
    {
        Impact,
        MaxFlightTime,
        MaxSubsteps,
    }

    /// <summary>One integrated shot: the arc to draw, and the impact if there was one.</summary>
    public sealed class TrajectoryResult
    {
        /// <summary>
        /// Integration positions in order, starting at the muzzle. When there is an impact the last
        /// entry IS the impact point, so a LineRenderer drawn through these stops on the surface
        /// instead of a substep inside it.
        /// </summary>
        public List<Vector3> Points { get; } = new List<Vector3>();

        /// <summary>The first hit the sweep found, or null.</summary>
        public ImpactRecord Impact { get; set; }

        /// <summary>How many substeps were integrated. dt * Substeps is the simulated time.</summary>
        public int Substeps { get; set; }

        /// <summary>Simulated seconds, interpolated inside the final substep when it impacted.</summary>
        public float FlightTime { get; set; }

        public TrajectoryStop StoppedReason { get; set; }

        public override string ToString() =>
            $"{StoppedReason} after {Substeps} substeps, {FlightTime:F4} s, {Points.Count} points" +
            (Impact != null ? " -> " + Impact : "");
    }

    /// <summary>
    /// Substepped, swept ballistics. The whole point, from handbook Section 09: "A bullet at even
    /// 300 m/s covers 6 metres in a single 20 ms physics step. A standard rigidbody will teleport
    /// straight through a wall between steps and report no collision." So the trajectory is
    /// integrated here in small substeps and a ray is swept from the previous position to the new
    /// one on EVERY substep. The first hit is the impact.
    ///
    /// Semi-implicit (symplectic) Euler, exactly as the simulating-ballistics-and-spatter skill
    /// shows: gravity, then quadratic drag, then position. That ordering is what makes the drag-free
    /// case match the analytic parabola to O(dt), which is the free correctness test
    /// (TrajectoryIntegratorTests, and Chapter 7).
    ///
    /// Pure C#. Unity glue is Recon.Ballistics.PhysicsRaycaster and ShotController.
    /// </summary>
    public static class TrajectoryIntegrator
    {
        /// <summary>
        /// Hard cap so a pathological dt cannot allocate an unbounded arc on a phone.
        /// 1 ms over the default 5 s flight is 5,000 substeps, so this is 40x headroom.
        /// </summary>
        public const int MaxSubsteps = 200000;

        public static TrajectoryResult Simulate(ShotParameters p, IRaycaster raycaster)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            p.Validate();
            if (raycaster == null) raycaster = new NullRaycaster();

            var result = new TrajectoryResult { StoppedReason = TrajectoryStop.MaxFlightTime };
            var pos = p.Origin;
            var vel = p.Direction * p.Speed;
            float dt = p.Dt;
            float dragFactor = p.DragFactor;
            float t = 0f;

            result.Points.Add(pos);

            while (t < p.MaxFlightTime)
            {
                if (result.Substeps >= MaxSubsteps)
                {
                    result.StoppedReason = TrajectoryStop.MaxSubsteps;
                    break;
                }

                // Semi-implicit Euler: velocity first, then position with the NEW velocity.
                vel += p.Gravity * dt;
                if (dragFactor > 0f) vel -= dragFactor * vel.Length() * vel * dt;

                var next = pos + vel * dt;
                var step = next - pos;
                float stepLength = step.Length();

                result.Substeps++;
                t += dt;

                // The sweep, every single substep. Skipping it on any substep is the tunnelling bug.
                if (stepLength > 0f &&
                    raycaster.Raycast(pos, step / stepLength, stepLength, p.LayerMask, out RayHit hit))
                {
                    float fraction = stepLength > 0f ? hit.Distance / stepLength : 0f;
                    if (fraction < 0f) fraction = 0f;
                    if (fraction > 1f) fraction = 1f;
                    float impactTime = t - dt + dt * fraction;

                    result.Impact = new ImpactRecord(hit.Point, hit.Normal, vel, hit.ColliderTag, impactTime);
                    result.Points.Add(hit.Point);
                    result.FlightTime = impactTime;
                    result.StoppedReason = TrajectoryStop.Impact;
                    return result;
                }

                pos = next;
                result.Points.Add(pos);
            }

            result.FlightTime = t;
            return result;
        }
    }
}
