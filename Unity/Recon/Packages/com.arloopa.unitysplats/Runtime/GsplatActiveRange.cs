// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System;

namespace Gsplat
{
    /// <summary>
    /// A half-open range of source splat IDs: [Offset, Offset + Count).
    /// </summary>
    [Serializable]
    public struct GsplatActiveRange : IEquatable<GsplatActiveRange>
    {
        public uint Offset;
        public uint Count;

        public GsplatActiveRange(uint offset, uint count)
        {
            Offset = offset;
            Count = count;
        }

        public bool Equals(GsplatActiveRange other) => Offset == other.Offset && Count == other.Count;
        public override bool Equals(object obj) => obj is GsplatActiveRange other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Offset, Count);
        public override string ToString() => $"[{Offset}, {(ulong)Offset + Count})";
    }
}
