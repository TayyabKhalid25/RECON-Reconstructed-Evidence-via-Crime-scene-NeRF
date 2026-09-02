using System;
using System.Numerics;
using Recon.Contract;

namespace Recon.Alignment
{
    /// <summary>
    /// Where the digital twin sits, as pure maths. <see cref="System.Numerics"/> rather than
    /// UnityEngine so this file compiles under `dotnet test Unity/Recon.Core.Tests`; the Unity glue
    /// converts to UnityEngine.Pose at the boundary. Component order is identical in both
    /// (x, y, z, w), so the conversion is a copy and never a reorder.
    ///
    /// <b>Scale here is the anchor-to-scene fit and is normally 1.</b> It is not
    /// Scene.unitScale, which converts scene units to metres, comes from the printed marker, and is
    /// applied exactly once by SceneRoot.ApplyUnitScale. Applying one where the other belongs is
    /// the double-scale bug from docs/ANCHORING.md, and it produces a twin that looks nearly right.
    /// </summary>
    public static class AnchorTransform
    {
        /// <summary>
        /// How far a quaternion's length may drift from 1 before it is refused. The same value as
        /// QUAT_TOLERANCE in web/src/lib/anchor.ts: loose enough for honest float error off a
        /// phone's pose, tight enough to catch an all-zero quaternion, an unnormalised one, or
        /// Euler angles sent in the wrong fields. Client and server must agree, or an anchor the
        /// server stored would be one the client refuses to use.
        /// </summary>
        public const double QuaternionTolerance = 1e-3;

        public static bool IsUnitQuaternion(Quaternion q, double tolerance = QuaternionTolerance)
        {
            if (!IsFinite(q.X) || !IsFinite(q.Y) || !IsFinite(q.Z) || !IsFinite(q.W)) return false;
            double length = Math.Sqrt((double)q.X * q.X + (double)q.Y * q.Y + (double)q.Z * q.Z + (double)q.W * q.W);
            if (!IsFinite(length)) return false;
            return Math.Abs(length - 1.0) <= tolerance;
        }

        public static bool IsUnitQuaternion(Quat q, double tolerance = QuaternionTolerance)
        {
            if (q == null) return false;
            return IsUnitQuaternion(ToQuaternion(q), tolerance);
        }

        /// <summary>
        /// Turns a stored anchor into the pose that places <c>SceneRoot</c>, refusing anything the
        /// server would have refused. Expiry is deliberately not checked here — the caller decides
        /// whether a stale anchor is still worth using, and <see cref="AnchorRecord.Expired"/> is
        /// the flag to read.
        /// </summary>
        public static AnchorPose ToPose(AnchorRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            string who = string.IsNullOrEmpty(record.AnchorId) ? "(no anchorId)" : record.AnchorId;

            if (record.Position == null)
                throw new AnchorTransformException($"anchor {who} has no position; nothing can be placed from it.");
            if (record.Rotation == null)
                throw new AnchorTransformException($"anchor {who} has no rotation; nothing can be placed from it.");

            if (!IsFinite(record.Position.X) || !IsFinite(record.Position.Y) || !IsFinite(record.Position.Z))
                throw new AnchorTransformException(
                    $"anchor {who} position {record.Position} is not finite; the twin would land nowhere.");

            var rotation = ToQuaternion(record.Rotation);
            if (!IsUnitQuaternion(rotation))
                throw new AnchorTransformException(
                    $"anchor {who} rotation {record.Rotation} is not a unit quaternion " +
                    $"(length must be 1 +/- {QuaternionTolerance}); a non-normalised quaternion skews the twin " +
                    "rather than failing visibly.");

            if (!IsFinite(record.Scale) || record.Scale <= 0)
                throw new AnchorTransformException(
                    $"anchor {who} scale {record.Scale} must be finite and positive: zero is an invisible twin " +
                    "and negative is a mirrored one, and both read as broken tracking rather than bad data. " +
                    "Note this is the anchor-to-scene fit (normally 1), not Scene.unitScale.");

            return new AnchorPose(
                new Vector3((float)record.Position.X, (float)record.Position.Y, (float)record.Position.Z),
                Quaternion.Normalize(rotation),
                (float)record.Scale);
        }

        /// <summary>
        /// Checks an outbound host before it is POSTed. Failing here costs a log line; failing at
        /// the server costs a round trip and a 400 that reads like a server problem.
        /// </summary>
        public static AnchorInput EnsureValidForPost(AnchorInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            if (string.IsNullOrWhiteSpace(input.AnchorId))
                throw new AnchorTransformException("anchor input has no anchorId; the provider handle is what makes it resolvable.");
            if (input.Position == null || input.Rotation == null)
                throw new AnchorTransformException($"anchor input {input.AnchorId} needs both position and rotation.");
            if (!IsFinite(input.Position.X) || !IsFinite(input.Position.Y) || !IsFinite(input.Position.Z))
                throw new AnchorTransformException($"anchor input {input.AnchorId} position {input.Position} is not finite.");
            if (!IsUnitQuaternion(input.Rotation))
                throw new AnchorTransformException(
                    $"anchor input {input.AnchorId} rotation {input.Rotation} is not a unit quaternion " +
                    $"(length must be 1 +/- {QuaternionTolerance}); the server rejects this with BAD_REQUEST.");
            if (input.Scale.HasValue && (!IsFinite(input.Scale.Value) || input.Scale.Value <= 0))
                throw new AnchorTransformException(
                    $"anchor input {input.AnchorId} scale {input.Scale.Value} must be finite and positive.");

            return input;
        }

        /// <summary>Component-for-component copy; no reorder, no negation, no axis flip.</summary>
        public static Quaternion ToQuaternion(Quat q) =>
            new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);

        /// <summary>Component-for-component copy; no reorder, no negation, no axis flip.</summary>
        public static Vector3 ToVector3(Vec3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        public static Vec3 FromVector3(Vector3 v) => new Vec3 { X = v.X, Y = v.Y, Z = v.Z };

        public static Quat FromQuaternion(Quaternion q) => new Quat { X = q.X, Y = q.Y, Z = q.Z, W = q.W };

        static bool IsFinite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
        static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }

    /// <summary>
    /// A validated anchor-to-scene transform. <see cref="Scale"/> is the anchor fit (normally 1),
    /// never unitScale.
    /// </summary>
    public readonly struct AnchorPose
    {
        public AnchorPose(Vector3 position, Quaternion rotation, float scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public Vector3 Position { get; }

        /// <summary>Normalised.</summary>
        public Quaternion Rotation { get; }

        /// <summary>Anchor-to-scene fit, > 0. NOT Scene.unitScale.</summary>
        public float Scale { get; }

        public override string ToString() =>
            $"pos ({Position.X:F4}, {Position.Y:F4}, {Position.Z:F4}) " +
            $"rot ({Rotation.X:F6}, {Rotation.Y:F6}, {Rotation.Z:F6}, {Rotation.W:F6}) fit {Scale:F4}";
    }

    /// <summary>An anchor that cannot be used to place the twin, with the reason a human can act on.</summary>
    public sealed class AnchorTransformException : Exception
    {
        public AnchorTransformException(string message) : base("Anchor refused: " + message) { }
    }
}
