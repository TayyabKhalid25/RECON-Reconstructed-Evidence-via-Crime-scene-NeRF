using System;
using System.Numerics;
using NUnit.Framework;
using Recon.Alignment;
using Recon.Contract;

namespace Recon.Tests.Core
{
    /// <summary>
    /// The three things the server enforces on an anchor transform, enforced again on the client
    /// before the twin is placed: unit quaternion (tolerance 1e-3), positive finite scale, finite
    /// coordinates. Re-checking is not paranoia — the anchor also arrives from a provider SDK on
    /// the resolve path, which never went through the server's Zod schema.
    /// </summary>
    public class AnchorTransformTests
    {
        static AnchorRecord Record(Quat rotation, double scale = 1.0, Vec3 position = null)
        {
            return new AnchorRecord
            {
                Id = "cmfanchor0001",
                AnchorId = "ua-1",
                Provider = "arcore-cloud-anchors",
                Position = position ?? new Vec3 { X = 0, Y = 0, Z = 0 },
                Rotation = rotation,
                Scale = scale,
                Frame = new AnchorFrame { Handedness = "left", UpAxis = "y", Space = "anchor->scene" },
            };
        }

        static Quat Identity => new Quat { X = 0, Y = 0, Z = 0, W = 1 };

        // ---- IsUnitQuaternion, mirroring web/src/lib/anchor.ts ---------------------------

        [Test]
        public void IsUnitQuaternion_AcceptsIdentity()
        {
            Assert.That(AnchorTransform.IsUnitQuaternion(new Quaternion(0, 0, 0, 1)), Is.True);
        }

        [Test]
        public void IsUnitQuaternion_AcceptsHonestFloatDrift()
        {
            // A phone's pose accumulates error; demanding exactly 1 would reject real input.
            // 1e-3 is the server's QUAT_TOLERANCE, so client and server agree on what is valid.
            var slightlyLong = new Quaternion(0, 0, 0, 1.0005f);
            Assert.That(AnchorTransform.IsUnitQuaternion(slightlyLong), Is.True);
        }

        [Test]
        public void IsUnitQuaternion_RejectsAllZero()
        {
            // The classic "rotation field never got filled in". Length 0, and a normalise would
            // produce NaN rather than an identity rotation.
            Assert.That(AnchorTransform.IsUnitQuaternion(new Quaternion(0, 0, 0, 0)), Is.False);
        }

        [Test]
        public void IsUnitQuaternion_RejectsUnnormalised()
        {
            // Euler angles in degrees dropped into the quaternion fields land here.
            Assert.That(AnchorTransform.IsUnitQuaternion(new Quaternion(0, 90, 0, 1)), Is.False);
            Assert.That(AnchorTransform.IsUnitQuaternion(new Quaternion(0, 0, 0, 1.01f)), Is.False);
        }

        [Test]
        public void IsUnitQuaternion_RejectsNonFinite()
        {
            Assert.That(AnchorTransform.IsUnitQuaternion(new Quaternion(float.NaN, 0, 0, 1)), Is.False);
            Assert.That(AnchorTransform.IsUnitQuaternion(new Quaternion(float.PositiveInfinity, 0, 0, 1)), Is.False);
        }

        [Test]
        public void IsUnitQuaternion_ToleranceIsTheServersDefault()
        {
            Assert.That(AnchorTransform.QuaternionTolerance, Is.EqualTo(1e-3).Within(1e-12));
        }

        // ---- ToPose ---------------------------------------------------------------------

        [Test]
        public void ToPose_KeepsPositionAsGiven_NoAxisFlip()
        {
            // docs/FRAMES.md: the .ply is already left-handed Y-up, so anchor space is too.
            // Two conversions cancel out and cost a day to find; there is no flip here to find.
            var pose = AnchorTransform.ToPose(Record(Identity, 1.0, new Vec3 { X = 1.25, Y = -0.5, Z = 3 }));

            Assert.That(pose.Position.X, Is.EqualTo(1.25f).Within(1e-6f));
            Assert.That(pose.Position.Y, Is.EqualTo(-0.5f).Within(1e-6f));
            Assert.That(pose.Position.Z, Is.EqualTo(3f).Within(1e-6f));
        }

        [Test]
        public void ToPose_NormalisesTheQuaternion()
        {
            var pose = AnchorTransform.ToPose(Record(new Quat { X = 0, Y = 0, Z = 0, W = 1.0005 }));

            Assert.That(pose.Rotation.Length(), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(pose.Rotation.W, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void ToPose_PreservesAQuarterTurnAboutY()
        {
            var pose = AnchorTransform.ToPose(Record(new Quat { X = 0, Y = 0.70710678, Z = 0, W = 0.70710678 }));

            // Rotating +X by 90 degrees about +Y in a left-handed frame is a real rotation either
            // way; what this asserts is that the components are not reordered or negated.
            Assert.That(pose.Rotation.Y, Is.EqualTo(0.70710678f).Within(1e-6f));
            Assert.That(pose.Rotation.W, Is.EqualTo(0.70710678f).Within(1e-6f));
            Assert.That(pose.Rotation.X, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(pose.Rotation.Z, Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void ToPose_ReturnsTheAnchorFitScale_WhichIsNotUnitScale()
        {
            // The double-scale bug (docs/ANCHORING.md): anchor `scale` is the anchor-to-scene fit
            // and is normally 1. Scene.unitScale (0.312834 for scene 2 cold) converts scene units
            // to metres and is applied exactly once, by SceneRoot.ApplyUnitScale. If this ever
            // returned unitScale the twin would come out at ~31 percent size and look "nearly
            // right", which is the expensive kind of wrong.
            var pose = AnchorTransform.ToPose(Record(Identity, 1.0));

            Assert.That(pose.Scale, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(pose.Scale, Is.Not.EqualTo(0.312834f));
        }

        [Test]
        public void ToPose_AcceptsANonIdentityFitScale_FromAReHostAwayFromTheOrigin()
        {
            var pose = AnchorTransform.ToPose(Record(Identity, 1.05));
            Assert.That(pose.Scale, Is.EqualTo(1.05f).Within(1e-6f));
        }

        [Test]
        public void ToPose_RefusesANonUnitQuaternion_SayingSo()
        {
            var e = Assert.Throws<AnchorTransformException>(
                () => AnchorTransform.ToPose(Record(new Quat { X = 0, Y = 0, Z = 0, W = 0 })));

            Assert.That(e.Message, Does.Contain("unit quaternion"));
            Assert.That(e.Message, Does.Contain("ua-1"));
        }

        [Test]
        public void ToPose_RefusesZeroScale_AnInvisibleTwin()
        {
            var e = Assert.Throws<AnchorTransformException>(() => AnchorTransform.ToPose(Record(Identity, 0)));
            Assert.That(e.Message, Does.Contain("scale"));
        }

        [Test]
        public void ToPose_RefusesNegativeScale_AMirroredTwin()
        {
            var e = Assert.Throws<AnchorTransformException>(() => AnchorTransform.ToPose(Record(Identity, -1)));
            Assert.That(e.Message, Does.Contain("scale"));
        }

        [Test]
        public void ToPose_RefusesNonFiniteScale()
        {
            Assert.Throws<AnchorTransformException>(() => AnchorTransform.ToPose(Record(Identity, double.NaN)));
            Assert.Throws<AnchorTransformException>(() => AnchorTransform.ToPose(Record(Identity, double.PositiveInfinity)));
        }

        [Test]
        public void ToPose_RefusesNonFinitePosition()
        {
            var e = Assert.Throws<AnchorTransformException>(
                () => AnchorTransform.ToPose(Record(Identity, 1.0, new Vec3 { X = double.NaN, Y = 0, Z = 0 })));
            Assert.That(e.Message, Does.Contain("position"));
        }

        [Test]
        public void ToPose_RefusesAMissingPositionOrRotation()
        {
            var noRotation = Record(null);
            Assert.Throws<AnchorTransformException>(() => AnchorTransform.ToPose(noRotation));

            var noPosition = Record(Identity);
            noPosition.Position = null;
            Assert.Throws<AnchorTransformException>(() => AnchorTransform.ToPose(noPosition));
        }

        [Test]
        public void ToPose_RefusesNull()
        {
            Assert.Throws<ArgumentNullException>(() => AnchorTransform.ToPose(null));
        }

        // ---- outbound validation --------------------------------------------------------

        [Test]
        public void EnsureValidForPost_RejectsWhatTheServerWouldReject()
        {
            var bad = new AnchorInput
            {
                AnchorId = "ua-1",
                Position = new Vec3 { X = 0, Y = 0, Z = 0 },
                Rotation = new Quat { X = 0, Y = 0, Z = 0, W = 0 },
            };

            // Failing here costs a log line; failing at the server costs a round trip and a 400
            // that reads like a server problem.
            Assert.Throws<AnchorTransformException>(() => AnchorTransform.EnsureValidForPost(bad));
        }

        [Test]
        public void EnsureValidForPost_RejectsAnEmptyAnchorId()
        {
            var bad = new AnchorInput
            {
                AnchorId = "",
                Position = new Vec3 { X = 0, Y = 0, Z = 0 },
                Rotation = new Quat { X = 0, Y = 0, Z = 0, W = 1 },
            };

            Assert.Throws<AnchorTransformException>(() => AnchorTransform.EnsureValidForPost(bad));
        }

        [Test]
        public void EnsureValidForPost_AcceptsAValidHost()
        {
            var ok = new AnchorInput
            {
                AnchorId = "ua-1",
                Position = new Vec3 { X = 0, Y = 0, Z = 0 },
                Rotation = new Quat { X = 0, Y = 0, Z = 0, W = 1 },
                Scale = 1.0,
            };

            Assert.That(AnchorTransform.EnsureValidForPost(ok), Is.SameAs(ok));
        }
    }
}
