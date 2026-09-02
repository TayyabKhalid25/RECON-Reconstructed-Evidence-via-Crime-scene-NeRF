using System;
using System.Numerics;
using NUnit.Framework;
using Recon.Spatter;

namespace Recon.Tests.Core
{
    /// <summary>
    /// Chapter 7 test cases for the blood spatter proxy (handbook Section 09, "Blood spatter, the
    /// actual formula", and AGENTS.md: "spatter ellipse ratio equal to sin of incidence angle").
    ///
    /// The relationship under test is the standard bloodstain pattern analysis one: stain width over
    /// length approximates sin(alpha), where alpha is the angle between the trajectory and the
    /// surface plane. This is a directional proxy and the tests are about that relationship only;
    /// no claim is made about rheology.
    /// </summary>
    public class SpatterMathTests
    {
        /// <summary>A direction striking the y = 0 plane at alpha degrees from the surface.</summary>
        static Vector3 DirectionAtIncidence(double alphaDeg)
        {
            double a = alphaDeg * Math.PI / 180.0;
            return new Vector3((float)Math.Cos(a), (float)-Math.Sin(a), 0f);
        }

        static readonly Vector3 Up = new Vector3(0f, 1f, 0f);

        /// <summary>
        /// TC-SPA-01. minor/major = sin(alpha) at 15, 30, 45, 60 and 90 degrees, to 1e-4. This is
        /// the whole spatter model, so it is pinned at five angles rather than one.
        /// </summary>
        [Test]
        public void EllipseAxisRatioEqualsSineOfIncidenceAngle()
        {
            foreach (double alpha in new double[] { 15, 30, 45, 60, 90 })
            {
                float ratio = SpatterMath.EllipseAxisRatio(DirectionAtIncidence(alpha), Up);
                Assert.That(ratio, Is.EqualTo(Math.Sin(alpha * Math.PI / 180.0)).Within(1e-4),
                    "ratio at alpha = " + alpha + " degrees");
            }
        }

        /// <summary>TC-SPA-01b. The inverse the report quotes: alpha = arcsin(width / length).</summary>
        [Test]
        public void IncidenceAngleIsTheArcsineOfTheAxisRatio()
        {
            foreach (double alpha in new double[] { 15, 30, 45, 60, 90 })
            {
                var dir = DirectionAtIncidence(alpha);
                float ratio = SpatterMath.EllipseAxisRatio(dir, Up);
                Assert.That(SpatterMath.IncidenceAngleDegrees(dir, Up), Is.EqualTo(alpha).Within(1e-3));
                Assert.That(Math.Asin(ratio) * 180.0 / Math.PI, Is.EqualTo(alpha).Within(1e-3));
            }
        }

        /// <summary>
        /// TC-SPA-02. A perpendicular impact is a circle: ratio 1, angle 90 degrees. "Ellipses all
        /// circles" is the failure the skill warns about, so the opposite end is asserted too.
        /// </summary>
        [Test]
        public void PerpendicularImpactGivesRatioOneAndNinetyDegrees()
        {
            Assert.That(SpatterMath.EllipseAxisRatio(new Vector3(0f, -1f, 0f), Up), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(SpatterMath.IncidenceAngleDegrees(new Vector3(0f, -1f, 0f), Up), Is.EqualTo(90f).Within(1e-3f));
        }

        [Test]
        public void GrazingImpactAlongTheSurfaceGivesRatioNearZero()
        {
            Assert.That(SpatterMath.EllipseAxisRatio(new Vector3(1f, 0f, 0f), Up), Is.EqualTo(0f).Within(1e-6f));
        }

        /// <summary>Inputs need not be unit length, and a normal facing the other way is the same stain.</summary>
        [Test]
        public void RatioIsIndependentOfInputLengthAndNormalSign()
        {
            var dir = DirectionAtIncidence(30) * 300f;
            Assert.That(SpatterMath.EllipseAxisRatio(dir, Up * 4f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(SpatterMath.EllipseAxisRatio(dir, -Up), Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void DegenerateInputsGiveZeroRatioRatherThanNaN()
        {
            Assert.That(SpatterMath.EllipseAxisRatio(Vector3.Zero, Up), Is.EqualTo(0f));
            Assert.That(SpatterMath.EllipseAxisRatio(DirectionAtIncidence(30), Vector3.Zero), Is.EqualTo(0f));
        }

        /// <summary>
        /// TC-SPA-03. The major axis lies IN the surface (no component along the normal) and points
        /// away from the source, which is what makes the stain directional evidence rather than a
        /// blob. A component along the normal would push the decal through the wall.
        /// </summary>
        [Test]
        public void MajorAxisLiesInTheSurfaceAndPointsAwayFromTheSource()
        {
            foreach (double alpha in new double[] { 15, 30, 45, 60, 89 })
            {
                var dir = DirectionAtIncidence(alpha);
                var major = SpatterMath.MajorAxisDirection(dir, Up);

                Assert.That(major.Length(), Is.EqualTo(1f).Within(1e-5f), "major axis must be unit length");
                Assert.That(Vector3.Dot(major, Up), Is.EqualTo(0f).Within(1e-6f), "no component along the normal");
                Assert.That(Vector3.Dot(major, dir), Is.GreaterThan(0f), "elongation points away from the source");
                Assert.That(major.X, Is.EqualTo(1f).Within(1e-5f));
            }
        }

        /// <summary>
        /// A perpendicular impact has no in-plane travel direction, so the major axis is arbitrary.
        /// It must still be a unit vector in the surface: the decal is built from it either way.
        /// </summary>
        [Test]
        public void MajorAxisOfAPerpendicularImpactIsStillAUnitVectorInTheSurface()
        {
            foreach (var n in new[] { Up, new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, -1f) })
            {
                var major = SpatterMath.MajorAxisDirection(-n, n);
                Assert.That(major.Length(), Is.EqualTo(1f).Within(1e-5f));
                Assert.That(Vector3.Dot(major, n), Is.EqualTo(0f).Within(1e-6f));
            }
        }

        /// <summary>
        /// TC-SPA-04. Droplets are emitted in a cone around the incoming direction. Every direction
        /// must be inside the cone, unit length, and identical on a rerun with the same seed: a
        /// figure in the report has to be reproducible, and so does a bug.
        /// </summary>
        [Test]
        public void DropletDirectionsStayInsideTheConeAndAreUnitLength()
        {
            var axis = Vector3.Normalize(new Vector3(0.3f, -1f, 0.2f));
            const float halfAngle = 25f;

            var dirs = SpatterMath.DropletDirections(axis, halfAngle, 64, seed: 20260902);

            Assert.That(dirs, Has.Length.EqualTo(64));
            double cosLimit = Math.Cos(halfAngle * Math.PI / 180.0) - 1e-5;
            foreach (var d in dirs)
            {
                Assert.That(d.Length(), Is.EqualTo(1f).Within(1e-4f));
                Assert.That(Vector3.Dot(d, axis), Is.GreaterThanOrEqualTo(cosLimit));
            }
        }

        [Test]
        public void DropletDirectionsAreDeterministicForASeedAndDifferForAnother()
        {
            var axis = new Vector3(0f, 0f, 1f);
            var a = SpatterMath.DropletDirections(axis, 30f, 32, seed: 7);
            var b = SpatterMath.DropletDirections(axis, 30f, 32, seed: 7);
            var c = SpatterMath.DropletDirections(axis, 30f, 32, seed: 8);

            Assert.That(a, Is.EqualTo(b), "same seed must give the same cone");
            Assert.That(a, Is.Not.EqualTo(c), "a different seed must give a different cone");
        }

        [Test]
        public void ZeroConeAngleGivesTheAxisAndZeroCountGivesNothing()
        {
            var axis = Vector3.Normalize(new Vector3(1f, 1f, 0f));
            var straight = SpatterMath.DropletDirections(axis, 0f, 4, seed: 1);
            Assert.That(straight, Has.Length.EqualTo(4));
            foreach (var d in straight)
                Assert.That(Vector3.Dot(d, axis), Is.EqualTo(1f).Within(1e-5f));

            Assert.That(SpatterMath.DropletDirections(axis, 20f, 0, seed: 1), Is.Empty);
            Assert.Throws<ArgumentOutOfRangeException>(() => SpatterMath.DropletDirections(axis, 20f, -1, seed: 1));
            Assert.Throws<ArgumentException>(() => SpatterMath.DropletDirections(Vector3.Zero, 20f, 4, seed: 1));
        }

        /// <summary>
        /// A full hemisphere of spread must still only ever go forwards; a cone half angle over 90
        /// degrees would emit droplets backwards through the surface the impact just hit.
        /// </summary>
        [Test]
        public void ConeHalfAngleIsClampedToAHemisphere()
        {
            var axis = new Vector3(0f, -1f, 0f);
            var dirs = SpatterMath.DropletDirections(axis, 200f, 128, seed: 3);
            foreach (var d in dirs)
                Assert.That(Vector3.Dot(d, axis), Is.GreaterThanOrEqualTo(-1e-5f));
        }

        /// <summary>
        /// The decal scale the emitter applies, kept next to the maths so the report can quote one
        /// number: localScale = (baseSize * sin alpha, baseSize, 1), i.e. minor over major = sin alpha.
        /// </summary>
        [Test]
        public void DecalScaleHasMinorOverMajorEqualToSineOfIncidence()
        {
            var dir = DirectionAtIncidence(30);
            var scale = SpatterMath.DecalScale(dir, Up, baseSize: 0.02f);

            Assert.That(scale.X, Is.EqualTo(0.01f).Within(1e-5f));
            Assert.That(scale.Y, Is.EqualTo(0.02f).Within(1e-5f));
            Assert.That(scale.Z, Is.EqualTo(1f));
            Assert.That(scale.X / scale.Y, Is.EqualTo(0.5f).Within(1e-4f));
        }

        /// <summary>A stain must never be scaled to zero width: a graze is thin, not invisible.</summary>
        [Test]
        public void DecalScaleKeepsAMinimumWidthForAGrazingImpact()
        {
            var scale = SpatterMath.DecalScale(new Vector3(1f, 0f, 0f), Up, baseSize: 0.02f);
            Assert.That(scale.X, Is.GreaterThan(0f));
            Assert.That(scale.X, Is.LessThan(0.02f * 0.06f));
        }
    }
}
