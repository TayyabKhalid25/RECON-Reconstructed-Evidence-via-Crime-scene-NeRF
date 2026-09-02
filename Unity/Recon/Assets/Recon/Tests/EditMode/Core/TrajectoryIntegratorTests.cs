using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Recon.Ballistics;

namespace Recon.Tests.Core
{
    /// <summary>
    /// Chapter 7 test cases for the ballistics integrator (handbook Section 09, "Ballistics, the
    /// bug you will definitely hit", and AGENTS.md: "ballistics against the analytic parabola
    /// (a drag-free shot must match it)").
    ///
    /// Runs under both the Unity Test Runner and `dotnet test Unity/Recon.Core.Tests`, so the
    /// integrator is validated on a machine with no Editor and no device. Constraint asserts only.
    /// </summary>
    public class TrajectoryIntegratorTests
    {
        const float G = 9.81f;

        /// <summary>y = x*tan(theta) - g*x^2/(2*v^2*cos^2(theta)), the drag-free parabola from the handbook.</summary>
        static double AnalyticY(double x, double speed, double thetaDeg, double g)
        {
            double th = thetaDeg * Math.PI / 180.0;
            double c = Math.Cos(th);
            return x * Math.Tan(th) - g * x * x / (2.0 * speed * speed * c * c);
        }

        static ShotParameters Shot(double thetaDeg, float speed, float dt, bool useDrag = false,
                                   float maxFlightTime = 5f)
        {
            double th = thetaDeg * Math.PI / 180.0;
            var dir = new Vector3((float)Math.Cos(th), (float)Math.Sin(th), 0f);
            return new ShotParameters(Vector3.Zero, dir, speed, dt: dt, useDrag: useDrag,
                                      maxFlightTime: maxFlightTime);
        }

        /// <summary>The integration point closest to a target x, so the comparison uses no interpolation.</summary>
        static Vector3 PointNearestX(IReadOnlyList<Vector3> points, double x)
        {
            var best = points[0];
            double bestD = double.MaxValue;
            foreach (var p in points)
            {
                double d = Math.Abs(p.X - x);
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        static double ErrorAgainstParabolaAt(double x, float speed, double thetaDeg, float dt)
        {
            var r = TrajectoryIntegrator.Simulate(Shot(thetaDeg, speed, dt), new NullRaycaster());
            var p = PointNearestX(r.Points, x);
            return Math.Abs(p.Y - AnalyticY(p.X, speed, thetaDeg, G));
        }

        // ------------------------------------------------------------------ TC-BAL-01

        /// <summary>
        /// TC-BAL-01. Drag off, 20 m/s at 30 degrees, dt = 1 ms: the integrated arc matches the
        /// analytic parabola to better than 1 cm at 2, 5, 10 and 15 m. This is the handbook free
        /// correctness check: a drag-free run should match the analytic parabola almost exactly.
        /// </summary>
        [Test]
        public void DragFreeShotMatchesAnalyticParabolaWithinOneCentimetre()
        {
            var result = TrajectoryIntegrator.Simulate(Shot(30, 20f, 0.001f), new NullRaycaster());

            Assert.That(result.Impact, Is.Null, "NullRaycaster must never report a hit");
            Assert.That(result.StoppedReason, Is.EqualTo(TrajectoryStop.MaxFlightTime));
            Assert.That(result.Points, Has.Count.GreaterThan(4000));

            foreach (var x in new double[] { 2, 5, 10, 15 })
            {
                var p = PointNearestX(result.Points, x);
                double expected = AnalyticY(p.X, 20f, 30, G);
                Assert.That(p.Y, Is.EqualTo(expected).Within(0.01),
                    "parabola mismatch at x = " + x + " m (sampled x = " + p.X + ")");
            }
        }

        /// <summary>
        /// TC-BAL-01b. Semi-implicit Euler is first order, so its error is O(dt): halving dt must
        /// halve the deviation from the parabola. A second-order or a broken integrator does not
        /// behave this way, which is what makes this assertion worth more than a single tolerance.
        /// </summary>
        [Test]
        public void ParabolaErrorHalvesWhenTimestepHalves()
        {
            double e1 = ErrorAgainstParabolaAt(10, 20f, 30, 0.001f);
            double eHalf = ErrorAgainstParabolaAt(10, 20f, 30, 0.0005f);

            Assert.That(e1, Is.GreaterThan(1e-4), "error at dt = 1 ms should be measurable, not float noise");
            Assert.That(eHalf / e1, Is.EqualTo(0.5).Within(0.02));
        }

        /// <summary>
        /// TC-BAL-01c. The deviation is not just small, it is the known truncation error of
        /// semi-implicit Euler under constant gravity: y(t) - y_n = 0.5*g*t*dt. Pinning the number
        /// means a future change that quietly alters the integration scheme fails here.
        ///
        /// The tolerance is relative (5 %) rather than absolute because the integrator runs in
        /// float32, and naive summation of ~600 substeps into a metres-sized accumulator drifts by
        /// about 5e-5 m, which is a fifth of the term being predicted at x = 2 m.
        /// </summary>
        [Test]
        public void ParabolaErrorEqualsHalfGravityTimesFlightTimeTimesTimestep()
        {
            const float dt = 0.001f;
            const float speed = 20f;
            var result = TrajectoryIntegrator.Simulate(Shot(30, speed, dt), new NullRaycaster());

            foreach (var x in new double[] { 2, 5, 10, 15 })
            {
                var p = PointNearestX(result.Points, x);
                double vx = speed * Math.Cos(30 * Math.PI / 180.0);
                double t = p.X / vx;
                double predicted = 0.5 * G * t * dt;               // metres, positive = falls short
                double actual = AnalyticY(p.X, speed, 30, G) - p.Y;
                Assert.That(actual / predicted, Is.EqualTo(1.0).Within(0.05),
                    "truncation error at x = " + x + " m: predicted " + predicted + " m, measured " + actual + " m");
            }
        }

        // ------------------------------------------------------------------ TC-BAL-02

        /// <summary>
        /// TC-BAL-02. The tunnelling guard, which is the whole reason the integrator exists: at
        /// 300 m/s a projectile crosses 6 m in one 20 ms physics step, and a plain rigidbody
        /// teleports through the wall. With dt = 0.1 ms and a sweep on every substep the wall at
        /// x = 5 m is hit exactly once, within 1 mm.
        /// </summary>
        [Test]
        public void FastShotSweepsIntoTheWallExactlyOnceAndWithinOneMillimetre()
        {
            var wall = new PlaneRaycaster(new Vector3(5f, 0f, 0f), new Vector3(-1f, 0f, 0f), "SplatMesh");
            var p = Shot(0, 300f, 0.0001f, maxFlightTime: 1f);

            var result = TrajectoryIntegrator.Simulate(p, wall);

            Assert.That(result.Impact, Is.Not.Null, "the swept raycast must catch the wall");
            Assert.That(wall.HitsReported, Is.EqualTo(1), "the integrator must stop at the first hit");
            Assert.That(result.StoppedReason, Is.EqualTo(TrajectoryStop.Impact));
            Assert.That(result.Impact.Point.X, Is.EqualTo(5f).Within(0.001f));
            Assert.That(result.Impact.ColliderTag, Is.EqualTo("SplatMesh"));
            Assert.That(result.Impact.Speed, Is.EqualTo(300f).Within(0.5f));
            Assert.That(result.Impact.FlightTime, Is.EqualTo(5f / 300f).Within(0.001f));
        }

        /// <summary>
        /// TC-BAL-02b. The impact lies inside the last swept segment: between the last integration
        /// point and the point the projectile would have reached had nothing been in the way. That
        /// is the property a tunnelled shot violates, so it is asserted rather than assumed.
        /// </summary>
        [Test]
        public void ImpactLiesBetweenTheLastTwoIntegrationPoints()
        {
            var wall = new PlaneRaycaster(new Vector3(5f, 0f, 0f), new Vector3(-1f, 0f, 0f), "SplatMesh");
            var p = Shot(0, 300f, 0.0001f, maxFlightTime: 1f);

            var result = TrajectoryIntegrator.Simulate(p, wall);
            var points = result.Points;

            // The arc ends at the impact so a LineRenderer stops on the wall, not inside it.
            Assert.That(points[points.Count - 1], Is.EqualTo(result.Impact.Point));

            var before = points[points.Count - 2];
            var wouldHaveBeen = before + result.Impact.IncomingVelocity * p.Dt;
            Assert.That(before.X, Is.LessThan(5f));
            Assert.That(wouldHaveBeen.X, Is.GreaterThanOrEqualTo(5f));
            Assert.That(result.Impact.Point.X, Is.GreaterThanOrEqualTo(before.X));
            Assert.That(result.Impact.Point.X, Is.LessThanOrEqualTo(wouldHaveBeen.X));
            Assert.That((result.Impact.Point - before).Length(),
                Is.LessThanOrEqualTo((wouldHaveBeen - before).Length() + 1e-4f),
                "the impact must be inside the swept segment, not past it");
        }

        // ------------------------------------------------------------------ TC-BAL-03

        /// <summary>
        /// TC-BAL-03. Quadratic drag must shorten the range of an otherwise identical shot. The
        /// projectile is a 9 mm-sized round (8 g, 6.39e-5 m^2, Cd 0.3) fired at 20 m/s and 30 degrees
        /// over a ground plane; drag off gives v^2*sin(2*theta)/g = 35.3 m.
        /// </summary>
        [Test]
        public void DragReducesRangeComparedWithTheSameShotInVacuum()
        {
            float vacuumRange = RangeOverGroundPlane(false);
            float dragRange = RangeOverGroundPlane(true);

            Assert.That(vacuumRange, Is.EqualTo(35.31f).Within(0.05f), "v^2 sin(2 theta) / g");
            Assert.That(dragRange, Is.LessThan(vacuumRange - 0.1f));
        }

        static float RangeOverGroundPlane(bool useDrag)
        {
            var ground = new PlaneRaycaster(Vector3.Zero, new Vector3(0f, 1f, 0f), "ArPlanes");
            var r = TrajectoryIntegrator.Simulate(Shot(30, 20f, 0.001f, useDrag), ground);
            Assert.That(r.Impact, Is.Not.Null, "the shot must land on the ground plane");
            return r.Impact.Point.X;
        }

        // ------------------------------------------------------------------ TC-BAL-04

        /// <summary>
        /// TC-BAL-04. The guard the handbook warns about: a step longer than 5 cm can put a wall
        /// between two integration points, so the integrator refuses the shot instead of silently
        /// tunnelling. 300 m/s at the default 20 ms physics step is the handbook example, 6 m.
        /// </summary>
        [Test]
        public void TimestepTooLargeForTheSpeedThrowsInsteadOfTunnelling()
        {
            var tooCoarse = Assert.Throws<ArgumentException>(() =>
                TrajectoryIntegrator.Simulate(Shot(0, 300f, 0.02f), new NullRaycaster()));
            Assert.That(tooCoarse.Message, Does.Contain("6"));
            Assert.That(tooCoarse.Message, Does.Contain("dt"));

            Assert.Throws<ArgumentException>(() =>
                TrajectoryIntegrator.Simulate(Shot(0, 20f, 0f), new NullRaycaster()));
            Assert.Throws<ArgumentException>(() =>
                TrajectoryIntegrator.Simulate(Shot(0, 20f, -0.001f), new NullRaycaster()));
            Assert.Throws<ArgumentNullException>(() =>
                TrajectoryIntegrator.Simulate(null, new NullRaycaster()));
        }

        /// <summary>A longer maxStepLength accepts a step the default refuses: it is a parameter, not a constant.</summary>
        [Test]
        public void RaisingMaxStepLengthAcceptsAStepTheDefaultRefuses()
        {
            var p = Shot(0, 300f, 0.001f);          // 0.30 m per step, over the 0.05 m default
            Assert.Throws<ArgumentException>(() => TrajectoryIntegrator.Simulate(p, new NullRaycaster()));

            p.MaxStepLength = 0.5f;
            Assert.That(TrajectoryIntegrator.Simulate(p, new NullRaycaster()).Points, Is.Not.Empty);
        }

        // ------------------------------------------------------------------ TC-BAL-05

        /// <summary>
        /// TC-BAL-05. Incidence angle is measured between the trajectory and the surface PLANE, not
        /// the normal, because that is the angle bloodstain pattern analysis uses and the one the
        /// spatter ellipse ratio is the sine of. Straight down onto a floor is 90 degrees.
        /// </summary>
        [Test]
        public void ShotStraightDownOntoAHorizontalPlaneHasNinetyDegreeIncidence()
        {
            var ground = new PlaneRaycaster(Vector3.Zero, new Vector3(0f, 1f, 0f), "ArPlanes");
            var p = new ShotParameters(new Vector3(0f, 1f, 0f), new Vector3(0f, -1f, 0f), 20f,
                                       dt: 0.001f, useDrag: false);

            var r = TrajectoryIntegrator.Simulate(p, ground);

            Assert.That(r.Impact, Is.Not.Null);
            Assert.That(r.Impact.IncidenceAngleDegrees, Is.EqualTo(90f).Within(0.01f));
            Assert.That(r.Impact.Normal, Is.EqualTo(new Vector3(0f, 1f, 0f)));
        }

        /// <summary>
        /// TC-BAL-05b. A 45 degree shot returning to its launch height in vacuum strikes at 45
        /// degrees, because the vertical speed is recovered on the way down. Symmetry of the
        /// trajectory is the property under test; semi-implicit Euler loses g*dt of it.
        /// </summary>
        [Test]
        public void FortyFiveDegreeShotStrikesAtFortyFiveDegreesAtTheLaunchHeight()
        {
            var ground = new PlaneRaycaster(Vector3.Zero, new Vector3(0f, 1f, 0f), "ArPlanes");
            var r = TrajectoryIntegrator.Simulate(Shot(45, 20f, 0.0001f), ground);

            Assert.That(r.Impact, Is.Not.Null);
            Assert.That(r.Impact.Point.Y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(r.Impact.IncidenceAngleDegrees, Is.EqualTo(45f).Within(0.05f));
        }

        /// <summary>A grazing shot along a wall has a small incidence angle, and it is never negative.</summary>
        [Test]
        public void GrazingImpactHasSmallPositiveIncidenceAngle()
        {
            var wall = new PlaneRaycaster(new Vector3(5f, 0f, 0f), new Vector3(-1f, 0f, 0f), "SplatMesh");
            var p = new ShotParameters(Vector3.Zero, new Vector3(1f, 0f, 0f), 300f,
                                       dt: 0.0001f, useDrag: false, maxFlightTime: 1f);
            p.Gravity = Vector3.Zero;
            p.Direction = new Vector3((float)Math.Cos(80 * Math.PI / 180.0), (float)Math.Sin(80 * Math.PI / 180.0), 0f);

            var r = TrajectoryIntegrator.Simulate(p, wall);

            Assert.That(r.Impact, Is.Not.Null);
            Assert.That(r.Impact.IncidenceAngleDegrees, Is.EqualTo(10f).Within(0.01f));
        }

        // ------------------------------------------------------------------ parameters, logging

        [Test]
        public void DirectionIsNormalisedOnConstruction()
        {
            var p = new ShotParameters(Vector3.Zero, new Vector3(0f, 3f, 4f), 10f);
            Assert.That(p.Direction.Length(), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(p.Direction.Y, Is.EqualTo(0.6f).Within(1e-6f));
            Assert.That(p.Gravity, Is.EqualTo(new Vector3(0f, -9.81f, 0f)));
            Assert.That(p.AirDensity, Is.EqualTo(1.225f));
            Assert.That(p.MaxStepLength, Is.EqualTo(0.05f));
        }

        [Test]
        public void ZeroDirectionThrows()
        {
            Assert.Throws<ArgumentException>(() => new ShotParameters(Vector3.Zero, Vector3.Zero, 10f));
        }

        /// <summary>
        /// The Challenge 1 study reads these files back, so an entry has to survive a JSON round
        /// trip: both impact frames (SceneRoot-local scene units AND world metres) and which
        /// collider set produced it.
        /// </summary>
        [Test]
        public void ShotLogEntryRoundTripsThroughJson()
        {
            var wall = new PlaneRaycaster(new Vector3(5f, 0f, 0f), new Vector3(-1f, 0f, 0f), "SplatMesh");
            var shot = Shot(0, 300f, 0.0001f, maxFlightTime: 1f);
            var result = TrajectoryIntegrator.Simulate(shot, wall);

            var entry = ShotLogEntry.From(shot, result, Recon.Colliders.ColliderSet.SplatMesh,
                                          sceneLocalPoint: new Vector3(13.5f, 1.25f, -2f),
                                          unitScale: 0.369573f,
                                          sceneId: "scn_2cold");

            var back = ShotLogEntry.FromJson(entry.ToJson());

            Assert.That(back.ColliderSet, Is.EqualTo("SplatMesh"));
            Assert.That(back.SceneId, Is.EqualTo("scn_2cold"));
            Assert.That(back.StoppedReason, Is.EqualTo("Impact"));
            Assert.That(back.UnitScale, Is.EqualTo(0.369573f).Within(1e-6f));
            Assert.That(back.SceneIsMetric, Is.True);
            Assert.That(back.ImpactPointSceneUnitsLocal, Is.EqualTo(new Vector3(13.5f, 1.25f, -2f)));
            Assert.That(back.ImpactPointWorldMetres.Value.X, Is.EqualTo(5f).Within(0.001f));
            Assert.That(back.Impact.IncidenceAngleDegrees, Is.EqualTo(entry.Impact.IncidenceAngleDegrees).Within(1e-4f));
            Assert.That(back.Parameters.Speed, Is.EqualTo(300f));
            Assert.That(back.Parameters.Dt, Is.EqualTo(0.0001f));
            Assert.That(back.Substeps, Is.EqualTo(result.Substeps));
        }

        [Test]
        public void ShotLogWithNoImpactRecordsTheReasonAndNoPoint()
        {
            var p = Shot(30, 20f, 0.001f);
            var result = TrajectoryIntegrator.Simulate(p, new NullRaycaster());
            var entry = ShotLogEntry.From(p, result, Recon.Colliders.ColliderSet.ArPlanes, null, 0f, null);

            var back = ShotLogEntry.FromJson(entry.ToJson());

            Assert.That(back.Impact, Is.Null);
            Assert.That(back.ImpactPointSceneUnitsLocal, Is.Null);
            Assert.That(back.ImpactPointWorldMetres, Is.Null);
            Assert.That(back.StoppedReason, Is.EqualTo("MaxFlightTime"));
            Assert.That(back.SceneIsMetric, Is.False);
        }

        [Test]
        public void ShotLogSerialisesAListOfShots()
        {
            var log = new ShotLog { SceneId = "scn_2cold" };
            var p = Shot(30, 20f, 0.001f);
            var result = TrajectoryIntegrator.Simulate(p, new NullRaycaster());
            log.Shots.Add(ShotLogEntry.From(p, result, Recon.Colliders.ColliderSet.Both, null, 0.3f, "scn_2cold"));

            var back = ShotLog.FromJson(log.ToJson());

            Assert.That(back.Shots, Has.Count.EqualTo(1));
            Assert.That(back.SceneId, Is.EqualTo("scn_2cold"));
            Assert.That(back.Shots[0].ColliderSet, Is.EqualTo("Both"));
        }
    }

    /// <summary>
    /// An infinite plane, intersected analytically, standing in for a wall or a floor. Used instead
    /// of a mock so the assertions are about the sweep the integrator performs and not about a
    /// recorded call. Hits at distance zero are ignored, so a shot launched from the plane leaves
    /// it cleanly rather than impacting at its own muzzle.
    /// </summary>
    internal sealed class PlaneRaycaster : IRaycaster
    {
        readonly Vector3 m_point;
        readonly Vector3 m_normal;
        readonly string m_tag;

        public PlaneRaycaster(Vector3 pointOnPlane, Vector3 normal, string colliderTag)
        {
            m_point = pointOnPlane;
            m_normal = Vector3.Normalize(normal);
            m_tag = colliderTag;
        }

        public int Calls { get; private set; }
        public int HitsReported { get; private set; }

        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask, out RayHit hit)
        {
            Calls++;
            hit = default;
            float denom = Vector3.Dot(direction, m_normal);
            if (Math.Abs(denom) < 1e-12f) return false;
            float s = Vector3.Dot(m_point - origin, m_normal) / denom;
            if (s <= 1e-9f || s > maxDistance) return false;
            HitsReported++;
            hit = new RayHit(origin + direction * s, m_normal, s, m_tag);
            return true;
        }
    }
}
