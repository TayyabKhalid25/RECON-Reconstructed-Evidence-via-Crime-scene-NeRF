// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using UnityEngine;

namespace Gsplat
{
    /// <summary>
    /// Canonical, renderer-independent Gaussian splat data decoded into Unity coordinates.
    /// Rotations use WXYZ component order. Colors contain raw degree-zero SH coefficients
    /// in RGB and activated opacity in A. SH coefficients are stored coefficient-major for
    /// each splat: <c>SHs[splat * coefficientCount + coefficient]</c>.
    /// </summary>
    public sealed class GsplatDecodedData
    {
        public int Count;
        public byte SHBands;
        public bool Antialiased;
        public Vector3[] Positions;
        public Vector3[] Scales;
        public Vector4[] Rotations;
        public Vector4[] Colors;
        public Vector3[] SHs;
        public Bounds Bounds;

        /// <summary>
        /// Allocates all canonical arrays for <paramref name="count"/> splats.
        /// </summary>
        public GsplatDecodedData(int count, byte shBands, bool antialiased = false)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Splat count cannot be negative.");
            if (shBands > 4)
                throw new ArgumentOutOfRangeException(nameof(shBands), "SH band count must be between 0 and 4.");

            Count = count;
            SHBands = shBands;
            Antialiased = antialiased;
            Positions = new Vector3[count];
            Scales = new Vector3[count];
            Rotations = new Vector4[count];
            Colors = new Vector4[count];

            int shCount = checked(count * GsplatUtils.SHBandsToCoefficientCount(shBands));
            SHs = shCount == 0 ? Array.Empty<Vector3>() : new Vector3[shCount];
            Bounds = new Bounds(Vector3.zero, Vector3.zero);
        }

        /// <summary>
        /// Verifies array shape and canonical value invariants before data is handed to an asset.
        /// </summary>
        public void Validate()
        {
            if (Count < 0)
                throw new InvalidDataException("Decoded splat count cannot be negative.");
            if (SHBands > 4)
                throw new InvalidDataException("Decoded SH band count must be between 0 and 4.");

            ValidateLength(Positions, Count, nameof(Positions));
            ValidateLength(Scales, Count, nameof(Scales));
            ValidateLength(Rotations, Count, nameof(Rotations));
            ValidateLength(Colors, Count, nameof(Colors));

            int expectedShCount;
            try
            {
                expectedShCount = checked(Count * GsplatUtils.SHBandsToCoefficientCount(SHBands));
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException("Decoded SH array length exceeds the supported range.", exception);
            }
            ValidateLength(SHs, expectedShCount, nameof(SHs));

            for (int i = 0; i < Count; ++i)
            {
                if (!IsFinite(Positions[i]))
                    throw new InvalidDataException($"Decoded position {i} contains a non-finite value.");
                if (!IsFinite(Scales[i]) || Scales[i].x < 0f || Scales[i].y < 0f || Scales[i].z < 0f)
                    throw new InvalidDataException($"Decoded linear scale {i} must be finite and non-negative.");
                if (!IsFinite(Rotations[i]) || Rotations[i].sqrMagnitude <= 1e-20f)
                    throw new InvalidDataException($"Decoded WXYZ rotation {i} is invalid.");
                if (!IsFinite(Colors[i]) || Colors[i].w < 0f || Colors[i].w > 1f)
                    throw new InvalidDataException(
                        $"Decoded DC/opacity value {i} must be finite with activated alpha in [0, 1].");
            }

            for (int i = 0; i < SHs.Length; ++i)
            {
                if (!IsFinite(SHs[i]))
                    throw new InvalidDataException($"Decoded SH coefficient {i} contains a non-finite value.");
            }

            if (!IsFinite(Bounds.center) || !IsFinite(Bounds.extents) ||
                Bounds.extents.x < 0f || Bounds.extents.y < 0f || Bounds.extents.z < 0f)
                throw new InvalidDataException("Decoded bounds are invalid.");
        }

        static void ValidateLength<T>(T[] values, int expected, string name)
        {
            if (values == null)
                throw new InvalidDataException($"Decoded {name} array is null.");
            if (values.Length != expected)
                throw new InvalidDataException(
                    $"Decoded {name} array has length {values.Length}; expected {expected}.");
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(Vector4 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
