using System;
using System.Collections.Generic;
using Recon.Contract;

namespace Recon.Scenes
{
    /// <summary>
    /// The client-side half of docs/FRAMES.md: Unity asserts the frame and the scale instead of
    /// guessing, and refuses loudly. Every rule here maps to a row in the debugging-frames-and-scale
    /// skill's diagnosis table, so a rejection message is also the fix.
    /// Pure C#: tested under dotnet and the Unity Test Runner from the same test file.
    /// </summary>
    public static class SceneMetadataValidator
    {
        public const string RequiredHandedness = "left";
        public const string RequiredUpAxis = "y";

        public sealed class Result
        {
            public List<string> Errors { get; } = new List<string>();
            public List<string> Warnings { get; } = new List<string>();
            public bool IsValid => Errors.Count == 0;

            public override string ToString() =>
                IsValid ? (Warnings.Count == 0 ? "valid" : "valid, warnings: " + string.Join("; ", Warnings))
                        : "REJECTED: " + string.Join("; ", Errors);
        }

        public static Result Validate(SceneMetadata m)
        {
            var r = new Result();
            if (m == null)
            {
                r.Errors.Add("metadata is null");
                return r;
            }

            if (!string.Equals(m.Handedness, RequiredHandedness, StringComparison.Ordinal))
                r.Errors.Add($"handedness is '{m.Handedness ?? "missing"}', Unity needs 'left'. The GPU export skipped the one conversion (docs/FRAMES.md); do not flip on the client");

            if (!string.Equals(m.UpAxis, RequiredUpAxis, StringComparison.Ordinal))
                r.Errors.Add($"upAxis is '{m.UpAxis ?? "missing"}', Unity needs 'y'. Check what the exporter actually did, not what it should have done");

            if (double.IsNaN(m.UnitScale) || double.IsInfinity(m.UnitScale) || m.UnitScale <= 0)
                r.Errors.Add($"unitScale is {m.UnitScale}: scene is not metric. Reject it, do not eyeball a scale factor");
            else if (m.UnitScale < 0.01 || m.UnitScale > 100)
                r.Warnings.Add($"unitScale {m.UnitScale} is outside the plausible range 0.01..100 m per unit; re-measure the marker");

            if (string.Equals(m.ScaleMethod, "none", StringComparison.OrdinalIgnoreCase))
                r.Errors.Add("scaleMethod is 'none': no marker or LiDAR reference, so no accuracy claim is possible");
            else if (string.IsNullOrEmpty(m.ScaleMethod))
                r.Warnings.Add("scaleMethod is missing");

            if (string.IsNullOrEmpty(m.PlyFile))
                r.Errors.Add("plyFile is missing");

            if (m.SplatCount <= 0)
                r.Warnings.Add($"splatCount is {m.SplatCount}");

            if (string.IsNullOrEmpty(m.Sha256) || m.Sha256.Length != 64 || IsAllZeros(m.Sha256))
                r.Warnings.Add("sha256 is missing or the placeholder; custody check will fail");

            if (m.BoundingBox?.Min == null || m.BoundingBox.Max == null || m.BoundingBox.Min.Length != 3 || m.BoundingBox.Max.Length != 3)
                r.Warnings.Add("boundingBox is missing or not 3D");
            else
            {
                bool degenerate = true;
                for (int i = 0; i < 3; i++) if (m.BoundingBox.Max[i] > m.BoundingBox.Min[i]) { degenerate = false; break; }
                if (degenerate) r.Warnings.Add("boundingBox is degenerate (max <= min on every axis)");
            }

            return r;
        }

        /// <summary>Throws <see cref="SceneRejectedException"/> listing every error, for callers that want an exception.</summary>
        public static SceneMetadata EnsureValid(SceneMetadata m)
        {
            var r = Validate(m);
            if (!r.IsValid) throw new SceneRejectedException(r);
            return m;
        }

        static bool IsAllZeros(string s)
        {
            foreach (var c in s) if (c != '0') return false;
            return true;
        }
    }

    public sealed class SceneRejectedException : Exception
    {
        public SceneMetadataValidator.Result Result { get; }

        public SceneRejectedException(SceneMetadataValidator.Result result)
            : base("Scene refused: " + string.Join(" | ", result.Errors))
        {
            Result = result;
        }
    }
}
